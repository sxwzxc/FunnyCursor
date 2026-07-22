using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MouseBeautifier
{
    /// <summary>
    /// System-tray icon built on a message-only window. Left click / double click
    /// opens the panel; right click shows a context menu (open / exit).
    /// Also owns the global "exit" hotkey (Ctrl+Shift+Q).
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private const int HOTKEY_ID_EXIT = 1;
        // Ctrl+Shift+Q — easy to remember (Q = Quit) and rarely used by other apps.
        private const uint HOTKEY_MODIFIERS = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;
        private const uint HOTKEY_VK = 0x51; /* 'Q' */
        // Fallback: Ctrl+Alt+Q
        private const uint HOTKEY_MODIFIERS_FALLBACK = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;

        private readonly string _className = "MouseBeautifierTray_" + Guid.NewGuid().ToString("N");
        private NativeMethods.WndProc _wndProc = null!;
        private IntPtr _hwnd;
        private IntPtr _hInstance;
        private NativeMethods.NOTIFYICONDATA _nid;
        private uint _taskbarCreatedMessage;
        private SafeIconHandle? _ownedIcon;
        private bool _classRegistered;
        private bool _iconAdded;
        private bool _hotkeyRegistered;
        private bool _disposed;

        public event Action? ShowPanelRequested;
        public event Action? ExitRequested;

        public TrayIcon()
        {
            Create();
        }

        private void Create()
        {
            try
            {
                _hInstance = Marshal.GetHINSTANCE(typeof(TrayIcon).Module);
                _wndProc = WndProc;
                _taskbarCreatedMessage =
                    NativeMethods.RegisterWindowMessage(
                        "TaskbarCreated");

                var wc = new NativeMethods.WNDCLASS
                {
                    style = 0,
                    lpfnWndProc =
                        Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = _hInstance,
                    lpszClassName = _className,
                };
                if (NativeMethods.RegisterClass(ref wc) == 0)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "RegisterClass for tray failed.");
                }

                _classRegistered = true;
                _hwnd = NativeMethods.CreateWindowEx(
                    0,
                    _className,
                    "",
                    0,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.HWND_MESSAGE,
                    IntPtr.Zero,
                    _hInstance,
                    IntPtr.Zero);
                if (_hwnd == IntPtr.Zero)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "CreateWindowEx for tray failed.");
                }

                _nid = new NativeMethods.NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = 1,
                    uFlags = NativeMethods.NIF_MESSAGE |
                        NativeMethods.NIF_ICON |
                        NativeMethods.NIF_TIP,
                    uCallbackMessage = NativeMethods.WM_TRAY,
                    szTip = "FunnyCursor 鼠标美化",
                };
                string baseDir = System.IO.Path.GetDirectoryName(
                    typeof(TrayIcon).Assembly.Location) ?? "";
                string icoPath = System.IO.Path.Combine(
                    baseDir,
                    "Assets",
                    "funnycursor.ico");
                if (System.IO.File.Exists(icoPath))
                {
                    IntPtr loaded = NativeMethods.LoadImage(
                        IntPtr.Zero,
                        icoPath,
                        NativeMethods.IMAGE_ICON,
                        0,
                        0,
                        NativeMethods.LR_LOADFROMFILE |
                            NativeMethods.LR_DEFAULTSIZE);
                    if (loaded != IntPtr.Zero)
                    {
                        _ownedIcon = new SafeIconHandle(loaded);
                    }
                }

                _nid.hIcon = _ownedIcon?.DangerousGetHandle() ??
                    NativeMethods.LoadIcon(
                        IntPtr.Zero,
                        (IntPtr)NativeMethods.IDI_APPLICATION);
                if (!NativeMethods.Shell_NotifyIcon(
                    NativeMethods.NIM_ADD,
                    ref _nid))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Shell_NotifyIcon add failed.");
                }

                _iconAdded = true;
                RegisterExitHotkey();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void RegisterExitHotkey()
        {
            _hotkeyRegistered = NativeMethods.RegisterHotKey(
                _hwnd,
                HOTKEY_ID_EXIT,
                HOTKEY_MODIFIERS,
                HOTKEY_VK);
            if (!_hotkeyRegistered)
            {
                App.Log(
                    "RegisterHotKey Ctrl+Shift+Q failed, trying Ctrl+Alt+Q");
                _hotkeyRegistered = NativeMethods.RegisterHotKey(
                    _hwnd,
                    HOTKEY_ID_EXIT,
                    HOTKEY_MODIFIERS_FALLBACK,
                    HOTKEY_VK);
                if (!_hotkeyRegistered)
                {
                    App.Log(
                        "RegisterHotKey fallback Ctrl+Alt+Q also failed");
                }
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

            if (_taskbarCreatedMessage != 0 &&
                msg == _taskbarCreatedMessage)
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
            using var menu = new SafeMenuHandle(
                NativeMethods.CreatePopupMenu());
            if (menu.IsInvalid)
            {
                return;
            }

            IntPtr menuHandle = menu.DangerousGetHandle();
            NativeMethods.AppendMenu(menuHandle, NativeMethods.MF_STRING, 1, "打开面板");
            NativeMethods.AppendMenu(menuHandle, NativeMethods.MF_STRING, 2, "退出 (Ctrl+Shift+Q)");
            NativeMethods.GetCursorPos(out var pt);
            NativeMethods.SetForegroundWindow(_hwnd);
            NativeMethods.TrackPopupMenu(menuHandle,
                NativeMethods.TPM_BOTTOMALIGN | NativeMethods.TPM_LEFTALIGN,
                pt.x, pt.y, 0, _hwnd, IntPtr.Zero);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_hwnd != IntPtr.Zero)
            {
                if (_hotkeyRegistered)
                {
                    NativeMethods.UnregisterHotKey(
                        _hwnd,
                        HOTKEY_ID_EXIT);
                    _hotkeyRegistered = false;
                }

                if (_iconAdded)
                {
                    NativeMethods.Shell_NotifyIcon(
                        NativeMethods.NIM_DELETE,
                        ref _nid);
                    _iconAdded = false;
                }

                NativeMethods.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            _ownedIcon?.Dispose();
            _ownedIcon = null;
            if (_classRegistered)
            {
                NativeMethods.UnregisterClass(
                    _className,
                    _hInstance);
                _classRegistered = false;
            }
        }
    }
}
