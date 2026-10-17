using System.Numerics;

namespace Viewer3D;

/// <summary>What is under a point of the screen.</summary>
/// <param name="Item">The scene item.</param>
/// <param name="TriangleIndex">For a mesh, the index of the triangle hit; -1 otherwise.</param>
/// <param name="Barycentric">For a mesh, the weights of the triangle's three vertices at the point (they add to 1).</param>
public sealed record PickResult(SceneItem Item, int TriangleIndex, Vector3 Barycentric)
{
    /// <summary>The identifier of <see cref="Item"/>.</summary>
    public int ItemId => Item.Id;

    /// <summary>For a mesh hit, the index of the triangle vertex nearest to the point; -1 otherwise.</summary>
    public int NearestVertex
    {
        get
        {
            if (Item is not MeshItem mesh || TriangleIndex < 0)
                return -1;
            int k = Barycentric.X >= Barycentric.Y && Barycentric.X >= Barycentric.Z ? 0 : Barycentric.Y >= Barycentric.Z ? 1 : 2;
            return mesh.Indices[3 * TriangleIndex + k];
        }
    }

    /// <summary>For a mesh hit, the 3D point hit (interpolated from the triangle's vertices); null otherwise.</summary>
    public Vector3? Point
    {
        get
        {
            if (Item is not MeshItem mesh || TriangleIndex < 0)
                return null;
            int[] idx = mesh.Indices;
            return mesh.Positions[idx[3 * TriangleIndex]] * Barycentric.X
                + mesh.Positions[idx[3 * TriangleIndex + 1]] * Barycentric.Y
                + mesh.Positions[idx[3 * TriangleIndex + 2]] * Barycentric.Z;
        }
    }

    /// <summary>For a mesh hit with <see cref="MeshItem.Scalars"/>, the scalar interpolated at the point.</summary>
    public float? Scalar
    {
        get
        {
            if (Item is not MeshItem { Scalars: { } scalars } mesh || TriangleIndex < 0)
                return null;
            int[] idx = mesh.Indices;
            return scalars[idx[3 * TriangleIndex]] * Barycentric.X
                + scalars[idx[3 * TriangleIndex + 1]] * Barycentric.Y
                + scalars[idx[3 * TriangleIndex + 2]] * Barycentric.Z;
        }
    }
}

/// <summary>Finds the front-most item under a screen point, from a prepared <see cref="DrawList"/>.</summary>
public static class Picker
{
    /// <summary>Extra pixels around thin segments that still count as a hit.</summary>
    public const float SegmentTolerance = 2f;

    /// <summary>The front-most item under (<paramref name="x"/>, <paramref name="y"/>), or null.</summary>
    public static PickResult? Pick(DrawList list, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (list.Scene is null)
            return null;
        ReadOnlySpan<uint> commands = list.Commands;
        for (int c = commands.Length - 1; c >= 0; c--)
        {
            int i = DrawList.IndexOf(commands[c]);
            switch (DrawList.KindOf(commands[c]))
            {
                case DrawKind.Triangle:
                    if (HitTriangle(list.TriXY, 6 * i, x, y, out Vector3 w))
                        return new PickResult(list.Scene.Items[list.TriItem[i]], list.TriSource[i], w);
                    break;
                case DrawKind.Circle:
                    float dx = x - list.CircleData[3 * i], dy = y - list.CircleData[3 * i + 1], r = list.CircleData[3 * i + 2];
                    if (dx * dx + dy * dy <= r * r)
                        return new PickResult(list.Scene.Items[list.CircleItem[i]], -1, default);
                    break;
                case DrawKind.Segment:
                    if (HitSegment(list.SegData, 5 * i, x, y))
                        return new PickResult(list.Scene.Items[list.SegItem[i]], -1, default);
                    break;
                case DrawKind.Ellipse:
                    if (HitEllipse(list.EllipseData, DrawList.EllipseStride * i, x, y))
                        return new PickResult(list.Scene.Items[list.EllipseItem[i]], -1, default);
                    break;
            }
        }
        return null;
    }

    static bool HitTriangle(float[] xy, int o, float x, float y, out Vector3 weights)
    {
        float ax = xy[o], ay = xy[o + 1], bx = xy[o + 2], by = xy[o + 3], cx = xy[o + 4], cy = xy[o + 5];
        float det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
        weights = default;
        if (det == 0)
            return false;
        float wa = ((by - cy) * (x - cx) + (cx - bx) * (y - cy)) / det;
        float wb = ((cy - ay) * (x - cx) + (ax - cx) * (y - cy)) / det;
        float wc = 1 - wa - wb;
        const float eps = -1e-5f;
        if (wa < eps || wb < eps || wc < eps)
            return false;
        weights = new Vector3(wa, wb, wc);
        return true;
    }

    /// <summary>
    /// Inside the outline ellipse c + L u, |u| ≤ 1: the point is inside when u = L⁻¹ (p − c) has |u| ≤ 1, which is
    /// (p − c)ᵀ (L Lᵀ)⁻¹ (p − c) ≤ 1.
    /// </summary>
    static bool HitEllipse(float[] data, int o, float x, float y)
    {
        float dx = x - data[o], dy = y - data[o + 1];
        float l11 = data[o + 2], l12 = data[o + 3], l21 = data[o + 4], l22 = data[o + 5];
        float det = l11 * l22 - l12 * l21;
        if (det == 0)
            return false;
        float u = (l22 * dx - l12 * dy) / det, v = (-l21 * dx + l11 * dy) / det;
        return u * u + v * v <= 1;
    }

    static bool HitSegment(float[] seg, int o, float x, float y)
    {
        float x1 = seg[o], y1 = seg[o + 1], x2 = seg[o + 2], y2 = seg[o + 3], half = seg[o + 4] / 2 + SegmentTolerance;
        float dx = x2 - x1, dy = y2 - y1;
        float len2 = dx * dx + dy * dy;
        float t = len2 > 0 ? Math.Clamp(((x - x1) * dx + (y - y1) * dy) / len2, 0, 1) : 0;
        float px = x1 + t * dx - x, py = y1 + t * dy - y;
        return px * px + py * py <= half * half;
    }
}
