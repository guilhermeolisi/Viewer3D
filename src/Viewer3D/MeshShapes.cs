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

    /// <summary>
    /// A 3D arrow from <paramref name="start"/> to <paramref name="end"/>: a cylinder of radius
    /// <paramref name="shaftRadius"/> closed at the start, and a cone of base radius <paramref name="headRadius"/> and
    /// length <paramref name="headLength"/> whose tip is at <paramref name="end"/>. When the arrow is shorter than the
    /// head, only the cone is drawn. Lit by the key light. Returns an empty mesh when the two points coincide.
    /// </summary>
    public static MeshItem Arrow(Vector3 start, Vector3 end, float shaftRadius, float headRadius, float headLength,
        Rgba color, int segments = 16, int id = 0)
    {
        Vector3 axis = end - start;
        float length = axis.Length();
        if (length <= 0f || segments < 3)
            return new MeshItem { Id = id, Positions = [], Indices = [], Color = color, Lighting = MeshLighting.KeyLight };
        Vector3 d = axis / length;
        (Vector3 u, Vector3 v) = Perpendiculars(d);
        float head = MathF.Min(headLength, length);
        Vector3 headBase = end - d * head;

        // Vertices: shaft bottom ring, shaft top ring, head base ring, then the bottom center, head center and tip.
        int n = segments;
        var positions = new Vector3[3 * n + 3];
        for (int k = 0; k < n; k++)
        {
            float t = 2 * MathF.PI * k / n;
            Vector3 radial = MathF.Cos(t) * u + MathF.Sin(t) * v;
            positions[k] = start + shaftRadius * radial;
            positions[n + k] = headBase + shaftRadius * radial;
            positions[2 * n + k] = headBase + headRadius * radial;
        }
        int bottom = 3 * n, headCenter = 3 * n + 1, tip = 3 * n + 2;
        positions[bottom] = start;
        positions[headCenter] = headBase;
        positions[tip] = end;

        var indices = new List<int>(12 * n);
        for (int k = 0; k < n; k++)
        {
            int k1 = (k + 1) % n;
            if (head < length)
            {
                // Side of the shaft (u, v, d right handed, so the order is counter-clockwise from outside), and its cap.
                indices.AddRange([k, k1, n + k, k1, n + k1, n + k]);
                indices.AddRange([bottom, k1, k]);
            }
            // Base of the head, facing back, and its side.
            indices.AddRange([headCenter, 2 * n + k1, 2 * n + k]);
            indices.AddRange([2 * n + k, 2 * n + k1, tip]);
        }
        return new MeshItem
        {
            Id = id,
            Positions = positions,
            Indices = [.. indices],
            Color = color,
            Lighting = MeshLighting.KeyLight,
        };
    }

    /// <summary>
    /// A flat polygon through <paramref name="vertices"/>, in order, filled as a fan from the first vertex (right for a
    /// convex polygon), drawn from both sides, with its outline as <see cref="MeshItem.Edges"/>. A color with alpha below
    /// 255 makes it translucent, as a plane drawn through a structure. Fewer than three vertices give an empty mesh.
    /// </summary>
    public static MeshItem Polygon(IReadOnlyList<Vector3> vertices, Rgba color, int id = 0)
    {
        int n = vertices.Count;
        if (n < 3)
            return new MeshItem { Id = id, Positions = [], Indices = [], Color = color, DoubleSided = true };
        var indices = new int[3 * (n - 2)];
        for (int k = 1; k < n - 1; k++)
        {
            indices[3 * (k - 1)] = 0;
            indices[3 * (k - 1) + 1] = k;
            indices[3 * (k - 1) + 2] = k + 1;
        }
        var edges = new int[2 * n];
        for (int k = 0; k < n; k++)
        {
            edges[2 * k] = k;
            edges[2 * k + 1] = (k + 1) % n;
        }
        return new MeshItem
        {
            Id = id,
            Positions = [.. vertices],
            Indices = indices,
            Color = color,
            DoubleSided = true,
            Edges = edges,
        };
    }

    /// <summary>Two unit vectors perpendicular to <paramref name="d"/> and to each other, with u x v = d.</summary>
    static (Vector3 U, Vector3 V) Perpendiculars(Vector3 d)
    {
        Vector3 helper = MathF.Abs(d.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 u = Vector3.Normalize(Vector3.Cross(helper, d));
        Vector3 v = Vector3.Cross(d, u);
        return (u, v);
    }
}
