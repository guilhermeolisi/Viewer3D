using System.Numerics;

namespace Viewer3D;

/// <summary>A direction shown by the axis indicator (for example a crystallographic axis).</summary>
/// <param name="Direction">World direction; only its orientation matters, the drawn length is fixed.</param>
/// <param name="Label">Short name drawn at the arrow tip ("a", "b", "c").</param>
/// <param name="Color">Color; null lets the viewer pick the color of its position in the list.</param>
public sealed record AxisDirection(Vector3 Direction, string Label, Rgba? Color = null);

/// <summary>One arrow of the axis indicator, in screen pixels.</summary>
/// <param name="Index">Position of the axis in the list it came from.</param>
/// <param name="Start">Arrow start (the indicator origin).</param>
/// <param name="End">Arrow tip.</param>
/// <param name="LabelPosition">Where the label goes, a little beyond the tip.</param>
/// <param name="IsPoint">True when the axis points almost straight at (or away from) the viewer: draw a dot.</param>
/// <param name="Depth">View z of the unit direction (bigger is nearer).</param>
public readonly record struct AxisGlyph(int Index, Vector2 Start, Vector2 End, Vector2 LabelPosition, bool IsPoint, float Depth);

/// <summary>
/// The small axis figure fixed in a corner of the viewer. It uses only the camera rotation, so zoom and pan never
/// move or resize it, and every axis is drawn with the same length, whatever its length in the scene.
/// </summary>
public static class AxisIndicator
{
    /// <summary>Below this projected length (as a fraction of the arrow length) an axis is drawn as a dot.</summary>
    public const float PointThreshold = 0.08f;

    /// <summary>
    /// The arrows for <paramref name="axes"/> under <paramref name="orientation"/>, with their origin at
    /// <paramref name="origin"/> and length <paramref name="length"/> pixels, ordered back to front.
    /// </summary>
    public static AxisGlyph[] Compute(IReadOnlyList<AxisDirection> axes, Quaternion orientation, Vector2 origin, float length)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var glyphs = new List<AxisGlyph>(axes.Count);
        for (int i = 0; i < axes.Count; i++)
        {
            Vector3 d = axes[i].Direction;
            if (d.LengthSquared() < 1e-20f)
                continue;
            Vector3 v = Vector3.Transform(Vector3.Normalize(d), orientation);
            var screen = new Vector2(v.X, -v.Y);
            bool isPoint = screen.Length() < PointThreshold;
            Vector2 end = origin + screen * length;
            Vector2 label = isPoint
                ? origin + new Vector2(0.18f * length, -0.18f * length)
                : end + Vector2.Normalize(screen) * 0.22f * length;
            glyphs.Add(new AxisGlyph(i, origin, isPoint ? origin : end, label, isPoint, v.Z));
        }
        glyphs.Sort((p, q) => p.Depth.CompareTo(q.Depth));
        return [.. glyphs];
    }
}
