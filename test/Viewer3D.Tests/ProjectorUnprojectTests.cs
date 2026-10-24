using System.Numerics;

namespace Viewer3D.Tests;

public class ProjectorUnprojectTests
{
    const float ViewRadius = 2f;

    // Tolerance of 1e-4 view radii: single precision over a few transforms stays far below it, and a wrong sign of y or
    // a missing perspective factor moves the point by a sizable fraction of the radius.
    const float Tol = 1e-4f * ViewRadius;

    static readonly Vector3[] Points =
    [
        new(0, 0, 0), new(1.2f, -0.4f, 0.3f), new(-0.7f, 0.9f, -1.1f), new(0.2f, 1.5f, 0.8f), new(-1.3f, -1.1f, 0.5f),
    ];

    static Camera Turned(ProjectionKind projection)
    {
        var camera = new Camera { Projection = projection, ViewRadius = ViewRadius, Target = new Vector3(0.5f, -0.25f, 1f) };
        camera.Orientation = Quaternion.CreateFromYawPitchRoll(0.7f, -0.4f, 0.25f);
        return camera;
    }

    [Theory]
    [InlineData(ProjectionKind.Orthographic)]
    [InlineData(ProjectionKind.Perspective)]
    public void Unproject_ThenIntersectThePlaneThroughThePoint_ShouldGiveThePointBack(ProjectionKind projection)
    {
        Camera camera = Turned(projection);
        Projector projector = camera.CreateProjector(640, 480);
        int checkedPoints = 0;

        foreach (Vector3 p in Points)
        {
            Vector3 world = camera.Target + p;
            Assert.True(projector.Project(world, out float x, out float y, out _));

            Ray ray = projector.Unproject(x, y);
            // A normal well away from the plane of the ray, different for each point.
            Vector3 normal = Vector3.Normalize(ray.Direction + new Vector3(0.3f, -0.2f, 0.1f) * (checkedPoints + 1));

            Assert.True(ray.TryIntersectPlane(world, normal, out Vector3 hit));
            Assert.True(Vector3.Distance(world, hit) < Tol, $"expected {world}, got {hit}");
            checkedPoints++;
        }

        Assert.Equal(Points.Length, checkedPoints);
    }

    [Theory]
    [InlineData(ProjectionKind.Orthographic)]
    [InlineData(ProjectionKind.Perspective)]
    public void Unproject_ShouldGiveARayWhosePointsProjectToTheSamePixel(ProjectionKind projection)
    {
        Camera camera = Turned(projection);
        Projector projector = camera.CreateProjector(640, 480);

        Ray ray = projector.Unproject(410, 130);

        Assert.Equal(1f, ray.Direction.Length(), 4);
        foreach (float t in new[] { 1f, 4f, 7f })
        {
            Assert.True(projector.Project(ray.At(t), out float x, out float y, out _));
            Assert.Equal(410f, x, 2);
            Assert.Equal(130f, y, 2);
        }
    }

    [Fact]
    public void Unproject_AtTheCenter_ShouldPointAlongTheViewDirectionThroughTheTarget()
    {
        Camera camera = Turned(ProjectionKind.Perspective);
        Projector projector = camera.CreateProjector(640, 480);

        Ray ray = projector.Unproject(320, 240);

        // View -z, taken back to the world.
        Vector3 forward = camera.ToWorld(-Vector3.UnitZ);
        Assert.True(Vector3.Distance(forward, ray.Direction) < 1e-5f, $"expected {forward}, got {ray.Direction}");
        Assert.True(ray.TryIntersectPlane(camera.Target, forward, out Vector3 hit));
        Assert.True(Vector3.Distance(camera.Target, hit) < Tol);
    }

    [Fact]
    public void ToWorld_ShouldUndoToView()
    {
        Camera camera = Turned(ProjectionKind.Orthographic);
        Projector projector = camera.CreateProjector(640, 480);
        var world = new Vector3(1.5f, -2f, 0.75f);

        Assert.True(Vector3.Distance(world, projector.ToWorld(projector.ToView(world))) < 1e-5f);
    }

    [Fact]
    public void TryIntersectPlane_ShouldRefuse_WhenTheRayIsParallelToThePlane()
    {
        var ray = new Ray(new Vector3(0, 0, 5), -Vector3.UnitZ);

        Assert.False(ray.TryIntersectPlane(Vector3.Zero, Vector3.UnitX, out _));
    }

    [Fact]
    public void TryIntersectPlane_ShouldRefuse_WhenThePlaneIsBehindTheOrigin()
    {
        var ray = new Ray(new Vector3(0, 0, 5), -Vector3.UnitZ);

        Assert.False(ray.TryIntersectPlane(new Vector3(0, 0, 6), Vector3.UnitZ, out _));
        Assert.True(ray.TryIntersectPlane(new Vector3(0, 0, -1), Vector3.UnitZ, out Vector3 hit));
        Assert.Equal(-1f, hit.Z, 5);
    }
}
