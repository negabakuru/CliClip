﻿using System;
using System.Windows;

using Newtonsoft.Json;
using System.IO;
using Xabe.FFmpeg;

namespace CliClip
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		public class AppSettings
		{
			public bool autoUpdateFfmpeg;
			public string ffmpegDirectory;

			public string lastOpenVideoPath;
			public string lastExportPath;
		}

		static private string AppDataPath;
		static public string TempPath { get; protected set; }
		static private string SettingsFilePath;
		static public AppSettings Settings { get; set; }
		string[] args;

		private void Application_Startup(object sender, StartupEventArgs e)
		{
			args = e.Args;
			InitApp();

			MainWindow mainWindow = new MainWindow();
			mainWindow.Show();
			if (Settings.autoUpdateFfmpeg)
			{
				UpdateFFMpegWindow updateFFmpegWindow = new UpdateFFMpegWindow();
				updateFFmpegWindow.ShowDialog();
			}

			if (args.Length > 0)
				mainWindow.LoadNewVideo(args[0]);
		}

		private void InitApp()
		{
			// Create/Load settings
			TempPath = Path.Combine(Path.GetTempPath(), @"CliClip\");
			AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CliClip");
			SettingsFilePath = Path.Combine(AppDataPath, "settings.json");

			if (Directory.Exists(AppDataPath) == false)
				Directory.CreateDirectory(AppDataPath);

			Settings = new AppSettings
			{
				autoUpdateFfmpeg = false,
				ffmpegDirectory = @"C:\ffmpeg\bin"
			};
			LoadSettings();
			if (File.Exists(SettingsFilePath) == false)
				SaveSettings();

			// Set up ffmpeg
			FFmpeg.SetExecutablesPath(Settings.ffmpegDirectory);
		}

		static public bool LoadSettings()
		{
			// read JSON directly from a file
			if (File.Exists(SettingsFilePath))
			{
				var serializer = new JsonSerializer();
				using (StreamReader file = File.OpenText(SettingsFilePath))
				{
					Settings = (AppSettings)serializer.Deserialize(file, typeof(AppSettings));
				}

				return true;
			}
			return false;
		}

		static public bool SaveSettings()
		{
			if (Directory.Exists(AppDataPath) == false)
				Directory.CreateDirectory(AppDataPath);

			// serialize JSON directly to a file
			using (StreamWriter file = File.CreateText(SettingsFilePath))
			{
				JsonSerializer serializer = new JsonSerializer();
				serializer.Serialize(file, Settings);
			}

			return true;
		}
	}
}
