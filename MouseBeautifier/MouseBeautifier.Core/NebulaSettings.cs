using System;
using System.Globalization;

namespace MouseBeautifier.Core
{
    /// <summary>
    /// User-editable settings for the nebula effect that orbits the cursor.
    /// Colors are stored as #RRGGBB; opacity belongs to the visual layer rather
    /// than being hidden in a color alpha channel. Each orbiting star-dust
    /// particle is rendered as three concentric layers: a solid center dot, an
    /// optional stroke ring around that dot, and a soft outer glow (halo).
    /// </summary>
    public sealed class NebulaSettings
    {
        public const int MinParticleCount = 12;
        public const int MaxParticleCount = 120;
        public const double MinRadius = 10;
        public const double MaxRadius = 260;
        public const double MinAngularSpeed = -360;
        public const double MaxAngularSpeed = 360;
        public const double MinStarSize = 0.5;
        public const double MaxStarSize = 12;
        public const double MinStrokeWidth = 0;
        public const double MaxStrokeWidth = 8;
        public const double MinHaloSize = 0;
        public const double MaxHaloSize = 6;

        public bool Enabled { get; set; }
        public int ParticleCount { get; set; } = 56;
        public double Radius { get; set; } = 88;
        public double AngularSpeed { get; set; } = 26;

        public double StarSize { get; set; } = 2.8;

        public string ParticleColor { get; set; } = "#A786FF";
        public double ParticleOpacity { get; set; } = 0.92;
        public string CloudColor { get; set; } = "#6C5CE7";
        public double CloudOpacity { get; set; } = 0.12;
        public double TrailOpacity { get; set; } = 0.2;

        // The stroke is a ring drawn on the rim of the center dot, sitting
        // between the solid body and the outer halo (center -> stroke -> halo).
        public string StrokeColor { get; set; } = "#E8DCFF";
        public double StrokeWidth { get; set; } = 1;
        public double StrokeOpacity { get; set; } = 0.85;

        // The halo is the soft outer glow around each star-dust particle.
        public string HaloColor { get; set; } = "#C9B6FF";
        public double HaloOpacity { get; set; } = 0.6;
        public double HaloSize { get; set; } = 2;

        public void Normalize()
        {
            ParticleCount = Math.Clamp(
                ParticleCount,
                MinParticleCount,
                MaxParticleCount);
            Radius = ClampFinite(Radius, MinRadius, MaxRadius, 88);
            AngularSpeed = ClampFinite(
                AngularSpeed,
                MinAngularSpeed,
                MaxAngularSpeed,
                26);
            StarSize = ClampFinite(
                StarSize,
                MinStarSize,
                MaxStarSize,
                2.8);

            ParticleColor = HexColor.NormalizeRgb(
                ParticleColor,
                "#A786FF");
            ParticleOpacity = NormalizeOpacity(ParticleOpacity, 0.92);
            CloudColor = HexColor.NormalizeRgb(CloudColor, "#6C5CE7");
            CloudOpacity = NormalizeOpacity(CloudOpacity, 0.12);
            TrailOpacity = NormalizeOpacity(TrailOpacity, 0.2);

            StrokeColor = HexColor.NormalizeRgb(StrokeColor, "#E8DCFF");
            StrokeWidth = ClampFinite(
                StrokeWidth,
                MinStrokeWidth,
                MaxStrokeWidth,
                1);
            StrokeOpacity = NormalizeOpacity(StrokeOpacity, 0.85);

            HaloColor = HexColor.NormalizeRgb(HaloColor, "#C9B6FF");
            HaloOpacity = NormalizeOpacity(HaloOpacity, 0.6);
            HaloSize = ClampFinite(
                HaloSize,
                MinHaloSize,
                MaxHaloSize,
                2);
        }

        public NebulaRenderSettings ToRenderSettings()
        {
            // Load/save and UI commit paths normalize the mutable model. Keep
            // frame snapshot creation allocation-free by not rewriting strings.
            return new NebulaRenderSettings(
                Enabled,
                ParticleCount,
                (float)Radius,
                (float)AngularSpeed,
                (float)StarSize,
                ParticleColor,
                (float)ParticleOpacity,
                CloudColor,
                (float)CloudOpacity,
                (float)TrailOpacity,
                StrokeColor,
                (float)StrokeWidth,
                (float)StrokeOpacity,
                HaloColor,
                (float)HaloOpacity,
                (float)HaloSize);
        }

        private static double NormalizeOpacity(
            double value,
            double fallback)
        {
            return ClampFinite(value, 0, 1, fallback);
        }

        private static double ClampFinite(
            double value,
            double minimum,
            double maximum,
            double fallback)
        {
            return double.IsFinite(value)
                ? Math.Clamp(value, minimum, maximum)
                : fallback;
        }
    }

    /// <summary>
    /// Immutable, validated input shared by layout and rendering.
    /// </summary>
    public readonly record struct NebulaRenderSettings(
        bool Enabled,
        int ParticleCount,
        float Radius,
        float AngularSpeed,
        float StarSize,
        string ParticleColor,
        float ParticleOpacity,
        string CloudColor,
        float CloudOpacity,
        float TrailOpacity,
        string StrokeColor,
        float StrokeWidth,
        float StrokeOpacity,
        string HaloColor,
        float HaloOpacity,
        float HaloSize);

    public static class HexColor
    {
        public static string NormalizeRgb(
            string? value,
            string fallback)
        {
            return TryParse(value, out _, out byte r, out byte g, out byte b)
                ? $"#{r:X2}{g:X2}{b:X2}"
                : NormalizeFallback(fallback);
        }

        public static double ReadAlpha(string? value)
        {
            return TryParse(value, out byte a, out _, out _, out _)
                ? a / 255d
                : 1;
        }

        private static string NormalizeFallback(string fallback)
        {
            return TryParse(
                fallback,
                out _,
                out byte r,
                out byte g,
                out byte b)
                ? $"#{r:X2}{g:X2}{b:X2}"
                : "#FFFFFF";
        }

        private static bool TryParse(
            string? value,
            out byte alpha,
            out byte red,
            out byte green,
            out byte blue)
        {
            alpha = 255;
            red = 0;
            green = 0;
            blue = 0;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string hex = value.Trim().TrimStart('#');
            if (hex.Length == 6)
            {
                hex = "FF" + hex;
            }

            if (hex.Length != 8 ||
                !uint.TryParse(
                    hex,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out uint argb))
            {
                return false;
            }

            alpha = (byte)(argb >> 24);
            red = (byte)(argb >> 16);
            green = (byte)(argb >> 8);
            blue = (byte)argb;
            return true;
        }
    }
}
