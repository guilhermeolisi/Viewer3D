using System.Numerics;

namespace Viewer3D.Tests;

public class SurfaceBuilderTests
{
    /// <summary>A grid in the engine layout: N + 1 rows, one value at the poles, 2N in between.</summary>
    static double[][] Grid(int n, Func<double, double, double> f)
    {
        var grid = new double[n + 1][];
        for (int i = 0; i <= n; i++)
        {
            double theta = i * Math.PI / n;
            int count = i == 0 || i == n ? 1 : 2 * n;
            grid[i] = new double[count];
            for (int j = 0; j < count; j++)
                grid[i][j] = f(theta, j * Math.PI / n);
        }
        return grid;
    }

    static double[][] Unit(int n) => Grid(n, (_, _) => 1);

    [Theory]
    [InlineData(2, 6, 8)]
    [InlineData(4, 26, 48)]
    public void Spherical_HasOneVertexPerPoleAndTheExpectedCounts(int n, int vertices, int triangles)
    {
        MeshItem mesh = SurfaceBuilder.Spherical(Unit(n), n);

        Assert.Equal(vertices, mesh.Positions.Length);
        Assert.Equal(triangles, mesh.TriangleCount);
        Assert.Equal(SurfaceBuilder.SphericalVertexCount(n), vertices);
        Assert.Equal(SurfaceBuilder.SphericalTriangleCount(n), triangles);
        Assert.All(mesh.Indices, k => Assert.InRange(k, 0, vertices - 1));
    }

    [Fact]
    public void Spherical_PolesAreOnTheZAxis()
    {
        MeshItem mesh = SurfaceBuilder.Spherical(Grid(4, (t, _) => t < 1 ? 2 : 3), 4);

        Assert.Equal(new Vector3(0, 0, 2), mesh.Positions[0]);
        Assert.Equal(new Vector3(0, 0, -3), mesh.Positions[^1]);
    }

    [Fact]
    public void Spherical_VertexIsAtItsThetaAndPhi()
    {
        // Row 1, column 1 of N = 4: theta = 45 degrees, phi = 45 degrees.
        MeshItem mesh = SurfaceBuilder.Spherical(Unit(4), 4);

        Vector3 p = mesh.Positions[1 + 1];
        Assert.Equal(0.5f, p.X, 5);
        Assert.Equal(0.5f, p.Y, 5);
        Assert.Equal(MathF.Sqrt(2) / 2, p.Z, 5);
    }

    [Fact]
    public void Spherical_IsClosedIncludingTheSeam()
    {
        // In a closed, consistently oriented surface every directed edge appears once and its reverse once.
        MeshItem mesh = SurfaceBuilder.Spherical(Unit(4), 4);
        var edges = new HashSet<(int, int)>();
        int[] idx = mesh.Indices;
        for (int t = 0; t < idx.Length; t += 3)
        {
            Assert.True(edges.Add((idx[t], idx[t + 1])));
            Assert.True(edges.Add((idx[t + 1], idx[t + 2])));
            Assert.True(edges.Add((idx[t + 2], idx[t])));
        }

        Assert.All(edges, e => Assert.Contains((e.Item2, e.Item1), edges));
    }

