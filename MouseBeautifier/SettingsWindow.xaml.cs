using Microsoft.UI.Windowing;
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
        private AppWindow? _appWindow;
        private IntPtr _windowHandle;
        private bool _enforcingMinimumSize;
        private bool _loading = true;

        public event Action? ExitRequested;

        public SettingsWindow(ISettingsService settingsService)
        {
            _settingsService = settingsService ??
                throw new ArgumentNullException(nameof(settingsService));
            InitializeComponent();
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
                SetComboValue(IconTypeCombo, s.IconType);
                IconSizeSlider.Value = s.IconSize;
                SetColor(IconColorText, IconColorPicker, s.IconColor);
                SetColor(RopeColorText, RopeColorPicker, s.RopeColor);
                CustomIconPathText.Text = s.CustomIconPath;

                EnableTrailToggle.IsOn = s.EnableTrail;
                SetColor(TrailColorText, TrailColorPicker, s.TrailColor);
                TrailLengthSlider.Value = s.TrailLength;
                TrailWidthSlider.Value = s.TrailWidth;

                EnableOrbitToggle.IsOn = s.EnableOrbit;
                OrbitCountSlider.Value = s.OrbitCount;
                OrbitRadiusSlider.Value = s.OrbitRadius;
                OrbitSpeedSlider.Value = s.OrbitSpeed;
                OrbitSizeSlider.Value = s.OrbitSize;
                SetColor(OrbitColorText, OrbitColorPicker, s.OrbitColor);

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

        private static string NormalizeColor(TextBox textBox)
        {
            Color color = ColorsUtil.Parse(textBox.Text);
            string normalized = ToHex(color);
            textBox.Text = normalized;
            return normalized;
        }

        private static string ToHex(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
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
            s.ClickColor = NormalizeColor(ClickColorText);
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
            s.IconType = GetComboValue(IconTypeCombo, "star");
            s.IconSize = IconSizeSlider.Value;
            s.IconColor = NormalizeColor(IconColorText);
            s.RopeColor = NormalizeColor(RopeColorText);
            s.CustomIconPath = CustomIconPathText.Text.Trim();

            s.EnableTrail = EnableTrailToggle.IsOn;
            s.TrailColor = NormalizeColor(TrailColorText);
            s.TrailLength = TrailLengthSlider.Value;
            s.TrailWidth = TrailWidthSlider.Value;

            s.EnableOrbit = EnableOrbitToggle.IsOn;
            s.OrbitCount = (int)Math.Round(OrbitCountSlider.Value);
            s.OrbitRadius = OrbitRadiusSlider.Value;
            s.OrbitSpeed = OrbitSpeedSlider.Value;
            s.OrbitSize = OrbitSizeSlider.Value;
            s.OrbitColor = NormalizeColor(OrbitColorText);

            s.EnableGlow = EnableGlowToggle.IsOn;
            s.GlowColor = NormalizeColor(GlowColorText);
            s.GlowSize = GlowSizeSlider.Value;
            s.GlowIntensity = GlowIntensitySlider.Value;

            s.StartWithWindows = StartWithWindowsToggle.IsOn;
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
                "OrbitColor" => OrbitColorText,
                "GlowColor" => GlowColorText,
                _ => null,
            };

            if (target != null)
            {
                target.Text = ToHex(args.NewColor);
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
