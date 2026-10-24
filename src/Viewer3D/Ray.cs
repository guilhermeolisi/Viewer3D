using System.Numerics;

namespace Viewer3D;

/// <summary>
/// A half line in world space, from <see cref="Origin"/> along <see cref="Direction"/> (unit length). It is what
/// <see cref="Projector.Unproject"/> returns for a point of the screen: every world point on it is drawn there.
/// </summary>
/// <param name="Origin">Start of the ray: the eye in perspective, a point in front of the scene in orthographic.</param>
/// <param name="Direction">Unit direction, away from the viewer.</param>
public readonly record struct Ray(Vector3 Origin, Vector3 Direction)
{
    /// <summary>The point at distance <paramref name="t"/> from the origin.</summary>
    public Vector3 At(float t) => Origin + t * Direction;

    /// <summary>
    /// Intersects the ray with the plane through <paramref name="point"/> with normal <paramref name="normal"/>.
    /// Returns false when the ray is parallel to the plane or the plane is behind the origin.
    /// </summary>
    public bool TryIntersectPlane(Vector3 point, Vector3 normal, out Vector3 hit)
    {
        hit = default;
        float n2 = normal.LengthSquared();
        if (n2 < 1e-30f)
            return false;
        float denominator = Vector3.Dot(Direction, normal);
        // Parallel within about a thousandth of a radian: the hit would be far away and jump with the pointer.
        if (MathF.Abs(denominator) < 1e-3f * MathF.Sqrt(n2) * Direction.Length())
            return false;
        float t = Vector3.Dot(point - Origin, normal) / denominator;
        if (t < 0)
            return false;
        hit = At(t);
        return true;
    }
}
