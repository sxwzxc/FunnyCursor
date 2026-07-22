using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MouseBeautifier.Core;

namespace MouseBeautifier
{
    public enum MouseButton { Left, Right, Middle }

    public readonly struct ClickEvent
    {
        public readonly int X; // physical screen pixel
        public readonly int Y;
        public readonly MouseButton Button;
        public readonly long Timestamp;

        public ClickEvent(
            int x,
            int y,
            MouseButton button,
            long timestamp)
        {
            X = x;
            Y = y;
            Button = button;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Global low-level mouse hook for click detection + polling for cursor position.
    /// The overlay window is click-through, so we rely on this hook for button events.
    /// </summary>
    public sealed class MouseTracker : IDisposable
    {
        private readonly TimestampedInputQueue<ClickEvent> _clicks =
            new(256);
        private NativeMethods.LowLevelMouseProc _proc = null!;
        private SafeHookHandle? _hook;
        private bool _disposed;

        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hook is { IsInvalid: false })
            {
                return;
            }

            _proc = HookCallback;
            using var cur = Process.GetCurrentProcess();
            using var mod = cur.MainModule!;
            IntPtr hook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL,
                _proc,
                NativeMethods.GetModuleHandle(mod.ModuleName),
                0);
            if (hook == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "SetWindowsHookEx failed.");
            }

            _hook = new SafeHookHandle(hook);
        }

        public void Stop()
        {
            _hook?.Dispose();
            _hook = null;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                var ms = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                MouseButton? btn = null;
                if (msg == NativeMethods.WM_LBUTTONDOWN) btn = MouseButton.Left;
                else if (msg == NativeMethods.WM_RBUTTONDOWN) btn = MouseButton.Right;
                else if (msg == NativeMethods.WM_MBUTTONDOWN) btn = MouseButton.Middle;

                if (btn.HasValue)
                {
                    long timestamp = Stopwatch.GetTimestamp();
                    _clicks.Enqueue(
                        timestamp,
                        new ClickEvent(
                            ms.pt.x,
                            ms.pt.y,
                            btn.Value,
                            timestamp));
                }
            }
            return NativeMethods.CallNextHookEx(
                _hook?.DangerousGetHandle() ?? IntPtr.Zero,
                nCode,
                wParam,
                lParam);
        }

        /// <summary>Current cursor position in physical screen pixels.</summary>
        public void GetPosition(out int x, out int y)
        {
            NativeMethods.GetCursorPos(out var p);
            x = p.x; y = p.y;
        }

        public bool TryDequeueClick(
            long inclusiveTimestamp,
            out ClickEvent e)
        {
            if (_clicks.TryDequeueUpTo(
                inclusiveTimestamp,
                out TimestampedInput<ClickEvent> input))
            {
                e = input.Value;
                return true;
            }

            e = default;
            return false;
        }

        public static long GetTimestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
