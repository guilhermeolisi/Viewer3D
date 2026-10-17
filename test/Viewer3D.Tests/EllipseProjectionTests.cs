using System.Numerics;

namespace Viewer3D.Tests;

public class EllipseProjectionTests
{
    /// <summary>Semi-axes of lengths 2, 1 and 0.5 turned by a rotation about an oblique axis.</summary>
    static Vector3[] TurnedAxes(out Quaternion q)
    {
        q = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1, 2, 3)), 0.9f);
        return [Vector3.Transform(new Vector3(2, 0, 0), q), Vector3.Transform(new Vector3(0, 1, 0), q), Vector3.Transform(new Vector3(0, 0, 0.5f), q)];
    }

    /// <summary>The point of the outline c + L u for the unit vector u at angle t.</summary>
    static Vector2 OutlinePoint((float L11, float L12, float L22) l, float t)
        => new(l.L11 * MathF.Cos(t) + l.L12 * MathF.Sin(t), l.L12 * MathF.Cos(t) + l.L22 * MathF.Sin(t));

    [Fact]
    public void Outline_OfASphere_IsACircleOfItsRadius()
    {
        // Pega o bloco 2x2 errado: uma esfera tem de dar o circulo do proprio raio, de qualquer lado.
        Vector3[] axes = [new(0.7f, 0, 0), new(0, 0.7f, 0), new(0, 0, 0.7f)];

        (float xx, float xy, float yy) = EllipseProjection.OutlineShape(axes[0], axes[1], axes[2]);
        var l = EllipseProjection.SquareRoot(xx, xy, yy);

        Assert.Equal(0.7f, l.L11, 5);
        Assert.Equal(0f, l.L12, 5);
        Assert.Equal(0.7f, l.L22, 5);
    }

    [Theory]
    [InlineData(0)] // vista ao longo de z: semi-eixos 2 e 1
    [InlineData(1)] // vista ao longo de y: semi-eixos 2 e 0,5
    [InlineData(2)] // vista ao longo de x: semi-eixos 1 e 0,5
    public void Outline_OfAnAlignedEllipsoid_HasTheSemiAxesOfTheView(int view)
    {
        // Pega eixo trocado: cada vista ao longo de um eixo mostra os outros dois semi-eixos.
        Vector3[] axes = [new(2, 0, 0), new(0, 1, 0), new(0, 0, 0.5f)];
        Quaternion turn = view switch
        {
            1 => Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2), // y passa a apontar para o observador
            2 => Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2), // x passa a apontar para o observador
            _ => Quaternion.Identity,
        };
        Vector3[] seen = [.. axes.Select(a => Vector3.Transform(a, turn))];

        (float xx, float xy, float yy) = EllipseProjection.OutlineShape(seen[0], seen[1], seen[2]);

        float[] expected = view switch { 1 => [2f, 0.5f], 2 => [1f, 0.5f], _ => [2f, 1f] };
        float[] semi = [MathF.Sqrt(MathF.Max(xx, yy)), MathF.Sqrt(MathF.Min(xx, yy))];
        Assert.Equal(0f, xy, 5);
        Assert.Equal(expected[0], semi[0], 4);
        Assert.Equal(expected[1], semi[1], 4);
    }

    [Fact]
    public void Outline_OfATurnedEllipsoid_HoldsEverySurfacePointAndTouchesTheSurface()
    {
        // Pega a silhueta montada a partir de E^-1 em vez do bloco de E: com ela, pontos da superficie caem fora.
        Vector3[] axes = TurnedAxes(out _);
        (float xx, float xy, float yy) = EllipseProjection.OutlineShape(axes[0], axes[1], axes[2]);
        float det = xx * yy - xy * xy;
        var random = new Random(150);
        float largest = 0;

        for (int k = 0; k < 2000; k++)
        {
            // Ponto da superficie: c + sum a_i w_i com |w| = 1.
            var w = Vector3.Normalize(new Vector3((float)(random.NextDouble() - 0.5), (float)(random.NextDouble() - 0.5),
                (float)(random.NextDouble() - 0.5)));
            Vector3 p = axes[0] * w.X + axes[1] * w.Y + axes[2] * w.Z;
            // p^T E2^-1 p, com E2^-1 = [[yy, -xy], [-xy, xx]] / det.
            float form = (yy * p.X * p.X - 2 * xy * p.X * p.Y + xx * p.Y * p.Y) / det;
            largest = MathF.Max(largest, form);
        }

        // Tolerancia 1e-4: arredondamento de float; 2000 pontos aleatorios chegam a menos de 2% da borda.
        Assert.True(largest <= 1 + 1e-4f, $"largest {largest}");
        Assert.True(largest >= 0.98f, $"largest {largest}");
    }

    [Fact]
    public void SquareRoot_SquaredGivesTheMatrixBack()
    {
        var l = EllipseProjection.SquareRoot(5f, 1.5f, 2f);

        Assert.Equal(5f, l.L11 * l.L11 + l.L12 * l.L12, 4);
        Assert.Equal(1.5f, l.L11 * l.L12 + l.L12 * l.L22, 4);
        Assert.Equal(2f, l.L12 * l.L12 + l.L22 * l.L22, 4);
        Assert.Equal(OutlinePoint(l, 0).X, l.L11);
    }

    [Fact]
    public void VisibleHalf_IsWhereTheNormalFacesTheViewer()
    {
        // Pega o sinal de t*: dentro da metade visivel a normal tem z > 0, fora dela z < 0, e nos extremos z = 0.
        Vector3[] axes = TurnedAxes(out _);
        Vector3 aj = axes[0], ak = axes[2];

        float start = EllipseProjection.VisibleHalfStart(aj, ak);

        float NormalZ(float t)
        {
            Vector3 p = aj * MathF.Cos(t) + ak * MathF.Sin(t);
            float[] weights = [.. axes.Select(a => Vector3.Dot(p, a) / (a.LengthSquared() * a.LengthSquared()))];
            Vector3 n = axes[0] * weights[0] + axes[1] * weights[1] + axes[2] * weights[2];
            return Vector3.Normalize(n).Z;
        }
        for (int k = 1; k < 36; k++)
        {
            float inside = start + k * MathF.PI / 36, outside = inside + MathF.PI;
            Assert.True(NormalZ(inside) > 0, $"t {inside}: {NormalZ(inside)}");
            Assert.True(NormalZ(outside) < 0, $"t {outside}: {NormalZ(outside)}");
        }
        Assert.Equal(0f, NormalZ(start), 4);
        Assert.Equal(0f, NormalZ(start + MathF.PI), 4);
    }

    [Fact]
    public void SurfaceDistance_EndsOnTheSurface()
    {
        // Pega a raiz invertida: o ponto a essa distancia tem de satisfazer x^T E^-1 x = 1.
        Vector3[] axes = TurnedAxes(out _);
        var d = Vector3.Normalize(new Vector3(0.3f, -0.8f, 0.5f));

        float r = EllipseProjection.SurfaceDistance(axes, d);

        Assert.Equal(1f, EllipseProjection.InverseShapeForm(axes, d * r), 4);
        Assert.Equal(2f, EllipseProjection.SurfaceDistance(axes, axes[0]), 4);
        Assert.Equal(0.5f, EllipseProjection.SurfaceDistance(axes, -axes[2]), 4);
    }

    [Fact]
    public void LineCrossing_CutsTheLineAtTheSurface()
    {
        Vector3[] axes = [new(2, 0, 0), new(0, 1, 0), new(0, 0, 0.5f)];

        bool hit = EllipseProjection.LineCrossing(axes, Vector3.Zero, new Vector3(-4, 0, 0), new Vector3(8, 0, 0), out float t0, out float t1);
        bool miss = EllipseProjection.LineCrossing(axes, Vector3.Zero, new Vector3(-4, 2, 0), new Vector3(8, 0, 0), out _, out _);

        Assert.True(hit);
        Assert.Equal(0.25f, t0, 5);
        Assert.Equal(0.75f, t1, 5);
        Assert.False(miss);
    }
}
