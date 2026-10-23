using System.Numerics;

namespace Viewer3D.Tests;

public class MeasureShapesTests
{
    static readonly Rgba Color = new(200, 40, 40);

    static LineItem[] Arc(SceneItem[] items) => [.. items.OfType<LineItem>().Where(l => l.Dash == 0)];

    static void Near(Vector3 expected, Vector3 actual) => Assert.True(Vector3.Distance(expected, actual) < 1e-5f, $"{actual} is not {expected}");

    [Fact]
    public void Distance_IsADashedLineWithTheLabelAtTheMiddle()
    {
        SceneItem[] items = MeasureShapes.Distance(new Vector3(0, 0, 0), new Vector3(2, 0, 0), Color, "2.0000 Å");

        LineItem line = Assert.Single(items.OfType<LineItem>());
        Assert.True(line.Dash > 0);
        LabelItem label = Assert.Single(items.OfType<LabelItem>());
        Assert.Equal(new Vector3(1, 0, 0), label.Position);
        Assert.Equal("2.0000 Å", label.Text);
    }

    [Fact]
    public void Angle_ArcEndpointsLieOnBothRays()
    {
        Vector3 vertex = new(1, 2, 3), a = new(4, 2, 3), c = new(1, 5, 7);

        SceneItem[] items = MeasureShapes.Angle(a, vertex, c, 0.5f, Color, "x", segments: 12);
        LineItem[] arc = Arc(items);

        Assert.Equal(12, arc.Length);
        Near(vertex + 0.5f * Vector3.Normalize(a - vertex), arc[0].Start);
        Near(vertex + 0.5f * Vector3.Normalize(c - vertex), arc[^1].End);
        Assert.All(arc, l => Assert.Equal(0.5f, Vector3.Distance(l.End, vertex), 1e-5f));
        Assert.Equal(2, items.OfType<LineItem>().Count(l => l.Dash > 0));
    }

    [Fact]
    public void Angle_90Degrees_ArcMidpointAtBisector()
    {
        SceneItem[] items = MeasureShapes.Angle(new Vector3(2, 0, 0), Vector3.Zero, new Vector3(0, 3, 0), 1, Color, "90.00°", segments: 2);
        LineItem[] arc = Arc(items);

        Near(Vector3.Normalize(new Vector3(1, 1, 0)), arc[0].End);
        LabelItem label = Assert.Single(items.OfType<LabelItem>());
        Assert.Equal(0, Vector3.Cross(label.Position, new Vector3(1, 1, 0)).Length(), 1e-5f);
    }

    [Fact]
    public void Angle_Straight_StillDrawsAHalfTurn()
    {
        SceneItem[] items = MeasureShapes.Angle(new Vector3(-1, 0, 0), Vector3.Zero, new Vector3(1, 0, 0), 1, Color, "180.00°", segments: 8);
        LineItem[] arc = Arc(items);

        Near(new Vector3(-1, 0, 0), arc[0].Start);
        Near(new Vector3(1, 0, 0), arc[^1].End);
        Assert.Equal(1, Vector3.Distance(arc[3].End, Vector3.Zero), 1e-5f);
        Assert.Equal(0, arc[3].End.X, 1e-5f);
    }

    [Fact]
    public void Overlays_AreNotPickable()
    {
        SceneItem[] distance = MeasureShapes.Distance(Vector3.Zero, Vector3.UnitX, Color, "1");
        SceneItem[] angle = MeasureShapes.Angle(Vector3.UnitX, Vector3.Zero, Vector3.UnitY, 0.3f, Color, "90");

        Assert.All(distance.Concat(angle), i => Assert.False(i.Pickable));
    }
}
