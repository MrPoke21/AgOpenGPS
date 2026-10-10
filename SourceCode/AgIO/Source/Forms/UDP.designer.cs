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
        public string steerIP =   "";
        public string machineIP = "";
        public string GPS_IP =    "";
        public string IMU_IP =    "";
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

        // Tracks module IPs that have actively responded (hello PGNs), with last-seen timestamp.
        // Entries older than ModuleIpExpiryMs are treated as expired and no longer targeted directly.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> respondedModuleIPs =
            new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>();
        private const int ModuleIpExpiryMs = 5000;

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
                        string  data = IPA.ToString();
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
                switch (data[3])
                {
                    case 0xFE: //254 AutoSteer Data
                        {
                            //serList.AddRange(data);
                            SendSteerModulePort(data, data.Length);
                            SendMachineModulePort(data, data.Length);
                            break;
                        }
                    case 0xEF: //239 machine pgn
                        {
                            SendMachineModulePort(data, data.Length);
                            SendSteerModulePort(data, data.Length);
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
                            SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xFB: //251 steer config
                        {
                            SendSteerModulePort(data, data.Length);
                            break;                        }

                    case 0xEE: //238 machine config
                        {
                            SendMachineModulePort(data, data.Length);
                            SendSteerModulePort(data, data.Length);
                            break;                        }

                    case 0xEC: //236 machine config
                        {
                            SendMachineModulePort(data, data.Length);
                            SendSteerModulePort(data, data.Length);
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

        private void RecordRespondedModuleIP(string senderIp)
        {
            if (string.IsNullOrEmpty(senderIp)) return;
            respondedModuleIPs[senderIp] = DateTime.Now;
        }

        // Returns the list of module IPs that responded within the expiry window (5s).
        // Also prunes expired entries from the dictionary.
        private List<string> GetActiveModuleIPs()
        {
            var active = new List<string>();
            DateTime now = DateTime.Now;

            foreach (var kvp in respondedModuleIPs)
            {
                if ((now - kvp.Value).TotalMilliseconds <= ModuleIpExpiryMs)
                {
                    active.Add(kvp.Key);
                }
                else
                {
                    respondedModuleIPs.TryRemove(kvp.Key, out _);
                }
            }

            return active;
        }

        public void _sendUDPMessage(byte[] byteData, IPEndPoint endPoint)
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

        // Sends UDP data only to IPs that have actively responded (hello PGNs) within the last 5 seconds.
        // If no IP has responded yet (or all have expired), falls back to the subnet broadcast endpoint
        // so new/unknown modules can still be discovered.
        public void SendUDPMessage(byte[] byteData, IPEndPoint endPoint)
        {
            List<string> activeIPs = GetActiveModuleIPs();

            if (activeIPs.Count == 0)
            {
                _sendUDPMessage(byteData, epModule);
                return;
            }

            foreach (string ip in activeIPs)
            {
                if (IPAddress.TryParse(ip, out IPAddress addr))
                {
                    _sendUDPMessage(byteData, new IPEndPoint(addr, endPoint.Port));
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

        private void ReceiveDataUDPAsync(IAsyncResult asyncResult)
        {
            try
            {
                if (UDPSocket == null) return;

                // Receive all data
                int msgLen = UDPSocket.EndReceiveFrom(asyncResult, ref endPointUDP);

                byte[] localMsg = new byte[msgLen];
                Array.Copy(udpBuffer, localMsg, msgLen);

                // Capture sender IP now (before BeginReceiveFrom overwrites endPointUDP with the next packet's sender)
                string senderIp = (endPointUDP as IPEndPoint)?.Address.ToString();

                // Process the packet on the receive thread. UI updates are marshalled separately.
                ProcessReceivedUDPData(localMsg, senderIp);

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

        private void UpdateSteerModuleStatus(string ping, string steerAngle, string wasCounts, string switchStatus, string workSwitchStatus)
        {
            if (IsDisposed || !IsHandleCreated) return;

            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() => UpdateSteerModuleStatus(ping, steerAngle, wasCounts, switchStatus, workSwitchStatus)));
                return;
            }

            if (!isViewAdvanced) return;

            lblPing.Text = ping;
            lblSteerAngle.Text = steerAngle;
            lblWASCounts.Text = wasCounts;
            lblSwitchStatus.Text = switchStatus;
            lblWorkSwitchStatus.Text = workSwitchStatus;
        }

        private void UpdateMachineModuleStatus(string ping, string inputs1To8, string inputs9To16)
        {
            if (IsDisposed || !IsHandleCreated) return;

            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() => UpdateMachineModuleStatus(ping, inputs1To8, inputs9To16)));
                return;
            }

            if (!isViewAdvanced) return;

            lblPingMachine.Text = ping;
            lbl1To8.Text = inputs1To8;
            lbl9To16.Text = inputs9To16;
        }

        private void UpdateScanReply(byte module, string ip, byte[] subnet, string subnetString)
        {
            if (IsDisposed || !IsHandleCreated) return;

            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() => UpdateScanReply(module, ip, subnet, subnetString)));
                return;
            }

            scanReply.subnet[0] = subnet[0];
            scanReply.subnet[1] = subnet[1];
            scanReply.subnet[2] = subnet[2];
            scanReply.subnetStr = subnetString;
            scanReply.isNewData = true;

            if (module == 126)
            {
                scanReply.steerIP = ip;
                scanReply.isNewSteer = true;
            }
            else if (module == 123)
            {
                scanReply.machineIP = ip;
                scanReply.isNewMachine = true;
            }
            else if (module == 121)
            {
                scanReply.IMU_IP = ip;
                scanReply.isNewIMU = true;
            }
            else if (module == 120)
            {
                scanReply.GPS_IP = ip;
                scanReply.isNewGPS = true;
            }
        }

        // Process received UDP data - separated from socket operations for better code organization
        private void ProcessReceivedUDPData(byte[] data, string senderIp)
        {
            try
            {
                if (data[0] == 0x80 && data[1] == 0x81)
                {
                    RecordRespondedModuleIP(senderIp);
                    //module return via udp sent to AOG
                    SendToLoopBackMessageAOG(data);
                    //check for Scan and Hello
                    if (data[3] == 126 && data.Length == 11)
                    {
                        traffic.helloFromAutoSteer = 0;
                        double actualSteerAngle = (Int16)((data[6] << 8) + data[5]);
                        UpdateSteerModuleStatus(
                            (((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds - pingSecondsStart) * 1000).ToString("N0"),
                            (actualSteerAngle * 0.01).ToString("N1"),
                            ((Int16)((data[8] << 8) + data[7])).ToString(),
                            ((data[9] & 2) == 2).ToString(),
                            ((data[9] & 1) == 1).ToString());
                    }

                    else if (data[3] == 123 && data.Length == 11)
                    {
                        traffic.helloFromMachine = 0;

                        UpdateMachineModuleStatus(
                            (((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds - pingSecondsStart) * 1000).ToString("N0"),
                            Convert.ToString(data[5], 2).PadLeft(8, '0'),
                            Convert.ToString(data[6], 2).PadLeft(8, '0'));
                    }

                    else if (data[3] == 121 && data.Length == 11)
                    {
                        traffic.helloFromIMU = 0;
                    }
                    //scan Reply
                    else if (data[3] == 203) //
                    {
                        if (data[2] == 126 || data[2] == 123 || data[2] == 121 || data[2] == 120)
                        {
                            string ip = data[5].ToString() + "." + data[6].ToString() + "." + data[7].ToString() + "." + data[8].ToString();
                            byte[] subnet = { data[9], data[10], data[11] };
                            string subnetString = data[9].ToString() + "." + data[10].ToString() + "." + data[11].ToString();
                            UpdateScanReply(data[2], ip, subnet, subnetString);
                        }
                    }

                    if (isUDPMonitorOn)
                    {
                        logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t") + endPointUDP.ToString() + "\t" + " < " + data[3].ToString() + "\r\n");
                    }

                } // end of pgns

                else if (data[0] == 36 && (data[1] == 71 || data[1] == 80 || data[1] == 75))
                {
                    traffic.cntrGPSOut += data.Length;
                    rawBuffer += Encoding.ASCII.GetString(data);
                    ParseNMEA(ref rawBuffer);

                    if (isUDPMonitorOn && isGPSLogOn)
                    {
                        logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t") + System.Text.Encoding.ASCII.GetString(data));
                    }
                }
            }
            catch
            {
            }
        }

        #endregion
    }
}
