using System.Numerics;

namespace Viewer3D.Tests;

public class LineShapesTests
{
    static readonly Vector3[] Square = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)];
    static readonly Rgba Gray = new(120, 120, 120);
    static readonly Rgba Orange = new(240, 140, 20);

    [Fact]
    public void Polyline_Open_HasOneSegmentLessThanPoints()
    {
        LineItem[] lines = LineShapes.Polyline(Square, Gray, 1.5f, null, Orange, 4f, id: -3);

        Assert.Equal(3, lines.Length);
        for (int k = 0; k < 3; k++)
        {
            Assert.Equal(Square[k], lines[k].Start);
            Assert.Equal(Square[k + 1], lines[k].End);
            Assert.Equal(Gray, lines[k].Color);
            Assert.Equal(1.5f, lines[k].Width);
            Assert.Equal(-3, lines[k].Id);
        }
    }

    [Fact]
    public void Polyline_Closed_GoesBackToTheFirstPoint()
    {
        LineItem[] lines = LineShapes.Polyline(Square, Gray, 1.5f, null, Orange, 4f, closed: true);

        Assert.Equal(4, lines.Length);
        Assert.Equal(Square[3], lines[3].Start);
        Assert.Equal(Square[0], lines[3].End);
    }

    [Fact]
    public void Polyline_HighlightsOnlyTheChosenSegment()
    {
        LineItem[] lines = LineShapes.Polyline(Square, Gray, 1.5f, 2, Orange, 4f, closed: true);

        for (int k = 0; k < lines.Length; k++)
        {
            Assert.Equal(k == 2 ? Orange : Gray, lines[k].Color);
            Assert.Equal(k == 2 ? 4f : 1.5f, lines[k].Width);
        }
    }

    [Fact]
    public void Polyline_WithOnePoint_IsEmpty()
    {
        Assert.Empty(LineShapes.Polyline([Vector3.Zero], Gray, 1f, null, Orange, 2f));
    }

    [Fact]
    public void Circle_ClosesOnItselfInThePlaneNormalToTheAxis()
    {
        Vector3 center = new(1, -2, 0.5f), normal = new(1, 1, 1);
        LineItem[] lines = LineShapes.Circle(center, normal, 0.75f, Gray, segments: 24, id: 7);

        Assert.Equal(24, lines.Length);
        Vector3 n = Vector3.Normalize(normal);
        foreach (LineItem line in lines)
        {
            // Every vertex at the radius and in the plane through the center normal to the axis (float, 1e-5).
            Assert.Equal(0.75f, Vector3.Distance(line.Start, center), 1e-5f);
            Assert.Equal(0, Vector3.Dot(line.Start - center, n), 1e-5f);
            Assert.False(line.Pickable);
            Assert.Equal(7, line.Id);
        }
        for (int k = 0; k < lines.Length; k++)
            Assert.Equal(lines[k].End, lines[(k + 1) % lines.Length].Start);
    }

    [Fact]
    public void WireSphere_EveryVertexIsOnTheSphere()
    {
        Vector3 center = new(0, 0, -1);
        LineItem[] lines = LineShapes.WireSphere(center, 2, Gray, meridians: 4, parallels: 3, segments: 16);

        Assert.Equal((4 + 3) * 16, lines.Length);
        Assert.All(lines, l => Assert.Equal(2, Vector3.Distance(l.Start, center), 1e-5f));
        // The equator is a parallel when their number is odd: some vertex sits on z = center.z at the radius.
        Assert.Contains(lines, l => MathF.Abs(l.Start.Z - center.Z) < 1e-5f && MathF.Abs(l.Start.X - 2) < 1e-5f);
    }
}
