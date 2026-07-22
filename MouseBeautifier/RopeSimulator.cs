using System;
using System.Numerics;
using MouseBeautifier.Core;

namespace MouseBeautifier
{
    /// <summary>
    /// Verlet-integrated rope. Point 0 is pinned to the cursor; point N (the
    /// "bob") is where the pendant (star/triangle/etc) is welded.
    ///
    /// Stability layers (in execution order per substep):
    ///   1. Pre-integration velocity clamp — caps Verlet velocity to prevent
    ///      inertia from previous frames accumulating into an explosion.
    ///   2. Distance constraints — classic FABRIK-style relaxation keeps each
    ///      segment at segLen, so the rope maintains its shape.
    ///   3. Post-constraint velocity clamp — THE KEY FIX: the constraint solver
    ///      moves _pos but NOT _prev, so _pos-_prev would contain the constraint
    ///      correction (a position fix, not real motion). We clamp _prev so the
    ///      induced velocity never exceeds segLen per step.
    ///   4. Non-destructive bob clamp — if the bob somehow exceeds the max-reach
    ///      sphere, we radially clamp ONLY the bob (not the whole chain). The old
    ///      code rebuilt the entire chain into a straight line, which looked like
    ///      the star "snapped off and flew away" — that was the root cause of the
    ///      user's "star flies off" bug.
    ///   5. NaN defense — reset to clean state if any value becomes NaN/Inf.
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
            // Reserve IconSize for the pendant: the rope's natural hanging length
            // is (RopeLength - IconSize), so the star (welded at the bob and
            // extending IconSize further) lands at exactly RopeLength when the
            // rope hangs straight. Previously segLen = RopeLength/segs, so the
            // rope wanted to hang at full RopeLength while the保底 clamp forced
            // the bob back to (RopeLength - IconSize) — that compression made
            // the chain fold and the last segment flip upward (dir.Y=-1),
            // pointing the star back at the cursor. Matching the rope's natural
            // length to the clamp target removes the fold entirely.
            float ropeAvail = Math.Max(8f, (float)(s.RopeLength - s.IconSize));
            _segLen = ropeAvail / segs;
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

            // NaN defense — reset, then fall through to the保底 clamp so even a
            // freshly-reset chain is brought within reach.
            if (HasNaN())
            {
                App.Log("RopeSimulator: NaN detected, resetting");
                ResetTo(anchor);
            }
            else if (!_anchored)
            {
                // First frame after ApplySettings — initialise hanging straight
                // down, then fall through to保底 so the bob is not left at the
                // full RopeLength (which would place the star past the limit).
                ResetTo(anchor);
            }
            else
            {
                double h = Math.Min(dt, 1.0 / 30.0);

                // Sub-step for stability with anchor interpolation.
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
            }

            // ── 保底机制 (safety net) — GUARANTEED on EVERY frame ──
            // This runs after *every* path above (normal integration, NaN
            // reset, first-frame init). Non-destructive radial clamp: each
            // point is clamped independently to its max reach from the anchor,
            // so the chain shape is preserved instead of being rebuilt into a
            // straight line (the old "star snaps off" bug). The bob (point N)
            // is clamped to ropeLen - IconSize, guaranteeing the star's
            // farthest tip stays within RopeLength of the cursor — the user's
            // hard requirement.
            ClampToAnchor(anchor, s);

            _lastAnchor = anchor;
        }

        /// <summary>
        /// 保底 clamp — the single guarantee that the bob (and thus the welded
        /// star) can never be farther from the anchor than RopeLength. Called
        /// on every Update regardless of which integration path was taken, so
        /// even a NaN-recovery reset or a first-frame init cannot leak a bob
        /// placed at the full RopeLength (which would put the star past the
        /// limit).
        /// </summary>
        private void ClampToAnchor(Vector2 anchor, AppSettings s)
        {
            // ropeLen here is the rope's NATURAL reach (= RopeLength - IconSize),
            // because ApplySettings sized each segment to leave room for the
            // pendant. The bob clamped to ropeLen => star far tip ≤ RopeLength.
            float ropeLen = _segLen * _n;
            float maxBob = Math.Max(_segLen, ropeLen);

            for (int i = 1; i <= _n; i++)
            {
                // Point i can be at most i*segLen from the anchor (its chain
                // prefix length); allow 15% stretch tolerance for the solver.
                // The bob is clamped to maxBob (the rope's full reach).
                float maxReach = (i == _n) ? maxBob : i * _segLen * 1.15f;
                Vector2 rel = _pos[i] - anchor;
                float d = rel.Length();
                if (d > maxReach)
                {
                    Vector2 dir = d < 1e-4f ? new Vector2(0, 1) : rel / d;
                    _pos[i] = anchor + dir * maxReach;
                    _prev[i] = _pos[i]; // zero velocity for the clamped point
                }
            }
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
            float damp = MathF.Pow(_damping, (float)(h * 60f));

            // Pre-integration velocity clamp.
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

            // Distance constraints — FABRIK-style relaxation.
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

            // POST-CONSTRAINT VELOCITY CLAMP.
            // The constraint solver moved _pos but not _prev, so _pos-_prev
            // contains the constraint correction (not real motion). Clamp _prev
            // so the induced velocity never exceeds segLen per step.
            float maxPostVel = _segLen;
            for (int i = 1; i <= _n; i++)
            {
                var v = _pos[i] - _prev[i];
                float vl = v.Length();
                if (vl > maxPostVel)
                    _prev[i] = _pos[i] - v * (maxPostVel / vl);
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

        /// <summary>Last rope point — the star is welded here.</summary>
        public Vector2 Bob => _pos.Length > 0 ? _pos[_n] : Vector2.Zero;
        public Vector2[] Points => _pos;
        public int Count => _pos.Length;
    }
}
