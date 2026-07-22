using System;
using Microsoft.Win32.SafeHandles;

namespace MouseBeautifier
{
    internal sealed class SafeScreenDcHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly IntPtr _window;

        internal SafeScreenDcHandle(IntPtr handle, IntPtr window)
            : base(true)
        {
            _window = window;
            SetHandle(handle);
        }

        protected override bool ReleaseHandle() =>
            NativeMethods.ReleaseDC(_window, handle) != 0;
    }

    internal sealed class SafeMemoryDcHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeMemoryDcHandle(IntPtr handle)
            : base(true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle() =>
            NativeMethods.DeleteDC(handle);
    }

    internal sealed class SafeGdiObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeGdiObjectHandle(IntPtr handle)
            : base(true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle() =>
            NativeMethods.DeleteObject(handle);
    }

    internal sealed class SafeHookHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeHookHandle(IntPtr handle)
            : base(true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle() =>
            NativeMethods.UnhookWindowsHookEx(handle);
    }

    internal sealed class SafeMenuHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeMenuHandle(IntPtr handle)
            : base(true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle() =>
            NativeMethods.DestroyMenu(handle);
    }

    internal sealed class SafeIconHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeIconHandle(IntPtr handle)
            : base(true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle() =>
            NativeMethods.DestroyIcon(handle);
    }
}
