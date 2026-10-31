using System.Numerics;

namespace Viewer3D.Tests;

public class PlaneFamilyShapesTests
{
    static readonly Rgba Blue = new(40, 90, 220, 90);
    static readonly Rgba Orange = new(240, 140, 20, 140);
    static readonly CellBox UnitCube = new(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    /// <summary>Area of a planar polygon given in order.</summary>
    static float Area(IReadOnlyList<Vector3> polygon)
    {
        Vector3 sum = Vector3.Zero;
        for (int k = 0; k < polygon.Count; k++)
            sum += Vector3.Cross(polygon[k], polygon[(k + 1) % polygon.Count]);
        return sum.Length() / 2;
    }

    static bool InsideBox(Vector3 p, CellBox box, float eps)
    {
        // Fractional coordinates in the box frame, each within [0, 1].
        var m = new Matrix4x4(box.A.X, box.A.Y, box.A.Z, 0, box.B.X, box.B.Y, box.B.Z, 0, box.C.X, box.C.Y, box.C.Z, 0,
            0, 0, 0, 1);
        Matrix4x4.Invert(m, out Matrix4x4 inverse);
        Vector3 f = Vector3.Transform(p - box.Origin, inverse);
        return f.X >= -eps && f.X <= 1 + eps && f.Y >= -eps && f.Y <= 1 + eps && f.Z >= -eps && f.Z <= 1 + eps;
    }

    [Fact]
    public void Build_PlanesAlongZInAUnitCube_GiveThreeSquaresOfUnitArea()
    {
        // Spacing 0.5 from k = 0 to 2: z = 0, 0.5 and 1, the two faces included.
        IReadOnlyList<MeshItem> planes = PlaneFamilyShapes.Build(Vector3.UnitZ, 0.5f, 0, 2, UnitCube, Blue, null, Orange);

        Assert.Equal(3, planes.Count);
        for (int k = 0; k < 3; k++)
        {
            Assert.Equal(4, planes[k].Positions.Length);
            Assert.Equal(1f, Area(planes[k].Positions), 1e-5f);
            Assert.All(planes[k].Positions, p => Assert.Equal(0.5f * k, p.Z, 1e-6f));
        }
    }

    [Fact]
    public void Section_111ThroughTheCenterOfACube_IsARegularHexagon()
    {
        Vector3 n = Vector3.Normalize(Vector3.One);
        Vector3[] hexagon = PlaneFamilyShapes.Section(n, Vector3.Dot(n, new Vector3(0.5f)), UnitCube);

        Assert.Equal(6, hexagon.Length);
        // Regular hexagon of side sqrt(2)/2: every side has that length and the area is 3·sqrt(3)/2·side².
        float side = MathF.Sqrt(2) / 2;
        for (int k = 0; k < 6; k++)
            Assert.Equal(side, Vector3.Distance(hexagon[k], hexagon[(k + 1) % 6]), 1e-5f);
        Assert.Equal(3 * MathF.Sqrt(3) / 2 * side * side, Area(hexagon), 1e-5f);
    }

    [Fact]
    public void Build_PlanesOutsideTheBox_GiveNoItem()
    {
        // k = -2, -1 and 3, 4 are outside [0, 1]; only k = 0, 1 and 2 cut the cube.
        IReadOnlyList<MeshItem> planes = PlaneFamilyShapes.Build(Vector3.UnitZ, 0.5f, -2, 4, UnitCube, Blue, null, Orange);

        Assert.Equal(3, planes.Count);
        Assert.Equal([2, 3, 4], planes.Select(p => p.Id));
    }

    [Fact]
    public void Section_PlaneTouchingOnlyACorner_IsEmpty()
    {
        Vector3 n = Vector3.Normalize(Vector3.One);
        Assert.Empty(PlaneFamilyShapes.Section(n, 0f, UnitCube));
        Assert.Empty(PlaneFamilyShapes.Section(n, Vector3.Dot(n, Vector3.One), UnitCube));
    }

    [Fact]
    public void Build_TheHighlightedPlane_HasItsOwnColor_AndPlanesAreNotPickable()
    {
        IReadOnlyList<MeshItem> planes = PlaneFamilyShapes.Build(Vector3.UnitX, 0.25f, 0, 4, UnitCube, Blue, 2, Orange, 10);

        Assert.Equal(5, planes.Count);
        Assert.Equal(Orange, planes[2].Color);
        Assert.All(planes.Where((_, k) => k != 2), p => Assert.Equal(Blue, p.Color));
        Assert.All(planes, p => Assert.False(p.Pickable));
        Assert.All(planes, p => Assert.True(p.DoubleSided));
        Assert.Equal(Enumerable.Range(10, 5), planes.Select(p => p.Id));
    }

    [Fact]
    public void Build_InAMonoclinicBlock_EveryVertexLiesOnItsPlaneAndInTheBox()
    {
        // Monoclinic cell (beta = 105°) in a 2x1x3 block, planes of an oblique normal.
        float beta = 105f * MathF.PI / 180f;
        var a = new Vector3(5.1f, 0, 0);
        var b = new Vector3(0, 7.3f, 0);
        var c = new Vector3(6.2f * MathF.Cos(beta), 0, 6.2f * MathF.Sin(beta));
        var block = new CellBox(new Vector3(1, -2, 0.5f), 2 * a, b, 3 * c);
        Vector3 n = Vector3.Normalize(new Vector3(0.3f, -0.5f, 0.8f));
        const float spacing = 1.7f;

        IReadOnlyList<MeshItem> planes = PlaneFamilyShapes.Build(n, spacing, -20, 20, block, Blue, null, Orange);

        Assert.NotEmpty(planes);
        float eps = 1e-4f * block.Size;
        foreach (MeshItem plane in planes)
        {
            int k = plane.Id - 20; // ids start at 0 for k = -20
            Assert.All(plane.Positions, p => Assert.Equal(k * spacing, Vector3.Dot(n, p - block.Origin), eps));
            Assert.All(plane.Positions, p => Assert.True(InsideBox(p, block, 1e-4f), $"{p} outside"));
            Assert.InRange(plane.Positions.Length, 3, 6);
        }
    }

    [Fact]
    public void Section_VerticesAreInOrder_SoTheFanCoversThePolygonOnce()
    {
        // An oblique cut of the cube: the fan of a polygon in order has the same area as the polygon.
        Vector3 n = Vector3.Normalize(new Vector3(1, 2, 0.5f));
        Vector3[] polygon = PlaneFamilyShapes.Section(n, 0.9f, UnitCube);
        MeshItem mesh = MeshShapes.Polygon(polygon, Blue);

        float fan = 0;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            Vector3 p0 = mesh.Positions[mesh.Indices[3 * t]];
            Vector3 p1 = mesh.Positions[mesh.Indices[3 * t + 1]];
            Vector3 p2 = mesh.Positions[mesh.Indices[3 * t + 2]];
            fan += Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n) / 2;
        }
        Assert.Equal(Area(polygon), MathF.Abs(fan), 1e-5f);
        // The same area holds for any order (the fan of a vertex list is its shoelace), so the order itself: walking the
        // vertices, every corner turns the same way around the normal, which only the order around the polygon does.
        Assert.Equal(5, polygon.Length);
        int turns = 0;
        for (int k = 0; k < polygon.Length; k++)
        {
            Vector3 a = polygon[k], b = polygon[(k + 1) % polygon.Length], c = polygon[(k + 2) % polygon.Length];
            turns += MathF.Sign(Vector3.Dot(Vector3.Cross(b - a, c - b), n));
        }
        Assert.Equal(polygon.Length, Math.Abs(turns));
    }
}
