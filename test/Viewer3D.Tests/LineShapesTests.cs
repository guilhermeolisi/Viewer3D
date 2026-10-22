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
}
