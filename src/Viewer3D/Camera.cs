using System.Numerics;

namespace Viewer3D;

/// <summary>How 3D points are projected on the screen.</summary>
public enum ProjectionKind
{
    /// <summary>Parallel projection: sizes do not depend on depth.</summary>
    Orthographic,

    /// <summary>Perspective projection: nearer things look bigger.</summary>
    Perspective,
}

/// <summary>
/// An orbiting camera. View space has x to the right, y up and z towards the viewer; <see cref="Orientation"/> takes
/// world directions to view directions. The camera looks at <see cref="Target"/> and shows a circle of radius
/// <see cref="ViewRadius"/> (scene units) inside the smaller side of the viewport.
/// Every change raises <see cref="Changed"/>, so two viewers can share one camera.
/// </summary>
public sealed class Camera
{
    /// <summary>Radians of rotation per pixel of drag.</summary>
    public const float OrbitRadiansPerPixel = 0.01f;

    /// <summary>Eye distance in perspective, as a multiple of <see cref="ViewRadius"/>.</summary>
    public const float PerspectiveDistanceFactor = 3f;

    Quaternion orientation = Quaternion.Identity;
    Vector3 target;
    float viewRadius = 1f;
    ProjectionKind projection;

    /// <summary>Raised after any change.</summary>
    public event EventHandler? Changed;

    /// <summary>World to view rotation.</summary>
    public Quaternion Orientation
    {
        get => orientation;
        set { orientation = Quaternion.Normalize(value); OnChanged(); }
    }

    /// <summary>Point the camera looks at, which is drawn at the center of the viewport.</summary>
    public Vector3 Target
    {
        get => target;
        set { target = value; OnChanged(); }
    }

    /// <summary>Radius, in scene units, of the circle that fits the smaller side of the viewport.</summary>
    public float ViewRadius
    {
        get => viewRadius;
        set { viewRadius = MathF.Max(value, 1e-9f); OnChanged(); }
    }

    /// <summary>Projection kind.</summary>
    public ProjectionKind Projection
    {
        get => projection;
        set { if (projection != value) { projection = value; OnChanged(); } }
    }

