using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using MouseBeautifier.Core;
using Windows.UI;

namespace MouseBeautifier
{
    /// <summary>
    /// Win2D projection for the nebula effect. Geometry comes from the pure
    /// NebulaLayout; every layer consumes one explicit peak-opacity setting.
    /// </summary>
    internal static class NebulaRenderer
    {
        public static void Render(
            CanvasDrawingSession session,
            Vector2 center,
            in NebulaRenderSettings settings,
            double orbitAngleDegrees,
            double animationTime)
        {
            if (!settings.Enabled)
            {
                return;
            }

            Span<NebulaParticleVisual> visuals =
                stackalloc NebulaParticleVisual[settings.ParticleCount];
            NebulaLayout.Generate(
                visuals,
                center,
                settings,
                orbitAngleDegrees,
                animationTime);

            Color particleColor = ForceOpaque(
                ColorsUtil.Parse(settings.ParticleColor));
            Color cloudColor = ForceOpaque(
                ColorsUtil.Parse(settings.CloudColor));
            Color strokeColor = ForceOpaque(
                ColorsUtil.Parse(settings.StrokeColor));
            Color haloColor = ForceOpaque(
                ColorsUtil.Parse(settings.HaloColor));
            float time = double.IsFinite(animationTime)
                ? (float)animationTime
                : 0;

            CanvasBlend savedBlend = session.Blend;
            using var trailStroke = new CanvasStrokeStyle
            {
                StartCap = CanvasCapStyle.Round,
                EndCap = CanvasCapStyle.Round,
            };

            try
            {
                // Alpha (SourceOver) compositing, not additive: overlapping
                // soft glows must blend hue instead of summing luminance, or
                // two near-max colors (blue is already 255) clip to white and
                // produce ugly white blobs where clouds and halos stack.
                session.Blend = CanvasBlend.SourceOver;
                DrawClouds(
                    session,
                    center,
                    settings,
                    cloudColor,
                    time);
                DrawTrails(
                    session,
                    visuals,
                    particleColor,
                    settings.TrailOpacity,
                    trailStroke);
                DrawHalos(
                    session,
                    visuals,
                    haloColor,
                    settings.HaloOpacity,
                    settings.HaloSize,
                    settings.StarSize);
                DrawParticles(
                    session,
                    visuals,
                    particleColor,
                    settings.ParticleOpacity,
                    strokeColor,
                    settings.StrokeWidth,
                    settings.StrokeOpacity);
            }
            finally
            {
                session.Blend = savedBlend;
            }
        }

        private static void DrawClouds(
            CanvasDrawingSession session,
            Vector2 center,
            in NebulaRenderSettings settings,
            Color color,
            float time)
        {
            if (settings.CloudOpacity <= 0)
            {
                return;
            }

            float pulse =
                1f + MathF.Sin(time * 0.42f) * 0.045f;
            DrawRadialGlow(
                session,
                center + new Vector2(
                    -settings.Radius * 0.13f,
                    settings.Radius * 0.02f),
                settings.Radius * 1.10f * pulse,
                settings.Radius * 0.68f * pulse,
                color,
                settings.CloudOpacity);
            DrawRadialGlow(
                session,
                center + new Vector2(
                    settings.Radius * 0.17f,
                    -settings.Radius * 0.08f),
                settings.Radius * 0.86f / pulse,
                settings.Radius * 0.54f / pulse,
                color,
                settings.CloudOpacity * 0.72f);
            DrawRadialGlow(
                session,
                center + new Vector2(
                    0,
                    settings.Radius * 0.08f),
                settings.Radius * 0.58f,
                settings.Radius * 0.40f,
                color,
                settings.CloudOpacity * 0.54f);
        }

        private static void DrawTrails(
            CanvasDrawingSession session,
            ReadOnlySpan<NebulaParticleVisual> visuals,
            Color color,
            float opacity,
            CanvasStrokeStyle stroke)
        {
            if (opacity <= 0)
            {
                return;
            }

            foreach (NebulaParticleVisual visual in visuals)
            {
                if (visual.TrailLength <= 0.01f)
                {
                    continue;
                }

                session.DrawLine(
                    visual.Position -
                        visual.Tangent * visual.TrailLength,
                    visual.Position,
                    WithOpacity(
                        AdjustBrightness(color, visual.Brightness),
                        opacity),
                    Math.Max(0.65f, visual.Radius * 0.62f),
                    stroke);
            }
        }

