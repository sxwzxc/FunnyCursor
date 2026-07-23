using System.Numerics;
using MouseBeautifier.Core;
using Xunit;

namespace MouseBeautifier.Core.Tests;

public sealed class FixedStepClockTests
{
    [Fact]
    public void DifferentFramePartitionsProduceSameStepCount()
    {
        FixedStepClock sixtyHertz = new();
        FixedStepClock oneFortyFourHertz = new();
        int sixtySteps = 0;
        int oneFortyFourSteps = 0;

        for (int frame = 0; frame < 60; frame++)
        {
            sixtyHertz.Advance(1.0 / 60, _ => sixtySteps++);
        }

        for (int frame = 0; frame < 144; frame++)
        {
            oneFortyFourHertz.Advance(
                1.0 / 144,
                _ => oneFortyFourSteps++);
        }

        Assert.Equal(120, sixtySteps);
        Assert.Equal(sixtySteps, oneFortyFourSteps);
    }

    [Fact]
    public void LongFrameCannotCreateUnboundedCatchUp()
    {
        FixedStepClock clock = new();
        int callbacks = 0;

        int steps = clock.Advance(10, _ => callbacks++);

        Assert.Equal(12, steps);
        Assert.Equal(steps, callbacks);
    }
}

public sealed class ParticleSimulationTests
{
    [Fact]
    public void SpawnedParticleStartsAliveAndExpiresNormally()
    {
        AppSettings settings = new()
        {
            ClickPreset = "sparkle",
            ClickParticleCount = 1,
        };
        ParticleSimulation particles = new(randomSeed: 42);

        particles.Spawn(Vector2.Zero, settings);

        ParticleState spawned = Assert.Single(particles.Particles);
        Assert.Equal(spawned.MaximumLife, spawned.Life);
        Assert.True(spawned.Life > 0);

        particles.Update(1.0 / 120, settings);
        Assert.Single(particles.Particles);

        for (int i = 0; i < 300; i++)
        {
            particles.Update(1.0 / 120, settings);
        }

        Assert.Empty(particles.Particles);
    }
}

public sealed class TrailSimulationTests
{
    [Fact]
    public void LongMovementIsResampledAtNearUniformSpacing()
    {
        AppSettings settings = new()
        {
            TrailLength = 10,
            TrailWidth = 4,
        };
        TrailSimulation trail = new();
        trail.Update(Vector2.Zero, 1.0 / 120, settings);
        trail.Update(new Vector2(100, 0), 1.0 / 120, settings);

        Assert.InRange(trail.Samples.Count, 33, 35);
        for (int i = 1; i < trail.Samples.Count; i++)
        {
            float distance = Vector2.Distance(
                trail.Samples[i - 1].Position,
                trail.Samples[i].Position);
            Assert.InRange(distance, 2.99f, 3.01f);
        }

        Assert.Equal(new Vector2(100, 0), trail.CurrentPoint);
    }

    [Fact]
    public void ExpiredSamplesAreRemoved()
    {
        AppSettings settings = new()
        {
            TrailLength = 0.05,
            TrailWidth = 4,
        };
        TrailSimulation trail = new();
        trail.Update(Vector2.Zero, 0, settings);
        trail.Update(new Vector2(30, 0), 0, settings);

        for (int i = 0; i < 10; i++)
        {
            trail.Update(new Vector2(30, 0), 0.01, settings);
        }

        Assert.Empty(trail.Samples);
    }
}

public sealed class TimestampedInputQueueTests
{
    [Fact]
    public void CapacityDropsOldestAndTimestampGatesConsumption()
    {
        TimestampedInputQueue<string> queue = new(capacity: 2);
        queue.Enqueue(10, "old");
        queue.Enqueue(20, "middle");
        queue.Enqueue(30, "new");

        Assert.Equal(2, queue.Count);
        Assert.False(queue.TryDequeueUpTo(19, out _));
        Assert.True(queue.TryDequeueUpTo(20, out var middle));
        Assert.Equal("middle", middle.Value);
        Assert.True(queue.TryDequeueUpTo(30, out var newest));
        Assert.Equal("new", newest.Value);
    }

