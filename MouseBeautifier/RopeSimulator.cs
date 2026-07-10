using System;
using System.Numerics;

namespace MouseBeautifier
{
    /// <summary>
    /// Verlet-integrated rope. Point 0 is pinned to the cursor; the rest hang
    /// under gravity and swing naturally according to the cursor's motion.
    ///
    /// Stability strategy (without killing physics):
    ///   1. Anchor interpolation — across substeps the anchor moves in small
    ///      increments, so constraint corrections stay small and never inject
    ///      huge velocities.
    ///   2. Per-step velocity clamp — caps the Verlet velocity to a fraction of
    ///      the segment length, so even if a constraint correction slips through,
    ///      it can never launch a point across the screen.
    ///   3. Max-distance safety net — hard clamps any point beyond the rope's
    ///      total length back toward the anchor.
    ///
    /// NOTE: The previous version translated the ENTIRE rope by the anchor delta
    /// each frame. That prevented the "fly off" bug but also made the rope rigidly
    /// follow the cursor with zero swing — it looked like a dead straight line.
    /// The approach below keeps stability AND restores pendulum-like swing physics.
    /// </summary>
    public sealed class RopeSimulator
    {
        private Vector2[] _pos = Array.Empty<Vector2>();
        private Vector2[] _prev = Array.Empty<Vector2>();
        private int _n;          // segment count => _n+1 points
        private float _segLen;
        private float _gravity;
        private float _damping;  // 0.5..0.999 (higher = less air drag)
        private float _stiffness;// 0..1 (higher = more constraint iterations)
        private Vector2 _lastAnchor;
        private bool _anchored;

        public void ApplySettings(AppSettings s)
        {
            int segs = Math.Clamp(s.RopeSegments, 2, 120);
            _segLen = (float)(s.RopeLength / segs);
            _gravity = (float)s.RopeGravity;
            _damping = (float)Math.Clamp(s.RopeDamping, 0.5, 0.999);
            _stiffness = (float)Math.Clamp(s.RopeStiffness, 0, 1);

            if (segs != _n || _pos.Length == 0)
            {
                _n = segs;
                _pos  = new Vector2[_n + 1];
                _prev = new Vector2[_n + 1];
                // Place rope hanging straight down from origin; repositioned to
                // the real anchor on the first Update call.
                for (int i = 0; i <= _n; i++)
                {
                    _pos[i]  = new Vector2(0, i * _segLen);
                    _prev[i] = _pos[i];
                }
                _anchored = false;
            }
        }

        public void Update(double dt, Vector2 anchor, AppSettings s)
        {
            if (s.RopeSegments != _n || _pos.Length == 0)
                ApplySettings(s);

            // First-time init: snap the rope to hang straight down from the anchor.
            if (!_anchored)
            {
                for (int i = 0; i <= _n; i++)
                {
                    _pos[i]  = anchor + new Vector2(0, i * _segLen);
                    _prev[i] = _pos[i];
                }
                _lastAnchor = anchor;
                _anchored = true;
                return; // skip the first frame so velocities start at zero
            }

            // Clamp dt to avoid pathological steps after a stall / debugger pause.
            double h = Math.Min(dt, 1.0 / 30.0);

            // Sub-step for stability. The anchor is interpolated across substeps
            // so no single step ever sees a large anchor jump — this is what
            // keeps constraint corrections (and therefore induced velocities) small.
            const double maxStep = 1.0 / 240.0;
            int substeps = Math.Max(1, (int)Math.Ceiling(h / maxStep));
            substeps = Math.Min(substeps, 16);
            double sub = h / substeps;

            Vector2 anchorStart = _lastAnchor;
            for (int step = 0; step < substeps; step++)
            {
                float t = (step + 1) / (float)substeps;
                Vector2 interpAnchor = anchorStart + (anchor - anchorStart) * t;
                IntegrateStep(sub, interpAnchor);
            }
            _lastAnchor = anchor;
        }

        private void IntegrateStep(double h, Vector2 anchor)
        {
            float g = _gravity * (float)(h * h);
            float damp = MathF.Pow(_damping, (float)(h * 60f));

            // Max velocity per step = 1.5 * segment length. This prevents the
            // "fly off" explosion without killing natural swing motion.
            float maxVel = _segLen * 1.5f;

            // Verlet integration for free points (1..n). Point 0 is pinned.
            for (int i = 1; i <= _n; i++)
            {
                var vel = (_pos[i] - _prev[i]) * damp;
                float vl = vel.Length();
                if (vl > maxVel)
                    vel *= maxVel / vl;
                _prev[i] = _pos[i];
                _pos[i]  = _pos[i] + vel + new Vector2(0, g);
            }

            // Pin the anchor (top of rope = cursor).
            _pos[0]  = anchor;
            _prev[0] = anchor;

            // Distance constraints — more iterations => stiffer rope.
            int iters = 16 + (int)(_stiffness * 32);
            for (int k = 0; k < iters; k++)
            {
                _pos[0] = anchor;
                for (int i = 0; i < _n; i++)
                {
                    var a = _pos[i];
                    var b = _pos[i + 1];
                    var d = b - a;
                    float len = d.Length();
                    if (len < 1e-4f) len = 1e-4f;
                    float diff = (len - _segLen) / len;
                    var offset = d * (0.5f * diff);
                    if (i == 0)
                    {
                        // anchor is fixed -> move only the child point
                        _pos[i + 1] -= offset * 2f;
                    }
                    else
                    {
                        _pos[i]     += offset;
                        _pos[i + 1] -= offset;
                    }
                }
                _pos[0] = anchor;
            }

            // Safety clamp: never let any point wander beyond the rope's total length.
            float maxDist = _n * _segLen * 1.1f;
            for (int i = 1; i <= _n; i++)
            {
                var rel = _pos[i] - anchor;
                float d = rel.Length();
                if (d > maxDist)
                {
                    _pos[i]  = anchor + rel * (maxDist / d);
                    _prev[i] = _pos[i];
                }
            }
        }

        public Vector2 Bob => _pos.Length > 0 ? _pos[_n] : Vector2.Zero;
        public Vector2[] Points => _pos;
        public int Count => _pos.Length;
    }
}
