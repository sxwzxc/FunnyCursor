using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using MouseBeautifier.Core;
using Windows.UI;
using WinRT;

namespace MouseBeautifier
{
    /// <summary>
    /// Owns one simulation clock and one raw layered window per physical monitor.
    /// The world advances once; all monitor-local renderers consume the same
    /// completed snapshot before the next update.
    /// </summary>
    public sealed class OverlayHost : IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly string _className =
            "FunnyCursorOverlay_" + Guid.NewGuid().ToString("N");
        private readonly MouseTracker _tracker = new();
        private readonly Stopwatch _clock = new();
        private readonly Dictionary<IntPtr, OverlaySurface> _byMonitor = new();
        private readonly Dictionary<IntPtr, OverlaySurface> _byWindow = new();
        private readonly Dictionary<string, long> _lastLog = new();

        private NativeMethods.WndProc _wndProc = null!;
        private IntPtr _instance;
        private IntPtr _controlWindow;
        private IntPtr _timerId;
        private SafeScreenDcHandle? _screenDc;
        private CanvasDevice? _device;
        private EffectWorld? _world;
        private bool _classRegistered;
        private bool _layoutDirty;
        private bool _rendering;
        private bool _started;
        private bool _disposed;
        private int _topmostCounter;

        public OverlayHost(ISettingsService settingsService)
        {
            _settingsService = settingsService ??
                throw new ArgumentNullException(nameof(settingsService));
        }

        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            try
            {
                Initialize();
                _started = true;
                App.Log(
                    $"Overlay: ready with {_byMonitor.Count} monitor surface(s)");
            }
            catch
            {
                DisposeResources();
                throw;
            }
        }

        private void Initialize()
        {
            _instance = NativeMethods.GetModuleHandle(null);
            _wndProc = WndProc;
            var windowClass = new NativeMethods.WNDCLASS
            {
                lpfnWndProc =
                    Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = _instance,
                lpszClassName = _className,
            };
            if (NativeMethods.RegisterClass(ref windowClass) == 0)
            {
                throw LastError("RegisterClass for overlay failed.");
            }

            _classRegistered = true;
            _controlWindow = NativeMethods.CreateWindowEx(
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
                _instance,
                IntPtr.Zero);
            if (_controlWindow == IntPtr.Zero)
            {
                throw LastError("CreateWindowEx for overlay control failed.");
            }

            IntPtr dc = NativeMethods.GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
            {
                throw LastError("GetDC for overlay failed.");
            }

            _screenDc = new SafeScreenDcHandle(dc, IntPtr.Zero);
            _device = CanvasDevice.GetSharedDevice();
            _world = new EffectWorld(LogThrottled);
            _tracker.Start();
            RefreshMonitorSurfaces();

            _timerId = NativeMethods.SetTimer(
                _controlWindow,
                (IntPtr)1,
                1000 / 60,
                IntPtr.Zero);
            if (_timerId == IntPtr.Zero)
            {
                throw LastError("SetTimer for overlay failed.");
            }

            _clock.Start();
        }

        private IntPtr WndProc(
            IntPtr window,
            uint message,
            IntPtr wParam,
            IntPtr lParam)
        {
            if (message == NativeMethods.WM_TIMER &&
                window == _controlWindow)
            {
                RenderFrame();
                return IntPtr.Zero;
            }

            if (message == NativeMethods.WM_DISPLAYCHANGE)
            {
                _layoutDirty = true;
                return IntPtr.Zero;
            }

            if (message == NativeMethods.WM_DPICHANGED &&
                _byWindow.TryGetValue(window, out OverlaySurface? surface))
            {
                uint packedDpi = unchecked((uint)wParam.ToInt64());
                uint dpiX = packedDpi & 0xffff;
                uint dpiY = packedDpi >> 16;
                NativeMethods.RECT suggested =
                    Marshal.PtrToStructure<NativeMethods.RECT>(lParam);
                surface.Resize(
                    ToPixelRect(suggested),
                    NormalizeDpi(dpiX),
                    NormalizeDpi(dpiY),
                    _device!);
                _layoutDirty = true;
                return IntPtr.Zero;
            }

            if (message == NativeMethods.WM_CLOSE)
            {
                NativeMethods.DestroyWindow(window);
                return IntPtr.Zero;
            }

            return NativeMethods.DefWindowProc(
                window,
                message,
                wParam,
                lParam);
        }

        private void RenderFrame()
        {
            if (_rendering ||
                _disposed ||
                _world == null)
            {
                return;
            }

            _rendering = true;
            try
            {
                if (_device == null)
                {
                    RecoverDevice();
                    if (_device == null)
                    {
                        return;
                    }
                }

                if (_layoutDirty)
                {
                    _layoutDirty = false;
                    RefreshMonitorSurfaces();
                }

                double elapsed = _clock.Elapsed.TotalSeconds;
                _clock.Restart();
                long timestamp = MouseTracker.GetTimestamp();
                _tracker.GetPosition(out int cursorX, out int cursorY);
                Vector2 cursor = new(cursorX, cursorY);

                while (_tracker.TryDequeueClick(
                    timestamp,
                    out ClickEvent click))
                {
                    _world.EnqueueClick(
                        click.Timestamp,
                        new Vector2(click.X, click.Y));
                }

                AppSettings settings = _settingsService.Current;
                _world.AdvanceFrame(
                    elapsed,
                    timestamp,
                    cursor,
                    settings);
                EffectFrameSnapshot frame =
                    _world.CaptureSnapshot(cursor);
                NebulaRenderSettings nebula =
                    (settings.Nebula ?? new NebulaSettings())
                        .ToRenderSettings();

                bool refreshTopmost = ++_topmostCounter >= 120;
                if (refreshTopmost)
                {
                    _topmostCounter = 0;
                }

                foreach (OverlaySurface surface in
                    _byMonitor.Values)
                {
                    try
                    {
                        surface.Render(
                            frame,
                            nebula,
                            refreshTopmost);
                    }
                    catch (Exception ex) when (
                        _device?.IsDeviceLost(ex.HResult) == true)
                    {
                        RecoverDevice();
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogThrottled(
                            $"Overlay render ({surface.DeviceName}): {ex}");
                    }
                }
            }
            catch (Exception ex) when (
                _device?.IsDeviceLost(ex.HResult) == true)
            {
                RecoverDevice();
            }
            catch (Exception ex)
            {
                LogThrottled("Overlay frame: " + ex);
            }
            finally
            {
                _rendering = false;
            }
        }

        private void RecoverDevice()
        {
            LogThrottled("Overlay: graphics device lost; rebuilding resources");
            try
            {
                _device?.Dispose();
                _device = null;
                CanvasDevice replacement =
                    CanvasDevice.GetSharedDevice();
                _device = replacement;
                foreach (OverlaySurface surface in _byMonitor.Values)
                {
                    surface.RecreateDeviceResources(replacement);
                }
            }
            catch (Exception ex)
            {
                LogThrottled("Overlay device recovery: " + ex);
                _layoutDirty = true;
            }
        }

        private void RefreshMonitorSurfaces()
        {
            if (_device == null || _screenDc == null)
            {
                return;
            }

            IReadOnlyList<MonitorDescription> monitors =
                EnumerateMonitors();
            var active = new HashSet<IntPtr>(
                monitors.Select(monitor => monitor.Handle));

            foreach (IntPtr removed in _byMonitor.Keys
                .Where(handle => !active.Contains(handle))
                .ToArray())
            {
                OverlaySurface surface = _byMonitor[removed];
                _byMonitor.Remove(removed);
                _byWindow.Remove(surface.Window);
                surface.Dispose();
            }

            foreach (MonitorDescription monitor in monitors)
            {
                if (_byMonitor.TryGetValue(
                    monitor.Handle,
                    out OverlaySurface? existing))
                {
                    existing.Resize(
                        monitor.Bounds,
                        monitor.DpiX,
                        monitor.DpiY,
                        _device);
                    continue;
                }

                CreateSurface(monitor);
            }
        }

        private void CreateSurface(MonitorDescription monitor)
        {
            IntPtr window = NativeMethods.CreateWindowEx(
                (uint)(NativeMethods.WS_EX_LAYERED |
                    NativeMethods.WS_EX_TRANSPARENT |
                    NativeMethods.WS_EX_TOPMOST |
                    NativeMethods.WS_EX_TOOLWINDOW |
                    NativeMethods.WS_EX_NOACTIVATE),
                _className,
                "FunnyCursorOverlay",
                unchecked((uint)NativeMethods.WS_POPUP),
                monitor.Bounds.Left,
                monitor.Bounds.Top,
                monitor.Bounds.Width,
                monitor.Bounds.Height,
                IntPtr.Zero,
                IntPtr.Zero,
                _instance,
                IntPtr.Zero);
            if (window == IntPtr.Zero)
            {
                throw LastError(
                    $"CreateWindowEx failed for {monitor.DeviceName}.");
            }

            OverlaySurface? surface = null;
            try
            {
                surface = new OverlaySurface(
                    window,
                    monitor,
                    _screenDc!,
                    _device!,
                    _settingsService,
                    LogThrottled);
                _byMonitor.Add(monitor.Handle, surface);
                _byWindow.Add(window, surface);
            }
            catch
            {
                surface?.Dispose();
                if (surface == null)
                {
                    NativeMethods.DestroyWindow(window);
                }

                throw;
            }
        }

        private static IReadOnlyList<MonitorDescription>
            EnumerateMonitors()
        {
            var monitors = new List<MonitorDescription>();
            NativeMethods.MonitorEnumProc callback =
                delegate(
                    IntPtr handle,
                    IntPtr monitorDc,
                    ref NativeMethods.RECT monitorRect,
                    IntPtr data)
                {
                    var info = new NativeMethods.MONITORINFOEX
                    {
                        cbSize = Marshal.SizeOf<
                            NativeMethods.MONITORINFOEX>(),
                        szDevice = string.Empty,
                    };
                    if (!NativeMethods.GetMonitorInfo(
                        handle,
                        ref info))
                    {
                        return true;
                    }

                    uint dpiX = 96;
                    uint dpiY = 96;
                    if (NativeMethods.GetDpiForMonitor(
                        handle,
                        NativeMethods.MDT_EFFECTIVE_DPI,
                        out uint reportedX,
                        out uint reportedY) >= 0)
                    {
                        dpiX = reportedX;
                        dpiY = reportedY;
                    }

                    monitors.Add(new MonitorDescription(
                        handle,
                        info.szDevice,
                        ToPixelRect(info.rcMonitor),
                        NormalizeDpi(dpiX),
                        NormalizeDpi(dpiY)));
                    return true;
                };

            if (!NativeMethods.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                callback,
                IntPtr.Zero))
            {
                throw LastError("EnumDisplayMonitors failed.");
            }

            return monitors;
        }

        private void LogThrottled(string message)
        {
            string key = message.Length > 160
                ? message[..160]
                : message;
            long now = Environment.TickCount64;
            if (_lastLog.TryGetValue(key, out long previous) &&
                now - previous < 5000)
            {
                return;
            }

            _lastLog[key] = now;
            App.Log(message);
        }

        private static PixelRect ToPixelRect(NativeMethods.RECT rect) =>
            new(
                rect.left,
                rect.top,
                Math.Max(0, rect.right - rect.left),
                Math.Max(0, rect.bottom - rect.top));

        private static float NormalizeDpi(uint dpi) =>
            dpi is >= 48 and <= 768 ? dpi : 96;

        private static Win32Exception LastError(string message) =>
            new(Marshal.GetLastWin32Error(), message);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeResources();
            GC.SuppressFinalize(this);
        }

        private void DisposeResources()
        {
            if (_timerId != IntPtr.Zero &&
                _controlWindow != IntPtr.Zero)
            {
                NativeMethods.KillTimer(
                    _controlWindow,
                    _timerId);
                _timerId = IntPtr.Zero;
            }

            _clock.Stop();
            _tracker.Dispose();

            foreach (OverlaySurface surface in
                _byMonitor.Values.ToArray())
            {
                surface.Dispose();
            }

            _byMonitor.Clear();
            _byWindow.Clear();

            if (_controlWindow != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(_controlWindow);
                _controlWindow = IntPtr.Zero;
            }

            _device?.Dispose();
            _device = null;
            _screenDc?.Dispose();
            _screenDc = null;
            _world = null;

            if (_classRegistered)
            {
                NativeMethods.UnregisterClass(
                    _className,
                    _instance);
                _classRegistered = false;
            }

            _started = false;
        }

        private readonly record struct MonitorDescription(
            IntPtr Handle,
            string DeviceName,
            PixelRect Bounds,
            float DpiX,
            float DpiY);

        private sealed class OverlaySurface : IDisposable
        {
            private readonly SafeScreenDcHandle _screenDc;
            private readonly ISettingsService _settingsService;
            private readonly Action<string> _log;
            private CanvasRenderTarget? _target;
            private EffectRenderer? _renderer;
            private DibSurface? _dib;
            private PixelRect _bounds;
            private float _dpiX;
            private float _dpiY;
            private bool _disposed;

            internal OverlaySurface(
                IntPtr window,
                MonitorDescription monitor,
                SafeScreenDcHandle screenDc,
                CanvasDevice device,
                ISettingsService settingsService,
                Action<string> log)
            {
                Window = window;
                DeviceName = monitor.DeviceName;
                _screenDc = screenDc;
                _settingsService = settingsService;
                _log = log;
                _bounds = monitor.Bounds;
                _dpiX = monitor.DpiX;
                _dpiY = monitor.DpiY;
                try
                {
                    CreateDeviceResources(device);
                    PositionWindow(show: true);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            internal IntPtr Window { get; }
            internal string DeviceName { get; }

            internal void Resize(
                PixelRect bounds,
                float dpiX,
                float dpiY,
                CanvasDevice device)
            {
                if (_disposed ||
                    (_bounds == bounds &&
                     Math.Abs(_dpiX - dpiX) < 0.01f &&
                     Math.Abs(_dpiY - dpiY) < 0.01f &&
                     _target != null &&
                     _renderer != null &&
                     _dib != null))
                {
                    return;
                }

                _bounds = bounds;
                _dpiX = dpiX;
                _dpiY = dpiY;
                RecreateDeviceResources(device);
                PositionWindow(show: true);
            }

            internal void RecreateDeviceResources(CanvasDevice device)
            {
                DisposeDeviceResources();
                CreateDeviceResources(device);
            }

            private void CreateDeviceResources(CanvasDevice device)
            {
                if (_bounds.IsEmpty)
                {
                    throw new InvalidOperationException(
                        "Cannot create an empty monitor overlay.");
                }

                CanvasRenderTarget? target = null;
                EffectRenderer? renderer = null;
                DibSurface? dib = null;
                try
                {
                    target = new CanvasRenderTarget(
                        device,
                        _bounds.Width * 96f / _dpiX,
                        _bounds.Height * 96f / _dpiY,
                        _dpiX);
                    renderer = new EffectRenderer(
                        target,
                        _settingsService);
                    dib = new DibSurface(
                        _screenDc,
                        _bounds.Width,
                        _bounds.Height);

                    _target = target;
                    _renderer = renderer;
                    _dib = dib;
                    _ = InitializeRendererAsync(renderer);
                }
                catch
                {
                    renderer?.Dispose();
                    target?.Dispose();
                    dib?.Dispose();
                    throw;
                }
            }

            private async System.Threading.Tasks.Task
                InitializeRendererAsync(EffectRenderer renderer)
            {
                try
                {
                    await renderer.InitializeResourcesAsync();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception ex)
                {
                    _log(
                        $"Overlay icon resources ({DeviceName}): {ex.Message}");
                }
            }

            internal void Render(
                in EffectFrameSnapshot frame,
                in NebulaRenderSettings nebula,
                bool refreshTopmost)
            {
                if (_disposed ||
                    _target == null ||
                    _renderer == null ||
                    _dib == null)
                {
                    return;
                }

                using (var session = _target.CreateDrawingSession())
                {
                    session.Clear(Color.FromArgb(0, 0, 0, 0));
                    session.Transform =
                        DisplayGeometry
                            .CreateScreenPixelsToLocalDipsTransform(
                                _bounds,
                                _dpiX,
                                _dpiY);
                    _renderer.Render(session, frame, nebula);
                }

                _target.GetPixelBytes(_dib.PixelBuffer);
                _dib.Present(Window, _bounds.Left, _bounds.Top);

                if (refreshTopmost)
                {
                    NativeMethods.SetWindowPos(
                        Window,
                        (IntPtr)NativeMethods.HWND_TOPMOST,
                        0,
                        0,
                        0,
                        0,
                        NativeMethods.SWP_NOACTIVATE |
                            NativeMethods.SWP_NOMOVE |
                            NativeMethods.SWP_NOSIZE);
                }
            }

            private void PositionWindow(bool show)
            {
                uint flags = NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_FRAMECHANGED;
                if (show)
                {
                    flags |= NativeMethods.SWP_SHOWWINDOW;
                }

                if (!NativeMethods.SetWindowPos(
                    Window,
                    (IntPtr)NativeMethods.HWND_TOPMOST,
                    _bounds.Left,
                    _bounds.Top,
                    _bounds.Width,
                    _bounds.Height,
                    flags))
                {
                    throw LastError(
                        $"SetWindowPos failed for {DeviceName}.");
                }
            }

            private void DisposeDeviceResources()
            {
                _renderer?.Dispose();
                _renderer = null;
                _target?.Dispose();
                _target = null;
                _dib?.Dispose();
                _dib = null;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                DisposeDeviceResources();
                if (Window != IntPtr.Zero)
                {
                    NativeMethods.DestroyWindow(Window);
                }
            }
        }

        private sealed class DibSurface : IDisposable
        {
            private readonly SafeScreenDcHandle _screenDc;
            private SafeMemoryDcHandle? _memoryDc;
            private SafeGdiObjectHandle? _bitmap;
            private IntPtr _oldBitmap;
            private IntPtr _bits;
            private bool _disposed;

            internal DibSurface(
                SafeScreenDcHandle screenDc,
                int width,
                int height)
            {
                _screenDc = screenDc;
                Width = width;
                Height = height;
                ByteCount = checked(width * height * 4);
                PixelBuffer = new Windows.Storage.Streams.Buffer(
                    (uint)ByteCount);
                try
                {
                    IntPtr memory = NativeMethods.CreateCompatibleDC(
                        screenDc.DangerousGetHandle());
                    if (memory == IntPtr.Zero)
                    {
                        throw LastError("CreateCompatibleDC failed.");
                    }

                    _memoryDc = new SafeMemoryDcHandle(memory);
                    var info = new NativeMethods.BITMAPINFO
                    {
                        bmiHeader = new NativeMethods.BITMAPINFOHEADER
                        {
                            biSize = (uint)Marshal.SizeOf<
                                NativeMethods.BITMAPINFOHEADER>(),
                            biWidth = width,
                            biHeight = -height,
                            biPlanes = 1,
                            biBitCount = 32,
                            biCompression = NativeMethods.BI_RGB,
                            biSizeImage = (uint)ByteCount,
                        },
                    };
                    IntPtr bitmap = NativeMethods.CreateDIBSection(
                        screenDc.DangerousGetHandle(),
                        ref info,
                        NativeMethods.DIB_RGB_COLORS,
                        out _bits,
                        IntPtr.Zero,
                        0);
                    if (bitmap == IntPtr.Zero ||
                        _bits == IntPtr.Zero)
                    {
                        throw LastError("CreateDIBSection failed.");
                    }

                    _bitmap = new SafeGdiObjectHandle(bitmap);
                    _oldBitmap = NativeMethods.SelectObject(
                        memory,
                        bitmap);
                    if (_oldBitmap == IntPtr.Zero ||
                        _oldBitmap == (IntPtr)(-1))
                    {
                        throw LastError(
                            "SelectObject for DIB failed.");
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            internal int Width { get; }
            internal int Height { get; }
            internal int ByteCount { get; }
            internal Windows.Storage.Streams.IBuffer PixelBuffer { get; }

            internal void Present(
                IntPtr window,
                int left,
                int top)
            {
                var byteAccess =
                    PixelBuffer.As<NativeMethods.IBufferByteAccess>();
                byteAccess.Buffer(out IntPtr sourcePixels);
                NativeMethods.CopyMemory(
                    _bits,
                    sourcePixels,
                    (UIntPtr)(uint)ByteCount);
                var destination = new NativeMethods.POINT
                {
                    x = left,
                    y = top,
                };
                var source = new NativeMethods.POINT();
                var size = new NativeMethods.POINT
                {
                    x = Width,
                    y = Height,
                };
                var blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    SourceConstantAlpha = 255,
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA,
                };
                if (!NativeMethods.UpdateLayeredWindow(
                    window,
                    _screenDc.DangerousGetHandle(),
                    ref destination,
                    ref size,
                    _memoryDc!.DangerousGetHandle(),
                    ref source,
                    0,
                    ref blend,
                    NativeMethods.ULW_ALPHA))
                {
                    throw LastError("UpdateLayeredWindow failed.");
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_memoryDc is { IsInvalid: false } &&
                    _oldBitmap != IntPtr.Zero &&
                    _oldBitmap != (IntPtr)(-1))
                {
                    NativeMethods.SelectObject(
                        _memoryDc.DangerousGetHandle(),
                        _oldBitmap);
                    _oldBitmap = IntPtr.Zero;
                }

                _bitmap?.Dispose();
                _bitmap = null;
                _bits = IntPtr.Zero;
                _memoryDc?.Dispose();
                _memoryDc = null;
            }
        }
    }
}
