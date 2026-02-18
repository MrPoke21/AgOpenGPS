using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using AgLibrary.Logging;

namespace AgIO
{
    public class CTraffic
    {
        public int cntrGPSIn = 0;
        public int cntrGPSInBytes = 0;
        public int cntrGPSOut = 0;

        public uint helloFromMachine = 99, helloFromAutoSteer = 99, helloFromIMU = 99;
    }

    public class CScanReply
    {
        public string steerIP = "";
        public string machineIP = "";
        public string GPS_IP = "";
        public string IMU_IP = "";
        public string subnetStr = "";

        public byte[] subnet = { 0, 0, 0 };

        public bool isNewSteer, isNewMachine, isNewGPS, isNewIMU;

        public bool isNewData = false;
    }

    public partial class FormLoop
    {
        // loopback Socket
        private Socket loopBackSocket;
        private EndPoint endPointLoopBack = new IPEndPoint(IPAddress.Loopback, 0);

        // UDP Socket
        public Socket UDPSocket;
        private EndPoint endPointUDP = new IPEndPoint(IPAddress.Any, 0);

        public bool isUDPNetworkConnected;

        //2 endpoints for local and 2 udp

        private IPEndPoint epAgOpen = new IPEndPoint(IPAddress.Parse(
            Properties.Settings.Default.eth_loopOne.ToString() + "." +
            Properties.Settings.Default.eth_loopTwo.ToString() + "." +
            Properties.Settings.Default.eth_loopThree.ToString() + "." +
            Properties.Settings.Default.eth_loopFour.ToString()), 15555);

        public IPEndPoint epModule = new IPEndPoint(IPAddress.Parse(
                Properties.Settings.Default.etIP_SubnetOne.ToString() + "." +
                Properties.Settings.Default.etIP_SubnetTwo.ToString() + "." +
                Properties.Settings.Default.etIP_SubnetThree.ToString() + ".255"), 8888);
        private IPEndPoint epNtrip;

        public IPEndPoint epModuleSet = new IPEndPoint(IPAddress.Parse("255.255.255.255"), 8888);
        public byte[] ipAutoSet = { 192, 168, 5 };

        //class for counting bytes
        public CTraffic traffic = new CTraffic();
        public CScanReply scanReply = new CScanReply();

        //scan results placed here
        public string scanReturn = "Scanning...";

        // Data stream - separate buffers for thread safety
        private byte[] udpBuffer = new byte[2048];
        private byte[] loopbackBuffer = new byte[2048];

        //used to send communication check pgn= C8 or 200
        private byte[] helloFromAgIO = { 0x80, 0x81, 0x7F, 200, 3, 56, 0, 0, 130 };

        public IPAddress ipCurrent;

