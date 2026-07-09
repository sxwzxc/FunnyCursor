using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace MouseBeautifier
{
    internal struct Particle
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public float Life;
        public float MaxLife;
        public float Size;
        public Color Color;
        public float Rot;
        public float RotVel;
        public string Kind;
    }

    internal struct Ripple
    {
        public Vector2 Pos;
        public float Radius;
        public float MaxRadius;
        public float Life;
        public float MaxLife;
        public Color Color;
        public float Width;
    }

    public sealed class ParticleSystem
    {
        private readonly List<Particle> _parts = new();
        private readonly List<Ripple> _ripples = new();
        private readonly Random _rnd = new();

        private static readonly Color[] ConfettiPalette =
        {
            Color.FromArgb(255, 255, 99, 132), Color.FromArgb(255, 54, 215, 232),
            Color.FromArgb(255, 255, 205, 86), Color.FromArgb(255, 132, 255, 99),
            Color.FromArgb(255, 199, 99, 255), Color.FromArgb(255, 99, 179, 255),
        };

        public void Spawn(Vector2 at, AppSettings s)
        {
            var baseColor = ColorsUtil.Parse(s.ClickColor);
            int n = Math.Clamp(s.ClickParticleCount, 1, 600);

            switch (s.ClickPreset)
            {
                case "ring":
                case "ripple":
                    AddRipple(at, baseColor, (float)(s.ClickSpeed * 0.6 + 40));
                    if (s.ClickPreset == "ripple")
                        AddRipple(at, baseColor, (float)(s.ClickSpeed * 0.9 + 60));
                    break;

                case "confetti":
                    for (int i = 0; i < n; i++)
                        AddParticle(at, ConfettiPalette[_rnd.Next(ConfettiPalette.Length)],
                            s, (float)_rnd.NextDouble() * 360, (float)(_rnd.NextDouble() * 600 - 300));
                    break;

                case "sparkle":
                default:
                    for (int i = 0; i < n; i++)
                        AddParticle(at, baseColor, s, 0, 0);
                    break;
            }
        }

        private void AddParticle(Vector2 at, Color color, AppSettings s, float rot, float rotVel)
        {
            double ang = _rnd.NextDouble() * Math.PI * 2;
            double spd = s.ClickSpeed * (0.25 + _rnd.NextDouble() * 0.85);
            _parts.Add(new Particle
            {
                Pos = at,
                Vel = new Vector2((float)(Math.Cos(ang) * spd), (float)(Math.Sin(ang) * spd)),
                MaxLife = 0.5f + (float)_rnd.NextDouble() * 0.9f,
                Size = 1.5f + (float)_rnd.NextDouble() * 4f,
                Color = color,
                Rot = rot,
                RotVel = rotVel,
                Kind = "p",
            });
            if (_parts.Count > 4000) _parts.RemoveRange(0, _parts.Count - 4000);
        }

        private void AddRipple(Vector2 at, Color color, float maxR)
        {
            _ripples.Add(new Ripple
            {
                Pos = at,
                Radius = 4,
                MaxRadius = maxR,
                MaxLife = 0.6f,
                Life = 0.6f,
                Color = color,
                Width = 3,
            });
        }

        public void Update(double dt, AppSettings s)
        {
            float g = (float)(s.ClickGravity * dt);
            float drag = (float)Math.Pow(0.96, dt * 60);

            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                var p = _parts[i];
                p.Vel.Y += g;
                p.Vel *= drag;
                p.Pos += p.Vel * (float)dt;
                p.Rot += p.RotVel * (float)dt;
                p.Life -= (float)dt;
                _parts[i] = p;
                if (p.Life <= 0) _parts.RemoveAt(i);
            }

            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                var r = _ripples[i];
                float t = 1 - r.Life / r.MaxLife;
                r.Radius = 4 + t * (r.MaxRadius - 4);
                r.Life -= (float)dt;
                _ripples[i] = r;
                if (r.Life <= 0) _ripples.RemoveAt(i);
            }
        }

        public void Render(CanvasDrawingSession session)
        {
            foreach (var r in _ripples)
            {
                float a = Math.Clamp(r.Life / r.MaxLife, 0, 1);
                var c = r.Color; c.A = (byte)(a * 220);
                session.DrawCircle(r.Pos.X, r.Pos.Y, r.Radius, c, r.Width * a + 0.5f);
            }

            foreach (var p in _parts)
            {
                float a = Math.Clamp(p.Life / p.MaxLife, 0, 1);
                var c = p.Color; c.A = (byte)(a * 255);

                if (p.RotVel != 0)
                {
                    var saved = session.Transform;
                    session.Transform = Matrix3x2.CreateRotation(p.Rot * (float)Math.PI / 180f, p.Pos);
                    session.FillRectangle(p.Pos.X - p.Size, p.Pos.Y - p.Size, p.Size * 2, p.Size * 2, c);
                    session.Transform = saved;
                }
                else
                {
                    // soft glow for sparkle
                    session.FillCircle(p.Pos.X, p.Pos.Y, p.Size, c);
                    var halo = c; halo.A = (byte)(a * 70);
                    session.FillCircle(p.Pos.X, p.Pos.Y, p.Size * 2.2f, halo);
                }
            }
        }

        public void Clear() { _parts.Clear(); _ripples.Clear(); }
    }
}
