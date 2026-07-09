using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace MouseBeautifier
{
    /// <summary>
    /// A loaded icon image. Holds either:
    ///  - a single <see cref="CanvasBitmap"/> (PNG / JPG / static GIF), or
    ///  - multiple frames + per-frame delays for an animated GIF, or
    ///  - a vector <see cref="SvgImageSource"/> (rendered through the XAML layer).
    /// Raster / GIF frames are drawn through Win2D so alpha transparency is honored;
    /// SVG keeps its vector crispness via the XAML Image control.
    /// </summary>
    public sealed class IconImage : IDisposable
    {
        public CanvasBitmap[]? Frames;   // null for SVG
        public double[]? Delays;         // seconds per frame (animated GIF only)
        public ImageSource? SvgSource;   // non-null for SVG

        public bool IsAnimated => Frames != null && Frames.Length > 1 && Delays != null;

        /// <summary>Returns the frame that should be displayed at the given clock time (seconds).</summary>
        public CanvasBitmap? GetFrame(double timeSec)
        {
            if (Frames == null || Frames.Length == 0) return null;
            if (Frames.Length == 1 || Delays == null) return Frames[0];

            double total = 0;
            foreach (var d in Delays) total += d;
            if (total <= 0) return Frames[0];

            double t = timeSec % total;
            for (int i = 0; i < Delays.Length; i++)
            {
                t -= Delays[i];
                if (t < 0) return Frames[i];
            }
            return Frames[^1];
        }

        public void Dispose()
        {
            if (Frames != null)
            {
                foreach (var f in Frames) f.Dispose();
                Frames = null;
            }
            SvgSource = null;
        }

        /// <summary>Loads a raster image or animated GIF. SVG is handled separately on the UI thread.</summary>
        public static async Task<IconImage?> LoadAsync(ICanvasResourceCreator creator, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var ext = Path.GetExtension(path).ToLowerInvariant();

            try
            {
                if (ext == ".gif")
                {
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    using var ras = await file.OpenReadAsync();
                    var decoder = await BitmapDecoder.CreateAsync(ras);
                    int count = (int)decoder.FrameCount;

                    if (count > 1)
                    {
                        var frames = new CanvasBitmap[count];
                        var delays = new double[count];
                        for (int i = 0; i < count; i++)
                        {
                            var frame = await decoder.GetFrameAsync((uint)i);
                            var sb = await frame.GetSoftwareBitmapAsync();
                            sb = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                            frames[i] = CanvasBitmap.CreateFromSoftwareBitmap(creator, sb);

                            double delay = 0.1; // default 100ms
                            try
                            {
                                var props = await frame.BitmapProperties.GetPropertiesAsync(new[] { "/grct/Delay" });
                                if (props.TryGetValue("/grct/Delay", out var pv))
                                {
                                    if (pv.Value is ushort d) delay = d / 100.0;       // centiseconds -> seconds
                                    else if (pv.Value is int di) delay = di / 100.0;
                                }
                            }
                            catch { /* keep default */ }
                            if (delay <= 0) delay = 0.1;
                            delays[i] = delay;
                        }
                        return new IconImage { Frames = frames, Delays = delays };
                    }

                    // Single-frame GIF -> static bitmap.
                    var f0 = await decoder.GetFrameAsync(0);
                    var sb0 = await f0.GetSoftwareBitmapAsync();
                    sb0 = SoftwareBitmap.Convert(sb0, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    var bmp0 = CanvasBitmap.CreateFromSoftwareBitmap(creator, sb0);
                    return new IconImage { Frames = new[] { bmp0 } };
                }

                // PNG / JPG / JPEG / BMP / WEBP -> static bitmap (alpha preserved).
                var file2 = await StorageFile.GetFileFromPathAsync(path);
                using var rs = await file2.OpenReadAsync();
                var bmp = await CanvasBitmap.LoadAsync(creator, rs);
                return new IconImage { Frames = new[] { bmp } };
            }
            catch
            {
                return null;
            }
        }
    }
}
