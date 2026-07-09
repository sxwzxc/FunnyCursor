using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseBeautifier
{
    public enum MouseButton { Left, Right, Middle }

    public readonly struct ClickEvent
    {
        public readonly int X; // physical screen pixel
        public readonly int Y;
        public readonly MouseButton Button;

        public ClickEvent(int x, int y, MouseButton button)
        {
            X = x; Y = y; Button = button;
        }
    }

    /// <summary>
    /// Global low-level mouse hook for click detection + polling for cursor position.
    /// The overlay window is click-through, so we rely on this hook for button events.
    /// </summary>
    public sealed class MouseTracker : IDisposable
    {
        private readonly object _lock = new();
        private readonly Queue<ClickEvent> _clicks = new();
        private NativeMethods.LowLevelMouseProc _proc = null!;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _disposed;

        public void Start()
        {
            _proc = HookCallback;
            using var cur = Process.GetCurrentProcess();
            using var mod = cur.MainModule!;
            _hookId = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(mod.ModuleName), 0);
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
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
                    lock (_lock)
                        _clicks.Enqueue(new ClickEvent(ms.pt.x, ms.pt.y, btn.Value));
                }
            }
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        /// <summary>Current cursor position in physical screen pixels.</summary>
        public void GetPosition(out int x, out int y)
        {
            NativeMethods.GetCursorPos(out var p);
            x = p.x; y = p.y;
        }

        public bool TryDequeueClick(out ClickEvent e)
        {
            lock (_lock)
            {
                if (_clicks.Count > 0) { e = _clicks.Dequeue(); return true; }
            }
            e = default;
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            GC.SuppressFinalize(this);
        }

        ~MouseTracker() => Dispose();
    }
}
