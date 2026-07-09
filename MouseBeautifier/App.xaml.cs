using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using WinRT;

namespace MouseBeautifier
{
    public sealed partial class App : Application
    {
        private OverlayWindow? _overlay;
        private MainWindow? _main;
        private TrayIcon? _tray;
        private bool _shutting;

        public App()
        {
            // Replaces the App.xaml <ResourceDictionary><XamlControlsResources/></...>.
            this.Resources.MergedDictionaries.Add(new XamlControlsResources());
        }

        [STAThread]
        public static int Main(string[] args)
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start((p) =>
            {
                var app = new App();
            });
            return 0;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            SettingsManager.Load();

            _overlay = new OverlayWindow();
            _overlay.Activate();
            _overlay.Start();

            _main = new MainWindow();
            _main.Activate();

            _tray = new TrayIcon();
            _tray.ShowPanelRequested += () => _main.ShowOrActivate();
            _tray.ExitRequested += () => RequestExit();

            _overlay.Closed += (_, _) => RequestExit();
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
