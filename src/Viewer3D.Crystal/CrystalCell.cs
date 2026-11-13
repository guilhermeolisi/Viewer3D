using System.Numerics;

namespace Viewer3D.Crystal;

/// <summary>
/// A unit cell: three edge vectors a, b and c in a Cartesian frame, in angstrom, kept as they were given, with the
/// edges (angstrom) and angles (degrees) they make. The scene is drawn in that frame, so a caller that has its own
/// convention for the axes passes the vectors with <see cref="FromBasis"/>; <see cref="FromParameters"/> builds them
/// from the six parameters in the frame of M. Järvinen, J. Appl. Cryst. 26 (1993) 525-531. The constructor with the
/// six parameters uses the <see cref="CrystalLattice.Triclinic"/> arrangement, and so does a <c>with</c> that changes a
/// parameter. Two cells are equal when their vectors are equal.
/// </summary>
public sealed record CrystalCell
{
    readonly double a, b, c, alpha, beta, gamma;
    readonly CellBasis basis;

    /// <summary>
    /// The cell of the six parameters, in the <see cref="CrystalLattice.Triclinic"/> arrangement of
    /// <see cref="FromParameters"/>: c along z, b in the yz plane.
    /// </summary>
    public CrystalCell(double A, double B, double C, double Alpha, double Beta, double Gamma)
        : this(A, B, C, Alpha, Beta, Gamma, CellBasis.FromParameters(A, B, C, Alpha, Beta, Gamma, CrystalLattice.Triclinic))
    {
    }

    CrystalCell(double a, double b, double c, double alpha, double beta, double gamma, CellBasis basis)
    {
        (this.a, this.b, this.c, this.alpha, this.beta, this.gamma) = (a, b, c, alpha, beta, gamma);
        this.basis = basis;
    }

    /// <summary>Length of a, in angstrom.</summary>
    public double A { get => a; init { a = value; basis = Rebuilt(); } }

    /// <summary>Length of b, in angstrom.</summary>
    public double B { get => b; init { b = value; basis = Rebuilt(); } }

    /// <summary>Length of c, in angstrom.</summary>
    public double C { get => c; init { c = value; basis = Rebuilt(); } }

    /// <summary>Angle between b and c, in degrees.</summary>
    public double Alpha { get => alpha; init { alpha = value; basis = Rebuilt(); } }

    /// <summary>Angle between a and c, in degrees.</summary>
    public double Beta { get => beta; init { beta = value; basis = Rebuilt(); } }

    /// <summary>Angle between a and b, in degrees.</summary>
    public double Gamma { get => gamma; init { gamma = value; basis = Rebuilt(); } }

    // A parameter changed by a with: the vectors are built again from the six parameters, in the triclinic arrangement.
    CellBasis Rebuilt() => CellBasis.FromParameters(a, b, c, alpha, beta, gamma, CrystalLattice.Triclinic);

    /// <summary>
    /// True when the vectors are finite, right-handed and make a cell with volume. Every other member returns zero
    /// vectors for an invalid cell.
    /// </summary>
    public bool IsValid => basis.Valid;

    /// <summary>Cartesian position, in angstrom, of the fractional coordinates (x, y, z).</summary>
    public Vector3 ToCartesian(double x, double y, double z) => basis.ToCartesian(x, y, z);

    /// <summary>Fractional coordinates of a Cartesian point in angstrom, without reducing it into the cell.</summary>
    public (double X, double Y, double Z) ToFractional(double x, double y, double z) => basis.ToFractional(x, y, z);

    /// <summary>The a edge as a Cartesian vector.</summary>
    public Vector3 AVector => ToCartesian(1, 0, 0);

    /// <summary>The b edge as a Cartesian vector.</summary>
    public Vector3 BVector => ToCartesian(0, 1, 0);

    /// <summary>The c edge as a Cartesian vector.</summary>
    public Vector3 CVector => ToCartesian(0, 0, 1);

    /// <summary>Unit vector along the reciprocal axis a*, perpendicular to b and c.</summary>
    public Vector3 AStar => basis.ReciprocalDirection(0);

    /// <summary>Unit vector along the reciprocal axis b*, perpendicular to c and a.</summary>
    public Vector3 BStar => basis.ReciprocalDirection(1);

