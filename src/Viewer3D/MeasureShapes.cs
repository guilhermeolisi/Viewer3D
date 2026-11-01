using System.Numerics;

namespace Viewer3D;

/// <summary>
/// Overlays that show a measurement on the scene: a dashed line with its value for a distance, and two dashed arms with
/// an arc for an angle. Every item is not <see cref="SceneItem.Pickable"/>, so clicking through the overlay still picks
/// the scene under it. The caller computes the value and formats the label.
/// </summary>
public static class MeasureShapes
{
    /// <summary>Default dash length of the lines, in device-independent pixels.</summary>
    public const float DefaultDash = 5f;

    /// <summary>A dashed line from <paramref name="a"/> to <paramref name="b"/> and <paramref name="label"/> at its middle.</summary>
    public static SceneItem[] Distance(Vector3 a, Vector3 b, Rgba color, string label, float width = 1.5f, float dash = DefaultDash) =>
    [
        Arm(a, b, color, width, dash),
        new LabelItem { Position = (a + b) / 2, Text = label, Color = color, Pickable = false },
    ];

    /// <summary>
    /// A dimension line as in a technical drawing: a solid line from <paramref name="start"/> to <paramref name="end"/>,
    /// a tick of <paramref name="tickLength"/> across each end along <paramref name="tickDirection"/> (its part
    /// perpendicular to the line; an arbitrary perpendicular when it has none), and <paramref name="label"/> at the
    /// middle. Every item carries <paramref name="id"/>.
    /// </summary>
    public static SceneItem[] Dimension(Vector3 start, Vector3 end, Vector3 tickDirection, float tickLength, Rgba color,
        string label, float width = 1.5f, int id = 0)
    {
        Vector3 along = Direction(end - start, Vector3.UnitX);
        Vector3 tick = AcrossAxis(tickDirection, along, Perpendicular(along)) * (tickLength / 2);
        return
        [
            new LineItem { Id = id, Start = start, End = end, Color = color, Width = width, Pickable = false },
            new LineItem { Id = id, Start = start - tick, End = start + tick, Color = color, Width = width, Pickable = false },
            new LineItem { Id = id, Start = end - tick, End = end + tick, Color = color, Width = width, Pickable = false },
            new LabelItem { Id = id, Position = (start + end) / 2, Text = label, Color = color, Pickable = false },
        ];
    }

    /// <summary>
    /// The angle at <paramref name="vertex"/> between the directions to <paramref name="a"/> and <paramref name="c"/>:
    /// two dashed arms from the vertex, an arc of <paramref name="radius"/> around it from one arm to the other in
    /// <paramref name="segments"/> pieces, and <paramref name="label"/> just outside the middle of the arc. The arc is
    /// the shorter one; for a straight angle it bends to an arbitrary side.
    /// </summary>
    public static SceneItem[] Angle(Vector3 a, Vector3 vertex, Vector3 c, float radius, Rgba color, string label,
        int segments = 24, float width = 1.5f, float dash = DefaultDash)
    {
        segments = Math.Max(segments, 1);
        var items = new List<SceneItem>(segments + 3)
        {
            Arm(vertex, a, color, width, dash),
            Arm(vertex, c, color, width, dash),
        };
        Vector3 u = Direction(a - vertex, Vector3.UnitX);
        Vector3 w = Direction(c - vertex, u);
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(u, w), -1f, 1f));
        // An orthonormal pair in the plane of the angle: the arc is cos(t) u + sin(t) p for t from 0 to the angle.
        Vector3 p = w - Vector3.Dot(w, u) * u;
        p = p.LengthSquared() > 1e-12f ? Vector3.Normalize(p) : Perpendicular(u);
        Vector3 previous = vertex + radius * u;
        for (int k = 1; k <= segments; k++)
        {
            float t = angle * k / segments;
            Vector3 next = vertex + radius * (MathF.Cos(t) * u + MathF.Sin(t) * p);
            items.Add(new LineItem { Start = previous, End = next, Color = color, Width = width, Pickable = false });
            previous = next;
        }
        float half = angle / 2;
        Vector3 bisector = MathF.Cos(half) * u + MathF.Sin(half) * p;
        items.Add(new LabelItem { Position = vertex + 1.35f * radius * bisector, Text = label, Color = color, Pickable = false });
        return [.. items];
    }

    /// <summary>
    /// The dihedral angle p-a-b-q around the axis a-b: the axis as a dashed line, two dashed arms of
    /// <paramref name="radius"/> from the middle of the axis toward <paramref name="p"/> and <paramref name="q"/> seen
    /// along the axis (their parts perpendicular to it), the arc between the arms in <paramref name="segments"/> pieces,
    /// and <paramref name="label"/> just outside the middle of the arc. The arc is the shorter one; the caller puts the
    /// sign in the label. A point on the axis line gives an arbitrary arm perpendicular to it.
    /// </summary>
    public static SceneItem[] Dihedral(Vector3 p, Vector3 a, Vector3 b, Vector3 q, float radius, Rgba color, string label,
        int segments = 24, float width = 1.5f, float dash = DefaultDash)
    {
        segments = Math.Max(segments, 1);
        Vector3 axis = Direction(b - a, Vector3.UnitZ);
        Vector3 middle = (a + b) / 2;
        Vector3 u = AcrossAxis(p - middle, axis, Perpendicular(axis));
        Vector3 w = AcrossAxis(q - middle, axis, u);
        var items = new List<SceneItem>(segments + 4)
        {
            Arm(a, b, color, width, dash),
            Arm(middle, middle + radius * u, color, width, dash),
            Arm(middle, middle + radius * w, color, width, dash),
        };
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(u, w), -1f, 1f));
        // Both arms are perpendicular to the axis, so the arc stays in the plane through the middle normal to it.
        Vector3 v = w - Vector3.Dot(w, u) * u;
        v = v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.Normalize(Vector3.Cross(axis, u));
        Vector3 previous = middle + radius * u;
        for (int k = 1; k <= segments; k++)
        {
            float t = angle * k / segments;
            Vector3 next = middle + radius * (MathF.Cos(t) * u + MathF.Sin(t) * v);
            items.Add(new LineItem { Start = previous, End = next, Color = color, Width = width, Pickable = false });
            previous = next;
        }
        float half = angle / 2;
        Vector3 bisector = MathF.Cos(half) * u + MathF.Sin(half) * v;
        items.Add(new LabelItem { Position = middle + 1.35f * radius * bisector, Text = label, Color = color, Pickable = false });
        return [.. items];
    }

    static Vector3 AcrossAxis(Vector3 v, Vector3 axis, Vector3 fallback)
    {
        Vector3 across = v - Vector3.Dot(v, axis) * axis;
        return across.LengthSquared() > 1e-12f ? Vector3.Normalize(across) : fallback;
    }

    static LineItem Arm(Vector3 from, Vector3 to, Rgba color, float width, float dash) =>
        new() { Start = from, End = to, Color = color, Width = width, Dash = dash, Pickable = false };

    static Vector3 Direction(Vector3 v, Vector3 fallback) => v.LengthSquared() > 1e-20f ? Vector3.Normalize(v) : fallback;

    static Vector3 Perpendicular(Vector3 u)
    {
        Vector3 other = MathF.Abs(u.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        return Vector3.Normalize(Vector3.Cross(u, other));
    }
}
