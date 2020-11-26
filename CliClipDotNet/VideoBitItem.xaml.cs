using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using LibVLCSharp.Shared;
using System.Collections.ObjectModel;

namespace CliClip
{
    /// <summary>
    /// Interaction logic for VideoBitItem.xaml
    /// </summary>
    public partial class VideoBitItem : UserControl
    {
        // List containing all VideoBitItem items
        ObservableCollection<VideoBitItem> bitList;
        // Bit start time in seconds
        public double startTime = 0.0;
        public string startTimeString
        {
            get
            {
                return new TimeSpan(0, 0, 0, 0, Convert.ToInt32(startTime * 1000.0)).ToString(@"hh\:mm\:ss\.fff");
            }
        }
        // Bit end time in seconds
        public double endTime = 0.0;
        public string endTimeString
        {
            get
            {
                return new TimeSpan(0, 0, 0, 0, Convert.ToInt32(endTime * 1000.0)).ToString(@"hh\:mm\:ss\.fff");
            }
        }
        // String displaying [startTime - endTime]
        public string timestampString
        {
            get
            {
                string startString = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(startTime * 1000.0)).ToString(@"hh\:mm\:ss\.fff");
                string endString = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(endTime * 1000.0)).ToString(@"hh\:mm\:ss\.fff");
                return $"[{startString} - {endString}]";
            }
        }
        public string mediaPath;
        //public Media media = null;
        public MediaTrack? audioTrack;
        public bool muted = false;
        public MediaTrack? subtitleTrack;
        public decimal rate = 1.0M;



        public VideoBitItem()
        {
            InitializeComponent();
        }

        public VideoBitItem(ObservableCollection<VideoBitItem> list, string path, double start, double end, decimal playrate, MediaTrack? audio, bool mute, MediaTrack? subtitles)
        {
            InitializeComponent();

            // Init members
            bitList = list;
            startTime = start;
            endTime = end;
            mediaPath = path;
            //media = source.Duplicate();
            rate = playrate;
            audioTrack = audio;
            muted = mute;
            subtitleTrack = subtitles;

            // Update UI
            UpdateUI();
        }

        ~VideoBitItem()
        {
            //if (media != null)
            //    media.Dispose();
        }

        protected void UpdateUI()
        {
            expander.Header = timestampString;
            playrateText.Text = rate.ToString();
            mutedText.Text = muted.ToString();
            if (audioTrack.HasValue)
                audioTrackText.Text = $"{audioTrack.Value.Id} [{audioTrack.Value.Language}] {audioTrack.Value.Description}";
            else
                audioTrackText.Text = "None";
            if (subtitleTrack.HasValue)
                subtitleTrackText.Text = $"{subtitleTrack.Value.Id} [{subtitleTrack.Value.Language}] {subtitleTrack.Value.Description}";
            else
                subtitleTrackText.Text = "None";
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            if (bitList != null)
                bitList.Remove(this);
        }
    }
}
