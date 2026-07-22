using System.Numerics;
using MouseBeautifier.Core;
using Xunit;

namespace MouseBeautifier.Core.Tests;

public sealed class PendantGeometryTests
{
    [Fact]
    public void StarTopVertexIsLocalAttachmentOrigin()
    {
        Vector2[] star = PendantGeometry.StarLocalPolygon(38);
        Vector2 top = star.OrderBy(point => point.Y).First();

        Assert.InRange(MathF.Abs(top.X), 0, 0.0001f);
        Assert.InRange(MathF.Abs(top.Y), 0, 0.0001f);
    }

    [Fact]
    public void AttachmentOriginAlwaysMapsToRopeTip()
    {
        Random random = new(20260715);
        for (int i = 0; i < 500; i++)
        {
            double angle = random.NextDouble() * 2 * Math.PI;
            Vector2 previous = new(
                (float)(random.NextDouble() * 1000),
                (float)(random.NextDouble() * 700));
            Vector2 tip = previous + new Vector2(
                (float)Math.Cos(angle) * 120,
                (float)Math.Sin(angle) * 120);

            PendantGeometry.PendantState pendant =
                PendantGeometry.ComputePendant(
                    new[] { previous, tip },
                    38);
            Vector2 transformed =
                PendantGeometry.TransformPoint(pendant, Vector2.Zero);

            Assert.InRange(
                Vector2.Distance(transformed, tip),
                0,
                0.0001f);
        }
    }

    [Fact]
    public void PendantDirectionIsUnitLengthAndFollowsRope()
    {
        Vector2 previous = new(400, 300);
        Vector2 tip = new(325, 425);
        PendantGeometry.PendantState pendant =
            PendantGeometry.ComputePendant(
                new[] { previous, tip },
                38);

        Assert.InRange(
            MathF.Abs(pendant.Direction.Length() - 1),
            0,
            0.0001f);
        Assert.InRange(
            Vector2.Distance(
                pendant.BaseCenter,
                tip + pendant.Direction * 38),
            0,
            0.0001f);
    }

    [Theory]
    [InlineData(0f, 0f, 1f)]
    [InlineData(1920f, 0f, 1f)]
    [InlineData(-1600f, -300f, 0.8f)]
    [InlineData(0f, 0f, 2f)]
    public void RenderTransformPreservesMonitorProjection(
        float monitorLeft,
        float monitorTop,
        float dipScale)
    {
        Vector2 previous = new(1710, 640);
        Vector2 tip = new(1800, 700);
        PendantGeometry.PendantState pendant =
            PendantGeometry.ComputePendant(
                new[] { previous, tip },
                38);
        Matrix3x2 projection =
            Matrix3x2.CreateTranslation(-monitorLeft, -monitorTop) *
            Matrix3x2.CreateScale(dipScale);
        Matrix3x2 complete =
            PendantGeometry.CreateRenderTransform(pendant, projection);

        Vector2 renderedTip =
            Vector2.Transform(Vector2.Zero, complete);
        Vector2 expectedTip =
            Vector2.Transform(tip, projection);
        Vector2 renderedFar =
            Vector2.Transform(new Vector2(0, 38), complete);
        Vector2 expectedFar =
            Vector2.Transform(
                tip + pendant.Direction * 38,
                projection);

        Assert.InRange(
            Vector2.Distance(renderedTip, expectedTip),
            0,
            0.0001f);
        Assert.InRange(
            Vector2.Distance(renderedFar, expectedFar),
            0,
            0.0001f);
    }

    [Fact]
    public void InvalidInputFallsBackToFiniteDownwardPendant()
    {
        PendantGeometry.PendantState pendant =
            PendantGeometry.ComputePendant(
                new[]
                {
                    new Vector2(float.NaN, 1),
                    new Vector2(float.NaN, float.NaN),
                },
                38);

        Assert.Equal(Vector2.Zero, pendant.Tip);
        Assert.Equal(new Vector2(0, 1), pendant.Direction);
        Assert.True(float.IsFinite(pendant.AngleRad));
        Assert.True(float.IsFinite(pendant.BaseCenter.X));
        Assert.True(float.IsFinite(pendant.BaseCenter.Y));
    }

    [Fact]
    public void InvalidSizeCannotPoisonPendantGeometry()
    {
        PendantGeometry.PendantState pendant =
            PendantGeometry.ComputePendant(
                new[]
                {
                    Vector2.Zero,
                    new Vector2(0, 10),
                },
                float.NaN);

        Assert.Equal(pendant.Tip, pendant.BaseCenter);
        Assert.True(float.IsFinite(pendant.AngleRad));
    }
}
