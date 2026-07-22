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
            EnableOrbit = true,
            OrbitCount = 59,
            OrbitRadius = 199,
            OrbitSpeed = -359,
            OrbitSize = 39,
            OrbitStrokeWidth = 7.5,
            OrbitColor = "#11121314",
            StartWithWindows = true,
        };

        actual.Reset();

        foreach (var property in typeof(AppSettings).GetProperties())
        {
            Assert.Equal(
                property.GetValue(expected),
                property.GetValue(actual));
        }
    }
}