    /// <summary>Unit vector along the reciprocal axis c*, perpendicular to a and b.</summary>
    public Vector3 CStar => basis.ReciprocalDirection(2);

    /// <summary>
    /// Writes the three edge vectors into <paramref name="destination"/>, nine numbers in angstrom: the components of a,
    /// then of b, then of c. Zeros for an invalid cell.
    /// </summary>
    public void CopyBasisTo(Span<double> destination) => basis.CopyBasisTo(destination);

    /// <summary>
    /// Writes the reciprocal vectors a*, b* and c* (the rows of the inverse of the matrix whose columns are a, b and c,
    /// without the factor 2π) into <paramref name="destination"/>, nine numbers in 1/angstrom: the components of a*,
    /// then of b*, then of c*. The normal of the plane (hkl) is h a* + k b* + l c*. Zeros for an invalid cell.
    /// </summary>
    public void CopyReciprocalBasisTo(Span<double> destination) => basis.CopyReciprocalBasisTo(destination);

    /// <summary>The basis matrix and its inverse, for a loop over many positions.</summary>
    internal CellBasis Basis() => basis;

    /// <summary>
    /// The cell of three edge vectors, nine numbers in angstrom: the components of a, then of b, then of c. The vectors
    /// are kept as given, without a rotation. They must be right-handed (a · (b × c) &gt; 0); left-handed, coplanar or
    /// not finite vectors give a cell that is not <see cref="IsValid"/>.
    /// </summary>
    public static CrystalCell FromBasis(ReadOnlySpan<double> basis)
    {
        if (basis.Length != 9)
            throw new ArgumentException("The basis has nine numbers: the components of a, then of b, then of c.", nameof(basis));
        (double a, double b, double c, double alpha, double beta, double gamma) = CellBasis.Parameters(basis);
        return new CrystalCell(a, b, c, alpha, beta, gamma, CellBasis.FromVectors(basis));
    }

    /// <summary>
    /// The cell of the six parameters (edges in angstrom, angles in degrees), with the edge vectors in the frame of
    /// M. Järvinen, J. Appl. Cryst. 26 (1993) 525-531, arranged for <paramref name="lattice"/>. The arrangement reads
    /// only the parameters its lattice leaves free (see <see cref="CrystalLattice"/>); the cell keeps the six as given.
    /// The cosines of 90 and 120 degrees are exact, so a right angle has no rounding residue.
    /// </summary>
    public static CrystalCell FromParameters(double a, double b, double c, double alpha, double beta, double gamma, CrystalLattice lattice)
        => new(a, b, c, alpha, beta, gamma, CellBasis.FromParameters(a, b, c, alpha, beta, gamma, lattice));

    /// <summary>
    /// The cell of three edge vectors given in any Cartesian frame (a simulation box, the lattice of a trajectory),
    /// kept as given. Three vectors in left-handed order give the cell of a, b and -c, which holds the same points with
    /// a right-handed basis. Coplanar vectors give a cell that is not <see cref="IsValid"/>.
    /// </summary>
    public static CrystalCell FromVectors(Vector3 a, Vector3 b, Vector3 c)
        => LatticeFrame.Create([a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z]).Cell;

