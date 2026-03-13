using System;
using System.Windows.Forms;
using Accord.Video.DirectShow;
using AgLibrary.Logging;

namespace AgOpenGPS
{
    public partial class FormWebCam : Form
    {
        private FilterInfoCollection _videoDevices;

        public FormWebCam()
        {
            InitializeComponent();
        }

        private void FormWebCam_Load(object sender, EventArgs e)
        {
            try
            {
                _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

                foreach (var videoDevice in _videoDevices)
                {
                    deviceComboBox.Items.Add(videoDevice.Name);
                }

                if (deviceComboBox.Items.Count > 0)
                {
                    deviceComboBox.SelectedItem = deviceComboBox.Items[0];
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter($"DirectShow video device enumeration failed: {ex}");
                MessageBox.Show(
                    "Failed to enumerate video devices. This may be due to:\n\n" +
                    "• Missing or corrupted video capture drivers\n" +
                    "• DirectShow filter issues\n\n" +
                    $"Error: {ex.Message}",
                    "Video Capture Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void UpdateButtons()
        {
            startButton.Enabled = deviceComboBox.SelectedItem != null;
            stopButton.Enabled = videoSourcePlayer.IsRunning;
        }

        private void deviceComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateButtons();
        }

        private void startButton_Click(object sender, EventArgs e)
        {
            try
            {
                var videoSource = new VideoCaptureDevice(_videoDevices[deviceComboBox.SelectedIndex].MonikerString);

                videoSourcePlayer.VideoSource = videoSource;
                videoSourcePlayer.Start();

                UpdateButtons();
            }
            catch (Exception ex)
            {
                Log.EventWriter($"Failed to start video capture: {ex}");
                MessageBox.Show(
                    $"Failed to start video device.\n\nError: {ex.Message}",
                    "Video Capture Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void stopButton_Click(object sender, EventArgs e)
        {
            try
            {
                videoSourcePlayer.SignalToStop();
                videoSourcePlayer.WaitForStop();

                UpdateButtons();
            }
            catch (Exception ex)
            {
                Log.EventWriter($"Failed to stop video capture: {ex}");
            }
        }
    }
}