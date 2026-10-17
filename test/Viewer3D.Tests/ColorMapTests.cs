namespace Viewer3D.Tests;

public class ColorMapTests
{
    static readonly Rgba Red = new(255, 0, 0), Green = new(0, 255, 0), Blue = new(0, 0, 255);

    [Fact]
    public void LevelColorMap_TakesTheGreatestLevelNotAboveTheValue()
    {
        var map = new LevelColorMap([0, 1, 2], [Red, Green, Blue]);

        Assert.Equal(Red, map.Map(0.99));
        Assert.Equal(Green, map.Map(1));
        Assert.Equal(Green, map.Map(1.5));
        Assert.Equal(Blue, map.Map(100));
    }

    [Fact]
    public void LevelColorMap_BelowEveryLevel_TakesTheLowest()
    {
        var map = new LevelColorMap([0, 1], [Red, Green]);

        Assert.Equal(Red, map.Map(-5));
    }

    [Fact]
    public void LevelColorMap_AcceptsLevelsInAnyOrder()
    {
        var map = new LevelColorMap([2, 0, 1], [Blue, Red, Green]);

        Assert.Equal(Red, map.Map(0.5));
        Assert.Equal(Green, map.Map(1.5));
        Assert.Equal(Blue, map.Map(2.5));
        Assert.Equal([0.0, 1.0, 2.0], map.Levels);
    }

    [Fact]
    public void LevelColorMap_RejectsMismatchedLists()
    {
        Assert.Throws<ArgumentException>(() => new LevelColorMap([0, 1], [Red]));
        Assert.Throws<ArgumentException>(() => new LevelColorMap([], []));
    }

    [Fact]
    public void FuncColorMap_CallsTheFunction()
    {
        var map = new FuncColorMap(v => v > 0 ? Red : Blue);

        Assert.Equal(Red, map.Map(1));
        Assert.Equal(Blue, map.Map(-1));
    }

    [Fact]
    public void Rgba_PacksAndShades()
    {
        var c = new Rgba(200, 100, 50, 128);

        Assert.Equal(0x80C86432u, c.ToArgb());
        Assert.Equal(c, Rgba.FromArgb(c.ToArgb()));
        Assert.Equal(new Rgba(100, 50, 25, 128), c.Shade(0.5f));
        Assert.Equal(new Rgba(128, 128, 128), Rgba.Lerp(Rgba.Black, Rgba.White, 0.5f));
    }
}
