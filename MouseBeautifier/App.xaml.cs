using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using WinRT;

namespace MouseBeautifier
{
    public sealed partial class App : Application
    {
        private OverlayWindow? _overlay;
        private TrayIcon? _tray;
        private bool _shutting;

        internal static void Log(string msg)
        {
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "FunnyCursor");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "startup.log"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            }
            catch { }
        }

        public App()
        {
            // NOTE: The settings UI is a pure Win32 dialog (SettingsDialog), NOT a
            // WinUI XAML window. WinUI's themed controls need framework theme
            // resources that cannot be deployed in this self-contained build, so a
            // XAML settings window crashes the process on launch (COMException
            // 0x80004005). The Win32 dialog avoids that dependency entirely.
            UnhandledException += (_, e) => Log("App.UnhandledException: " + e.Exception);
        }

        [STAThread]
        public static int Main(string[] args)
        {
            Log("Main start");
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Log("UnhandledException: " + e.ExceptionObject);
            try
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                Log("ComWrappers initialized");
                Application.Start((p) =>
                {
                    Log("Application.Start callback -> new App()");
                    var app = new App();
                });
                Log("Application.Start returned");
            }
            catch (Exception ex)
            {
                Log("Main exception: " + ex);
                throw;
            }
            return 0;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            Log("OnLaunched start");
            try
            {
                SettingsManager.Load();
                Log("Settings loaded");

                _overlay = new OverlayWindow();
                Log("OverlayWindow constructed");
                _overlay.Activate();
                Log("Overlay activated");
                _overlay.Start();
                Log("Overlay started");

                // Settings UI is a pure Win32 dialog (WinUI XAML controls require
                // framework theme resources that cannot be deployed in this build).
                SettingsDialog.Show();
                Log("SettingsDialog shown");
                SettingsDialog.ExitRequested += () => RequestExit();

                _tray = new TrayIcon();
                _tray.ShowPanelRequested += () => SettingsDialog.Show();
                _tray.ExitRequested += () => RequestExit();
                Log("Tray created");

                _overlay.Closed += (_, _) => RequestExit();
                Log("OnLaunched done");
            }
            catch (Exception ex)
            {
                Log("OnLaunched exception: " + ex);
                throw;
            }
        }

        public void RequestExit()
        {
            if (_shutting) return;
            _shutting = true;
            try { _tray?.Dispose(); } catch { }
            Environment.Exit(0);
        }
    }
}