    [Fact]
    public void BackwardTimestampIsNormalizedToFifoOrder()
    {
        TimestampedInputQueue<int> queue = new(capacity: 4);
        queue.Enqueue(100, 1);
        queue.Enqueue(50, 2);

        Assert.False(queue.TryDequeueUpTo(99, out _));
        Assert.True(queue.TryDequeueUpTo(100, out var first));
        Assert.True(queue.TryDequeueUpTo(100, out var second));
        Assert.Equal(1, first.Value);
        Assert.Equal(2, second.Value);
        Assert.Equal(100, second.Timestamp);
    }
}

public sealed class EffectWorldTests
{
    [Fact]
    public void StationaryWorldMatchesAcrossPresentationRates()
    {
        AppSettings settings = new()
        {
            EnableClickEffects = false,
            EnableTrail = false,
            EnableRope = true,
            Nebula = new NebulaSettings
            {
                Enabled = true,
            },
        };
        EffectWorld sixtyHertz = new(randomSeed: 42);
        EffectWorld oneFortyFourHertz = new(randomSeed: 42);
        Vector2 cursor = new(500, 300);

        for (int frame = 0; frame < 60; frame++)
        {
            sixtyHertz.AdvanceFrame(
                1.0 / 60,
                frame,
                cursor,
                settings);
        }

        for (int frame = 0; frame < 144; frame++)
        {
            oneFortyFourHertz.AdvanceFrame(
                1.0 / 144,
                frame,
                cursor,
                settings);
        }

        Assert.InRange(
            Vector2.Distance(
                sixtyHertz.Rope.Bob,
                oneFortyFourHertz.Rope.Bob),
            0,
            0.001f);
        Assert.InRange(
            Math.Abs(
                sixtyHertz.OrbitAngleDegrees -
                oneFortyFourHertz.OrbitAngleDegrees),
            0,
            0.001f);
    }

    [Fact]
    public void OrbitPhaseRemainsContinuousBeyondFullRotation()
    {
        AppSettings settings = new()
        {
            EnableClickEffects = false,
            EnableTrail = false,
            EnableRope = false,
            Nebula = new NebulaSettings
            {
                Enabled = true,
                AngularSpeed = 180,
            },
        };
        EffectWorld world = new(randomSeed: 42);

        for (int frame = 0; frame < 180; frame++)
        {
            world.AdvanceFrame(
                1.0 / 60,
                frame,
                Vector2.Zero,
                settings);
        }

        Assert.InRange(world.OrbitAngleDegrees, 539.9, 540.1);
    }

    [Fact]
    public void DisabledOrbitFreezesItsIndependentClock()
    {
        AppSettings settings = new()
        {
            EnableClickEffects = false,
            EnableTrail = false,
            EnableRope = false,
            Nebula = new NebulaSettings
            {
                Enabled = true,
                AngularSpeed = 45,
            },
        };
        EffectWorld world = new(randomSeed: 42);
        world.AdvanceFrame(
            1.0 / 60,
            1,
            Vector2.Zero,
            settings);
        double angle = world.OrbitAngleDegrees;
        double animationTime = world.OrbitAnimationTime;

        settings.Nebula.Enabled = false;
        for (int frame = 0; frame < 120; frame++)
        {
            world.AdvanceFrame(
                1.0 / 60,
                frame + 2,
                Vector2.Zero,
                settings);
        }

        Assert.Equal(angle, world.OrbitAngleDegrees);
        Assert.Equal(animationTime, world.OrbitAnimationTime);
    }

    [Fact]
    public void ClickIsNotAppliedBeforeItsTimestamp()
    {
        AppSettings settings = new()
        {
            ClickPreset = "sparkle",
            ClickParticleCount = 1,
        };
        EffectWorld world = new(randomSeed: 42);
        world.EnqueueClick(100, new Vector2(20, 30));

        world.AdvanceFrame(
            1.0 / 60,
            99,
            Vector2.Zero,
            settings);
        Assert.Empty(world.Particles.Particles);

        world.AdvanceFrame(
            1.0 / 60,
            100,
            Vector2.Zero,
            settings);
        Assert.Single(world.Particles.Particles);
    }
}
