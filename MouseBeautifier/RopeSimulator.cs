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

            // Pin anchor (top of rope = cursor).
            _pos[0] = anchor;
            _prev[0] = anchor;

            float g = _gravity * (float)(dt * dt);
            float damp = MathF.Pow(_damping, (float)(dt * 60f));

            for (int i = 1; i <= _n; i++)
            {
                var cur = _pos[i];
                var vel = (_pos[i] - _prev[i]) * damp;
                var next = cur + vel + new Vector2(0, g);
                _prev[i] = cur;
                _pos[i] = next;
            }

            // Distance constraints (iterate; more iterations => stiffer rope).
            int iters = 6 + (int)(_stiffness * 24);
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
        }

        public Vector2 Bob => _pos.Length > 0 ? _pos[_n] : Vector2.Zero;
        public Vector2[] Points => _pos;
        public int Count => _pos.Length;
    }
}
