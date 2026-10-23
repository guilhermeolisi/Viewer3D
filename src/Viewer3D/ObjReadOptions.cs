namespace Viewer3D;

/// <summary>
/// Limits and defaults for <see cref="ObjReader"/>. A Wavefront OBJ file comes from outside the program, so every read
/// is bounded: a file over a limit is refused with an <see cref="ObjReadException"/>, never drawn in part.
/// </summary>
public sealed class ObjReadOptions
{
    /// <summary>The default options.</summary>
    public static ObjReadOptions Default { get; } = new();

    /// <summary>
    /// Most triangles a model may have, after its polygons are split into triangles. The default, 100,000, keeps a
    /// frame near 40 ms on the software renderer: the measurements in the README (Performance) fit about 14 ms per frame
    /// plus 0.25 microseconds per triangle.
    /// </summary>
    public int MaxTriangles { get; init; } = 100_000;

    /// <summary>
    /// Most vertices (<c>v</c> lines) a model may declare. The default is three per allowed triangle, so a "triangle
    /// soup" export, with its own three vertices for every face, is read up to <see cref="MaxTriangles"/>.
    /// </summary>
    public int MaxVertices { get; init; } = 300_000;

    /// <summary>Largest file read, in bytes (characters, for a <see cref="TextReader"/>). Applies to each material file too.</summary>
    public long MaxFileBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Color of faces with no material, or with a material that is not found.</summary>
    public Rgba DefaultColor { get; init; } = new(176, 176, 176);
}
