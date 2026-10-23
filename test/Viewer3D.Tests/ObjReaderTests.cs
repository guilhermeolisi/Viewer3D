using System.Numerics;

namespace Viewer3D.Tests;

public class ObjReaderTests
{
    // A cube of side 2 around the origin, with quads, counter-clockwise from outside.
    const string Cube = """
        # cube
        v -1 -1 -1
        v  1 -1 -1
        v  1  1 -1
        v -1  1 -1
        v -1 -1  1
        v  1 -1  1
        v  1  1  1
        v -1  1  1
        f 1 4 3 2
        f 5 6 7 8
        f 1 2 6 5
        f 2 3 7 6
        f 3 4 8 7
        f 4 1 5 8
        """;

    static ObjModel Read(string text, Func<string, TextReader?>? materials = null, ObjReadOptions? options = null) =>
        ObjReader.Read(new StringReader(text), materials, options);

    static ObjReadException Refused(string text, ObjReadOptions? options = null) =>
        Assert.Throws<ObjReadException>(() => Read(text, options: options));

    [Fact]
    public void Cube_Has12TrianglesAndClosedBounds()
    {
        ObjModel model = Read(Cube);
        Scene scene = model.ToScene();

        Assert.Equal(12, model.TriangleCount);
        Assert.Equal(8, model.VertexCount);
        Assert.Empty(model.Warnings);
        MeshItem mesh = Assert.Single(model.Meshes);
        Assert.True(mesh.DoubleSided);
        Assert.Equal(MeshLighting.KeyLight, mesh.Lighting);
        Assert.Equal(Vector3.Zero, scene.Center);
        Assert.Equal(MathF.Sqrt(3), scene.Radius, 1e-5f);
    }

    [Fact]
    public void Tetrahedron_Has4Triangles()
    {
        ObjModel model = Read("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            v 0 0 1
            f 1 3 2
            f 1 2 4
            f 1 4 3
            f 2 3 4
            """);

        Assert.Equal(4, model.TriangleCount);
        Assert.Equal(4, Assert.Single(model.Meshes).Positions.Length);
    }

    [Fact]
    public void Pentagon_FansInto3Triangles()
    {
        ObjModel model = Read("""
            v 0 0 0
            v 2 0 0
            v 3 1 0
            v 1 2 0
            v -1 1 0
            f 1 2 3 4 5
            """);

        MeshItem mesh = Assert.Single(model.Meshes);
        Assert.Equal([0, 1, 2, 0, 2, 3, 0, 3, 4], mesh.Indices);
    }

    [Fact]
    public void NegativeIndices_ResolveRelativeToEnd()
    {
        // -1 is the last vertex defined before the face, so this is the same triangle as "f 2 3 4".
        ObjModel model = Read("""
            v 9 9 9
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f -3 -2 -1
            v 5 5 5
            """);

        MeshItem mesh = Assert.Single(model.Meshes);
        Assert.Equal([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], mesh.Positions);
        Assert.Equal(5, model.VertexCount);
    }

    [Fact]
    public void CornerForms_WithTextureAndNormalIndices_AreRead()
    {
        ObjModel model = Read("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vn 0 0 1
            f 1/1/1 2/1/1 3/1/1
            f 1//1 2//1 3//1
            f 1/1 2/1 3/1
            """);

        Assert.Equal(3, model.TriangleCount);
    }

    [Fact]
    public void Mtl_KdAndD_SetFaceColorAndAlpha()
    {
        const string obj = """
            mtllib model.mtl
            v 0 0 0
            v 1 0 0
            v 0 1 0
            v 0 0 1
            usemtl red
            f 1 2 3
            usemtl glass
            f 1 2 4
            usemtl red
            f 1 3 4
            """;
        const string mtl = """
            newmtl red
            Kd 1 0 0
            newmtl glass
            Kd 0 0.5 1
            d 0.4
            """;

        ObjModel model = Read(obj, name => name == "model.mtl" ? new StringReader(mtl) : null);

        Assert.Empty(model.Warnings);
        Assert.Equal(2, model.Meshes.Count);
        Assert.Equal(new Rgba(255, 0, 0, 255), model.Meshes[0].Color);
        Assert.Equal(2, model.Meshes[0].TriangleCount);
        Assert.Equal(new Rgba(0, 128, 255, 102), model.Meshes[1].Color);
    }

    [Fact]
    public void MissingMaterial_UsesDefaultColorAndWarns()
    {
        var options = new ObjReadOptions { DefaultColor = new Rgba(1, 2, 3) };

        ObjModel model = Read("""
            mtllib absent.mtl
            v 0 0 0
            v 1 0 0
            v 0 1 0
            usemtl red
            f 1 2 3
            """, _ => null, options);

        Assert.Equal(new Rgba(1, 2, 3), Assert.Single(model.Meshes).Color);
        Assert.Equal(2, model.Warnings.Count);
    }

