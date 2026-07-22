using System;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using MouseBeautifier.Core;
using Windows.Foundation;
using Windows.UI;

namespace MouseBeautifier
{
    /// <summary>
    /// Stateless Win2D projection of <see cref="EffectWorld"/> plus device-bound
    /// icon resources. Simulation is exclusively owned by EffectWorld.
    /// </summary>
    public sealed class EffectRenderer : IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly ParticleRenderer _particles = new();
        private readonly TrailRenderer _trail = new();
        private readonly IconResourceManager _icons;
        private bool _settingsHooked;

        public EffectRenderer(
            ICanvasResourceCreator creator,
            ISettingsService settingsService)
        {
            _settingsService = settingsService ??
                throw new ArgumentNullException(nameof(settingsService));
            _icons = new IconResourceManager(creator);
            _settingsService.Changed += OnSettingsChanged;
            _settingsHooked = true;
        }

        /// <summary>
        /// Loads the bundled + custom icon resources. Call once the CanvasControl's
        /// device is ready (e.g. from the CreateResources event).
        /// </summary>
        public async Task InitializeResourcesAsync()
        {
            await _icons.InitializeAsync(AppContext.BaseDirectory);
            await _icons.ReloadCustomAsync(
                _settingsService.Current.CustomIconPath);
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            _ = _icons.ReloadCustomAsync(
                _settingsService.Current.CustomIconPath);
        }

        /// <summary>Returns the loaded icon for image-based types (custom / pig / girl), or null for vector shapes.</summary>
        private IconImage? GetImageIcon(string iconType)
        {
            return _icons.Get(iconType);
        }

        public void Render(
            CanvasDrawingSession session,
            in EffectFrameSnapshot frame)
        {
            var s = _settingsService.Current;
            Vector2 cursor = frame.Cursor;

            if (s.EnableGlow) DrawGlow(session, cursor, s);
            if (s.EnableTrail)
                _trail.Render(
                    session,
                    frame.Trail,
                    ColorsUtil.Parse(s.TrailColor),
                    (float)s.TrailWidth);
            if (s.EnableOrbit)
                DrawOrbit(
                    session,
                    cursor,
                    s,
                    frame.OrbitAngleDegrees);
            if (s.EnableRope) DrawRope(session, s, frame);
            _particles.Render(session, frame.Particles, s);
        }

        // ---------- Glow ----------
        private void DrawGlow(CanvasDrawingSession session, Vector2 c, AppSettings s)
        {
            float r = (float)s.GlowSize;
            if (r <= 0) return;
            var start = ColorsUtil.Parse(s.GlowColor);
            start.A = (byte)(255 * Math.Clamp(s.GlowIntensity, 0, 1));
            var end = start; end.A = 0;
            using var brush = new CanvasRadialGradientBrush(session, start, end)
            {
                Center = new Vector2(c.X, c.Y),
                RadiusX = r,
                RadiusY = r,
            };
            session.FillCircle(c.X, c.Y, r, brush);
        }

        // ---------- Orbit (环绕旋转粒子) ----------
        private static void DrawOrbit(
            CanvasDrawingSession session,
            Vector2 c,
            AppSettings s,
            float orbitAngleDegrees)
        {
            int n = Math.Max(1, (int)s.OrbitCount);
            float radius = (float)s.OrbitRadius;
            float size = (float)s.OrbitSize;
            var baseColor = ColorsUtil.Parse(s.OrbitColor);

            // faint connecting ring for a cohesive "halo" look
            using (var ring = new CanvasSolidColorBrush(session, baseColor))
            {
                ring.Opacity = 0.16f;
                session.DrawCircle(c.X, c.Y, radius, ring, Math.Max(1f, size * 0.3f));
            }

            // rotating particles; alpha + size gradient gives a comet-tail sweep
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)n;                       // 0 (head) .. 1 (tail)
                double a = orbitAngleDegrees * Math.PI / 180.0 +
                    i * 2 * Math.PI / n;
                float px = c.X + (float)Math.Cos(a) * radius;
                float py = c.Y + (float)Math.Sin(a) * radius;

