using System.Numerics;

namespace Viewer3D.Tests;

public class CameraAnimationTests
{
    static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    static readonly TimeSpan Half = TimeSpan.FromSeconds(0.5);

    static float Degrees(float d) => d * MathF.PI / 180;

    static CameraState State(Quaternion q, Vector3 target = default, float radius = 1) => new(q, target, radius);

    static Quaternion AroundY(float degrees) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(degrees));

    /// <summary>Same rotation up to sign: q and -q are one rotation.</summary>
    static void AssertSameRotation(Quaternion expected, Quaternion actual) =>
        Assert.True(MathF.Abs(Quaternion.Dot(expected, actual)) > 1 - 1e-5f, $"expected {expected}, got {actual}");

    [Fact]
    public void StateAt_Ends_AreTheGivenStates()
    {
        CameraState from = State(Quaternion.Identity, new Vector3(1, 2, 3), 2);
        CameraState to = State(AroundY(90), new Vector3(-1, 0, 4), 8);
        var animation = new CameraAnimation(from, to, Second);

        Assert.Equal(from, animation.StateAt(TimeSpan.Zero));
        Assert.Equal(to, animation.StateAt(Second));
        Assert.Equal(from, animation.StateAt(TimeSpan.FromSeconds(-1)));
        Assert.Equal(to, animation.StateAt(TimeSpan.FromSeconds(5)));
        Assert.False(animation.IsFinishedAt(Half));
        Assert.True(animation.IsFinishedAt(Second));
    }

    [Fact]
    public void StateAt_ZeroDuration_IsTheEndRightAway()
    {
        CameraState to = State(AroundY(30), Vector3.One, 3);
        var animation = new CameraAnimation(State(Quaternion.Identity), to, TimeSpan.Zero);

        Assert.Equal(to, animation.StateAt(TimeSpan.Zero));
        Assert.True(animation.IsFinishedAt(TimeSpan.Zero));
    }

    [Fact]
    public void StateAt_350Degrees_GoesTheShortWay()
    {
        var animation = new CameraAnimation(State(Quaternion.Identity), State(AroundY(350)), Second);

        // 350 degrees one way is 10 degrees the other: the middle is at -5, not at 175.
        AssertSameRotation(AroundY(-5), animation.StateAt(Half).Orientation);
    }

    [Fact]
    public void StateAt_NegatedEndQuaternion_StillGoesTheShortWay()
    {
        var animation = new CameraAnimation(State(Quaternion.Identity), State(Quaternion.Negate(AroundY(20))), Second);

        AssertSameRotation(AroundY(10), animation.StateAt(Half).Orientation);
    }

    [Fact]
    public void StateAt_HalfTurnAroundUp_KeepsUpUp()
    {
        var animation = new CameraAnimation(State(Quaternion.Identity), State(AroundY(180)), Second);
        var camera = new Camera();

        foreach (double f in new[] { 0.25, 0.5, 0.75 })
        {
            camera.Set(animation.StateAt(TimeSpan.FromSeconds(f)));
            Vector3 up = camera.ToView(Vector3.UnitY);
            Assert.True(Vector3.Distance(Vector3.UnitY, up) < 1e-5f, $"at {f}: up went to {up}");
        }
    }

    [Fact]
    public void StateAt_Middle_RadiusIsTheGeometricMeanAndTargetTheMidpoint()
    {
        var animation = new CameraAnimation(
            State(Quaternion.Identity, new Vector3(0, 0, 0), 1),
            State(Quaternion.Identity, new Vector3(2, 4, -6), 16), Second);

        CameraState middle = animation.StateAt(Half);

        // The default easing is symmetric, so half the time is half the way; on a log scale that is sqrt(1 * 16).
        Assert.Equal(4f, middle.ViewRadius, 1e-4f);
        Assert.True(Vector3.Distance(new Vector3(1, 2, -3), middle.Target) < 1e-5f, $"target {middle.Target}");
    }

    [Fact]
    public void StateAt_DefaultEasing_MovesOnlyForward()
    {
        var animation = new CameraAnimation(State(Quaternion.Identity, radius: 1), State(AroundY(120), radius: 10), Second);
        float lastRadius = 1;
        float lastAngle = 0;

        for (int i = 1; i <= 100; i++)
        {
            CameraState s = animation.StateAt(TimeSpan.FromSeconds(i / 100.0));
            float angle = 2 * MathF.Acos(Math.Clamp(MathF.Abs(s.Orientation.W), 0, 1));
            Assert.True(s.ViewRadius >= lastRadius, $"radius went back at step {i}");
            Assert.True(angle >= lastAngle - 1e-5f, $"angle went back at step {i}");
            lastRadius = s.ViewRadius;
            lastAngle = angle;
        }
    }

    [Fact]
    public void SmoothStep_IsMonotoneAndClamped()
    {
        Assert.Equal(0, CameraAnimation.SmoothStep(-1));
        Assert.Equal(1, CameraAnimation.SmoothStep(2));
        Assert.Equal(0.5, CameraAnimation.SmoothStep(0.5), 12);
        double last = 0;
        for (int i = 1; i <= 1000; i++)
        {
            double v = CameraAnimation.SmoothStep(i / 1000.0);
            Assert.True(v >= last);
            last = v;
        }
    }

    [Fact]
    public void StateAt_CustomEasing_IsUsed()
    {
        var animation = new CameraAnimation(
            State(Quaternion.Identity, radius: 1), State(Quaternion.Identity, new Vector3(10, 0, 0), 1), Second, t => t * t);

        Assert.Equal(2.5f, animation.StateAt(Half).Target.X, 1e-4f);
    }

    [Fact]
    public void StateAt_DoesNotAllocate()
    {
        var animation = new CameraAnimation(State(Quaternion.Identity, radius: 1), State(AroundY(90), Vector3.One, 5), Second);
        CameraState sink = default;
        for (int i = 0; i < 100; i++)
            sink = animation.StateAt(TimeSpan.FromMilliseconds(i * 10));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            sink = animation.StateAt(TimeSpan.FromMilliseconds(i));
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
        Assert.NotEqual(default, sink);
    }
}

