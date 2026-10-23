using System.Numerics;

namespace Viewer3D.Tests;

public class CameraTests
{
    const float Tol = 1e-4f;

    static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < Tol, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void Project_Orthographic_MatchesHandComputation()
    {
        // 200 x 100 viewport, view radius 1: 50 pixels per unit, center at (100, 50), y up becomes y down.
        var camera = new Camera();
        Projector p = camera.CreateProjector(200, 100);

        Assert.True(p.Project(new Vector3(1, 0.5f, 0), out float x, out float y, out float depth));

        Assert.Equal(150f, x, 3);
        Assert.Equal(25f, y, 3);
        Assert.Equal(0f, depth, 3);
    }

    [Fact]
    public void Project_Perspective_EnlargesNearerPoints()
    {
        // Eye at 3 view radii: a point at z = 1 is 2 units from the eye, factor 3 / 2.
        var camera = new Camera { Projection = ProjectionKind.Perspective };
        Projector p = camera.CreateProjector(200, 100);

        p.Project(new Vector3(1, 0, 1), out float xNear, out _, out _);
        p.Project(new Vector3(1, 0, 0), out float xTarget, out _, out _);

        Assert.Equal(175f, xNear, 3);
        Assert.Equal(150f, xTarget, 3);
    }

    [Fact]
    public void Project_Perspective_RejectsPointsBehindTheEye()
    {
        var camera = new Camera { Projection = ProjectionKind.Perspective };

        Assert.False(camera.CreateProjector(100, 100).Project(new Vector3(0, 0, 3.5f), out _, out _, out _));
    }

    [Fact]
    public void Orbit_DragRight_MovesTheFrontPointRight()
    {
        var camera = new Camera();

        camera.Orbit(MathF.PI / 2 / Camera.OrbitRadiansPerPixel, 0);

        Near(new Vector3(1, 0, 0), camera.ToView(new Vector3(0, 0, 1)));
    }

    [Fact]
    public void Orbit_DragDown_MovesTheFrontPointDown()
    {
        var camera = new Camera();

        camera.Orbit(0, MathF.PI / 2 / Camera.OrbitRadiansPerPixel);

        Near(new Vector3(0, -1, 0), camera.ToView(new Vector3(0, 0, 1)));
    }

    [Fact]
    public void Orbit_SecondDrag_TurnsAroundTheScreenAxisNotTheWorldAxis()
    {
        // After a quarter turn to the right, a vertical drag must still tilt about the screen's horizontal axis.
        var camera = new Camera();
        camera.Orbit(MathF.PI / 2 / Camera.OrbitRadiansPerPixel, 0);

        camera.Orbit(0, MathF.PI / 2 / Camera.OrbitRadiansPerPixel);

        // World +y was view +y; a downward tilt sends it towards the viewer.
        Near(new Vector3(0, 0, 1), camera.ToView(new Vector3(0, 1, 0)));
    }

    [Fact]
    public void Pan_MovesTheSceneWithThePointer()
    {
        var camera = new Camera();

        camera.Pan(50, 0, 200, 100);

        camera.CreateProjector(200, 100).Project(Vector3.Zero, out float x, out float y, out _);
        Assert.Equal(150f, x, 3);
        Assert.Equal(50f, y, 3);
    }

    [Fact]
    public void ZoomAt_KeepsThePointUnderThePointer()
    {
        var camera = new Camera();
        var world = new Vector3(1, 0.5f, 0);

        camera.ZoomAt(2, 150, 25, 200, 100);

        camera.CreateProjector(200, 100).Project(world, out float x, out float y, out _);
        Assert.Equal(150f, x, 3);
        Assert.Equal(25f, y, 3);
        Assert.Equal(0.5f, camera.ViewRadius, 5);
    }

    [Fact]
    public void LookAlong_PutsDirectionIntoTheScreenAndUpUp()
    {
        var camera = new Camera();

        camera.LookAlong(new Vector3(1, 0, 0), new Vector3(0, 0, 1));

        Near(new Vector3(0, 1, 0), camera.ToView(new Vector3(0, 0, 1)));
        Near(new Vector3(0, 0, 1), camera.ToView(new Vector3(-1, 0, 0)));
        Near(new Vector3(1, 0, 0), camera.ToView(new Vector3(0, -1, 0)));
    }

    [Fact]
    public void LookAlong_UpParallelToDirection_StillGivesARotation()
    {
        var camera = new Camera();

        camera.LookAlong(new Vector3(0, 0, -2), new Vector3(0, 0, 1));

        Near(new Vector3(0, 0, 1), camera.ToView(new Vector3(0, 0, 1)));
        Assert.Equal(1f, camera.Orientation.Length(), 4);
    }

    [Fact]
    public void Frame_UsesCenterAndRadiusWithMargin()
    {
        var camera = new Camera();

        camera.Frame(new Vector3(1, 2, 3), 2);

        Assert.Equal(new Vector3(1, 2, 3), camera.Target);
        Assert.Equal(2.1f, camera.ViewRadius, 4);
    }

    [Fact]
    public void Frame_Scene_UsesTheBoundingSphere()
    {
        var scene = new Scene([new SphereItem { Center = new Vector3(2, 0, 0), Radius = 1 }]);
        var camera = new Camera();

        camera.Frame(scene);

        Near(new Vector3(2, 0, 0), camera.Target);
        Assert.Equal(1.05f, camera.ViewRadius, 4);
    }

    [Fact]
    public void Changed_IsRaisedByEveryChange_AndCloneHasNoSubscribers()
    {
        var camera = new Camera();
        int raised = 0;
        camera.Changed += (_, _) => raised++;

        camera.Orbit(5, 0);
        camera.Pan(1, 1, 10, 10);
        camera.ZoomAt(1.1f, 5, 5, 10, 10);
        Camera clone = camera.Clone();
        clone.Orbit(5, 0);

        Assert.Equal(3, raised);
        Assert.NotEqual(camera.Orientation, clone.Orientation);
    }

    [Fact]
    public void CopyFrom_CopiesTheWholeState()
    {
        var source = new Camera { Projection = ProjectionKind.Perspective };
        source.Orbit(30, 10);
        source.Frame(new Vector3(1, 1, 1), 3);
        var target = new Camera();

        target.CopyFrom(source);

        Assert.Equal(source.Orientation, target.Orientation);
        Assert.Equal(source.Target, target.Target);
        Assert.Equal(source.ViewRadius, target.ViewRadius);
        Assert.Equal(ProjectionKind.Perspective, target.Projection);
    }

    [Fact]
    public void Set_AppliesTheStateWithOneChanged_NormalizedAndFloored()
    {
        var camera = new Camera { Projection = ProjectionKind.Perspective };
        int raised = 0;
        camera.Changed += (_, _) => raised++;
        var unnormalized = new Quaternion(0, 2, 0, 2);

        camera.Set(unnormalized, new Vector3(1, 2, 3), -5);

        Assert.Equal(1, raised);
        Assert.Equal(Quaternion.Normalize(unnormalized), camera.Orientation);
        Assert.Equal(new Vector3(1, 2, 3), camera.Target);
        Assert.Equal(1e-9f, camera.ViewRadius);
        Assert.Equal(ProjectionKind.Perspective, camera.Projection);
    }

    [Fact]
    public void State_RoundTripsThroughSet()
    {
        var source = new Camera();
        source.Orbit(40, -15);
        source.Frame(new Vector3(-1, 0, 2), 4);
        var copy = new Camera();

        copy.Set(source.State);

        Assert.Equal(source.State, copy.State);
    }
}