    /// <summary>Equal when both are valid with the same vectors, or both invalid with the same parameters.</summary>
    public bool Equals(CrystalCell? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (basis.Valid != other.basis.Valid)
            return false;
        return basis.Valid
            ? basis.SameVectors(other.basis)
            : a.Equals(other.a) && b.Equals(other.b) && c.Equals(other.c) && alpha.Equals(other.alpha)
                && beta.Equals(other.beta) && gamma.Equals(other.gamma);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => basis.Valid ? basis.VectorsHashCode() : HashCode.Combine(a, b, c, alpha, beta, gamma);
}

/// <summary>
/// Three edge vectors in a Cartesian frame of their own, in double precision: the <see cref="CrystalCell"/> they make
/// and the fractional coordinates of a Cartesian position in that cell. When the vectors are left-handed, c is reversed
/// (and with it the third fractional coordinate), so the cell is right-handed and the points are the same.
/// </summary>
internal readonly struct LatticeFrame
{
    LatticeFrame(CrystalCell cell, bool reversed)
    {
        Cell = cell;
        Reversed = reversed;
    }

    /// <summary>False for vectors that are coplanar, too short or not finite.</summary>
    public bool Valid => Cell.IsValid;

    /// <summary>The cell of the vectors, right-handed.</summary>
    public CrystalCell Cell { get; }

    /// <summary>True when the vectors were left-handed and c was reversed.</summary>
    public bool Reversed { get; }

    /// <summary>The frame of a, b and c, nine numbers: the three components of a, then of b, then of c.</summary>
    public static LatticeFrame Create(ReadOnlySpan<double> v)
    {
        Span<double> vectors = stackalloc double[9];
        v[..9].CopyTo(vectors);
        bool reversed = CellBasis.Determinant(vectors) < 0;
        if (reversed)
        {
            vectors[6] = -vectors[6];
            vectors[7] = -vectors[7];
            vectors[8] = -vectors[8];
        }
        return new LatticeFrame(CrystalCell.FromBasis(vectors), reversed);
    }

    /// <summary>Fractional coordinates, in <see cref="Cell"/>, of a Cartesian position in the frame of the vectors.</summary>
    public (double X, double Y, double Z) ToFractional(double x, double y, double z) => Cell.ToFractional(x, y, z);
}

/// <summary>
/// The basis matrix of a cell, in double precision, with its inverse: the columns of the matrix are a, b and c, and the
/// rows of the inverse are a*, b* and c*. Distances compared within 1e-4 angstrom go through here, not through
/// <see cref="Vector3"/>.
/// </summary>
internal readonly struct CellBasis
{
    // The matrix, row by row: column j is edge j (a, b, c).
    readonly double m00, m01, m02, m10, m11, m12, m20, m21, m22;

    // The inverse, row by row: row i is the reciprocal vector i (a*, b*, c*).
    readonly double n00, n01, n02, n10, n11, n12, n20, n21, n22;

    public bool Valid { get; }

    /// <summary>Shortest of the three edges; zero for an invalid cell.</summary>
    public double MinEdge { get; }

    /// <summary>Cell volume, in cubic angstrom (the determinant of the matrix); zero for an invalid cell.</summary>
    public double Volume { get; }

    // Below this square of V / (|a| |b| |c|) the cell is taken as flat: the angles of a cell that thin are a rounding
    // residue, not a lattice.
    const double FlatRatioSquared = 1e-6;

    CellBasis(ReadOnlySpan<double> v, double volume, double minEdge)
    {
        double ax = v[0], ay = v[1], az = v[2], bx = v[3], by = v[4], bz = v[5], cx = v[6], cy = v[7], cz = v[8];
        (m00, m10, m20, m01, m11, m21, m02, m12, m22) = (ax, ay, az, bx, by, bz, cx, cy, cz);
        // Inverse by the adjugate: the rows are b x c, c x a and a x b over the determinant.
        (n00, n01, n02) = ((by * cz - bz * cy) / volume, (bz * cx - bx * cz) / volume, (bx * cy - by * cx) / volume);
        (n10, n11, n12) = ((cy * az - cz * ay) / volume, (cz * ax - cx * az) / volume, (cx * ay - cy * ax) / volume);
        (n20, n21, n22) = ((ay * bz - az * by) / volume, (az * bx - ax * bz) / volume, (ax * by - ay * bx) / volume);
        Volume = volume;
        MinEdge = minEdge;
        Valid = true;
    }

    /// <summary>a · (b × c) of nine numbers, the components of a, then of b, then of c.</summary>
    public static double Determinant(ReadOnlySpan<double> v)
        => v[0] * (v[4] * v[8] - v[5] * v[7]) - v[1] * (v[3] * v[8] - v[5] * v[6]) + v[2] * (v[3] * v[7] - v[4] * v[6]);

    /// <summary>The basis of three edge vectors as given; invalid unless they are finite, right-handed and not flat.</summary>
    public static CellBasis FromVectors(ReadOnlySpan<double> v)
    {
        double volume = Determinant(v);
        double la = Length(v[0], v[1], v[2]), lb = Length(v[3], v[4], v[5]), lc = Length(v[6], v[7], v[8]);
        if (!double.IsFinite(volume) || !(volume > 0) || !double.IsFinite(la * lb * lc))
            return default;
        double ratio = volume / (la * lb * lc);
        if (!(ratio * ratio > FlatRatioSquared))
            return default;
        return new CellBasis(v, volume, Math.Min(la, Math.Min(lb, lc)));
    }

    /// <summary>
    /// The basis of the six parameters in the frame of Järvinen (1993), arranged for <paramref name="lattice"/>.
    /// </summary>
    public static CellBasis FromParameters(double a, double b, double c, double alpha, double beta, double gamma, CrystalLattice lattice)
    {
        Span<double> v = stackalloc double[9];
        switch (lattice)
        {
            case CrystalLattice.Triclinic:
                {
                    double cosAlpha = Cos(alpha), sinAlpha = Sin(alpha, cosAlpha), cosBeta = Cos(beta), cosGamma = Cos(gamma);
                    // Giacovazzo, Fundamentals of Crystallography, eq. 2.31b: a_y = a (cos γ - cos α cos β) / sin α.
                    double ay = (cosGamma - cosAlpha * cosBeta) / sinAlpha;
                    double ax = Math.Sqrt(Math.Max(0, 1 - cosBeta * cosBeta - ay * ay));
                    Set(v, a * ax, a * ay, a * cosBeta, 0, b * sinAlpha, b * cosAlpha, 0, 0, c);
                    break;
                }
            case CrystalLattice.MonoclinicUniqueB:
                {
                    double cosBeta = Cos(beta);
                    Set(v, 0, a, 0, 0, 0, b, c * Sin(beta, cosBeta), c * cosBeta, 0);
                    break;
                }
            case CrystalLattice.MonoclinicUniqueC:
                {
                    double cosGamma = Cos(gamma);
                    Set(v, a * Sin(gamma, cosGamma), a * cosGamma, 0, 0, b, 0, 0, 0, c);
                    break;
                }
            case CrystalLattice.MonoclinicUniqueA:
                {
                    double cosAlpha = Cos(alpha);
                    Set(v, 0, 0, a, b * Sin(alpha, cosAlpha), b * cosAlpha, 0, 0, c, 0);
                    break;
                }
            case CrystalLattice.Orthorhombic:
            case CrystalLattice.Tetragonal:
            case CrystalLattice.Cubic:
                Set(v, a, 0, 0, 0, b, 0, 0, 0, c);
                break;
            case CrystalLattice.Rhombohedral:
                {
                    double cosAlpha = Cos(alpha);
                    double bx = a * Math.Sqrt(2.0 / 3.0 * (1 - cosAlpha));
                    double bz = a * Math.Sqrt(1 - 2.0 / 3.0 * (1 - cosAlpha));
                    double half = 0.5 * bx, side = Math.Sqrt(3.0) / 2.0 * bx;
                    Set(v, -half, -side, bz, bx, 0, bz, -half, side, bz);
                    break;
                }
            case CrystalLattice.Hexagonal:
                Set(v, a * Sin120, a * -0.5, 0, 0, b, 0, 0, 0, c);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(lattice), lattice, "Unknown lattice.");
        }
        return FromVectors(v);
    }