    [Fact]
    public void Spherical_TrianglesFaceOutwards()
    {
        MeshItem mesh = SurfaceBuilder.Spherical(Unit(6), 6);
        int[] idx = mesh.Indices;
        for (int t = 0; t < idx.Length; t += 3)
        {
            Vector3 a = mesh.Positions[idx[t]], b = mesh.Positions[idx[t + 1]], c = mesh.Positions[idx[t + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            Assert.True(Vector3.Dot(normal, (a + b + c) / 3) > 0, $"triangle {t / 3} faces inwards");
        }
    }

    [Fact]
    public void Spherical_ColorsByTheScalarsThroughTheMap()
    {
        var map = new LevelColorMap([0, 1, 2], [new Rgba(255, 0, 0), new Rgba(0, 255, 0), new Rgba(0, 0, 255)]);
        double[][] radii = Unit(2);
        double[][] scalars = Grid(2, (t, _) => t < 0.1 ? 2.5 : t > 3 ? -1 : 1.5);

        MeshItem mesh = SurfaceBuilder.Spherical(radii, 2, scalars, map);

        Assert.Equal(new Rgba(0, 0, 255), mesh.VertexColors![0]);
        Assert.Equal(new Rgba(0, 255, 0), mesh.VertexColors[1]);
        Assert.Equal(new Rgba(255, 0, 0), mesh.VertexColors[^1]);
        Assert.Equal(1.5f, mesh.Scalars![1]);
    }

    [Fact]
    public void Spherical_WithoutScalarsColorsByTheRadius()
    {
        MeshItem mesh = SurfaceBuilder.Spherical(Grid(2, (t, _) => 1 + t), 2);

        Assert.Equal(1f, mesh.Scalars![0]);
        Assert.Equal((float)(1 + Math.PI), mesh.Scalars[^1], 5);
        Assert.Null(mesh.VertexColors);
    }

    [Fact]
    public void Spherical_RejectsAGridWithADuplicatedPole()
    {
        double[][] grid = Unit(2);
        grid[0] = [1, 1, 1, 1];

        Assert.Throws<ArgumentException>(() => SurfaceBuilder.Spherical(grid, 2));
    }

    [Fact]
    public void Decimate_KeepsTheSameAngles()
    {
        static double F(double t, double p) => 1 + 0.3 * Math.Cos(2 * t) + 0.2 * Math.Sin(t) * Math.Cos(3 * p);

        double[][] coarse = SurfaceBuilder.Decimate(Grid(360, F), 360, 4);
        double[][] direct = Grid(90, F);

        Assert.Equal(direct.Length, coarse.Length);
        for (int i = 0; i < direct.Length; i++)
        {
            Assert.Equal(direct[i].Length, coarse[i].Length);
            for (int j = 0; j < direct[i].Length; j++)
                Assert.Equal(direct[i][j], coarse[i][j], 12);
        }
    }

    [Fact]
    public void Decimate_RejectsAStepThatDoesNotDivideN()
    {
        Assert.Throws<ArgumentException>(() => SurfaceBuilder.Decimate(Unit(10), 10, 3));
    }

    [Fact]
    public void Recolor_KeepsTheGeometryArrays()
    {
        MeshItem mesh = SurfaceBuilder.Spherical(Unit(4), 4, map: new FuncColorMap(_ => Rgba.White), id: 9);

        MeshItem recolored = SurfaceBuilder.Recolor(mesh, new FuncColorMap(_ => Rgba.Black));

        Assert.Same(mesh.Positions, recolored.Positions);
        Assert.Same(mesh.Indices, recolored.Indices);
        Assert.Equal(9, recolored.Id);
        Assert.All(recolored.VertexColors!, c => Assert.Equal(Rgba.Black, c));
    }

    [Fact]
    public void Recolor_WithNewScalars_ColorsByThem()
    {
        MeshItem mesh = SurfaceBuilder.Spherical(Unit(2), 2);
        float[] heights = mesh.Positions.Select(p => p.Z).ToArray();

        MeshItem recolored = SurfaceBuilder.Recolor(mesh, heights, new LevelColorMap([-1, 0.5], [Rgba.Black, Rgba.White]));

        Assert.Same(heights, recolored.Scalars);
        Assert.Equal(Rgba.White, recolored.VertexColors![0]);
        Assert.Equal(Rgba.Black, recolored.VertexColors[^1]);
    }

    [Fact]
    public void HeightField_BuildsADoubleSidedGridFacingUp()
    {
        double[][] z = [[0, 1, 2], [3, 4, 5], [6, 7, 8]];

        MeshItem mesh = SurfaceBuilder.HeightField(z, -1, 1, 10, 20);

        Assert.True(mesh.DoubleSided);
        Assert.Equal(9, mesh.Positions.Length);
        Assert.Equal(8, mesh.TriangleCount);
        Assert.Equal(new Vector3(-1, 10, 0), mesh.Positions[0]);
        Assert.Equal(new Vector3(1, 20, 8), mesh.Positions[8]);
        Assert.Equal(new Vector3(0, 15, 4), mesh.Positions[4]);
        int[] idx = mesh.Indices;
        Vector3 n = Vector3.Cross(mesh.Positions[idx[1]] - mesh.Positions[idx[0]], mesh.Positions[idx[2]] - mesh.Positions[idx[0]]);
        Assert.True(n.Z > 0);
    }
}
