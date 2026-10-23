using System.Numerics;

namespace Viewer3D.Crystal;

/// <summary>
/// A unit cell: edges in angstrom and angles in degrees. The Cartesian frame puts a along x, b in the xy plane and c on
/// the side of +z (the upper triangular basis used for anisotropic displacement parameters), so the scene is in
/// angstrom.
/// </summary>
public sealed record CrystalCell(double A, double B, double C, double Alpha, double Beta, double Gamma)
{
    /// <summary>
    /// True when the edges are positive and the angles make a cell with volume. Every other member returns zero vectors
    /// for an invalid cell.
    /// </summary>
    public bool IsValid => Basis().Valid;

    /// <summary>Cartesian position, in angstrom, of the fractional coordinates (x, y, z).</summary>
    public Vector3 ToCartesian(double x, double y, double z) => Basis().ToCartesian(x, y, z);

    /// <summary>The a edge as a Cartesian vector.</summary>
    public Vector3 AVector => ToCartesian(1, 0, 0);

    /// <summary>The b edge as a Cartesian vector.</summary>
    public Vector3 BVector => ToCartesian(0, 1, 0);

    /// <summary>The c edge as a Cartesian vector.</summary>
    public Vector3 CVector => ToCartesian(0, 0, 1);

    /// <summary>Unit vector along the reciprocal axis a*, perpendicular to b and c.</summary>
    public Vector3 AStar => Reciprocal(BVector, CVector);

    /// <summary>Unit vector along the reciprocal axis b*, perpendicular to c and a.</summary>
    public Vector3 BStar => Reciprocal(CVector, AVector);

    /// <summary>Unit vector along the reciprocal axis c*, perpendicular to a and b.</summary>
    public Vector3 CStar => Reciprocal(AVector, BVector);

    static Vector3 Reciprocal(Vector3 u, Vector3 v)
    {
        Vector3 n = Vector3.Cross(u, v);
        float length = n.Length();
        return length > 0 ? n / length : Vector3.Zero;
    }

    /// <summary>The basis matrix, computed once for a loop over many positions.</summary>
    internal CellBasis Basis() => CellBasis.Create(A, B, C, Alpha, Beta, Gamma);
}

/// <summary>
/// The upper triangular basis of a cell, in double precision: columns are a, b and c. Distances compared within 1e-4
/// angstrom go through here, not through <see cref="Vector3"/>.
/// </summary>
internal readonly struct CellBasis
{
    readonly double m00, m01, m02, m11, m12, m22;

    public bool Valid { get; }

    /// <summary>Shortest of the three edges; zero for an invalid cell.</summary>
    public double MinEdge { get; }

    /// <summary>Cell volume, in cubic angstrom (the product of the diagonal); zero for an invalid cell.</summary>
    public double Volume => m00 * m11 * m22;

    CellBasis(double m00, double m01, double m02, double m11, double m12, double m22, double minEdge)
    {
        this.m00 = m00;
        this.m01 = m01;
        this.m02 = m02;
        this.m11 = m11;
        this.m12 = m12;
        this.m22 = m22;
        MinEdge = minEdge;
        Valid = true;
    }

    public static CellBasis Create(double a, double b, double c, double alpha, double beta, double gamma)
    {
        double ca = Math.Cos(alpha * Math.PI / 180), cb = Math.Cos(beta * Math.PI / 180);
        double cg = Math.Cos(gamma * Math.PI / 180), sg = Math.Sin(gamma * Math.PI / 180);
        double root = 1 - ca * ca - cb * cb - cg * cg + 2 * ca * cb * cg;
        // root e (V/abc)^2; o mesmo corte do motor para cela degenerada.
        if (!(root > 1e-6) || !(a > 0) || !(b > 0) || !(c > 0) || !(sg > 0) || !double.IsFinite(a * b * c))
            return default;
        double volume = a * b * c * Math.Sqrt(root);
        return new CellBasis(a, b * cg, c * cb, b * sg, c * (ca - cb * cg) / sg, volume / (a * b * sg),
            Math.Min(a, Math.Min(b, c)));
    }

    /// <summary>
    /// Lengths of the reciprocal vectors a*, b* and c* (|b x c| / V and its turns), in 1/angstrom: the spacing of the
    /// (100), (010) and (001) planes is their inverse. Zero for an invalid cell.
    /// </summary>
    public (double A, double B, double C) ReciprocalLengths()
    {
        if (!Valid)
            return (0, 0, 0);
        double volume = Volume;
        // a = (m00, 0, 0), b = (m01, m11, 0), c = (m02, m12, m22).
        double bcx = m11 * m22, bcy = -m01 * m22, bcz = m01 * m12 - m11 * m02;
        double cay = m22 * m00, caz = -m12 * m00;
        return (Math.Sqrt(bcx * bcx + bcy * bcy + bcz * bcz) / volume,
            Math.Sqrt(cay * cay + caz * caz) / volume,
            1 / m22);
    }

    public void ToCartesian(double x, double y, double z, out double cx, out double cy, out double cz)
    {
        cx = m00 * x + m01 * y + m02 * z;
        cy = m11 * y + m12 * z;
        cz = m22 * z;
    }

    public Vector3 ToCartesian(double x, double y, double z)
    {
        if (!Valid)
            return Vector3.Zero;
        ToCartesian(x, y, z, out double cx, out double cy, out double cz);
        return new Vector3((float)cx, (float)cy, (float)cz);
    }

    /// <summary>Distance, in angstrom, between two fractional positions.</summary>
    public double Distance(double dx, double dy, double dz)
    {
        ToCartesian(dx, dy, dz, out double cx, out double cy, out double cz);
        return Math.Sqrt(cx * cx + cy * cy + cz * cz);
    }
}
