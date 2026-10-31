using System.Numerics;

namespace Viewer3D;

/// <summary>
/// A parallelepiped given by a corner and its three edge vectors: a unit cell, or a block of cells when the edges are
/// multiples of the cell edges.
/// </summary>
/// <param name="Origin">The corner the three edges start from.</param>
/// <param name="A">First edge.</param>
/// <param name="B">Second edge.</param>
/// <param name="C">Third edge.</param>
public readonly record struct CellBox(Vector3 Origin, Vector3 A, Vector3 B, Vector3 C)
{
    /// <summary>The eight corners; corner k is <see cref="Origin"/> plus the edges selected by bits 0, 1 and 2.</summary>
    public Vector3[] Corners()
    {
        var corners = new Vector3[8];
        for (int k = 0; k < 8; k++)
            corners[k] = Origin + ((k & 1) != 0 ? A : Vector3.Zero) + ((k & 2) != 0 ? B : Vector3.Zero)
                + ((k & 4) != 0 ? C : Vector3.Zero);
        return corners;
    }

    /// <summary>The longest of the four diagonals, a length scale for tolerances.</summary>
    public float Size => MathF.Max(MathF.Max((A + B + C).Length(), (A + B - C).Length()),
        MathF.Max((A - B + C).Length(), (-A + B + C).Length()));
}

/// <summary>
/// A family of parallel planes cut by a box, and the traces of one plane on the walls of a block of cells: the lattice
/// planes of a crystal seen in its cells.
/// </summary>
public static class PlaneFamilyShapes
{
    /// <summary>The twelve edges of a <see cref="CellBox"/>, as pairs of corner indices.</summary>
    static readonly int[] BoxEdges = [0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7];

    /// <summary>
    /// The polygon where the plane of points p with n·(p - box.Origin) = <paramref name="offset"/> cuts the box, its
    /// vertices in order around it. Empty when the plane misses the box or only touches an edge or a corner. A plane on
    /// a face of the box gives that face.
    /// </summary>
    /// <param name="unitNormal">Unit normal of the plane.</param>
    /// <param name="offset">Signed distance of the plane from <see cref="CellBox.Origin"/>, along the normal.</param>
    /// <param name="box">The box.</param>
    public static Vector3[] Section(Vector3 unitNormal, float offset, CellBox box)
    {
        Vector3[] corners = box.Corners();
        float eps = 1e-5f * box.Size;
        var points = new List<Vector3>(6);
        void AddPoint(Vector3 p)
        {
            foreach (Vector3 q in points)
                if (Vector3.Distance(p, q) <= eps)
                    return;
            points.Add(p);
        }
        for (int e = 0; e < BoxEdges.Length; e += 2)
        {
            Vector3 p0 = corners[BoxEdges[e]], p1 = corners[BoxEdges[e + 1]];
            float s0 = Vector3.Dot(unitNormal, p0 - box.Origin) - offset;
            float s1 = Vector3.Dot(unitNormal, p1 - box.Origin) - offset;
            bool on0 = MathF.Abs(s0) <= eps, on1 = MathF.Abs(s1) <= eps;
            if (on0)
                AddPoint(p0);
            if (on1)
                AddPoint(p1);
            if (!on0 && !on1 && s0 * s1 < 0)
                AddPoint(p0 + (p1 - p0) * (s0 / (s0 - s1)));
        }
        if (points.Count < 3)
            return [];
        return OrderAround(points, unitNormal);
    }

