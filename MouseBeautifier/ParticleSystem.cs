using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using MouseBeautifier.Core;
using Windows.UI;

namespace MouseBeautifier
{
    /// <summary>Win2D projection of rendering-independent particle state.</summary>
    internal sealed class ParticleRenderer
    {
        private static readonly Color[] ConfettiPalette =
        {
            Color.FromArgb(255, 255, 99, 132),
            Color.FromArgb(255, 54, 215, 232),
            Color.FromArgb(255, 255, 205, 86),
            Color.FromArgb(255, 132, 255, 99),
            Color.FromArgb(255, 199, 99, 255),
            Color.FromArgb(255, 99, 179, 255),
        };

        public void Render(
            CanvasDrawingSession session,
            ParticleSimulation simulation,
            AppSettings settings)
        {
            Color baseColor = ColorsUtil.Parse(settings.ClickColor);

            foreach (RippleState ripple in simulation.Ripples)
            {
                float alpha = Math.Clamp(
                    ripple.Life / ripple.MaximumLife,
                    0,
                    1);
                Color color = baseColor;
                Color aura = color;
                aura.A = (byte)(alpha * 42);
                session.DrawCircle(
                    ripple.Position.X,
                    ripple.Position.Y,
                    ripple.Radius,
                    aura,
                    ripple.Width * alpha * 3.2f + 1.5f);
                color.A = (byte)(alpha * 205);
                session.DrawCircle(
                    ripple.Position.X,
                    ripple.Position.Y,
                    ripple.Radius,
                    color,
                    ripple.Width * alpha + 0.5f);
            }

            foreach (ParticleState particle in simulation.Particles)
            {
                float alpha = Math.Clamp(
                    particle.Life / particle.MaximumLife,
                    0,
                    1);
                Color color = particle.Kind ==
                    ParticleVisualKind.Confetti
                    ? ConfettiPalette[
                        Math.Clamp(
                            particle.PaletteIndex,
                            0,
                            ConfettiPalette.Length - 1)]
                    : baseColor;
                color.A = (byte)(alpha * 255);

                if (particle.Kind == ParticleVisualKind.Confetti)
                {
                    Matrix3x2 saved = session.Transform;
                    try
                    {
                        session.Transform = Matrix3x2.CreateRotation(
                            particle.RotationDegrees *
                                (float)Math.PI / 180,
                            particle.Position) *
                            saved;
                        session.FillRectangle(
                            particle.Position.X - particle.Size,
                            particle.Position.Y - particle.Size,
                            particle.Size * 2,
                            particle.Size * 2,
                            color);
                    }
                    finally
                    {
                        session.Transform = saved;
                    }
                }
                else
                {
                    Color halo = color;
                    halo.A = (byte)(alpha * 54);
                    session.FillCircle(
                        particle.Position.X,
                        particle.Position.Y,
                        particle.Size * 2.8f,
                        halo);

                    session.FillCircle(
                        particle.Position.X,
                        particle.Position.Y,
                        particle.Size,
                        color);

                    float angle =
                        particle.RotationDegrees *
                        (float)Math.PI / 180;
                    Vector2 axis = new(
                        MathF.Cos(angle),
                        MathF.Sin(angle));
                    Vector2 normal = new(-axis.Y, axis.X);
                    float rayLength =
                        particle.Size * (1.4f + alpha);
                    Color ray = color;
                    ray.A = (byte)(alpha * 145);
                    session.DrawLine(
                        particle.Position - axis * rayLength,
                        particle.Position + axis * rayLength,
                        ray,
                        Math.Max(0.55f, particle.Size * 0.22f));
                    session.DrawLine(
                        particle.Position - normal * rayLength * 0.62f,
                        particle.Position + normal * rayLength * 0.62f,
                        ray,
                        Math.Max(0.5f, particle.Size * 0.16f));

                    Color core = Color.FromArgb(
                        (byte)(alpha * 235),
                        255,
                        255,
                        255);
                    session.FillCircle(
                        particle.Position,
                        Math.Max(0.55f, particle.Size * 0.34f),
                        core);
                }
            }
        }
    }
}
