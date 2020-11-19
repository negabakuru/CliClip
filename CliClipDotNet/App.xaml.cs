using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

using LibVLCSharp.Shared;
using Xabe.FFmpeg;

namespace CliClipDotNet
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        static public string ffmpegDirectory = @"C:\ffmpeg\bin";
        public static LibVLC VLC { get; protected set; }
        string[] args;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            args = e.Args;
            LibVLCSharp.Shared.Core.Initialize();
            VLC = new LibVLC();

            FFmpeg.SetExecutablesPath(ffmpegDirectory);

            MainWindow mainWindow = new MainWindow();
            UpdateFFMpegWindow updateFFmpegWindow = new UpdateFFMpegWindow();
            mainWindow.Show();
            updateFFmpegWindow.ShowDialog();

            if (args.Length > 0 && mainWindow.LoadMediaFromPath(args[0]))
                mainWindow.LoadNewVideo(mainWindow.baseMedia);
        }
    }
}
