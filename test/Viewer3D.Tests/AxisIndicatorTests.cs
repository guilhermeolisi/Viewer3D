using System.Numerics;

namespace Viewer3D.Tests;

public class AxisIndicatorTests
{
    static readonly AxisDirection[] Cartesian =
    [
        new(Vector3.UnitX, "a"),
        new(Vector3.UnitY, "b"),
        new(Vector3.UnitZ, "c"),
    ];

    static readonly Vector2 Origin = new(50, 150);

    [Fact]
    public void Compute_IgnoresZoomAndPan()
    {
        var camera = new Camera();
        camera.Orbit(40, 25);
        AxisGlyph[] before = AxisIndicator.Compute(Cartesian, camera.Orientation, Origin, 30);

        camera.ZoomAt(3, 10, 10, 200, 200);
        camera.Pan(40, -20, 200, 200);
        AxisGlyph[] after = AxisIndicator.Compute(Cartesian, camera.Orientation, Origin, 30);

        Assert.Equal(before, after);
    }

    [Fact]
    public void Compute_FollowsTheRotation()
    {
        var camera = new Camera();
        AxisGlyph[] before = AxisIndicator.Compute(Cartesian, camera.Orientation, Origin, 30);

        camera.Orbit(40, 25);
        AxisGlyph[] after = AxisIndicator.Compute(Cartesian, camera.Orientation, Origin, 30);

        Assert.NotEqual(before.First(g => g.Index == 0).End, after.First(g => g.Index == 0).End);
    }

    [Fact]
    public void Compute_ArrowsHaveTheSameLengthWhateverTheAxisLength()
    {
        AxisDirection[] axes = [new(new Vector3(5, 0, 0), "a"), new(new Vector3(0, 0.2f, 0), "b")];

        AxisGlyph[] glyphs = AxisIndicator.Compute(axes, Quaternion.Identity, Origin, 30);

        foreach (AxisGlyph g in glyphs)
            Assert.Equal(30f, Vector2.Distance(g.Start, g.End), 3);
    }

    [Fact]
    public void Compute_KeepsTheAngleBetweenNonOrthogonalAxes()
    {
        // Hexagonal a and b, 120 degrees apart, seen from along c.
        AxisDirection[] axes =
        [
            new(Vector3.UnitX, "a"),
            new(new Vector3(MathF.Cos(2 * MathF.PI / 3), MathF.Sin(2 * MathF.PI / 3), 0), "b"),
        ];

        AxisGlyph[] glyphs = AxisIndicator.Compute(axes, Quaternion.Identity, Origin, 30);

        Vector2 a = glyphs.First(g => g.Index == 0).End - Origin, b = glyphs.First(g => g.Index == 1).End - Origin;
        float angle = MathF.Acos(Vector2.Dot(a, b) / (a.Length() * b.Length())) * 180 / MathF.PI;
        Assert.Equal(120f, angle, 2);
    }

    [Fact]
    public void Compute_AxisPointingAtTheViewerBecomesAPoint()
    {
        AxisGlyph[] glyphs = AxisIndicator.Compute(Cartesian, Quaternion.Identity, Origin, 30);

        AxisGlyph c = glyphs.First(g => g.Index == 2);
        Assert.True(c.IsPoint);
        Assert.Equal(c.Start, c.End);
        Assert.False(glyphs.First(g => g.Index == 0).IsPoint);
    }

    [Fact]
    public void Compute_OrdersFromBackToFront()
    {
        var camera = new Camera();
        camera.Orbit(40, 25);

        AxisGlyph[] glyphs = AxisIndicator.Compute(Cartesian, camera.Orientation, Origin, 30);

        Assert.Equal(3, glyphs.Length);
        for (int i = 1; i < glyphs.Length; i++)
            Assert.True(glyphs[i - 1].Depth <= glyphs[i].Depth);
    }

    [Fact]
    public void Compute_ScreenYPointsDown()
    {
        AxisGlyph[] glyphs = AxisIndicator.Compute(Cartesian, Quaternion.Identity, Origin, 30);

        Assert.Equal(new Vector2(80, 150), glyphs.First(g => g.Index == 0).End);
        Assert.Equal(new Vector2(50, 120), glyphs.First(g => g.Index == 1).End);
    }
}
