using System;
using System.Numerics;

namespace MouseBeautifier
{
    /// <summary>
    /// Verlet-integrated rope. The top point is pinned to the cursor; the rest hang
    /// under gravity and swing according to the cursor's velocity / acceleration.
    ///
    /// Stability technique: when the anchor (cursor) moves, the ENTIRE rope is
    /// translated by the same delta BEFORE integration. Without this, a fast cursor
    /// movement leaves the rope far behind the anchor, and the constraint solver
    /// yanks each point back — but updates only _pos, not _prev. The next frame's
    /// Verlet velocity = _pos - _prev then contains that huge correction, launching
    /// the points past the anchor (the "bob flies off" bug). Translating the whole
    /// rope keeps its shape relative to the cursor constant, so corrections stay
    /// small and velocities stay sane.
    /// </summary>
    public sealed class RopeSimulator
    {
        private Vector2[] _pos = Array.Empty<Vector2>();
        private Vector2[] _prev = Array.Empty<Vector2>();
        private int _n; // number of segments => _n+1 points
        private float _segLen;
        private float _gravity;
        private float _damping;
        private float _stiffness;
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
                _pos = new Vector2[_n + 1];
                _prev = new Vector2[_n + 1];
                for (int i = 0; i <= _n; i++)
                {
                    _pos[i] = new Vector2(0, i * _segLen);
                    _prev[i] = _pos[i];
                }
                _anchored = false;
            }
        }

        public void Update(double dt, Vector2 anchor, AppSettings s)
        {
            if (s.RopeSegments != _n || _pos.Length == 0)
                ApplySettings(s);

            // First-time init: snap the rope to the anchor so there's no huge
            // initial delta that would launch it.
            if (!_anchored)
            {
                _lastAnchor = anchor;
                _anchored = true;
            }

            // Translate the entire rope by the anchor's movement since last frame.
            // This is the KEY fix: it keeps the rope's shape relative to the cursor
            // constant, so the constraint solver never has to close a large gap.
            Vector2 delta = anchor - _lastAnchor;
            if (delta.X != 0 || delta.Y != 0)
            {
                for (int i = 0; i <= _n; i++)
                {
                    _pos[i] += delta;
                    _prev[i] += delta;
                }
            }
            _lastAnchor = anchor;

            // Sub-step the integration for stability under frame-rate variation.
            const double maxStep = 1.0 / 120.0;
            int substeps = Math.Max(1, (int)Math.Ceiling(dt / maxStep));
            substeps = Math.Min(substeps, 8); // cap to avoid perf cliff
            double h = dt / substeps;

            for (int step = 0; step < substeps; step++)
                IntegrateStep(h, anchor);
        }

        private void IntegrateStep(double h, Vector2 anchor)
        {
            // Pin anchor (top of rope = cursor).
            _pos[0] = anchor;
            _prev[0] = anchor;

            float g = _gravity * (float)(h * h);
            float damp = MathF.Pow(_damping, (float)(h * 60f));

            for (int i = 1; i <= _n; i++)
            {
                var cur = _pos[i];
                var vel = (_pos[i] - _prev[i]) * damp;
                var next = cur + vel + new Vector2(0, g);
                _prev[i] = cur;
                _pos[i] = next;
            }

            // Distance constraints (iterate; more iterations => stiffer rope).
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
                        // anchor fixed -> move only the child
                        _pos[i + 1] -= offset * 2f;
                    }
                    else
                    {
                        _pos[i] += offset;
                        _pos[i + 1] -= offset;
                    }
                }
                _pos[0] = anchor;
            }

            // Safety clamp: never let any point wander more than the rope's total
            // length from the anchor. Belt-and-suspenders in case a pathological
            // sequence of frames outran the solver.
            float maxDist = _n * _segLen * 1.1f;
            for (int i = 1; i <= _n; i++)
            {
                var rel = _pos[i] - anchor;
                float d = rel.Length();
                if (d > maxDist)
                {
                    _pos[i] = anchor + rel * (maxDist / d);
                    _prev[i] = _pos[i];
                }
            }
        }

        public Vector2 Bob => _pos.Length > 0 ? _pos[_n] : Vector2.Zero;
        public Vector2[] Points => _pos;
        public int Count => _pos.Length;
    }
}
