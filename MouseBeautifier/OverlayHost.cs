using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace MouseBeautifier
{
    /// <summary>
    /// Transparent, click-through, always-on-top cursor-effect overlay.
    ///
    /// Why this is NOT a WinUI 3 Window:
    /// WinUI 3 windows render through an opaque DComp swap chain, so they only
    /// become transparent via the DWM accent policy. On this machine the "true
    /// transparent" accent state renders solid black, while BLURBEHIND blurs the
    /// desktop behind (unacceptable occlusion). WinUI also ignores the classic
    /// layered-window color key — we proved that by seeing a solid magenta screen.
    ///
    /// The reliable fix is a *raw* Win32 layered window presented with
    /// UpdateLayeredWindow: we render the effects with Win2D into an off-screen
    /// CanvasRenderTarget, read the pixels back, and blit them through a 32-bit
    /// ARGB DIB. Every empty (transparent) pixel stays fully transparent, so the
    /// desktop behind shows through crisply with zero blur and zero obscuring.
    /// This is the canonical transparent-overlay technique and works on every
    /// Windows 10/11 build.
    ///
    /// The window lives on the WinUI/App UI thread (WinUI's own message loop
    /// pumps its messages), so the Win2D device is created on a thread where it
    /// is known to work.
    /// </summary>
    public sealed class OverlayHost : IDisposable
    {
        private readonly string _className = "FunnyCursorOverlay_" + Guid.NewGuid().ToString("N");
        private readonly MouseTracker _tracker = new();
        private readonly System.Diagnostics.Stopwatch _sw = new();

        private IntPtr _hwnd;
        private IntPtr _hInstance;
        private NativeMethods.WndProc _wndProc = null!;
        private IntPtr _timerId;

        private CanvasDevice? _device;
        private CanvasRenderTarget? _rt;
        private EffectRenderer? _renderer;

        private IntPtr _hdcScreen;
        private IntPtr _hdcMem;
        private IntPtr _hbm;
        private IntPtr _ppvBits;
        private byte[]? _dibBuf;

        private int _cx, _cy, _vx, _vy;
        private double _scale = 1;
        private Vector2 _cursor = Vector2.Zero;
        private int _topmostCounter;
        private bool _busy;

        public void Start()
        {
            try { Init(); }
            catch (Exception ex) { App.Log("Overlay init: " + ex); }
        }

        private void Init()
        {
            _hInstance = NativeMethods.GetModuleHandle(null);
            _wndProc = WndProc;

            var wc = new NativeMethods.WNDCLASS
            {
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = _hInstance,
                lpszClassName = _className,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
            };
            if (NativeMethods.RegisterClass(ref wc) == 0)
            {
                App.Log("Overlay: RegisterClass failed");
                return;
            }

            _hwnd = NativeMethods.CreateWindowEx(
                (uint)(NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
                     | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE),
                _className, "FunnyCursorOverlay", unchecked((uint)NativeMethods.WS_POPUP),
                0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                App.Log("Overlay: CreateWindowEx failed, lastError=" + Marshal.GetLastWin32Error());
                return;
            }
            App.Log("Overlay: window created");

            SetupResources();
            App.Log("Overlay: resources ready, loop running");
        }

        private void SetupResources()
        {
            _vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            _vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            _cx = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            _cy = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            _scale = NativeMethods.GetDpiForSystem() / 96.0;
            if (_cx <= 0 || _cy <= 0) { _cx = 1920; _cy = 1080; _vx = 0; _vy = 0; }

            _device = CanvasDevice.GetSharedDevice();
            float dipW = (float)(_cx / _scale);
            float dipH = (float)(_cy / _scale);
            // 3-arg ctor => defaults to B8G8R8A8 with CanvasAlphaMode.Premultiplied,
            // which is exactly what UpdateLayeredWindow + AC_SRC_ALPHA expects.
            _rt = new CanvasRenderTarget(_device, dipW, dipH, (float)(96 * _scale));
            using (var ds = _rt.CreateDrawingSession())
                ds.Clear(Color.FromArgb(0, 0, 0, 0));

            _renderer = new EffectRenderer(_rt);
            _renderer.SetViewport(_vx, _vy, _scale);
            _renderer.InitResources();

            // Install the global low-level mouse hook (needed for click effects).
            // Must run on the UI thread, which owns the message loop that dispatches
            // the hook callbacks.
            try { _tracker.Start(); } catch (Exception ex) { App.Log("Overlay: tracker start: " + ex); }

            // Build a 32-bit ARGB DIB (bottom-up) we push each frame via UpdateLayeredWindow.
            _hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
            _hdcMem = NativeMethods.CreateCompatibleDC(_hdcScreen);
            var bmi = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = _cx,
                    biHeight = _cy,           // positive => bottom-up
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = NativeMethods.BI_RGB,
                    biSizeImage = (uint)(_cx * _cy * 4),
                }
            };
            _hbm = NativeMethods.CreateDIBSection(_hdcScreen, ref bmi, NativeMethods.DIB_RGB_COLORS, out _ppvBits, IntPtr.Zero, 0);
            NativeMethods.SelectObject(_hdcMem, _hbm);
            _dibBuf = new byte[_cx * _cy * 4];

            NativeMethods.SetWindowPos(_hwnd, (IntPtr)NativeMethods.HWND_TOPMOST, _vx, _vy, _cx, _cy,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_FRAMECHANGED);

            _sw.Start();
            _timerId = (IntPtr)1;
            NativeMethods.SetTimer(_hwnd, _timerId, 1000 / 60, IntPtr.Zero);
        }

        private void CleanupResources()
        {
            if (_timerId != IntPtr.Zero) { NativeMethods.KillTimer(_hwnd, _timerId); _timerId = IntPtr.Zero; }
            try { _renderer?.Dispose(); } catch { }
            try { _rt?.Dispose(); } catch { }
            try { _device?.Dispose(); } catch { }
            if (_hbm != IntPtr.Zero) { NativeMethods.DeleteObject(_hbm); _hbm = IntPtr.Zero; }
            if (_hdcMem != IntPtr.Zero) { NativeMethods.DeleteDC(_hdcMem); _hdcMem = IntPtr.Zero; }
            if (_hdcScreen != IntPtr.Zero) { NativeMethods.ReleaseDC(IntPtr.Zero, _hdcScreen); _hdcScreen = IntPtr.Zero; }
        }

        private IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            if (m == NativeMethods.WM_TIMER) { RenderFrame(); return IntPtr.Zero; }
            if (m == NativeMethods.WM_CLOSE) { NativeMethods.DestroyWindow(h); return IntPtr.Zero; }
            if (m == NativeMethods.WM_DESTROY)
            {
                CleanupResources();
                try { NativeMethods.UnregisterClass(_className, _hInstance); } catch { }
                _hwnd = IntPtr.Zero;
                return IntPtr.Zero;
            }
            return NativeMethods.DefWindowProc(h, m, w, l);
        }

        private void RenderFrame()
        {
            if (_busy) return;
            // If resources aren't ready yet, just skip this tick (do NOT set _busy,
            // or rendering would stall permanently).
            if (_rt == null || _renderer == null || _dibBuf == null || _ppvBits == IntPtr.Zero) return;
            _busy = true;
            try
            {
                double dt = Math.Min(_sw.Elapsed.TotalSeconds, 0.05);
                _sw.Restart();

                if (++_topmostCounter >= 120)
                {
                    _topmostCounter = 0;
                    NativeMethods.SetWindowPos(_hwnd, (IntPtr)NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
                }

                _tracker.GetPosition(out int px, out int py);
                float dx = (float)((px - _vx) / _scale);
                float dy = (float)((py - _vy) / _scale);
                _cursor = new Vector2(dx, dy);

                _renderer.Update(dt, _cursor, _tracker);

                // Render the effects off-screen, then read the raw premultiplied BGRA
                // bytes back synchronously (GetPixelBytes is inherited from CanvasBitmap
                // and is fully synchronous — no async, so no UI-thread deadlock).
                using var ds = _rt.CreateDrawingSession();
                ds.Clear(Color.FromArgb(0, 0, 0, 0));
                _renderer.Render(ds, _cursor);

                byte[] pixels = _rt.GetPixelBytes();
                Present(pixels);
            }
            catch (Exception ex)
            {
                App.Log("Overlay render: " + ex);
            }
            finally
            {
                _busy = false;
            }
        }

        private void Present(byte[] src)
        {
            int stride = _cx * 4;
            // DIB is bottom-up; flip rows while copying. src is top-down BGRA
            // (premultiplied); the 4th byte (alpha) lands in the DIB's reserved/
            // alpha slot untouched, which UpdateLayeredWindow uses as the alpha.
            for (int y = 0; y < _cy; y++)
            {
                int srcRow = (_cy - 1 - y) * stride;
                int dstRow = y * stride;
                Buffer.BlockCopy(src, srcRow, _dibBuf!, dstRow, stride);
            }
            Marshal.Copy(_dibBuf!, 0, _ppvBits, _dibBuf!.Length);

            var pptDst = new NativeMethods.POINT { x = _vx, y = _vy };
            var pptSrc = new NativeMethods.POINT { x = 0, y = 0 };
            var psize = new NativeMethods.POINT { x = _cx, y = _cy };
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA,
            };

            var g1 = GCHandle.Alloc(pptDst, GCHandleType.Pinned);
            var g2 = GCHandle.Alloc(pptSrc, GCHandleType.Pinned);
            var g3 = GCHandle.Alloc(psize, GCHandleType.Pinned);
            try
            {
                NativeMethods.UpdateLayeredWindow(_hwnd, _hdcScreen, g1.AddrOfPinnedObject(),
                    g3.AddrOfPinnedObject(), _hdcMem, g2.AddrOfPinnedObject(), 0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                g1.Free(); g2.Free(); g3.Free();
            }
        }

        public void Dispose()
        {
            if (_hwnd != IntPtr.Zero)
                NativeMethods.PostMessage(_hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            try { _tracker.Dispose(); } catch { }
        }
    }
}
