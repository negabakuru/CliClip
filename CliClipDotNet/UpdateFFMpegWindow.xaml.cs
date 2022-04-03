using System;
using System.Windows;

using System.IO;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace CliClip
{
	/// <summary>
	/// Interaction logic for UpdateFFMpegWindow.xaml
	/// </summary>
	public partial class UpdateFFMpegWindow : Window
	{
		public UpdateFFMpegWindow()
		{
			InitializeComponent();
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			UpdateFFMpeg();
		}

		async private void UpdateFFMpeg()
		{
			FFmpeg.SetExecutablesPath(App.Settings.ffmpegDirectory);
			if (Directory.Exists(App.Settings.ffmpegDirectory) == false)
				Directory.CreateDirectory(App.Settings.ffmpegDirectory);

			var progressFunc = new Progress<ProgressInfo>(OnDownloadProgress);

			await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, App.Settings.ffmpegDirectory, progressFunc);
			this.Close();
		}

		private void OnDownloadProgress(ProgressInfo info)
		{
			double progressPercent = Convert.ToDouble(info.DownloadedBytes) / Convert.ToDouble(info.TotalBytes);
			updateTextBlock.Text = $"Updating FFMpeg ({info.DownloadedBytes}B/{info.TotalBytes}B)";
			updateProgressBar.Value = progressPercent;
		}
	}
}
