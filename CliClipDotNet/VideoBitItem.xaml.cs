using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using LibVLCSharp.Shared;

namespace CliClip
{
    /// <summary>
    /// Interaction logic for VideoBitItem.xaml
    /// </summary>
    public partial class VideoBitItem : UserControl
    {
        // Bit start time in seconds
        protected double startTime = 0.0;
        // Bit end time in seconds
        protected double endTime = 0.0;
        // String displaying [startTime - endTime]
        public string timestampString
        {
            get
            {
                string startString = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(startTime * 1000.0)).ToString();
                string endString = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(endTime * 1000.0)).ToString();
                return $"[{startString} - {endString}]";
            }
        }
        Media media = null;
        MediaTrack? audioTrack;
        bool muted = false;
        MediaTrack? subtitleTrack;
        decimal rate = 1.0M;

        public VideoBitItem()
        {
            InitializeComponent();
        }

        public VideoBitItem(Media source, double start, double end, decimal playrate, MediaTrack? audio, bool mute, MediaTrack? subtitles)
        {
            InitializeComponent();

            // Init members
            startTime = start;
            endTime = end;
            media = source.Duplicate();
            rate = playrate;
            audioTrack = audio;
            muted = mute;
            subtitleTrack = subtitles;

            // Update UI
            UpdateUI();
        }

        ~VideoBitItem()
        {
            if (media != null)
                media.Dispose();
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
    }
}
