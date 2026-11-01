using System.Numerics;

namespace Viewer3D;

/// <summary>Groups of <see cref="LineItem"/> built from a few numbers.</summary>
public static class LineShapes
{
    /// <summary>
    /// A polyline through <paramref name="points"/>, one <see cref="LineItem"/> per segment (n - 1 segments, or n when
    /// <paramref name="closed"/>, the last one going back to the first point). The segment of index
    /// <paramref name="highlight"/>, from point k to point k + 1, is drawn with <paramref name="highlightColor"/> and
    /// <paramref name="highlightWidth"/>, as the current step of a path; null highlights none.
    /// </summary>
    public static LineItem[] Polyline(IReadOnlyList<Vector3> points, Rgba color, float width, int? highlight,
        Rgba highlightColor, float highlightWidth, bool closed = false, int id = 0)
    {
        int n = points.Count;
        if (n < 2)
            return [];
        int count = closed ? n : n - 1;
        var lines = new LineItem[count];
        for (int k = 0; k < count; k++)
        {
            bool current = highlight == k;
            lines[k] = new LineItem
            {
                Id = id,
                Start = points[k],
                End = points[(k + 1) % n],
                Color = current ? highlightColor : color,
                Width = current ? highlightWidth : width,
            };
        }
        return lines;
    }

    /// <summary>
    /// A circle of <paramref name="radius"/> around <paramref name="center"/> in the plane normal to
    /// <paramref name="normal"/>, as <paramref name="segments"/> chords (at least 3). The lines are guides, not
    /// <see cref="SceneItem.Pickable"/>, so a click goes through them. A zero normal gives the circle in the xy plane.
    /// </summary>
    public static LineItem[] Circle(Vector3 center, Vector3 normal, float radius, Rgba color, int segments = 48,
        float width = 1f, float dash = 0, int id = 0)
    {
        segments = Math.Max(segments, 3);
        Vector3 n = normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        // An orthonormal pair (u, v) in the plane of the circle: the points are center + radius (cos t u + sin t v).
        Vector3 u = Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));
        Vector3 v = Vector3.Cross(n, u);
        var lines = new LineItem[segments];
        Vector3 previous = center + radius * u;
        for (int k = 1; k <= segments; k++)
        {
            float t = 2 * MathF.PI * k / segments;
            Vector3 next = k == segments ? center + radius * u : center + radius * (MathF.Cos(t) * u + MathF.Sin(t) * v);
            lines[k - 1] = new LineItem
            {
                Id = id,
                Start = previous,
                End = next,
                Color = color,
                Width = width,
                Dash = dash,
                Pickable = false,
            };
            previous = next;
        }
        return lines;
    }

    /// <summary>
    /// A wire sphere of <paramref name="radius"/> around <paramref name="center"/>: <paramref name="meridians"/> great
    /// circles through the z axis, at even turns of 180° / meridians, and <paramref name="parallels"/> circles normal to
    /// z at even polar angles between the poles (the poles themselves excluded). Each circle has
    /// <paramref name="segments"/> chords; the lines are guides, not <see cref="SceneItem.Pickable"/>.
    /// </summary>
    public static LineItem[] WireSphere(Vector3 center, float radius, Rgba color, int meridians = 6, int parallels = 5,
        int segments = 48, float width = 1f, int id = 0)
    {
        var lines = new List<LineItem>();
        for (int m = 0; m < meridians; m++)
        {
            float phi = MathF.PI * m / meridians;
            lines.AddRange(Circle(center, new Vector3(-MathF.Sin(phi), MathF.Cos(phi), 0), radius, color, segments, width, id: id));
        }
        for (int p = 1; p <= parallels; p++)
        {
            float theta = MathF.PI * p / (parallels + 1);
            lines.AddRange(Circle(center + radius * MathF.Cos(theta) * Vector3.UnitZ, Vector3.UnitZ, radius * MathF.Sin(theta),
                color, segments, width, id: id));
        }
        return [.. lines];
    }
}
