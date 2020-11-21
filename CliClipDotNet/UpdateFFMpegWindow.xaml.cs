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
using System.Windows.Shapes;

using System.IO;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace CliClipDotNet
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

        async private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FFmpeg.SetExecutablesPath(App.Settings.ffmpegDirectory);
            if (Directory.Exists(App.Settings.ffmpegDirectory) == false)
                Directory.CreateDirectory(App.Settings.ffmpegDirectory);

            var progressFunc = new Progress<ProgressInfo>(info =>
           {
               double progressPercent = Convert.ToDouble(info.DownloadedBytes) / Convert.ToDouble(info.TotalBytes);
               updateTextBlock.Text = $"Updating FFMpeg ({info.DownloadedBytes}B/{info.TotalBytes}B)";
               updateProgressBar.Value = progressPercent;
           });

            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Full, App.Settings.ffmpegDirectory, progressFunc);
            this.Close();
        }
    }
}
