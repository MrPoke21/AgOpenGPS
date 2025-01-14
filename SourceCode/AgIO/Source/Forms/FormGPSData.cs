//Please, if you use this give me some credit
//Copyright BrianTee, copy right out of it.

using System;
using System.Windows.Forms;

namespace AgIO
{
    public partial class FormGPSData : Form
    {
        private readonly FormLoop mf = null;

        public FormGPSData(Form callingForm)
        {
            mf = callingForm as FormLoop;
            InitializeComponent();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            lblLatitude.Text = mf.latitude.ToString("N8");
            lblLongitude.Text = mf.longitude.ToString("N8");

            ////other sat and GPS info
            lblFixQuality.Text = mf.FixQuality;
            lblSatsTracked.Text = mf.satellitesData.ToString();
            lblHDOP.Text = mf.hdopData.ToString("N1");
            lblSpeed.Text = mf.speedData.ToString("N1");

            lblRoll.Text = mf.rollData.ToString("N2");
            lblIMURoll.Text = mf.imuRollData.ToString("N2");
            lblIMUPitch.Text = mf.imuPitchData.ToString("N2");
            lblIMUYawRate.Text = mf.imuYawRateData.ToString("N2");
            lblIMUHeading.Text = mf.imuHeadingData.ToString("N2");

            lblAge.Text = mf.ageData.ToString("N1");

            lblGPSHeading.Text = mf.headingTrueData.ToString("N2");
            lblDualHeading.Text = mf.headingTrueDualData.ToString("N2");

            lblAltitude.Text = mf.altitudeData.ToString("N1");

            tboxVTG.Text = mf.vtgSentence;
            tboxGGA.Text = mf.ggaSentence;
            tboxPAOGI.Text = mf.paogiSentence;
            tboxAVR.Text = mf.avrSentence;
            tboxHDT.Text = mf.hdtSentence;
            tboxHPD.Text = mf.hpdSentence;
            tboxPANDA.Text = mf.pandaSentence;
            tboxKSXT.Text = mf.ksxtSentence;
        }

        private void FormGPSData_Load(object sender, EventArgs e)
        {
            tboxGGA.Text = "";
            tboxVTG.Text = "";
            tboxHDT.Text = "";
            tboxAVR.Text = "";
            tboxPAOGI.Text = "";
            tboxHPD.Text = "";
            tboxPANDA.Text = "";
            tboxKSXT.Text = "";
        }

        private void FormGPSData_FormClosing(object sender, FormClosingEventArgs e)
        {
            mf.isGPSSentencesOn = false;
        }
    }
}