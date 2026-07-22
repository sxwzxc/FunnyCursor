using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace MouseBeautifier
{
    /// <summary>
    /// A Win2D raster icon: either one bitmap or multiple animated GIF frames.
    /// Alpha transparency is preserved and used to find the visible attachment
    /// boundary.
    /// </summary>
    public sealed class IconImage : IDisposable
    {
        public CanvasBitmap[]? Frames;
        public double[]? Delays;         // seconds per frame (animated GIF only)
        private Rect[]? _visibleSourceBounds;

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

        /// <summary>
        /// Returns the non-transparent part of a frame in bitmap coordinates.
        /// Images often contain transparent padding at their top edge; drawing
        /// the entire canvas with y=0 at the rope tip leaves a visible gap even
        /// when the mathematical rectangles touch.
        /// </summary>
        public Rect GetVisibleSourceBounds(CanvasBitmap frame)
        {
            if (Frames != null && _visibleSourceBounds != null)
            {
                for (int i = 0; i < Frames.Length; i++)
                {
                    if (ReferenceEquals(Frames[i], frame))
                    {
                        return _visibleSourceBounds[i];
                    }
                }
            }

            return new Rect(0, 0, frame.Size.Width, frame.Size.Height);
        }

        public void Dispose()
        {
            if (Frames != null)
            {
                foreach (var f in Frames) f.Dispose();
                Frames = null;
            }
            _visibleSourceBounds = null;
        }

        /// <summary>Loads a raster image or animated GIF.</summary>
        public static async Task<IconImage?> LoadAsync(
            ICanvasResourceCreator creator,
            string path,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var createdBitmaps = new List<CanvasBitmap>();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ext == ".gif")
                {
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    cancellationToken.ThrowIfCancellationRequested();
                    using var ras = await file.OpenReadAsync();
                    var decoder = await BitmapDecoder.CreateAsync(ras);
                    int count = (int)decoder.FrameCount;

                    if (count > 1)
                    {
                        var frames = new CanvasBitmap[count];
                        var delays = new double[count];
                        for (int i = 0; i < count; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var frame = await decoder.GetFrameAsync((uint)i);
                            using var source =
                                await frame.GetSoftwareBitmapAsync();
                            using var converted = SoftwareBitmap.Convert(
                                source,
                                BitmapPixelFormat.Bgra8,
                                BitmapAlphaMode.Premultiplied);
                            frames[i] = CanvasBitmap.CreateFromSoftwareBitmap(
                                creator,
                                converted);
                            createdBitmaps.Add(frames[i]);

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
                        cancellationToken.ThrowIfCancellationRequested();
                        createdBitmaps.Clear();
                        return CreateRaster(frames, delays);
                    }

                    // Single-frame GIF -> static bitmap.
                    var f0 = await decoder.GetFrameAsync(0);
                    using var source0 =
                        await f0.GetSoftwareBitmapAsync();
                    using var converted0 = SoftwareBitmap.Convert(
                        source0,
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied);
                    var bmp0 = CanvasBitmap.CreateFromSoftwareBitmap(
                        creator,
                        converted0);
                    createdBitmaps.Add(bmp0);
                    cancellationToken.ThrowIfCancellationRequested();
                    createdBitmaps.Clear();
                    return CreateRaster(new[] { bmp0 });
                }

                // PNG / JPG / JPEG / BMP / WEBP -> static bitmap (alpha preserved).
                var file2 = await StorageFile.GetFileFromPathAsync(path);
                cancellationToken.ThrowIfCancellationRequested();
                using var rs = await file2.OpenReadAsync();
                var bmp = await CanvasBitmap.LoadAsync(creator, rs);
                createdBitmaps.Add(bmp);
                cancellationToken.ThrowIfCancellationRequested();
                createdBitmaps.Clear();
                return CreateRaster(new[] { bmp });
            }
            catch (OperationCanceledException)
            {
                foreach (CanvasBitmap bitmap in createdBitmaps)
                {
                    bitmap.Dispose();
                }

                throw;
            }
            catch
            {
                foreach (CanvasBitmap bitmap in createdBitmaps)
                {
                    bitmap.Dispose();
                }

                return null;
            }
        }

        private static IconImage CreateRaster(
            CanvasBitmap[] frames,
            double[]? delays = null)
        {
            var bounds = new Rect[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                bounds[i] = FindVisibleSourceBounds(frames[i]);
            }

            // Animated frames normally share one canvas. Use their union so
            // changing alpha content cannot move the apparent attachment point
            // from frame to frame.
            bool sameCanvas = frames.Length > 1;
            for (int i = 1; i < frames.Length && sameCanvas; i++)
            {
                sameCanvas =
                    frames[i].SizeInPixels.Width ==
                        frames[0].SizeInPixels.Width &&
                    frames[i].SizeInPixels.Height ==
                        frames[0].SizeInPixels.Height;
            }

            if (sameCanvas)
            {
                double left = bounds.Min(bound => bound.Left);
                double top = bounds.Min(bound => bound.Top);
                double right = bounds.Max(bound => bound.Right);
                double bottom = bounds.Max(bound => bound.Bottom);
                var union = new Rect(
                    left,
                    top,
                    right - left,
                    bottom - top);
                for (int i = 0; i < bounds.Length; i++)
                {
                    bounds[i] = union;
                }
            }

            return new IconImage
            {
                Frames = frames,
                Delays = delays,
                _visibleSourceBounds = bounds,
            };
        }

        private static Rect FindVisibleSourceBounds(CanvasBitmap bitmap)
        {
            Rect full = new(0, 0, bitmap.Size.Width, bitmap.Size.Height);
            try
            {
                int pixelWidth = checked((int)bitmap.SizeInPixels.Width);
                int pixelHeight = checked((int)bitmap.SizeInPixels.Height);
                if (pixelWidth <= 0 || pixelHeight <= 0)
                {
                    return full;
                }

                byte[] pixels = bitmap.GetPixelBytes();
                long requiredBytes = (long)pixelWidth * pixelHeight * 4;
                if (pixels.LongLength < requiredBytes)
                {
                    return full;
                }

                int minX = pixelWidth;
                int minY = pixelHeight;
                int maxX = -1;
                int maxY = -1;
                for (int y = 0; y < pixelHeight; y++)
                {
                    int row = y * pixelWidth * 4;
                    for (int x = 0; x < pixelWidth; x++)
                    {
                        // CanvasBitmap pixel bytes are BGRA8.
                        if (pixels[row + x * 4 + 3] <= 2)
                        {
                            continue;
                        }

                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }

                if (maxX < minX || maxY < minY)
                {
                    return full;
                }

                double dipPerPixelX = bitmap.Size.Width / pixelWidth;
                double dipPerPixelY = bitmap.Size.Height / pixelHeight;
                return new Rect(
                    minX * dipPerPixelX,
                    minY * dipPerPixelY,
                    (maxX - minX + 1) * dipPerPixelX,
                    (maxY - minY + 1) * dipPerPixelY);
            }
            catch
            {
                // Alpha trimming is visual polish, never a reason to reject an
                // otherwise valid icon resource.
                return full;
            }
        }
    }
}
