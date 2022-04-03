using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;

using Xabe.FFmpeg;

namespace CliClip
{
    /// <summary>
    /// Interaction logic for VideoBitItem.xaml
    /// </summary>
    public partial class VideoBitItem : UserControl
    {
        // List containing all VideoBitItem items
        readonly ObservableCollection<VideoBitItem> bitList;
        public MediaBit bit = new MediaBit();

        public string StartTimeString
        {
            get
            {
                return bit.startTime.ToString(@"hh\:mm\:ss\.fff");
            }
        }

        public string EndTimeString
        {
            get
            {
                return bit.endTime.ToString(@"hh\:mm\:ss\.fff");
            }
        }
        // String displaying [startTime - endTime]
        public string TimestampString
        {
            get
            {
                string startString = bit.startTime.ToString(@"hh\:mm\:ss\.fff");
                string endString = bit.endTime.ToString(@"hh\:mm\:ss\.fff");
                return $"[{startString} - {endString}]";
            }
        }


        public VideoBitItem()
        {
            InitializeComponent();
        }

        public VideoBitItem(ObservableCollection<VideoBitItem> list, string path, TimeSpan start, TimeSpan end, decimal playrate, IAudioStream audio, bool mute, ISubtitleStream subtitles)
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
            Expander.Header = TimestampString;
            playrateText.Text = bit.rate.ToString();
            mutedText.Text = bit.muted.ToString();
            if (bit.audioTrack != null)
                audioTrackText.Text = $"{bit.audioTrack.Index} [{bit.audioTrack.Language}]";
            else
                audioTrackText.Text = "None";
            if (bit.subtitleTrack != null)
                subtitleTrackText.Text = $"{bit.subtitleTrack.Index} [{bit.subtitleTrack.Language}] {bit.subtitleTrack.Title}";
            else
                subtitleTrackText.Text = "None";
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (bitList != null)
                bitList.Remove(this);
        }
    }
}
