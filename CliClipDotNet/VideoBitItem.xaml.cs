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
        public MediaBit bit = new MediaBit();

        public string startTimeString
        {
            get
            {
                return new TimeSpan(0, 0, 0, 0, Convert.ToInt32(bit.startTime * 1000.0)).ToString(@"hh\:mm\:ss\.fff");
            }
        }

        public string endTimeString
        {
            get
            {
                return new TimeSpan(0, 0, 0, 0, Convert.ToInt32(bit.endTime * 1000.0)).ToString(@"hh\:mm\:ss\.fff");
            }
        }
        // String displaying [startTime - endTime]
        public string timestampString
        {
            get
            {
                string startString = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(bit.startTime * 1000.0)).ToString(@"hh\:mm\:ss\.fff");
                string endString = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(bit.endTime * 1000.0)).ToString(@"hh\:mm\:ss\.fff");
                return $"[{startString} - {endString}]";
            }
        }


        public VideoBitItem()
        {
            InitializeComponent();
        }

        public VideoBitItem(ObservableCollection<VideoBitItem> list, string path, double start, double end, decimal playrate, MediaTrack? audio, bool mute, MediaTrack? subtitles)
        {
            InitializeComponent();

            // Init members
            bitList = list;
            bit.startTime = start;
            bit.endTime = end;
            bit.mediaPath = path;
            bit.rate = playrate;
            bit.audioTrack = audio;
            bit.muted = mute;
            bit.subtitleTrack = subtitles;

            // Update UI
            UpdateUI();
        }

        protected void UpdateUI()
        {
            expander.Header = timestampString;
            playrateText.Text = bit.rate.ToString();
            mutedText.Text = bit.muted.ToString();
            if (bit.audioTrack.HasValue)
                audioTrackText.Text = $"{bit.audioTrack.Value.Id} [{bit.audioTrack.Value.Language}] {bit.audioTrack.Value.Description}";
            else
                audioTrackText.Text = "None";
            if (bit.subtitleTrack.HasValue)
                subtitleTrackText.Text = $"{bit.subtitleTrack.Value.Id} [{bit.subtitleTrack.Value.Language}] {bit.subtitleTrack.Value.Description}";
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