public class CameraTurntableTests
{
    [Fact]
    public void StateAt_OnePeriod_IsBackAtTheStart_AndHalfIsHalfATurn()
    {
        var from = new CameraState(Quaternion.Identity, new Vector3(1, 1, 1), 2);
        var turntable = new CameraTurntable(from, Vector3.UnitY, TimeSpan.FromSeconds(4));
        var camera = new Camera();

        camera.Set(turntable.StateAt(TimeSpan.FromSeconds(4)));
        Assert.True(MathF.Abs(Quaternion.Dot(Quaternion.Identity, camera.Orientation)) > 1 - 1e-5f);

        CameraState half = turntable.StateAt(TimeSpan.FromSeconds(2));
        camera.Set(half);
        Assert.Equal(from.Target, half.Target);
        Assert.Equal(from.ViewRadius, half.ViewRadius);
        // Half a turn around the view's up: the point in front goes to the back.
        Assert.True(camera.ToView(new Vector3(1, 1, 2)).Z < -0.99f);
    }

    [Fact]
    public void StateAt_TurnsLikeCameraRotate()
    {
        var start = new Camera();
        start.Orbit(37, -21);
        var turntable = new CameraTurntable(start.State, Vector3.UnitY, TimeSpan.FromSeconds(8));
        var rotated = start.Clone();
        rotated.Rotate(Vector3.UnitY, MathF.PI / 4);

        CameraState s = turntable.StateAt(TimeSpan.FromSeconds(1));

        Assert.True(MathF.Abs(Quaternion.Dot(rotated.Orientation, s.Orientation)) > 1 - 1e-5f);
    }

    [Fact]
    public void Constructor_RejectsZeroAxisAndPeriod()
    {
        var from = new CameraState(Quaternion.Identity, Vector3.Zero, 1);
        Assert.Throws<ArgumentException>(() => new CameraTurntable(from, Vector3.Zero, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraTurntable(from, Vector3.UnitY, TimeSpan.Zero));
    }
}
