using System.Numerics;

namespace Viewer3D;

/// <summary>Small closed meshes built from a few numbers.</summary>
public static class MeshShapes
{
    /// <summary>Two triangles per face, counter-clockwise seen from outside; corner k is at the signs of bits 0, 1, 2.</summary>
    static readonly int[] CubeIndices =
    [
        0, 2, 1, 1, 2, 3, // -z
        4, 5, 6, 5, 7, 6, // +z
        0, 1, 4, 1, 5, 4, // -y
        2, 6, 3, 3, 6, 7, // +y
        0, 4, 2, 2, 4, 6, // -x
        1, 3, 5, 3, 7, 5, // +x
    ];

    /// <summary>
    /// A cube with edges along x, y and z, of half edge <paramref name="half"/>, in one color, lit by the key light.
    /// Twelve triangles over eight corners.
    /// </summary>
    public static MeshItem Cube(Vector3 center, float half, Rgba color, int id = 0)
    {
        var positions = new Vector3[8];
        for (int k = 0; k < 8; k++)
        {
            positions[k] = center + new Vector3(
                (k & 1) != 0 ? half : -half,
                (k & 2) != 0 ? half : -half,
                (k & 4) != 0 ? half : -half);
        }
        return new MeshItem
        {
            Id = id,
            Positions = positions,
            Indices = (int[])CubeIndices.Clone(),
            Color = color,
            Lighting = MeshLighting.KeyLight,
        };
    }
}
