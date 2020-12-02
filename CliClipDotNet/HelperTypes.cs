using System.Collections.Generic;
using LibVLCSharp.Shared;

namespace CliClip
{
    public class MediaTrackComboBoxItem
    {
        public string displayString { get; set; }
        public bool isNull { get; set; }
        public MediaTrack track { get; set; }
    }

    public class MediaBit
    {
        public string mediaPath;

        // Bit start time in seconds
        public double startTime = 0.0;
        // Bit end time in seconds
        public double endTime = 0.0;

        public MediaTrack? audioTrack;
        public bool muted = false;

        public MediaTrack? subtitleTrack;

        public decimal rate = 1.0M;
    }

    public class BitsRenderItem
    {
        public List<MediaBit> bitList = new List<MediaBit>();
        public string playingMediaPath;
        public MediaTrackComboBoxItem selectedAudio = null;
        public MediaTrackComboBoxItem selectedSubtitles = null;
    }
}