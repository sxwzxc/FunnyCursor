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
                color.A = (byte)(alpha * 220);
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
                    session.Transform = Matrix3x2.CreateRotation(
                        particle.RotationDegrees *
                            (float)Math.PI / 180,
                        particle.Position);
                    session.FillRectangle(
                        particle.Position.X - particle.Size,
                        particle.Position.Y - particle.Size,
                        particle.Size * 2,
                        particle.Size * 2,
                        color);
                    session.Transform = saved;
                }
                else
                {
                    session.FillCircle(
                        particle.Position.X,
                        particle.Position.Y,
                        particle.Size,
                        color);
                    Color halo = color;
                    halo.A = (byte)(alpha * 70);
                    session.FillCircle(
                        particle.Position.X,
                        particle.Position.Y,
                        particle.Size * 2.2f,
                        halo);
                }
            }
        }
    }
}