                var col = baseColor;
                col.A = (byte)(255 * (0.30 + 0.70 * (1 - t)));
                float r = size * (0.55f + 0.45f * (1 - (float)t));
                session.FillCircle(px, py, r, col);
            }
        }

        // ---------- Rope + hanging icon ----------
        // Pendant geometry (tip == rope end, rotation about that point) lives in
        // the pure, headless-testable PendantGeometry class — single source of
        // truth shared by the renderer and the StarAttachment test harness.

        private void DrawRope(
            CanvasDrawingSession session,
            AppSettings s,
            in EffectFrameSnapshot frame)
        {
            var pts = frame.Rope.Points;
            if (pts.Length < 2) return;

            var col = ColorsUtil.Parse(s.RopeColor);

            // Draw the rope with round caps + round joins so segment joints stay
            // smooth. Previously each segment was a separate DrawLine with default
            // butt caps, so the rope looked "broken into pieces" when it bent.
            using var stroke = new CanvasStrokeStyle
            {
                LineJoin = CanvasLineJoin.Round,
                StartCap = CanvasCapStyle.Round,
                EndCap = CanvasCapStyle.Round,
            };
            for (int pass = 0; pass < 3; pass++)
            {
                float w = (float)s.RopeWidth * (3 - pass);
                var c = col; c.A = (byte)(50 * (3 - pass));
                for (int i = 0; i < pts.Length - 1; i++)
                    session.DrawLine(pts[i], pts[i + 1], c, w, stroke);
            }

            // Pendant layout: tip == rope end (Bob), extends along rope direction.
            // The tip is ALWAYS the rope's last drawn point (pts[last]); we never
            // relocate it. The rope simulator already caps the rope span at
            // RopeLength, so the pendant hangs naturally off the real rope end.
            // (An earlier "safety net" clamped the tip to RopeLength - IconSize,
            // which pulled the pendant inward by up to a full IconSize whenever the
            // rope was taut — that was the visible rope/pendant separation bug.)
            var pendant = PendantGeometry.ComputePendant(pts, (float)s.IconSize);

            // Visible knot at the joint so the rope<->star bond is unambiguous
            // at any swing angle (the math already guarantees zero separation;
            // this removes any perceptual gap from anti-aliasing / thin rope).
            DrawConnector(session, pendant, s);

            var icon = GetImageIcon(s.IconType);
            bool hasBitmap = icon?.Frames != null;
            // Wrap pendant drawing in try-catch: if the icon type's geometry fails
            // (e.g. device lost, invalid state), we must NOT let the exception
            // propagate to RenderFrame's catch — that would skip Present() every
            // frame and make ALL effects disappear (the "切换图标后特效消失" bug).
            try
            {
                if (hasBitmap && icon != null)
                {
                    float size = (float)s.IconSize;

                    var saved = session.Transform;
                    try
                    {
                        // Keep the monitor's screen-pixel -> local-DIP projection.
                        // Replacing it with only R*T made the rope and pendant use
                        // different coordinate systems, causing a very large gap
                        // whenever DPI != 96 or the monitor origin was not (0,0).
                        session.Transform = PendantGeometry.CreateRenderTransform(
                            pendant,
                            saved);

                        var imageFrame = icon.GetFrame(frame.AnimationTime);
                        if (imageFrame != null)
                        {
                            Rect src = icon.GetVisibleSourceBounds(imageFrame);
                            double sourceMax = Math.Max(src.Width, src.Height);
                            double imageScale = sourceMax > 0
                                ? size / sourceMax
                                : 1;
                            double width = src.Width * imageScale;
                            double height = src.Height * imageScale;
                            var dst = new Rect(-width / 2, 0, width, height);
                            session.DrawImage(
                                imageFrame,
                                dst,
                                src,
                                1.0f,
                                CanvasImageInterpolation.HighQualityCubic);
                        }
                    }
                    finally
                    {
                        // Never leak the pendant transform into particles rendered
                        // later in the same drawing session.
                        session.Transform = saved;
                    }
                }
                else
                {
                    DrawBuiltinIcon(session, pendant, s);
                }
            }
            catch (Exception ex)
            {
                App.Log("DrawRope pendant: " + ex.Message + " (iconType=" + s.IconType + ")");
            }
        }

        /// <summary>
        /// Small knot drawn exactly at the rope/pendant joint. The geometry
        /// already guarantees the star's top tip == rope end (zero separation);
        /// this makes the bond visually unambiguous at any swing angle and
        /// removes any perceptual gap from a thin rope or anti-aliasing.
        /// </summary>
        private void DrawConnector(CanvasDrawingSession session, in PendantGeometry.PendantState p, AppSettings s)
        {
            float r = (float)Math.Max(s.RopeWidth * 1.6, s.IconSize * 0.05);
            var c = ColorsUtil.Parse(s.RopeColor);
            session.FillCircle(p.Tip.X, p.Tip.Y, r, c);
        }

        private void DrawBuiltinIcon(CanvasDrawingSession session, in PendantGeometry.PendantState p, AppSettings s)
        {
            var color = ColorsUtil.Parse(s.IconColor);
            float size = (float)s.IconSize;

            var saved = session.Transform;
            // Transform: origin = tip, +Y axis = rope end direction.
            // The shape is drawn in local space with its TIP at (0,0) and extends
            // in +Y, so after rotation it always points along the rope.
            // Order MUST be R*T (rotate-about-origin then translate to Tip) so
            // local (0,0) maps to Tip. The saved transform is then appended so
            // both the rope and pendant use the same monitor/DPI projection.
            session.Transform = PendantGeometry.CreateRenderTransform(p, saved);

            try
            {
                switch (s.IconType)
                {
                    case "circle":
                        // Circle hangs below the tip: center at (0, size/2).
                        session.FillCircle(0, size / 2f, size / 2f, color);
                        break;
                    case "square":
                        // Square hangs below the tip.
                        session.FillRectangle(-size / 2f, 0, size, size, color);
                        break;
                    case "triangle":
                        // Triangle: tip at origin, base at y=size. This makes it look
                        // like the rope narrows into a triangle point — "a rope whose
                        // bottom end IS a triangle" per the user's request.
                        FillTriangleTip(session, color, size);
                        break;
                    case "diamond":
                        // Diamond: tip at origin, widest at y=size/2, bottom point at y=size.
                        FillDiamondTip(session, color, size);
                        break;
                    case "heart":
                        DrawHeart(session, color, size);
                        break;
                    case "smiley":
                        DrawSmiley(session, color, size / 2f, size);
                        break;
                    case "star":
                    default:
                        FillStarTip(session, color, size);
                        break;
                }
            }
            finally
            {
                session.Transform = saved;
            }
        }

        /// <summary>Triangle with tip at (0,0), base at y=size. Width = size*0.9.</summary>
        private static void FillTriangleTip(CanvasDrawingSession s, Color c, float size)
        {
            float halfW = size * 0.5f;
            var pts = new Vector2[]
            {
                new Vector2(0, 0),        // tip (at rope end)
                new Vector2(-halfW, size), // base left
                new Vector2(halfW, size),  // base right
            };
            using var geo = CanvasGeometry.CreatePolygon(s, pts);
            s.FillGeometry(geo, c);
        }

        /// <summary>Diamond: tip at (0,0), widest at y=size/2, bottom at y=size.</summary>
        private static void FillDiamondTip(CanvasDrawingSession s, Color c, float size)
        {
            float halfW = size * 0.5f;
            var pts = new Vector2[]
            {
                new Vector2(0, 0),           // top tip
                new Vector2(halfW, size/2f), // right
                new Vector2(0, size),        // bottom tip
                new Vector2(-halfW, size/2f),// left
            };
            using var geo = CanvasGeometry.CreatePolygon(s, pts);
            s.FillGeometry(geo, c);
        }

        /// <summary>Star anchored by its top tip at (0,0), extends in +Y.</summary>
        private static void FillStarTip(CanvasDrawingSession s, Color c, float size)
        {
            int points = 5;
            var pts = new Vector2[points * 2];
            float outer = size * 0.5f;
            float inner = outer * 0.45f;
            // Center the star at (0, size/2) so its top tip sits at the rope end,
            // then it extends downward to y = size.
            float cx = 0, cy = size / 2f;
            for (int i = 0; i < points * 2; i++)
            {
                float rad = (i % 2 == 0) ? outer : inner;
                double a = -Math.PI / 2 + i * Math.PI / points;
                pts[i] = new Vector2(cx + (float)(Math.Cos(a) * rad), cy + (float)(Math.Sin(a) * rad));
            }
            using var geo = CanvasGeometry.CreatePolygon(s, pts);
            s.FillGeometry(geo, c);
        }

        private static void FillPolygon(CanvasDrawingSession s, Color c, float r, int sides, float startDeg)
        {
            var pts = new Vector2[sides];
            double start = startDeg * Math.PI / 180;
            for (int i = 0; i < sides; i++)
            {
                double a = start + i * 2 * Math.PI / sides;
                pts[i] = new Vector2((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r));
            }
            using var geo = CanvasGeometry.CreatePolygon(s, pts);
            s.FillGeometry(geo, c);
        }

        private static void FillStar(CanvasDrawingSession s, Color c, float r)
        {
            int points = 5;
            var pts = new Vector2[points * 2];
            float inner = r * 0.45f;
            for (int i = 0; i < points * 2; i++)
            {
                float rad = (i % 2 == 0) ? r : inner;
                double a = -Math.PI / 2 + i * Math.PI / points;
                pts[i] = new Vector2((float)(Math.Cos(a) * rad), (float)(Math.Sin(a) * rad));
            }
            using var geo = CanvasGeometry.CreatePolygon(s, pts);
            s.FillGeometry(geo, c);
        }

        private static void DrawHeart(CanvasDrawingSession s, Color c, float size)
        {
            var pts = new Vector2[40];
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < pts.Length; i++)
            {
                double t = i * 2 * Math.PI / pts.Length;
                float x = (float)(16 * Math.Pow(Math.Sin(t), 3));
                float y = (float)(
                    -13 * Math.Cos(t) +
                    5 * Math.Cos(2 * t) +
                    2 * Math.Cos(3 * t) +
                    Math.Cos(4 * t));
                pts[i] = new Vector2(x, y);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

            // Normalize the visible heart itself (not its mathematical canvas)
            // so its topmost point is exactly the rope attachment origin.
            float scale = size / Math.Max(maxX - minX, maxY - minY);
            float centerX = (minX + maxX) / 2f;
            for (int i = 0; i < pts.Length; i++)
            {
                pts[i] = new Vector2(
                    (pts[i].X - centerX) * scale,
                    (pts[i].Y - minY) * scale);
            }
            using var geo = CanvasGeometry.CreatePolygon(s, pts);
            s.FillGeometry(geo, c);
        }

        private static void DrawSmiley(CanvasDrawingSession s, Color c, float r, float size)
        {
            float cy = size / 2f; // center below the tip
            s.FillCircle(0, cy, r, c);
            var dark = ColorsUtil.Parse("#FF333333");
            s.FillCircle(-r * 0.35f, cy - r * 0.2f, r * 0.12f, dark);
            s.FillCircle(r * 0.35f, cy - r * 0.2f, r * 0.12f, dark);

            // smile = lower arc (downward bulge in screen coords)
            int n = 16;
            var mouth = new Vector2[n + 1];
            float R = r * 0.55f;
            float mcy = cy - r * 0.1f;
            for (int i = 0; i <= n; i++)
            {
                double a = (20 + 140.0 * i / n) * Math.PI / 180; // 20deg .. 160deg
                mouth[i] = new Vector2((float)(Math.Cos(a) * R), mcy + (float)(Math.Sin(a) * R));
            }
            for (int i = 1; i <= n; i++)
                s.DrawLine(mouth[i - 1].X, mouth[i - 1].Y, mouth[i].X, mouth[i].Y, dark, r * 0.1f);
        }

        public void Dispose()
        {
            if (_settingsHooked)
            {
                _settingsService.Changed -= OnSettingsChanged;
                _settingsHooked = false;
            }

            _icons.Dispose();
        }
    }
}
