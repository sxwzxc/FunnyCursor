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
            // CRITICAL: CanvasControl defaults to an opaque ClearColor, which makes
            // the whole swap chain opaque black. Setting transparent makes the
            // Win2D surface per-pixel alpha, so the DWM compositor blends it over
            // the windows below — this is what actually fixes the "black screen".
            _fx.ClearColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

            var icon = new Image { Visibility = Visibility.Collapsed, Stretch = Stretch.Uniform };
            var overlay = new Canvas();
            overlay.Children.Add(icon);

            var grid = new Grid();
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

            // Make the window background fully transparent (crisp, no blur) so the
            // desktop and any windows behind show through with zero obscuring.
            // ACCENT_ENABLE_TRANSPARENTGRADIENT with nColor=0 gives a completely
            // transparent surface; the Win2D swap chain's per-pixel-alpha content
            // (transparent ClearColor set in the ctor) then draws only the effects
            // on top — nothing else is painted over the screen.
            //
            // (BLURBEHIND would blur the desktop behind; we deliberately avoid it
            // because the user wants the content behind to stay perfectly clear.)
            ApplyTransparency();

            PositionFullScreen();
        }

        /// <summary>
        /// Strips the default WS_OVERLAPPEDWINDOW frame and applies the overlay's
        /// extended styles: tool window (no taskbar) + transparent (click-through)
        /// + topmost (always on top).
        /// NOTE: WS_EX_LAYERED is intentionally NOT set — WinUI 3 / Win2D render
        /// through a DXGI swap chain whose alpha the DWM compositor blends
        /// directly; adding WS_EX_LAYERED + SetLayeredWindowAttributes forces an
        /// opaque layered composite that shows up as the "black screen" bug.
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
            ex |= NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_TRANSPARENT
                | NativeMethods.WS_EX_TOPMOST
                | NativeMethods.WS_EX_NOACTIVATE;
            // Remove WS_EX_LAYERED if present — it breaks Win2D alpha compositing
            // and conflicts with the BLURBEHIND accent below.
            ex &= ~NativeMethods.WS_EX_LAYERED;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)ex);
        }

        /// <summary>
        /// Applies a DWM transparent-gradient accent (nColor=0 → fully transparent)
        /// so the windows beneath the overlay show through crisply with no blur.
        /// This is what makes the overlay transparent instead of a solid black
        /// surface. The Win2D effects (transparent ClearColor) then render on top.
        /// </summary>
        private void ApplyTransparency()
        {
            var accent = new NativeMethods.ACCENTPOLICY
            {
                nAccentState = NativeMethods.ACCENT_ENABLE_TRANSPARENTGRADIENT,
                nFlags = 0,
                nColor = 0,
                nAnimationId = 0,
            };
            int size = Marshal.SizeOf<NativeMethods.ACCENTPOLICY>();
            IntPtr p = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, p, false);
            var data = new NativeMethods.WINCOMPATTRDATA
            {
                nAttribute = NativeMethods.WCA_ACCENT_POLICY,
                pData = p,
                ulDataSize = size,
            };
            NativeMethods.SetWindowCompositionAttribute(_hwnd, ref data);
            Marshal.FreeHGlobal(p);
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
