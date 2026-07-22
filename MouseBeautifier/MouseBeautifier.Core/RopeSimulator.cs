using System;
using System.Numerics;

namespace MouseBeautifier.Core
{
    /// <summary>
    /// Verlet-integrated rope. Point zero is pinned to the cursor and the final
    /// point is the pendant attachment point.
    /// </summary>
    public sealed class RopeSimulator
    {
        private readonly Action<string>? _log;
        private Vector2[] _positions = Array.Empty<Vector2>();
        private Vector2[] _previous = Array.Empty<Vector2>();
        private int _segmentCount;
        private float _segmentLength;
        private float _gravity;
        private float _damping;
        private float _stiffness;
        private Vector2 _lastAnchor;
        private bool _anchored;

        public RopeSimulator(Action<string>? log = null)
        {
            _log = log;
        }

        public void ApplySettings(AppSettings settings)
        {
            int segmentCount = Math.Clamp(settings.RopeSegments, 2, 120);
            double configuredRopeLength =
                double.IsFinite(settings.RopeLength)
                    ? settings.RopeLength
                    : 170;
            double configuredIconSize =
                double.IsFinite(settings.IconSize)
                    ? settings.IconSize
                    : 38;
            float availableLength = Math.Max(
                8f,
                (float)(configuredRopeLength - configuredIconSize));
            float segmentLength = availableLength / segmentCount;
            bool geometryChanged =
                segmentCount != _segmentCount ||
                MathF.Abs(segmentLength - _segmentLength) > 0.001f;

            _segmentLength = segmentLength;
            _gravity = float.IsFinite((float)settings.RopeGravity)
                ? (float)settings.RopeGravity
                : 0;
            _damping = double.IsFinite(settings.RopeDamping)
                ? (float)Math.Clamp(settings.RopeDamping, 0.5, 0.999)
                : 0.9f;
            _stiffness = double.IsFinite(settings.RopeStiffness)
                ? (float)Math.Clamp(settings.RopeStiffness, 0, 1)
                : 0.6f;

            if (!geometryChanged && _positions.Length != 0)
            {
                return;
            }

            _segmentCount = segmentCount;
            _positions = new Vector2[_segmentCount + 1];
            _previous = new Vector2[_segmentCount + 1];
            for (int i = 0; i <= _segmentCount; i++)
            {
                _positions[i] = new Vector2(0, i * _segmentLength);
                _previous[i] = _positions[i];
            }

            _anchored = false;
        }

        public void Update(double deltaSeconds, Vector2 anchor, AppSettings settings)
        {
            int configuredSegments =
                Math.Clamp(settings.RopeSegments, 2, 120);
            double ropeLength = double.IsFinite(settings.RopeLength)
                ? settings.RopeLength
                : 170;
            double iconSize = double.IsFinite(settings.IconSize)
                ? settings.IconSize
                : 38;
            float configuredLength = Math.Max(
                8f,
                (float)(ropeLength - iconSize));
            if (configuredSegments != _segmentCount ||
                _positions.Length == 0 ||
                MathF.Abs(
                    configuredLength /
                    configuredSegments -
                    _segmentLength) > 0.001f)
            {
                ApplySettings(settings);
            }

            if (!float.IsFinite(anchor.X) ||
                !float.IsFinite(anchor.Y))
            {
                anchor = _anchored ? _lastAnchor : Vector2.Zero;
            }

            if (HasInvalidPoint())
            {
                _log?.Invoke("RopeSimulator: invalid point detected; resetting");
                ResetTo(anchor);
            }
            else if (!_anchored)
            {
                ResetTo(anchor);
            }
            else
            {
                if (!double.IsFinite(deltaSeconds) ||
                    deltaSeconds <= 0)
                {
                    ClampToAnchor(anchor);
                    _lastAnchor = anchor;
                    return;
                }

                double boundedDelta =
                    Math.Min(deltaSeconds, 1.0 / 30.0);
                const double maximumStep = 1.0 / 240.0;
                int steps = Math.Clamp(
                    (int)Math.Ceiling(boundedDelta / maximumStep),
                    1,
                    16);
                double stepDelta = boundedDelta / steps;
                Vector2 anchorStart = _lastAnchor;

                for (int step = 0; step < steps; step++)
                {
                    float amount = (step + 1) / (float)steps;
                    Vector2 interpolatedAnchor =
                        anchorStart + (anchor - anchorStart) * amount;
                    IntegrateStep(stepDelta, interpolatedAnchor);
                }
            }

            ClampToAnchor(anchor);
            if (HasInvalidPoint())
            {
                _log?.Invoke(
                    "RopeSimulator: invalid result detected; resetting");
                ResetTo(anchor);
            }

            _lastAnchor = anchor;
        }

