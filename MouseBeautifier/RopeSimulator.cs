using System;
using System.Numerics;

namespace MouseBeautifier
{
    /// <summary>
    /// Verlet-integrated rope. The top point is pinned to the cursor; the rest hang
    /// under gravity and swing according to the cursor's velocity / acceleration.
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
            }
        }

        public void Update(double dt, Vector2 anchor, AppSettings s)
        {
            if (s.RopeSegments != _n || _pos.Length == 0)
                ApplySettings(s);

            // Sub-step the integration so a frame stutter can't launch the rope.
            // Each sub-step is at most ~1/120s; a 50ms frame → 6 sub-steps.
            const double maxStep = 1.0 / 120.0;
            int substeps = Math.Max(1, (int)Math.Ceiling(dt / maxStep));
            substeps = Math.Min(substeps, 8); // cap to avoid perf cliff
            double h = dt / substeps;

            // Interpolate the anchor across sub-steps so the top of the rope
            // follows the cursor smoothly instead of snapping per frame.
            Vector2 startAnchor = _pos[0];

            for (int step = 0; step < substeps; step++)
            {
                float t = (step + 1f) / substeps;
                Vector2 a = Vector2.Lerp(startAnchor, anchor, t);
                IntegrateStep(h, a);
            }
        }

        private void IntegrateStep(double h, Vector2 anchor)
        {
            // Pin anchor (top of rope = cursor).
            _pos[0] = anchor;
            _prev[0] = anchor;

            float g = _gravity * (float)(h * h);
            float damp = MathF.Pow(_damping, (float)(h * 60f));

            // Cap how fast any point may move per step (in px/step). Without this
            // a violent cursor jerk propagates huge velocities down the chain and
            // the constraint solver can't recover, making the bob "fly off".
            const float maxStepLen = 60f;

            for (int i = 1; i <= _n; i++)
            {
                var cur = _pos[i];
                var vel = (_pos[i] - _prev[i]) * damp;

                float vl = vel.Length();
                if (vl > maxStepLen)
                    vel *= maxStepLen / vl;

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
            // length from the anchor.
            float maxDist = _n * _segLen * 1.05f;
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