    /// <summary>
    /// One translucent flat polygon for each plane of the family n·(p - box.Origin) = k·<paramref name="spacing"/>, k
    /// from <paramref name="first"/> to <paramref name="last"/>, cut by the box and drawn from both sides with its
    /// outline. A plane that misses the box gives no item. The planes are not pickable, so the atoms behind them can
    /// still be picked. Item ids are <paramref name="idBase"/> plus k - <paramref name="first"/>.
    /// </summary>
    /// <param name="unitNormal">Unit normal of the planes.</param>
    /// <param name="spacing">Distance between consecutive planes; must be positive.</param>
    /// <param name="first">Index of the first plane.</param>
    /// <param name="last">Index of the last plane.</param>
    /// <param name="box">The box that cuts the planes.</param>
    /// <param name="color">Color of the planes, usually with alpha below 255.</param>
    /// <param name="highlighted">Index of the plane drawn in <paramref name="highlightColor"/>, or null.</param>
    /// <param name="highlightColor">Color of the highlighted plane.</param>
    /// <param name="idBase">Id of the plane <paramref name="first"/>.</param>
    public static IReadOnlyList<MeshItem> Build(Vector3 unitNormal, float spacing, int first, int last, CellBox box,
        Rgba color, int? highlighted, Rgba highlightColor, int idBase = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacing);
        var items = new List<MeshItem>();
        for (int k = first; k <= last; k++)
        {
            Vector3[] polygon = Section(unitNormal, k * spacing, box);
            if (polygon.Length == 0)
                continue;
            MeshItem mesh = MeshShapes.Polygon(polygon, k == highlighted ? highlightColor : color, idBase + k - first);
            items.Add(new MeshItem
            {
                Id = mesh.Id,
                Pickable = false,
                Positions = mesh.Positions,
                Indices = mesh.Indices,
                Color = mesh.Color,
                DoubleSided = true,
                Edges = mesh.Edges,
            });
        }
        return items;
    }

    /// <summary>
    /// The traces of the plane n·(p - cell.Origin) = <paramref name="offset"/> on the walls of the block of
    /// <paramref name="na"/> x <paramref name="nb"/> x <paramref name="nc"/> cells built on <paramref name="cell"/>: one
    /// segment for each wall it crosses, which together draw the two-dimensional net of the plane. A wall that lies in
    /// the plane gives no segment (the other walls draw its net).
    /// </summary>
    public static IReadOnlyList<LineItem> GridSection(Vector3 unitNormal, float offset, CellBox cell, int na, int nb,
        int nc, Rgba color, float width = 1f)
    {
        var lines = new List<LineItem>();
        Vector3[] edges = [cell.A, cell.B, cell.C];
        int[] counts = [na, nb, nc];
        for (int axis = 0; axis < 3; axis++)
        {
            int i1 = (axis + 1) % 3, i2 = (axis + 2) % 3;
            Vector3 u = edges[i1] * counts[i1], v = edges[i2] * counts[i2];
            for (int m = 0; m <= counts[axis]; m++)
            {
                Vector3 corner = cell.Origin + edges[axis] * m;
                float eps = 1e-5f * cell.Size * Math.Max(na, Math.Max(nb, nc));
                if (WallTrace(unitNormal, offset, cell.Origin, corner, u, v, eps) is not (Vector3 start, Vector3 end))
                    continue;
                // Where two walls meet on the plane (an edge of the block lying in it), both give the same trace.
                bool known = lines.Any(l =>
                    (Vector3.Distance(l.Start, start) <= eps && Vector3.Distance(l.End, end) <= eps)
                    || (Vector3.Distance(l.Start, end) <= eps && Vector3.Distance(l.End, start) <= eps));
                if (!known)
                    lines.Add(new LineItem { Start = start, End = end, Color = color, Width = width, Pickable = false });
            }
        }
        return lines;
    }

    /// <summary>The segment where the plane cuts the wall corner + s·u + t·v (s, t in [0, 1]), or null.</summary>
    static (Vector3, Vector3)? WallTrace(Vector3 n, float offset, Vector3 origin, Vector3 corner, Vector3 u, Vector3 v,
        float eps)
    {
        Vector3[] p = [corner, corner + u, corner + u + v, corner + v];
        var s = new float[4];
        for (int k = 0; k < 4; k++)
            s[k] = Vector3.Dot(n, p[k] - origin) - offset;
        if (s.All(x => MathF.Abs(x) <= eps))
            return null;
        var points = new List<Vector3>(2);
        void AddPoint(Vector3 q)
        {
            foreach (Vector3 r in points)
                if (Vector3.Distance(q, r) <= eps)
                    return;
            points.Add(q);
        }
        for (int k = 0; k < 4; k++)
        {
            int k1 = (k + 1) % 4;
            bool on0 = MathF.Abs(s[k]) <= eps, on1 = MathF.Abs(s[k1]) <= eps;
            if (on0)
                AddPoint(p[k]);
            if (on1)
                AddPoint(p[k1]);
            if (!on0 && !on1 && s[k] * s[k1] < 0)
                AddPoint(p[k] + (p[k1] - p[k]) * (s[k] / (s[k] - s[k1])));
        }
        return points.Count == 2 ? (points[0], points[1]) : null;
    }

    /// <summary>The points of a convex planar set, ordered by angle around their centroid, seen along the normal.</summary>
    static Vector3[] OrderAround(List<Vector3> points, Vector3 normal)
    {
        Vector3 centroid = Vector3.Zero;
        foreach (Vector3 p in points)
            centroid += p;
        centroid /= points.Count;
        Vector3 helper = MathF.Abs(normal.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 u = Vector3.Normalize(Vector3.Cross(helper, normal));
        Vector3 v = Vector3.Cross(normal, u);
        return [.. points.OrderBy(p => MathF.Atan2(Vector3.Dot(p - centroid, v), Vector3.Dot(p - centroid, u)))];
    }
}
