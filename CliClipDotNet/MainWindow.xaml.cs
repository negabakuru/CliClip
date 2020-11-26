using System;
using System.Windows;
using System.Collections.Generic;

using System.Linq;
using LibVLCSharp.Shared;
using System.IO;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace CliClip
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public class MediaTrackComboBoxItem
        {
            public string displayString { get; set; }
            public bool isNull { get; set; }
            public MediaTrack track { get; set; }
        }

        // List of file extension the app is allowed to work with
        static readonly string[] allowedExtensions = { ".mp4", ".avi", ".mkv", ".webm" };

        MediaPlayer bitMediaPlayer;
        public string mediaPath { get; protected set; }
        // Media descriptor used to keep as a clean descriptor of the current loaded file
        public Media baseMedia { get; protected set; }
        // Media descriptor used by the media player. This one should contain all options necessary for playback
        public Media playingMedia { get; protected set; }
        // List of audio tracks for the currently played media
        protected ObservableCollection<MediaTrackComboBoxItem> playingMediaAudioTracks = new ObservableCollection<MediaTrackComboBoxItem> { new MediaTrackComboBoxItem { displayString = "None", isNull = true } };
        // List of subtitle tracks for the currently played media
        protected ObservableCollection<MediaTrackComboBoxItem> playingMediaSubtitleTracks = new ObservableCollection<MediaTrackComboBoxItem> { new MediaTrackComboBoxItem { displayString = "None", isNull = true } };

        // List of video bits added by the user
        protected ObservableCollection<VideoBitItem> bitList = new ObservableCollection<VideoBitItem>();

        // Vars used when moving the bit slider thumbs
        bool wasPlayingBeforeSeek = false;
        double bitSeekStartTime = 0.0;
        double bitSeekEndTime = 0.0;
        double lastSeekTime = 0.0;


        // Window showing progress while rendering video
        ConversionProgressWindow ffmpegProgressWindow;
        // List of temp bit filenames
        protected List<string> bitsOutputPathList = new List<string>();
        // Used to cancel an ffmpeg task
        protected CancellationTokenSource currentFfmpegCancelToken;
        // Id of bit currently being rendered
        protected int currentBitConversion = 0;


        Media resultMedia;
        MediaPlayer resultMediaPlayer;



        public MainWindow()
        {
            InitializeComponent();

            Style s = new Style();
            s.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            tabControl.ItemContainerStyle = s;

            // Settings
            autoUpdateFfmpegCheckbox.IsChecked = App.Settings.autoUpdateFfmpeg;
            setFfmpegFolderMenuItem.ToolTip = App.Settings.ffmpegDirectory;
        }

        private void videoView_Loaded(object sender, RoutedEventArgs e)
        {
            // Bits tab
            bitMediaPlayer = new MediaPlayer(App.VLC);
            bitMediaPlayer.Playing += MediaPlayer_Playing;
            bitMediaPlayer.PositionChanged += MediaPlayer_PositionChanged;
            bitMediaPlayer.EnableHardwareDecoding = true;

            videoView.MediaPlayer = bitMediaPlayer;

            bitItemsControl.ItemsSource = bitList;

            audioTrackComboBox.ItemsSource = playingMediaAudioTracks;
            subtitleTrackComboBox.ItemsSource = playingMediaSubtitleTracks;

        }

        private void resultVideoView_Loaded(object sender, RoutedEventArgs e)
        {
            // Result tab
            resultMediaPlayer = new MediaPlayer(App.VLC);
            resultMediaPlayer.EnableHardwareDecoding = true;
            resultVideoView.MediaPlayer = resultMediaPlayer;
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            App.Current.Shutdown();
        }

        private void autoUpdateFfmpegCheckbox_Click(object sender, RoutedEventArgs e)
        {
            if (App.Settings != null)
            {
                App.Settings.autoUpdateFfmpeg = autoUpdateFfmpegCheckbox.IsEnabled;
                App.SaveSettings();
            }
        }

        private void setFfmpegFolderMenuItem_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.SelectedPath = App.Settings.ffmpegDirectory;
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                App.Settings.ffmpegDirectory = dialog.SelectedPath;
                App.SaveSettings();
                setFfmpegFolderMenuItem.ToolTip = App.Settings.ffmpegDirectory;
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

        public bool LoadMediaFromPath(string path)
        {
            if (File.Exists(path))
            {
                Media newMedia = new Media(App.VLC, path, FromType.FromPath);

                // Replace base media with new one
                if (baseMedia != null)
                    baseMedia.Dispose();
                baseMedia = newMedia;
                mediaPath = path;

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
            videoPlaybackSlider.Maximum = videoBitRangeSlider.Maximum;

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

            // Update combo boxes for audio tracks and subtitle tracks
            playingMediaAudioTracks.Clear();
            playingMediaSubtitleTracks.Clear();
            playingMediaSubtitleTracks.Add(new MediaTrackComboBoxItem { displayString = "None", isNull = true });
            foreach (MediaTrack track in playingMedia.Tracks)
            {
                string trackDisplayName = $"{track.Id}: [{track.Language}] {track.Description}";
                Console.WriteLine(trackDisplayName);
                if (track.TrackType == TrackType.Audio)
                {
                    playingMediaAudioTracks.Add(new MediaTrackComboBoxItem { displayString = trackDisplayName, isNull = false, track = track });
                }
                else if (track.TrackType == TrackType.Text)
                {
                    playingMediaSubtitleTracks.Add(new MediaTrackComboBoxItem { displayString = trackDisplayName, isNull = false, track = track });
                }
            }
            if (playingMediaAudioTracks.Count == 0)
                playingMediaAudioTracks.Add(new MediaTrackComboBoxItem { displayString = "None", isNull = true });
            audioTrackComboBox.SelectedIndex = 0;
            subtitleTrackComboBox.SelectedIndex = 0;

            // Reset playback start/end time. Set media playback to loop
            playingMedia.AddOption("input-repeat=65535");
            playingMedia.AddOption("start-time=0.0");
            playingMedia.AddOption($"stop-time={videoBitRangeSlider.HigherValue}");

            bitMediaPlayer.Play(playingMedia);

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

        private void audioTrackComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (bitMediaPlayer != null)
            {
                MediaTrackComboBoxItem selectedItem = (MediaTrackComboBoxItem)audioTrackComboBox.SelectedItem;
                if (selectedItem != null && !selectedItem.isNull)
                    bitMediaPlayer.SetAudioTrack(selectedItem.track.Id);
                else
                    bitMediaPlayer.SetAudioTrack(-1);
            }
        }

        private void subtitleTrackComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (bitMediaPlayer != null)
            {
                MediaTrackComboBoxItem selectedItem = (MediaTrackComboBoxItem)subtitleTrackComboBox.SelectedItem;
                if (selectedItem != null && !selectedItem.isNull)
                    bitMediaPlayer.SetSpu(selectedItem.track.Id);
                else
                    bitMediaPlayer.SetSpu(-1);
            }
        }

        private void MediaPlayer_Playing(object sender, EventArgs e)
        {
            // switch to UI thread
            this.Dispatcher.Invoke(() =>
            {
                // selected tracks are reset by media player so we set them to the selected ones as if we received a change from the UI
                audioTrackComboBox_SelectionChanged(null, null);
                subtitleTrackComboBox_SelectionChanged(null, null);
            });
        }

        private void muteCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (bitMediaPlayer != null)
                bitMediaPlayer.Mute = muteCheckBox.IsChecked.HasValue ? muteCheckBox.IsChecked.Value : false;
        }

        private void playRateBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (bitMediaPlayer != null)
            {
                if (playRateBox.Value.HasValue)
                    bitMediaPlayer.SetRate((float)playRateBox.Value.Value);
                else
                    playRateBox.Value = (decimal)bitMediaPlayer.Rate;
            }
        }

        private void addBitButton_Click(object sender, RoutedEventArgs e)
        {
            // Add current selected video bit to the list of bits to process
            if (bitMediaPlayer != null && playingMedia != null)
            {
                bitList.Add(new VideoBitItem(
                    bitList,
                    mediaPath,
                    videoBitRangeSlider.LowerValue,
                    videoBitRangeSlider.HigherValue,
                    playRateBox.Value.HasValue ? playRateBox.Value.Value : 1.0M,
                    ((MediaTrackComboBoxItem)audioTrackComboBox.SelectedItem).isNull ? null : (MediaTrack?)((MediaTrackComboBoxItem)audioTrackComboBox.SelectedItem).track,
                    muteCheckBox.IsChecked.HasValue ? muteCheckBox.IsChecked.Value : false,
                    ((MediaTrackComboBoxItem)subtitleTrackComboBox.SelectedItem).isNull ? null : (MediaTrack?)((MediaTrackComboBoxItem)subtitleTrackComboBox.SelectedItem).track));
            }
            else
                System.Windows.MessageBox.Show("Cannot add a bit from the current media");
        }

        private void videoBitRangeSlider_HigherValueChanged(object sender, RoutedEventArgs e)
        {
            if (playingMedia != null && bitMediaPlayer != null)
            {
                // Save new value to set end time on drag completed
                bitSeekEndTime = videoBitRangeSlider.HigherValue;
                lastSeekTime = bitSeekEndTime;
                // Seek to corresponding time
                bitMediaPlayer.Time = Convert.ToInt64(videoBitRangeSlider.HigherValue * 1000.0);
            }
        }

        private void videoBitRangeSlider_LowerValueChanged(object sender, RoutedEventArgs e)
        {
            if (playingMedia != null && bitMediaPlayer != null)
            {
                // Save new value to set start time on drag completed
                bitSeekStartTime = videoBitRangeSlider.LowerValue;
                lastSeekTime = bitSeekStartTime;
                // Seek to corresponding time
                bitMediaPlayer.Time = Convert.ToInt64(videoBitRangeSlider.LowerValue * 1000.0);
            }
        }

        private void videoBitRangeSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            if (bitMediaPlayer != null)
            {
                wasPlayingBeforeSeek = bitMediaPlayer.IsPlaying;
                bitMediaPlayer.SetPause(true);
            }
        }

        private void videoBitRangeSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (bitMediaPlayer != null && playingMedia != null)
            {
                playingMedia.AddOption("input-repeat=65535");
                // Set vlc playback start time to lower value on range slide
                playingMedia.AddOption($"start-time={bitSeekStartTime}");
                // Set vlc playback end time to higher value on range slide
                playingMedia.AddOption($"stop-time={bitSeekEndTime}");

                //if (wasPlayingBeforeSeek)
                //    mediaPlayer.SetPause(false);
                bitMediaPlayer.Play(playingMedia);

                bitMediaPlayer.Position = Convert.ToInt64(lastSeekTime * 1000.0);

                wasPlayingBeforeSeek = false;
            }
        }

        private void MediaPlayer_PositionChanged(object sender, MediaPlayerPositionChangedEventArgs e)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    if (bitMediaPlayer != null && playingMedia != null)
                    {
                        videoPlaybackSlider.Value = e.Position * (Convert.ToDouble(playingMedia.Duration) * 0.001);
                    }
                });
            }
            catch (TaskCanceledException ex)
            {
                // catch exception because it can be thrown when exiting program
            }
        }

        async private void renderButton_Click(object sender, RoutedEventArgs e)
        {
            if (bitList.Count > 0)
                RenderBits();
            else
                MessageBox.Show("No video bits were added to the list.");
        }

        async private void RenderBits()
        {
            if (bitList.Count > 0)
            {
                CleanTempFolder();

                bitMediaPlayer.SetPause(true);

                // get the total number of ffmpeg conversions required (+1 if we need to concatenate bits)
                int totalConversions = bitList.Count > 1 ? bitList.Count + 1 : bitList.Count;
                bitsOutputPathList.Clear();

                ffmpegProgressWindow = new ConversionProgressWindow();
                ffmpegProgressWindow.Show();
                ffmpegProgressWindow.progressBar.Value = 0.0;

                // Render each bit separately
                for (currentBitConversion = 0; currentBitConversion < bitList.Count; ++currentBitConversion)
                {
                    ffmpegProgressWindow.statusText.Text = $"Rendering [{currentBitConversion}/{totalConversions}]";

                    VideoBitItem bitItem = bitList[currentBitConversion];

                    string outputFilename = currentBitConversion.ToString() + (Path.HasExtension(bitItem.mediaPath) ? Path.GetExtension(bitItem.mediaPath) : ".mp4");
                    string outputFilePath = Path.Combine(App.TempPath, "bits\\", outputFilename);
                    bitsOutputPathList.Add(outputFilePath);

                    // Get media infos to add required streams to conversion
                    IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(bitItem.mediaPath);

                    // Basic setup
                    IStream videoStream = mediaInfo.VideoStreams.FirstOrDefault().ChangeSpeed(Convert.ToDouble(bitItem.rate));
                    IConversion conv = FFmpeg.Conversions.New()
                        .SetOutput(outputFilePath)
                        .SetOverwriteOutput(true)
                        .AddStream(videoStream)
                        .AddParameter($"-ss {bitItem.startTimeString} -to {bitItem.endTimeString}");

                    // Set selected audio stream if not muted
                    if (bitItem.muted == false)
                    {
                        foreach (var stream in mediaInfo.AudioStreams)
                        {
                            if (stream.Language == bitItem.audioTrack?.Language)
                            {
                                stream.ChangeSpeed(Convert.ToDouble(bitItem.rate));
                                conv.AddStream(stream);
                            }
                        }
                    }

                    // Set selected subtitle stream
                    foreach (var stream in mediaInfo.SubtitleStreams)
                    {
                        if (stream.Language == bitItem.subtitleTrack?.Language)
                            conv.AddStream(stream);
                    }

                    conv.OnProgress += OnConvertionProgress;
                    currentFfmpegCancelToken = new CancellationTokenSource();
                    await conv.Start(currentFfmpegCancelToken.Token);
                }

                // render the final temp file
                if (bitsOutputPathList.Count > 1)
                {
                    var conv = await FFmpeg.Conversions.FromSnippet.Concatenate(Path.Combine(App.TempPath, "bits/", "final.mp4"), bitsOutputPathList.ToArray());
                    conv.OnProgress += OnConvertionProgress;
                    await conv.Start();
                }
                else
                {
                    //var conv = await FFmpeg.Conversions.FromSnippet.Convert(bitsOutputPathList[0], Path.Combine(App.TempPath, "bits/", "final.mp4"));
                    IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(bitsOutputPathList[0]);
                    IConversion conv = FFmpeg.Conversions.New()
                        .SetOutput(Path.Combine(App.TempPath, "bits/", "final.mp4"))
                        .SetOverwriteOutput(true)
                        .AddStream(mediaInfo.VideoStreams.FirstOrDefault())
                        .AddStream(mediaInfo.AudioStreams.FirstOrDefault());
                    conv.OnProgress += OnConvertionProgress;
                    await conv.Start();
                }

                ffmpegProgressWindow.Close();
                ffmpegProgressWindow = null;

                // Switch to result tab
                tabControl.SelectedIndex = 1;

                resultMedia = new Media(App.VLC, Path.Combine(App.TempPath, "bits\\final.mp4"), FromType.FromPath);
                resultMedia.AddOption("input-repeat=65535");
                resultMediaPlayer.Play(resultMedia);
            }
        }

        private void OnConvertionProgress(object sender, Xabe.FFmpeg.Events.ConversionProgressEventArgs args)
        {
            this.Dispatcher.Invoke(() =>
            {
                // Update progress bar completion on progress window
                int totalConversions = bitList.Count > 1 ? bitList.Count + 1 : bitList.Count;
                float singleBitPercent = 100.0f / (float)totalConversions;
                double totalProgressPercent = (singleBitPercent * currentBitConversion) + (args.Percent * singleBitPercent);
                ffmpegProgressWindow.progressBar.Value = totalProgressPercent;
            });
        }

        private void CleanTempFolder()
        {
            // make sure the temp directory exists and is cleaned
            if (Directory.Exists(Path.Combine(App.TempPath, "bits/")))
            {
                string[] fileList = Directory.GetFiles(App.TempPath);
                if (fileList.Length > 0)
                    foreach (string file in fileList)
                        File.Delete(file);
            }
            else
                Directory.CreateDirectory(Path.Combine(App.TempPath, "bits/"));
        }
    }
}
