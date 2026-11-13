using System.Numerics;

namespace Viewer3D.Crystal.Tests;

public class CrystalLatticeHelperTests
{
    // Metric components are products of edges up to ~10 angstrom, so of order 1e2 square angstrom, where one ulp is
    // ~1.4e-14; a few roundings in the cosines and the products stay below 1e-13. 1e-12 square angstrom is that bound
    // with a margin of ten.
    const double MetricTolerance = 1e-12;

    public static TheoryData<CrystalLattice, double, double, double, double, double, double> Cells => new()
    {
        { CrystalLattice.Triclinic, 4, 5, 6, 80, 95, 110 },
        { CrystalLattice.Triclinic, 7.3, 8.1, 9.2, 101.2, 97.4, 63.5 },
        { CrystalLattice.MonoclinicUniqueB, 7.1, 5.3, 9.4, 90, 100, 90 },
        { CrystalLattice.MonoclinicUniqueC, 7.1, 5.3, 9.4, 90, 90, 104 },
        { CrystalLattice.MonoclinicUniqueA, 7.1, 5.3, 9.4, 112, 90, 90 },
        { CrystalLattice.Orthorhombic, 4, 5, 6, 90, 90, 90 },
        { CrystalLattice.Tetragonal, 4, 4, 6, 90, 90, 90 },
        { CrystalLattice.Cubic, 5.463, 5.463, 5.463, 90, 90, 90 },
        { CrystalLattice.Rhombohedral, 5, 5, 5, 75, 75, 75 },
        { CrystalLattice.Rhombohedral, 5, 5, 5, 104, 104, 104 },
        { CrystalLattice.Hexagonal, 3.2094, 3.2094, 5.2108, 90, 90, 120 },
        // Close to flat: (V/abc)^2 = 0.0076, well above the cut at 1e-6.
        { CrystalLattice.Triclinic, 4, 4, 4, 119, 119, 119 },
    };

