using MouseBeautifier.Core;
using Xunit;

namespace MouseBeautifier.Core.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void ResetRestoresEveryPublicSetting()
    {
        AppSettings expected = new();
        AppSettings actual = new()
        {
            EnableClickEffects = false,
            ClickPreset = "ripple",
            ClickColor = "#00000000",
            ClickParticleCount = 199,
            ClickSpeed = 1234,
            ClickGravity = 2222,
            EnableRope = false,
            RopeLength = 499,
            RopeSegments = 39,
            RopeGravity = 2999,
            RopeDamping = 0.55,
            RopeStiffness = 0.95,
            IconType = "custom",
            CustomIconPath = "custom.png",
            IconSize = 119,
            IconColor = "#01020304",
            RopeColor = "#05060708",
            RopeWidth = 19,
            RopeStyle = "pulse",
            EnableTrail = false,
            TrailColor = "#090A0B0C",
            TrailLength = 1.9,
            TrailWidth = 39,
            EnableGlow = false,
            GlowColor = "#0D0E0F10",
            GlowSize = 199,
            GlowIntensity = 0.99,
            Nebula = new NebulaSettings
            {
                Enabled = true,
                ParticleCount = 59,
                Radius = 199,
                AngularSpeed = -359,
                StarSize = 11,
                ParticleColor = "#111314",
                ParticleOpacity = 0.13,
                CloudColor = "#151617",
                CloudOpacity = 0.14,
                TrailOpacity = 0.15,
                StrokeColor = "#191A1B",
                StrokeWidth = 3.5,
                StrokeOpacity = 0.17,
                HaloColor = "#161718",
                HaloOpacity = 0.19,
                HaloSize = 2.75,
            },
            StartWithWindows = true,
        };

        actual.Reset();

        Assert.Equal(
            AppSettingsJson.Serialize(expected, writeIndented: false),
            AppSettingsJson.Serialize(actual, writeIndented: false));
    }
}