        private static void DrawHalos(
            CanvasDrawingSession session,
            ReadOnlySpan<NebulaParticleVisual> visuals,
            Color color,
            float opacity,
            float size,
            float starSize)
        {
            if (opacity <= 0 || size <= 0)
            {
                return;
            }

            // A linear center-to-edge gradient reads as a flat disk with a
            // hard rim. Emitted light instead has a hot core that falls off
            // quickly and feathers into a long faint tail, so shape the stops
            // to concentrate brightness at the center and fade to nothing
            // well before the outer radius (no visible circular boundary).
            CanvasGradientStop[] stops =
            {
                new() { Position = 0f, Color = WithOpacity(color, opacity) },
                new()
                {
                    Position = 0.14f,
                    Color = WithOpacity(color, opacity * 0.78f),
                },
                new()
                {
                    Position = 0.36f,
                    Color = WithOpacity(color, opacity * 0.32f),
                },
                new()
                {
                    Position = 0.66f,
                    Color = WithOpacity(color, opacity * 0.08f),
                },
                new() { Position = 1f, Color = WithOpacity(color, 0f) },
            };
            using var brush = new CanvasRadialGradientBrush(session, stops);
            foreach (NebulaParticleVisual visual in visuals)
            {
                // Anchor the glow to the configured star size (plus the
                // per-particle twinkle) so HaloSize has a large, obvious range
                // that stays visible well beyond the solid particle body.
                float radius =
                    (starSize + visual.Radius) * (0.6f + size);
                brush.Center = visual.Position;
                brush.RadiusX = radius;
                brush.RadiusY = radius;
                session.FillCircle(
                    visual.Position,
                    radius,
                    brush);
            }
        }

        private static void DrawParticles(
            CanvasDrawingSession session,
            ReadOnlySpan<NebulaParticleVisual> visuals,
            Color color,
            float opacity,
            Color strokeColor,
            float strokeWidth,
            float strokeOpacity)
        {
            bool drawBody = opacity > 0;
            bool drawStroke = strokeWidth > 0 && strokeOpacity > 0;
            if (!drawBody && !drawStroke)
            {
                return;
            }

            Span<int> depthOrder = stackalloc int[visuals.Length];
            for (int i = 0; i < depthOrder.Length; i++)
            {
                depthOrder[i] = i;
                int position = i;
                while (position > 0 &&
                    visuals[depthOrder[position - 1]].Depth >
                    visuals[depthOrder[position]].Depth)
                {
                    (depthOrder[position - 1], depthOrder[position]) =
                        (depthOrder[position], depthOrder[position - 1]);
                    position--;
                }
            }

            foreach (int index in depthOrder)
            {
                NebulaParticleVisual visual = visuals[index];
                if (drawBody)
                {
                    session.FillCircle(
                        visual.Position,
                        visual.Radius,
                        WithOpacity(
                            AdjustBrightness(color, visual.Brightness),
                            opacity));
                }

                // The stroke rides the rim of the center dot, forming the
                // middle band of the center -> stroke -> halo structure.
                if (drawStroke)
                {
                    session.DrawCircle(
                        visual.Position,
                        visual.Radius + strokeWidth * 0.5f,
                        WithOpacity(
                            AdjustBrightness(strokeColor, visual.Brightness),
                            strokeOpacity),
                        strokeWidth);
                }
            }
        }

        private static void DrawRadialGlow(
            CanvasDrawingSession session,
            Vector2 center,
            float radiusX,
            float radiusY,
            Color color,
            float opacity)
        {
            if (opacity <= 0 || radiusX <= 0 || radiusY <= 0)
            {
                return;
            }

            Color peak = WithOpacity(color, opacity);
            Color edge = WithOpacity(color, 0);
            using var brush =
                new CanvasRadialGradientBrush(session, peak, edge)
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

        private static Color AdjustBrightness(
            Color color,
            float brightness)
        {
            brightness = Math.Clamp(brightness, 0, 1);
            return Color.FromArgb(
                255,
                (byte)Math.Clamp(
                    MathF.Round(color.R * brightness),
                    0,
                    255),
                (byte)Math.Clamp(
                    MathF.Round(color.G * brightness),
                    0,
                    255),
                (byte)Math.Clamp(
                    MathF.Round(color.B * brightness),
                    0,
                    255));
        }

        private static Color ForceOpaque(Color color)
        {
            color.A = 255;
            return color;
        }

        private static Color WithOpacity(Color color, float opacity)
        {
            color.A = (byte)Math.Clamp(
                MathF.Round(Math.Clamp(opacity, 0, 1) * 255),
                0,
                255);
            return color;
        }
    }
}
