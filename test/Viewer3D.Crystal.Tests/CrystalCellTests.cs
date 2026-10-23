using System.Numerics;

namespace Viewer3D.Crystal.Tests;

public class CrystalCellTests
{
    // Distancias em angstrom, float da Vector3: 1e-4 A cobre o arredondamento de float em celas de ate ~10 A.
    const double Tolerance = 1e-4;

    [Fact]
    public void ToCartesian_PutsAAlongXAndBInTheXyPlane()
    {
        var cell = new CrystalCell(4, 5, 6, 80, 95, 110);

        Vector3 a = cell.AVector, b = cell.BVector, c = cell.CVector;

        Assert.Equal(4, a.X, Tolerance);
        Assert.Equal(0, a.Y, Tolerance);
        Assert.Equal(0, a.Z, Tolerance);
        Assert.Equal(0, b.Z, Tolerance);
        Assert.True(b.Y > 0);
        Assert.True(c.Z > 0);
        Assert.Equal(5, b.Length(), Tolerance);
        Assert.Equal(6, c.Length(), Tolerance);
        Assert.Equal(110, Angle(a, b), 1e-3);
        Assert.Equal(95, Angle(a, c), 1e-3);
        Assert.Equal(80, Angle(b, c), 1e-3);
    }

    [Fact]
    public void ToCartesian_MatchesMetricDistances_InAMonoclinicCell()
    {
        // Monoclinica, beta = 100 graus: |d|^2 = a^2 dx^2 + b^2 dy^2 + c^2 dz^2 + 2 a c cos(beta) dx dz.
        var cell = new CrystalCell(7.1, 5.3, 9.4, 90, 100, 90);
        (double x, double y, double z)[] deltas = [(0.3, 0.1, -0.2), (0.5, 0.5, 0.5), (-0.25, 0.75, 0.4)];
        double cosBeta = Math.Cos(100 * Math.PI / 180);

        foreach ((double x, double y, double z) in deltas)
        {
            double expected = Math.Sqrt(7.1 * 7.1 * x * x + 5.3 * 5.3 * y * y + 9.4 * 9.4 * z * z
                + 2 * 7.1 * 9.4 * cosBeta * x * z);
            Assert.Equal(expected, cell.ToCartesian(x, y, z).Length(), Tolerance);
        }
    }

    [Fact]
    public void ToCartesian_MatchesTheHcpNeighborDistances()
    {
        // Mg hexagonal compacto, a = 3,2094 A, c = 5,2108 A, 2c: (1/3, 2/3, 1/4) e (2/3, 1/3, 3/4). Vizinhos no plano a
        // a = 3,2094 A; fora do plano, sqrt(a^2/3 + c^2/4) = 3,1966 A.
        var cell = new CrystalCell(3.2094, 3.2094, 5.2108, 90, 90, 120);

        Vector3 p = cell.ToCartesian(1.0 / 3, 2.0 / 3, 0.25), q = cell.ToCartesian(2.0 / 3, 1.0 / 3, 0.75);

        Assert.Equal(3.2094, cell.ToCartesian(1, 0, 0).Length(), Tolerance);
        Assert.Equal(3.2094, (cell.ToCartesian(1.0 / 3 + 1, 2.0 / 3 + 1, 0.25) - cell.ToCartesian(1.0 / 3, 2.0 / 3 + 1, 0.25)).Length(), Tolerance);
        Assert.Equal(Math.Sqrt(3.2094 * 3.2094 / 3 + 5.2108 * 5.2108 / 4), (q - p).Length(), Tolerance);
    }

    [Fact]
    public void ReciprocalDirections_ArePerpendicularToTheOtherTwoAxes()
    {
        var cell = new CrystalCell(4, 5, 6, 80, 95, 110);

        Assert.Equal(0, Vector3.Dot(cell.AStar, cell.BVector), Tolerance);
        Assert.Equal(0, Vector3.Dot(cell.AStar, cell.CVector), Tolerance);
        Assert.Equal(0, Vector3.Dot(cell.BStar, cell.CVector), Tolerance);
        Assert.Equal(0, Vector3.Dot(cell.BStar, cell.AVector), Tolerance);
        Assert.Equal(0, Vector3.Dot(cell.CStar, cell.AVector), Tolerance);
        Assert.Equal(0, Vector3.Dot(cell.CStar, cell.BVector), Tolerance);
        Assert.True(Vector3.Dot(cell.AStar, cell.AVector) > 0);
        Assert.True(Vector3.Dot(cell.CStar, cell.CVector) > 0);
        Assert.Equal(1, cell.CStar.Length(), Tolerance);
    }

    [Fact]
    public void IsValid_IsFalse_ForAFlatCell()
    {
        Assert.True(new CrystalCell(4, 4, 4, 90, 90, 90).IsValid);
        Assert.False(new CrystalCell(4, 4, 4, 120, 120, 120).IsValid);
        Assert.False(new CrystalCell(0, 4, 4, 90, 90, 90).IsValid);
        Assert.Equal(Vector3.Zero, new CrystalCell(-1, 4, 4, 90, 90, 90).ToCartesian(0.5, 0.5, 0.5));
    }

    [Fact]
    public void FromVectors_GivesTheLengthsAndAngles_InAnyOrientation()
    {
        var cell = new CrystalCell(4, 5, 6, 80, 95, 110);
        Quaternion turn = Quaternion.CreateFromYawPitchRoll(0.7f, -0.4f, 1.1f);

        CrystalCell back = CrystalCell.FromVectors(Vector3.Transform(cell.AVector, turn), Vector3.Transform(cell.BVector, turn),
            Vector3.Transform(cell.CVector, turn));

        Assert.Equal(4, back.A, Tolerance);
        Assert.Equal(5, back.B, Tolerance);
        Assert.Equal(6, back.C, Tolerance);
        Assert.Equal(80, back.Alpha, 1e-3);
        Assert.Equal(95, back.Beta, 1e-3);
        Assert.Equal(110, back.Gamma, 1e-3);
    }

    [Fact]
    public void FromVectors_LeftHanded_ReversesC()
    {
        // a, b and -c are left-handed: the cell is the one of a, b and c, whose angles with c are 80 and 95 (100 and
        // 85 with -c).
        var cell = new CrystalCell(4, 5, 6, 80, 95, 110);

        CrystalCell back = CrystalCell.FromVectors(cell.AVector, cell.BVector, -cell.CVector);

        Assert.Equal(80, back.Alpha, 1e-3);
        Assert.Equal(95, back.Beta, 1e-3);
        Assert.Equal(110, back.Gamma, 1e-3);
        Assert.True(back.IsValid);
    }

    [Fact]
    public void FromVectors_Coplanar_IsNotValid()
    {
        Assert.False(CrystalCell.FromVectors(new Vector3(4, 0, 0), new Vector3(0, 5, 0), new Vector3(1, 1, 0)).IsValid);
        Assert.False(CrystalCell.FromVectors(new Vector3(4, 0, 0), Vector3.Zero, new Vector3(0, 0, 6)).IsValid);
    }

    static double Angle(Vector3 u, Vector3 v)
        => Math.Acos(Vector3.Dot(u, v) / (u.Length() * v.Length())) * 180 / Math.PI;
}
