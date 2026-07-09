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

        // Color-key for the layered window: every pixel of this exact color becomes
        // fully transparent, so the desktop behind shows through with zero blur and
        // zero obscuring. Magenta (255,0,255) is chosen because it is virtually never
        // present in cursor-effect colors or icon art, so it won't accidentally erase
        // any drawn effect. COLORREF format is 0x00BBGGRR.
        private const int TRANSPARENT_KEY = 0x00FF00FF;

        public OverlayWindow()
        {
            _fx = new CanvasControl();
            // CRITICAL: CanvasControl defaults to an opaque ClearColor, which makes
            // the whole swap chain opaque black. Setting transparent makes the
            // Win2D surface per-pixel alpha, so the DWM compositor blends it over
            // the windows below — this is what actually fixes the "black screen".
            _fx.ClearColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

            var icon = new Image { Visibility = Visibility.Collapsed, Stretch = Stretch.Uniform };
            var overlay = new Canvas();
            overlay.Children.Add(icon);

            var grid = new Grid();
            // Opaque magenta = the color-key. The Win2D canvas clears to transparent
            // and paints effects on top, so the only magenta pixels left are the
            // "empty" areas — those get keyed out, making the overlay see-through.
            grid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 0, 255));
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

            // Make the window fully transparent (crisp, no blur) via a layered
            // window + color key, so the desktop behind shows through unchanged.
            ApplyTransparency();

            PositionFullScreen();
        }

        /// <summary>
        /// Strips the default WS_OVERLAPPEDWINDOW frame and applies the overlay's
        /// extended styles. WS_EX_LAYERED is REQUIRED here — it is what enables the
        /// color-key transparency (see ApplyTransparency); it also pairs with
        /// WS_EX_TRANSPARENT for click-through and WS_EX_TOPMOST for always-on-top.
        /// </summary>
        private void ApplyWindowStyles()
        {
            // Remove the default frame / title bar by switching to WS_POPUP.
            int style = (int)NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE);
            style &= ~NativeMethods.WS_OVERLAPPEDWINDOW;
            style |= NativeMethods.WS_POPUP | NativeMethods.WS_VISIBLE;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE, (IntPtr)style);

            // Extended styles for a transparent click-through overlay:
            //  - WS_EX_LAYERED      : enables per-pixel transparency via a color key
            //  - WS_EX_TRANSPARENT  : mouse hits pass through to windows below
            //  - WS_EX_TOPMOST      : always on top
            //  - WS_EX_TOOLWINDOW   : no taskbar button
            //  - WS_EX_NOACTIVATE   : never steals focus
            int ex = (int)NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
            ex |= NativeMethods.WS_EX_LAYERED
                | NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_TRANSPARENT
                | NativeMethods.WS_EX_TOPMOST
                | NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)ex);
        }

        /// <summary>
        /// Makes the overlay see-through using a layered window + color key
        /// (the classic, maximally-compatible transparent-overlay technique).
        /// Every pixel matching TRANSPARENT_KEY (opaque magenta) is made fully
        /// transparent, so the desktop and any windows behind show through with
        /// zero blur and zero obscuring. Only the Win2D effects (which never paint
        /// magenta) remain visible.
        /// This deliberately avoids the DWM accent policy: its "true transparent"
        /// state is unreliable across Windows builds and was rendering solid black
        /// on the user's machine, whereas the layered/color-key mechanism is stable
        /// on every Windows 10/11 version.
        /// </summary>
        private void ApplyTransparency()
        {
            bool ok = NativeMethods.SetLayeredWindowAttributes(_hwnd, (uint)TRANSPARENT_KEY, 0, NativeMethods.LWA_COLORKEY);
            App.Log("Layered color-key applied (key=0x" + TRANSPARENT_KEY.ToString("X8") + ", ok=" + ok + ")");
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
            ApplyTransparency();
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
