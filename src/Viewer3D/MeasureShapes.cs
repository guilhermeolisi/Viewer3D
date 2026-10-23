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

    static LineItem Arm(Vector3 from, Vector3 to, Rgba color, float width, float dash) =>
        new() { Start = from, End = to, Color = color, Width = width, Dash = dash, Pickable = false };

    static Vector3 Direction(Vector3 v, Vector3 fallback) => v.LengthSquared() > 1e-20f ? Vector3.Normalize(v) : fallback;

    static Vector3 Perpendicular(Vector3 u)
    {
        Vector3 other = MathF.Abs(u.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        return Vector3.Normalize(Vector3.Cross(u, other));
    }
}
