using System;
using System.Collections.Generic;
using System.Numerics;

namespace MouseBeautifier.Core
{
    /// <summary>
    /// Read-only view of one completed simulation frame. It is intentionally a
    /// small value type: every monitor renderer receives the same instance before
    /// the world is advanced again, without cloning particle or rope collections.
    /// </summary>
    public readonly struct EffectFrameSnapshot
    {
        internal EffectFrameSnapshot(
            ParticleSimulation particles,
            TrailSimulation trail,
            RopeSimulator rope,
            double animationTime,
            double orbitAnimationTime,
            double orbitAngleDegrees,
            Vector2 cursor)
        {
            Particles = particles;
            Trail = trail;
            Rope = rope;
            AnimationTime = animationTime;
            OrbitAnimationTime = orbitAnimationTime;
            OrbitAngleDegrees = orbitAngleDegrees;
            Cursor = cursor;
        }

        public ParticleSimulation Particles { get; }
        public TrailSimulation Trail { get; }
        public RopeSimulator Rope { get; }
        public double AnimationTime { get; }
        public double OrbitAnimationTime { get; }
        public double OrbitAngleDegrees { get; }
        public Vector2 Cursor { get; }
    }

    public readonly struct ClickInput
    {
        public ClickInput(Vector2 position)
        {
            Position = position;
        }

        public Vector2 Position { get; }
    }

    /// <summary>
    /// Owns all mutable effect state. It has no Win2D or WinUI dependency and is
    /// advanced exclusively through one fixed-step clock.
    /// </summary>
    public sealed class EffectWorld
    {
        private readonly FixedStepClock _clock;
        private readonly TimestampedInputQueue<ClickInput> _clicks;
        private readonly List<ClickInput> _pendingClicks = new();
        private readonly Action<double> _stepCurrentFrame;
        private readonly int _clickQueueCapacity;
        private AppSettings? _currentSettings;
        private Vector2 _currentCursor;
        private bool _ropeWasEnabled;

        public EffectWorld(
            Action<string>? log = null,
            int clickQueueCapacity = 256,
            int? randomSeed = null)
        {
            _clock = new FixedStepClock();
            _clickQueueCapacity = clickQueueCapacity;
            _clicks =
                new TimestampedInputQueue<ClickInput>(
                    clickQueueCapacity);
            _stepCurrentFrame = StepCurrentFrame;
            Particles = new ParticleSimulation(randomSeed);
            Trail = new TrailSimulation();
            Rope = new RopeSimulator(log);
        }

        public ParticleSimulation Particles { get; }

        public TrailSimulation Trail { get; }

        public RopeSimulator Rope { get; }

        public double AnimationTime { get; private set; }

        public double OrbitAnimationTime { get; private set; }

        public double OrbitAngleDegrees { get; private set; }

        public double InterpolationAlpha => _clock.InterpolationAlpha;

        public int PendingClickCount =>
            _clicks.Count + _pendingClicks.Count;

        public void EnqueueClick(
            long timestamp,
            Vector2 position)
        {
            if (float.IsFinite(position.X) &&
                float.IsFinite(position.Y))
            {
                _clicks.Enqueue(
                    timestamp,
                    new ClickInput(position));
            }
        }

        public int AdvanceFrame(
            double elapsedSeconds,
            long inputTimestamp,
            Vector2 cursor,
            AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            while (_clicks.TryDequeueUpTo(
                inputTimestamp,
                out TimestampedInput<ClickInput> click))
            {
                if (_pendingClicks.Count == _clickQueueCapacity)
                {
                    _pendingClicks.RemoveAt(0);
                }

                _pendingClicks.Add(click.Value);
            }

            if (!settings.EnableClickEffects)
            {
                _pendingClicks.Clear();
                _clicks.Clear();
            }

            if (!float.IsFinite(cursor.X) ||
                !float.IsFinite(cursor.Y))
            {
                cursor = Vector2.Zero;
            }

            _currentCursor = cursor;
            _currentSettings = settings;
            return _clock.Advance(elapsedSeconds, _stepCurrentFrame);
        }

        public EffectFrameSnapshot CaptureSnapshot(Vector2 cursor)
        {
            return new EffectFrameSnapshot(
                Particles,
                Trail,
                Rope,
                AnimationTime,
                OrbitAnimationTime,
                OrbitAngleDegrees,
                cursor);
        }

        public void Reset()
        {
            _clock.Reset();
            _clicks.Clear();
            _pendingClicks.Clear();
            Particles.Clear();
            Trail.Clear();
            AnimationTime = 0;
            OrbitAnimationTime = 0;
            OrbitAngleDegrees = 0;
            _ropeWasEnabled = false;
        }

        private void Step(
            double deltaSeconds,
            Vector2 cursor,
            AppSettings settings)
        {
            AnimationTime += deltaSeconds;
            NebulaSettings nebula =
                settings.Nebula ?? new NebulaSettings();
            if (nebula.Enabled)
            {
                OrbitAnimationTime += deltaSeconds;
                double orbitSpeed =
                    double.IsFinite(nebula.AngularSpeed)
                        ? Math.Clamp(
                            nebula.AngularSpeed,
                            NebulaSettings.MinAngularSpeed,
                            NebulaSettings.MaxAngularSpeed)
                        : 0;
                // Keep an unwrapped double-precision phase. Wrapping this
                // shared angle before applying each star's speed multiplier
                // causes non-integer-speed particles to jump every 360°.
                OrbitAngleDegrees += orbitSpeed * deltaSeconds;
            }

            if (settings.EnableClickEffects &&
                _pendingClicks.Count > 0)
            {
                foreach (ClickInput click in _pendingClicks)
                {
                    Particles.Spawn(click.Position, settings);
                }

                _pendingClicks.Clear();
            }

            Particles.Update(deltaSeconds, settings);

            if (settings.EnableTrail)
            {
                Trail.Update(cursor, deltaSeconds, settings);
            }
            else
            {
                Trail.Clear();
            }

            if (settings.EnableRope)
            {
                if (!_ropeWasEnabled)
                {
                    Rope.Reset(cursor, settings);
                }

                Rope.Update(deltaSeconds, cursor, settings);
            }

            _ropeWasEnabled = settings.EnableRope;
        }

        private void StepCurrentFrame(double deltaSeconds)
        {
            Step(deltaSeconds, _currentCursor, _currentSettings!);
        }
    }
}
