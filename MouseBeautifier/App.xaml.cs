using Microsoft.UI.Xaml;
using MouseBeautifier.Core;
using System;
using System.IO;
using System.Threading;

namespace MouseBeautifier
{
    public sealed partial class App : Application
    {
        private readonly ISettingsService _settingsService =
            new JsonSettingsService();
        private OverlayHost? _overlay;
        private TrayIcon? _tray;
        private SettingsWindow? _settingsWindow;
        private Mutex? _singleInstance;
        private bool _ownsSingleInstance;
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
            try
            {
                _singleInstance = new Mutex(
                    true,
                    @"Local\FunnyCursor.MouseBeautifier.SingleInstance",
                    out _ownsSingleInstance);
            }
            catch (Exception ex)
            {
                Log("Single-instance mutex: " + ex);
                _singleInstance?.Dispose();
                _singleInstance = null;
                _ownsSingleInstance = true;
            }

            InitializeComponent();
            UnhandledException += (_, e) => Log("App.UnhandledException: " + e.Exception);
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            Log("OnLaunched start");
            if (!_ownsSingleInstance)
            {
                Log("Another FunnyCursor instance is already running");
                RequestExit();
                return;
            }

            try
            {
                _settingsService.Load();
                Log("Settings loaded");

                _overlay = new OverlayHost(_settingsService);
                Log("OverlayHost created");
                _overlay.Start();
                Log("Overlay started");

                ShowSettingsWindow();
                Log("SettingsWindow shown");

                _tray = new TrayIcon();
                _tray.ShowPanelRequested += ShowSettingsWindow;
                _tray.ExitRequested += RequestExit;
                Log("Tray created");

                Log("OnLaunched done");
            }
            catch (Exception ex)
            {
                Log("OnLaunched exception: " + ex);
                RequestExit();
            }
        }

        private void ShowSettingsWindow()
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow(_settingsService);
                _settingsWindow.ExitRequested += RequestExit;
                _settingsWindow.Closed += OnSettingsWindowClosed;
            }

            _settingsWindow.Activate();
        }

        private void OnSettingsWindowClosed(
            object sender,
            WindowEventArgs args)
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.ExitRequested -= RequestExit;
                _settingsWindow.Closed -= OnSettingsWindowClosed;
                _settingsWindow = null;
            }
        }

        public void RequestExit()
        {
            if (_shutting) return;
            _shutting = true;

            try
            {
                if (_tray != null)
                {
                    _tray.ShowPanelRequested -= ShowSettingsWindow;
                    _tray.ExitRequested -= RequestExit;
                    _tray.Dispose();
                    _tray = null;
                }
            }
            catch (Exception ex)
            {
                Log("Shutdown tray: " + ex);
            }

            try
            {
                _overlay?.Dispose();
                _overlay = null;
            }
            catch (Exception ex)
            {
                Log("Shutdown overlay: " + ex);
            }

            try
            {
                _settingsService.Save();
            }
            catch (Exception ex)
            {
                Log("Shutdown settings save: " + ex);
            }

            try
            {
                if (_settingsWindow != null)
                {
                    SettingsWindow window = _settingsWindow;
                    _settingsWindow = null;
                    window.ExitRequested -= RequestExit;
                    window.Closed -= OnSettingsWindowClosed;
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                Log("Shutdown settings window: " + ex);
            }

            try
            {
                if (_ownsSingleInstance)
                {
                    _singleInstance?.ReleaseMutex();
                    _ownsSingleInstance = false;
                }

                _singleInstance?.Dispose();
                _singleInstance = null;
            }
            catch (Exception ex)
            {
                Log("Shutdown mutex: " + ex);
            }

            Log("Shutdown complete");
            Exit();
        }
    }
}
