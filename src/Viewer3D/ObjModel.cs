namespace Viewer3D;

/// <summary>
/// A Wavefront OBJ model read by <see cref="ObjReader"/>: one <see cref="MeshItem"/> per material, ready to be shown.
/// </summary>
public sealed class ObjModel
{
    internal ObjModel(IReadOnlyList<MeshItem> meshes, int vertexCount, IReadOnlyList<string> warnings)
    {
        Meshes = meshes;
        VertexCount = vertexCount;
        Warnings = warnings;
        TriangleCount = meshes.Sum(m => m.TriangleCount);
    }

    /// <summary>
    /// The meshes, one per material in the order the materials were first used. They are
    /// <see cref="MeshItem.DoubleSided"/>, because the winding of an OBJ file is not reliable, and lit with
    /// <see cref="MeshLighting.KeyLight"/>.
    /// </summary>
    public IReadOnlyList<MeshItem> Meshes { get; }

    /// <summary>Number of triangles, after the polygons were split into triangles.</summary>
    public int TriangleCount { get; }

    /// <summary>Number of vertices the file declares.</summary>
    public int VertexCount { get; }

    /// <summary>
    /// What was read with a fallback, such as a material file or a material that was not found (its faces get
    /// <see cref="ObjReadOptions.DefaultColor"/>). Empty when the whole file was read as written.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>A scene with the meshes.</summary>
    public Scene ToScene() => new(Meshes);
}
