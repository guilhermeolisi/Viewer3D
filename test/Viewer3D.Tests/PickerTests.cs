using System.Numerics;

namespace Viewer3D.Tests;

public class PickerTests
{
    static readonly Camera Camera = new() { ViewRadius = 2 };

    [Fact]
    public void Pick_ReturnsTheFrontItem()
    {
        var back = new SphereItem { Id = 1, Center = new Vector3(0, 0, -1), Radius = 0.5f };
        var front = new SphereItem { Id = 2, Center = new Vector3(0, 0, 1), Radius = 0.5f };
        DrawList list = new FramePreparer().Prepare(new Scene([front, back]), Camera, 200, 200);

        PickResult? pick = Picker.Pick(list, 100, 100);

        Assert.NotNull(pick);
        Assert.Equal(2, pick.ItemId);
        Assert.Equal(-1, pick.TriangleIndex);
    }

    static MeshItem FrontSquare(byte alpha, int id) => new()
    {
        Id = id,
        Positions = [new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1)],
        Indices = [0, 1, 2, 0, 2, 3],
        Color = new Rgba(30, 90, 200, alpha),
    };

    [Fact]
    public void Pick_OpaqueBehindTranslucent_ReturnsTheOpaque()
    {
        // The atom inside a translucent polyhedron stays clickable; with an opaque face, the face wins.
        var atom = new SphereItem { Id = 1, Center = new Vector3(0, 0, -0.5f), Radius = 0.3f };
        DrawList translucent = new FramePreparer().Prepare(new Scene([FrontSquare(100, 2), atom]), Camera, 200, 200);
        DrawList opaque = new FramePreparer().Prepare(new Scene([FrontSquare(255, 2), atom]), Camera, 200, 200);

        Assert.Equal(1, Picker.Pick(translucent, 100, 100)?.ItemId);
        Assert.Equal(2, Picker.Pick(opaque, 100, 100)?.ItemId);
    }

    [Fact]
    public void Pick_TranslucentAlone_ReturnsTheMesh()
    {
        var atom = new SphereItem { Id = 1, Center = new Vector3(0, 0, -0.5f), Radius = 0.3f };
        DrawList list = new FramePreparer().Prepare(new Scene([FrontSquare(100, 2), atom]), Camera, 200, 200);

        // Off the atom's disc, only the translucent face is under the point.
        PickResult? pick = Picker.Pick(list, 60, 60);

        Assert.NotNull(pick);
        Assert.Equal(2, pick.ItemId);
        Assert.True(pick.TriangleIndex >= 0);
    }

    /// <summary>A translucent slab: a front square at z = 0.6 and a back square at z = -0.6, both sides drawn.</summary>
    static MeshItem Slab(int id) => new()
    {
        Id = id,
        Positions =
        [
            new(-1, -1, 0.6f), new(1, -1, 0.6f), new(1, 1, 0.6f), new(-1, 1, 0.6f),
            new(-1, -1, -0.6f), new(1, -1, -0.6f), new(1, 1, -0.6f), new(-1, 1, -0.6f),
        ],
        Indices = [0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6],
        Color = new Rgba(30, 90, 200, 100),
        DoubleSided = true,
    };

    [Theory]
    [InlineData(0f, 1)]
    [InlineData(-1.2f, 2)]
    public void Pick_OpaqueBehindTranslucent_WinsOnlyInsideTheMesh(float z, int expected)
    {
        // Inside the slab the atom is picked; behind its back side the slab itself is, so the polyhedron stays clickable
        // in front of the bonds and atoms of the cells behind it.
        var atom = new SphereItem { Id = 1, Center = new Vector3(0, 0, z), Radius = 0.2f };
        DrawList list = new FramePreparer().Prepare(new Scene([Slab(2), atom]), Camera, 200, 200);

        Assert.Equal(expected, Picker.Pick(list, 100, 100)?.ItemId);
    }

    [Fact]
    public void Pick_OutsideEverything_ReturnsNull()
    {
        DrawList list = new FramePreparer().Prepare(new Scene([new SphereItem { Radius = 0.1f }]), Camera, 200, 200);

        Assert.Null(Picker.Pick(list, 5, 5));
    }

    [Fact]
    public void Pick_Mesh_GivesTriangleBarycentricNearestVertexAndScalar()
    {
        // One triangle facing the viewer: corners at (50, 150), (150, 150) and (50, 50) in pixels.
        var mesh = new MeshItem
        {
            Id = 7,
            Positions = [new(-1, -1, 0), new(1, -1, 0), new(-1, 1, 0)],
            Indices = [0, 1, 2],
            Scalars = [0f, 10f, 20f],
        };
        DrawList list = new FramePreparer().Prepare(new Scene([mesh]), Camera, 200, 200);

        PickResult? pick = Picker.Pick(list, 60, 140);

        Assert.NotNull(pick);
        Assert.Equal(7, pick.ItemId);
        Assert.Equal(0, pick.TriangleIndex);
        Assert.Equal(1f, pick.Barycentric.X + pick.Barycentric.Y + pick.Barycentric.Z, 4);
        Assert.Equal(0.8f, pick.Barycentric.X, 4);
        Assert.Equal(0.1f, pick.Barycentric.Y, 4);
        Assert.Equal(0.1f, pick.Barycentric.Z, 4);
        Assert.Equal(0, pick.NearestVertex);
        Assert.Equal(3f, pick.Scalar!.Value, 3);
        Assert.True(Vector3.Distance(new Vector3(-0.8f, -0.8f, 0), pick.Point!.Value) < 1e-4f);
    }

    [Fact]
    public void Pick_Ellipsoid_HitsInsideTheOutlineAndMissesOutside()
    {
        // Semi-eixo 1 ao longo de x (50 px) e 0,25 ao longo de y (12,5 px): um disco do maior semi-eixo acertaria os dois.
        var ellipsoid = new EllipsoidItem { Id = 9, Axes = [new(1, 0, 0), new(0, 0.25f, 0), new(0, 0, 0.25f)] };
        DrawList list = new FramePreparer().Prepare(new Scene([ellipsoid]), Camera, 200, 200);

        Assert.Equal(9, Picker.Pick(list, 145, 100)?.ItemId);
        Assert.Null(Picker.Pick(list, 100, 120));
        Assert.Null(Picker.Pick(list, 140, 108));
    }

    [Fact]
    public void Pick_Segment_HitsWithinItsWidth()
    {
        var line = new LineItem { Id = 3, Start = new Vector3(-1, 0, 0), End = new Vector3(1, 0, 0), Width = 2 };
        DrawList list = new FramePreparer().Prepare(new Scene([line]), Camera, 200, 200);

        Assert.Equal(3, Picker.Pick(list, 100, 102)?.ItemId);
        Assert.Null(Picker.Pick(list, 100, 110));
    }
}
