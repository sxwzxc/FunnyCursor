using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using MouseBeautifier.Core;

namespace MouseBeautifier
{
    public static class SettingsManager
    {
        public static AppSettings Current { get; private set; } = new();

        public static event Action? Changed;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FunnyCursor", "settings.json");

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "FunnyCursor";

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null) Current = loaded;
                }
            }
            catch
            {
                Current = new AppSettings();
            }
            ApplyStartup();
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* best effort */ }
            ApplyStartup();
            Changed?.Invoke();
        }

        private static void ApplyStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null) return;
                if (Current.StartWithWindows)
                {
                    var exe = Environment.ProcessPath ?? "";
                    if (!string.IsNullOrEmpty(exe))
                        key.SetValue(AppName, exe);
                }
                else if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch { /* no admin / locked */ }
        }
    }

}
