using System.Numerics;

namespace Viewer3D.Tests;

public class PointCloudShapesTests
{
    static readonly Vector3[] Points = [new(0, 0, 0), new(1, 0, 0), new(0, 2, 0)];
    static readonly double[] Values = [0, 4, 9];
    static readonly IColorMap Map = new FuncColorMap(v => new Rgba((byte)(20 * v), 0, 0));

    [Fact]
    public void Build_SizeAndColorFollowTheValue()
    {
        SphereItem[] spheres = PointCloudShapes.Build(Points, Values, v => (float)Math.Sqrt(v) / 10, Map, idBase: 500);

        Assert.Equal(3, spheres.Length);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(Points[i], spheres[i].Center);
            Assert.Equal((float)Math.Sqrt(Values[i]) / 10, spheres[i].Radius);
            Assert.Equal(new Rgba((byte)(20 * Values[i]), 0, 0), spheres[i].Color);
            Assert.Equal(500 + i, spheres[i].Id);
            Assert.False(spheres[i].Dashed);
        }
    }

    [Fact]
    public void Build_TheMarkedPointsAreDashed()
    {
        SphereItem[] spheres = PointCloudShapes.Build(Points, Values, _ => 0.1f, Map, [false, true, false]);

        Assert.Equal([false, true, false], spheres.Select(s => s.Dashed));
    }

    [Fact]
    public void Build_OneValuePerPoint()
    {
        Assert.Throws<ArgumentException>(() => PointCloudShapes.Build(Points, [1, 2], _ => 0.1f, Map));
        Assert.Throws<ArgumentException>(() => PointCloudShapes.Build(Points, Values, _ => 0.1f, Map, [true]));
    }
}
