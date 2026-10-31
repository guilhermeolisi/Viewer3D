namespace Viewer3D.Tests;

public class AxesFrameTicksTests
{
    // Oracle: by hand, Heckbert's 1-2-5 rule (Graphics Gems, 1990): range = nice(2m) rounded up to 1, 2, 5 or 10 times
    // 10^n; step = nice(range / 4) with the thresholds 1.5, 3 and 7; limit = ceiling(m / step) * step.
    //   0.36:   2m = 0.72 -> range 1;       1/4 = 0.25 (2.5 < 3)     -> step 0.2;   ceiling(1.8) = 2 -> 0.4
    //   2600:   2m = 5200 -> range 10000;   2500 (2.5 < 3)           -> step 2000;  ceiling(1.3) = 2 -> 4000
    //   0.0012: 2m = 0.0024 -> range 0.005; 0.00125 (1.25 < 1.5)     -> step 0.001; ceiling(1.2) = 2 -> 0.002
    [Theory]
    [InlineData(0.36, 0.2, 0.4)]
    [InlineData(2600, 2000, 4000)]
    [InlineData(0.0012, 0.001, 0.002)]
    public void NiceLimits_KnownRanges_GiveTheHandStepAndLimit(double maxAbs, double step, double limit)
    {
        AxesFrameLimits limits = AxesFrame.NiceLimits(maxAbs, 5);

        Assert.Equal(step, limits.Step, step * 1e-12);
        Assert.Equal(limit, limits.Max, limit * 1e-12);
        Assert.Equal(-limit, limits.Min, limit * 1e-12);
    }

    [Fact]
    public void NiceLimits_Sweep_StepIsOneTwoOrFiveTimesAPowerOfTenAndTheLimitCoversTheRange()
    {
        for (double maxAbs = 1e-6; maxAbs < 1e6; maxAbs *= 1.07)
        {
            AxesFrameLimits limits = AxesFrame.NiceLimits(maxAbs, 5);

            double mantissa = limits.Step / Math.Pow(10, Math.Floor(Math.Log10(limits.Step) + 1e-9));
            Assert.True(
                Math.Abs(mantissa - 1) < 1e-9 || Math.Abs(mantissa - 2) < 1e-9 || Math.Abs(mantissa - 5) < 1e-9,
                $"step {limits.Step} for {maxAbs}");
            Assert.True(limits.Max >= maxAbs * (1 - 1e-12), $"limit {limits.Max} below {maxAbs}");
            Assert.True(limits.Max - maxAbs < limits.Step * (1 + 1e-9), $"limit {limits.Max} more than a step beyond {maxAbs}");
            Assert.Equal(-limits.Max, limits.Min);
            double ticks = limits.Max / limits.Step;
            Assert.Equal(Math.Round(ticks), ticks, 1e-9);
        }
    }

    [Fact]
    public void NiceLimits_NotPositive_FallsBackToTheUnitRange()
    {
        Assert.Equal(AxesFrame.NiceLimits(1, 5), AxesFrame.NiceLimits(0, 5));
        Assert.Equal(AxesFrame.NiceLimits(1, 5), AxesFrame.NiceLimits(double.NaN, 5));
    }

    [Fact]
    public void TickCount_CountsBothEnds()
    {
        Assert.Equal(5, new AxesFrameLimits(-0.4, 0.4, 0.2).TickCount);
        Assert.Equal(5, new AxesFrameLimits(-4000, 4000, 2000).TickCount);
    }

    [Theory]
    [InlineData(0.4, 0.2, "0.4")]
    [InlineData(-4000, 2000, "-4000")]
    [InlineData(0.002, 0.001, "0.002")]
    [InlineData(-1e-17, 0.2, "0.0")]
    [InlineData(-1e-13, 2000, "0")]
    [InlineData(0.25, 0.05, "0.25")]
    public void FormatTick_UsesTheDecimalsOfTheStepAndNoMinusZero(double value, double step, string text)
    {
        Assert.Equal(text, AxesFrame.FormatTick(value, step));
    }

    [Fact]
    public void AutoLimits_TakesTheLargestCoordinateOfTheScene()
    {
        var scene = new Scene(
        [
            new MeshItem { Positions = [new(0.1f, -0.36f, 0.2f), new(0.3f, 0, 0), new(0, 0.2f, -0.1f)], Indices = [0, 1, 2] },
            new SphereItem { Center = new(0.05f, 0, 0), Radius = 0.1f },
        ]);

        Assert.Equal(0.36, AxesFrame.MaxAbsCoordinate(scene), 1e-6);
        Assert.Equal(new AxesFrameLimits(-0.4, 0.4, 0.2).Max, AxesFrame.AutoLimits(scene).Max, 1e-12);
    }
}
