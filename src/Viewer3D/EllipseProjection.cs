using System.Numerics;

namespace Viewer3D;

/// <summary>
/// Geometry of an ellipsoid given by three mutually perpendicular semi-axes aᵢ. Its shape matrix is
/// E = Σ aᵢaᵢᵀ: the surface is the set of points x with (x − c)ᵀ E⁻¹ (x − c) = 1, and, since the axes are
/// perpendicular, E⁻¹ = Σ aᵢaᵢᵀ / |aᵢ|⁴. Pure functions, used to paint, cut and pick ellipsoids.
/// </summary>
public static class EllipseProjection
{
    /// <summary>
    /// The outline of the ellipsoid seen along z (orthographic): the ellipse whose shape matrix is the (x, y) block of E.
    /// Returns the three distinct entries of that symmetric 2x2 block.
    /// </summary>
    public static (float Xx, float Xy, float Yy) OutlineShape(Vector3 a0, Vector3 a1, Vector3 a2)
        => (a0.X * a0.X + a1.X * a1.X + a2.X * a2.X,
            a0.X * a0.Y + a1.X * a1.Y + a2.X * a2.Y,
            a0.Y * a0.Y + a1.Y * a1.Y + a2.Y * a2.Y);

    /// <summary>
    /// The symmetric square root L of the positive definite matrix [[xx, xy], [xy, yy]], so that the ellipse with that
    /// shape matrix is the unit disc mapped by L. The symmetric root (not Cholesky) keeps the principal directions, so a
    /// highlight at the upper left of the disc stays at the upper left of the ellipse.
    /// </summary>
    public static (float L11, float L12, float L22) SquareRoot(float xx, float xy, float yy)
    {
        float det = xx * yy - xy * xy;
        float s = MathF.Sqrt(MathF.Max(det, 0f));
        float t = MathF.Sqrt(MathF.Max(xx + yy + 2 * s, 1e-30f));
        return ((xx + s) / t, xy / t, (yy + s) / t);
    }

    /// <summary>
    /// Start of the visible half of the principal ellipse c + aⱼ cos t + aₖ sin t, for a viewer on +z: the half where the
    /// surface normal has a positive z runs from the returned angle to that angle plus π. The normal at the point of
    /// angle t is a positive multiple of âⱼ cos t / |aⱼ| + âₖ sin t / |aₖ|, whose z is R cos(t − t*) with
    /// t* = atan2(aₖ.z / |aₖ|², aⱼ.z / |aⱼ|²), so the visible half is t* ± π/2.
    /// </summary>
    public static float VisibleHalfStart(Vector3 aj, Vector3 ak)
    {
        float tStar = MathF.Atan2(ak.Z / ak.LengthSquared(), aj.Z / aj.LengthSquared());
        return tStar - MathF.PI / 2;
    }

    /// <summary>dᵀ E⁻¹ d for the ellipsoid of semi-axes <paramref name="axes"/>.</summary>
    public static float InverseShapeForm(IReadOnlyList<Vector3> axes, Vector3 d)
    {
        float sum = 0;
        for (int i = 0; i < 3; i++)
        {
            float l2 = axes[i].LengthSquared();
            float p = Vector3.Dot(d, axes[i]);
            sum += p * p / (l2 * l2);
        }
        return sum;
    }

    /// <summary>
    /// Distance from the center to the surface along the unit direction <paramref name="direction"/>:
    /// 1 / sqrt(dᵀ E⁻¹ d). Used to cut a bond where it enters the ellipsoid.
    /// </summary>
    public static float SurfaceDistance(IReadOnlyList<Vector3> axes, Vector3 direction)
        => 1f / MathF.Sqrt(InverseShapeForm(axes, Vector3.Normalize(direction)));

    /// <summary>
    /// Where the line start + t·d crosses the surface, with t in units of <paramref name="d"/>. Returns false when the
    /// line misses the ellipsoid or only touches it.
    /// </summary>
    public static bool LineCrossing(IReadOnlyList<Vector3> axes, Vector3 center, Vector3 start, Vector3 d, out float t0, out float t1)
    {
        Vector3 m = start - center;
        float a = 0, b = 0, c = -1;
        for (int i = 0; i < 3; i++)
        {
            float l2 = axes[i].LengthSquared();
            float w = 1f / (l2 * l2);
            float pd = Vector3.Dot(d, axes[i]), pm = Vector3.Dot(m, axes[i]);
            a += pd * pd * w;
            b += pm * pd * w;
            c += pm * pm * w;
        }
        t0 = t1 = 0;
        if (a <= 0)
            return false;
        float disc = b * b - a * c;
        if (disc <= 0)
            return false;
        float root = MathF.Sqrt(disc);
        t0 = (-b - root) / a;
        t1 = (-b + root) / a;
        return true;
    }
}
