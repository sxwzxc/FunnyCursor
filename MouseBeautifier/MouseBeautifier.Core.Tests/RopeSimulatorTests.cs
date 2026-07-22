using System.Numerics;
using MouseBeautifier.Core;
using Xunit;

namespace MouseBeautifier.Core.Tests;

public sealed class RopeSimulatorTests
{
    [Fact]
    public void StationaryRopeSettlesAtConfiguredReach()
    {
        AppSettings settings = CreateSettings();
        RopeSimulator rope = new();
        Vector2 anchor = new(500, 300);

        rope.ApplySettings(settings);
        for (int i = 0; i < 300; i++)
        {
            rope.Update(1.0 / 60.0, anchor, settings);
        }

        float expectedReach =
            (float)(settings.RopeLength - settings.IconSize);
        Assert.InRange(
            MathF.Abs(rope.Bob.X - anchor.X),
            0,
            5);
        Assert.InRange(
            rope.Bob.Y - anchor.Y,
            expectedReach * 0.8f,
            expectedReach * 1.2f);
    }

    [Fact]
    public void ViolentJitterKeepsEveryPointFiniteAndBounded()
    {
        AppSettings settings = CreateSettings();
        RopeSimulator rope = new();
        Vector2 anchor = new(400, 300);
        Random random = new(42);
        float bobLimit =
            (float)(settings.RopeLength - settings.IconSize) + 1;

        rope.ApplySettings(settings);
        for (int frame = 0; frame < 600; frame++)
        {
            anchor += new Vector2(
                (float)(random.NextDouble() * 600 - 300),
                (float)(random.NextDouble() * 600 - 300));
            rope.Update(1.0 / 60.0, anchor, settings);

            Assert.InRange(
                Vector2.Distance(rope.Bob, anchor),
                0,
                bobLimit);
            foreach (Vector2 point in rope.Points)
            {
                Assert.True(float.IsFinite(point.X));
                Assert.True(float.IsFinite(point.Y));
            }
        }
    }

    [Fact]
    public void TeleportCannotDetachBob()
    {
        AppSettings settings = CreateSettings();
        RopeSimulator rope = new();
        Vector2 anchor = Vector2.Zero;
        float bobLimit =
            (float)(settings.RopeLength - settings.IconSize) + 1;

        rope.ApplySettings(settings);
        for (int i = 0; i < 60; i++)
        {
            rope.Update(1.0 / 60.0, anchor, settings);
        }

        anchor = new Vector2(2000, 800);
        rope.Update(0.1, anchor, settings);

        Assert.InRange(
            Vector2.Distance(rope.Bob, anchor),
            0,
            bobLimit);
    }

    [Fact]
    public void CompleteStarRemainsWithinConfiguredRopeLength()
    {
        AppSettings settings = CreateSettings();
        RopeSimulator rope = new();
        Vector2 anchor = new(400, 300);
        Random random = new(7);
        float size = (float)settings.IconSize;
        Vector2[] star = PendantGeometry.StarLocalPolygon(size);

        rope.ApplySettings(settings);
        for (int frame = 0; frame < 600; frame++)
        {
            anchor += new Vector2(
                (float)(random.NextDouble() * 200 - 100),
                0);
            rope.Update(1.0 / 60.0, anchor, settings);
            PendantGeometry.PendantState pendant =
                PendantGeometry.ComputePendant(rope.Points, size);

            foreach (Vector2 localPoint in star)
            {
                Vector2 worldPoint =
                    PendantGeometry.TransformPoint(pendant, localPoint);
                Assert.InRange(
                    Vector2.Distance(worldPoint, anchor),
                    0,
                    (float)settings.RopeLength * 1.02f);
            }
        }
    }

    private static AppSettings CreateSettings()
    {
        return new AppSettings
        {
            RopeLength = 170,
            RopeSegments = 18,
            RopeGravity = 1500,
            RopeDamping = 0.9,
            RopeStiffness = 0.6,
            IconSize = 38,
        };
    }
}
