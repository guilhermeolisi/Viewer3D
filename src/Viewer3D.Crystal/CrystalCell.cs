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

    /// <summary>
    /// The cell of three edge vectors given in any Cartesian frame (a simulation box, the lattice of a trajectory): their
    /// lengths and the angles between them. The cell keeps its own frame, a along x and b in the xy plane, so the vectors
    /// are taken up to a rotation. Three vectors in left-handed order give the cell of a, b and -c, which holds the same
    /// points with a right-handed basis. Coplanar vectors give a cell that is not <see cref="IsValid"/>.
    /// </summary>
    public static CrystalCell FromVectors(Vector3 a, Vector3 b, Vector3 c)
        => LatticeFrame.Create([a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z]).Cell;
}

/// <summary>
/// Three edge vectors in a Cartesian frame of their own, in double precision: the <see cref="CrystalCell"/> they make and
/// the fractional coordinates of a Cartesian position in that frame, by the inverse of the matrix of the vectors as
/// given. When the vectors are left-handed, c is reversed (and with it the third fractional coordinate), so the cell
/// is right-handed and the points are the same.
/// </summary>
internal readonly struct LatticeFrame
{
    // Inversa da matriz de colunas a, b e c como vieram, linha por linha.
    readonly double i00, i01, i02, i10, i11, i12, i20, i21, i22;

    LatticeFrame(CrystalCell cell, bool reversed, double[] inverse)
    {
        Cell = cell;
        Reversed = reversed;
        (i00, i01, i02, i10, i11, i12, i20, i21, i22) =
            (inverse[0], inverse[1], inverse[2], inverse[3], inverse[4], inverse[5], inverse[6], inverse[7], inverse[8]);
        Valid = true;
    }

    // Vetores sem volume: a cela, que nao e valida, e nada mais.
    LatticeFrame(CrystalCell cell) => Cell = cell;

    /// <summary>False for vectors that are coplanar, too short or not finite.</summary>
    public bool Valid { get; }

    /// <summary>The cell of the vectors, right-handed.</summary>
    public CrystalCell Cell { get; }

    /// <summary>True when the vectors were left-handed and c was reversed.</summary>
    public bool Reversed { get; }

    /// <summary>The frame of a, b and c, nine numbers: the three components of a, then of b, then of c.</summary>
    public static LatticeFrame Create(ReadOnlySpan<double> v)
    {
        double ax = v[0], ay = v[1], az = v[2], bx = v[3], by = v[4], bz = v[5], cx = v[6], cy = v[7], cz = v[8];
        double la = Math.Sqrt(ax * ax + ay * ay + az * az);
        double lb = Math.Sqrt(bx * bx + by * by + bz * bz);
        double lc = Math.Sqrt(cx * cx + cy * cy + cz * cz);
        // Determinante = a . (b x c), o volume com sinal.
        double det = ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);
        bool reversed = det < 0;
        double sign = reversed ? -1 : 1;
        var cell = new CrystalCell(la, lb, lc,
            Angle(bx, by, bz, sign * cx, sign * cy, sign * cz, lb, lc),
            Angle(ax, ay, az, sign * cx, sign * cy, sign * cz, la, lc),
            Angle(ax, ay, az, bx, by, bz, la, lb));
        if (!double.IsFinite(det) || !cell.IsValid)
            return new LatticeFrame(cell);
        // Inversa pela adjunta: as linhas sao b x c, c x a e a x b sobre o determinante.
        double[] inverse =
        [
            (by * cz - bz * cy) / det, (bz * cx - bx * cz) / det, (bx * cy - by * cx) / det,
            (cy * az - cz * ay) / det, (cz * ax - cx * az) / det, (cx * ay - cy * ax) / det,
            (ay * bz - az * by) / det, (az * bx - ax * bz) / det, (ax * by - ay * bx) / det,
        ];
        if (reversed)
        {
            inverse[6] = -inverse[6];
            inverse[7] = -inverse[7];
            inverse[8] = -inverse[8];
        }
        return new LatticeFrame(cell, reversed, inverse);
    }

    /// <summary>Fractional coordinates, in <see cref="Cell"/>, of a Cartesian position in the frame of the vectors.</summary>
    public (double X, double Y, double Z) ToFractional(double x, double y, double z)
        => (i00 * x + i01 * y + i02 * z, i10 * x + i11 * y + i12 * z, i20 * x + i21 * y + i22 * z);

    static double Angle(double ux, double uy, double uz, double vx, double vy, double vz, double lu, double lv)
    {
        double cos = (ux * vx + uy * vy + uz * vz) / (lu * lv);
        return Math.Acos(Math.Clamp(cos, -1, 1)) * 180 / Math.PI;
    }
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
