using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MouseBeautifier
{
    /// <summary>
    /// Settings dialog implemented with raw Win32 (no WinUI XAML). WinUI's themed
    /// controls require framework theme resources that cannot be deployed in this
    /// self-contained build (PRI generation is disabled because the sandbox lacks
    /// Visual Studio's AppxPackage tooling). Win32 common controls render via the OS
    /// and need no such resources, so the panel stays fully functional.
    /// </summary>
    internal static class SettingsDialog
    {
        private const int DLG_W = 520;
        private const int GROUP_X = 8;
        private const int GROUP_W = 504;
        private const int LBL_X = 18;
        private const int CTRL_X = 150;
        private const int TRACK_W = 220;
        private const int VAL_X = 378;
        private const int ROW_H = 22;
        private const int GROUP_TOP = 18;   // caption area inside a group box
        private const int GROUP_PAD = 8;    // bottom padding inside a group box

        // control ids
        private const int ID_GRP_CLICK = 10, ID_GRP_ROPE = 11, ID_GRP_TRAIL = 12,
            ID_GRP_ORBIT = 13, ID_GRP_GLOW = 14, ID_GRP_GENERAL = 15, ID_GRP_ABOUT = 16;
        private const int ID_CHK_CLICK = 100, ID_CHK_ROPE = 101, ID_CHK_TRAIL = 102,
            ID_CHK_ORBIT = 103, ID_CHK_GLOW = 104, ID_CHK_STARTUP = 105;
        private const int ID_CMB_PRESET = 200, ID_CMB_ICON = 201;
        private const int ID_TRK_CLICKCOUNT = 300, ID_TRK_CLICKSPEED = 301, ID_TRK_CLICKGRAV = 302,
            ID_TRK_ROPELEN = 303, ID_TRK_ROPESEG = 304, ID_TRK_ROPEGRAV = 305,
            ID_TRK_ROPEDAMP = 306, ID_TRK_ROPESTIFF = 307, ID_TRK_ICONSIZE = 308,
            ID_TRK_ROPEWIDTH = 309, ID_TRK_TRAILLEN = 310, ID_TRK_TRAILWIDTH = 311,
            ID_TRK_ORBITCOUNT = 312, ID_TRK_ORBITRAD = 313, ID_TRK_ORBITSPEED = 314,
            ID_TRK_ORBITSIZE = 315, ID_TRK_GLOWSIZE = 316, ID_TRK_GLOWINT = 317;
        private const int ID_BTN_CLICKCOLOR = 400, ID_BTN_ICONCOLOR = 401, ID_BTN_ROPECOLOR = 402,
            ID_BTN_TRAILCOLOR = 403, ID_BTN_ORBITCOLOR = 404, ID_BTN_GLOWCOLOR = 405;
        private const int ID_EDT_ICONPATH = 500, ID_BTN_BROWSE = 501;
        private const int ID_BTN_RESET = 600, ID_BTN_EXIT = 601, ID_BTN_OPENREPO = 602, ID_BTN_COPYVER = 603;

        private static readonly string[] PresetVals = { "sparkle", "confetti", "ring", "ripple" };
        private static readonly string[] PresetDisp = { "闪烁粒子", "彩色纸屑", "扩散光环", "水波纹" };
        private static readonly string[] IconVals = { "star", "circle", "square", "triangle", "diamond", "heart", "smiley", "pig", "girl", "custom" };
        private static readonly string[] IconDisp = { "五角星", "圆形", "方形", "三角", "菱形", "心形", "笑脸", "🐷 粉色小猪", "👧 二次元女孩", "自定义图片" };

        private static readonly string _className = "MouseBeautifierSettings_" + Guid.NewGuid().ToString("N");
        private static DlgNative.WndProc _wndProc = null!;
        private static IntPtr _hwnd = IntPtr.Zero;
        private static IntPtr _hInstance;
        private static bool _classRegistered;
        private static readonly List<(IntPtr hwnd, int baseY)> _children = new();
        private static readonly Dictionary<int, IntPtr> _valLabels = new();
        private static int _scroll;
        private static int _contentHeight;
        private static readonly int[] _custColors = new int[16];
        private static GCHandle _custColorsHandle;

        public static event Action? ExitRequested;

        public static void Show()
        {
            if (_hwnd != IntPtr.Zero)
            {
                DlgNative.ShowWindow(_hwnd, DlgNative.SW_SHOW);
                DlgNative.SetForegroundWindow(_hwnd);
                return;
            }

            _hInstance = Marshal.GetHINSTANCE(typeof(SettingsDialog).Module);
            _wndProc = WndProc;

            var icc = new DlgNative.INITCOMMONCONTROLSEX
            {
                dwSize = (uint)Marshal.SizeOf<DlgNative.INITCOMMONCONTROLSEX>(),
                dwICC = DlgNative.ICC_STANDARD_CLASSES,
            };
            DlgNative.InitCommonControlsEx(ref icc);

            if (!_classRegistered)
            {
                var wc = new DlgNative.WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<DlgNative.WNDCLASSEX>(),
                    style = 0,
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = _hInstance,
                    hbrBackground = (IntPtr)(5 + 1), // COLOR_WINDOW + 1
                    lpszClassName = _className,
                };
                DlgNative.RegisterClassEx(ref wc);
                _classRegistered = true;
            }

            _hwnd = DlgNative.CreateWindowEx(
                0, _className, $"FunnyCursor 设置  v{AppInfo.Version}",
                DlgNative.WS_OVERLAPPEDWINDOW | DlgNative.WS_VSCROLL,
                DlgNative.CW_USEDEFAULT, DlgNative.CW_USEDEFAULT, DLG_W, 640,
                IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);

            DlgNative.ShowWindow(_hwnd, DlgNative.SW_SHOW);

            // Set window icon (title bar) to the custom app icon.
            string baseDir = System.IO.Path.GetDirectoryName(typeof(SettingsDialog).Assembly.Location) ?? "";
            string icoPath = System.IO.Path.Combine(baseDir, "Assets", "funnycursor.ico");
            if (System.IO.File.Exists(icoPath))
            {
                IntPtr hIcon = DlgNative.LoadImage(IntPtr.Zero, icoPath, DlgNative.IMAGE_ICON, 0, 0,
                    DlgNative.LR_LOADFROMFILE | DlgNative.LR_DEFAULTSIZE);
                if (hIcon != IntPtr.Zero)
                {
                    DlgNative.SendMessage(_hwnd, DlgNative.WM_SETICON, (IntPtr)DlgNative.ICON_BIG, hIcon);
                    DlgNative.SendMessage(_hwnd, DlgNative.WM_SETICON, (IntPtr)DlgNative.ICON_SMALL, hIcon);
                }
            }
        }

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case DlgNative.WM_CREATE:
                    BuildUI(hWnd);
                    return IntPtr.Zero;
                case DlgNative.WM_COMMAND:
                    int id = (int)(wParam.ToInt64() & 0xFFFF);
                    OnCommand(hWnd, id);
                    return IntPtr.Zero;
                case DlgNative.WM_HSCROLL:
                    OnHScroll(hWnd, lParam);
                    return IntPtr.Zero;
                case DlgNative.WM_VSCROLL:
                    OnVScroll(hWnd, wParam);
                    return IntPtr.Zero;
                case DlgNative.WM_CLOSE:
                    // Hide instead of destroy, so the window can be re-opened from the tray
                    // without re-registering its window class.
                    DlgNative.ShowWindow(hWnd, DlgNative.SW_HIDE);
                    return IntPtr.Zero;
            }
            return DlgNative.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        // ---------------- UI construction ----------------
        private static void BuildUI(IntPtr hWnd)
        {
            _children.Clear();
            _valLabels.Clear();
            int y = 8;

            // ---- 点击特效 (6 rows) ----
            y = GroupStart(ID_GRP_CLICK, "点击特效", y, 6);
            AddCheckBox(hWnd, ID_CHK_CLICK, "启用点击特效", ref y, SettingsManager.Current.EnableClickEffects);
            AddCombo(hWnd, ID_CMB_PRESET, "特效预设", ref y, PresetDisp, IndexOf(PresetVals, SettingsManager.Current.ClickPreset));
            AddColorButton(hWnd, ID_BTN_CLICKCOLOR, "特效颜色", ref y, SettingsManager.Current.ClickColor);
            AddTrack(hWnd, ID_TRK_CLICKCOUNT, "粒子数量", ref y, 1, 200, SettingsManager.Current.ClickParticleCount);
            AddTrack(hWnd, ID_TRK_CLICKSPEED, "喷射速度", ref y, 50, 2000, SettingsManager.Current.ClickSpeed);
            AddTrack(hWnd, ID_TRK_CLICKGRAV, "重力", ref y, 0, 3000, SettingsManager.Current.ClickGravity);

            // ---- 悬挂绳子 + 图标 (11 rows) ----
            y = GroupStart(ID_GRP_ROPE, "悬挂绳子 + 图标", y, 11);
            AddCheckBox(hWnd, ID_CHK_ROPE, "启用绳子", ref y, SettingsManager.Current.EnableRope);
            AddTrack(hWnd, ID_TRK_ROPELEN, "绳子长度", ref y, 20, 500, SettingsManager.Current.RopeLength);
            AddTrack(hWnd, ID_TRK_ROPESEG, "绳子节数", ref y, 2, 40, SettingsManager.Current.RopeSegments);
            AddTrack(hWnd, ID_TRK_ROPEGRAV, "重力", ref y, 0, 3000, SettingsManager.Current.RopeGravity);
            AddTrackScaled(hWnd, ID_TRK_ROPEDAMP, "阻尼", ref y, 50, 99, SettingsManager.Current.RopeDamping, 100);
            AddTrackScaled(hWnd, ID_TRK_ROPESTIFF, "刚度", ref y, 10, 100, SettingsManager.Current.RopeStiffness, 100);
            AddCombo(hWnd, ID_CMB_ICON, "悬挂图标", ref y, IconDisp, IndexOf(IconVals, SettingsManager.Current.IconType));
            AddTrack(hWnd, ID_TRK_ICONSIZE, "图标大小", ref y, 10, 120, SettingsManager.Current.IconSize);
            AddColorButton(hWnd, ID_BTN_ICONCOLOR, "图标颜色", ref y, SettingsManager.Current.IconColor);
            AddColorButton(hWnd, ID_BTN_ROPECOLOR, "绳子颜色", ref y, SettingsManager.Current.RopeColor);
            AddEdit(hWnd, ID_EDT_ICONPATH, "自定义图标路径 (PNG/SVG/GIF)", ref y, SettingsManager.Current.CustomIconPath, ID_BTN_BROWSE);

            // ---- 光标拖尾 (4 rows) ----
            y = GroupStart(ID_GRP_TRAIL, "光标拖尾", y, 4);
            AddCheckBox(hWnd, ID_CHK_TRAIL, "启用拖尾", ref y, SettingsManager.Current.EnableTrail);
            AddColorButton(hWnd, ID_BTN_TRAILCOLOR, "拖尾颜色", ref y, SettingsManager.Current.TrailColor);
            AddTrackScaled(hWnd, ID_TRK_TRAILLEN, "拖尾长度(秒)", ref y, 10, 200, SettingsManager.Current.TrailLength, 100);
            AddTrack(hWnd, ID_TRK_TRAILWIDTH, "拖尾宽度", ref y, 2, 40, SettingsManager.Current.TrailWidth);

            // ---- 环绕粒子 (7 rows) ----
            y = GroupStart(ID_GRP_ORBIT, "环绕粒子", y, 7);
            AddCheckBox(hWnd, ID_CHK_ORBIT, "启用环绕粒子", ref y, SettingsManager.Current.EnableOrbit);
            AddTrack(hWnd, ID_TRK_ORBITCOUNT, "粒子数量", ref y, 1, 60, SettingsManager.Current.OrbitCount);
            AddTrack(hWnd, ID_TRK_ORBITRAD, "环绕半径", ref y, 10, 200, SettingsManager.Current.OrbitRadius);
            // Trackbar range must be non-negative (16-bit words), so map -360..360 -> 0..720.
            AddTrack(hWnd, ID_TRK_ORBITSPEED, "旋转速度(度/秒)", ref y, 0, 720, SettingsManager.Current.OrbitSpeed + 360);
            AddTrack(hWnd, ID_TRK_ORBITSIZE, "粒子大小", ref y, 2, 40, SettingsManager.Current.OrbitSize);
            AddColorButton(hWnd, ID_BTN_ORBITCOLOR, "粒子颜色", ref y, SettingsManager.Current.OrbitColor);

            // ---- 光标光晕 (4 rows) ----
            y = GroupStart(ID_GRP_GLOW, "光标光晕", y, 4);
            AddCheckBox(hWnd, ID_CHK_GLOW, "启用光晕", ref y, SettingsManager.Current.EnableGlow);
            AddColorButton(hWnd, ID_BTN_GLOWCOLOR, "光晕颜色", ref y, SettingsManager.Current.GlowColor);
            AddTrack(hWnd, ID_TRK_GLOWSIZE, "光晕半径", ref y, 10, 200, SettingsManager.Current.GlowSize);
            AddTrackScaled(hWnd, ID_TRK_GLOWINT, "光晕强度", ref y, 0, 100, SettingsManager.Current.GlowIntensity, 100);

            // ---- 常规 (3 rows) ----
            y = GroupStart(ID_GRP_GENERAL, "常规", y, 3);
            AddCheckBox(hWnd, ID_CHK_STARTUP, "开机自启", ref y, SettingsManager.Current.StartWithWindows);
            AddButton(hWnd, ID_BTN_RESET, "恢复默认设置", ref y, 150, 24);
            AddButton(hWnd, ID_BTN_EXIT, "退出程序 (Ctrl+Shift+F10)", ref y, 200, 24);

            // ---- 关于 (5 rows) ----
            y = GroupStart(ID_GRP_ABOUT, "关于", y, 5);
            AddInfoLabel(hWnd, "产品", $"{AppInfo.Product}", ref y);
            AddInfoLabel(hWnd, "版本", $"{AppInfo.Version}", ref y, ID_BTN_COPYVER, "复制");
            AddInfoLabel(hWnd, "作者", AppInfo.Author, ref y);
            AddInfoLabel(hWnd, "版权", AppInfo.Copyright, ref y);
            AddInfoLabel(hWnd, "仓库", AppInfo.RepositoryUrl, ref y, ID_BTN_OPENREPO, "打开");

            _contentHeight = y + 10;
            DlgNative.GetClientRect(hWnd, out DlgNative.RECT rc);
            int clientH = rc.bottom - rc.top;
            int maxScroll = Math.Max(0, _contentHeight - clientH + 20);
            DlgNative.SetScrollRange(hWnd, DlgNative.SB_VERT, 0, maxScroll, true);
            _scroll = 0;
        }

        private static int GroupStart(int id, string title, int y, int rows)
        {
            int h = GROUP_TOP + rows * ROW_H + GROUP_PAD;
            CreateChild(DlgNative.WC_BUTTON, title, id,
                DlgNative.BS_GROUPBOX | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE,
                GROUP_X, y, GROUP_W, h, IntPtr.Zero, 0);
            return y + GROUP_TOP; // inner content starts below the group caption
        }

        private static void AddCheckBox(IntPtr parent, int id, string text, ref int y, bool a)
        {
            IntPtr c = CreateChild(DlgNative.WC_BUTTON, text, id,
                DlgNative.BS_AUTOCHECKBOX | DlgNative.BS_LEFT | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE | DlgNative.WS_TABSTOP,
                LBL_X, y, 300, 20, parent, 0);
            DlgNative.CheckDlgButton(parent, id, a ? 1u : 0u);
            y += ROW_H;
        }

        private static void AddCombo(IntPtr parent, int id, string label, ref int y, string[] items, int sel)
        {
            CreateChild(DlgNative.WC_STATIC, label, 0,
                DlgNative.SS_LEFT | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE, LBL_X, y + 2, 120, 18, parent, 0);
            IntPtr cb = CreateChild(DlgNative.WC_COMBOBOX, "", id,
                DlgNative.CBS_DROPDOWNLIST | DlgNative.CBS_HASSTRINGS | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE | DlgNative.WS_TABSTOP | DlgNative.WS_VSCROLL,
                CTRL_X, y, 200, 22, parent, 0);
            foreach (var it in items)
                DlgNative.SendMessage(cb, DlgNative.CB_ADDSTRING, IntPtr.Zero, it);
            if (sel >= 0) DlgNative.SendMessage(cb, DlgNative.CB_SETCURSEL, (IntPtr)sel, IntPtr.Zero);
            y += ROW_H;
        }

        private static void AddColorButton(IntPtr parent, int id, string label, ref int y, string hex)
        {
            CreateChild(DlgNative.WC_STATIC, label, 0,
                DlgNative.SS_LEFT | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE, LBL_X, y + 2, 120, 18, parent, 0);
            CreateChild(DlgNative.WC_BUTTON, hex, id,
                DlgNative.BS_PUSHBUTTON | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE | DlgNative.WS_TABSTOP,
                CTRL_X, y, 200, 20, parent, 0);
            y += ROW_H;
        }

        private static void AddEdit(IntPtr parent, int id, string label, ref int y, string text, int browseId)
        {
            CreateChild(DlgNative.WC_STATIC, label, 0,
                DlgNative.SS_LEFT | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE, LBL_X, y + 2, 120, 18, parent, 0);
            IntPtr ed = CreateChild(DlgNative.WC_EDIT, text, id,
                DlgNative.ES_AUTOHSCROLL | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE | DlgNative.WS_TABSTOP | DlgNative.WS_BORDER,
                CTRL_X, y, 250, 20, parent, DlgNative.WS_EX_CLIENTEDGE);
            DlgNative.SendMessage(ed, DlgNative.EM_LIMITTEXT, (IntPtr)512, IntPtr.Zero);
            CreateChild(DlgNative.WC_BUTTON, "浏览…", browseId,
                DlgNative.BS_PUSHBUTTON | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE | DlgNative.WS_TABSTOP,
                CTRL_X + 258, y, 60, 20, parent, 0);
            y += ROW_H;
        }

        private static void AddButton(IntPtr parent, int id, string text, ref int y, int w, int h)
        {
            CreateChild(DlgNative.WC_BUTTON, text, id,
                DlgNative.BS_PUSHBUTTON | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE | DlgNative.WS_TABSTOP,
                LBL_X, y, w, h, parent, 0);
            y += ROW_H;
        }

        /// <summary>
        /// 创建一行"标签 : 值 [+ 操作按钮]"的信息行，用于「关于」分组。
        /// valueBtnId 与 valueBtnText 为 0/null 时不显示按钮。
        /// </summary>
        private static void AddInfoLabel(IntPtr parent, string label, string value, ref int y, int valueBtnId = 0, string? valueBtnText = null)
        {
            CreateChild(DlgNative.WC_STATIC, label, 0,
                DlgNative.SS_LEFT | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE, LBL_X, y + 2, 120, 18, parent, 0);
            CreateChild(DlgNative.WC_STATIC, value, 0,
                DlgNative.SS_LEFT | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE, CTRL_X, y + 2, 220, 18, parent, 0);
            if (valueBtnId != 0 && !string.IsNullOrEmpty(valueBtnText))
            {
                CreateChild(DlgNative.WC_BUTTON, valueBtnText!, valueBtnId,
                    DlgNative.BS_PUSHBUTTON | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE | DlgNative.WS_TABSTOP,
                    CTRL_X + 228, y, 60, 20, parent, 0);
            }
            y += ROW_H;
        }

        private static void AddTrack(IntPtr parent, int id, string label, ref int y, int min, int max, double val)
        {
            CreateChild(DlgNative.WC_STATIC, label, 0,
                DlgNative.SS_LEFT | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE, LBL_X, y + 2, 120, 18, parent, 0);
            IntPtr trk = CreateChild(DlgNative.TRACKBAR_CLASS, "", id,
                DlgNative.WS_CHILD | DlgNative.WS_VISIBLE | DlgNative.WS_TABSTOP,
                CTRL_X, y, TRACK_W, 20, parent, 0);
            SetTrackRange(trk, min, max, (int)Math.Round(val));
            IntPtr valLbl = CreateChild(DlgNative.WC_STATIC, TrackValue(id, (int)Math.Round(val)).ToString("0.##"), 0,
                DlgNative.SS_LEFT | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE, VAL_X, y + 2, 120, 18, parent, 0);
            _valLabels[id] = valLbl;
            y += ROW_H;
        }

        private static void AddTrackScaled(IntPtr parent, int id, string label, ref int y, int min, int max, double val, double scale)
        {
            CreateChild(DlgNative.WC_STATIC, label, 0,
                DlgNative.SS_LEFT | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE, LBL_X, y + 2, 120, 18, parent, 0);
            int pos = (int)Math.Round(val * scale);
            IntPtr trk = CreateChild(DlgNative.TRACKBAR_CLASS, "", id,
                DlgNative.WS_CHILD | DlgNative.WS_VISIBLE | DlgNative.WS_TABSTOP,
                CTRL_X, y, TRACK_W, 20, parent, 0);
            SetTrackRange(trk, min, max, pos);
            IntPtr valLbl = CreateChild(DlgNative.WC_STATIC, TrackValue(id, pos).ToString("0.##"), 0,
                DlgNative.SS_LEFT | DlgNative.WS_CHILD | DlgNative.WS_VISIBLE, VAL_X, y + 2, 120, 18, parent, 0);
            _valLabels[id] = valLbl;
            y += ROW_H;
        }

        // ---------------- helpers ----------------
        private static IntPtr CreateChild(string cls, string text, int id, int style, int x, int y, int w, int h, IntPtr parent, int exStyle)
        {
            IntPtr hwnd = DlgNative.CreateWindowEx(exStyle, cls, text, style, x, y, w, h,
                parent, (IntPtr)id, _hInstance, IntPtr.Zero);
            _children.Add((hwnd, y));
            return hwnd;
        }

        private static void SetTrackRange(IntPtr trk, int min, int max, int pos)
        {
            DlgNative.SendMessage(trk, DlgNative.TBM_SETRANGE, (IntPtr)1, (IntPtr)((min & 0xFFFF) | (max << 16)));
            DlgNative.SendMessage(trk, DlgNative.TBM_SETPOS, (IntPtr)1, (IntPtr)pos);
            DlgNative.SendMessage(trk, DlgNative.TBM_SETTICFREQ, (IntPtr)Math.Max(1, (max - min) / 10), IntPtr.Zero);
        }

        private static int IndexOf(string[] arr, string? v)
        {
            if (v == null) return 0;
            for (int i = 0; i < arr.Length; i++) if (arr[i] == v) return i;
            return 0;
        }

        // ---------------- events ----------------
        private static void OnCommand(IntPtr hWnd, int id)
        {
            if (id == ID_BTN_RESET)
            {
                SettingsManager.Current.Reset();
                Rebuild(hWnd);
                SettingsManager.Save();
                return;
            }
            if (id == ID_BTN_EXIT) { ExitRequested?.Invoke(); return; }
            if (id == ID_BTN_BROWSE) { BrowseIcon(hWnd); return; }
            if (id == ID_BTN_OPENREPO) { OpenUrl(AppInfo.RepositoryUrl); return; }
            if (id == ID_BTN_COPYVER)
            {
                CopyToClipboard($"FunnyCursor v{AppInfo.Version}");
                return;
            }

            if (id == ID_BTN_CLICKCOLOR || id == ID_BTN_ICONCOLOR || id == ID_BTN_ROPECOLOR ||
                id == ID_BTN_TRAILCOLOR || id == ID_BTN_ORBITCOLOR || id == ID_BTN_GLOWCOLOR)
            {
                PickColor(hWnd, id);
                return;
            }

            // checkbox / combo / edit change -> commit
            Commit(hWnd);
        }

        private static void OnHScroll(IntPtr hWnd, IntPtr lParam)
        {
            IntPtr ctrl = lParam;
            int id = DlgNative.GetDlgCtrlID(ctrl);
            int pos = (int)DlgNative.SendMessage(ctrl, DlgNative.TBM_GETPOS, IntPtr.Zero, IntPtr.Zero).ToInt64();
            if (_valLabels.TryGetValue(id, out IntPtr valLbl))
            {
                double v = TrackValue(id, pos);
                DlgNative.SetWindowText(valLbl, v.ToString("0.##"));
            }
            Commit(hWnd);
        }

        private static void OnVScroll(IntPtr hWnd, IntPtr wParam)
        {
            int code = (int)(wParam.ToInt64() & 0xFFFF);
            int pos = DlgNative.GetScrollPos(hWnd, DlgNative.SB_VERT);
            DlgNative.GetClientRect(hWnd, out DlgNative.RECT rc);
            int clientH = rc.bottom - rc.top;
            int maxScroll = Math.Max(0, _contentHeight - clientH + 20);
            int newPos = pos;
            switch (code)
            {
                case DlgNative.SB_LINEUP: newPos = pos - 20; break;
                case DlgNative.SB_LINEDOWN: newPos = pos + 20; break;
                case DlgNative.SB_PAGEUP: newPos = pos - clientH; break;
                case DlgNative.SB_PAGEDOWN: newPos = pos + clientH; break;
                case DlgNative.SB_THUMBPOSITION:
                case DlgNative.SB_THUMBTRACK: newPos = (int)((wParam.ToInt64() >> 16) & 0xFFFF); break;
                case DlgNative.SB_ENDSCROLL: return;
            }
            newPos = Math.Max(0, Math.Min(maxScroll, newPos));
            DlgNative.SetScrollPos(hWnd, DlgNative.SB_VERT, newPos, true);
            _scroll = newPos;
            foreach (var (hwnd, baseY) in _children)
                DlgNative.SetWindowPos(hwnd, IntPtr.Zero, 0, baseY - _scroll, 0, 0,
                    DlgNative.SWP_NOSIZE | DlgNative.SWP_NOZORDER | DlgNative.SWP_NOACTIVATE);
        }

        private static void Rebuild(IntPtr hWnd)
        {
            // destroy all children and rebuild from current settings
            foreach (var (hwnd, _) in _children)
                DlgNative.DestroyWindow(hwnd);
            _children.Clear();
            BuildUI(hWnd);
            // BuildUI resets _scroll to 0; make sure children reflect that position.
            foreach (var (hwnd, baseY) in _children)
                DlgNative.SetWindowPos(hwnd, IntPtr.Zero, 0, baseY, 0, 0,
                    DlgNative.SWP_NOSIZE | DlgNative.SWP_NOZORDER | DlgNative.SWP_NOACTIVATE);
        }

        // ---------------- value mapping ----------------
        private static double TrackValue(int id, int pos)
        {
            switch (id)
            {
                case ID_TRK_ROPEDAMP: return pos / 100.0;
                case ID_TRK_ROPESTIFF: return pos / 100.0;
                case ID_TRK_TRAILLEN: return pos / 100.0;
                case ID_TRK_GLOWINT: return pos / 100.0;
                case ID_TRK_ORBITSPEED: return pos - 360.0; // 0..720 -> -360..360
                default: return pos;
            }
        }

        private static double GetTrackValue(IntPtr hWnd, int id)
        {
            int pos = (int)DlgNative.SendMessage(GetChild(hWnd, id), DlgNative.TBM_GETPOS, IntPtr.Zero, IntPtr.Zero).ToInt64();
            return TrackValue(id, pos);
        }

        // ---------------- commit ----------------
        private static void Commit(IntPtr hWnd)
        {
            var s = SettingsManager.Current;
            s.EnableClickEffects = DlgNative.IsDlgButtonChecked(hWnd, ID_CHK_CLICK) == 1;
            int psel = DlgNative.SendMessage(GetChild(hWnd, ID_CMB_PRESET), DlgNative.CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (psel < 0 || psel >= PresetVals.Length) psel = 0;
            s.ClickPreset = PresetVals[psel];
            s.ClickParticleCount = (int)GetTrackValue(hWnd, ID_TRK_CLICKCOUNT);
            s.ClickSpeed = (int)GetTrackValue(hWnd, ID_TRK_CLICKSPEED);
            s.ClickGravity = (int)GetTrackValue(hWnd, ID_TRK_CLICKGRAV);

            s.EnableRope = DlgNative.IsDlgButtonChecked(hWnd, ID_CHK_ROPE) == 1;
            s.RopeLength = (int)GetTrackValue(hWnd, ID_TRK_ROPELEN);
            s.RopeSegments = (int)GetTrackValue(hWnd, ID_TRK_ROPESEG);
            s.RopeGravity = (int)GetTrackValue(hWnd, ID_TRK_ROPEGRAV);
            s.RopeDamping = GetTrackValue(hWnd, ID_TRK_ROPEDAMP);
            s.RopeStiffness = GetTrackValue(hWnd, ID_TRK_ROPESTIFF);
            int isel = DlgNative.SendMessage(GetChild(hWnd, ID_CMB_ICON), DlgNative.CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (isel < 0 || isel >= IconVals.Length) isel = 0;
            s.IconType = IconVals[isel];
            s.IconSize = (int)GetTrackValue(hWnd, ID_TRK_ICONSIZE);
            s.IconColor = GetButtonText(GetChild(hWnd, ID_BTN_ICONCOLOR));
            s.RopeColor = GetButtonText(GetChild(hWnd, ID_BTN_ROPECOLOR));
            s.CustomIconPath = GetEditText(GetChild(hWnd, ID_EDT_ICONPATH));

            s.EnableTrail = DlgNative.IsDlgButtonChecked(hWnd, ID_CHK_TRAIL) == 1;
            s.TrailColor = GetButtonText(GetChild(hWnd, ID_BTN_TRAILCOLOR));
            s.TrailLength = GetTrackValue(hWnd, ID_TRK_TRAILLEN);
            s.TrailWidth = (int)GetTrackValue(hWnd, ID_TRK_TRAILWIDTH);

            s.EnableOrbit = DlgNative.IsDlgButtonChecked(hWnd, ID_CHK_ORBIT) == 1;
            s.OrbitCount = (int)GetTrackValue(hWnd, ID_TRK_ORBITCOUNT);
            s.OrbitRadius = (int)GetTrackValue(hWnd, ID_TRK_ORBITRAD);
            s.OrbitSpeed = (int)GetTrackValue(hWnd, ID_TRK_ORBITSPEED);
            s.OrbitSize = (int)GetTrackValue(hWnd, ID_TRK_ORBITSIZE);
            s.OrbitColor = GetButtonText(GetChild(hWnd, ID_BTN_ORBITCOLOR));

            s.EnableGlow = DlgNative.IsDlgButtonChecked(hWnd, ID_CHK_GLOW) == 1;
            s.GlowColor = GetButtonText(GetChild(hWnd, ID_BTN_GLOWCOLOR));
            s.GlowSize = (int)GetTrackValue(hWnd, ID_TRK_GLOWSIZE);
            s.GlowIntensity = GetTrackValue(hWnd, ID_TRK_GLOWINT);

            s.StartWithWindows = DlgNative.IsDlgButtonChecked(hWnd, ID_CHK_STARTUP) == 1;

            SettingsManager.Save();
        }

        private static IntPtr GetChild(IntPtr hWnd, int id) => DlgNative.GetDlgItem(hWnd, id);

        private static string GetButtonText(IntPtr hwnd)
        {
            int len = DlgNative.GetWindowTextLength(hwnd);
            var sb = new StringBuilder(len + 1);
            DlgNative.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetEditText(IntPtr hwnd)
        {
            int len = DlgNative.GetWindowTextLength(hwnd);
            var sb = new StringBuilder(len + 1);
            DlgNative.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        // ---------------- color / file pickers ----------------
        private static void PickColor(IntPtr hWnd, int id)
        {
            IntPtr btn = GetChild(hWnd, id);
            string cur = GetButtonText(btn);
            int rgb = HexToColorref(cur);

            if (!_custColorsHandle.IsAllocated)
                _custColorsHandle = GCHandle.Alloc(_custColors, GCHandleType.Pinned);

            var cc = new DlgNative.CHOOSECOLOR
            {
                lStructSize = (uint)Marshal.SizeOf<DlgNative.CHOOSECOLOR>(),
                hwndOwner = hWnd,
                rgbResult = rgb,
                lpCustColors = _custColorsHandle.AddrOfPinnedObject(),
                Flags = DlgNative.CC_ANYCOLOR | DlgNative.CC_RGBINIT | DlgNative.CC_FULLOPEN,
            };
            if (DlgNative.ChooseColor(ref cc))
            {
                int r = cc.rgbResult & 0xFF;
                int g = (cc.rgbResult >> 8) & 0xFF;
                int b = (cc.rgbResult >> 16) & 0xFF;
                string hex = $"#FF{r:X2}{g:X2}{b:X2}";
                DlgNative.SetWindowText(btn, hex);
                Commit(hWnd);
            }
        }

        private static void BrowseIcon(IntPtr hWnd)
        {
            var ofn = new DlgNative.OPENFILENAME
            {
                lStructSize = (uint)Marshal.SizeOf<DlgNative.OPENFILENAME>(),
                hwndOwner = hWnd,
                lpstrFilter = "图片文件\0*.png;*.jpg;*.jpeg;*.svg;*.gif\0所有文件\0*.*\0\0",
                lpstrFile = new string('\0', 512),
                nMaxFile = 512,
                lpstrTitle = "选择自定义图标",
                Flags = DlgNative.OFN_FILEMUSTEXIST | DlgNative.OFN_PATHMUSTEXIST | DlgNative.OFN_EXPLORER,
            };
            if (DlgNative.GetOpenFileName(ref ofn))
            {
                string path = ofn.lpstrFile.TrimEnd('\0');
                DlgNative.SetWindowText(GetChild(hWnd, ID_EDT_ICONPATH), path);
                Commit(hWnd);
            }
        }

        private static int HexToColorref(string hex)
        {
            var c = ColorsUtil.Parse(hex);
            return (c.B << 16) | (c.G << 8) | c.R;
        }

        // ---------------- misc helpers ----------------

        /// <summary>用系统默认浏览器打开 URL（ShellExecute 的等价调用）。</summary>
        private static void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            catch { /* 用户取消或无默认浏览器 */ }
        }

        /// <summary>把文本写入剪贴板（OLE 剪贴板，无需引用 WinForms）。</summary>
        private static void CopyToClipboard(string text)
        {
            // 走 Win32 OpenClipboard / SetClipboardData，避免依赖 System.Windows.Forms。
            if (!DlgNative.OpenClipboard(IntPtr.Zero)) return;
            try
            {
                DlgNative.EmptyClipboard();
                // CF_UNICODETEXT = 13
                IntPtr hGlobal = Marshal.StringToHGlobalUni(text);
                DlgNative.SetClipboardData(13, hGlobal);
                // 系统拥有该内存，不要 FreeHGlobal。
            }
            finally
            {
                DlgNative.CloseClipboard();
            }
        }
    }
}
