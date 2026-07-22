using System;
using System.Collections.Generic;
using System.Numerics;

namespace MouseBeautifier.Core
{
    public enum ParticleVisualKind
    {
        Sparkle,
        Confetti,
    }

    public struct ParticleState
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Life;
        public float MaximumLife;
        public float Size;
        public float RotationDegrees;
        public float RotationVelocity;
        public ParticleVisualKind Kind;
        public int PaletteIndex;
    }

    public struct RippleState
    {
        public Vector2 Position;
        public float Radius;
        public float MaximumRadius;
        public float Life;
        public float MaximumLife;
        public float Width;
        public int RingIndex;
    }

    /// <summary>
    /// Rendering-independent click effect simulation.
    /// </summary>
    public sealed class ParticleSimulation
    {
        private const int MaximumParticles = 4000;
        private const int ConfettiPaletteSize = 6;
        private readonly List<ParticleState> _particles = new();
        private readonly List<RippleState> _ripples = new();
        private readonly Random _random;

        public ParticleSimulation(int? randomSeed = null)
        {
            _random = randomSeed.HasValue
                ? new Random(randomSeed.Value)
                : new Random();
        }

        public IReadOnlyList<ParticleState> Particles => _particles;

        public IReadOnlyList<RippleState> Ripples => _ripples;

        public void Spawn(Vector2 position, AppSettings settings)
        {
            double configuredSpeed = double.IsFinite(settings.ClickSpeed)
                ? settings.ClickSpeed
                : 0;
            int count = Math.Clamp(
                settings.ClickParticleCount,
                1,
                600);

            switch (settings.ClickPreset)
            {
                case "ring":
                    AddRipple(
                        position,
                        (float)(configuredSpeed * 0.6 + 40),
                        0);
                    break;

                case "ripple":
                    AddRipple(
                        position,
                        (float)(configuredSpeed * 0.6 + 40),
                        0);
                    AddRipple(
                        position,
                        (float)(configuredSpeed * 0.9 + 60),
                        1);
                    break;

                case "confetti":
                    for (int i = 0; i < count; i++)
                    {
                        AddParticle(
                            position,
                            settings,
                            ParticleVisualKind.Confetti,
                            _random.Next(ConfettiPaletteSize),
                            (float)_random.NextDouble() * 360,
                            (float)(_random.NextDouble() * 600 - 300));
                    }
                    break;

                default:
                    for (int i = 0; i < count; i++)
                    {
                        AddParticle(
                            position,
                            settings,
                            ParticleVisualKind.Sparkle,
                            -1,
                            0,
                            0);
                    }
                    break;
            }
        }

        public void Update(double deltaSeconds, AppSettings settings)
        {
            if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0)
            {
                return;
            }

            float delta = (float)Math.Min(deltaSeconds, 0.1);
            float gravity = double.IsFinite(settings.ClickGravity)
                ? (float)settings.ClickGravity * delta
                : 0;
            float drag = MathF.Pow(0.96f, delta * 60);

            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                ParticleState particle = _particles[i];
                particle.Velocity.Y += gravity;
                particle.Velocity *= drag;
                particle.Position += particle.Velocity * delta;
                particle.RotationDegrees +=
                    particle.RotationVelocity * delta;
                particle.Life -= delta;

                if (particle.Life <= 0)
                {
                    _particles.RemoveAt(i);
                }
                else
                {
                    _particles[i] = particle;
                }
            }

            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                RippleState ripple = _ripples[i];
                ripple.Life -= delta;
                float progress = 1 -
                    Math.Clamp(
                        ripple.Life / ripple.MaximumLife,
                        0,
                        1);
                ripple.Radius = 4 +
                    progress * (ripple.MaximumRadius - 4);

                if (ripple.Life <= 0)
                {
                    _ripples.RemoveAt(i);
                }
                else
                {
                    _ripples[i] = ripple;
                }
            }
        }

        public void Clear()
        {
            _particles.Clear();
            _ripples.Clear();
        }

        private void AddParticle(
            Vector2 position,
            AppSettings settings,
            ParticleVisualKind kind,
            int paletteIndex,
            float rotation,
            float rotationVelocity)
        {
            double angle = _random.NextDouble() * Math.PI * 2;
            double configuredSpeed = double.IsFinite(settings.ClickSpeed)
                ? settings.ClickSpeed
                : 0;
            double speed = configuredSpeed *
                (0.25 + _random.NextDouble() * 0.85);
            float maximumLife =
                0.5f + (float)_random.NextDouble() * 0.9f;

            _particles.Add(new ParticleState
            {
                Position = position,
                Velocity = new Vector2(
                    (float)(Math.Cos(angle) * speed),
                    (float)(Math.Sin(angle) * speed)),
                Life = maximumLife,
                MaximumLife = maximumLife,
                Size = 1.5f + (float)_random.NextDouble() * 4,
                RotationDegrees = rotation,
                RotationVelocity = rotationVelocity,
                Kind = kind,
                PaletteIndex = paletteIndex,
            });

            if (_particles.Count > MaximumParticles)
            {
                _particles.RemoveRange(
                    0,
                    _particles.Count - MaximumParticles);
            }
        }

        private void AddRipple(
            Vector2 position,
            float maximumRadius,
            int ringIndex)
        {
            const float life = 0.6f;
            _ripples.Add(new RippleState
            {
                Position = position,
                Radius = 4,
                MaximumRadius = maximumRadius,
                Life = life,
                MaximumLife = life,
                Width = 3,
                RingIndex = ringIndex,
            });
        }
    }
}
