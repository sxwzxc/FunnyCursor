using System;
using System.Runtime.InteropServices;

namespace MouseBeautifier
{
    /// <summary>
    /// System-tray icon built on a message-only window. Left click / double click
    /// opens the panel; right click shows a context menu (open / exit).
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
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

            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void ShowContextMenu()
        {
            IntPtr menu = NativeMethods.CreatePopupMenu();
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 1, "打开面板");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 2, "退出");
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
                NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _nid);
                NativeMethods.DestroyWindow(_hwnd);
                NativeMethods.UnregisterClass(_className, _hInstance);
                _hwnd = IntPtr.Zero;
            }
        }
    }
}
