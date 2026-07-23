using Microsoft.Win32;
using MouseBeautifier.Core;
using System;
using System.IO;

namespace MouseBeautifier
{
    internal sealed class JsonSettingsService : ISettingsService
    {
        private const string RunKey =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "FunnyCursor";
        private readonly string _filePath;

        public JsonSettingsService(string? filePath = null)
        {
            _filePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "FunnyCursor",
                "settings.json");
        }

        public AppSettings Current { get; private set; } = new();

        public event EventHandler? Changed;

        public void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    Current = AppSettingsJson.Deserialize(json);
                }
                else
                {
                    Current = new AppSettings();
                }
            }
            catch (Exception ex)
            {
                App.Log("Settings load failed: " + ex.Message);
                Current = new AppSettings();
            }

            Current.Normalize();
            ApplyStartupRegistration();
        }

        public void Save()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temporaryPath = _filePath + ".tmp";
                File.WriteAllText(
                    temporaryPath,
                    AppSettingsJson.Serialize(Current));
                File.Move(
                    temporaryPath,
                    _filePath,
                    overwrite: true);
            }
            catch (Exception ex)
            {
                App.Log("Settings save failed: " + ex.Message);
            }

            ApplyStartupRegistration();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Reset()
        {
            Current.Reset();
            Save();
        }

        private void ApplyStartupRegistration()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (key == null)
                {
                    return;
                }

                if (Current.StartWithWindows)
                {
                    string executable = Environment.ProcessPath ?? "";
                    if (!string.IsNullOrEmpty(executable))
                    {
                        key.SetValue(AppName, $"\"{executable}\"");
                    }
                }
                else if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                App.Log("Startup registration failed: " + ex.Message);
            }
        }
    }
}
