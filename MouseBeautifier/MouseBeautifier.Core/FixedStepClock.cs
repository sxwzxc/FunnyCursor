using System;

namespace MouseBeautifier.Core
{
    /// <summary>
    /// Converts irregular presentation-frame deltas into a bounded stream of
    /// deterministic simulation steps.
    /// </summary>
    public sealed class FixedStepClock
    {
        private double _accumulator;

        public FixedStepClock(
            double fixedDeltaSeconds = 1.0 / 120.0,
            int maximumStepsPerFrame = 12,
            double maximumFrameDeltaSeconds = 0.1)
        {
            if (!double.IsFinite(fixedDeltaSeconds) ||
                fixedDeltaSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fixedDeltaSeconds));
            }

            if (maximumStepsPerFrame < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumStepsPerFrame));
            }

            if (!double.IsFinite(maximumFrameDeltaSeconds) ||
                maximumFrameDeltaSeconds < fixedDeltaSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumFrameDeltaSeconds));
            }

            FixedDeltaSeconds = fixedDeltaSeconds;
            MaximumStepsPerFrame = maximumStepsPerFrame;
            MaximumFrameDeltaSeconds = maximumFrameDeltaSeconds;
        }

        public double FixedDeltaSeconds { get; }

        public int MaximumStepsPerFrame { get; }

        public double MaximumFrameDeltaSeconds { get; }

        public double InterpolationAlpha =>
            Math.Clamp(_accumulator / FixedDeltaSeconds, 0, 1);

        public int Advance(double elapsedSeconds, Action<double> step)
        {
            ArgumentNullException.ThrowIfNull(step);

            if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0)
            {
                return 0;
            }

            _accumulator += Math.Min(
                elapsedSeconds,
                MaximumFrameDeltaSeconds);

            double maximumBacklog =
                FixedDeltaSeconds * MaximumStepsPerFrame;
            if (_accumulator > maximumBacklog)
            {
                _accumulator = maximumBacklog;
            }

            int stepCount = 0;
            while (_accumulator + 1e-12 >= FixedDeltaSeconds &&
                   stepCount < MaximumStepsPerFrame)
            {
                step(FixedDeltaSeconds);
                _accumulator -= FixedDeltaSeconds;
                stepCount++;
            }

            if (_accumulator < 0)
            {
                _accumulator = 0;
            }

            return stepCount;
        }

        public void Reset()
        {
            _accumulator = 0;
        }
    }
}