        public void Reset(Vector2 anchor, AppSettings settings)
        {
            ApplySettings(settings);
            if (!float.IsFinite(anchor.X) ||
                !float.IsFinite(anchor.Y))
            {
                anchor = Vector2.Zero;
            }

            ResetTo(anchor);
        }

        private void ClampToAnchor(Vector2 anchor)
        {
            _positions[0] = anchor;
            _previous[0] = anchor;
            float ropeLength = _segmentLength * _segmentCount;
            float maximumBobDistance = Math.Max(_segmentLength, ropeLength);

            for (int i = 1; i <= _segmentCount; i++)
            {
                float maximumDistance = i == _segmentCount
                    ? maximumBobDistance
                    : i * _segmentLength * 1.15f;
                Vector2 relative = _positions[i] - anchor;
                float distance = relative.Length();
                if (distance <= maximumDistance)
                {
                    continue;
                }

                Vector2 direction = distance < 1e-4f
                    ? new Vector2(0, 1)
                    : relative / distance;
                _positions[i] = anchor + direction * maximumDistance;
                _previous[i] = _positions[i];
            }
        }

        private void ResetTo(Vector2 anchor)
        {
            for (int i = 0; i <= _segmentCount; i++)
            {
                _positions[i] = anchor + new Vector2(0, i * _segmentLength);
                _previous[i] = _positions[i];
            }

            _lastAnchor = anchor;
            _anchored = true;
        }

        private void IntegrateStep(double deltaSeconds, Vector2 anchor)
        {
            float gravityStep = _gravity * (float)(deltaSeconds * deltaSeconds);
            float dampingStep = MathF.Pow(
                _damping,
                (float)(deltaSeconds * 60f));
            float maximumVelocity = _segmentLength * 1.5f;

            for (int i = 1; i <= _segmentCount; i++)
            {
                Vector2 velocity =
                    (_positions[i] - _previous[i]) * dampingStep;
                float length = velocity.Length();
                if (length > maximumVelocity)
                {
                    velocity *= maximumVelocity / length;
                }

                _previous[i] = _positions[i];
                _positions[i] += velocity + new Vector2(0, gravityStep);
            }

            _positions[0] = anchor;
            _previous[0] = anchor;

            int iterations = 16 + (int)(_stiffness * 32);
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                _positions[0] = anchor;
                for (int i = 0; i < _segmentCount; i++)
                {
                    Vector2 first = _positions[i];
                    Vector2 second = _positions[i + 1];
                    Vector2 delta = second - first;
                    float length = Math.Max(delta.Length(), 1e-4f);
                    float difference =
                        (length - _segmentLength) / length;
                    Vector2 offset = delta * (0.5f * difference);

                    if (i == 0)
                    {
                        _positions[i + 1] -= offset * 2f;
                    }
                    else
                    {
                        _positions[i] += offset;
                        _positions[i + 1] -= offset;
                    }
                }
            }

            float maximumPostConstraintVelocity = _segmentLength;
            for (int i = 1; i <= _segmentCount; i++)
            {
                Vector2 velocity = _positions[i] - _previous[i];
                float length = velocity.Length();
                if (length > maximumPostConstraintVelocity)
                {
                    _previous[i] = _positions[i] -
                        velocity * (maximumPostConstraintVelocity / length);
                }
            }
        }

        private bool HasInvalidPoint()
        {
            foreach (Vector2 point in _positions)
            {
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
                {
                    return true;
                }
            }

            return false;
        }

        public Vector2 Bob =>
            _positions.Length > 0
                ? _positions[_segmentCount]
                : Vector2.Zero;

        public Vector2[] Points => _positions;

        public int Count => _positions.Length;
    }
}
