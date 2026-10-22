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
}
