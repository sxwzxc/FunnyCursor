using System;
using System.Globalization;
using System.Reflection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT;
using WinRT.Interop;

namespace MouseBeautifier
{
    public sealed partial class MainWindow : Window
    {
        // toggles
        private ToggleSwitch tgClick = null!;
        private ToggleSwitch tgRope = null!;
        private ToggleSwitch tgTrail = null!;
        private ToggleSwitch tgGlow = null!;
        private ToggleSwitch tgOrbit = null!;
        private ToggleSwitch tgStartup = null!;

        // combos
        private ComboBox cbClickPreset = null!;
        private ComboBox cbIconType = null!;

        // color / text
        private TextBox tbClickColor = null!;
        private TextBox tbIconColor = null!;
        private TextBox tbRopeColor = null!;
        private TextBox tbTrailColor = null!;
        private TextBox tbGlowColor = null!;
        private TextBox tbOrbitColor = null!;
        private TextBox tbIconPath = null!;

        // sliders
        private Slider slClickCount = null!;
        private Slider slClickSpeed = null!;
        private Slider slClickGravity = null!;
        private Slider slRopeLen = null!;
        private Slider slRopeSeg = null!;
        private Slider slRopeGrav = null!;
        private Slider slRopeDamp = null!;
        private Slider slRopeStiff = null!;
        private Slider slIconSize = null!;
        private Slider slRopeWidth = null!;
        private Slider slTrailLen = null!;
        private Slider slTrailWidth = null!;
        private Slider slGlowSize = null!;
        private Slider slGlowInt = null!;
        private Slider slOrbitCount = null!;
        private Slider slOrbitRadius = null!;
        private Slider slOrbitSpeed = null!;
        private Slider slOrbitSize = null!;

        // button
        private Button btnBrowse = null!;

        private bool _exiting;
        private IntPtr _hwnd;

        public MainWindow()
        {
            _hwnd = WindowNative.GetWindowHandle(this);
            this.Title = "MouseBeautifier 设置";
            this.AppWindow.ResizeClient(new SizeInt32(480, 720));

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var root = new StackPanel { Spacing = 0, Padding = new Thickness(16) };
            scroll.Content = root;
            this.Content = scroll;

            BuildClickCard(root);
            BuildRopeCard(root);
            BuildTrailCard(root);
            BuildOrbitCard(root);
            BuildGlowCard(root);
            BuildGeneralCard(root);

            SyncFromSettings();

            this.AppWindow.Closing += OnClosing;
        }

