using System.Numerics;

namespace Viewer3D.Crystal;

/// <summary>
/// Scale factor of a displacement ellipsoid at a probability level. For a Gaussian displacement with tensor U, the
/// surface xᵀ U⁻¹ x = f² holds the atom with probability P(χ²₃ ≤ f²), so f = sqrt of the χ² quantile with three degrees
/// of freedom (Johnson 1976, ORTEP-II, ORNL-5138; Burnett and Johnson 1996, ORTEP-III, ORNL-6895). The semi-axes are
/// f·sqrt(λᵢ), with λᵢ the eigenvalues of U.
/// </summary>
public static class ProbabilityScale
{
    /// <summary>The probability levels offered, in percent.</summary>
    public static IReadOnlyList<int> Levels { get; } = [30, 50, 70, 90];

    /// <summary>The factor f for a level in percent: 30, 50, 70 or 90.</summary>
    public static double For(int level) => level switch
    {
        30 => 1.1932,
        50 => 1.5382,
        70 => 1.9144,
        90 => 2.5003,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "The level must be 30, 50, 70 or 90."),
    };
}

/// <summary>How one displacement of an atom is drawn.</summary>
internal enum DisplacementShapeKind
{
    /// <summary>All eigenvalues equal and positive: a sphere.</summary>
    Sphere,

    /// <summary>Positive eigenvalues, not all equal: an ellipsoid.</summary>
    Ellipsoid,

    /// <summary>Some eigenvalue zero or negative: the tensor has no ellipsoid, drawn as a small cube.</summary>
    Cube,

    /// <summary>No data: a small dashed sphere.</summary>
    Missing,
}

/// <summary>One shape of an atom in the thermal ellipsoid style.</summary>
internal sealed record DisplacementShape(DisplacementShapeKind Kind, float Radius, Vector3[]? Axes, Rgba Color, string? Element)
{
    /// <summary>
    /// The shape of the Cartesian tensor <paramref name="cartesian6"/> (11, 22, 33, 12, 13, 23, Å²) at scale
    /// <paramref name="factor"/>; null data gives <see cref="DisplacementShapeKind.Missing"/>.
    /// </summary>
    public static DisplacementShape From(double[]? cartesian6, double factor, double markerSize, Rgba color, string? element)
    {
        if (cartesian6 is not { Length: 6 } u || u.Any(v => !double.IsFinite(v)))
            return new DisplacementShape(DisplacementShapeKind.Missing, (float)markerSize, null, color, element);
        double[,] m =
        {
            { u[0], u[3], u[4] },
            { u[3], u[1], u[5] },
            { u[4], u[5], u[2] },
        };
        (double[] values, double[,] vectors) = SymmetricEigen3.Solve(m);
        if (values[0] <= 0)
            return new DisplacementShape(DisplacementShapeKind.Cube, (float)markerSize, null, color, element);
        double max = values[2];
        if (values[2] - values[0] <= 1e-9 * max)
            return new DisplacementShape(DisplacementShapeKind.Sphere, (float)(factor * Math.Sqrt(values[1])), null, color, element);
        var axes = new Vector3[3];
        for (int k = 0; k < 3; k++)
        {
            double length = factor * Math.Sqrt(values[k]);
            axes[k] = new Vector3((float)(vectors[0, k] * length), (float)(vectors[1, k] * length), (float)(vectors[2, k] * length));
        }
        return new DisplacementShape(DisplacementShapeKind.Ellipsoid, (float)(factor * Math.Sqrt(max)), axes, color, element);
    }

    /// <summary>Distance from the center to the surface along <paramref name="direction"/>, to cut a bond there.</summary>
    public float Reach(Vector3 direction) => Kind == DisplacementShapeKind.Ellipsoid
        ? EllipseProjection.SurfaceDistance(Axes!, direction)
        : Radius;
}

/// <summary>Eigenvalues and eigenvectors of a symmetric 3x3 matrix, by cyclic Jacobi rotations.</summary>
internal static class SymmetricEigen3
{
    /// <summary>Eigenvalues in increasing order, and the unit eigenvectors as the columns of the second result.</summary>
    public static (double[] Values, double[,] Vectors) Solve(double[,] matrix)
    {
        var a = (double[,])matrix.Clone();
        var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 50; sweep++)
        {
            double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
            double scale = Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]);
            if (off <= 1e-15 * scale || off == 0)
                break;
            for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                    Rotate(a, v, p, q);
        }
        int[] order = [0, 1, 2];
        Array.Sort(order, (x, y) => a[x, x].CompareTo(a[y, y]));
        var values = new double[3];
        var vectors = new double[3, 3];
        for (int k = 0; k < 3; k++)
        {
            values[k] = a[order[k], order[k]];
            for (int r = 0; r < 3; r++)
                vectors[r, k] = v[r, order[k]];
        }
        return (values, vectors);
    }

    static void Rotate(double[,] a, double[,] v, int p, int q)
    {
        double apq = a[p, q];
        if (apq == 0)
            return;
        double theta = (a[q, q] - a[p, p]) / (2 * apq);
        double t = Math.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
        double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
        for (int k = 0; k < 3; k++)
        {
            double akp = a[k, p], akq = a[k, q];
            a[k, p] = c * akp - s * akq;
            a[k, q] = s * akp + c * akq;
        }
        for (int k = 0; k < 3; k++)
        {
            double apk = a[p, k], aqk = a[q, k];
            a[p, k] = c * apk - s * aqk;
            a[q, k] = s * apk + c * aqk;
        }
        for (int k = 0; k < 3; k++)
        {
            double vkp = v[k, p], vkq = v[k, q];
            v[k, p] = c * vkp - s * vkq;
            v[k, q] = s * vkp + c * vkq;
        }
    }
}
