using System.Numerics;

namespace Viewer3D;

/// <summary>A cloud of points drawn as spheres whose radius and color follow a value per point.</summary>
public static class PointCloudShapes
{
    /// <summary>
    /// One <see cref="SphereItem"/> per point: at <paramref name="positions"/>[i], with the radius
    /// <paramref name="radius"/>(<paramref name="values"/>[i]) and the color <paramref name="map"/> gives for the value.
    /// A point marked in <paramref name="dashed"/> is drawn as a dashed outline only, for a point that is there but
    /// has no value to show. Item ids are <paramref name="idBase"/> plus the index of the point.
    /// </summary>
    /// <param name="positions">Point positions.</param>
    /// <param name="values">One value per point.</param>
    /// <param name="radius">Radius of the sphere of a value; must not be negative.</param>
    /// <param name="map">Color of a value.</param>
    /// <param name="dashed">Optional flag per point for a dashed outline; null draws every point shaded.</param>
    /// <param name="idBase">Id of the first point.</param>
    public static SphereItem[] Build(IReadOnlyList<Vector3> positions, IReadOnlyList<double> values, Func<double, float> radius,
        IColorMap map, IReadOnlyList<bool>? dashed = null, int idBase = 0)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(radius);
        ArgumentNullException.ThrowIfNull(map);
        if (values.Count != positions.Count)
            throw new ArgumentException("There must be one value per point.", nameof(values));
        if (dashed is not null && dashed.Count != positions.Count)
            throw new ArgumentException("There must be one flag per point.", nameof(dashed));
        var spheres = new SphereItem[positions.Count];
        for (int i = 0; i < spheres.Length; i++)
            spheres[i] = new SphereItem
            {
                Id = idBase + i,
                Center = positions[i],
                Radius = radius(values[i]),
                Color = map.Map(values[i]),
                Dashed = dashed?[i] ?? false,
            };
        return spheres;
    }
}
