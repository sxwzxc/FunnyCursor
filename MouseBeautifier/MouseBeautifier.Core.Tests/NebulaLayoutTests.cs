using System.Numerics;
using MouseBeautifier.Core;
using Xunit;

namespace MouseBeautifier.Core.Tests;

public sealed class NebulaLayoutTests
{
    [Fact]
    public void LayoutIsDeterministicAndFinite()
    {
        NebulaRenderSettings settings =
            new NebulaSettings
            {
                Enabled = true,
                ParticleCount = 120,
            }.ToRenderSettings();
        NebulaParticleVisual[] first =
            new NebulaParticleVisual[settings.ParticleCount];
        NebulaParticleVisual[] second =
            new NebulaParticleVisual[settings.ParticleCount];

        NebulaLayout.Generate(
            first,
            new Vector2(400, 300),
            settings,
            725.5,
            12.25);
        NebulaLayout.Generate(
            second,
            new Vector2(400, 300),
            settings,
            725.5,
            12.25);

        Assert.Equal(first, second);
        Assert.All(first, visual =>
        {
            Assert.True(float.IsFinite(visual.Position.X));
            Assert.True(float.IsFinite(visual.Position.Y));
            Assert.True(float.IsFinite(visual.Tangent.X));
            Assert.True(float.IsFinite(visual.Tangent.Y));
            Assert.True(float.IsFinite(visual.Radius));
            Assert.True(float.IsFinite(visual.Brightness));
            Assert.True(float.IsFinite(visual.TrailLength));
            Assert.True(visual.Radius > 0);
            Assert.InRange(visual.Brightness, 0.55f, 1f);
        });
    }

    [Fact]
    public void HaloSizeDoesNotChangeOrbitingStarGeometry()
    {
        NebulaSettings firstSettings = new()
        {
            HaloSize = NebulaSettings.MinHaloSize,
        };
        NebulaSettings secondSettings = new()
        {
            HaloSize = NebulaSettings.MaxHaloSize,
        };
        NebulaParticleVisual[] first =
            new NebulaParticleVisual[firstSettings.ParticleCount];
        NebulaParticleVisual[] second =
            new NebulaParticleVisual[secondSettings.ParticleCount];

        NebulaLayout.Generate(
            first,
            Vector2.Zero,
            firstSettings.ToRenderSettings(),
            45,
            3);
        NebulaLayout.Generate(
            second,
            Vector2.Zero,
            secondSettings.ToRenderSettings(),
            45,
            3);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ZeroAngularSpeedProducesNoCometTrail()
    {
        NebulaSettings source = new()
        {
            AngularSpeed = 0,
        };
        NebulaParticleVisual[] visuals =
            new NebulaParticleVisual[source.ParticleCount];

        NebulaLayout.Generate(
            visuals,
            Vector2.Zero,
            source.ToRenderSettings(),
            0,
            5);

        Assert.All(
            visuals,
            visual => Assert.Equal(0, visual.TrailLength));
    }

    [Fact]
    public void ReverseRotationFlipsTrailDirection()
    {
        NebulaSettings clockwise = new()
        {
            AngularSpeed = 90,
        };
        NebulaSettings counterClockwise = new()
        {
            AngularSpeed = -90,
        };
        NebulaParticleVisual[] forward =
            new NebulaParticleVisual[clockwise.ParticleCount];
        NebulaParticleVisual[] reverse =
            new NebulaParticleVisual[counterClockwise.ParticleCount];

        NebulaLayout.Generate(
            forward,
            Vector2.Zero,
            clockwise.ToRenderSettings(),
            30,
            2);
        NebulaLayout.Generate(
            reverse,
            Vector2.Zero,
            counterClockwise.ToRenderSettings(),
            30,
            2);

        for (int i = 0; i < forward.Length; i++)
        {
            Assert.Equal(forward[i].Position, reverse[i].Position);
            Assert.InRange(
                Vector2.Distance(
                    forward[i].Tangent,
                    -reverse[i].Tangent),
                0,
                0.00001f);
            Assert.Equal(
                forward[i].TrailLength,
                reverse[i].TrailLength);
        }
    }
}