    [Fact]
    public void IndexOutOfRange_ThrowsWithLine()
    {
        ObjReadException e = Refused("""
            v 0 0 0
            v 1 0 0
            f 1 2 3
            """);

        Assert.Equal(ObjReadError.IndexOutOfRange, e.Reason);
        Assert.Equal(3, e.Line);
        Assert.Contains("Line 3", e.Message);
    }

    [Fact]
    public void MalformedVertexLine_ThrowsWithLine()
    {
        ObjReadException e = Refused("""
            v 0 0 0
            v 1 zero 0
            v 0 1 0
            f 1 2 3
            """);

        Assert.Equal(ObjReadError.Malformed, e.Reason);
        Assert.Equal(2, e.Line);
    }

    [Fact]
    public void FaceWithTwoCorners_IsMalformed()
    {
        ObjReadException e = Refused("""
            v 0 0 0
            v 1 0 0
            f 1 2
            """);

        Assert.Equal(ObjReadError.Malformed, e.Reason);
        Assert.Equal(3, e.Line);
    }

    [Fact]
    public void NoFaces_IsRefused()
    {
        Assert.Equal(ObjReadError.Empty, Refused("v 0 0 0").Reason);
    }

    [Fact]
    public void OverTriangleCeiling_Refuses()
    {
        ObjReadException e = Refused(Cube, new ObjReadOptions { MaxTriangles = 11 });

        Assert.Equal(ObjReadError.TooManyTriangles, e.Reason);
        Assert.Equal(12, e.Count);
        Assert.Equal(11, e.Limit);
        Assert.Contains("12 triangles", e.Message);
    }

    [Fact]
    public void OverVertexCeiling_Refuses()
    {
        ObjReadException e = Refused(Cube, new ObjReadOptions { MaxVertices = 7 });

        Assert.Equal(ObjReadError.TooManyVertices, e.Reason);
        Assert.Equal(8, e.Count);
    }

    [Fact]
    public void OverSizeCeiling_Refuses()
    {
        ObjReadException e = Refused(Cube, new ObjReadOptions { MaxFileBytes = 40 });

        Assert.Equal(ObjReadError.FileTooLarge, e.Reason);
    }

    [Fact]
    public void Canceled_Throws()
    {
        string big = string.Concat(Enumerable.Repeat("v 0 0 0\n", 5000)) + "f 1 2 3\n";
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => ObjReader.Read(new StringReader(big), cancellationToken: cancel.Token));
    }

    [Fact]
    public void Normals_OrientWinding()
    {
        // The face is written clockwise seen from +z, but its normals point to +z: the reader turns it around.
        ObjModel model = Read("""
            v 0 0 0
            v 0 1 0
            v 1 0 0
            vn 0 0 1
            f 1//1 2//1 3//1
            """);

        MeshItem mesh = Assert.Single(model.Meshes);
        Vector3 a = mesh.Positions[mesh.Indices[0]], b = mesh.Positions[mesh.Indices[1]], c = mesh.Positions[mesh.Indices[2]];
        Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), Vector3.UnitZ) > 0);
    }

    [Fact]
    public void ReadFromPath_FindsSiblingMaterialFile_AndNotOneOutsideTheFolder()
    {
        string folder = Directory.CreateTempSubdirectory("viewer3d-obj-").FullName;
        try
        {
            string inner = Directory.CreateDirectory(Path.Combine(folder, "model")).FullName;
            File.WriteAllText(Path.Combine(inner, "colors.mtl"), "newmtl blue\nKd 0 0 1\n");
            File.WriteAllText(Path.Combine(folder, "outside.mtl"), "newmtl green\nKd 0 1 0\n");
            string path = Path.Combine(inner, "model.obj");
            File.WriteAllText(path, "mtllib colors.mtl ../outside.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl blue\nf 1 2 3\nusemtl green\nf 1 3 2\n");

            ObjModel model = ObjReader.Read(path, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(new Rgba(0, 0, 255), model.Meshes[0].Color);
            Assert.Equal(ObjReadOptions.Default.DefaultColor, model.Meshes[1].Color);
            Assert.Contains(model.Warnings, w => w.Contains("../outside.mtl"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ObjRoundTrip_FrameCountsAllFaces()
    {
        // Every face of the cube read from text reaches the draw list. The camera looks along a diagonal: seen along
        // an axis, four faces of the cube are edge-on, have zero projected area and are dropped by the preparation.
        Scene scene = Read(Cube).ToScene();
        var camera = new Camera();
        camera.LookAlong(new Vector3(-1, -0.8f, -0.6f), Vector3.UnitY);
        camera.Frame(scene);

        DrawList list = new FramePreparer().Prepare(scene, camera, 300, 300);

        Assert.Equal(12, list.TriangleCount);
    }
}