        // ---------------- Card builders ----------------
        private Border Card(string title, out StackPanel body)
        {
            var header = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 30, 60, 90)),
                Margin = new Thickness(0, 0, 0, 8),
            };
            body = new StackPanel { Spacing = 8 };
            var inner = new StackPanel { Spacing = 10 };
            inner.Children.Add(header);
            inner.Children.Add(body);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 245, 248, 252)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 220, 228, 238)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12),
            };
            border.Child = inner;
            return border;
        }

        private void BuildClickCard(StackPanel root)
        {
            var card = Card("点击特效", out var body);

            body.Children.Add(ToggleRow("EnableClickEffects", "启用点击特效", out tgClick));
            body.Children.Add(ComboRow("ClickPreset", "特效预设", out cbClickPreset,
                ("sparkle", "闪烁粒子"), ("confetti", "彩色纸屑"), ("ring", "扩散光环"), ("ripple", "水波纹")));
            body.Children.Add(TextRow("ClickColor", "特效颜色 (#RRGGBB)", out tbClickColor));
            body.Children.Add(SliderRow("ClickParticleCount", "粒子数量", 1, 200, 1, out slClickCount));
            body.Children.Add(SliderRow("ClickSpeed", "喷射速度", 50, 2000, 10, out slClickSpeed));
            body.Children.Add(SliderRow("ClickGravity", "重力", 0, 3000, 10, out slClickGravity));

            root.Children.Add(card);
        }

        private void BuildRopeCard(StackPanel root)
        {
            var card = Card("悬挂绳子 + 图标", out var body);

            body.Children.Add(ToggleRow("EnableRope", "启用绳子", out tgRope));
            body.Children.Add(SliderRow("RopeLength", "绳子长度", 20, 500, 5, out slRopeLen));
            body.Children.Add(SliderRow("RopeSegments", "绳子节数", 2, 40, 1, out slRopeSeg));
            body.Children.Add(SliderRow("RopeGravity", "重力", 0, 3000, 10, out slRopeGrav));
            body.Children.Add(SliderRow("RopeDamping", "阻尼", 0.5, 0.99, 0.01, out slRopeDamp));
            body.Children.Add(SliderRow("RopeStiffness", "刚度", 0.1, 1, 0.05, out slRopeStiff));

            body.Children.Add(ComboRow("IconType", "悬挂图标", out cbIconType,
                ("star", "五角星"), ("circle", "圆形"), ("square", "方形"), ("triangle", "三角"),
                ("diamond", "菱形"), ("heart", "心形"), ("smiley", "笑脸"),
                ("pig", "🐷 粉色小猪"), ("girl", "👧 二次元女孩"), ("custom", "自定义图片")));

            body.Children.Add(SliderRow("IconSize", "图标大小", 10, 120, 1, out slIconSize));
            body.Children.Add(TextRow("IconColor", "图标颜色 (#RRGGBB)", out tbIconColor));
            body.Children.Add(TextRow("RopeColor", "绳子颜色 (#RRGGBB)", out tbRopeColor));
            body.Children.Add(SliderRow("RopeWidth", "绳子粗细", 1, 12, 0.5, out slRopeWidth));

            var pathLabel = new TextBlock { Text = "自定义图标路径 (PNG / SVG / GIF，支持透明)", Margin = new Thickness(0, 4, 0, 2) };
            var pathRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            tbIconPath = new TextBox { Tag = "CustomIconPath", Width = 250, IsEnabled = false };
            tbIconPath.TextChanged += Commit;
            btnBrowse = new Button { Content = "浏览…", IsEnabled = false };
            btnBrowse.Click += btnBrowse_Click;
            pathRow.Children.Add(tbIconPath);
            pathRow.Children.Add(btnBrowse);
            body.Children.Add(pathLabel);
            body.Children.Add(pathRow);

            root.Children.Add(card);
        }

        private void BuildTrailCard(StackPanel root)
        {
            var card = Card("光标拖尾", out var body);

            body.Children.Add(ToggleRow("EnableTrail", "启用拖尾", out tgTrail));
            body.Children.Add(TextRow("TrailColor", "拖尾颜色 (#RRGGBB)", out tbTrailColor));
            body.Children.Add(SliderRow("TrailLength", "拖尾长度 (秒)", 0.1, 2, 0.05, out slTrailLen));
            body.Children.Add(SliderRow("TrailWidth", "拖尾宽度", 1, 20, 0.5, out slTrailWidth));

            root.Children.Add(card);
        }

        private void BuildOrbitCard(StackPanel root)
        {
            var card = Card("环绕粒子", out var body);

            body.Children.Add(ToggleRow("EnableOrbit", "启用环绕粒子", out tgOrbit));
            body.Children.Add(SliderRow("OrbitCount", "粒子数量", 1, 60, 1, out slOrbitCount));
            body.Children.Add(SliderRow("OrbitRadius", "环绕半径", 10, 200, 1, out slOrbitRadius));
            body.Children.Add(SliderRow("OrbitSpeed", "旋转速度 (度/秒)", -360, 360, 5, out slOrbitSpeed));
            body.Children.Add(SliderRow("OrbitSize", "粒子大小", 1, 20, 0.5, out slOrbitSize));
            body.Children.Add(TextRow("OrbitColor", "粒子颜色 (#RRGGBB)", out tbOrbitColor));

            root.Children.Add(card);
        }

        private void BuildGlowCard(StackPanel root)
        {
            var card = Card("光标光晕", out var body);

            body.Children.Add(ToggleRow("EnableGlow", "启用光晕", out tgGlow));
            body.Children.Add(TextRow("GlowColor", "光晕颜色 (#RRGGBB)", out tbGlowColor));
            body.Children.Add(SliderRow("GlowSize", "光晕半径", 10, 200, 1, out slGlowSize));
            body.Children.Add(SliderRow("GlowIntensity", "光晕强度", 0, 1, 0.05, out slGlowInt));

            root.Children.Add(card);
        }

        private void BuildGeneralCard(StackPanel root)
        {
            var card = Card("常规", out var body);

            body.Children.Add(ToggleRow("StartWithWindows", "开机自启", out tgStartup));

            var btnReset = new Button { Content = "恢复默认设置", Margin = new Thickness(0, 4, 0, 0) };
            btnReset.Click += btnReset_Click;
            var btnExit = new Button { Content = "退出程序", Margin = new Thickness(0, 4, 0, 0) };
            btnExit.Click += btnExit_Click;

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0) };
            btnRow.Children.Add(btnReset);
            btnRow.Children.Add(btnExit);
            body.Children.Add(btnRow);

            root.Children.Add(card);
        }

        // ---------------- Row helpers ----------------
        private Grid ToggleRow(string propName, string label, out ToggleSwitch ts)
        {
            ts = new ToggleSwitch { Tag = propName, HorizontalAlignment = HorizontalAlignment.Right };
            ts.Toggled += Commit;
            return LabeledRow(label, ts);
        }

        private Grid ComboRow(string propName, string label, out ComboBox cb,
            params (string tag, string text)[] items)
        {
            cb = new ComboBox { Tag = propName, Width = 200, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var (t, x) in items)
                cb.Items.Add(new ComboBoxItem { Tag = t, Content = x });
            cb.SelectionChanged += Commit;
            return LabeledRow(label, cb);
        }

        private Grid TextRow(string propName, string label, out TextBox tb)
        {
            tb = new TextBox { Tag = propName, Width = 200, HorizontalAlignment = HorizontalAlignment.Right };
            tb.TextChanged += Commit;
            return LabeledRow(label, tb);
        }

        private StackPanel SliderRow(string propName, string label, double min, double max,
            double step, out Slider slider)
        {
            slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                StepFrequency = step,
                Width = 200,
                Tag = propName,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            slider.ValueChanged += Commit;

            var value = new TextBlock
            {
                Tag = propName + "_v",
                Width = 46,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
            };

            var hrow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            hrow.Children.Add(slider);
            hrow.Children.Add(value);

            var sp = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };
            sp.Children.Add(new TextBlock { Text = label });
            sp.Children.Add(hrow);
            return sp;
        }

        private Grid LabeledRow(string label, FrameworkElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(control, 1);
            g.Children.Add(lbl);
            g.Children.Add(control);
            return g;
        }

        // ---------------- Window behavior ----------------
        private void OnClosing(object? sender, AppWindowClosingEventArgs e)
        {
            if (!_exiting)
            {
                e.Cancel = true;
                NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
            }
        }

        public void ShowOrActivate()
        {
            this.Activate();
            try { this.AppWindow.MoveInZOrderAtTop(); } catch { }
        }

        public void RequestExit()
        {
            _exiting = true;
            this.Close();
        }

        // ---------------- Generic commit ----------------
        private void Commit(object? sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string propName) return;
            var prop = typeof(AppSettings).GetProperty(propName);
            if (prop == null) return;

            object? value = null;
            switch (sender)
            {
                case ToggleSwitch t: value = t.IsOn; break;
                case Slider s:
                    value = Convert.ChangeType(s.Value, prop.PropertyType, CultureInfo.InvariantCulture);
                    RefreshLabel(s, s.Value);
                    break;
                case TextBox tb: value = tb.Text; break;
                case ComboBox cb:
                    value = (cb.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                    break;
            }
            if (value == null) return;

            try { prop.SetValue(SettingsManager.Current, value); }
            catch { return; }

            if (propName == "IconType")
            {
                bool custom = value as string == "custom";
                tbIconPath.IsEnabled = custom;
                btnBrowse.IsEnabled = custom;
            }

            SettingsManager.Save();
        }

        private static void RefreshLabel(FrameworkElement fe, double v)
        {
            if (fe.Parent is Panel p && fe.Tag is string tag)
            {
                foreach (var c in p.Children)
                    if (c is TextBlock tb && tb.Tag as string == tag + "_v")
                        tb.Text = v.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        // ---------------- Sync UI <- settings ----------------
        private void SyncFromSettings()
        {
            var s = SettingsManager.Current;

            tgClick.IsOn = s.EnableClickEffects;
            SelectByTag(cbClickPreset, s.ClickPreset);
            tbClickColor.Text = s.ClickColor;
            SetSlider(slClickCount, s.ClickParticleCount);
            SetSlider(slClickSpeed, s.ClickSpeed);
            SetSlider(slClickGravity, s.ClickGravity);

            tgRope.IsOn = s.EnableRope;
            SetSlider(slRopeLen, s.RopeLength);
            SetSlider(slRopeSeg, s.RopeSegments);
            SetSlider(slRopeGrav, s.RopeGravity);
            SetSlider(slRopeDamp, s.RopeDamping);
            SetSlider(slRopeStiff, s.RopeStiffness);
            SelectByTag(cbIconType, s.IconType);
            SetSlider(slIconSize, s.IconSize);
            tbIconColor.Text = s.IconColor;
            tbRopeColor.Text = s.RopeColor;
            SetSlider(slRopeWidth, s.RopeWidth);
            tbIconPath.Text = s.CustomIconPath;
            bool custom = s.IconType == "custom";
            tbIconPath.IsEnabled = custom;
            btnBrowse.IsEnabled = custom;

            tgTrail.IsOn = s.EnableTrail;
            tbTrailColor.Text = s.TrailColor;
            SetSlider(slTrailLen, s.TrailLength);
            SetSlider(slTrailWidth, s.TrailWidth);

            tgGlow.IsOn = s.EnableGlow;
            tbGlowColor.Text = s.GlowColor;
            SetSlider(slGlowSize, s.GlowSize);
            SetSlider(slGlowInt, s.GlowIntensity);

            tgOrbit.IsOn = s.EnableOrbit;
            SetSlider(slOrbitCount, s.OrbitCount);
            SetSlider(slOrbitRadius, s.OrbitRadius);
            SetSlider(slOrbitSpeed, s.OrbitSpeed);
            SetSlider(slOrbitSize, s.OrbitSize);
            tbOrbitColor.Text = s.OrbitColor;

            tgStartup.IsOn = s.StartWithWindows;
        }

        private static void SetSlider(Slider s, double v)
        {
            s.Value = v;
            RefreshLabel(s, v);
        }

        private static void SelectByTag(ComboBox cb, string? val)
        {
            foreach (var it in cb.Items)
                if (it is ComboBoxItem ci && (ci.Tag as string) == val) { cb.SelectedItem = ci; return; }
        }

        // ---------------- Buttons ----------------
        private async void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, _hwnd);
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".svg");
            picker.FileTypeFilter.Add(".gif");
            var file = await picker.PickSingleFileAsync();
            if (file != null) tbIconPath.Text = file.Path;
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            SettingsManager.Current.Reset();
            SyncFromSettings();
            SettingsManager.Save();
        }

        private void btnExit_Click(object sender, RoutedEventArgs e)
        {
            ((App)Application.Current).RequestExit();
        }
    }
}