    [Theory]
    [MemberData(nameof(Cells))]
    public void FromParameters_ShouldGiveVectorsWhoseMetricIsTheCellMetric(CrystalLattice lattice, double a, double b, double c,
        double alpha, double beta, double gamma)
    {
        CrystalCell cell = CrystalCell.FromParameters(a, b, c, alpha, beta, gamma, lattice);
        double[] m = Vectors(cell);

        Assert.True(cell.IsValid);
        // Oraculo: analitico, M^T M = G, the metric tensor written from the six parameters (Giacovazzo, Fundamentals of
        // Crystallography, eq. 2.13); the second path is the right-handedness, a . (b x c) = V > 0.
        double[,] g = Metric(a, b, c, alpha, beta, gamma);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                Assert.Equal(g[i, j], m[3 * i] * m[3 * j] + m[3 * i + 1] * m[3 * j + 1] + m[3 * i + 2] * m[3 * j + 2], MetricTolerance);
        Assert.True(Determinant(m) > 0);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void CopyReciprocalBasisTo_ShouldGiveTheInverseOfTheVectors(CrystalLattice lattice, double a, double b, double c,
        double alpha, double beta, double gamma)
    {
        CrystalCell cell = CrystalCell.FromParameters(a, b, c, alpha, beta, gamma, lattice);
        double[] m = Vectors(cell);
        double[] r = new double[9];

        cell.CopyReciprocalBasisTo(r);

        // Oraculo: caso limite, a*_i . a_j = delta_ij (reciprocal without 2 pi); products of order 1, so 1e-13.
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                Assert.Equal(i == j ? 1 : 0, r[3 * i] * m[3 * j] + r[3 * i + 1] * m[3 * j + 1] + r[3 * i + 2] * m[3 * j + 2], 1e-13);
    }

    [Fact]
    public void FromParameters_ShouldPlaceTheAxes_AsJarvinen()
    {
        // Oraculo: caso limite, the axes of each arrangement (Jarvinen, J. Appl. Cryst. 26 (1993) 525-531), exact where
        // the arrangement writes a zero.
        double[] tri = Vectors(CrystalCell.FromParameters(4, 5, 6, 80, 95, 110, CrystalLattice.Triclinic));
        Assert.Equal(new double[] { 0, 0, 6 }, tri[6..9]);
        Assert.Equal(0, tri[3]);
        Assert.True(tri[0] > 0 && tri[4] > 0);

        double[] monoB = Vectors(CrystalCell.FromParameters(7.1, 5.3, 9.4, 90, 100, 90, CrystalLattice.MonoclinicUniqueB));
        Assert.Equal(new double[] { 0, 7.1, 0, 0, 0, 5.3 }, monoB[0..6]);
        Assert.Equal(0, monoB[8]);

        double[] monoC = Vectors(CrystalCell.FromParameters(7.1, 5.3, 9.4, 90, 90, 104, CrystalLattice.MonoclinicUniqueC));
        Assert.Equal(new double[] { 0, 5.3, 0, 0, 0, 9.4 }, monoC[3..9]);
        Assert.Equal(0, monoC[2]);

        double[] monoA = Vectors(CrystalCell.FromParameters(7.1, 5.3, 9.4, 112, 90, 90, CrystalLattice.MonoclinicUniqueA));
        Assert.Equal(new double[] { 0, 0, 7.1 }, monoA[0..3]);
        Assert.Equal(new double[] { 0, 9.4, 0 }, monoA[6..9]);
        Assert.Equal(0, monoA[5]);

        foreach (CrystalLattice lattice in new[] { CrystalLattice.Orthorhombic, CrystalLattice.Tetragonal, CrystalLattice.Cubic })
            Assert.Equal(new double[] { 4, 0, 0, 0, 5, 0, 0, 0, 6 }, Vectors(CrystalCell.FromParameters(4, 5, 6, 90, 90, 90, lattice)));

        double[] hex = Vectors(CrystalCell.FromParameters(3.2094, 3.2094, 5.2108, 90, 90, 120, CrystalLattice.Hexagonal));
        Assert.Equal(new double[] { 0, 3.2094, 0, 0, 0, 5.2108 }, hex[3..9]);
        Assert.Equal(0, hex[2]);
        // a is 30 degrees below x.
        Assert.Equal(-30, Math.Atan2(hex[1], hex[0]) * 180 / Math.PI, 1e-12);

        double[] rho = Vectors(CrystalCell.FromParameters(5, 5, 5, 75, 75, 75, CrystalLattice.Rhombohedral));
        // [111] along z, b in the xz plane.
        Assert.Equal(0, rho[0] + rho[3] + rho[6], 1e-14);
        Assert.Equal(0, rho[1] + rho[4] + rho[7], 1e-14);
        Assert.Equal(0, rho[4]);
    }

    [Fact]
    public void Triclinic_ShouldPutAStarAlongX()
    {
        // b and c lie in the yz plane, so a*, perpendicular to both, is along x (Jarvinen's e1 parallel to a*).
        double[] r = new double[9];

        CrystalCell.FromParameters(7.3, 8.1, 9.2, 101.2, 97.4, 63.5, CrystalLattice.Triclinic).CopyReciprocalBasisTo(r);

        // Oraculo: caso limite, the zeros of b and c in x are exact, so are those of a* in y and z.
        Assert.True(r[0] > 0);
        Assert.Equal(0, r[1]);
        Assert.Equal(0, r[2]);
    }

    [Fact]
    public void RightAngles_ShouldCarryNoRoundingResidue()
    {
        // cos 90 degrees in double is 6.1e-17, not 0; the arrangement takes it as exact, so a cubic cell is diagonal to the
        // bit, whichever arrangement builds it.
        double[] expected = [5.463, 0, 0, 0, 5.463, 0, 0, 0, 5.463];

        Assert.Equal(expected, Vectors(new CrystalCell(5.463, 5.463, 5.463, 90, 90, 90)));
        Assert.Equal(expected, Vectors(CrystalCell.FromParameters(5.463, 5.463, 5.463, 90, 90, 90, CrystalLattice.Cubic)));
        double[] hex = Vectors(CrystalCell.FromParameters(3, 3, 5, 90, 90, 120, CrystalLattice.MonoclinicUniqueC));
        Assert.Equal(-1.5, hex[1]);
        Assert.Equal(0.8660254037844386 * 3, hex[0]);
    }

    [Fact]
    public void FromBasis_ShouldKeepTheVectorsAsGiven_AndTheirParameters()
    {
        // A cell turned away from every axis: the vectors come back exactly, and the parameters are their lengths and angles.
        double[] given = [3.1, 0.4, -0.7, -1.2, 4.5, 0.9, 0.3, -0.8, 5.6];

        CrystalCell cell = CrystalCell.FromBasis(given);

        Assert.True(cell.IsValid);
        Assert.Equal(given, Vectors(cell));
        Assert.Equal(Math.Sqrt(3.1 * 3.1 + 0.4 * 0.4 + 0.7 * 0.7), cell.A, 1e-14);
        double cosGamma = (3.1 * -1.2 + 0.4 * 4.5 + -0.7 * 0.9) / (cell.A * cell.B);
        Assert.Equal(Math.Acos(cosGamma) * 180 / Math.PI, cell.Gamma, 1e-12);
        (double x, double y, double z) = cell.ToFractional(given[0] + given[3], given[1] + given[4], given[2] + given[5]);
        Assert.Equal(1, x, 1e-14);
        Assert.Equal(1, y, 1e-14);
        Assert.Equal(0, z, 1e-14);
    }

    [Fact]
    public void FromBasis_ShouldNotBeValid_ForLeftHandedFlatOrBadVectors()
    {
        Assert.False(CrystalCell.FromBasis([4, 0, 0, 0, 5, 0, 0, 0, -6]).IsValid);
        Assert.False(CrystalCell.FromBasis([4, 0, 0, 0, 5, 0, 1, 1, 0]).IsValid);
        Assert.False(CrystalCell.FromBasis([4, 0, 0, 0, 5, 0, 0, 0, double.NaN]).IsValid);
        Assert.Equal(Vector3.Zero, CrystalCell.FromBasis([4, 0, 0, 0, 5, 0, 0, 0, -6]).ToCartesian(0.5, 0.5, 0.5));
        Assert.Throws<ArgumentException>(() => CrystalCell.FromBasis([4, 0, 0, 0, 5, 0, 0, 0]));
    }

    [Fact]
    public void FromVectors_ShouldKeepTheOrientation()
    {
        var a = new Vector3(3, 1, 0.5f);
        var b = new Vector3(-0.5f, 4, 1);
        var c = new Vector3(0.2f, -0.6f, 5);

        CrystalCell cell = CrystalCell.FromVectors(a, b, c);

        Assert.Equal(a, cell.AVector);
        Assert.Equal(b, cell.BVector);
        Assert.Equal(c, cell.CVector);
    }

    [Fact]
    public void Equality_ShouldGoByTheVectors()
    {
        var parameters = new CrystalCell(5.463, 5.463, 5.463, 90, 90, 90);

        Assert.Equal(parameters, CrystalCell.FromBasis([5.463, 0, 0, 0, 5.463, 0, 0, 0, 5.463]));
        Assert.Equal(parameters.GetHashCode(), CrystalCell.FromBasis([5.463, 0, 0, 0, 5.463, 0, 0, 0, 5.463]).GetHashCode());
        // The same six parameters in another arrangement are another cell: the vectors differ (triclinic puts c along z,
        // monoclinic unique axis b puts b along z). In the hexagonal cell the two arrangements coincide, so it is not used here.
        Assert.NotEqual(new CrystalCell(7.1, 5.3, 9.4, 90, 100, 90), CrystalCell.FromParameters(7.1, 5.3, 9.4, 90, 100, 90, CrystalLattice.MonoclinicUniqueB));
        Assert.NotEqual(parameters, CrystalCell.FromBasis([0, 5.463, 0, 0, 0, 5.463, 5.463, 0, 0]));
    }

    [Fact]
    public void With_ShouldBuildTheVectorsAgain_InTheTriclinicArrangement()
    {
        CrystalCell mono = CrystalCell.FromParameters(7.1, 5.3, 9.4, 90, 100, 90, CrystalLattice.MonoclinicUniqueB);

        CrystalCell longer = mono with { A = 7.2 };

        Assert.Equal(new CrystalCell(7.2, 5.3, 9.4, 90, 100, 90), longer);
        Assert.Equal(7.2, longer.A);
        Assert.Equal(7.2, longer.AVector.Length(), 1e-5);
    }

    [Fact]
    public void CopyTo_ShouldRefuseAShortDestination()
    {
        var cell = new CrystalCell(4, 5, 6, 90, 90, 90);

        Assert.Throws<ArgumentException>(() => cell.CopyBasisTo(new double[8]));
        Assert.Throws<ArgumentException>(() => cell.CopyReciprocalBasisTo(new double[8]));
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void RotatedBasis_ShouldKeepDistancesVolumeAndReciprocal(CrystalLattice lattice, double a, double b, double c,
        double alpha, double beta, double gamma)
    {
        CrystalCell cell = CrystalCell.FromParameters(a, b, c, alpha, beta, gamma, lattice);
        double[,] r = Rotation(1, 2, 3, 37);
        double[] m = Vectors(cell);
        double[] turned = new double[9];
        for (int edge = 0; edge < 3; edge++)
            for (int i = 0; i < 3; i++)
                turned[3 * edge + i] = r[i, 0] * m[3 * edge] + r[i, 1] * m[3 * edge + 1] + r[i, 2] * m[3 * edge + 2];

        CrystalCell rotated = CrystalCell.FromBasis(turned);
        CellBasis before = cell.Basis(), after = rotated.Basis();

        // Oraculo: caso limite, a proper rotation of the three vectors changes no invariant of the cell. Edges up to
        // ~10 angstrom and volumes up to ~600 cubic angstrom; the rotation adds a few roundings of order 1e-15 relative.
        Assert.True(rotated.IsValid);
        Assert.Equal(cell.A, rotated.A, 1e-12);
        Assert.Equal(cell.B, rotated.B, 1e-12);
        Assert.Equal(cell.C, rotated.C, 1e-12);
        Assert.Equal(cell.Alpha, rotated.Alpha, 1e-10);
        Assert.Equal(cell.Beta, rotated.Beta, 1e-10);
        Assert.Equal(cell.Gamma, rotated.Gamma, 1e-10);
        Assert.Equal(before.Volume, after.Volume, 1e-11);
        foreach ((double dx, double dy, double dz) in new[] { (1.0, 0.0, 0.0), (0.5, -0.25, 0.75), (1.0, 1.0, 1.0), (-0.3, 0.6, 0.1) })
            Assert.Equal(before.Distance(dx, dy, dz), after.Distance(dx, dy, dz), 1e-12);
        (double ra, double rb, double rc) = before.ReciprocalLengths();
        (double ta, double tb, double tc) = after.ReciprocalLengths();
        Assert.Equal(ra, ta, 1e-14);
        Assert.Equal(rb, tb, 1e-14);
        Assert.Equal(rc, tc, 1e-14);

        // The reciprocal vectors turn with the cell (r*' = R r*), and stay dual to the turned edges.
        double[] star = new double[9], turnedStar = new double[9];
        cell.CopyReciprocalBasisTo(star);
        rotated.CopyReciprocalBasisTo(turnedStar);
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < 3; i++)
            {
                double expected = r[i, 0] * star[3 * row] + r[i, 1] * star[3 * row + 1] + r[i, 2] * star[3 * row + 2];
                Assert.Equal(expected, turnedStar[3 * row + i], 1e-13);
                Assert.Equal(row == i ? 1 : 0, turnedStar[3 * row] * turned[3 * i] + turnedStar[3 * row + 1] * turned[3 * i + 1]
                    + turnedStar[3 * row + 2] * turned[3 * i + 2], 1e-13);
            }

        // The single-precision directions a*, b* and c* turn the same way, to the float of a unit vector (~6e-8).
        Vector3[] directions = [cell.AStar, cell.BStar, cell.CStar];
        Vector3[] turnedDirections = [rotated.AStar, rotated.BStar, rotated.CStar];
        for (int row = 0; row < 3; row++)
        {
            Vector3 d = directions[row];
            var expected = new Vector3(
                (float)(r[0, 0] * d.X + r[0, 1] * d.Y + r[0, 2] * d.Z),
                (float)(r[1, 0] * d.X + r[1, 1] * d.Y + r[1, 2] * d.Z),
                (float)(r[2, 0] * d.X + r[2, 1] * d.Y + r[2, 2] * d.Z));
            Assert.True(Vector3.Distance(expected, turnedDirections[row]) < 1e-6f, $"{turnedDirections[row]} is not {expected}");
        }
    }

    // Rotation by the angle (degrees) about the axis (x, y, z), by Rodrigues' formula.
    static double[,] Rotation(double x, double y, double z, double degrees)
    {
        double length = Math.Sqrt(x * x + y * y + z * z);
        (x, y, z) = (x / length, y / length, z / length);
        double t = degrees * Math.PI / 180, cos = Math.Cos(t), sin = Math.Sin(t), k = 1 - cos;
        return new[,]
        {
            { cos + x * x * k, x * y * k - z * sin, x * z * k + y * sin },
            { y * x * k + z * sin, cos + y * y * k, y * z * k - x * sin },
            { z * x * k - y * sin, z * y * k + x * sin, cos + z * z * k },
        };
    }

    static double[] Vectors(CrystalCell cell)
    {
        double[] m = new double[9];
        cell.CopyBasisTo(m);
        return m;
    }

    static double Determinant(double[] v)
        => v[0] * (v[4] * v[8] - v[5] * v[7]) - v[1] * (v[3] * v[8] - v[5] * v[6]) + v[2] * (v[3] * v[7] - v[4] * v[6]);

    static double[,] Metric(double a, double b, double c, double alpha, double beta, double gamma)
    {
        double ca = Math.Cos(alpha * Math.PI / 180), cb = Math.Cos(beta * Math.PI / 180), cg = Math.Cos(gamma * Math.PI / 180);
        return new[,]
        {
            { a * a, a * b * cg, a * c * cb },
            { a * b * cg, b * b, b * c * ca },
            { a * c * cb, b * c * ca, c * c },
        };
    }
}
