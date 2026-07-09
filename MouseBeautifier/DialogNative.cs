using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MouseBeautifier
{
    /// <summary>
    /// Win32 P/Invoke surface used by the settings dialog. Isolated from
    /// NativeMethods so the (working) tray icon code is never touched.
    /// </summary>
    internal static class DlgNative
    {
        // ---- Window classes ----
        public const string WC_BUTTON = "Button";
        public const string WC_STATIC = "Static";
        public const string WC_EDIT = "Edit";
        public const string WC_COMBOBOX = "ComboBox";
        public const string TRACKBAR_CLASS = "msctls_trackbar32";

        // ---- Standard styles ----
        public const int WS_CHILD = 0x40000000;
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_TABSTOP = 0x10000;
        public const int WS_BORDER = 0x00800000;
        public const int WS_CAPTION = 0x00C00000;
        public const int WS_SYSMENU = 0x00080000;
        public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
        public const int WS_EX_CLIENTEDGE = 0x00000200;

        // ---- Button styles ----
        public const int BS_CHECKBOX = 0x0002;
        public const int BS_AUTOCHECKBOX = 0x0003;
        public const int BS_GROUPBOX = 0x0007;
        public const int BS_PUSHBUTTON = 0x0000;
        public const int BS_LEFT = 0x00000100;
        public const int BS_MULTILINE = 0x00002000;

        // ---- Edit styles ----
        public const int ES_AUTOHSCROLL = 0x0080;
        public const int ES_NUMBER = 0x2000;

        // ---- Combo styles ----
        public const int CBS_DROPDOWNLIST = 0x0003;
        public const int CBS_HASSTRINGS = 0x0200;

        // ---- Static styles ----
        public const int SS_LEFT = 0x00000000;
        public const int SS_NOTIFY = 0x0100;

        // ---- Messages ----
        public const int WM_CREATE = 0x0001;
        public const int WM_COMMAND = 0x0111;
        public const int WM_HSCROLL = 0x0114;
        public const int WM_VSCROLL = 0x0115;
        public const int WM_CLOSE = 0x0010;
        public const int WM_DESTROY = 0x0002;
        public const int WM_PAINT = 0x000F;
        public const int BN_CLICKED = 0;
        public const int CBN_SELCHANGE = 1;
        public const int EN_CHANGE = 0x0300;

        // ---- Scrollbar ----
        public const int WS_VSCROLL = 0x00200000;
        public const int SB_VERT = 1;
        public const int SB_LINEUP = 0;
        public const int SB_LINEDOWN = 1;
        public const int SB_PAGEUP = 2;
        public const int SB_PAGEDOWN = 3;
        public const int SB_THUMBTRACK = 4;
        public const int SB_THUMBPOSITION = 5;
        public const int SB_ENDSCROLL = 8;

        // ---- Trackbar messages ----
        public const int TBM_SETRANGE = 0x0405;
        public const int TBM_SETPOS = 0x0406;
        public const int TBM_GETPOS = 0x0400;
        public const int TBM_SETTICFREQ = 0x0409;

        // ---- Combo messages ----
        public const int CB_ADDSTRING = 0x0143;
        public const int CB_RESETCONTENT = 0x014B;
        public const int CB_GETCURSEL = 0x0147;
        public const int CB_SETCURSEL = 0x014E;
        public const int CB_GETLBTEXT = 0x0148;
        public const int CB_GETLBTEXTLEN = 0x0149;

        // ---- Edit messages ----
        public const int EM_LIMITTEXT = 0x00C5;

        // ---- ShowWindow ----
        public const int SW_SHOW = 5;
        public const int SW_HIDE = 0;
        public const int CW_USEDEFAULT = unchecked((int)0x80000000);

        // ---- Common controls ----
        public const int ICC_STANDARD_CLASSES = 0x00002000;

        // ---- ChooseColor ----
        public const int CC_ANYCOLOR = 0x00000100;
        public const int CC_RGBINIT = 0x00000001;
        public const int CC_FULLOPEN = 0x00000002;

        // ---- GetOpenFileName ----
        public const int OFN_FILEMUSTEXIST = 0x00001000;

        // ---- Icon loading (shared with NativeMethods) ----
        public const int IMAGE_ICON = 1;
        public const int LR_LOADFROMFILE = 0x00000010;
        public const int LR_DEFAULTSIZE = 0x00000040;
        public const int WM_SETICON = 0x0080;
        public const int ICON_SMALL = 0;
        public const int ICON_BIG = 1;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint flags);
        public const int OFN_PATHMUSTEXIST = 0x00000800;
        public const int OFN_EXPLORER = 0x00080000;

        [StructLayout(LayoutKind.Sequential)]
        public struct INITCOMMONCONTROLSEX
        {
            public uint dwSize;
            public uint dwICC;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left, top, right, bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CHOOSECOLOR
        {
            public uint lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public int rgbResult;
            public IntPtr lpCustColors;
            public uint Flags;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public IntPtr lpTemplateName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct OPENFILENAME
        {
            public uint lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public uint nMaxCustFilter;
            public uint nFilterIndex;
            public string lpstrFile;
            public uint nMaxFile;
            public string lpstrFileTitle;
            public uint nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public uint Flags;
            public ushort nFileOffset;
            public ushort nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public uint dwReserved;
            public uint FlagsEx;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName,
            string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDlgItem(IntPtr hDlg, int nID);

        [DllImport("user32.dll")]
        public static extern uint IsDlgButtonChecked(IntPtr hDlg, int nIDButton);

        [DllImport("user32.dll")]
        public static extern bool CheckDlgButton(IntPtr hDlg, int nIDButton, uint uCheck);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetWindowText(IntPtr hWnd, string lpString);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

        [DllImport("comctl32.dll", SetLastError = true)]
        public static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

        [DllImport("comdlg32.dll", SetLastError = true)]
        public static extern bool ChooseColor(ref CHOOSECOLOR lpcc);

        [DllImport("comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool GetOpenFileName(ref OPENFILENAME ofn);

        [DllImport("user32.dll")]
        public static extern bool SetScrollRange(IntPtr hWnd, int nBar, int nMinPos, int nMaxPos, bool bRedraw);

        [DllImport("user32.dll")]
        public static extern int SetScrollPos(IntPtr hWnd, int nBar, int nPos, bool bRedraw);

        [DllImport("user32.dll")]
        public static extern int GetScrollPos(IntPtr hWnd, int nBar);

        [DllImport("user32.dll")]
        public static extern int GetDlgCtrlID(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);
        public static readonly IntPtr HWND_TOPMOST = (IntPtr)(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }
    }
}
