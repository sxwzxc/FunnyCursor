using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace MouseBeautifier
{
    public sealed class Trail
    {
        private readonly List<Vector2> _pts = new();
        private readonly List<float> _ages = new();
        private float _maxAge = 0.5f;

        public void Push(Vector2 p, double dt, AppSettings s)
        {
            _maxAge = (float)Math.Max(0.05, s.TrailLength);
            _pts.Add(p);
            _ages.Add(0f);
            for (int i = 0; i < _ages.Count; i++) _ages[i] += (float)dt;
            for (int i = _pts.Count - 1; i >= 0; i--)
            {
                if (_ages[i] > _maxAge) { _pts.RemoveAt(i); _ages.RemoveAt(i); }
            }
        }

        public void Clear()
        {
            _pts.Clear();
            _ages.Clear();
        }

        public void Render(CanvasDrawingSession session, Color color, float width)
        {
            if (_pts.Count < 2) return;
            for (int i = 1; i < _pts.Count; i++)
            {
                float t = 1 - _ages[i] / _maxAge; // 1 = fresh
                var c = color; c.A = (byte)(t * 230);
                float w = width * t;
                if (w < 0.5f) w = 0.5f;
                session.DrawLine(_pts[i - 1].X, _pts[i - 1].Y, _pts[i].X, _pts[i].Y, c, w);
            }
        }
    }
}