    /// <summary>Edges (angstrom) and angles (degrees) of three edge vectors, the components of a, then of b, then of c.</summary>
    public static (double A, double B, double C, double Alpha, double Beta, double Gamma) Parameters(ReadOnlySpan<double> v)
    {
        double la = Length(v[0], v[1], v[2]), lb = Length(v[3], v[4], v[5]), lc = Length(v[6], v[7], v[8]);
        return (la, lb, lc, Angle(v[3], v[4], v[5], v[6], v[7], v[8], lb, lc), Angle(v[0], v[1], v[2], v[6], v[7], v[8], la, lc),
            Angle(v[0], v[1], v[2], v[3], v[4], v[5], la, lb));
    }

    // sin 120 degrees, the double nearest sqrt(3) / 2.
    const double Sin120 = 0.8660254037844386;

    // Exact for the right angle and for 120 degrees, so the cells that have them carry no rounding residue.
    static double Cos(double degrees) => degrees switch
    {
        90.0 => 0,
        120.0 => -0.5,
        _ => Math.Cos(degrees * (Math.PI / 180)),
    };

    // The positive sine of a cell angle, from its cosine; exact for the right angle and for 120 degrees.
    static double Sin(double degrees, double cos) => degrees == 120.0 ? Sin120 : Math.Sqrt(1 - cos * cos);

