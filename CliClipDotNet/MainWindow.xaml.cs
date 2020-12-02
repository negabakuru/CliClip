using System;
using System.Windows;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

using Xabe.FFmpeg;
using LibVLCSharp.Shared;

namespace CliClip
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // List of file extension the app is allowed to work with
        static readonly string[] AllowedExtensions = { ".mp4", ".avi", ".mkv", ".webm" };

        // Media Player used to display loaded video
        MediaPlayer bitMediaPlayer = null;

        public string playingMediaPath { get; protected set; }
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
        // Timer to actually update media player's position so it doesn't change too often
        System.Windows.Forms.Timer setSeekTimeTimer = new System.Windows.Forms.Timer();

        // List of bits renders in case we need to undo renders
        protected List<BitsRenderItem> bitsRenderItemList = new List<BitsRenderItem> { new BitsRenderItem() };
        // Id for current bits render iteration
        protected int currentBitsRenderItem = 0;

        // Window showing progress while rendering video
        ConversionProgressWindow ffmpegProgressWindow = null;
        // List of temp bit filenames
        protected List<string> bitsOutputPathList = new List<string>();
        // Used to cancel an ffmpeg task
        protected CancellationTokenSource currentFfmpegCancelToken = null;
        // Id of bit currently being rendered
        protected int currentBitRendered = 0;
        // Id of current step of bit rendering
        protected int currentConversionStep = 0;
        protected int totalConversionSteps = 0;

        Media resultMedia = null;
        MediaPlayer resultMediaPlayer = null;



        public MainWindow()
        {
            InitializeComponent();

            Style s = new Style();
            s.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            tabControl.ItemContainerStyle = s;

            // Settings
            autoUpdateFfmpegCheckbox.IsChecked = App.Settings.autoUpdateFfmpeg;
            setFfmpegFolderMenuItem.ToolTip = App.Settings.ffmpegDirectory;

            CleanTempBitsFolder();
            CleanTempRendersFolder();
        }

        ~MainWindow()
        {
            if (bitMediaPlayer.IsPlaying)
                bitMediaPlayer.Stop();
            if (resultMediaPlayer.IsPlaying)
                resultMediaPlayer.Stop();

            if (playingMedia != null)
                playingMedia.Dispose();
            if (resultMedia != null)
                resultMedia.Dispose();

            if (bitMediaPlayer != null)
                bitMediaPlayer.Dispose();
            if (resultMediaPlayer != null)
                resultMediaPlayer.Dispose();

            CleanTempBitsFolder();
            CleanTempRendersFolder();
        }

        private void videoView_Loaded(object sender, RoutedEventArgs e)
        {
            // Bits tab
            bitMediaPlayer = new MediaPlayer(App.VLC);
            bitMediaPlayer.Playing += MediaPlayer_Playing;
            bitMediaPlayer.Paused += MediaPlayer_Paused;
            bitMediaPlayer.PositionChanged += MediaPlayer_PositionChanged;
            bitMediaPlayer.EnableHardwareDecoding = true;

            videoView.MediaPlayer = bitMediaPlayer;

            bitItemsControl.ItemsSource = bitList;

            audioTrackComboBox.ItemsSource = playingMediaAudioTracks;
            subtitleTrackComboBox.ItemsSource = playingMediaSubtitleTracks;

            setSeekTimeTimer.Tick += UpdatePlayerSeekTime;
            setSeekTimeTimer.Interval = 100;
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
                LoadNewVideo(files[0]);
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
                foreach (string ext in MainWindow.AllowedExtensions)
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

        async public void LoadNewVideo(string path)
        {
            if (File.Exists(path))
            {
                playingMediaPath = path;
                Media newMedia = new Media(App.VLC, path, FromType.FromPath);

                // Replace base media with new one
                if (playingMedia != null)
                    playingMedia.Dispose();
                playingMedia = newMedia;
            }
            else
                return;

            await playingMedia.Parse();

            TimeSpan videoDuration = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(playingMedia.Duration));
            durationTextBlock.Text = videoDuration.ToString(@"hh\:mm\:ss\.fff");

            // reset range slider
            videoBitRangeSlider.LowerValue = 0.0;
            videoBitRangeSlider.Maximum = playingMedia.Duration * 0.001; // set slider to duration in seconds
            videoBitRangeSlider.HigherValue = videoBitRangeSlider.Maximum;
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

                if (track.TrackType == TrackType.Audio)
                    playingMediaAudioTracks.Add(new MediaTrackComboBoxItem { displayString = trackDisplayName, isNull = false, track = track });
                else if (track.TrackType == TrackType.Text)
                    playingMediaSubtitleTracks.Add(new MediaTrackComboBoxItem { displayString = trackDisplayName, isNull = false, track = track });
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
            openFileDialog.InitialDirectory = Path.GetDirectoryName(App.Settings.lastOpenVideoPath);
            System.Text.StringBuilder strBuild = new System.Text.StringBuilder("Video files|");
            foreach (string ext in MainWindow.AllowedExtensions)
            {
                if (ext != MainWindow.AllowedExtensions[0])
                    strBuild.Append(";");
                strBuild.Append($"*{ext}");
            }
            openFileDialog.Filter = strBuild.ToString();

            // Load the video returned
            if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                App.Settings.lastOpenVideoPath = openFileDialog.FileName;
                App.SaveSettings();
                filePathTextBox.Text = openFileDialog.FileName;
                LoadNewVideo(openFileDialog.FileName);
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

                togglePauseButton.Content = @"⏸";
            });
        }

        private void MediaPlayer_Paused(object sender, EventArgs e)
        {
            // switch to UI thread
            this.Dispatcher.Invoke(() =>
            {
                togglePauseButton.Content = @"▶";
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
                    playingMediaPath,
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
            }
        }

        private void videoBitRangeSlider_LowerValueChanged(object sender, RoutedEventArgs e)
        {
            if (playingMedia != null && bitMediaPlayer != null)
            {
                // Save new value to set start time on drag completed
                bitSeekStartTime = videoBitRangeSlider.LowerValue;
                lastSeekTime = bitSeekStartTime;
            }
        }

        private void UpdatePlayerSeekTime(object sender, EventArgs e)
        {
            // Seek to corresponding time
            if (bitMediaPlayer != null)
                bitMediaPlayer.Time = Convert.ToInt64(lastSeekTime * 1000.0);
        }

        private void videoBitRangeSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            if (bitMediaPlayer != null)
            {
                wasPlayingBeforeSeek = bitMediaPlayer.IsPlaying;
                bitMediaPlayer.SetPause(true);
                setSeekTimeTimer.Start();
            }
        }

        private void videoBitRangeSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (bitMediaPlayer != null && playingMedia != null)
            {
                setSeekTimeTimer.Stop();

                playingMedia.AddOption("input-repeat=65535");
                // Set vlc playback start time to lower value on range slide
                playingMedia.AddOption($"start-time={bitSeekStartTime}");
                // Set vlc playback end time to higher value on range slide
                playingMedia.AddOption($"stop-time={bitSeekEndTime}");

                //if (wasPlayingBeforeSeek)
                //    mediaPlayer.SetPause(false);
                bitMediaPlayer.Play(playingMedia);

                bitMediaPlayer.Time = Convert.ToInt64(lastSeekTime * 1000.0);

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

        private void SaveCurrentBitsRenderItem()
        {
            bitsRenderItemList.Last().playingMediaPath = playingMediaPath;
            bitsRenderItemList.Last().selectedAudio = (MediaTrackComboBoxItem)audioTrackComboBox.SelectedItem;
            bitsRenderItemList.Last().selectedSubtitles = (MediaTrackComboBoxItem)subtitleTrackComboBox.SelectedItem;
            bitsRenderItemList.Last().bitList.Clear();
            foreach (VideoBitItem bitItem in bitList)
                bitsRenderItemList.Last().bitList.Add(new MediaBit
                {
                    mediaPath = bitItem.bit.mediaPath,
                    startTime = bitItem.bit.startTime,
                    endTime = bitItem.bit.endTime,
                    audioTrack = bitItem.bit.audioTrack,
                    muted = bitItem.bit.muted,
                    subtitleTrack = bitItem.bit.subtitleTrack,
                    rate = bitItem.bit.rate
                });
        }

        private void renderButton_Click(object sender, RoutedEventArgs e)
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
                CleanTempBitsFolder();

                bitMediaPlayer.SetPause(true);

                // get the total number of ffmpeg conversions required (+1 if we need to concatenate bits)
                totalConversionSteps = bitList.Count * 2 + 1; // *2 in case we need to render effects on bit. +1 for the final concatenated file
                bitsOutputPathList.Clear();

                ffmpegProgressWindow = new ConversionProgressWindow();
                ffmpegProgressWindow.Owner = this;
                ffmpegProgressWindow.Show();
                ffmpegProgressWindow.progressBar.Value = 0.0;

                // Render each bit separately
                for (currentBitRendered = 0; currentBitRendered < bitList.Count; ++currentBitRendered)
                    await RenderCurrentBit();

                currentConversionStep = totalConversionSteps;
                ffmpegProgressWindow.statusText.Text = $"Rendering [{currentConversionStep}/{totalConversionSteps}]";

                // render the final temp file (keep the temp files just in case?)
                if (bitsOutputPathList.Count > 1)
                {
                    // Concatenate all bits into one video
                    StringBuilder concatFileListStr = new StringBuilder();
                    foreach (string bit in bitsOutputPathList)
                        concatFileListStr.AppendLine($"file \'{bit}\'");
                    File.WriteAllText(Path.Combine(App.TempPath, "bits\\concatList.txt"), concatFileListStr.ToString());

                    var conv = FFmpeg.Conversions.New();
                    conv.OnProgress += OnConvertionProgress;
                    await conv.Start($"-f concat -safe 0 -i {Path.Combine(App.TempPath, "bits/concatList.txt")} -c copy {Path.Combine(App.TempPath, "bits\\final.mp4")}");
                }
                else
                {
                    // Single bit already rendered so we just copy the file
                    File.Copy(bitsOutputPathList[0], Path.Combine(App.TempPath, "bits/", "final.mp4"), true);
                }

                // Save current bits render item and switch to a new one
                SaveCurrentBitsRenderItem();
                ++currentBitsRenderItem;
                if (currentBitsRenderItem < bitsRenderItemList.Count)
                    bitsRenderItemList.RemoveRange(currentBitsRenderItem, bitsRenderItemList.Count - currentBitsRenderItem); // remove overwritten render items
                bitsRenderItemList.Add(new BitsRenderItem());
                prevRenderItemButton.Visibility = Visibility.Visible;
                nextRenderItemButton.Visibility = Visibility.Hidden;
                loadCurrentRenderedBitButton.Visibility = Visibility.Visible;

                // Move new rendered video to render folder
                if (File.Exists(Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4")))
                    File.Delete(Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4"));
                File.Move(Path.Combine(App.TempPath, "bits/", "final.mp4"), Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4"));

                ffmpegProgressWindow.Close();
                ffmpegProgressWindow = null;

                // Clear bit list and load new video for new bits render item
                bitList.Clear();
                LoadNewVideo(Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4"));
                SaveCurrentBitsRenderItem();
            }
        }

        async private Task RenderCurrentBit()
        {
            currentConversionStep = (currentBitRendered * 2 + 1);
            ffmpegProgressWindow.statusText.Text = $"Rendering [{currentConversionStep}/{totalConversionSteps}]";

            VideoBitItem bitItem = bitList[currentBitRendered];

            // convert to mp4 video in case subtitles need to be burned into video so we can apply effects like changing playback speed
            string outputFilename = $"{currentBitRendered.ToString()}.mp4";
            string outputFilePath = Path.Combine(App.TempPath, "bits\\", outputFilename);
            bitsOutputPathList.Add(outputFilePath);

            // Get media infos to add required streams to conversion
            IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(bitItem.bit.mediaPath);

            // Extract selected subtitle stream
            bool subtitleExtracted = false;
            foreach (var stream in mediaInfo.SubtitleStreams)
            {
                if (stream.Index == bitItem.bit.subtitleTrack?.Id)
                {
                    await FFmpeg.Conversions.New()
                    .SetOverwriteOutput(true)
                    .AddStream(stream)
                    .SetOutput(Path.ChangeExtension(outputFilePath, ".srt"))
                    .Start();
                    subtitleExtracted = true;
                }
            }

            // Basic setup
            IVideoStream videoStream = mediaInfo.VideoStreams.FirstOrDefault();
            // TODO handle subtitles scaling
            if (subtitleExtracted) // burn extracted subtitles into video
                videoStream.AddSubtitles(Path.ChangeExtension(outputFilePath, ".srt"));
            IConversion conv = FFmpeg.Conversions.New()
                .SetOutput(outputFilePath)
                .SetOverwriteOutput(true)
                .AddStream(videoStream)
                .AddParameter($"-ss {bitItem.startTimeString} -to {bitItem.endTimeString} -g {Convert.ToInt32(videoStream.Framerate)}"); // set start/end time, burn subtitles into video and set keyframe interval (for seeking)

            // Set selected audio stream if not muted
            if (bitItem.bit.muted == false)
            {
                foreach (var stream in mediaInfo.AudioStreams)
                {
                    if (stream.Index == bitItem.bit.audioTrack?.Id)
                        conv.AddStream(stream);
                }
            }

            conv.OnProgress += OnConvertionProgress;
            currentFfmpegCancelToken = new CancellationTokenSource();
            await conv.Start(currentFfmpegCancelToken.Token);

            currentFfmpegCancelToken.Dispose();
            currentFfmpegCancelToken = null;

            ++currentConversionStep;
            ffmpegProgressWindow.statusText.Text = $"Rendering [{currentConversionStep}/{totalConversionSteps}]";
            // Should apply additional effects after subtitles were burned into video
            if (bitItem.bit.rate != 1.0M)
            {
                // Set filename to replace current rendered bit video
                outputFilename = $"{currentBitRendered.ToString()}_fx.mp4";
                outputFilePath = Path.Combine(App.TempPath, "bits\\", outputFilename);
                bitsOutputPathList[bitsOutputPathList.Count - 1] = outputFilePath;

                mediaInfo = await FFmpeg.GetMediaInfo(outputFilePath);
                conv = FFmpeg.Conversions.New()
                    .SetOutput(outputFilePath)
                    .SetOverwriteOutput(true)
                    .AddStream(mediaInfo.VideoStreams.First().ChangeSpeed(Convert.ToDouble(bitItem.bit.rate)))
                    .AddStream(mediaInfo.AudioStreams.FirstOrDefault().ChangeSpeed(Convert.ToDouble(bitItem.bit.rate)));

                currentFfmpegCancelToken = new CancellationTokenSource();
                conv.OnProgress += OnConvertionProgress;
                await conv.Start(currentFfmpegCancelToken.Token);

                currentFfmpegCancelToken.Dispose();
                currentFfmpegCancelToken = null;
            }
        }

        private void OnConvertionProgress(object sender, Xabe.FFmpeg.Events.ConversionProgressEventArgs args)
        {
            this.Dispatcher.Invoke(() =>
            {
                // Update progress bar completion on progress window
                double singleBitPercent = 100.0 / (double)totalConversionSteps;
                double totalProgressPercent = (singleBitPercent * (double)currentConversionStep) + ((double)args.Percent * 0.01 * singleBitPercent);
                if (ffmpegProgressWindow != null)
                    ffmpegProgressWindow.progressBar.Value = totalProgressPercent;
            });
        }

        private void CleanTempRendersFolder()
        {
            // make sure the temp directory exists and is cleaned
            if (Directory.Exists(Path.Combine(App.TempPath, "renders/")))
            {
                string[] fileList = Directory.GetFiles(Path.Combine(App.TempPath, "renders/"));
                if (fileList.Length > 0)
                    foreach (string file in fileList)
                        File.Delete(file);
            }
            else
                Directory.CreateDirectory(Path.Combine(App.TempPath, "renders/"));
        }

        private void CleanTempBitsFolder()
        {
            // make sure the temp directory exists and is cleaned
            if (Directory.Exists(Path.Combine(App.TempPath, "bits/")))
            {
                string[] fileList = Directory.GetFiles(Path.Combine(App.TempPath, "bits/"));
                if (fileList.Length > 0)
                    foreach (string file in fileList)
                        File.Delete(file);
            }
            else
                Directory.CreateDirectory(Path.Combine(App.TempPath, "bits/"));
        }

        private void newClip_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("All unsaved work will be lost.\nReset clip anyway?", "Reset clip", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                ResetClip();
        }

        private void ResetClip()
        {
            if (bitMediaPlayer != null)
                bitMediaPlayer.Stop();
            playingMediaPath = "";
            if (playingMedia != null)
            {
                playingMedia.Dispose();
                playingMedia = null;
            }
            playingMediaAudioTracks = new ObservableCollection<MediaTrackComboBoxItem> { new MediaTrackComboBoxItem { displayString = "None", isNull = true } };
            playingMediaSubtitleTracks = new ObservableCollection<MediaTrackComboBoxItem> { new MediaTrackComboBoxItem { displayString = "None", isNull = true } };
            bitList.Clear();

            bitSeekStartTime = 0.0;
            bitSeekEndTime = 0.0;

            prevRenderItemButton.Visibility = Visibility.Hidden;
            nextRenderItemButton.Visibility = Visibility.Hidden;
            loadCurrentRenderedBitButton.Visibility = Visibility.Hidden;

            bitsRenderItemList = new List<BitsRenderItem> { new BitsRenderItem() };
            currentBitsRenderItem = 0;

            bitsOutputPathList = new List<string>();

            if (resultMedia != null)
            {
                resultMedia.Dispose();
                resultMedia = null;
            }
            if (resultMediaPlayer != null)
                resultMediaPlayer.Stop();

            CleanTempBitsFolder();
            CleanTempRendersFolder();
        }

        async private void exportClip_Click(object sender, RoutedEventArgs e)
        {
            if (currentBitsRenderItem == 0)
            {
                MessageBox.Show("You need to render bits to export your clip.");
                return;
            }

            System.Windows.Forms.SaveFileDialog dialog = new System.Windows.Forms.SaveFileDialog();
            dialog.Filter = "MP4 (*.mp4)|*.mp4|WEBM (*.webm)|*.webm|GIF (*.gif)|*.gif";
            dialog.AddExtension = true;
            dialog.FilterIndex = 0;
            dialog.OverwritePrompt = true;
            dialog.RestoreDirectory = true;
            dialog.InitialDirectory = Path.GetDirectoryName(App.Settings.lastExportPath);

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                App.Settings.lastExportPath = dialog.FileName;
                App.SaveSettings();

                string sourceFilePath = Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4");
                string saveFilePath = dialog.FileName;

                // Prepare for eventual conversion
                var conv = FFmpeg.Conversions.New()
               .SetOverwriteOutput(true);
                conv.OnProgress += OnConvertionProgress;
                currentConversionStep = 0;
                totalConversionSteps = 1;

                ffmpegProgressWindow = new ConversionProgressWindow();
                ffmpegProgressWindow.Owner = this;
                ffmpegProgressWindow.statusText.Text = "Exporting clip...";
                ffmpegProgressWindow.progressBar.Value = 0.0;
                ffmpegProgressWindow.Show();

                switch (Path.GetExtension(dialog.FileName))
                {
                    case ".mp4":
                        File.Copy(sourceFilePath, saveFilePath, true);
                        break;

                    case ".webm":
                        await conv.Start($"-i {sourceFilePath} {saveFilePath}");
                        break;

                    case ".gif":
                        await conv.Start($"-i {sourceFilePath} -loop 0 {saveFilePath}");
                        break;
                }
                if (ffmpegProgressWindow != null)
                {
                    ffmpegProgressWindow.Close();
                    ffmpegProgressWindow = null;
                }

                MessageBox.Show($"Successfully exported \"{saveFilePath}\"");
            }
        }

        private void LoadRenderItem(int index)
        {
            if (index != currentBitsRenderItem && index >= 0 && index < bitsRenderItemList.Count)
            {
                // Save current bits render item and switch to a new one
                SaveCurrentBitsRenderItem();

                currentBitsRenderItem = index;
                BitsRenderItem renderItem = bitsRenderItemList[currentBitsRenderItem];
                bitsOutputPathList = new List<string>();

                prevRenderItemButton.Visibility = (currentBitsRenderItem > 0) ? Visibility.Visible : Visibility.Hidden;
                nextRenderItemButton.Visibility = (currentBitsRenderItem < bitsRenderItemList.Count - 1) ? Visibility.Visible : Visibility.Hidden;
                loadCurrentRenderedBitButton.Visibility = (currentBitsRenderItem > 0) ? Visibility.Visible : Visibility.Hidden;

                bitSeekStartTime = 0.0;
                bitSeekEndTime = 0.0;

                if (currentBitsRenderItem > 0 && File.Exists(Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4")))
                    LoadNewVideo(Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4"));
                else
                    LoadNewVideo(bitsRenderItemList[currentBitsRenderItem].playingMediaPath);

                // Update bit list
                bitList.Clear();
                foreach (var bit in renderItem.bitList)
                    bitList.Add(new VideoBitItem(bitList, bit.mediaPath, bit.startTime, bit.endTime, bit.rate, bit.audioTrack, bit.muted, bit.subtitleTrack));

                // Set selected audio track
                foreach (var audioTrack in playingMediaAudioTracks)
                    if ((audioTrack.isNull && renderItem.selectedAudio.isNull) || (audioTrack.track.Id == renderItem.selectedAudio.track.Id))
                    audioTrackComboBox.SelectedItem = audioTrack;

                // Set selected subtitles track
                foreach (var subtitlesTrack in playingMediaSubtitleTracks)
                    if ((subtitlesTrack.isNull && renderItem.selectedSubtitles.isNull) || (subtitlesTrack.track.Id == renderItem.selectedSubtitles.track.Id))
                        subtitleTrackComboBox.SelectedItem = subtitlesTrack;
            }
        }

        private void loadCurrentRenderedBitButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentBitsRenderItem > 0 && File.Exists(Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4")))
                LoadNewVideo(Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4"));
            else
                MessageBox.Show($"Could not load  video {Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4")}");
        }

        private void prevRenderItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentBitsRenderItem > 0)
            {
                LoadRenderItem(currentBitsRenderItem - 1);
            }
        }

        private void nextRenderItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentBitsRenderItem < bitsRenderItemList.Count - 1)
            {
                LoadRenderItem(currentBitsRenderItem + 1);
            }
        }

        private void togglePauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (bitMediaPlayer != null)
            {
                if (bitMediaPlayer.IsPlaying)
                    bitMediaPlayer.SetPause(true);
                else
                    bitMediaPlayer.SetPause(false);
            }
        }
    }
}
