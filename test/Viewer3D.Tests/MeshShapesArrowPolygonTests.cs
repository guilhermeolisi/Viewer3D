using System.Numerics;

namespace Viewer3D.Tests;

public class MeshShapesArrowPolygonTests
{
    static readonly Rgba Red = new(220, 30, 30);

    /// <summary>Distance from <paramref name="p"/> to the line through <paramref name="a"/> along the unit <paramref name="d"/>.</summary>
    static float DistanceToAxis(Vector3 p, Vector3 a, Vector3 d)
    {
        Vector3 w = p - a;
        return (w - Vector3.Dot(w, d) * d).Length();
    }

    [Fact]
    public void Arrow_HasItsTipAtTheEnd_AndStaysWithinTheHeadRadius()
    {
        var start = new Vector3(1, 2, 3);
        var end = new Vector3(3, -1, 4);
        MeshItem arrow = MeshShapes.Arrow(start, end, 0.05f, 0.15f, 0.4f, Red, segments: 12);

        Vector3 d = Vector3.Normalize(end - start);
        // The farthest vertex along the axis is the tip, and it sits on the end point.
        Vector3 tip = arrow.Positions.MaxBy(p => Vector3.Dot(p - start, d));
        Assert.True(Vector3.Distance(tip, end) < 1e-5f, $"tip {tip}");
        // Float rounding of the ring vertices: 1e-5 of a radius of 0.15.
        Assert.All(arrow.Positions, p => Assert.True(DistanceToAxis(p, start, d) <= 0.15f + 1e-5f, $"{p}"));
        Assert.All(arrow.Positions, p => Assert.InRange(Vector3.Dot(p - start, d), -1e-5f, (end - start).Length() + 1e-5f));
        Assert.Equal(MeshLighting.KeyLight, arrow.Lighting);
        Assert.Equal(0, arrow.Indices.Length % 3);
        Assert.All(arrow.Indices, i => Assert.InRange(i, 0, arrow.Positions.Length - 1));
    }

    [Fact]
    public void Arrow_FacesPointOutward()
    {
        // Along z: the sides face away from the axis (the cone also forward), the two discs face back (-z).
        MeshItem arrow = MeshShapes.Arrow(Vector3.Zero, new Vector3(0, 0, 2), 0.1f, 0.3f, 0.5f, Red);
        int sides = 0, discs = 0;
        for (int t = 0; t < arrow.TriangleCount; t++)
        {
            Vector3 a = arrow.Positions[arrow.Indices[3 * t]];
            Vector3 b = arrow.Positions[arrow.Indices[3 * t + 1]];
            Vector3 c = arrow.Positions[arrow.Indices[3 * t + 2]];
            Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            Vector3 center = (a + b + c) / 3;
            var radialNormal = new Vector2(normal.X, normal.Y);
            if (radialNormal.Length() > 1e-4f)
            {
                Assert.True(Vector2.Dot(radialNormal, new Vector2(center.X, center.Y)) > 0, $"triangle {t} faces the axis");
                Assert.True(normal.Z > -1e-4f, $"triangle {t} side faces back");
                sides++;
            }
            else
            {
                Assert.True(normal.Z < 0, $"triangle {t} disc faces forward");
                discs++;
            }
        }
        // 16 segments: two triangles per segment on the shaft and one on the cone; the bottom disc and the head base.
        Assert.Equal(48, sides);
        Assert.Equal(32, discs);
    }

    [Fact]
    public void Arrow_ShorterThanItsHead_IsOnlyACone()
    {
        MeshItem arrow = MeshShapes.Arrow(Vector3.Zero, new Vector3(0.2f, 0, 0), 0.05f, 0.1f, 0.5f, Red, segments: 8);

        // Two triangles per segment: the base disc and the side of the cone.
        Assert.Equal(16, arrow.TriangleCount);
    }

    [Fact]
    public void Polygon_IsAFanWithItsOutlineAsEdges()
    {
        Vector3[] square = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(-0.5f, 0.5f, 0)];
        MeshItem polygon = MeshShapes.Polygon(square, new Rgba(40, 90, 200, 90), id: -7);

        Assert.Equal(3, polygon.TriangleCount);
        Assert.Equal(10, polygon.Edges!.Length);
        Assert.Equal([0, 1, 1, 2, 2, 3, 3, 4, 4, 0], polygon.Edges);
        Assert.True(polygon.DoubleSided);
        Assert.Equal(90, polygon.Color.A);
        Assert.Equal(-7, polygon.Id);
        Assert.Equal(square, polygon.Positions);
    }

    [Fact]
    public void Polygon_WithFewerThanThreeVertices_IsEmpty()
    {
        MeshItem polygon = MeshShapes.Polygon([Vector3.Zero, Vector3.One], Red);

        Assert.Equal(0, polygon.TriangleCount);
    }
}