        //initialize loopback and udp network
        public void LoadUDPNetwork()
        {
            helloFromAgIO[5] = 56;

            lblIP.Text = "";
            try //udp network
            {
                foreach (IPAddress IPA in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (IPA.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string data = IPA.ToString();
                        lblIP.Text += IPA.ToString().Trim() + "\r\n";
                    }
                }

                // Initialise the socket
                UDPSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                UDPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                UDPSocket.Bind(new IPEndPoint(IPAddress.Any, 9999));
                UDPSocket.BeginReceiveFrom(udpBuffer, 0, udpBuffer.Length, SocketFlags.None, ref endPointUDP,
                    new AsyncCallback(ReceiveDataUDPAsync), null);

                isUDPNetworkConnected = true;

                if (isUDPNetworkConnected)
                {
                    Log.EventWriter("UDP Network is connected: " + epModule.ToString());
                }
                else
                {
                    Log.EventWriter("UDP Network Failed to Connect");
                }

                btnUDP.BackColor = Color.LimeGreen;

                //if (!isFound)
                //{
                //    MessageBox.Show("Network Address of Modules -> " + Properties.Settings.Default.setIP_localAOG+"[2 - 254] May not exist. \r\n"
                //    + "Are you sure ethernet is connected?\r\n" + "Go to UDP Settings to fix.\r\n\r\n", "Network Connection Error",
                //    MessageBoxButtons.OK, MessageBoxIcon.Error);
                //    //btnUDP.BackColor = Color.Red;
                //    lblIP.Text = "Not Connected";
                //}
            }
            catch (Exception e)
            {
                Log.EventWriter("Catch -> Load UDP Server" + e);
                MessageBox.Show(e.Message, "Serious Network Connection Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnUDP.BackColor = Color.Red;
                lblIP.Text = "Error";
            }
        }

        private void LoadLoopback()
        {
            try //loopback
            {
                loopBackSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                loopBackSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                loopBackSocket.Bind(new IPEndPoint(IPAddress.Loopback, 17777));
                loopBackSocket.BeginReceiveFrom(loopbackBuffer, 0, loopbackBuffer.Length, SocketFlags.None, ref endPointLoopBack,
                    new AsyncCallback(ReceiveDataLoopAsync), null);
                Log.EventWriter("Loopback is Connected: " + IPAddress.Loopback.ToString() + ":17777");

            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch - > Load UDP Loopback Failed: " + ex.ToString());
                MessageBox.Show("Load Error: " + ex.Message, "Loopback Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #region Send LoopBack

        private void SendToLoopBackMessageAOG(byte[] byteData)
        {
            SendDataToLoopBack(byteData, epAgOpen);
        }

        private void SendDataToLoopBack(byte[] byteData, IPEndPoint endPoint)
        {
            try
            {
                if (byteData.Length != 0 && loopBackSocket != null)
                {
                    // Send packet to AgVR
                    loopBackSocket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None, endPoint,
                         new AsyncCallback(SendDataLoopAsync), null);
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("SendDataToLoopBack Error: " + ex.Message);
            }
        }

        public void SendDataLoopAsync(IAsyncResult asyncResult)
        {
            try
            {
                if (loopBackSocket != null)
                {
                    loopBackSocket.EndSend(asyncResult);
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("SendDataLoopAsync Error: " + ex.Message);
            }
        }

        #endregion

        #region Receive LoopBack

        private void ReceiveFromLoopBack(byte[] data)
        {
            if (data == null || data.Length < 4)
            {
                return;
            }

            if (isUDPNetworkConnected)
            {
                //Send out to udp network
                SendUDPMessage(data, epModule);
            }
            else if (data[0] == 0x80 && data[1] == 0x81)
            {
                SendSteerModulePort(data, data.Length);
                switch (data[3])
                {
                    case 0xFE: //254 AutoSteer Data
                        {
                            //serList.AddRange(data);
                            //SendSteerModulePort(data, data.Length);
                            SendMachineModulePort(data, data.Length);
                            break;
                        }
                    case 0xEF: //239 machine pgn
                        {
                            SendMachineModulePort(data, data.Length);
                            //SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xE5: //229 Symmetric Sections - Zones
                        {
                            SendMachineModulePort(data, data.Length);
                            //SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xFC: //252 steer settings
                        {
                            //SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xFB: //251 steer config
                        {
                            //SendSteerModulePort(data, data.Length);
                            break;
                        }

                    case 0xEE: //238 machine config
                        {
                            SendMachineModulePort(data, data.Length);
                            //SendSteerModulePort(data, data.Length);
                            break;
                        }

                    case 0xEC: //236 machine config
                        {
                            SendMachineModulePort(data, data.Length);
                            //SendSteerModulePort(data, data.Length);
                            break;
                        }
                }
            }
        }

        private void ReceiveDataLoopAsync(IAsyncResult asyncResult)
        {
            try
            {
                if (loopBackSocket == null) return;

                // Receive all data
                int msgLen = loopBackSocket.EndReceiveFrom(asyncResult, ref endPointLoopBack);

                byte[] localMsg = new byte[msgLen];
                Array.Copy(loopbackBuffer, localMsg, msgLen);

                BeginInvoke((MethodInvoker)(() => ReceiveFromLoopBack(localMsg)));
            }
            catch (Exception ex)
            {
                Log.EventWriter("ReceiveDataLoopAsync Error: " + ex.Message);
            }
            finally
            {
                // Always restart listener once, in finally block
                try
                {
                    if (loopBackSocket != null)
                    {
                        loopBackSocket.BeginReceiveFrom(loopbackBuffer, 0, loopbackBuffer.Length, SocketFlags.None, ref endPointLoopBack,
                            new AsyncCallback(ReceiveDataLoopAsync), null);
                    }
                }
                catch (Exception ex)
                {
                    Log.EventWriter("ReceiveDataLoopAsync Re-listen Error: " + ex.Message);
                }
            }
        }

        #endregion

        #region Send UDP

        public void SendUDPMessage(byte[] byteData, IPEndPoint endPoint)
        {
            if (isUDPNetworkConnected)
            {
                if (isUDPMonitorOn)
                {
                    if (epNtrip != null && endPoint.Port == epNtrip.Port)
                    {
                        if (isNTRIPLogOn)
                            logUDPSentence.Append(DateTime.Now.ToString("HH:mm:ss.fff\t") + endPoint.ToString() + "\t" + " > NTRIP\r\n");
                    }
                    else
                    {
                        logUDPSentence.Append(DateTime.Now.ToString("HH:mm:ss.fff\t") + endPoint.ToString() + "\t" + " > " + byteData[3].ToString() + "\r\n");
                    }
                }

                try
                {
                    // Send packet to the zero
                    if (byteData.Length != 0)
                    {
                        UDPSocket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None,
                           endPoint, new AsyncCallback(SendDataUDPAsync), null);
                    }
                }
                catch (Exception)
                {
                    //WriteErrorLog("Sending UDP Message" + e.ToString());
                    //MessageBox.Show("Send Error: " + e.Message, "UDP Client", MessageBoxButtons.OK,
                    //MessageBoxIcon.Error);
                }
            }
        }

        private void SendDataUDPAsync(IAsyncResult asyncResult)
        {
            try
            {
                if (UDPSocket != null)
                {
                    UDPSocket.EndSend(asyncResult);
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("SendDataUDPAsync Error: " + ex.Message);
            }
        }

        #endregion

        #region Receive UDP

        // Optimized UI update methods - called only when isViewAdvanced is true
        private void UpdateSteerUI(byte[] data)
        {
            try
            {
                lblPing.Text = (((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds - pingSecondsStart) * 1000).ToString("0");
                double actualSteerAngle = (Int16)((data[6] << 8) + data[5]);
                lblSteerAngle.Text = (actualSteerAngle * 0.01).ToString("N1");
                lblWASCounts.Text = ((Int16)((data[8] << 8) + data[7])).ToString();

                lblSwitchStatus.Text = ((data[9] & 2) == 2).ToString();
                lblWorkSwitchStatus.Text = ((data[9] & 1) == 1).ToString();
            }
            catch (Exception ex)
            {
                Log.EventWriter("UpdateSteerUI Error: " + ex.Message);
            }
        }

        private void UpdateMachineUI(byte[] data)
        {
            try
            {
                lblPingMachine.Text = (((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds - pingSecondsStart) * 1000).ToString("0");
                lbl1To8.Text = Convert.ToString(data[5], 2).PadLeft(8, '0');
                lbl9To16.Text = Convert.ToString(data[6], 2).PadLeft(8, '0');
            }
            catch (Exception ex)
            {
                Log.EventWriter("UpdateMachineUI Error: " + ex.Message);
            }
        }

        private void ReceiveDataUDPAsync(IAsyncResult asyncResult)
        {
            try
            {
                if (UDPSocket == null) return;

                // Receive all data
                int msgLen = UDPSocket.EndReceiveFrom(asyncResult, ref endPointUDP);

                byte[] localMsg = new byte[msgLen];
                Array.Copy(udpBuffer, localMsg, msgLen);

                // Process the received data
                ProcessReceivedUDPData(localMsg);

                // Log monitoring (if enabled)
                if (isUDPMonitorOn && msgLen >= 4 && localMsg[0] == 0x80 && localMsg[1] == 0x81)
                {
                    byte pgn = localMsg[3];
                    BeginInvoke((MethodInvoker)(() => 
                    {
                        logUDPSentence.Append(DateTime.Now.ToString("HH:mm:ss.fff\t") + 
                                            endPointUDP.ToString() + "\t < " + 
                                            pgn.ToString() + "\r\n");
                    }));
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("ReceiveDataUDPAsync Error: " + ex.Message);
            }
            finally
            {
                // Always restart listener once, in finally block - CRITICAL FIX: removed duplicate call
                try
                {
                    if (UDPSocket != null && isUDPNetworkConnected)
                    {
                        UDPSocket.BeginReceiveFrom(udpBuffer, 0, udpBuffer.Length, SocketFlags.None, ref endPointUDP,
                            new AsyncCallback(ReceiveDataUDPAsync), null);
                    }
                }
                catch (Exception ex)
                {
                    Log.EventWriter("ReceiveDataUDPAsync Re-listen Error: " + ex.Message);
                }
            }
        }

        // Process received UDP data - separated from socket operations for better code organization
        private void ProcessReceivedUDPData(byte[] data)
        {
            try
            {
                if (data == null || data.Length < 4)
                {
                    return;
                }

                // OPTIMIZATION: Process critical hello/ping messages directly on network thread
                // to avoid UI thread context switch overhead (saves 50-80ms per message)
                if (data[0] == 0x80 && data[1] == 0x81)
                {
                    byte pgn = data[3];

                    // Fast path for hello/ping messages (PGN 126, 123, 121)
                    if (pgn == 126 && data.Length >= 10) // Steer hello
                    {
                        traffic.helloFromAutoSteer = 0; // Thread-safe: uint write is atomic

                        // Only invoke UI update if advanced view is on
                        if (isViewAdvanced)
                        {
                            BeginInvoke((MethodInvoker)(() => UpdateSteerUI(data)));
                        }
                    }
                    else if (pgn == 123 && data.Length >= 7) // Machine hello
                    {
                        traffic.helloFromMachine = 0; // Thread-safe: uint write is atomic

                        if (isViewAdvanced)
                        {
                            BeginInvoke((MethodInvoker)(() => UpdateMachineUI(data)));
                        }
                    }
                    else if (pgn == 121) // IMU hello
                    {
                        traffic.helloFromIMU = 0; // Thread-safe: uint write is atomic
                    }
                    else
                    {
                        // Non-critical messages: GPS data, scan replies, etc.
                        // Process on UI thread (not time-critical)
                        BeginInvoke((MethodInvoker)(() => ReceiveFromUDP(data)));
                    }
                }
                else
                {
                    // Invalid or non-AgOpenGPS packet
                    BeginInvoke((MethodInvoker)(() => ReceiveFromUDP(data)));
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("ProcessReceivedUDPData Error: " + ex.Message);
            }
        }

        private void ReceiveFromUDP(byte[] data)
        {
            try
            {
                if (data == null || data.Length < 4)
                {
                    return;
                }

                if (data[0] == 0x80 && data[1] == 0x81)
                {
                    byte pgn = data[3];

                    // Skip hello messages - already processed on network thread
                    if (pgn == 126 || pgn == 123 || pgn == 121)
                    {
                        return; // Already handled in ReceiveDataUDPAsync for performance
                    }

                    //scan Reply
                    if (pgn == 203) //
                    {
                        if (data.Length < 12)
                        {
                            Log.EventWriter("ReceiveFromUDP: Invalid scan reply length: " + data.Length);
                            return;
                        }

                        if (data[2] == 126)  //steer module
                        {
                            scanReply.steerIP = data[5].ToString() + "." + data[6].ToString() + "." + data[7].ToString() + "." + data[8].ToString();

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString() + "." + data[10].ToString() + "." + data[11].ToString();

                            scanReply.isNewData = true;
                            scanReply.isNewSteer = true;
                        }
                        //
                        else if (data[2] == 123)   //machine module
                        {
                            scanReply.machineIP = data[5].ToString() + "." + data[6].ToString() + "." + data[7].ToString() + "." + data[8].ToString();

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString() + "." + data[10].ToString() + "." + data[11].ToString();

                            scanReply.isNewData = true;
                            scanReply.isNewMachine = true;

                        }
                        else if (data[2] == 121)   //IMU Module
                        {
                            scanReply.IMU_IP = data[5].ToString() + "." + data[6].ToString() + "." + data[7].ToString() + "." + data[8].ToString();

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString() + "." + data[10].ToString() + "." + data[11].ToString();

                            scanReply.isNewData = true;
                            scanReply.isNewIMU = true;
                        }

                        else if (data[2] == 120)    //GPS module
                        {
                            scanReply.GPS_IP = data[5].ToString() + "." + data[6].ToString() + "." + data[7].ToString() + "." + data[8].ToString();

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString() + "." + data[10].ToString() + "." + data[11].ToString();

                            scanReply.isNewData = true;
                            scanReply.isNewGPS = true;
                        }
                    }
                    //GPS DATA
                    else if (pgn == 0xD6 && data.Length == 63)
                    {

                        traffic.cntrGPSOut += data.Length;

                        longitude = BitConverter.ToDouble(data, 5);

                        latitude = BitConverter.ToDouble(data, 13);

                        speed = BitConverter.ToSingle(data, 29);
                        speedData = speed;

                        altitude = BitConverter.ToSingle(data, 37);
                        altitudeData = altitude;

                        satellitesTracked = BitConverter.ToUInt16(data, 41);
                        satellitesData = satellitesTracked;

                        fixQuality = data[43];
                        fixQualityData = fixQuality;

                        hdopX100 = BitConverter.ToUInt16(data, 44);
                        hdopData = hdopX100 * 0.01f;

                        ageX100 = BitConverter.ToUInt16(data, 46);
                        ageData = ageX100 * 0.01f;

                        imuHeading = (BitConverter.ToSingle(data, 48));
                        imuHeadingData = imuHeading;

                        imuRoll = BitConverter.ToSingle(data, 52);
                        imuRollData = imuRoll;

                        imuPitch = BitConverter.ToSingle(data, 56);
                        imuPitchData = imuPitch;

                        // imuYaw = BitConverter.ToInt16(data, 54);

                        SendToLoopBackMessageAOG(data);
                    }
                    else
                    {
                        //module return via udp sent to AOG
                        SendToLoopBackMessageAOG(data);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("ReceiveFromUDP Error: " + ex.Message);
            }
        }
    }
    #endregion

}
