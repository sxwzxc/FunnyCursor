using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using MouseBeautifier.Core;
using Windows.UI;

namespace MouseBeautifier
{
    /// <summary>Win2D projection of the resampled trail history.</summary>
    internal sealed class TrailRenderer
    {
        public void Render(
            CanvasDrawingSession session,
            TrailSimulation trail,
            Color color,
            float width)
        {
            if (trail.Samples.Count == 0 ||
                !trail.HasCurrentPoint)
            {
                return;
            }

            using var stroke = new CanvasStrokeStyle
            {
                StartCap = CanvasCapStyle.Round,
                EndCap = CanvasCapStyle.Round,
                LineJoin = CanvasLineJoin.Round,
            };

            Vector2 previous = trail.Samples[0].Position;
            for (int i = 1; i < trail.Samples.Count; i++)
            {
                TrailSample sample = trail.Samples[i];
                DrawSegment(
                    session,
                    previous,
                    sample.Position,
                    sample.Age,
                    trail.MaximumAge,
                    color,
                    width,
                    stroke);
                previous = sample.Position;
            }

            DrawSegment(
                session,
                previous,
                trail.CurrentPoint,
                0,
                trail.MaximumAge,
                color,
                width,
                stroke);
        }

        private static void DrawSegment(
            CanvasDrawingSession session,
            Vector2 from,
            Vector2 to,
            float age,
            float maximumAge,
            Color baseColor,
            float baseWidth,
            CanvasStrokeStyle stroke)
        {
            float freshness = 1 -
                Math.Clamp(age / maximumAge, 0, 1);
            // Smoothstep avoids a harsh linear cutoff at the tail.
            freshness = freshness * freshness * (3 - 2 * freshness);

            Color aura = baseColor;
            aura.A = (byte)(
                baseColor.A / 255f *
                freshness *
                58);
            session.DrawLine(
                from,
                to,
                aura,
                Math.Max(1.5f, baseWidth * freshness * 2.6f),
                stroke);

            Color core = baseColor;
            core.A = (byte)(
                baseColor.A / 255f *
                freshness *
                235);
            session.DrawLine(
                from,
                to,
                core,
                Math.Max(0.5f, baseWidth * freshness),
                stroke);
        }
    }
}
