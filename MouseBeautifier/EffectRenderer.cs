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
        private static readonly Color[] NebulaPalette =
        {
            Color.FromArgb(255, 92, 232, 255),
            Color.FromArgb(255, 82, 139, 255),
            Color.FromArgb(255, 151, 102, 255),
            Color.FromArgb(255, 246, 91, 225),
            Color.FromArgb(255, 255, 92, 145),
            Color.FromArgb(255, 255, 205, 102),
            Color.FromArgb(255, 104, 255, 205),
            Color.FromArgb(255, 202, 238, 255),
        };

        private readonly struct NebulaVisual
        {
            public NebulaVisual(
                Vector2 position,
                Vector2 tangent,
                Color color,
                float radius,
                float opacity,
                float trailLength,
                float rayStrength)
            {
                Position = position;
                Tangent = tangent;
                Color = color;
                Radius = radius;
                Opacity = opacity;
                TrailLength = trailLength;
                RayStrength = rayStrength;
            }

            public Vector2 Position { get; }
            public Vector2 Tangent { get; }
            public Color Color { get; }
            public float Radius { get; }
            public float Opacity { get; }
            public float TrailLength { get; }
            public float RayStrength { get; }
        }

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

            if (s.EnableGlow)
                DrawGlow(session, cursor, s, frame.AnimationTime);
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
                    frame.OrbitAngleDegrees,
                    frame.OrbitAnimationTime);
            if (s.EnableRope) DrawRope(session, s, frame);
            _particles.Render(session, frame.Particles, s);
        }

        // ---------- Glow ----------
        private void DrawGlow(
            CanvasDrawingSession session,
            Vector2 c,
            AppSettings s,
            double animationTime)
        {
            float r = (float)s.GlowSize;
            if (!float.IsFinite(r) || r <= 0) return;
            float intensity = (float)Math.Clamp(s.GlowIntensity, 0, 1);
            float pulse = 0.96f +
                0.04f * MathF.Sin((float)animationTime * 1.8f);
            float outerRadius = r * pulse;
            Color baseColor = ColorsUtil.Parse(s.GlowColor);
            Color center = WithOpacity(baseColor, intensity * 0.72f);
            Color edge = WithOpacity(baseColor, 0);
            using var outerBrush =
                new CanvasRadialGradientBrush(session, center, edge)
            {
                Center = c,
                RadiusX = outerRadius,
                RadiusY = outerRadius,
            };
            session.FillCircle(c, outerRadius, outerBrush);

            Color core = WithOpacity(baseColor, intensity * 0.34f);
            using var coreBrush =
                new CanvasRadialGradientBrush(session, core, edge)
            {
                Center = c,
                RadiusX = outerRadius * 0.38f,
                RadiusY = outerRadius * 0.38f,
            };
            session.FillCircle(c, outerRadius * 0.42f, coreBrush);
        }

        // ---------- Nebula orbit (星云环绕) ----------
        private static void DrawOrbit(
            CanvasDrawingSession session,
            Vector2 c,
            AppSettings s,
            double orbitAngleDegrees,
            double animationTime)
        {
            int count = Math.Clamp(s.OrbitCount, 1, 120);
            float configuredRadius = (float)s.OrbitRadius;
            float radius = float.IsFinite(configuredRadius)
                ? Math.Clamp(configuredRadius, 4, 260)
                : 88;
            float configuredSize = (float)s.OrbitSize;
            float particleSize = float.IsFinite(configuredSize)
                ? Math.Clamp(configuredSize, 0.5f, 12)
                : 2.8f;
            float configuredStrokeWidth = (float)s.OrbitStrokeWidth;
            float strokeWidth = float.IsFinite(configuredStrokeWidth)
                ? Math.Clamp(configuredStrokeWidth, 0, 8)
                : 0.8f;
            Color tint = ColorsUtil.Parse(s.OrbitColor);
            float time = (float)animationTime;
            const float tau = MathF.PI * 2;
            const float diskTilt = 0.22f;
            float tiltCos = MathF.Cos(diskTilt);
            float tiltSin = MathF.Sin(diskTilt);
            Span<NebulaVisual> visuals =
                stackalloc NebulaVisual[count];

            for (int i = 0; i < count; i++)
            {
                float radialSeed = Hash01(i * 7 + 1);
                float phaseSeed = Hash01(i * 7 + 2);
                float sizeSeed = Hash01(i * 7 + 3);
                float colorSeed = Hash01(i * 7 + 4);
                float speedSeed = Hash01(i * 7 + 5);
                float glowSeed = Hash01(i * 7 + 6);
                float raySeed = Hash01(i * 7 + 7);

                // A mixed core/disk distribution creates a bright nucleus and
                // a broad stellar disk instead of a hollow ring.
                float radial = radialSeed < 0.24f
                    ? 0.10f + 0.34f * MathF.Pow(
                        radialSeed / 0.24f,
                        1.35f)
                    : 0.36f + 0.66f * MathF.Sqrt(
                        (radialSeed - 0.24f) / 0.76f);
                float orbitRadius = radius * radial;
                int arm = i % 4;
                float phase =
                    arm * tau / 4f +
                    radial * 2.8f +
                    (phaseSeed - 0.5f) * 0.95f;
                float speed =
                    0.24f +
                    (1.05f - radial) * 1.18f +
                    (speedSeed - 0.5f) * 0.20f;
                float orbitalRotation = (float)(
                    Math.IEEERemainder(
                        orbitAngleDegrees * speed,
                        360) *
                    Math.PI / 180);
                float angle =
                    phase +
                    orbitalRotation +
                    MathF.Sin(
                        time * (0.28f + speedSeed * 0.42f) +
                        phase) * 0.13f;
                float breathing =
                    1f +
                    MathF.Sin(time * 0.55f + phase) *
                    (0.025f + speedSeed * 0.035f);
                float ellipse = 0.56f + colorSeed * 0.22f;
                float localX =
                    MathF.Cos(angle) * orbitRadius * breathing;
                float localY =
                    MathF.Sin(angle) * orbitRadius *
                    breathing * ellipse;
                Vector2 position = c + new Vector2(
                    localX * tiltCos - localY * tiltSin,
                    localX * tiltSin + localY * tiltCos);
                position += new Vector2(
                    MathF.Sin(time * 0.31f + phaseSeed * tau),
                    MathF.Cos(time * 0.27f + colorSeed * tau)) *
                    radius * (0.008f + speedSeed * 0.018f);

                Vector2 localTangent = new(
                    -MathF.Sin(angle),
                    MathF.Cos(angle) * ellipse);
                Vector2 tangent = Vector2.Normalize(new Vector2(
                    localTangent.X * tiltCos -
                        localTangent.Y * tiltSin,
                    localTangent.X * tiltSin +
                        localTangent.Y * tiltCos));

                float twinkle =
                    0.64f +
                    0.36f *
                    (0.5f + 0.5f * MathF.Sin(
                        time * (1.2f + speedSeed * 4.1f) +
                        phaseSeed * tau));
                float radiusFactor =
                    0.16f +
                    1.34f *
                    MathF.Pow(sizeSeed, 2.55f);
                float dotRadius =
                    particleSize * radiusFactor * twinkle;
                float opacity =
                    (0.28f + (1.05f - radial) * 0.62f) *
                    (0.68f + twinkle * 0.32f);

                float colorPhase =
                    colorSeed +
                    arm * 0.11f +
                    radial * 0.16f +
                    time * (0.004f + speedSeed * 0.007f);
                Color particleColor =
                    PaletteColor(colorPhase, tint);
                float trailLength =
                    dotRadius *
                    (1.8f + speed * 2.6f) *
                    (0.35f + glowSeed * 0.9f);
                float rayStrength =
                    raySeed > 0.82f
                        ? (raySeed - 0.82f) / 0.18f
                        : 0;
                visuals[i] = new NebulaVisual(
                    position,
                    tangent,
                    particleColor,
                    dotRadius,
                    opacity,
                    trailLength,
                    rayStrength);
            }

            CanvasBlend savedBlend = session.Blend;
            using var trailStroke = new CanvasStrokeStyle
            {
                StartCap = CanvasCapStyle.Round,
                EndCap = CanvasCapStyle.Round,
            };
            try
            {
                // Additive blending is the key difference between opaque dots
                // and luminous energy: overlapping colors bloom naturally.
                session.Blend = CanvasBlend.Add;

                float cloudPulse =
                    1f + MathF.Sin(time * 0.42f) * 0.045f;
                DrawNebulaCloud(
                    session,
                    c + new Vector2(-radius * 0.13f, radius * 0.02f),
                    radius * 1.10f * cloudPulse,
                    radius * 0.68f * cloudPulse,
                    WithOpacity(
                        PaletteColor(0.12f + time * 0.003f, tint),
                        0.12f));
                DrawNebulaCloud(
                    session,
                    c + new Vector2(radius * 0.17f, -radius * 0.08f),
                    radius * 0.86f / cloudPulse,
                    radius * 0.54f / cloudPulse,
                    WithOpacity(
                        PaletteColor(0.42f + time * 0.002f, tint),
                        0.11f));
                DrawNebulaCloud(
                    session,
                    c + new Vector2(0, radius * 0.08f),
                    radius * 0.58f,
                    radius * 0.40f,
                    WithOpacity(
                        PaletteColor(0.72f - time * 0.0025f, tint),
                        0.13f));

                // Pass 1: comet trails and broad bokeh halos.
                foreach (NebulaVisual visual in visuals)
                {
                    Vector2 trailStart =
                        visual.Position -
                        visual.Tangent * visual.TrailLength;
                    session.DrawLine(
                        trailStart,
                        visual.Position,
                        WithOpacity(
                            visual.Color,
                            visual.Opacity * 0.19f),
                        Math.Max(0.65f, visual.Radius * 0.62f),
                        trailStroke);
                    session.FillCircle(
                        visual.Position,
                        visual.Radius * 5.2f,
                        WithOpacity(
                            visual.Color,
                            visual.Opacity * 0.035f));
                    session.FillCircle(
                        visual.Position,
                        visual.Radius * 2.7f,
                        WithOpacity(
                            visual.Color,
                            visual.Opacity * 0.12f));
                }

                // Pass 2: saturated bodies, white-hot cores and diffraction
                // spikes. Separating passes makes the cloud feel layered.
                foreach (NebulaVisual visual in visuals)
                {
                    session.FillCircle(
                        visual.Position,
                        visual.Radius,
                        WithOpacity(
                            visual.Color,
                            visual.Opacity * 0.92f));

                    if (strokeWidth > 0)
                    {
                        session.DrawCircle(
                            visual.Position,
                            visual.Radius + strokeWidth * 0.5f,
                            WithOpacity(
                                visual.Color,
                                visual.Opacity * 0.86f),
                            strokeWidth);
                    }

                    Color hotCore = Blend(
                        visual.Color,
                        Color.FromArgb(255, 255, 255, 255),
                        0.58f);
                    session.FillCircle(
                        visual.Position,
                        Math.Max(0.42f, visual.Radius * 0.34f),
                        WithOpacity(
                            hotCore,
                            visual.Opacity * 0.95f));

                    if (visual.RayStrength > 0)
                    {
                        float ray =
                            visual.Radius *
                            (2.4f + visual.RayStrength * 3.8f);
                        Vector2 normal = new(
                            -visual.Tangent.Y,
                            visual.Tangent.X);
                        Color rayColor = WithOpacity(
                            hotCore,
                            visual.Opacity *
                                (0.22f +
                                visual.RayStrength * 0.32f));
                        session.DrawLine(
                            visual.Position -
                                visual.Tangent * ray,
                            visual.Position +
                                visual.Tangent * ray,
                            rayColor,
                            0.72f);
                        session.DrawLine(
                            visual.Position -
                                normal * ray * 0.58f,
                            visual.Position +
                                normal * ray * 0.58f,
                            rayColor,
                            0.62f);
                    }
                }

                // A compact multi-color nucleus visually binds the four arms.
                DrawNebulaCloud(
                    session,
                    c,
                    radius * 0.23f,
                    radius * 0.17f,
                    WithOpacity(
                        PaletteColor(0.90f + time * 0.006f, tint),
                        0.24f));
                session.FillCircle(
                    c,
                    Math.Max(1.2f, particleSize * 0.72f),
                    Color.FromArgb(210, 245, 250, 255));
            }
            finally
            {
                session.Blend = savedBlend;
            }
        }

        private static void DrawNebulaCloud(
            CanvasDrawingSession session,
            Vector2 center,
            float radiusX,
            float radiusY,
            Color centerColor)
        {
            Color edge = centerColor;
            edge.A = 0;
            using var brush =
                new CanvasRadialGradientBrush(session, centerColor, edge)
            {
                Center = center,
                RadiusX = radiusX,
                RadiusY = radiusY,
            };
            session.FillEllipse(
                center,
                radiusX,
                radiusY,
                brush);
        }

        private static Color WithOpacity(Color color, float opacity)
        {
            color.A = (byte)Math.Clamp(
                color.A * Math.Clamp(opacity, 0, 1),
                0,
                255);
            return color;
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Color.FromArgb(
                from.A,
                (byte)(from.R + (to.R - from.R) * amount),
                (byte)(from.G + (to.G - from.G) * amount),
                (byte)(from.B + (to.B - from.B) * amount));
        }

        private static Color PaletteColor(float phase, Color tint)
        {
            phase -= MathF.Floor(phase);
            float scaled = phase * NebulaPalette.Length;
            int first = Math.Min(
                (int)scaled,
                NebulaPalette.Length - 1);
            int second = (first + 1) % NebulaPalette.Length;
            float amount = scaled - first;
            // Smooth interpolation prevents visible color bands as particles
            // slowly travel through the cosmic palette.
            amount = amount * amount * (3 - 2 * amount);
            Color color = Blend(
                NebulaPalette[first],
                NebulaPalette[second],
                amount);
            // Keep the palette variation, but let the user's tint remain the
            // dominant color so changing the setting is immediately visible.
            color = Blend(color, tint, 0.72f);
            color.A = tint.A;
            return color;
        }

        private static float Hash01(int value)
        {
            uint x = unchecked((uint)value);
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return (x & 0x00ffffff) / 16777216f;
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
            float configuredWidth = (float)s.RopeWidth;
            float ropeWidth = float.IsFinite(configuredWidth)
                ? Math.Clamp(configuredWidth, 1, 20)
                : 3;

            DrawRopeStyle(
                session,
                pts,
                col,
                ropeWidth,
                s.RopeStyle,
                frame.AnimationTime);

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
                            double imageWidth = src.Width * imageScale;
                            double imageHeight = src.Height * imageScale;
                            var dst = new Rect(
                                -imageWidth / 2,
                                0,
                                imageWidth,
                                imageHeight);
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

        private static void DrawRopeStyle(
            CanvasDrawingSession session,
            Vector2[] points,
            Color color,
            float width,
            string? style,
            double animationTime)
        {
            using var stroke = new CanvasStrokeStyle
            {
                LineJoin = CanvasLineJoin.Round,
                StartCap = CanvasCapStyle.Round,
                EndCap = CanvasCapStyle.Round,
            };

            switch (style)
            {
                case "glass":
                    DrawGlassRope(session, points, color, width, stroke);
                    break;
                case "minimal":
                    DrawMinimalRope(session, points, color, width, stroke);
                    break;
                case "pulse":
                    DrawPulseRope(
                        session,
                        points,
                        color,
                        width,
                        animationTime,
                        stroke);
                    break;
                default:
                    DrawNeonRope(session, points, color, width, stroke);
                    break;
            }
        }

        private static void DrawNeonRope(
            CanvasDrawingSession session,
            Vector2[] points,
            Color color,
            float width,
            CanvasStrokeStyle stroke)
        {
            DrawRopeGlow(session, points, color, width, 0.14f, 3.2f, stroke);
            Color edge = Blend(
                color,
                Color.FromArgb(255, 8, 14, 28),
                0.72f);
            Color body = Blend(
                color,
                Color.FromArgb(255, 255, 255, 255),
                0.10f);
            Color highlight = Blend(
                color,
                Color.FromArgb(255, 255, 255, 255),
                0.68f);

            DrawRopeLayer(session, points, edge, width * 1.85f, 0.86f, 0, stroke);
            DrawRopeLayer(session, points, body, width, 0.94f, 0, stroke);
            DrawRopeLayer(
                session,
                points,
                highlight,
                Math.Max(0.55f, width * 0.20f),
                0.48f,
                width * 0.18f,
                stroke);
        }

        private static void DrawGlassRope(
            CanvasDrawingSession session,
            Vector2[] points,
            Color color,
            float width,
            CanvasStrokeStyle stroke)
        {
            Color edge = Blend(
                color,
                Color.FromArgb(255, 7, 12, 24),
                0.84f);
            Color glass = Blend(
                color,
                Color.FromArgb(255, 255, 255, 255),
                0.24f);
            Color inner = Blend(
                color,
                Color.FromArgb(255, 255, 255, 255),
                0.72f);

            DrawRopeLayer(session, points, edge, width * 2.20f, 0.92f, 0, stroke);
            DrawRopeLayer(session, points, glass, width * 1.42f, 0.48f, 0, stroke);
            DrawRopeLayer(
                session,
                points,
                inner,
                Math.Max(0.65f, width * 0.42f),
                0.82f,
                width * 0.20f,
                stroke);
        }

        private static void DrawMinimalRope(
            CanvasDrawingSession session,
            Vector2[] points,
            Color color,
            float width,
            CanvasStrokeStyle stroke)
        {
            Color edge = Blend(
                color,
                Color.FromArgb(255, 10, 16, 30),
                0.52f);
            Color highlight = Blend(
                color,
                Color.FromArgb(255, 255, 255, 255),
                0.58f);

            DrawRopeLayer(session, points, edge, width * 1.32f, 0.68f, 0, stroke);
            DrawRopeLayer(session, points, color, width, 0.96f, 0, stroke);
            DrawRopeLayer(
                session,
                points,
                highlight,
                Math.Max(0.45f, width * 0.14f),
                0.38f,
                width * 0.16f,
                stroke);
        }

        private static void DrawPulseRope(
            CanvasDrawingSession session,
            Vector2[] points,
            Color color,
            float width,
            double animationTime,
            CanvasStrokeStyle stroke)
        {
            float pulse = 0.5f + 0.5f *
                MathF.Sin((float)animationTime * 2.4f);
            float glowOpacity = 0.10f + pulse * 0.16f;
            float glowWidth = width * (2.8f + pulse * 1.1f);
            DrawRopeGlow(
                session,
                points,
                color,
                glowWidth / 3.2f,
                glowOpacity,
                3.2f,
                stroke);

            Color edge = Blend(
                color,
                Color.FromArgb(255, 8, 14, 28),
                0.72f);
            Color body = Blend(
                color,
                Color.FromArgb(255, 255, 255, 255),
                0.08f + pulse * 0.16f);
            Color highlight = Blend(
                color,
                Color.FromArgb(255, 255, 255, 255),
                0.64f + pulse * 0.18f);

            DrawRopeLayer(session, points, edge, width * 1.85f, 0.86f, 0, stroke);
            DrawRopeLayer(session, points, body, width, 0.90f + pulse * 0.08f, 0, stroke);
            DrawRopeLayer(
                session,
                points,
                highlight,
                Math.Max(0.55f, width * 0.20f),
                0.36f + pulse * 0.22f,
                width * 0.18f,
                stroke);
        }

        private static void DrawRopeGlow(
            CanvasDrawingSession session,
            Vector2[] points,
            Color color,
            float width,
            float opacity,
            float widthMultiplier,
            CanvasStrokeStyle stroke)
        {
            CanvasBlend savedBlend = session.Blend;
            try
            {
                session.Blend = CanvasBlend.Add;
                DrawRopeLayer(
                    session,
                    points,
                    color,
                    width * widthMultiplier,
                    opacity,
                    0,
                    stroke);
            }
            finally
            {
                session.Blend = savedBlend;
            }
        }

        private static void DrawRopeLayer(
            CanvasDrawingSession session,
            Vector2[] points,
            Color color,
            float width,
            float opacity,
            float offset,
            CanvasStrokeStyle stroke)
        {
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 start = points[i];
                Vector2 end = points[i + 1];
                Vector2 direction = end - start;
                float length = direction.Length();
                if (!float.IsFinite(length) || length < 0.001f)
                {
                    continue;
                }

                Vector2 normal = new(
                    -direction.Y / length,
                    direction.X / length);
                Vector2 shift = normal * offset;
                session.DrawLine(
                    start + shift,
                    end + shift,
                    WithOpacity(color, opacity),
                    width,
                    stroke);
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
            float configuredWidth = (float)s.RopeWidth;
            float width = float.IsFinite(configuredWidth)
                ? Math.Clamp(configuredWidth, 1, 20)
                : 3;
            float r = Math.Max(width * 1.2f, (float)s.IconSize * 0.05f);
            Color ropeColor = ColorsUtil.Parse(s.RopeColor);
            Color edge = Blend(
                ropeColor,
                Color.FromArgb(255, 8, 14, 28),
                0.72f);
            Color core = Blend(
                ropeColor,
                Color.FromArgb(255, 255, 255, 255),
                0.18f);
            session.FillCircle(p.Tip.X, p.Tip.Y, r, WithOpacity(edge, 0.92f));
            session.FillCircle(
                p.Tip.X,
                p.Tip.Y,
                r * 0.64f,
                WithOpacity(core, 0.96f));
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