    /// <summary>Rotates the scene as a drag of (<paramref name="dx"/>, <paramref name="dy"/>) pixels would.</summary>
    public void Orbit(float dx, float dy)
    {
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, dx * OrbitRadiansPerPixel);
        var pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, dy * OrbitRadiansPerPixel);
        // Applied in view space, after the current orientation.
        Orientation = Quaternion.Concatenate(Quaternion.Concatenate(orientation, yaw), pitch);
    }

    /// <summary>Rotates the scene by <paramref name="radians"/> around a view-space axis.</summary>
    public void Rotate(Vector3 viewAxis, float radians)
        => Orientation = Quaternion.Concatenate(orientation, Quaternion.CreateFromAxisAngle(Vector3.Normalize(viewAxis), radians));

    /// <summary>Moves the scene as a drag of (<paramref name="dx"/>, <paramref name="dy"/>) pixels would.</summary>
    public void Pan(float dx, float dy, float width, float height)
    {
        float s = Scale(width, height);
        target -= ToWorld(new Vector3(dx / s, -dy / s, 0));
        OnChanged();
    }

    /// <summary>
    /// Zooms by <paramref name="factor"/> (above 1 brings the scene closer) keeping the point under
    /// (<paramref name="x"/>, <paramref name="y"/>) where it is.
    /// </summary>
    public void ZoomAt(float factor, float x, float y, float width, float height)
    {
        if (factor <= 0)
            return;
        float s = Scale(width, height);
        var before = new Vector3((x - width / 2) / s, -(y - height / 2) / s, 0);
        target += ToWorld(before - before / factor);
        viewRadius = MathF.Max(viewRadius / factor, 1e-9f);
        OnChanged();
    }

    /// <summary>Points the view along <paramref name="direction"/> with <paramref name="up"/> towards the top.</summary>
    public void LookAlong(Vector3 direction, Vector3 up)
    {
        if (direction.LengthSquared() < 1e-20f)
            return;
        Vector3 z = -Vector3.Normalize(direction);
        Vector3 x = Vector3.Cross(up, z);
        if (x.LengthSquared() < 1e-12f)
            x = Vector3.Cross(MathF.Abs(z.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX, z);
        x = Vector3.Normalize(x);
        Vector3 y = Vector3.Cross(z, x);
        // Row-vector convention of System.Numerics: v_view = v_world * M, so the columns are the view axes.
        var m = new Matrix4x4(
            x.X, y.X, z.X, 0,
            x.Y, y.Y, z.Y, 0,
            x.Z, y.Z, z.Z, 0,
            0, 0, 0, 1);
        Orientation = Quaternion.CreateFromRotationMatrix(m);
    }

    /// <summary>Centers the camera on a sphere of <paramref name="radius"/> around <paramref name="center"/>.</summary>
    public void Frame(Vector3 center, float radius)
    {
        target = center;
        viewRadius = MathF.Max(radius * 1.05f, 1e-9f);
        OnChanged();
    }

    /// <summary>Centers the camera on the whole scene.</summary>
    public void Frame(Scene scene) => Frame(scene.Center, scene.Radius);

    /// <summary>Orientation, target and view radius, the part of the camera that <see cref="CameraAnimation"/> moves.</summary>
    public CameraState State => new(orientation, target, viewRadius);

    /// <summary>
    /// Sets orientation, target and view radius at once and raises <see cref="Changed"/> once, with the same
    /// normalization and floor as the separate setters.
    /// </summary>
    public void Set(Quaternion orientation, Vector3 target, float viewRadius)
    {
        this.orientation = Quaternion.Normalize(orientation);
        this.target = target;
        this.viewRadius = MathF.Max(viewRadius, 1e-9f);
        OnChanged();
    }

    /// <summary>Sets the state taken from <see cref="State"/> or from an animation.</summary>
    public void Set(in CameraState state) => Set(state.Orientation, state.Target, state.ViewRadius);

    /// <summary>A copy with the same state and no subscribers.</summary>
    public Camera Clone() => new() { orientation = orientation, target = target, viewRadius = viewRadius, projection = projection };

    /// <summary>Copies the state of <paramref name="other"/> and raises <see cref="Changed"/> once.</summary>
    public void CopyFrom(Camera other)
    {
        orientation = other.orientation;
        target = other.target;
        viewRadius = other.viewRadius;
        projection = other.projection;
        OnChanged();
    }

    /// <summary>Pixels per scene unit at the target depth.</summary>
    public float Scale(float width, float height) => MathF.Max(MathF.Min(width, height), 1f) / (2 * viewRadius);

    /// <summary>Takes a world point to view space (relative to the target, not projected).</summary>
    public Vector3 ToView(Vector3 world) => Vector3.Transform(world - target, orientation);

    /// <summary>Takes a view-space direction back to world space.</summary>
    public Vector3 ToWorld(Vector3 view) => Vector3.Transform(view, Quaternion.Conjugate(orientation));

    /// <summary>The projection for a viewport, to project many points.</summary>
    public Projector CreateProjector(float width, float height) => new(this, width, height);

    void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Projects world points for one camera state and one viewport.</summary>
public readonly struct Projector
{
    readonly Quaternion orientation;
    readonly Vector3 target;
    readonly float scale, cx, cy, eye;
    readonly bool perspective;

    internal Projector(Camera camera, float width, float height)
    {
        orientation = camera.Orientation;
        target = camera.Target;
        scale = camera.Scale(width, height);
        cx = width / 2;
        cy = height / 2;
        perspective = camera.Projection == ProjectionKind.Perspective;
        eye = Camera.PerspectiveDistanceFactor * camera.ViewRadius;
    }

    /// <summary>Pixels per scene unit at the target depth.</summary>
    public float Scale => scale;

    /// <summary>World point to view space.</summary>
    public Vector3 ToView(Vector3 world) => Vector3.Transform(world - target, orientation);

    /// <summary>
    /// Projects a view-space point. Returns false when the point is behind the eye (perspective only).
    /// <paramref name="factor"/> is the size multiplier at that depth (1 in orthographic).
    /// </summary>
    public bool ProjectView(Vector3 v, out float x, out float y, out float factor)
    {
        factor = 1f;
        if (perspective)
        {
            float d = eye - v.Z;
            if (d < eye * 0.02f)
            {
                x = y = 0;
                return false;
            }
            factor = eye / d;
        }
        x = cx + v.X * scale * factor;
        y = cy - v.Y * scale * factor;
        return true;
    }

    /// <summary>Projects a world point to screen pixels; depth is the view z (bigger is nearer).</summary>
    public bool Project(Vector3 world, out float x, out float y, out float depth)
    {
        Vector3 v = ToView(world);
        depth = v.Z;
        return ProjectView(v, out x, out y, out _);
    }
}
