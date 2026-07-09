using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
        private readonly CanvasControl _canvas;
        private readonly Image _iconImage;
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
        private Vector2 _lastCursor;
        private float _lean;
        private double _animTime;

        public EffectRenderer(CanvasControl canvas, Image iconImage)
        {
            _canvas = canvas;
            _iconImage = iconImage;
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
                _pigIcon = await IconImage.LoadAsync(_canvas,
                    Path.Combine(AppContext.BaseDirectory, "Assets/pig.png"));
                _girlIcon = await IconImage.LoadAsync(_canvas,
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
            // SVG is rendered through the XAML Image control (created on the UI thread).
            if (Path.GetExtension(path).ToLowerInvariant() == ".svg")
            {
                try
                {
                    var uri = new Uri("file:///" + path.Replace('\\', '/'));
                    _customIcon = new IconImage { SvgSource = new SvgImageSource(uri) };
                }
                catch { _customIcon = null; }
                return;
            }
            try
            {
                _customIcon = await IconImage.LoadAsync(_canvas, path);
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

            // Track cursor velocity (DIP/s) for the auto-orientation lean.
            var vel = dt > 1e-6 ? (cursor - _lastCursor) / (float)dt : Vector2.Zero;
            _lastCursor = cursor;
            float targetLean = Math.Clamp(vel.X * 0.015f, -30f, 30f);
            _lean += (targetLean - _lean) * (float)Math.Min(1, dt * 8);
            _animTime += dt;

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

        // ---------- Rope + hanging icon ----------
        private void DrawRope(CanvasDrawingSession session, AppSettings s)
        {
            var pts = _rope.Points;
            if (pts.Length < 2) return;

            var col = ColorsUtil.Parse(s.RopeColor);
            for (int pass = 0; pass < 3; pass++)
            {
                float w = (float)s.RopeWidth * (3 - pass);
                var c = col; c.A = (byte)(50 * (3 - pass));
                for (int i = 0; i < pts.Length - 1; i++)
                    session.DrawLine(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, c, w);
            }

            var bob = _rope.Bob;

            // Orientation: rope swing angle (0° when hanging straight down) + a
            // velocity-driven lean, so the pendant auto-adjusts toward mouse motion.
            var dir = pts[^1] - pts[^2];
            double swing = Math.Atan2(dir.X, dir.Y) * 180 / Math.PI;
            double finalAngle = Math.Clamp(swing + _lean, -85, 85);

            var icon = GetImageIcon(s.IconType);
            if (icon != null)
            {
                if (icon.SvgSource != null)
                {
                    // SVG: drawn via the XAML Image overlay (vector, alpha preserved).
                    double size = s.IconSize;
                    _iconImage.Visibility = Visibility.Visible;
                    _iconImage.Source = icon.SvgSource;
                    _iconImage.Width = size;
                    _iconImage.Height = size;
                    _iconImage.Stretch = Stretch.Uniform;
                    Canvas.SetLeft(_iconImage, bob.X - size / 2);
                    Canvas.SetTop(_iconImage, bob.Y - size / 2);
                    if (_iconImage.RenderTransform is not RotateTransform rt)
                    {
                        rt = new RotateTransform();
                        _iconImage.RenderTransform = rt;
                    }
                    rt.Angle = finalAngle;
                    rt.CenterX = size / 2;
                    rt.CenterY = size / 2;
                }
                else
                {
                    _iconImage.Visibility = Visibility.Collapsed;

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
            }
            else
            {
                _iconImage.Visibility = Visibility.Collapsed;
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
