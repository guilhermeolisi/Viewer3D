using System.Numerics;

namespace Viewer3D.Tests;

/// <summary>The edges of a mesh (<see cref="MeshItem.Edges"/>) and its translucent faces, in <see cref="FramePreparer"/>.</summary>
public class MeshEdgesTests
{
    /// <summary>View radius 2 in a 200 px square: 50 px per scene unit, the origin at (100, 100).</summary>
    static readonly Camera Camera = new() { ViewRadius = 2 };

    /// <summary>A square facing the viewer, split along the diagonal from corner 0 to corner 2.</summary>
    static MeshItem Square(int[]? edges, Rgba? color = null, int id = 0) => new()
    {
        Id = id,
        Positions = [new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0)],
        Indices = [0, 1, 2, 0, 2, 3],
        Edges = edges,
        Color = color ?? Rgba.Gray,
    };

    static readonly int[] SquareEdges = [0, 1, 1, 2, 2, 3, 3, 0];

    [Fact]
    public void Prepare_MeshWithEdges_DrawsOnlyTheGivenEdges()
    {
        DrawList list = new FramePreparer().Prepare(new Scene([Square(SquareEdges)]), Camera, 200, 200);

        // Pega a aresta de todo triangulo: a diagonal 0-2 seria um segmento inclinado.
        Assert.True(list.SegmentCount >= 4);
        ReadOnlySpan<float> seg = list.Segments;
        for (int i = 0; i < list.SegmentCount; i++)
        {
            float dx = MathF.Abs(seg[5 * i + 2] - seg[5 * i]), dy = MathF.Abs(seg[5 * i + 3] - seg[5 * i + 1]);
            Assert.True(dx < 1.5f || dy < 1.5f, $"segment {i} is slanted: dx {dx}, dy {dy}");
        }
    }

    [Fact]
    public void Prepare_MeshWithoutEdges_HasNoSegments()
    {
        DrawList list = new FramePreparer().Prepare(new Scene([Square(null)]), Camera, 200, 200);

        Assert.Equal(2, list.TriangleCount);
        Assert.Equal(0, list.SegmentCount);
    }

    [Fact]
    public void Prepare_EdgeThroughSphere_IsCutInsideIt()
    {
        // A esfera no meio da aresta de baixo (y = -1): disco de 15 px em torno de (100, 150).
        var sphere = new SphereItem { Center = new Vector3(0, -1, 0), Radius = 0.3f };
        DrawList list = new FramePreparer().Prepare(new Scene([Square([0, 1]), sphere]), Camera, 200, 200);

        ReadOnlySpan<float> seg = list.Segments;
        Assert.True(list.SegmentCount >= 2, "the edge is drawn on both sides of the sphere");
        for (int i = 0; i < list.SegmentCount; i++)
        {
            // Meio pixel de folga: as pecas se sobrepoem 0,5 px para nao deixar ponto claro na emenda.
            float mid = (seg[5 * i] + seg[5 * i + 2]) / 2;
            Assert.True(mid < 85.5f || mid > 114.5f, $"segment {i} centered at x = {mid}, inside the sphere");
        }
    }

    [Fact]
    public void Prepare_EdgeSegments_ReportTheMeshItem()
    {
        var sphere = new SphereItem { Center = new Vector3(3, 3, 0), Radius = 0.2f };
        DrawList list = new FramePreparer().Prepare(new Scene([sphere, Square(SquareEdges)]), Camera, 200, 200);

        Assert.True(list.SegmentCount > 0);
        foreach (int item in list.SegmentItems)
            Assert.Equal(1, item);
    }

    [Fact]
    public void Prepare_EdgePieces_AreCapped()
    {
        // Uma esfera minuscula longe da aresta faria 2 / 0,01 = 200 pedacos; o teto da aresta e 8.
        var tiny = new SphereItem { Center = new Vector3(0, 1.8f, 0), Radius = 0.01f };
        DrawList list = new FramePreparer().Prepare(new Scene([Square([0, 1]), tiny]), Camera, 200, 200);

        Assert.Equal(FramePreparer.MaxEdgePieces, list.SegmentCount);
    }

    [Fact]
    public void Prepare_TranslucentDoubleSidedCube_KeepsBackFacesAndAlpha()
    {
        MeshItem solid = MeshShapes.Cube(Vector3.Zero, 0.5f, new Rgba(200, 40, 40, 100));
        var doubleSided = new MeshItem
        {
            Positions = solid.Positions,
            Indices = solid.Indices,
            Color = solid.Color,
            Lighting = solid.Lighting,
            DoubleSided = true,
        };
        var camera = new Camera { ViewRadius = 2 };
        camera.Orbit(0.5f / Camera.OrbitRadiansPerPixel, 0.4f / Camera.OrbitRadiansPerPixel);

        DrawList single = new FramePreparer().Prepare(new Scene([solid]), camera, 200, 200);
        DrawList both = new FramePreparer().Prepare(new Scene([doubleSided]), camera, 200, 200);

        // Visto de quina, tres faces de frente (6 triangulos); com dupla face, as tres do fundo tambem.
        Assert.Equal(6, single.TriangleCount);
        Assert.Equal(12, both.TriangleCount);
        foreach (uint color in both.TriangleColors)
            Assert.Equal(100u, color >> 24);
    }
}
