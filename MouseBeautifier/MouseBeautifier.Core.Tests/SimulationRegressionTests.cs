using System.Diagnostics;
using System.Numerics;
using MouseBeautifier.Core;
using Xunit;

namespace MouseBeautifier.Core.Tests;

public sealed class SimulationRegressionTests
{
    [Fact]
    public void ClearingTimestampQueueStartsANewTimestampEpoch()
    {
        TimestampedInputQueue<string> queue = new(capacity: 2);
        queue.Enqueue(100, "before-reset");
        queue.Clear();
        queue.Enqueue(10, "after-reset");

        Assert.True(queue.TryDequeueUpTo(10, out var input));
        Assert.Equal("after-reset", input.Value);
        Assert.Equal(10, input.Timestamp);
    }

    [Fact]
    public void InputBurstAndRuntimeGeometryChangesRemainBounded()
    {
        AppSettings settings = new()
        {
            ClickPreset = "confetti",
            ClickParticleCount = 600,
            EnableClickEffects = true,
            EnableRope = true,
            EnableTrail = true,
            TrailLength = 10,
            TrailWidth = 2,
        };
        EffectWorld world = new(clickQueueCapacity: 8, randomSeed: 42);

        for (int i = 0; i < 1_000; i++)
        {
            world.EnqueueClick(i, new Vector2(i, -i));
        }

        Assert.Equal(8, world.PendingClickCount);

        for (int frame = 0; frame < 1_000; frame++)
        {
            if (frame % 25 == 0)
            {
                settings.RopeSegments = 2 + frame % 39;
                settings.RopeLength = 20 + frame % 481;
                settings.IconSize = 10 + frame % 111;
            }

            Vector2 cursor = new(
                MathF.Sin(frame * 0.17f) * 4_000,
                MathF.Cos(frame * 0.11f) * 2_000);
            world.AdvanceFrame(1.0 / 60, 2_000 + frame, cursor, settings);

            Assert.InRange(world.Particles.Particles.Count, 0, 4_000);
            Assert.InRange(world.Trail.Samples.Count, 0, 2_048);
            Assert.All(world.Rope.Points, point =>
            {
                Assert.True(float.IsFinite(point.X));
                Assert.True(float.IsFinite(point.Y));
            });

            double availableReach = Math.Max(
                8,
                settings.RopeLength - settings.IconSize);
            Assert.InRange(
                Vector2.Distance(world.Rope.Bob, cursor),
                0,
                (float)availableReach + 0.01f);
        }
    }

    [Fact]
    public void SustainedSimulationMeetsHeadlessPerformanceBudget()
    {
        AppSettings settings = new()
        {
            EnableClickEffects = false,
            EnableRope = true,
            EnableTrail = false,
            RopeSegments = 40,
            RopeStiffness = 1,
            Nebula = new NebulaSettings
            {
                Enabled = true,
            },
        };
        EffectWorld world = new(randomSeed: 42);

        for (int frame = 0; frame < 240; frame++)
        {
            world.AdvanceFrame(
                1.0 / 60,
                frame,
                new Vector2(frame, frame % 100),
                settings);
        }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch stopwatch = Stopwatch.StartNew();

        for (int frame = 0; frame < 2_000; frame++)
        {
            Vector2 cursor = new(
                MathF.Sin(frame * 0.01f) * 1_000,
                MathF.Cos(frame * 0.01f) * 600);
            world.AdvanceFrame(
                1.0 / 60,
                10_000 + frame,
                cursor,
                settings);
            _ = world.CaptureSnapshot(cursor);
        }

        stopwatch.Stop();
        long allocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(3),
            $"2,000 frames took {stopwatch.Elapsed.TotalMilliseconds:N0} ms.");
        Assert.InRange(allocatedBytes, 0, 64 * 1024);
    }
}
