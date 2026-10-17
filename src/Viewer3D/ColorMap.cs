namespace Viewer3D;

/// <summary>Maps a scalar value to a color.</summary>
public interface IColorMap
{
    /// <summary>The color of <paramref name="value"/>.</summary>
    Rgba Map(double value);
}

/// <summary>
/// A stepped map: each value takes the color of the greatest level that is less than or equal to it, and a value below
/// every level takes the color of the lowest one. This is how filled level (contour) maps color a region.
/// </summary>
public sealed class LevelColorMap : IColorMap
{
    readonly double[] levels;
    readonly Rgba[] colors;

    /// <summary>Creates the map. Levels may come in any order; each keeps its own color.</summary>
    public LevelColorMap(IReadOnlyList<double> levels, IReadOnlyList<Rgba> colors)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(colors);
        if (levels.Count != colors.Count)
            throw new ArgumentException("There must be one color per level.", nameof(colors));
        if (levels.Count == 0)
            throw new ArgumentException("At least one level is needed.", nameof(levels));
        this.levels = [.. levels];
        this.colors = [.. colors];
        Array.Sort(this.levels, this.colors);
    }

    /// <summary>The levels, ascending.</summary>
    public IReadOnlyList<double> Levels => levels;

    /// <summary>The color of each level in <see cref="Levels"/>.</summary>
    public IReadOnlyList<Rgba> Colors => colors;

    /// <inheritdoc/>
    public Rgba Map(double value)
    {
        int i = Array.BinarySearch(levels, value);
        if (i < 0)
            i = ~i - 1;
        else
            // Equal levels: take the last of them.
            while (i + 1 < levels.Length && levels[i + 1] == value) i++;
        return colors[Math.Max(i, 0)];
    }
}

/// <summary>A map given by a function.</summary>
/// <param name="map">The function.</param>
public sealed class FuncColorMap(Func<double, Rgba> map) : IColorMap
{
    readonly Func<double, Rgba> map = map ?? throw new ArgumentNullException(nameof(map));

    /// <inheritdoc/>
    public Rgba Map(double value) => map(value);
}
