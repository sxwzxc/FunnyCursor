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
            // Explicit transparent background so the Win2D swap chain's per-pixel
            // alpha is composited correctly over windows below.
            grid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
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

            ApplyWindowStyles();

            // Make the window background transparent via DWM accent.
            // ACCENT_ENABLE_BLURBEHIND with color=0 renders as a fully transparent
            // background that lets the windows behind show through — unlike
            // ACCENT_ENABLE_TRANSPARENTGRADIENT which only paints a translucent
            // gradient (and still shows the window's default opaque backing).
            var accent = new NativeMethods.ACCENTPOLICY
            {
                nAccentState = NativeMethods.ACCENT_ENABLE_BLURBEHIND,
                nFlags = 0,
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

            PositionFullScreen();
        }

        /// <summary>
        /// Strips the default WS_OVERLAPPEDWINDOW frame and applies the overlay's
        /// extended styles: layered (per-pixel alpha) + tool window (no taskbar) +
        /// transparent (click-through) + topmost (always on top).
        /// </summary>
        private void ApplyWindowStyles()
        {
            // Remove the default frame / title bar by switching to WS_POPUP.
            int style = (int)NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE);
            style &= ~NativeMethods.WS_OVERLAPPEDWINDOW;
            style |= NativeMethods.WS_POPUP | NativeMethods.WS_VISIBLE;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE, (IntPtr)style);

            // Extended styles needed for a transparent click-through overlay.
            int ex = (int)NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
            ex |= NativeMethods.WS_EX_LAYERED
                | NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_TRANSPARENT
                | NativeMethods.WS_EX_TOPMOST;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)ex);

            // For layered windows: alpha=255 (don't reduce overall opacity),
            // no color key — per-pixel alpha comes from the DWM + Win2D swap chain.
            NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 255, NativeMethods.LWA_ALPHA);
        }

        /// <summary>
        /// Sizes the overlay to cover the entire virtual screen (all monitors) and
        /// forces it to the top of the z-order. SWP_FRAMECHANGED re-evaluates the
        /// window frame after the style changes above.
        /// </summary>
        private void PositionFullScreen()
        {
            _vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            _vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int cx = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int cy = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            NativeMethods.SetWindowPos(_hwnd, (IntPtr)NativeMethods.HWND_TOPMOST,
                _vx, _vy, cx, cy,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_FRAMECHANGED);

            _lastScreenW = cx;
            _lastScreenH = cy;
            _scale = NativeMethods.GetDpiForSystem() / 96.0;
            _renderer.SetViewport(_vx, _vy, _scale);
        }

        public void Start()
        {
            // Re-apply styles + position after Activate(); WinUI's Window.Activate()
            // can reset the frame and z-order, so we force them back here.
            ApplyWindowStyles();
            PositionFullScreen();

            _tracker.Start();
            _sw.Start();
            _timer.Start();
        }

        private int _tickErrors;
        private int _topmostCounter;
        private int _lastScreenW, _lastScreenH;

        private void OnTick(object? sender, object e)
        {
            try
            {
                double dt = Math.Min(_sw.Elapsed.TotalSeconds, 0.05);
                _sw.Restart();

                // Periodically re-assert topmost + re-cover the virtual screen so
                // the overlay stays on top of other apps and survives resolution /
                // monitor changes. (~every 2 s at 120 fps)
                if (++_topmostCounter >= 240)
                {
                    _topmostCounter = 0;
                    int cw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
                    int ch = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
                    if (cw != _lastScreenW || ch != _lastScreenH)
                    {
                        // Display configuration changed — resize and re-position.
                        PositionFullScreen();
                        _lastScreenW = cw;
                        _lastScreenH = ch;
                    }
                    else
                    {
                        NativeMethods.SetWindowPos(_hwnd, (IntPtr)NativeMethods.HWND_TOPMOST,
                            0, 0, 0, 0,
                            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
                    }
                }

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
