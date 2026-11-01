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
    public void Dihedral_ArcSweepsTheDihedralAngleAroundTheAxis()
    {
        // Axis along z through (0,0,0)-(0,0,2); p at azimuth 0 and q at azimuth 120 degrees, at different heights.
        // Oraculo: a mao, the angle between the projections of p and q on the plane normal to the axis is 120 degrees.
        Vector3 a = new(0, 0, 0), b = new(0, 0, 2), p = new(1.5f, 0, -0.4f);
        Vector3 q = new(2 * MathF.Cos(2 * MathF.PI / 3), 2 * MathF.Sin(2 * MathF.PI / 3), 2.7f);

        SceneItem[] items = MeasureShapes.Dihedral(p, a, b, q, 0.5f, Color, "120.00°", segments: 12);
        LineItem[] arc = Arc(items);
        Vector3 middle = new(0, 0, 1);

        Assert.Equal(12, arc.Length);
        Near(middle + new Vector3(0.5f, 0, 0), arc[0].Start);
        Near(middle + 0.5f * new Vector3(MathF.Cos(2 * MathF.PI / 3), MathF.Sin(2 * MathF.PI / 3), 0), arc[^1].End);
        Assert.All(arc, l => Assert.Equal(0.5f, Vector3.Distance(l.End, middle), 1e-5f));
        Assert.All(arc, l => Assert.Equal(1f, l.End.Z, 1e-5f));
        float swept = arc.Sum(l => MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(l.Start - middle), Vector3.Normalize(l.End - middle)), -1f, 1f)));
        Assert.Equal(2 * MathF.PI / 3, swept, 1e-4f);
    }

    [Fact]
    public void Dihedral_ArmsArePerpendicularToTheDashedAxis()
    {
        Vector3 a = new(1, 1, 0), b = new(2, 3, 1), p = new(0, 2, 2), q = new(3, 1, -1);

        SceneItem[] items = MeasureShapes.Dihedral(p, a, b, q, 0.7f, Color, "x");
        LineItem[] dashed = [.. items.OfType<LineItem>().Where(l => l.Dash > 0)];

        Assert.Equal(3, dashed.Length);
        Near(a, dashed[0].Start);
        Near(b, dashed[0].End);
        Vector3 axis = Vector3.Normalize(b - a);
        foreach (LineItem arm in dashed.Skip(1))
        {
            Near((a + b) / 2, arm.Start);
            Assert.Equal(0.7f, Vector3.Distance(arm.Start, arm.End), 1e-5f);
            Assert.Equal(0, Vector3.Dot(arm.End - arm.Start, axis), 1e-5f);
        }
        Assert.Single(items.OfType<LabelItem>());
        Assert.All(items, i => Assert.False(i.Pickable));
    }

    [Fact]
    public void Overlays_AreNotPickable()
    {
        SceneItem[] distance = MeasureShapes.Distance(Vector3.Zero, Vector3.UnitX, Color, "1");
        SceneItem[] angle = MeasureShapes.Angle(Vector3.UnitX, Vector3.Zero, Vector3.UnitY, 0.3f, Color, "90");

        Assert.All(distance.Concat(angle), i => Assert.False(i.Pickable));
    }

    [Fact]
    public void Dimension_IsASolidLineWithTicksAcrossBothEndsAndTheLabelAtTheMiddle()
    {
        Vector3 start = new(0, 0, 0), end = new(0, 0, 3);
        // The tick direction leans along the line: only its part across it counts.
        SceneItem[] items = MeasureShapes.Dimension(start, end, new Vector3(1, 0, 1), 0.4f, Color, "d = 3.0000 Å", id: 42);

        LineItem[] lines = [.. items.OfType<LineItem>()];
        Assert.Equal(3, lines.Length);
        Assert.All(lines, l => Assert.Equal(0, l.Dash));
        Assert.Equal(start, lines[0].Start);
        Assert.Equal(end, lines[0].End);
        foreach (LineItem tick in lines.Skip(1))
        {
            Assert.Equal(0.4f, Vector3.Distance(tick.Start, tick.End), 1e-5f);
            Assert.Equal(0, Vector3.Dot(tick.End - tick.Start, end - start), 1e-5f);
        }
        Near(start, (lines[1].Start + lines[1].End) / 2);
        Near(end, (lines[2].Start + lines[2].End) / 2);
        LabelItem label = Assert.Single(items.OfType<LabelItem>());
        Assert.Equal(new Vector3(0, 0, 1.5f), label.Position);
        Assert.Equal("d = 3.0000 Å", label.Text);
        Assert.All(items, i => Assert.False(i.Pickable));
        Assert.All(items, i => Assert.Equal(42, i.Id));
    }

    [Fact]
    public void Dimension_TickAlongTheLine_StillCrossesIt()
    {
        SceneItem[] items = MeasureShapes.Dimension(Vector3.Zero, Vector3.UnitX, Vector3.UnitX, 0.2f, Color, "1");

        LineItem tick = items.OfType<LineItem>().ElementAt(1);
        Assert.Equal(0.2f, Vector3.Distance(tick.Start, tick.End), 1e-5f);
        Assert.Equal(0, Vector3.Dot(tick.End - tick.Start, Vector3.UnitX), 1e-5f);
    }
}
