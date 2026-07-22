using System.Numerics;
using MouseBeautifier.Core;
using Xunit;

namespace MouseBeautifier.Core.Tests;

public sealed class DisplayGeometryTests
{
    [Fact]
    public void NegativeVirtualOriginMapsToMonitorLocalDips()
    {
        PixelRect monitor = new(-2560, -720, 2560, 1440);

        Vector2 local = DisplayGeometry.ScreenPixelsToLocalDips(
            new Vector2(-1280, 0),
            monitor,
            192,
            192);

        Assert.Equal(new Vector2(640, 360), local);
    }

    [Fact]
    public void MixedDpiMappingRoundTripsPhysicalPixels()
    {
        PixelRect monitor = new(1920, 0, 2560, 1440);
        Vector2 physical = new(3200.5f, 719.5f);

        Vector2 dips = DisplayGeometry.ScreenPixelsToLocalDips(
            physical,
            monitor,
            144,
            144);
        Vector2 roundTrip = DisplayGeometry.LocalDipsToScreenPixels(
            dips,
            monitor,
            144,
            144);

        Assert.InRange(Vector2.Distance(physical, roundTrip), 0, 0.001f);
    }

    [Fact]
    public void ProjectionMatrixMatchesDirectMapping()
    {
        PixelRect monitor = new(-1920, 300, 1920, 1080);
        Vector2 screenPoint = new(-960, 840);

        Vector2 direct = DisplayGeometry.ScreenPixelsToLocalDips(
            screenPoint,
            monitor,
            120,
            144);
        Vector2 transformed = Vector2.Transform(
            screenPoint,
            DisplayGeometry
                .CreateScreenPixelsToLocalDipsTransform(
                    monitor,
                    120,
                    144));

        Assert.InRange(Vector2.Distance(direct, transformed), 0, 0.001f);
    }

    [Fact]
    public void SnapshotIsSharedViewOfSingleAdvancedWorld()
    {
        AppSettings settings = new()
        {
            EnableRope = true,
            EnableTrail = true,
        };
        EffectWorld world = new(randomSeed: 7);
        Vector2 cursor = new(-500, 250);
        world.AdvanceFrame(1.0 / 60, 10, cursor, settings);

        EffectFrameSnapshot first = world.CaptureSnapshot(cursor);
        EffectFrameSnapshot second = world.CaptureSnapshot(cursor);

        Assert.Same(first.Particles, second.Particles);
        Assert.Same(first.Trail, second.Trail);
        Assert.Same(first.Rope, second.Rope);
        Assert.Equal(first.AnimationTime, second.AnimationTime);
        Assert.Equal(cursor, first.Cursor);
    }
}
