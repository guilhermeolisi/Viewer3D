using System.Numerics;

namespace Viewer3D.Tests;

public class GridSectionTests
{
    static readonly Rgba Gray = new(90, 90, 90);
    static readonly CellBox UnitCell = new(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    [Fact]
    public void GridSection_PlaneZHalfInA2x1x1Block_CrossesTheFiveWallsItMeets()
    {
        // Walls x = 0, 1, 2 and y = 0, 1 cross z = 0.5; the walls z = 0 and z = 1 are parallel to it.
        IReadOnlyList<LineItem> lines = PlaneFamilyShapes.GridSection(Vector3.UnitZ, 0.5f, UnitCell, 2, 1, 1, Gray);

        Assert.Equal(5, lines.Count);
        Assert.All(lines, l => Assert.Equal(0.5f, l.Start.Z, 1e-6f));
        Assert.All(lines, l => Assert.Equal(0.5f, l.End.Z, 1e-6f));
        // Three traces of length 1 (along y, on the walls x = m) and two of length 2 (along x, on the walls y = m).
        Assert.Equal(3, lines.Count(l => MathF.Abs(Vector3.Distance(l.Start, l.End) - 1) < 1e-5f));
        Assert.Equal(2, lines.Count(l => MathF.Abs(Vector3.Distance(l.Start, l.End) - 2) < 1e-5f));
        Assert.All(lines, l => Assert.False(l.Pickable));
    }

    [Fact]
    public void GridSection_PlaneOnAWall_SkipsThatWall_AndKeepsTheNetOfTheOthers()
    {
        // z = 0 lies in the wall z = 0: that wall gives no trace, the walls x = 0..2 and y = 0..1 draw its net.
        IReadOnlyList<LineItem> lines = PlaneFamilyShapes.GridSection(Vector3.UnitZ, 0f, UnitCell, 2, 1, 1, Gray);

        Assert.Equal(5, lines.Count);
        Assert.All(lines, l => Assert.Equal(0f, l.Start.Z, 1e-6f));
    }

    [Fact]
    public void GridSection_PlaneOutsideTheBlock_GivesNothing()
    {
        Assert.Empty(PlaneFamilyShapes.GridSection(Vector3.UnitZ, 1.5f, UnitCell, 2, 1, 1, Gray));
    }

    [Fact]
    public void GridSection_TracesLieOnThePlaneAndOnTheirWall()
    {
        // (110) plane through the middle of a 2x2x1 block: every end is on the plane and on a wall x, y or z = integer.
        Vector3 n = Vector3.Normalize(new Vector3(1, 1, 0));
        float offset = Vector3.Dot(n, new Vector3(1, 1, 0));
        IReadOnlyList<LineItem> lines = PlaneFamilyShapes.GridSection(n, offset, UnitCell, 2, 2, 1, Gray);

        // The plane x + y = 2 holds three vertical lines, each the trace of two walls (x = m and y = 2 - m), drawn once,
        // and crosses the walls z = 0 and z = 1 along the diagonal: five traces.
        Assert.Equal(5, lines.Count);
        static bool OnAWall(Vector3 p) =>
            new[] { p.X, p.Y, p.Z }.Any(c => MathF.Abs(c - MathF.Round(c)) < 1e-5f);
        foreach (LineItem line in lines)
        {
            Assert.Equal(offset, Vector3.Dot(n, line.Start), 1e-5f);
            Assert.Equal(offset, Vector3.Dot(n, line.End), 1e-5f);
            Assert.True(OnAWall(line.Start) && OnAWall(line.End));
        }
    }
}
