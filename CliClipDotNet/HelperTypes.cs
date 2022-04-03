using System;
using System.Collections.Generic;

using Xabe.FFmpeg;

namespace CliClip
{
    public class MediaTrackComboBoxItem
    {
        public int Index { get; set; }
        public string DisplayString { get; set; }
        public bool IsNull { get; set; }
        public IStream Track { get; set; }
    }

    public class MediaBit
    {
        public string mediaPath;

        // Bit start time in seconds
        public TimeSpan startTime;
        // Bit end time in seconds
        public TimeSpan endTime;

        public IAudioStream audioTrack;
        public bool muted = false;

        public ISubtitleStream subtitleTrack;

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