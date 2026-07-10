using System;
using System.Numerics;

namespace MouseBeautifier
{
    /// <summary>
    /// Verlet-integrated rope. Point 0 is pinned to the cursor; the rest hang
    /// under gravity and swing naturally according to the cursor's motion.
    ///
    /// Stability strategy (4 layers, in execution order):
    ///   1. Anchor interpolation — across substeps the anchor moves in small
    ///      increments, so constraint corrections stay small.
    ///   2. Pre-integration velocity clamp — caps the Verlet velocity BEFORE
    ///      integration so inertia from previous frames can't explode.
    ///   3. Post-constraint velocity clamp — THE KEY FIX: after the constraint
    ///      solver moves _pos to satisfy distance constraints, it does NOT touch
    ///      _prev. So the next frame's Verlet velocity = _pos - _prev would
    ///      contain the constraint correction (a position fix, not real motion),
    ///      injecting fake velocity that accumulates and eventually launches
    ///      points across the screen. We clamp _prev after constraints so the
    ///      induced velocity never exceeds segLen per step.
    ///   4. Max-distance safety net + NaN defense — hard clamps any point beyond
    ///      the rope's total length, and resets the rope if NaN/Infinity creeps in
    ///      (shouldn't happen, but defense-in-depth prevents a permanently broken
    ///      rope from freezing the overlay).
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
        private Vector2 _cursorVel;
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

            // NaN defense: if any point became NaN (shouldn't, but defense-in-depth),
            // reset the rope to a clean hanging state under the current anchor.
            if (HasNaN())
            {
                App.Log("RopeSimulator: NaN detected, resetting rope");
                ResetTo(anchor);
                return;
            }

            if (!_anchored)
            {
                ResetTo(anchor);
                return;
            }

            double h = Math.Min(dt, 1.0 / 30.0);

            // Sub-step for stability with anchor interpolation across substeps.
            const double maxStep = 1.0 / 240.0;
            int substeps = Math.Max(1, (int)Math.Ceiling(h / maxStep));
            substeps = Math.Min(substeps, 16);
            double sub = h / substeps;

            Vector2 anchorStart = _lastAnchor;
            _cursorVel = dt > 1e-4 ? (anchor - anchorStart) / (float)dt : Vector2.Zero;

            for (int step = 0; step < substeps; step++)
            {
                float t = (step + 1) / (float)substeps;
                Vector2 interpAnchor = anchorStart + (anchor - anchorStart) * t;
                IntegrateStep(sub, interpAnchor);
            }
            _lastAnchor = anchor;
        }

        private void ResetTo(Vector2 anchor)
        {
            for (int i = 0; i <= _n; i++)
            {
                _pos[i]  = anchor + new Vector2(0, i * _segLen);
                _prev[i] = _pos[i];
            }
            _lastAnchor = anchor;
            _anchored = true;
        }

        private void IntegrateStep(double h, Vector2 anchor)
        {
            float g = _gravity * (float)(h * h);

            // Adaptive damping: extra damping when cursor moves fast, to suppress
            // energy injection from rapid cursor motion.
            float cursorSpeed = _cursorVel.Length();
            float extraDamp = MathF.Min(cursorSpeed / 1500f, 0.5f);
            float effectiveDamp = Math.Clamp(_damping - extraDamp, 0.3f, 0.999f);
            float damp = MathF.Pow(effectiveDamp, (float)(h * 60f));

            // Max velocity per step for Verlet integration (pre-integration clamp).
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

            // POST-CONSTRAINT VELOCITY CLAMP — THE KEY FIX.
            // The constraint solver moved _pos to satisfy distance constraints but
            // did not touch _prev. So _pos - _prev now contains the constraint
            // correction (a position fix), which would become next frame's Verlet
            // velocity and accumulate into an explosion. We clamp _prev so the
            // induced velocity never exceeds segLen per step — this kills the
            // "fly off screen" bug while preserving normal pendulum swing (which
            // produces velocities well below segLen per step).
            float maxPostVel = _segLen;
            for (int i = 1; i <= _n; i++)
            {
                var v = _pos[i] - _prev[i];
                float vl = v.Length();
                if (vl > maxPostVel)
                    _prev[i] = _pos[i] - v * (maxPostVel / vl);
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

        private bool HasNaN()
        {
            for (int i = 0; i < _pos.Length; i++)
            {
                if (float.IsNaN(_pos[i].X) || float.IsNaN(_pos[i].Y) ||
                    float.IsInfinity(_pos[i].X) || float.IsInfinity(_pos[i].Y))
                    return true;
            }
            return false;
        }

        public Vector2 Bob => _pos.Length > 0 ? _pos[_n] : Vector2.Zero;
        public Vector2[] Points => _pos;
        public int Count => _pos.Length;
    }
}