    static void Set(Span<double> v, double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz)
        => (v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8]) = (ax, ay, az, bx, by, bz, cx, cy, cz);

    static double Length(double x, double y, double z) => Math.Sqrt(x * x + y * y + z * z);

    static double Angle(double ux, double uy, double uz, double vx, double vy, double vz, double lu, double lv)
    {
        double cos = (ux * vx + uy * vy + uz * vz) / (lu * lv);
        return Math.Acos(Math.Clamp(cos, -1, 1)) * 180 / Math.PI;
    }

    /// <summary>
    /// Lengths of the reciprocal vectors a*, b* and c* (the rows of the inverse), in 1/angstrom: the spacing of the
    /// (100), (010) and (001) planes is their inverse. Zero for an invalid cell.
    /// </summary>
    public (double A, double B, double C) ReciprocalLengths()
        => Valid ? (Length(n00, n01, n02), Length(n10, n11, n12), Length(n20, n21, n22)) : (0, 0, 0);

    /// <summary>Unit vector along the reciprocal vector <paramref name="row"/> (0 for a*, 1 for b*, 2 for c*).</summary>
    public Vector3 ReciprocalDirection(int row)
    {
        if (!Valid)
            return Vector3.Zero;
        (double x, double y, double z) = row switch
        {
            0 => (n00, n01, n02),
            1 => (n10, n11, n12),
            _ => (n20, n21, n22),
        };
        double length = Length(x, y, z);
        return new Vector3((float)(x / length), (float)(y / length), (float)(z / length));
    }

    public void ToCartesian(double x, double y, double z, out double cx, out double cy, out double cz)
    {
        cx = m00 * x + m01 * y + m02 * z;
        cy = m10 * x + m11 * y + m12 * z;
        cz = m20 * x + m21 * y + m22 * z;
    }

    public Vector3 ToCartesian(double x, double y, double z)
    {
        if (!Valid)
            return Vector3.Zero;
        ToCartesian(x, y, z, out double cx, out double cy, out double cz);
        return new Vector3((float)cx, (float)cy, (float)cz);
    }

    public (double X, double Y, double Z) ToFractional(double x, double y, double z)
    {
        if (!Valid)
            return (0, 0, 0);
        return (n00 * x + n01 * y + n02 * z, n10 * x + n11 * y + n12 * z, n20 * x + n21 * y + n22 * z);
    }

    /// <summary>Distance, in angstrom, between two fractional positions.</summary>
    public double Distance(double dx, double dy, double dz)
    {
        ToCartesian(dx, dy, dz, out double cx, out double cy, out double cz);
        return Math.Sqrt(cx * cx + cy * cy + cz * cz);
    }

    public void CopyBasisTo(Span<double> destination)
    {
        if (destination.Length < 9)
            throw new ArgumentException("The destination holds nine numbers.", nameof(destination));
        Set(destination, m00, m10, m20, m01, m11, m21, m02, m12, m22);
    }

    public void CopyReciprocalBasisTo(Span<double> destination)
    {
        if (destination.Length < 9)
            throw new ArgumentException("The destination holds nine numbers.", nameof(destination));
        Set(destination, n00, n01, n02, n10, n11, n12, n20, n21, n22);
    }

    public bool SameVectors(in CellBasis other)
        => m00.Equals(other.m00) && m01.Equals(other.m01) && m02.Equals(other.m02) && m10.Equals(other.m10)
            && m11.Equals(other.m11) && m12.Equals(other.m12) && m20.Equals(other.m20) && m21.Equals(other.m21)
            && m22.Equals(other.m22);

    public int VectorsHashCode()
    {
        var hash = new HashCode();
        hash.Add(m00);
        hash.Add(m01);
        hash.Add(m02);
        hash.Add(m10);
        hash.Add(m11);
        hash.Add(m12);
        hash.Add(m20);
        hash.Add(m21);
        hash.Add(m22);
        return hash.ToHashCode();
    }
}
