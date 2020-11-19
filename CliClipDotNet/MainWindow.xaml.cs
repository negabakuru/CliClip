using System;
using System.Windows;

using LibVLCSharp.Shared;
using System.IO;
using System.Threading;

namespace CliClipDotNet
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // List of file extension the app is allowed to work with
        static readonly string[] allowedExtensions = { ".mp4", ".avi", ".mkv", ".webm"};

        MediaPlayer mediaPlayer;
        // Media descriptor used to keep as a clean descriptor of the current loaded file
        public Media baseMedia { get; protected set; }
        // Media descriptor used by the media player. This one should contain all options necessary for playback
        public Media playingMedia { get; protected set; }


        public MainWindow()
        {
            InitializeComponent();
        }

        private void videoView_Loaded(object sender, RoutedEventArgs e)
        {
            mediaPlayer = new MediaPlayer(App.VLC);
            mediaPlayer.Mute = true;

            videoView.MediaPlayer = mediaPlayer;
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            App.Current.Shutdown();
        }

        public bool LoadMediaFromPath(string mediaPath)
        {
            if (File.Exists(mediaPath))
            {
                Media newMedia = new Media(App.VLC, mediaPath, FromType.FromPath);

                // Replace base media with new one
                if (baseMedia != null)
                    baseMedia.Dispose();
                baseMedia = newMedia;

                return true;
            }
            else
                return false;
        }

        async public void LoadNewVideo(Media newVideo)
        {
            // Replace playing media descriptor with new one and parse media for track infos
            if (playingMedia != null)
                playingMedia.Dispose();
            playingMedia = newVideo.Duplicate();
            await playingMedia.Parse();

            TimeSpan videoDuration = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(playingMedia.Duration));
            durationTextBlock.Text = videoDuration.ToString(@"hh\:mm\:ss\.fff");

            // reset range slider
            videoBitRangeSlider.Maximum = newVideo.Duration * 0.001; // set slider to duration in seconds
            videoBitRangeSlider.HigherValue = videoBitRangeSlider.Maximum;
            videoBitRangeSlider.LowerValue = 0.0;

            // Compute framerate from video track data
            if (playingMedia.Tracks.Length > 0 && playingMedia.Tracks[0].TrackType == TrackType.Video)
            {
                double framerate = (double)playingMedia.Tracks[0].Data.Video.FrameRateNum / (double)playingMedia.Tracks[0].Data.Video.FrameRateDen;
                videoBitRangeSlider.Step = framerate;
                framerateTextBlock.Text = $"{framerate}";
            }

            // Compute readable tick frequency for the video duration
            double roughTickInterval = videoBitRangeSlider.Maximum * 0.05;
            double greatestDenominator = 0.1;
            while ((roughTickInterval / (greatestDenominator * 10.0)) >= 1.0)
                greatestDenominator *= 10.0;
            videoBitRangeSlider.TickFrequency = roughTickInterval - (roughTickInterval % greatestDenominator);

            // Reset playback start/end time. Set media playback to loop
            playingMedia.AddOption("input-repeat=65535");
            playingMedia.AddOption("start-time=0.0");
            playingMedia.AddOption($"stop-time={videoBitRangeSlider.HigherValue}");

            mediaPlayer.Play(playingMedia);

            noVideoLabel.Visibility = Visibility.Hidden;
        }

        private void loadVideoButton_Click(object sender, RoutedEventArgs e)
        {
            // Open dialog for user to select a video file
            System.Windows.Forms.OpenFileDialog openFileDialog = new System.Windows.Forms.OpenFileDialog();
            openFileDialog.CheckFileExists = true;
            openFileDialog.CheckPathExists = true;
            openFileDialog.Multiselect = false;
            System.Text.StringBuilder strBuild = new System.Text.StringBuilder("Video files|");
            foreach (string ext in MainWindow.allowedExtensions)
            {
                if (ext != MainWindow.allowedExtensions[0])
                    strBuild.Append(";");
                strBuild.Append("*");
                strBuild.Append(ext);
            }
            openFileDialog.Filter = strBuild.ToString();

            // Load the video returned
            if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                filePathTextBox.Text = openFileDialog.FileName;

                LoadMediaFromPath(openFileDialog.FileName);
                LoadNewVideo(baseMedia);
            }
        }

        private void videoBitRangeSlider_HigherValueChanged(object sender, RoutedEventArgs e)
        {
            if (playingMedia != null)
            {
                playingMedia.AddOption($"stop-time={videoBitRangeSlider.HigherValue}");
                mediaPlayer.Play(playingMedia);
            }
        }

        private void videoBitRangeSlider_LowerValueChanged(object sender, RoutedEventArgs e)
        {
            if (playingMedia != null)
            {
                playingMedia.AddOption($"start-time={videoBitRangeSlider.LowerValue}");
                mediaPlayer.Play(playingMedia);
            }
        }

        private void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                LoadMediaFromPath(files[0]);
                LoadNewVideo(baseMedia);
                e.Handled = true;
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            bool dropEnabled = false;
            // Block drag and drop for unauthorized file extentions
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                string fileExtention = Path.GetExtension(files[0]).ToLower();
                foreach (string ext in MainWindow.allowedExtensions)
                {
                    if (fileExtention == ext)
                        dropEnabled = true;
                }
            }

            if (!dropEnabled)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }
    }
}
