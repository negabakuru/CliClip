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
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using LibVLCSharp.Shared;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CliClipDotNet
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        MediaPlayer mediaPlayer;
        public Media baseMedia { get; protected set; }
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

        public bool LoadMediaFromPath(string mediaPath)
        {
            if (File.Exists(mediaPath))
            {
                Media newMedia = new Media(App.VLC, mediaPath, FromType.FromPath);

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
            if (playingMedia != null)
                playingMedia.Dispose();
            playingMedia = newVideo.Duplicate();
            await playingMedia.Parse();

            TimeSpan videoDuration = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(playingMedia.Duration));
            durationTextBlock.Text = videoDuration.ToString(@"hh\:mm\:ss\.fff");

            playingMedia.AddOption("input-repeat=65535");

            // reset range slider
            videoBitRangeSlider.Maximum = newVideo.Duration * 0.001; // set slider to duration in seconds
            videoBitRangeSlider.HigherValue = videoBitRangeSlider.Maximum;
            videoBitRangeSlider.LowerValue = 0.0;

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
            videoBitRangeSlider.TickFrequency =  roughTickInterval - (roughTickInterval % greatestDenominator);

            playingMedia.AddOption("start-time=0.0");
            playingMedia.AddOption($"stop-time={videoBitRangeSlider.HigherValue}");
            mediaPlayer.Play(playingMedia);

            noVideoLabel.Visibility = Visibility.Hidden;
        }

        private void loadVideoButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.CheckFileExists = true;
            openFileDialog.CheckPathExists = true;
            openFileDialog.Multiselect = false;
            openFileDialog.Filter = "Video files|*.mp4;*.webm;*.mkv;*.avi";

            if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                filePathTextBox.Text = openFileDialog.FileName;

                LoadMediaFromPath(openFileDialog.FileName);
                LoadNewVideo(baseMedia);
            }
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            App.Current.Shutdown();
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
    }
}
