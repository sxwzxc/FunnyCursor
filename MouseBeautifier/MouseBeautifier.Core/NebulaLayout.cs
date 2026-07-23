using System;
using System.Numerics;

namespace MouseBeautifier.Core
{
    public readonly struct NebulaParticleVisual
    {
        public NebulaParticleVisual(
            Vector2 position,
            Vector2 tangent,
            float radius,
            float brightness,
            float trailLength,
            float depth)
        {
            Position = position;
            Tangent = tangent;
            Radius = radius;
            Brightness = brightness;
            TrailLength = trailLength;
            Depth = depth;
        }

        public Vector2 Position { get; }
        public Vector2 Tangent { get; }
        public float Radius { get; }
        public float Brightness { get; }
        public float TrailLength { get; }
        public float Depth { get; }
    }

    /// <summary>
    /// Pure, deterministic geometry for the nebula. Win2D-specific compositing
    /// stays in the UI project so layout can be covered by headless tests.
    /// </summary>
    public static class NebulaLayout
    {
        public static void Generate(
            Span<NebulaParticleVisual> destination,
            Vector2 center,
            in NebulaRenderSettings settings,
            double orbitAngleDegrees,
            double animationTime)
        {
            if (destination.Length < settings.ParticleCount)
            {
                throw new ArgumentException(
                    "The destination span is smaller than ParticleCount.",
                    nameof(destination));
            }

            float time = double.IsFinite(animationTime)
                ? (float)animationTime
                : 0;
            double unwrappedAngle = double.IsFinite(orbitAngleDegrees)
                ? orbitAngleDegrees
                : 0;
            const float tau = MathF.PI * 2;
            const float diskTilt = 0.22f;
            float tiltCos = MathF.Cos(diskTilt);
            float tiltSin = MathF.Sin(diskTilt);
            float movementDirection = MathF.Sign(settings.AngularSpeed);

            for (int i = 0; i < settings.ParticleCount; i++)
            {
                float radialSeed = Hash01(i * 7 + 1);
                float phaseSeed = Hash01(i * 7 + 2);
                float sizeSeed = Hash01(i * 7 + 3);
                float speedSeed = Hash01(i * 7 + 5);
                float glowSeed = Hash01(i * 7 + 6);

                float radial = radialSeed < 0.24f
                    ? 0.10f + 0.34f * MathF.Pow(
                        radialSeed / 0.24f,
                        1.35f)
                    : 0.36f + 0.66f * MathF.Sqrt(
                        (radialSeed - 0.24f) / 0.76f);
                float orbitRadius = settings.Radius * radial;
                int arm = i % 4;
                float phase =
                    arm * tau / 4f +
                    radial * 2.8f +
                    (phaseSeed - 0.5f) * 0.95f;
                float speedMultiplier =
                    0.24f +
                    (1.05f - radial) * 1.18f +
                    (speedSeed - 0.5f) * 0.20f;
                float orbitalRotation = (float)(
                    Math.IEEERemainder(
                        unwrappedAngle * speedMultiplier,
                        360) *
                    Math.PI /
                    180);
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
                float ellipse = 0.56f + Hash01(i * 7 + 4) * 0.22f;
                float localX =
                    MathF.Cos(angle) * orbitRadius * breathing;
                float localY =
                    MathF.Sin(angle) *
                    orbitRadius *
                    breathing *
                    ellipse;
                Vector2 position = center + new Vector2(
                    localX * tiltCos - localY * tiltSin,
                    localX * tiltSin + localY * tiltCos);
                position += new Vector2(
                    MathF.Sin(time * 0.31f + phaseSeed * tau),
                    MathF.Cos(time * 0.27f + speedSeed * tau)) *
                    settings.Radius *
                    (0.008f + speedSeed * 0.018f);

                Vector2 localTangent = new(
                    -MathF.Sin(angle),
                    MathF.Cos(angle) * ellipse);
                Vector2 tangent = Vector2.Normalize(new Vector2(
                    localTangent.X * tiltCos -
                        localTangent.Y * tiltSin,
                    localTangent.X * tiltSin +
                        localTangent.Y * tiltCos));
                if (movementDirection < 0)
                {
                    tangent = -tangent;
                }

                float twinkle =
                    0.5f +
                    0.5f * MathF.Sin(
                        time * (1.2f + speedSeed * 4.1f) +
                        phaseSeed * tau);
                float radiusFactor =
                    0.55f +
                    0.90f * MathF.Pow(sizeSeed, 2.2f);
                float dotRadius =
                    settings.StarSize *
                    radiusFactor *
                    (0.94f + twinkle * 0.06f);
                float brightness = Math.Clamp(
                    0.68f +
                    twinkle * 0.22f +
                    (1f - radial) * 0.10f,
                    0.55f,
                    1f);

                float angularVelocity =
                    MathF.Abs(settings.AngularSpeed) *
                    speedMultiplier *
                    MathF.PI /
                    180f;
                float trailLength =
                    angularVelocity *
                    orbitRadius *
                    0.075f *
                    (0.75f + glowSeed * 0.5f);

                destination[i] = new NebulaParticleVisual(
                    position,
                    tangent,
                    dotRadius,
                    brightness,
                    trailLength,
                    localY);
            }
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
    }
}
