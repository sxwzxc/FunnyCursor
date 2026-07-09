using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace MouseBeautifier
{
    public sealed class OverlayWindow : Window
    {
        private readonly MouseTracker _tracker = new();
        private readonly EffectRenderer _renderer;
        private readonly DispatcherQueueTimer _timer;
        private readonly System.Diagnostics.Stopwatch _sw = new();
        private CanvasControl _fx = null!;
        private Vector2 _cursor = Vector2.Zero;
        private IntPtr _hwnd;
        private int _vx, _vy;
        private double _scale = 1;
        private bool _configured;

        public OverlayWindow()
        {
            _fx = new CanvasControl();
            var icon = new Image { Visibility = Visibility.Collapsed, Stretch = Stretch.Uniform };
            var overlay = new Canvas();
            overlay.Children.Add(icon);

            var grid = new Grid();
            grid.Children.Add(_fx);
            grid.Children.Add(overlay);
            this.Content = grid;

            _fx.Draw += FxCanvas_Draw;
            _renderer = new EffectRenderer(_fx, icon);
            _fx.CreateResources += (_, _) => _renderer.InitResources();

            _hwnd = WindowNative.GetWindowHandle(this);
            Configure();

            _timer = this.DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / 120.0);
            _timer.Tick += OnTick;

            this.Closed += (_, _) => Cleanup();
        }

        private void Configure()
        {
            if (_configured) return;
            _configured = true;

            // Keep the overlay out of the taskbar via the tool-window extended style.
            int ex0 = (int)NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
            ex0 |= NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)ex0);

            this.ExtendsContentIntoTitleBar = true;

            // Make the window background fully transparent via DWM accent.
            var accent = new NativeMethods.ACCENTPOLICY
            {
                nAccentState = NativeMethods.ACCENT_ENABLE_TRANSPARENTGRADIENT,
                nFlags = 2,
                nColor = 0,
            };
            var data = new NativeMethods.WINCOMPATTRDATA
            {
                nAttribute = NativeMethods.WCA_ACCENT_POLICY,
                pData = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.ACCENTPOLICY>()),
                ulDataSize = Marshal.SizeOf<NativeMethods.ACCENTPOLICY>(),
            };
            Marshal.StructureToPtr(accent, data.pData, false);
            NativeMethods.SetWindowCompositionAttribute(_hwnd, ref data);
            Marshal.FreeHGlobal(data.pData);

            // Click-through + always on top.
            int ex = (int)NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
            ex |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOPMOST;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)ex);

            // Cover the whole virtual screen (all monitors).
            _vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            _vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int cx = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int cy = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            NativeMethods.SetWindowPos(_hwnd, (IntPtr)NativeMethods.HWND_TOPMOST,
                _vx, _vy, cx, cy, NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

            _scale = NativeMethods.GetDpiForSystem() / 96.0;
            _renderer.SetViewport(_vx, _vy, _scale);
        }

        public void Start()
        {
            _tracker.Start();
            _sw.Start();
            _timer.Start();
        }

        private int _tickErrors;

        private void OnTick(object? sender, object e)
        {
            try
            {
                double dt = Math.Min(_sw.Elapsed.TotalSeconds, 0.05);
                _sw.Restart();

                _tracker.GetPosition(out int px, out int py);
                float dx = (float)((px - _vx) / _scale);
                float dy = (float)((py - _vy) / _scale);
                _cursor = new Vector2(dx, dy);

                _renderer.Update(dt, _cursor, _tracker);
                // Request a redraw; the FxCanvas_Draw handler paints the frame.
                _fx.Invalidate();
            }
            catch (Exception ex)
            {
                if (_tickErrors++ < 3)
                    App.Log("OnTick exception: " + ex);
                _timer.Stop();
            }
        }

        private void FxCanvas_Draw(object sender, CanvasDrawEventArgs e)
        {
            try
            {
                _renderer.Render(e.DrawingSession, _cursor);
            }
            catch (Exception ex)
            {
                App.Log("FxCanvas_Draw exception: " + ex);
            }
        }

        private void Cleanup()
        {
            _timer?.Stop();
            _tracker.Dispose();
            _renderer.Dispose();
        }
    }
}
