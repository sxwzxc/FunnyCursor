using System;
using System.Numerics;

namespace MouseBeautifier.Core
{
    /// <summary>
    /// Rendering-independent geometry for a pendant welded to the rope bob.
    /// </summary>
    public static class PendantGeometry
    {
        public readonly struct PendantState
        {
            public PendantState(Vector2 tip, Vector2 direction, float size)
            {
                if (!float.IsFinite(tip.X) ||
                    !float.IsFinite(tip.Y))
                {
                    tip = Vector2.Zero;
                }

                float directionLength = direction.Length();
                if (!float.IsFinite(directionLength) ||
                    directionLength < 1e-4f)
                {
                    direction = new Vector2(0, 1);
                }
                else
                {
                    direction /= directionLength;
                }

                if (!float.IsFinite(size) || size < 0)
                {
                    size = 0;
                }

                Tip = tip;
                Direction = direction;
                AngleRad = MathF.Atan2(-direction.X, direction.Y);
                BaseCenter = tip + direction * size;
            }

            public Vector2 Tip { get; }

            public Vector2 Direction { get; }

            public float AngleRad { get; }

            public Vector2 BaseCenter { get; }
        }

        public static PendantState ComputePendant(
            Vector2[]? ropePoints,
            float iconSize)
        {
            if (ropePoints == null || ropePoints.Length < 2)
            {
                return new PendantState(
                    Vector2.Zero,
                    new Vector2(0, 1),
                    iconSize);
            }

            Vector2 tip = ropePoints[^1];
            Vector2 direction = tip - ropePoints[^2];
            float length = direction.Length();
            direction = !float.IsFinite(length) || length < 1e-4f
                ? new Vector2(0, 1)
                : direction / length;

            if (!float.IsFinite(tip.X) ||
                !float.IsFinite(tip.Y))
            {
                return new PendantState(
                    Vector2.Zero,
                    new Vector2(0, 1),
                    iconSize);
            }

            return new PendantState(tip, direction, iconSize);
        }

        /// <summary>
        /// Creates the complete local-icon to render-target transform while
        /// retaining the overlay surface's screen-pixel to monitor-local-DIP
        /// projection.
        /// </summary>
        public static Matrix3x2 CreateRenderTransform(
            in PendantState pendant,
            Matrix3x2 screenToRender)
        {
            return Matrix3x2.CreateRotation(pendant.AngleRad) *
                Matrix3x2.CreateTranslation(pendant.Tip) *
                screenToRender;
        }

        public static Vector2 TransformPoint(
            in PendantState pendant,
            Vector2 localPoint)
        {
            float cosine = MathF.Cos(pendant.AngleRad);
            float sine = MathF.Sin(pendant.AngleRad);
            return new Vector2(
                cosine * localPoint.X -
                    sine * localPoint.Y +
                    pendant.Tip.X,
                sine * localPoint.X +
                    cosine * localPoint.Y +
                    pendant.Tip.Y);
        }

        public static Vector2[] StarLocalPolygon(float size)
        {
            const int points = 5;
            var vertices = new Vector2[points * 2];
            float outerRadius = size * 0.5f;
            float innerRadius = outerRadius * 0.45f;
            float centerY = size / 2f;

            for (int i = 0; i < vertices.Length; i++)
            {
                float radius = i % 2 == 0
                    ? outerRadius
                    : innerRadius;
                double angle = -Math.PI / 2 + i * Math.PI / points;
                vertices[i] = new Vector2(
                    (float)(Math.Cos(angle) * radius),
                    centerY + (float)(Math.Sin(angle) * radius));
            }

            return vertices;
        }
    }
}
