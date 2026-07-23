using MouseBeautifier.Core;
using Xunit;

namespace MouseBeautifier.Core.Tests;

public sealed class NebulaSettingsTests
{
    [Fact]
    public void NormalizeUsesOneSharedParameterContract()
    {
        NebulaSettings settings = new()
        {
            ParticleCount = -1,
            Radius = double.NaN,
            AngularSpeed = double.PositiveInfinity,
            StarSize = 99,
            ParticleColor = "#80112233",
            ParticleOpacity = 4,
            CloudColor = "invalid",
            CloudOpacity = -1,
            TrailOpacity = double.NaN,
            StrokeColor = "invalid",
            StrokeWidth = 99,
            StrokeOpacity = -1,
            HaloColor = "invalid",
            HaloOpacity = -2,
            HaloSize = 20,
        };

        settings.Normalize();

        Assert.Equal(NebulaSettings.MinParticleCount, settings.ParticleCount);
        Assert.Equal(88, settings.Radius);
        Assert.Equal(26, settings.AngularSpeed);
        Assert.Equal(NebulaSettings.MaxStarSize, settings.StarSize);
        Assert.Equal("#112233", settings.ParticleColor);
        Assert.Equal(1, settings.ParticleOpacity);
        Assert.Equal("#6C5CE7", settings.CloudColor);
        Assert.Equal(0, settings.CloudOpacity);
        Assert.Equal(0.2, settings.TrailOpacity);
        Assert.Equal("#E8DCFF", settings.StrokeColor);
        Assert.Equal(NebulaSettings.MaxStrokeWidth, settings.StrokeWidth);
        Assert.Equal(0, settings.StrokeOpacity);
        Assert.Equal("#C9B6FF", settings.HaloColor);
        Assert.Equal(0, settings.HaloOpacity);
        Assert.Equal(NebulaSettings.MaxHaloSize, settings.HaloSize);
    }

    [Fact]
    public void LegacyFlatOrbitSettingsMigrateToIndependentLayers()
    {
        const string json = """
            {
              "EnableOrbit": true,
              "OrbitCount": 44,
              "OrbitRadius": 130,
              "OrbitSpeed": -90,
              "OrbitSize": 4,
              "OrbitStrokeWidth": 2,
              "OrbitColor": "#80112233",
              "OrbitHaloIntensity": 0.8,
              "OrbitHaloSize": 1.4,
              "OrbitHaloColor": "#40AABBCC"
            }
            """;

        AppSettings settings = AppSettingsJson.Deserialize(json);
        NebulaSettings nebula = settings.Nebula;

        Assert.True(nebula.Enabled);
        Assert.Equal(44, nebula.ParticleCount);
        Assert.Equal(130, nebula.Radius);
        Assert.Equal(-90, nebula.AngularSpeed);
        Assert.Equal(4, nebula.StarSize);
        Assert.Equal("#112233", nebula.ParticleColor);
        Assert.Equal("#112233", nebula.CloudColor);
        Assert.Equal(2, nebula.StrokeWidth);
        Assert.Equal("#112233", nebula.StrokeColor);
        Assert.Equal((double)0x80 / 255, nebula.StrokeOpacity, precision: 6);
        Assert.Equal(0.8 * 0x40 / 255, nebula.HaloOpacity, precision: 6);
        Assert.Equal("#AABBCC", nebula.HaloColor);
        Assert.Equal(1.4, nebula.HaloSize);

        string migrated = AppSettingsJson.Serialize(settings);
        Assert.Contains("\"SchemaVersion\": 2", migrated);
        Assert.Contains("\"Nebula\"", migrated);
        Assert.DoesNotContain("\"EnableOrbit\"", migrated);
        Assert.DoesNotContain("\"OrbitHaloIntensity\"", migrated);
    }

    [Fact]
    public void StructuredSettingsRoundTripWithoutColorAlpha()
    {
        AppSettings original = new()
        {
            Nebula = new NebulaSettings
            {
                Enabled = true,
                ParticleColor = "#123456",
                ParticleOpacity = 1,
                CloudColor = "#0F1E2D",
                StrokeColor = "#ABCDEF",
                StrokeWidth = 1.5,
                StrokeOpacity = 0.7,
                HaloColor = "#654321",
                HaloOpacity = 0.37,
                HaloSize = 2.5,
            },
        };

        AppSettings roundTripped = AppSettingsJson.Deserialize(
            AppSettingsJson.Serialize(original));

        Assert.Equal("#123456", roundTripped.Nebula.ParticleColor);
        Assert.Equal(1, roundTripped.Nebula.ParticleOpacity);
        Assert.Equal("#0F1E2D", roundTripped.Nebula.CloudColor);
        Assert.Equal("#ABCDEF", roundTripped.Nebula.StrokeColor);
        Assert.Equal(1.5, roundTripped.Nebula.StrokeWidth);
        Assert.Equal(0.7, roundTripped.Nebula.StrokeOpacity);
        Assert.Equal("#654321", roundTripped.Nebula.HaloColor);
        Assert.Equal(0.37, roundTripped.Nebula.HaloOpacity);
        Assert.Equal(2.5, roundTripped.Nebula.HaloSize);
    }
}
