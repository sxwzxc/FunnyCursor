using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace MouseBeautifier
{
    /// <summary>
    /// Owns all visual state and draws everything onto the Win2D canvas each frame.
    /// Reads AppSettings live, so control-panel changes apply instantly.
    /// </summary>
    public sealed class EffectRenderer : IDisposable
    {
        private readonly ICanvasResourceCreator _creator;
        private readonly ParticleSystem _particles = new();
        private readonly RopeSimulator _rope = new();
        private readonly Trail _trail = new();

        private IconImage? _customIcon;
        private IconImage? _pigIcon;
        private IconImage? _girlIcon;
        private bool _settingsHooked;

        // viewport (physical -> dips conversion)
        private int _vx, _vy;
        private double _scale = 1;

        // orientation / animation state
        private double _animTime;
        private float _orbitAngle;

        public EffectRenderer(ICanvasResourceCreator creator)
        {
            _creator = creator;
            SettingsManager.Changed += OnSettingsChanged;
            _settingsHooked = true;
            OnSettingsChanged();
        }

        /// <summary>
        /// Loads the bundled + custom icon resources. Call once the CanvasControl's
        /// device is ready (e.g. from the CreateResources event).
        /// </summary>
        public async void InitResources()
        {
            try
            {
                _pigIcon = await IconImage.LoadAsync(_creator,
                    Path.Combine(AppContext.BaseDirectory, "Assets/pig.png"));
                _girlIcon = await IconImage.LoadAsync(_creator,
                    Path.Combine(AppContext.BaseDirectory, "Assets/girl.png"));
                await LoadCustomIconAsync();
            }
            catch { /* device not ready yet — Icons simply stay unavailable */ }
        }

        public void SetViewport(int vx, int vy, double scale)
        {
            _vx = vx; _vy = vy; _scale = scale;
        }

        private void OnSettingsChanged()
        {
            _rope.ApplySettings(SettingsManager.Current);
            _ = LoadCustomIconAsync();
        }

        private async Task LoadCustomIconAsync()
        {
            var path = SettingsManager.Current.CustomIconPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _customIcon = null;
                return;
            }
            // SVG is not supported by the layered (GDI-presented) overlay; fall
            // back to the built-in vector shape instead of loading a vector source.
            if (Path.GetExtension(path).ToLowerInvariant() == ".svg")
            {
                _customIcon = null;
                return;
            }
            try
            {
                _customIcon = await IconImage.LoadAsync(_creator, path);
            }
            catch { _customIcon = null; }
        }

        /// <summary>Returns the loaded icon for image-based types (custom / pig / girl), or null for vector shapes.</summary>
        private IconImage? GetImageIcon(string iconType)
        {
            return iconType switch
            {
                "custom" => _customIcon,
                "pig" => _pigIcon,
                "girl" => _girlIcon,
                _ => null,
            };
        }

        public void Update(double dt, Vector2 cursor, MouseTracker tracker)
        {
            var s = SettingsManager.Current;

            _animTime += dt;
            if (s.EnableOrbit) _orbitAngle = (_orbitAngle + (float)(s.OrbitSpeed * dt)) % 360f;

            if (s.EnableClickEffects)
            {
                while (tracker.TryDequeueClick(out var c))
                {
                    float cx = (float)((c.X - _vx) / _scale);
                    float cy = (float)((c.Y - _vy) / _scale);
                    _particles.Spawn(new Vector2(cx, cy), s);
                }
            }

            _particles.Update(dt, s);

            if (s.EnableTrail) _trail.Push(cursor, dt, s);
            else _trail.Clear();

            if (s.EnableRope) _rope.Update(dt, cursor, s);
        }

        public void Render(CanvasDrawingSession session, Vector2 cursor)
        {
            var s = SettingsManager.Current;

            if (s.EnableGlow) DrawGlow(session, cursor, s);
            if (s.EnableTrail) _trail.Render(session, ColorsUtil.Parse(s.TrailColor), (float)s.TrailWidth);
            if (s.EnableOrbit) DrawOrbit(session, cursor, s);
            if (s.EnableRope) DrawRope(session, s);
            _particles.Render(session);
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
        private void DrawOrbit(CanvasDrawingSession session, Vector2 c, AppSettings s)
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
                double a = _orbitAngle * Math.PI / 180.0 + i * 2 * Math.PI / n;
                float px = c.X + (float)Math.Cos(a) * radius;
                float py = c.Y + (float)Math.Sin(a) * radius;

                var col = baseColor;
                col.A = (byte)(255 * (0.30 + 0.70 * (1 - t)));
                float r = size * (0.55f + 0.45f * (1 - (float)t));
                session.FillCircle(px, py, r, col);
            }
        }

        // ---------- Rope + hanging icon ----------
        private void DrawRope(CanvasDrawingSession session, AppSettings s)
        {
            var pts = _rope.Points;
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

            var bob = _rope.Bob;

            // The pendant rotates to match the rope's LAST segment direction exactly,
            // so it always looks tied to the end of the rope (no independent lean
            // that would make it appear detached).
            var dir = pts[^1] - pts[^2];
            double finalAngle = Math.Atan2(dir.X, dir.Y) * 180 / Math.PI;

            var icon = GetImageIcon(s.IconType);
            // SVG is unsupported by the layered overlay, so any SVG custom icon
            // falls through to the built-in vector shape below.
            bool hasBitmap = icon != null && icon.SvgSource == null && icon.Frames != null;
            if (hasBitmap)
            {
                float half = (float)(s.IconSize / 2);
                float size = (float)s.IconSize;

                var saved = session.Transform;
                session.Transform = Matrix3x2.CreateTranslation(bob) *
                                    Matrix3x2.CreateRotation((float)(finalAngle * Math.PI / 180f));

                var frame = icon.GetFrame(_animTime);
                if (frame != null)
                {
                    var dst = new Rect(-half, -half, size, size);
                    var src = new Rect(0, 0, frame.Size.Width, frame.Size.Height);
                    // Default CanvasAlphaMode (Premultiplied) keeps PNG / GIF transparency correct.
                    session.DrawImage(frame, dst, src, 1.0f, CanvasImageInterpolation.HighQualityCubic);
                }
                session.Transform = saved;
            }
            else
            {
                DrawBuiltinIcon(session, bob, (float)finalAngle, s);
            }
        }

        private void DrawBuiltinIcon(CanvasDrawingSession session, Vector2 center, float angleDeg, AppSettings s)
        {
            var color = ColorsUtil.Parse(s.IconColor);
            float r = (float)(s.IconSize / 2);

            var saved = session.Transform;
            session.Transform = Matrix3x2.CreateTranslation(center) *
                                Matrix3x2.CreateRotation(angleDeg * (float)Math.PI / 180f);

            switch (s.IconType)
            {
                case "circle":
                    session.FillCircle(0, 0, r, color);
                    break;
                case "square":
                    session.FillRectangle(-r, -r, r * 2, r * 2, color);
                    break;
                case "triangle":
                    FillPolygon(session, color, r, 3, -90);
                    break;
                case "diamond":
                    FillPolygon(session, color, r, 4, -90);
                    break;
                case "heart":
                    DrawHeart(session, color, r);
                    break;
                case "smiley":
                    DrawSmiley(session, color, r);
                    break;
                case "star":
                default:
                    FillStar(session, color, r);
                    break;
            }
            session.Transform = saved;
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

        private static void DrawHeart(CanvasDrawingSession s, Color c, float r)
        {
            var pts = new Vector2[40];
            for (int i = 0; i < pts.Length; i++)
            {
                double t = i * 2 * Math.PI / pts.Length;
                double x = 16 * Math.Pow(Math.Sin(t), 3);
                double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                pts[i] = new Vector2((float)(x / 16 * r), (float)(-y / 16 * r));
            }
            using var geo = CanvasGeometry.CreatePolygon(s, pts);
            s.FillGeometry(geo, c);
        }

        private static void DrawSmiley(CanvasDrawingSession s, Color c, float r)
        {
            s.FillCircle(0, 0, r, c);
            var dark = ColorsUtil.Parse("#FF333333");
            s.FillCircle(-r * 0.35f, -r * 0.2f, r * 0.12f, dark);
            s.FillCircle(r * 0.35f, -r * 0.2f, r * 0.12f, dark);

            // smile = lower arc (downward bulge in screen coords)
            int n = 16;
            var mouth = new Vector2[n + 1];
            float R = r * 0.55f;
            float cy = -r * 0.1f;
            for (int i = 0; i <= n; i++)
            {
                double a = (20 + 140.0 * i / n) * Math.PI / 180; // 20deg .. 160deg
                mouth[i] = new Vector2((float)(Math.Cos(a) * R), cy + (float)(Math.Sin(a) * R));
            }
            for (int i = 1; i <= n; i++)
                s.DrawLine(mouth[i - 1].X, mouth[i - 1].Y, mouth[i].X, mouth[i].Y, dark, r * 0.1f);
        }

        public void Dispose()
        {
            if (_settingsHooked)
            {
                SettingsManager.Changed -= OnSettingsChanged;
                _settingsHooked = false;
            }
        }
    }
}
