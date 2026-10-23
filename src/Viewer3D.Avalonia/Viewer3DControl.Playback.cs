using Avalonia;
using Avalonia.Data;

namespace Viewer3D.Avalonia;

/// <summary>
/// Playback of a <see cref="SceneSequence"/>: while <see cref="IsPlaying"/>, the control shows
/// <see cref="Sequence"/> frame by frame, driven by the same clock as the camera animation (one applied frame at most
/// every <see cref="AnimationFrameInterval"/>, advancing by elapsed time, so a slow machine skips frames instead of
/// slowing down). The camera stays free while it plays. Paused, without a sequence, or outside the visual tree, no
/// frame is requested.
/// </summary>
public partial class Viewer3DControl
{
    /// <summary>Defines <see cref="Sequence"/>.</summary>
    public static readonly StyledProperty<SceneSequence?> SequenceProperty =
        AvaloniaProperty.Register<Viewer3DControl, SceneSequence?>(nameof(Sequence));

    /// <summary>Defines <see cref="FrameIndex"/>.</summary>
    public static readonly StyledProperty<int> FrameIndexProperty =
        AvaloniaProperty.Register<Viewer3DControl, int>(nameof(FrameIndex), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Defines <see cref="IsPlaying"/>.</summary>
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<Viewer3DControl, bool>(nameof(IsPlaying), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Defines <see cref="PlaybackRate"/>.</summary>
    public static readonly StyledProperty<double> PlaybackRateProperty =
        AvaloniaProperty.Register<Viewer3DControl, double>(nameof(PlaybackRate), 1.0);

    // Time into the sequence, at PlaybackRate; it only moves while the clock runs.
    TimeSpan playhead;
    TimeSpan? lastPlaybackTick;
    TimeSpan lastPlaybackFrame;
    int playbackGeneration;
    bool playbackRunning;
    bool applyingPlayback;
    bool attached;

    /// <summary>
    /// An animation to show in place of <see cref="Scene"/>; null shows <see cref="Scene"/>. A new sequence keeps
    /// <see cref="FrameIndex"/> (limited to its frames), so a rebuilt animation goes on from where it was.
    /// </summary>
    public SceneSequence? Sequence
    {
        get => GetValue(SequenceProperty);
        set => SetValue(SequenceProperty, value);
    }

    /// <summary>
    /// The frame of <see cref="Sequence"/> on screen (from 0). Setting it goes to that frame, also while playing, and
    /// the playback goes on from there. Binds two way by default.
    /// </summary>
    public int FrameIndex
    {
        get => GetValue(FrameIndexProperty);
        set => SetValue(FrameIndexProperty, value);
    }

    /// <summary>
    /// Whether <see cref="Sequence"/> plays. A <see cref="SequenceRepeat.Once"/> sequence sets it back to false at its
    /// end, and setting it to true there plays again from the first frame. Binds two way by default.
    /// </summary>
    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    /// <summary>Speed of the playback: 1 is the time of the sequence, 2 twice as fast. Negative counts as 0.</summary>
    public double PlaybackRate
    {
        get => GetValue(PlaybackRateProperty);
        set => SetValue(PlaybackRateProperty, value);
    }

    /// <summary>Raised when <see cref="FrameIndex"/> changes, by the playback or from outside.</summary>
    public event EventHandler? FrameChanged;

    /// <summary>Whether frames are being requested for the playback right now (for tests).</summary>
    internal bool IsPlaybackClockRunning => playbackRunning;

    /// <summary>
    /// Takes the camera, colors, labels and selection of the control now (on the UI thread) and returns an object that
    /// paints the frames of <see cref="Sequence"/>, or <see cref="Scene"/> as a single frame, into PNG images on any
    /// thread.
    /// </summary>
    public SceneFrameRenderer CreateFrameRenderer(int width, int height, double scale = 1)
    {
        CheckImageSize(width, height, scale);
        Camera camera = Camera?.Clone() ?? new Camera();
        return new SceneFrameRenderer(Sequence, Scene ?? Scene.Empty, camera, CreateOptions(camera), ShowLabels, width,
            height, scale);
    }

    // The frame to paint: the low detail version while something moves, when there is one.
    Scene SceneToPaint(bool moving)
    {
        if (Sequence is { } sequence)
        {
            int i = Math.Clamp(FrameIndex, 0, sequence.Frames.Count - 1);
            Scene? low = moving || playbackRunning ? sequence.LowDetailFrames?[i] : null;
            return low ?? sequence.Frames[i];
        }
        return (moving ? LowDetailScene : null) ?? Scene ?? Scene.Empty;
    }

    // The full scene on screen now: the current frame of the sequence, or the scene.
    Scene CurrentFullScene() =>
        Sequence is { } sequence ? sequence.Frames[Math.Clamp(FrameIndex, 0, sequence.Frames.Count - 1)] : Scene ?? Scene.Empty;

    void OnPlaybackPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == SequenceProperty)
        {
            if (Sequence is { } sequence)
            {
                int index = Math.Clamp(FrameIndex, 0, sequence.Frames.Count - 1);
                if (index != FrameIndex)
                    SetFrameIndexFromPlayback(index);
                playhead = sequence.StartOf(index);
            }
            UpdatePlaybackClock();
        }
        else if (change.Property == FrameIndexProperty)
        {
            if (Sequence is { } sequence)
            {
                int index = change.GetNewValue<int>();
                int limited = Math.Clamp(index, 0, sequence.Frames.Count - 1);
                if (limited != index)
                {
                    // Brought back into range; the change to the limited value does the rest.
                    SetCurrentValue(FrameIndexProperty, limited);
                    return;
                }
                // A frame chosen from outside: the playback goes on from its start. A frame applied by the playback
                // keeps the playhead, which is already inside the frame.
                if (!applyingPlayback)
                    playhead = sequence.StartOf(index);
            }
            FrameChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (change.Property == IsPlayingProperty)
        {
            if (change.GetNewValue<bool>() && Sequence is { Repeat: SequenceRepeat.Once } once && playhead >= once.Duration)
            {
                // Played to the end: play again from the first frame.
                playhead = TimeSpan.Zero;
                SetFrameIndexFromPlayback(0);
            }
            UpdatePlaybackClock();
        }
    }

    void SetFrameIndexFromPlayback(int index)
    {
        applyingPlayback = true;
        try
        {
            SetCurrentValue(FrameIndexProperty, index);
        }
        finally
        {
            applyingPlayback = false;
        }
    }

    void OnPlaybackAttached()
    {
        attached = true;
        UpdatePlaybackClock();
    }

    void OnPlaybackDetached()
    {
        attached = false;
        UpdatePlaybackClock();
    }

    // Starts or stops asking for frames, from the state of the properties.
    void UpdatePlaybackClock()
    {
        bool run = attached && IsPlaying && Sequence is { Frames.Count: > 1 };
        if (run == playbackRunning)
            return;
        playbackRunning = run;
        // A frame already asked for finds another generation and does nothing.
        int generation = ++playbackGeneration;
        lastPlaybackTick = null;
        // The low detail frame while playing, the full one when it stops.
        InvalidateVisual();
        if (run)
            RequestFrameCallback(time => OnPlaybackFrame(generation, time));
    }

    void OnPlaybackFrame(int generation, TimeSpan time)
    {
        if (generation != playbackGeneration || Sequence is not { } sequence)
            return;
        if (lastPlaybackTick is not { } last)
        {
            // The first frame only fixes the clock.
            lastPlaybackTick = time;
            lastPlaybackFrame = time;
            RequestFrameCallback(t => OnPlaybackFrame(generation, t));
            return;
        }
        lastPlaybackTick = time;
        double rate = double.IsFinite(PlaybackRate) ? Math.Max(PlaybackRate, 0) : 0;
        playhead += TimeSpan.FromTicks((long)((time - last).Ticks * rate));
        if (time - lastPlaybackFrame >= AnimationFrameInterval)
        {
            lastPlaybackFrame = time;
            int frame = sequence.FrameAt(playhead, out bool finished);
            if (frame != FrameIndex)
                SetFrameIndexFromPlayback(frame);
            if (finished)
            {
                playhead = sequence.Duration;
                // Stops the clock through the property change.
                SetCurrentValue(IsPlayingProperty, false);
                return;
            }
        }
        if (generation == playbackGeneration)
            RequestFrameCallback(t => OnPlaybackFrame(generation, t));
    }
}
