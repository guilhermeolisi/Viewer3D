using System.Numerics;
using Avalonia.Controls;

namespace Viewer3D.Avalonia;

/// <summary>
/// Camera animation: a timed move to a camera state and a never ending turntable. Frames come from
/// <see cref="TopLevel.RequestAnimationFrame"/>, in step with the screen, and the move advances by elapsed time, so a
/// slow machine shows the same duration with fewer frames. At most one frame is applied every
/// <see cref="AnimationFrameInterval"/>. While a move runs the control paints <see cref="LowDetailScene"/>, as during a
/// gesture. When nothing moves, no frame is requested.
/// </summary>
public partial class Viewer3DControl
{
    /// <summary>Shortest time between two applied animation frames: about 30 frames per second.</summary>
    public static readonly TimeSpan AnimationFrameInterval = TimeSpan.FromSeconds(1.0 / 30);

    CameraAnimation? animation;
    CameraTurntable? turntable;
    TimeSpan? animationStart;
    TimeSpan lastAnimationFrame;
    int animationGeneration;
    bool applyingAnimation;

    /// <summary>
    /// Asks for one animation frame (for tests, which hand out the frame times themselves). Null uses
    /// <see cref="TopLevel.RequestAnimationFrame"/> of the control's top level.
    /// </summary>
    internal Action<Action<TimeSpan>>? RequestFrame { get; set; }

    /// <summary>Whether a camera move or a turntable is running.</summary>
    public bool IsCameraAnimating => animation is not null || turntable is not null;

    /// <summary>
    /// Moves the camera to <paramref name="target"/> over <paramref name="duration"/>, from where it is now (also from
    /// the middle of another move). A zero duration, or a control outside the visual tree, applies the target at once.
    /// <paramref name="easing"/> maps elapsed fraction to progress; null uses <see cref="CameraAnimation.SmoothStep"/>.
    /// </summary>
    public void AnimateCamera(CameraState target, TimeSpan duration, Func<double, double>? easing = null)
    {
        if (Camera is not { } camera)
            return;
        StopAnimation();
        if (duration <= TimeSpan.Zero || VisualRoot is null)
        {
            ApplyAnimationState(camera, target);
            return;
        }
        animation = new CameraAnimation(camera.State, target, duration, easing);
        BeginAnimation();
    }

    /// <summary>
    /// Turns the camera around <paramref name="viewAxis"/> (view space: y is up on the screen), one full turn per
    /// <paramref name="period"/>, until <see cref="StopTurntable"/>, a gesture or a camera change from elsewhere.
    /// Does nothing outside the visual tree.
    /// </summary>
    public void StartTurntable(Vector3 viewAxis, TimeSpan period)
    {
        if (Camera is not { } camera || VisualRoot is null)
            return;
        StopAnimation();
        turntable = new CameraTurntable(camera.State, viewAxis, period);
        BeginAnimation();
    }

    /// <summary>Stops the turntable, leaving the camera where it is.</summary>
    public void StopTurntable()
    {
        if (turntable is not null)
            StopAnimation();
    }

    /// <summary>Stops any camera move or turntable, leaving the camera where it is.</summary>
    public void StopAnimation()
    {
        if (!IsCameraAnimating)
            return;
        animation = null;
        turntable = null;
        animationStart = null;
        // A frame already asked for finds another generation and does nothing.
        animationGeneration++;
        // The full scene comes back.
        InvalidateVisual();
    }

    void BeginAnimation()
    {
        animationStart = null;
        int generation = ++animationGeneration;
        InvalidateVisual();
        RequestAnimationFrame(generation);
    }

    void RequestAnimationFrame(int generation)
    {
        Action<TimeSpan> callback = time => OnAnimationFrame(generation, time);
        if (RequestFrame is { } request)
            request(callback);
        else
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(callback);
    }

    void OnAnimationFrame(int generation, TimeSpan time)
    {
        if (generation != animationGeneration || Camera is not { } camera)
            return;
        if (animationStart is not { } start)
        {
            // The first frame only fixes the clock: the camera is still at the start.
            animationStart = time;
            lastAnimationFrame = time;
            RequestAnimationFrame(generation);
            return;
        }
        TimeSpan elapsed = time - start;
        if (animation is { } move && move.IsFinishedAt(elapsed))
        {
            animation = null;
            animationStart = null;
            animationGeneration++;
            // IsCameraAnimating is already false, so the frame this change asks for paints the full scene.
            ApplyAnimationState(camera, move.To);
            return;
        }
        if (time - lastAnimationFrame >= AnimationFrameInterval)
        {
            lastAnimationFrame = time;
            CameraState state = animation is { } running ? running.StateAt(elapsed) : turntable!.StateAt(elapsed);
            ApplyAnimationState(camera, state);
        }
        RequestAnimationFrame(generation);
    }

    void ApplyAnimationState(Camera camera, in CameraState state)
    {
        applyingAnimation = true;
        try
        {
            camera.Set(state);
        }
        finally
        {
            applyingAnimation = false;
        }
    }
}
