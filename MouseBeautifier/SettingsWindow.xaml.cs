using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using MouseBeautifier.Core;
using System;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace MouseBeautifier
{
    public sealed partial class SettingsWindow : Window
    {
        private const int MinimumWindowWidthDips = 760;
        private const int MinimumWindowHeightDips = 620;
        private const int InitialWindowWidthDips = 980;
        private const int InitialWindowHeightDips = 820;
        private readonly ISettingsService _settingsService;
        private readonly DispatcherQueueTimer _saveTimer;
        private AppWindow? _appWindow;
        private IntPtr _windowHandle;
        private bool _enforcingMinimumSize;
        private bool _loading = true;
        private bool _savePending;

        public event Action? ExitRequested;

        public SettingsWindow(ISettingsService settingsService)
        {
            _settingsService = settingsService ??
                throw new ArgumentNullException(nameof(settingsService));
            InitializeComponent();
            _saveTimer = DispatcherQueue.CreateTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(250);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += SaveTimer_Tick;
            SettingsNavigation.SelectedItem = SettingsNavigation.MenuItems[0];
            ShowSettingsSection("ClickEffects");
            ConfigureWindow();
            Closed += SettingsWindow_Closed;
            LoadSettings();
            _loading = false;
        }

        private void ConfigureWindow()
        {
            try
            {
                _windowHandle = WindowNative.GetWindowHandle(this);
                var windowId =
                    Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
                        _windowHandle);
                _appWindow = AppWindow.GetFromWindowId(windowId);
                uint dpi = GetWindowDpi();
                _appWindow.Resize(new SizeInt32(
                    DipsToPixels(InitialWindowWidthDips, dpi),
                    DipsToPixels(InitialWindowHeightDips, dpi)));
                _appWindow.SetIcon("Assets\\funnycursor.ico");
                _appWindow.Changed += AppWindow_Changed;
            }
            catch (Exception ex)
            {
                App.Log("SettingsWindow.ConfigureWindow: " + ex.Message);
            }
        }

        private void AppWindow_Changed(
            AppWindow sender,
            AppWindowChangedEventArgs args)
        {
            if (!args.DidSizeChange || _enforcingMinimumSize)
            {
                return;
            }

            SizeInt32 current = sender.Size;
            uint dpi = GetWindowDpi();
            int minimumWidth =
                DipsToPixels(MinimumWindowWidthDips, dpi);
            int minimumHeight =
                DipsToPixels(MinimumWindowHeightDips, dpi);
            int width = Math.Max(current.Width, minimumWidth);
            int height = Math.Max(current.Height, minimumHeight);
            if (width == current.Width && height == current.Height)
            {
                return;
            }

            try
            {
                _enforcingMinimumSize = true;
                sender.Resize(new SizeInt32(width, height));
            }
            finally
            {
                _enforcingMinimumSize = false;
            }
        }

        private uint GetWindowDpi()
        {
            if (_windowHandle == IntPtr.Zero)
            {
                return 96;
            }

            uint dpi = NativeMethods.GetDpiForWindow(_windowHandle);
            return dpi == 0 ? 96u : dpi;
        }

        private static int DipsToPixels(int dips, uint dpi)
        {
            return Math.Max(
                1,
                (int)Math.Round(dips * dpi / 96d));
        }

        private void SettingsWindow_Closed(
            object sender,
            WindowEventArgs args)
        {
            Closed -= SettingsWindow_Closed;
            _saveTimer.Stop();
            _saveTimer.Tick -= SaveTimer_Tick;
            SavePendingSettings();
            if (_appWindow != null)
            {
                _appWindow.Changed -= AppWindow_Changed;
                _appWindow = null;
            }

            _windowHandle = IntPtr.Zero;
        }

        private void LoadSettings()
        {
            bool wasLoading = _loading;
            _loading = true;
            try
            {
                AppSettings s = _settingsService.Current;

                EnableClickEffectsToggle.IsOn = s.EnableClickEffects;
                SetComboValue(ClickPresetCombo, s.ClickPreset);
                SetColor(ClickColorText, ClickColorPicker, s.ClickColor);
                ClickParticleCountSlider.Value = s.ClickParticleCount;
                ClickSpeedSlider.Value = s.ClickSpeed;
                ClickGravitySlider.Value = s.ClickGravity;

                EnableRopeToggle.IsOn = s.EnableRope;
                RopeLengthSlider.Value = s.RopeLength;
                RopeSegmentsSlider.Value = s.RopeSegments;
                RopeGravitySlider.Value = s.RopeGravity;
                RopeDampingSlider.Value = s.RopeDamping;
                RopeStiffnessSlider.Value = s.RopeStiffness;
                RopeWidthSlider.Value = s.RopeWidth;
                SetComboValue(RopeStyleCombo, s.RopeStyle);
                SetComboValue(IconTypeCombo, s.IconType);
                IconSizeSlider.Value = s.IconSize;
                SetColor(IconColorText, IconColorPicker, s.IconColor);
                SetColor(RopeColorText, RopeColorPicker, s.RopeColor);
                CustomIconPathText.Text = s.CustomIconPath;

                EnableTrailToggle.IsOn = s.EnableTrail;
                SetColor(TrailColorText, TrailColorPicker, s.TrailColor);
                TrailLengthSlider.Value = s.TrailLength;
                TrailWidthSlider.Value = s.TrailWidth;

                NebulaSettings nebula =
                    s.Nebula ??= new NebulaSettings();
                EnableOrbitToggle.IsOn = nebula.Enabled;
                OrbitCountSlider.Value = nebula.ParticleCount;
                OrbitRadiusSlider.Value = nebula.Radius;
                OrbitSpeedSlider.Value = nebula.AngularSpeed;
                OrbitStarSizeSlider.Value = nebula.StarSize;
                SetRgbColor(
                    OrbitParticleColorText,
                    OrbitParticleColorPicker,
                    nebula.ParticleColor);
                OrbitParticleOpacitySlider.Value =
                    nebula.ParticleOpacity;
                SetRgbColor(
                    OrbitCloudColorText,
                    OrbitCloudColorPicker,
                    nebula.CloudColor);
                OrbitCloudOpacitySlider.Value = nebula.CloudOpacity;
                OrbitTrailOpacitySlider.Value = nebula.TrailOpacity;
                SetRgbColor(
                    OrbitStrokeColorText,
                    OrbitStrokeColorPicker,
                    nebula.StrokeColor);
                OrbitStrokeWidthSlider.Value = nebula.StrokeWidth;
                OrbitStrokeOpacitySlider.Value = nebula.StrokeOpacity;
                SetRgbColor(
                    OrbitHaloColorText,
                    OrbitHaloColorPicker,
                    nebula.HaloColor);
                OrbitHaloOpacitySlider.Value = nebula.HaloOpacity;
                OrbitHaloSizeSlider.Value = nebula.HaloSize;

                EnableGlowToggle.IsOn = s.EnableGlow;
                SetColor(GlowColorText, GlowColorPicker, s.GlowColor);
                GlowSizeSlider.Value = s.GlowSize;
                GlowIntensitySlider.Value = s.GlowIntensity;

                StartWithWindowsToggle.IsOn = s.StartWithWindows;
                ProductText.Text = AppInfo.Product;
                VersionText.Text = $"版本：{AppInfo.Version}";
                AuthorText.Text = $"作者：{AppInfo.Author}";
                CopyrightText.Text = AppInfo.Copyright;
            }
            finally
            {
                _loading = wasLoading;
            }
        }

        private static void SetComboValue(ComboBox comboBox, string value)
        {
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), value, StringComparison.Ordinal))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            comboBox.SelectedIndex = 0;
        }

        private static string GetComboValue(ComboBox comboBox, string fallback)
        {
            return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
        }

        private static void SetColor(TextBox textBox, ColorPicker picker, string value)
        {
            Color color = ColorsUtil.Parse(value);
            string normalized = ToHex(color);
            textBox.Text = normalized;
            picker.Color = color;
        }

        private static void SetRgbColor(
            TextBox textBox,
            ColorPicker picker,
            string value)
        {
            Color color = ColorsUtil.Parse(value);
            color.A = 255;
            textBox.Text = ToRgbHex(color);
            picker.Color = color;
        }

        private static string NormalizeColor(
            TextBox textBox,
            ColorPicker picker)
        {
            Color color = ColorsUtil.Parse(textBox.Text);
            string normalized = ToHex(color);
            textBox.Text = normalized;
            picker.Color = color;
            return normalized;
        }

        private static string NormalizeRgbColor(
            TextBox textBox,
            ColorPicker picker)
        {
            string normalized = HexColor.NormalizeRgb(
                textBox.Text,
                ToRgbHex(picker.Color));
            Color color = ColorsUtil.Parse(normalized);
            color.A = 255;
            textBox.Text = normalized;
            picker.Color = color;
            return normalized;
        }

        private static string ToHex(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private static string ToRgbHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private void SettingsNavigation_SelectionChanged(
            NavigationView sender,
            NavigationViewSelectionChangedEventArgs args)
        {
            string? section = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
            ShowSettingsSection(section);
        }

        private void ShowSettingsSection(string? section)
        {
            ClickEffectsSection.Visibility =
                section == "ClickEffects" ? Visibility.Visible : Visibility.Collapsed;
            RopeSection.Visibility =
                section == "Rope" ? Visibility.Visible : Visibility.Collapsed;
            TrailSection.Visibility =
                section == "Trail" ? Visibility.Visible : Visibility.Collapsed;
            OrbitSection.Visibility =
                section == "Orbit" ? Visibility.Visible : Visibility.Collapsed;
            GlowSection.Visibility =
                section == "Glow" ? Visibility.Visible : Visibility.Collapsed;
            GeneralSection.Visibility =
                section == "General" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CommitSettings()
        {
            if (_loading)
            {
                return;
            }

            AppSettings s = _settingsService.Current;

            s.EnableClickEffects = EnableClickEffectsToggle.IsOn;
            s.ClickPreset = GetComboValue(ClickPresetCombo, "sparkle");
            s.ClickColor = NormalizeColor(
                ClickColorText,
                ClickColorPicker);
            s.ClickParticleCount = (int)Math.Round(ClickParticleCountSlider.Value);
            s.ClickSpeed = ClickSpeedSlider.Value;
            s.ClickGravity = ClickGravitySlider.Value;

            s.EnableRope = EnableRopeToggle.IsOn;
            s.RopeLength = RopeLengthSlider.Value;
            s.RopeSegments = (int)Math.Round(RopeSegmentsSlider.Value);
            s.RopeGravity = RopeGravitySlider.Value;
            s.RopeDamping = RopeDampingSlider.Value;
            s.RopeStiffness = RopeStiffnessSlider.Value;
            s.RopeWidth = RopeWidthSlider.Value;
            s.RopeStyle = GetComboValue(RopeStyleCombo, "neon");
            s.IconType = GetComboValue(IconTypeCombo, "star");
            s.IconSize = IconSizeSlider.Value;
            s.IconColor = NormalizeColor(
                IconColorText,
                IconColorPicker);
            s.RopeColor = NormalizeColor(
                RopeColorText,
                RopeColorPicker);
            s.CustomIconPath = CustomIconPathText.Text.Trim();

            s.EnableTrail = EnableTrailToggle.IsOn;
            s.TrailColor = NormalizeColor(
                TrailColorText,
                TrailColorPicker);
            s.TrailLength = TrailLengthSlider.Value;
            s.TrailWidth = TrailWidthSlider.Value;

            NebulaSettings nebula =
                s.Nebula ??= new NebulaSettings();
            nebula.Enabled = EnableOrbitToggle.IsOn;
            nebula.ParticleCount =
                (int)Math.Round(OrbitCountSlider.Value);
            nebula.Radius = OrbitRadiusSlider.Value;
            nebula.AngularSpeed = OrbitSpeedSlider.Value;
            nebula.StarSize = OrbitStarSizeSlider.Value;
            nebula.ParticleColor = NormalizeRgbColor(
                OrbitParticleColorText,
                OrbitParticleColorPicker);
            nebula.ParticleOpacity =
                OrbitParticleOpacitySlider.Value;
            nebula.CloudColor = NormalizeRgbColor(
                OrbitCloudColorText,
                OrbitCloudColorPicker);
            nebula.CloudOpacity = OrbitCloudOpacitySlider.Value;
            nebula.TrailOpacity = OrbitTrailOpacitySlider.Value;
            nebula.StrokeColor = NormalizeRgbColor(
                OrbitStrokeColorText,
                OrbitStrokeColorPicker);
            nebula.StrokeWidth = OrbitStrokeWidthSlider.Value;
            nebula.StrokeOpacity = OrbitStrokeOpacitySlider.Value;
            nebula.HaloColor = NormalizeRgbColor(
                OrbitHaloColorText,
                OrbitHaloColorPicker);
            nebula.HaloOpacity = OrbitHaloOpacitySlider.Value;
            nebula.HaloSize = OrbitHaloSizeSlider.Value;
            nebula.Normalize();

            s.EnableGlow = EnableGlowToggle.IsOn;
            s.GlowColor = NormalizeColor(
                GlowColorText,
                GlowColorPicker);
            s.GlowSize = GlowSizeSlider.Value;
            s.GlowIntensity = GlowIntensitySlider.Value;

            s.StartWithWindows = StartWithWindowsToggle.IsOn;
            ScheduleSave();
        }

        private void ScheduleSave()
        {
            _savePending = true;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveTimer_Tick(
            DispatcherQueueTimer sender,
            object args)
        {
            sender.Stop();
            SavePendingSettings();
        }

        private void SavePendingSettings()
        {
            if (!_savePending)
            {
                return;
            }

            _savePending = false;
            _settingsService.Save();
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            CommitSettings();
        }

        private void SettingChanged(object sender, SelectionChangedEventArgs e)
        {
            CommitSettings();
        }

        private void SettingChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            CommitSettings();
        }

        private void ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            if (_loading)
            {
                return;
            }

            TextBox? target = sender.Tag?.ToString() switch
            {
                "ClickColor" => ClickColorText,
                "IconColor" => IconColorText,
                "RopeColor" => RopeColorText,
                "TrailColor" => TrailColorText,
                "OrbitParticleColor" => OrbitParticleColorText,
                "OrbitCloudColor" => OrbitCloudColorText,
                "OrbitStrokeColor" => OrbitStrokeColorText,
                "OrbitHaloColor" => OrbitHaloColorText,
                "GlowColor" => GlowColorText,
                _ => null,
            };

            if (target != null)
            {
                bool isNebulaRgb =
                    sender.Tag?.ToString()?.StartsWith(
                        "Orbit",
                        StringComparison.Ordinal) == true;
                target.Text = isNebulaRgb
                    ? ToRgbHex(args.NewColor)
                    : ToHex(args.NewColor);
                CommitSettings();
            }
        }

        private async void BrowseIconButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".gif");
                picker.FileTypeFilter.Add(".bmp");
                picker.FileTypeFilter.Add(".webp");

                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    CustomIconPathText.Text = file.Path;
                    SetComboValue(IconTypeCombo, "custom");
                    CommitSettings();
                }
            }
            catch (Exception ex)
            {
                App.Log("SettingsWindow.BrowseIcon: " + ex);
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _saveTimer.Stop();
            _savePending = false;
            _settingsService.Reset();
            LoadSettings();
        }

        private void CopyVersionButton_Click(object sender, RoutedEventArgs e)
        {
            var package = new DataPackage();
            package.SetText($"FunnyCursor v{AppInfo.Version}");
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }

        private void OpenRepositoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppInfo.RepositoryUrl,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                App.Log("SettingsWindow.OpenRepository: " + ex.Message);
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            ExitRequested?.Invoke();
        }
    }
}
