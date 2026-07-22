using System;
using System.Collections.Generic;
using System.Numerics;

namespace MouseBeautifier.Core
{
    public readonly struct TrailSample
    {
        public TrailSample(Vector2 position, float age)
        {
            Position = position;
            Age = age;
        }

        public Vector2 Position { get; }

        public float Age { get; }
    }

    /// <summary>
    /// Distance-resampled trail history. Sample density is independent of the
    /// presentation frame rate and bounded by lifetime plus a hard point limit.
    /// </summary>
    public sealed class TrailSimulation
    {
        private const int MaximumSamples = 2048;
        private readonly List<TrailSample> _samples = new();
        private Vector2 _lastInput;
        private Vector2 _currentPoint;
        private float _distanceRemainder;
        private bool _hasInput;

        public IReadOnlyList<TrailSample> Samples => _samples;

        public Vector2 CurrentPoint => _currentPoint;

        public bool HasCurrentPoint => _hasInput;

        public float MaximumAge { get; private set; } = 0.5f;

        public void Update(
            Vector2 position,
            double deltaSeconds,
            AppSettings settings)
        {
            if (!IsFinite(position))
            {
                return;
            }

            float delta = double.IsFinite(deltaSeconds)
                ? (float)Math.Clamp(deltaSeconds, 0, 0.1)
                : 0;
            double configuredLength =
                double.IsFinite(settings.TrailLength)
                    ? settings.TrailLength
                    : 0.5;
            MaximumAge =
                (float)Math.Max(0.05, configuredLength);

            for (int i = _samples.Count - 1; i >= 0; i--)
            {
                TrailSample sample = _samples[i];
                float age = sample.Age + delta;
                if (age > MaximumAge)
                {
                    _samples.RemoveAt(i);
                }
                else
                {
                    _samples[i] =
                        new TrailSample(sample.Position, age);
                }
            }

            if (!_hasInput)
            {
                _hasInput = true;
                _lastInput = position;
                _currentPoint = position;
                _samples.Add(new TrailSample(position, 0));
                return;
            }

            double configuredWidth =
                double.IsFinite(settings.TrailWidth)
                    ? settings.TrailWidth
                    : 6;
            float spacing = (float)Math.Clamp(
                configuredWidth * 0.75,
                2,
                12);
            Vector2 segment = position - _lastInput;
            float segmentLength = segment.Length();

            if (segmentLength > 1e-5f)
            {
                Vector2 direction = segment / segmentLength;
                float travelled = 0;
                float distanceToNext = spacing - _distanceRemainder;

                while (distanceToNext <= segmentLength - travelled)
                {
                    travelled += distanceToNext;
                    Vector2 sampledPosition =
                        _lastInput + direction * travelled;
                    _samples.Add(
                        new TrailSample(sampledPosition, 0));
                    _distanceRemainder = 0;
                    distanceToNext = spacing;
                }

                _distanceRemainder += segmentLength - travelled;
            }

            _lastInput = position;
            _currentPoint = position;

            if (_samples.Count > MaximumSamples)
            {
                _samples.RemoveRange(
                    0,
                    _samples.Count - MaximumSamples);
            }
        }

        public void Clear()
        {
            _samples.Clear();
            _distanceRemainder = 0;
            _hasInput = false;
        }

        private static bool IsFinite(Vector2 value)
        {
            return float.IsFinite(value.X) &&
                float.IsFinite(value.Y);
        }
    }
}
