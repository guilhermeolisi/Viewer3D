using System.Numerics;

namespace Viewer3D;

/// <summary>The animated part of a <see cref="Camera"/>: world to view rotation, look-at point and view radius.</summary>
public readonly record struct CameraState(Quaternion Orientation, Vector3 Target, float ViewRadius);

/// <summary>
/// A camera move from one state to another over a fixed time. Orientation goes by spherical interpolation along the
/// shorter arc, the target linearly and the view radius on a log scale, so zooming in and out feel alike. Pure: it only
/// computes states, and the caller applies them with <see cref="Camera.Set(in CameraState)"/>.
/// </summary>
public sealed class CameraAnimation
{
    /// <summary>The default easing: smooth start and smooth stop.</summary>
    public static readonly Func<double, double> DefaultEasing = SmoothStep;

    readonly Quaternion from, to;
    readonly Vector3 fromTarget, toTarget;
    readonly float logFrom, logTo;
    readonly Func<double, double> easing;

    /// <summary>
    /// Creates the move. <paramref name="easing"/> maps elapsed fraction to progress, both in [0, 1]; null uses
    /// <see cref="SmoothStep"/>.
    /// </summary>
    public CameraAnimation(CameraState from, CameraState to, TimeSpan duration, Func<double, double>? easing = null)
    {
        From = from;
        To = to;
        Duration = duration;
        this.easing = easing ?? DefaultEasing;
        this.from = Quaternion.Normalize(from.Orientation);
        Quaternion end = Quaternion.Normalize(to.Orientation);
        // q and -q are the same rotation; taking the one on the same side as the start gives the shorter arc.
        this.to = Quaternion.Dot(this.from, end) < 0 ? Quaternion.Negate(end) : end;
        fromTarget = from.Target;
        toTarget = to.Target;
        logFrom = MathF.Log(MathF.Max(from.ViewRadius, 1e-9f));
        logTo = MathF.Log(MathF.Max(to.ViewRadius, 1e-9f));
    }

    /// <summary>The state at the start.</summary>
    public CameraState From { get; }

    /// <summary>The state at the end.</summary>
    public CameraState To { get; }

    /// <summary>How long the move takes.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Whether the move is over at <paramref name="elapsed"/>.</summary>
    public bool IsFinishedAt(TimeSpan elapsed) => elapsed >= Duration;

    /// <summary>The state at <paramref name="elapsed"/> from the start, clamped to the two ends.</summary>
    public CameraState StateAt(TimeSpan elapsed)
    {
        if (Duration <= TimeSpan.Zero || elapsed >= Duration)
            return To;
        if (elapsed <= TimeSpan.Zero)
            return From;
        float p = (float)Math.Clamp(easing(elapsed.TotalSeconds / Duration.TotalSeconds), 0, 1);
        return new CameraState(
            Quaternion.Normalize(Quaternion.Slerp(from, to, p)),
            Vector3.Lerp(fromTarget, toTarget, p),
            MathF.Exp(logFrom + (logTo - logFrom) * p));
    }

    /// <summary>Cubic smooth step, 3t² - 2t³, on t clamped to [0, 1].</summary>
    public static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>
/// A never ending turn around a view-space axis (a turntable), one full turn per <see cref="Period"/>. The turn is
/// composed after the start orientation, as <see cref="Camera.Rotate"/> does; target and radius stay put.
/// </summary>
public sealed class CameraTurntable
{
    readonly Vector3 axis;

    /// <summary>Creates the turn from <paramref name="from"/> around <paramref name="viewAxis"/>.</summary>
    public CameraTurntable(CameraState from, Vector3 viewAxis, TimeSpan period)
    {
        if (viewAxis.LengthSquared() < 1e-20f)
            throw new ArgumentException("The axis must not be zero.", nameof(viewAxis));
        if (period <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(period), "The period must be positive.");
        From = from;
        axis = Vector3.Normalize(viewAxis);
        Period = period;
    }

    /// <summary>The state at the start.</summary>
    public CameraState From { get; }

    /// <summary>Time of one full turn.</summary>
    public TimeSpan Period { get; }

    /// <summary>The state at <paramref name="elapsed"/> from the start.</summary>
    public CameraState StateAt(TimeSpan elapsed)
    {
        // The angle uses the time inside the current turn, so it keeps its precision after hours.
        double turns = elapsed.Ticks % Period.Ticks / (double)Period.Ticks;
        var turn = Quaternion.CreateFromAxisAngle(axis, (float)(turns * 2 * Math.PI));
        return From with { Orientation = Quaternion.Normalize(Quaternion.Concatenate(From.Orientation, turn)) };
    }
}
