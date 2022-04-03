using System;
using System.Windows;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

using Xabe.FFmpeg;
using Mpv.NET.Player;
using System.Collections.Generic;

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
		readonly MpvPlayer bitMediaPlayer = null;

		public string PlayingMediaPath { get; protected set; }
		public IMediaInfo PlayingMediaInfos { get; protected set; }

		// List of audio tracks for the currently played media
		protected ObservableCollection<MediaTrackComboBoxItem> playingMediaAudioTracks = new ObservableCollection<MediaTrackComboBoxItem> { new MediaTrackComboBoxItem { DisplayString = "None", IsNull = true } };
		// List of subtitle tracks for the currently played media
		protected ObservableCollection<MediaTrackComboBoxItem> playingMediaSubtitleTracks = new ObservableCollection<MediaTrackComboBoxItem> { new MediaTrackComboBoxItem { DisplayString = "None", IsNull = true } };

		// List of video bits added by the user
		protected ObservableCollection<VideoBitItem> bitList = new ObservableCollection<VideoBitItem>();

		// Vars used when moving the bit slider thumbs
		protected bool wasPlayingBeforeSeek = false;
		protected TimeSpan bitSeekStartTime;
		protected TimeSpan bitSeekEndTime;
		protected TimeSpan lastSeekTime;
		// Timer to actually update media player's position so it doesn't change too often
		readonly System.Windows.Forms.Timer setSeekTimeTimer = new System.Windows.Forms.Timer();

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



		public MainWindow()
		{
			InitializeComponent();

			Style s = new Style();
			s.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
			TabControl.ItemContainerStyle = s;

			// TODO check if we can update the mpv version to mpv-2
			bitMediaPlayer = new MpvPlayer(VideoView.Handle, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mpv-1.dll"))
			{
				KeepOpen = KeepOpen.Always,
				Loop = true,
				Volume = 100,
			};

			bitMediaPlayer.MediaResumed += MediaPlayer_Playing;
			bitMediaPlayer.MediaPaused += MediaPlayer_Paused;
			bitMediaPlayer.PositionChanged += MediaPlayer_PositionChanged;
			bitMediaPlayer.MediaError += MediaPlayer_Error;

			BitItemsControl.ItemsSource = bitList;

			AudioTrackComboBox.ItemsSource = playingMediaAudioTracks;
			SubtitleTrackComboBox.ItemsSource = playingMediaSubtitleTracks;

			setSeekTimeTimer.Tick += UpdatePlayerSeekTime;
			setSeekTimeTimer.Interval = 100;

			// Settings
			AutoUpdateFfmpegCheckbox.IsChecked = App.Settings.autoUpdateFfmpeg;
			SetFfmpegFolderMenuItem.ToolTip = App.Settings.ffmpegDirectory;

			CleanTempBitsFolder();
			CleanTempRendersFolder();
		}

		private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (bitMediaPlayer != null)
			{
				if (bitMediaPlayer.IsPlaying)
					bitMediaPlayer.Stop();
				// Dispose blocks the process forever :[
				//bitMediaPlayer.Dispose();
			}

			CleanTempBitsFolder();
			CleanTempRendersFolder();
		}

		private void MenuItem_Click(object sender, RoutedEventArgs e)
		{
			App.Current.Shutdown();
		}

		private void AutoUpdateFfmpegCheckbox_Click(object sender, RoutedEventArgs e)
		{
			if (App.Settings != null)
			{
				App.Settings.autoUpdateFfmpeg = AutoUpdateFfmpegCheckbox.IsEnabled;
				App.SaveSettings();
			}
		}

		private void SetFfmpegFolderMenuItem_Click(object sender, RoutedEventArgs e)
		{
			System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog
			{
				SelectedPath = App.Settings.ffmpegDirectory
			};
			if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
			{
				App.Settings.ffmpegDirectory = dialog.SelectedPath;
				App.SaveSettings();
				SetFfmpegFolderMenuItem.ToolTip = App.Settings.ffmpegDirectory;
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

		private void LoadVideoButton_Click(object sender, RoutedEventArgs e)
		{
			// Open dialog for user to select a video file
			System.Windows.Forms.OpenFileDialog openFileDialog = new System.Windows.Forms.OpenFileDialog
			{
				CheckFileExists = true,
				CheckPathExists = true,
				Multiselect = false,
				InitialDirectory = Path.GetDirectoryName(App.Settings.lastOpenVideoPath)
			};
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
				LoadNewVideo(openFileDialog.FileName);
			}
		}

		async public void LoadNewVideo(string path)
		{
			if (File.Exists(path))
			{
				PlayingMediaPath = path;

				PlayingMediaInfos = await FFmpeg.GetMediaInfo(PlayingMediaPath);
				bitMediaPlayer.Load(PlayingMediaPath);
			}
			else
				return;

			FilePathTextBox.Text = PlayingMediaPath;

			TimeSpan videoDuration = PlayingMediaInfos.Duration;
			DurationTextBlock.Text = videoDuration.ToString(@"hh\:mm\:ss\.fff");

			// Set media playback to loop
			bitMediaPlayer.API.SetPropertyString("ab-loop-count", "inf");

			// reset range slider
			VideoBitRangeSlider.Maximum = videoDuration.TotalSeconds; // set slider to duration in seconds
			VideoPlaybackSlider.Maximum = VideoBitRangeSlider.Maximum;

			// Reset bit range infos
			SetBitStart(new TimeSpan(0));
			SetBitEnd(videoDuration);

			// Compute framerate from video track data
			if (PlayingMediaInfos.VideoStreams.Count() > 0)
			{
				double framerate = PlayingMediaInfos.VideoStreams.ElementAt(0).Framerate;
				VideoBitRangeSlider.Step = 1.0 / framerate;
				FramerateTextBlock.Text = $"{framerate}";
			}

			// Compute readable tick frequency for the video duration
			double roughTickInterval = VideoBitRangeSlider.Maximum * 0.05;
			double greatestDenominator = 0.1;
			while ((roughTickInterval / (greatestDenominator * 10.0)) >= 1.0)
				greatestDenominator *= 10.0;
			VideoBitRangeSlider.TickFrequency = roughTickInterval - (roughTickInterval % greatestDenominator);

			// Update combo boxes for audio tracks and subtitle tracks
			playingMediaAudioTracks.Clear();
			playingMediaSubtitleTracks.Clear();
			playingMediaSubtitleTracks.Add(new MediaTrackComboBoxItem { Index = -1, DisplayString = "None", IsNull = true });
			int categoryIndex = 1;
			foreach (IAudioStream track in PlayingMediaInfos.AudioStreams)
			{
				string trackDisplayName = $"{categoryIndex}: [{track.Language}]";
				playingMediaAudioTracks.Add(new MediaTrackComboBoxItem { Index = categoryIndex, DisplayString = trackDisplayName, IsNull = false, Track = track });
				++categoryIndex;
			}
			categoryIndex = 1;
			foreach (ISubtitleStream track in PlayingMediaInfos.SubtitleStreams)
			{
				string trackDisplayName = $"{categoryIndex}: [{track.Language}] {track.Title}";
				playingMediaSubtitleTracks.Add(new MediaTrackComboBoxItem { Index = categoryIndex, DisplayString = trackDisplayName, IsNull = false, Track = track });
				++categoryIndex;
			}
			if (playingMediaAudioTracks.Count == 0)
				playingMediaAudioTracks.Add(new MediaTrackComboBoxItem { Index = -1, DisplayString = "None", IsNull = true });
			AudioTrackComboBox.SelectedIndex = 0;
			SubtitleTrackComboBox.SelectedIndex = 0;

			bitMediaPlayer.Resume();
			NoVideoTextBlock.Visibility = Visibility.Hidden;
		}

		private void AudioTrackComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
		{
			if (bitMediaPlayer != null)
			{
				MediaTrackComboBoxItem selectedItem = (MediaTrackComboBoxItem)AudioTrackComboBox.SelectedItem;
				if (selectedItem != null && !selectedItem.IsNull)
					bitMediaPlayer.API.SetPropertyString("aid", selectedItem.Index.ToString());
				else
					bitMediaPlayer.API.SetPropertyString("aid", "no");
			}
		}

		private void SubtitleTrackComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
		{
			if (bitMediaPlayer != null)
			{
				MediaTrackComboBoxItem selectedItem = (MediaTrackComboBoxItem)SubtitleTrackComboBox.SelectedItem;
				if (selectedItem != null && !selectedItem.IsNull)
					bitMediaPlayer.API.SetPropertyString("sid", selectedItem.Index.ToString());
				else
					bitMediaPlayer.API.SetPropertyString("sid", "no");
			}
		}

		private void MediaPlayer_Playing(object sender, EventArgs e)
		{
			// switch to UI thread
			this.Dispatcher.Invoke(() =>
			{
				TogglePauseButton.Content = @"⏸";
			});
		}

		private void MediaPlayer_Paused(object sender, EventArgs e)
		{
			// switch to UI thread
			this.Dispatcher.Invoke(() =>
			{
				TogglePauseButton.Content = @"▶";
			});
		}

		private void MediaPlayer_PositionChanged(object sender, MpvPlayerPositionChangedEventArgs args)
		{
			try
			{
				this.Dispatcher.Invoke(() =>
				{
					if (bitMediaPlayer != null)
					{
						VideoPlaybackSlider.Value = args.NewPosition.TotalSeconds;
					}
				});
			}
			catch (TaskCanceledException ex)
			{
				// catch exception because it can be thrown when exiting program
				Console.WriteLine(ex);
			}
		}

		private void MediaPlayer_Error(object sender, EventArgs e)
		{
			this.Dispatcher.Invoke(() =>
			{
				Console.WriteLine(e);
			});
		}

		private void MuteCheckBox_Click(object sender, RoutedEventArgs e)
		{
			if (bitMediaPlayer != null)
				bitMediaPlayer.Volume = MuteCheckBox.IsChecked.HasValue && MuteCheckBox.IsChecked.Value ? 0 : 100;
		}

		private void PlayRateBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
		{
			if (bitMediaPlayer != null)
			{
				if (PlayRateBox.Value.HasValue)
					bitMediaPlayer.Speed = (double)PlayRateBox.Value.Value;
				else
					PlayRateBox.Value = (decimal)bitMediaPlayer.Speed;
			}
		}

		private void SetBitStart(TimeSpan Timestamp, bool UpdateSlider = true)
		{
			bitSeekStartTime = Timestamp;
			if (UpdateSlider)
				VideoBitRangeSlider.LowerValue = bitSeekStartTime.TotalSeconds;
			bitMediaPlayer.API.SetPropertyString("ab-loop-a", bitSeekStartTime.TotalSeconds.ToString());
		}

		private void SetBitEnd(TimeSpan Timestamp, bool UpdateSlider = true)
		{
			bitSeekEndTime = Timestamp;
			if (UpdateSlider)
				VideoBitRangeSlider.HigherValue = bitSeekEndTime.TotalSeconds;
			bitMediaPlayer.API.SetPropertyString("ab-loop-b", bitSeekEndTime.TotalSeconds.ToString());
		}

		private void SetBitStartButton_Click(object sender, RoutedEventArgs e)
		{
			if (VideoPlaybackSlider.Value < VideoBitRangeSlider.HigherValue)
				SetBitStart(TimeSpan.FromSeconds(VideoPlaybackSlider.Value));
		}

		private void SetBitEndButton_Click(object sender, RoutedEventArgs e)
		{
			if (VideoPlaybackSlider.Value > VideoBitRangeSlider.LowerValue)
				SetBitEnd(TimeSpan.FromSeconds(VideoPlaybackSlider.Value));
		}

		private void GoToBitStartButton_Click(object sender, RoutedEventArgs e)
		{
			bitMediaPlayer.Position = bitSeekStartTime;
		}

		private void GoToBitEndButton_Click(object sender, RoutedEventArgs e)
		{
			bitMediaPlayer.Position = bitSeekEndTime;
		}

		private void PreviousFrameButton_Click(object sender, RoutedEventArgs e)
		{
			if (bitMediaPlayer != null)
			{
				bitMediaPlayer.Pause();
				bitMediaPlayer.PreviousFrame();
			}
		}

		private void NextFrameButton_Click(object sender, RoutedEventArgs e)
		{
			if (bitMediaPlayer != null)
			{
				bitMediaPlayer.Pause();
				bitMediaPlayer.NextFrame();
			}
		}

		private void TogglePauseButton_Click(object sender, RoutedEventArgs e)
		{
			if (bitMediaPlayer != null)
			{
				if (bitMediaPlayer.IsPlaying)
					bitMediaPlayer.Pause();
				else
					bitMediaPlayer.Resume();
			}
		}

		private void AddBitButton_Click(object sender, RoutedEventArgs e)
		{
			// Add current selected video bit to the list of bits to process
			if (bitMediaPlayer != null)
			{
				bitList.Add(new VideoBitItem(
					bitList,
					PlayingMediaPath,
					TimeSpan.FromSeconds(VideoBitRangeSlider.LowerValue),
					TimeSpan.FromSeconds(VideoBitRangeSlider.HigherValue),
					PlayRateBox.Value ?? 1.0M,
					AudioTrackComboBox.SelectedItem == null ? null : (IAudioStream)((MediaTrackComboBoxItem)AudioTrackComboBox.SelectedItem).Track,
					MuteCheckBox.IsChecked.HasValue && MuteCheckBox.IsChecked.Value,
					SubtitleTrackComboBox.SelectedItem == null ? null : (ISubtitleStream)((MediaTrackComboBoxItem)SubtitleTrackComboBox.SelectedItem).Track));
			}
			else
				System.Windows.MessageBox.Show("Cannot add a bit from the current media");
		}

		private void VideoBitRangeSlider_HigherValueChanged(object sender, RoutedEventArgs e)
		{
			if (bitMediaPlayer != null)
			{
				// Save new value to set end time on drag completed
				SetBitEnd(TimeSpan.FromSeconds(VideoBitRangeSlider.HigherValue), false);
				lastSeekTime = bitSeekEndTime;
			}
		}

		private void VideoBitRangeSlider_LowerValueChanged(object sender, RoutedEventArgs e)
		{
			if (bitMediaPlayer != null)
			{
				// Save new value to set start time on drag completed
				SetBitStart(TimeSpan.FromSeconds(VideoBitRangeSlider.LowerValue), false);
				lastSeekTime = bitSeekStartTime;
			}
		}

		private void VideoPlaybackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			lastSeekTime = TimeSpan.FromSeconds(VideoPlaybackSlider.Value);
		}

		private void VideoPlaybackSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
		{
			if (bitMediaPlayer != null)
			{
				wasPlayingBeforeSeek = bitMediaPlayer.IsPlaying;
				bitMediaPlayer.Pause();
				setSeekTimeTimer.Start();
			}
		}

		private void VideoPlaybackSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
		{
			if (bitMediaPlayer != null)
			{
				setSeekTimeTimer.Stop();

				bitMediaPlayer.Position = lastSeekTime;

				if (wasPlayingBeforeSeek)
					bitMediaPlayer.Resume();

				wasPlayingBeforeSeek = false;
			}
		}

		private void UpdatePlayerSeekTime(object sender, EventArgs e)
		{
			// Seek to corresponding time
			if (bitMediaPlayer != null)
				bitMediaPlayer.Position = lastSeekTime;
		}

		private void VideoBitRangeSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
		{
			if (bitMediaPlayer != null)
			{
				wasPlayingBeforeSeek = bitMediaPlayer.IsPlaying;
				bitMediaPlayer.Pause();
				setSeekTimeTimer.Start();
			}
		}

		private void VideoBitRangeSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
		{
			if (bitMediaPlayer != null)
			{
				setSeekTimeTimer.Stop();

				// Set playback start and end time to values on range slide
				SetBitStart(TimeSpan.FromSeconds(VideoBitRangeSlider.LowerValue));
				SetBitEnd(TimeSpan.FromSeconds(VideoBitRangeSlider.HigherValue));

				if (wasPlayingBeforeSeek)
					bitMediaPlayer.Resume();

				bitMediaPlayer.Position = lastSeekTime;

				wasPlayingBeforeSeek = false;
			}
		}

		private void NewClip_Click(object sender, RoutedEventArgs e)
		{
			if (MessageBox.Show("All unsaved work will be lost.\nReset clip anyway?", "Reset clip", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
				ResetClip();
		}

		private void ResetClip()
		{
			if (bitMediaPlayer != null)
				bitMediaPlayer.Stop();
			PlayingMediaPath = "";
			playingMediaAudioTracks = new ObservableCollection<MediaTrackComboBoxItem> { new MediaTrackComboBoxItem { DisplayString = "None", IsNull = true } };
			playingMediaSubtitleTracks = new ObservableCollection<MediaTrackComboBoxItem> { new MediaTrackComboBoxItem { DisplayString = "None", IsNull = true } };
			bitList.Clear();

			bitSeekStartTime = new TimeSpan(0);
			bitSeekEndTime = new TimeSpan(0);

			PrevRenderItemButton.Visibility = Visibility.Hidden;
			NextRenderItemButton.Visibility = Visibility.Hidden;
			LoadCurrentRenderedBitButton.Visibility = Visibility.Hidden;

			bitsRenderItemList = new List<BitsRenderItem> { new BitsRenderItem() };
			currentBitsRenderItem = 0;

			bitsOutputPathList = new List<string>();

			CleanTempBitsFolder();
			CleanTempRendersFolder();
		}

		private void SaveCurrentBitsRenderItem()
		{
			bitsRenderItemList[currentBitsRenderItem].playingMediaPath = PlayingMediaPath;
			bitsRenderItemList[currentBitsRenderItem].selectedAudio = (MediaTrackComboBoxItem)AudioTrackComboBox.SelectedItem;
			bitsRenderItemList[currentBitsRenderItem].selectedSubtitles = (MediaTrackComboBoxItem)SubtitleTrackComboBox.SelectedItem;
			bitsRenderItemList[currentBitsRenderItem].bitList.Clear();
			foreach (VideoBitItem bitItem in bitList)
				bitsRenderItemList[currentBitsRenderItem].bitList.Add(new MediaBit
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

		private void RenderButton_Click(object sender, RoutedEventArgs e)
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

				bitMediaPlayer.Pause();

				// get the total number of ffmpeg conversions required (+1 if we need to concatenate bits)
				totalConversionSteps = bitList.Count * 2 + 1; // *2 in case we need to render effects on bit. +1 for the final concatenated file
				bitsOutputPathList.Clear();

				ffmpegProgressWindow = new ConversionProgressWindow
				{
					Owner = this
				};
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
				PrevRenderItemButton.Visibility = Visibility.Visible;
				NextRenderItemButton.Visibility = Visibility.Hidden;
				LoadCurrentRenderedBitButton.Visibility = Visibility.Visible;

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
			string outputFilename = currentBitRendered.ToString() + ".mp4";
			string outputFilePath = Path.Combine(App.TempPath, "bits\\", outputFilename);
			bitsOutputPathList.Add(outputFilePath);

			// Get media infos to add required streams to conversion
			IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(bitItem.bit.mediaPath);

			// Extract selected subtitle stream
			bool subtitleExtracted = false;
			foreach (var stream in mediaInfo.SubtitleStreams)
			{
				if (stream.Index == bitItem.bit.subtitleTrack?.Index)
				{
					await FFmpeg.Conversions.New()
					.SetOverwriteOutput(true)
					.AddStream(stream)
					.SetOutput(Path.ChangeExtension(outputFilePath, ".ass"))
					.Start();
					subtitleExtracted = true;
				}
			}

			// Set up bit rendering
			IVideoStream videoStream = mediaInfo.VideoStreams.FirstOrDefault();
			if (subtitleExtracted) // burn extracted subtitles into video
				videoStream.AddSubtitles(Path.ChangeExtension(outputFilePath, ".ass"), GetClosestVideoSize(videoStream.Width, videoStream.Height));
			IConversion conv = FFmpeg.Conversions.New()
				.SetOutput(outputFilePath)
				.SetOverwriteOutput(true)
				.AddStream(videoStream)
				.AddParameter($"-ss {bitItem.StartTimeString} -to {bitItem.EndTimeString} -g {Convert.ToInt32(videoStream.Framerate)}"); // set start/end time, burn subtitles into video and set keyframe interval (for seeking)

			// Set selected audio stream if not muted
			if (bitItem.bit.muted == false)
			{
				foreach (var stream in mediaInfo.AudioStreams)
				{
					if (stream.Index == bitItem.bit.audioTrack?.Index)
						conv.AddStream(stream);
				}
			}

			// begin rendering
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
				outputFilename = currentBitRendered.ToString() + "_fx.mp4";
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

		static public VideoSize GetClosestVideoSize(int width, int height)
		{
			VideoSize closestSize = VideoSize.Hd1080;
			int smallestDelta = 999999;

			if (Math.Abs(width - 720) + Math.Abs(height - 480) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 720) + Math.Abs(height - 480);
				closestSize = VideoSize.Ntsc;
			}
			if (Math.Abs(width - 720) + Math.Abs(height - 576) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 720) + Math.Abs(height - 576);
				closestSize = VideoSize.Pal;
			}
			if (Math.Abs(width - 352) + Math.Abs(height - 240) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 352) + Math.Abs(height - 240);
				closestSize = VideoSize.Qntsc;
			}
			if (Math.Abs(width - 352) + Math.Abs(height - 288) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 352) + Math.Abs(height - 288);
				closestSize = VideoSize.Qpal;
			}
			if (Math.Abs(width - 640) + Math.Abs(height - 480) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 640) + Math.Abs(height - 480);
				closestSize = VideoSize.Sntsc;
			}
			if (Math.Abs(width - 768) + Math.Abs(height - 576) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 768) + Math.Abs(height - 576);
				closestSize = VideoSize.Spal;
			}
			if (Math.Abs(width - 352) + Math.Abs(height - 240) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 352) + Math.Abs(height - 240);
				closestSize = VideoSize.Film;
			}
			if (Math.Abs(width - 352) + Math.Abs(height - 240) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 352) + Math.Abs(height - 240);
				closestSize = VideoSize.NtscFilm;
			}
			if (Math.Abs(width - 128) + Math.Abs(height - 96) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 128) + Math.Abs(height - 96);
				closestSize = VideoSize.Sqcif;
			}
			if (Math.Abs(width - 176) + Math.Abs(height - 144) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 176) + Math.Abs(height - 144);
				closestSize = VideoSize.Qcif;
			}
			if (Math.Abs(width - 352) + Math.Abs(height - 288) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 352) + Math.Abs(height - 288);
				closestSize = VideoSize.Cif;
			}
			if (Math.Abs(width - 704) + Math.Abs(height - 576) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 704) + Math.Abs(height - 576);
				closestSize = VideoSize._4Cif;
			}
			if (Math.Abs(width - 1408) + Math.Abs(height - 1152) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 1408) + Math.Abs(height - 1152);
				closestSize = VideoSize._16cif;
			}
			if (Math.Abs(width - 160) + Math.Abs(height - 120) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 160) + Math.Abs(height - 120);
				closestSize = VideoSize.Qqvga;
			}
			if (Math.Abs(width - 320) + Math.Abs(height - 240) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 320) + Math.Abs(height - 240);
				closestSize = VideoSize.Qvga;
			}
			if (Math.Abs(width - 640) + Math.Abs(height - 480) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 640) + Math.Abs(height - 480);
				closestSize = VideoSize.Vga;
			}
			if (Math.Abs(width - 800) + Math.Abs(height - 600) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 800) + Math.Abs(height - 600);
				closestSize = VideoSize.Svga;
			}
			if (Math.Abs(width - 1024) + Math.Abs(height - 768) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 1024) + Math.Abs(height - 768);
				closestSize = VideoSize.Xga;
			}
			if (Math.Abs(width - 1600) + Math.Abs(height - 1200) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 1600) + Math.Abs(height - 1200);
				closestSize = VideoSize.Uxga;
			}
			if (Math.Abs(width - 2048) + Math.Abs(height - 1536) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 2048) + Math.Abs(height - 1536);
				closestSize = VideoSize.Qxga;
			}
			if (Math.Abs(width - 1280) + Math.Abs(height - 1024) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 1280) + Math.Abs(height - 1024);
				closestSize = VideoSize.Sxga;
			}
			if (Math.Abs(width - 2560) + Math.Abs(height - 2048) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 2560) + Math.Abs(height - 2048);
				closestSize = VideoSize.Qsxga;
			}
			if (Math.Abs(width - 5120) + Math.Abs(height - 4096) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 5120) + Math.Abs(height - 4096);
				closestSize = VideoSize.Hsxga;
			}
			if (Math.Abs(width - 852) + Math.Abs(height - 480) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 852) + Math.Abs(height - 480);
				closestSize = VideoSize.Wvga;
			}
			if (Math.Abs(width - 1366) + Math.Abs(height - 768) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 1366) + Math.Abs(height - 768);
				closestSize = VideoSize.Wxga;
			}
			if (Math.Abs(width - 1600) + Math.Abs(height - 1024) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 1600) + Math.Abs(height - 1024);
				closestSize = VideoSize.Wsxga;
			}
			if (Math.Abs(width - 1920) + Math.Abs(height - 1200) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 1920) + Math.Abs(height - 1200);
				closestSize = VideoSize.Wuxga;
			}
			if (Math.Abs(width - 2560) + Math.Abs(height - 1600) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 2560) + Math.Abs(height - 1600);
				closestSize = VideoSize.Woxga;
			}
			if (Math.Abs(width - 3200) + Math.Abs(height - 2048) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 3200) + Math.Abs(height - 2048);
				closestSize = VideoSize.Wqsxga;
			}
			if (Math.Abs(width - 3840) + Math.Abs(height - 2400) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 3840) + Math.Abs(height - 2400);
				closestSize = VideoSize.Wquxga;
			}
			if (Math.Abs(width - 6400) + Math.Abs(height - 4096) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 6400) + Math.Abs(height - 4096);
				closestSize = VideoSize.Whsxga;
			}
			if (Math.Abs(width - 7680) + Math.Abs(height - 4800) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 7680) + Math.Abs(height - 4800);
				closestSize = VideoSize.Whuxga;
			}
			if (Math.Abs(width - 320) + Math.Abs(height - 200) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 320) + Math.Abs(height - 200);
				closestSize = VideoSize.Cga;
			}
			if (Math.Abs(width - 640) + Math.Abs(height - 350) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 640) + Math.Abs(height - 350);
				closestSize = VideoSize.Ega;
			}
			if (Math.Abs(width - 852) + Math.Abs(height - 480) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 852) + Math.Abs(height - 480);
				closestSize = VideoSize.Hd480;
			}
			if (Math.Abs(width - 1280) + Math.Abs(height - 720) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 1280) + Math.Abs(height - 720);
				closestSize = VideoSize.Hd720;
			}
			if (Math.Abs(width - 1920) + Math.Abs(height - 1080) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 1920) + Math.Abs(height - 1080);
				closestSize = VideoSize.Hd1080;
			}
			if (Math.Abs(width - 2048) + Math.Abs(height - 1080) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 2048) + Math.Abs(height - 1080);
				closestSize = VideoSize._2K;
			}
			if (Math.Abs(width - 1998) + Math.Abs(height - 1080) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 1998) + Math.Abs(height - 1080);
				closestSize = VideoSize._2Kflat;
			}
			if (Math.Abs(width - 2048) + Math.Abs(height - 858) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 2048) + Math.Abs(height - 858);
				closestSize = VideoSize._2Kscope;
			}
			if (Math.Abs(width - 4096) + Math.Abs(height - 2160) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 4096) + Math.Abs(height - 2160);
				closestSize = VideoSize._4K;
			}
			if (Math.Abs(width - 3996) + Math.Abs(height - 2160) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 3996) + Math.Abs(height - 2160);
				closestSize = VideoSize._4Kflat;
			}
			if (Math.Abs(width - 4096) + Math.Abs(height - 1716) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 4096) + Math.Abs(height - 1716);
				closestSize = VideoSize._4Kscope;
			}
			if (Math.Abs(width - 640) + Math.Abs(height - 360) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 640) + Math.Abs(height - 360);
				closestSize = VideoSize.Nhd;
			}
			if (Math.Abs(width - 240) + Math.Abs(height - 160) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 240) + Math.Abs(height - 160);
				closestSize = VideoSize.Hqvga;
			}
			if (Math.Abs(width - 400) + Math.Abs(height - 240) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 400) + Math.Abs(height - 240);
				closestSize = VideoSize.Wqvga;
			}
			if (Math.Abs(width - 432) + Math.Abs(height - 240) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 432) + Math.Abs(height - 240);
				closestSize = VideoSize.Fwqvga;
			}
			if (Math.Abs(width - 480) + Math.Abs(height - 320) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 480) + Math.Abs(height - 320);
				closestSize = VideoSize.Hvga;
			}
			if (Math.Abs(width - 960) + Math.Abs(height - 540) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 960) + Math.Abs(height - 540);
				closestSize = VideoSize.Qhd;
			}
			if (Math.Abs(width - 2048) + Math.Abs(height - 1080) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 2048) + Math.Abs(height - 1080);
				closestSize = VideoSize._2Kdci;
			}
			if (Math.Abs(width - 4096) + Math.Abs(height - 2160) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 4096) + Math.Abs(height - 2160);
				closestSize = VideoSize._4Kdci;
			}
			if (Math.Abs(width - 3840) + Math.Abs(height - 2160) < smallestDelta)
			{
				smallestDelta = Math.Abs(width - 3840) + Math.Abs(height - 2160);
				closestSize = VideoSize.Uhd2160;
			}
			if (Math.Abs(width - 7680) + Math.Abs(height - 4320) < smallestDelta)
			{
				// don't need delta for the last check
				//smallestDelta = Math.Abs(width - 7680) + Math.Abs(height - 4320);
				closestSize = VideoSize.Uhd4320;
			}

			return closestSize;
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

		async private void ExportClip_Click(object sender, RoutedEventArgs e)
		{
			if (currentBitsRenderItem == 0)
			{
				MessageBox.Show("You need to render bits to export your clip.");
				return;
			}

			System.Windows.Forms.SaveFileDialog dialog = new System.Windows.Forms.SaveFileDialog
			{
				Filter = "MP4 (*.mp4)|*.mp4|WEBM (*.webm)|*.webm|GIF (*.gif)|*.gif",
				AddExtension = true,
				FilterIndex = 0,
				OverwritePrompt = true,
				RestoreDirectory = true,
				InitialDirectory = Path.GetDirectoryName(App.Settings.lastExportPath)
			};

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

				ffmpegProgressWindow = new ConversionProgressWindow
				{
					Owner = this
				};
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

				PrevRenderItemButton.Visibility = (currentBitsRenderItem > 0) ? Visibility.Visible : Visibility.Hidden;
				NextRenderItemButton.Visibility = (currentBitsRenderItem < bitsRenderItemList.Count - 1) ? Visibility.Visible : Visibility.Hidden;
				LoadCurrentRenderedBitButton.Visibility = (currentBitsRenderItem > 0) ? Visibility.Visible : Visibility.Hidden;

				bitSeekStartTime = new TimeSpan(0);
				bitSeekEndTime = new TimeSpan(0);

				// Update bit list
				bitList.Clear();
				foreach (var bit in renderItem.bitList)
					bitList.Add(new VideoBitItem(bitList, bit.mediaPath, bit.startTime, bit.endTime, bit.rate, bit.audioTrack, bit.muted, bit.subtitleTrack));

				if (currentBitsRenderItem > 0 && File.Exists(Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4")))
					LoadNewVideo(Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4"));
				else
					LoadNewVideo(bitsRenderItemList[currentBitsRenderItem].playingMediaPath);

				// Set selected audio track
				foreach (var audioTrack in playingMediaAudioTracks)
					if ((audioTrack.IsNull && renderItem.selectedAudio.IsNull) || (audioTrack.Track.Index == renderItem.selectedAudio.Track.Index))
						AudioTrackComboBox.SelectedItem = audioTrack;

				// Set selected subtitles track
				foreach (var subtitlesTrack in playingMediaSubtitleTracks)
					if ((subtitlesTrack.IsNull && renderItem.selectedSubtitles.IsNull) || (subtitlesTrack.Track.Index == renderItem.selectedSubtitles.Track.Index))
						SubtitleTrackComboBox.SelectedItem = subtitlesTrack;
			}
		}

		private void LoadCurrentRenderedBitButton_Click(object sender, RoutedEventArgs e)
		{
			if (currentBitsRenderItem > 0 && File.Exists(Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4")))
				LoadNewVideo(Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4"));
			else
				MessageBox.Show($"Could not load  video {Path.Combine(App.TempPath, "renders/", $"{currentBitsRenderItem}.mp4")}");
		}

		private void PrevRenderItemButton_Click(object sender, RoutedEventArgs e)
		{
			if (currentBitsRenderItem > 0)
			{
				LoadRenderItem(currentBitsRenderItem - 1);
			}
		}

		private void NextRenderItemButton_Click(object sender, RoutedEventArgs e)
		{
			if (currentBitsRenderItem < bitsRenderItemList.Count - 1)
			{
				LoadRenderItem(currentBitsRenderItem + 1);
			}
		}

	}
}
