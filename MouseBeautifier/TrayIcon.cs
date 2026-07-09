using System;
using System.Runtime.InteropServices;

namespace MouseBeautifier
{
    /// <summary>
    /// System-tray icon built on a message-only window. Left click / double click
    /// opens the panel; right click shows a context menu (open / exit).
    /// Also owns the global "exit" hotkey (Ctrl+Shift+F10).
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private const int HOTKEY_ID_EXIT = 1;
        // Ctrl+Shift+F10 — chosen because it's rarely used by other apps and easy to press.
        private const uint HOTKEY_MODIFIERS = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;
        private const uint HOTKEY_VK = NativeMethods.VK_F10;

        private readonly string _className = "MouseBeautifierTray_" + Guid.NewGuid().ToString("N");
        private NativeMethods.WndProc _wndProc = null!;
        private IntPtr _hwnd;
        private IntPtr _hInstance;
        private NativeMethods.NOTIFYICONDATA _nid;
        private bool _disposed;

        public event Action? ShowPanelRequested;
        public event Action? ExitRequested;

        public TrayIcon()
        {
            Create();
        }

        private void Create()
        {
            _hInstance = Marshal.GetHINSTANCE(typeof(TrayIcon).Module);
            _wndProc = WndProc;

            var wc = new NativeMethods.WNDCLASS
            {
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = _hInstance,
                lpszClassName = _className,
            };
            NativeMethods.RegisterClass(ref wc);

            _hwnd = NativeMethods.CreateWindowEx(0, _className, "", 0, 0, 0, 0, 0,
                NativeMethods.HWND_MESSAGE, IntPtr.Zero, _hInstance, IntPtr.Zero);

            _nid = new NativeMethods.NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
                uCallbackMessage = NativeMethods.WM_TRAY,
                szTip = "FunnyCursor 鼠标美化",
            };
            // Try to load the custom app icon from the output Assets directory; fall back to default.
            string baseDir = System.IO.Path.GetDirectoryName(typeof(TrayIcon).Assembly.Location) ?? "";
            string icoPath = System.IO.Path.Combine(baseDir, "Assets", "funnycursor.ico");
            IntPtr hIcon = System.IO.File.Exists(icoPath)
                ? NativeMethods.LoadImage(IntPtr.Zero, icoPath, NativeMethods.IMAGE_ICON, 0, 0,
                    NativeMethods.LR_LOADFROMFILE | NativeMethods.LR_DEFAULTSIZE)
                : NativeMethods.LoadIcon(IntPtr.Zero, (IntPtr)NativeMethods.IDI_APPLICATION);
            _nid.hIcon = hIcon != IntPtr.Zero ? hIcon : NativeMethods.LoadIcon(IntPtr.Zero, (IntPtr)NativeMethods.IDI_APPLICATION);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _nid);

            // Register a global hotkey for quick exit (Ctrl+Shift+F10).
            if (!NativeMethods.RegisterHotKey(_hwnd, HOTKEY_ID_EXIT, HOTKEY_MODIFIERS, HOTKEY_VK))
            {
                // Fall back to Ctrl+Alt+Q if the default is taken.
                NativeMethods.RegisterHotKey(_hwnd, HOTKEY_ID_EXIT,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, 0x51 /*'Q'*/);
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == NativeMethods.WM_TRAY)
            {
                uint uMsg = (uint)lParam.ToInt32();
                if (uMsg == NativeMethods.WM_LBUTTONUP || uMsg == NativeMethods.WM_LBUTTONDBLCLK)
                    ShowPanelRequested?.Invoke();
                else if (uMsg == NativeMethods.WM_RBUTTONUP)
                    ShowContextMenu();
                return IntPtr.Zero;
            }

            if (msg == NativeMethods.WM_TASKBARCREATED)
            {
                NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _nid);
                return IntPtr.Zero;
            }

            if (msg == NativeMethods.WM_COMMAND)
            {
                int id = wParam.ToInt32() & 0xFFFF;
                if (id == 1) ShowPanelRequested?.Invoke();
                else if (id == 2) ExitRequested?.Invoke();
                return IntPtr.Zero;
            }

            // Global hotkey triggered (Ctrl+Shift+F10 or fallback Ctrl+Alt+Q).
            if (msg == NativeMethods.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_ID_EXIT)
                {
                    ExitRequested?.Invoke();
                    return IntPtr.Zero;
                }
            }

            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void ShowContextMenu()
        {
            IntPtr menu = NativeMethods.CreatePopupMenu();
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 1, "打开面板");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 2, "退出 (Ctrl+Shift+F10)");
            NativeMethods.GetCursorPos(out var pt);
            NativeMethods.SetForegroundWindow(_hwnd);
            NativeMethods.TrackPopupMenu(menu,
                NativeMethods.TPM_BOTTOMALIGN | NativeMethods.TPM_LEFTALIGN,
                pt.x, pt.y, 0, _hwnd, IntPtr.Zero);
            NativeMethods.DestroyMenu(menu);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_ID_EXIT);
                NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _nid);
                NativeMethods.DestroyWindow(_hwnd);
                NativeMethods.UnregisterClass(_className, _hInstance);
                _hwnd = IntPtr.Zero;
            }
        }
    }
}
