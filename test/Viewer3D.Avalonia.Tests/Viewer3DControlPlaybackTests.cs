using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

public class Viewer3DControlPlaybackTests
{
    static readonly Rgba Red = new(220, 30, 30);
    static readonly TimeSpan Tenth = TimeSpan.FromMilliseconds(100);

    /// <summary>Hands out animation frames at the times the test chooses, in place of the screen.</summary>
    sealed class FrameClock
    {
        readonly List<Action<TimeSpan>> pending = [];

        public int Pending => pending.Count;

        public void Request(Action<TimeSpan> callback) => pending.Add(callback);

        public void Tick(double milliseconds)
        {
            Action<TimeSpan>[] now = [.. pending];
            pending.Clear();
            foreach (Action<TimeSpan> callback in now)
                callback(TimeSpan.FromMilliseconds(milliseconds));
        }
    }

    sealed class PlayingSource : INotifyPropertyChanged
    {
        bool playing;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool Playing
        {
            get => playing;
            set
            {
                playing = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Playing)));
            }
        }
    }

    // One sphere with the same Id in every frame, moving along x.
    static Scene Frame(float x, float radius = 0.3f) => new([new SphereItem { Id = 7, Center = new Vector3(x, 0, 0), Radius = radius, Color = Red }]);

    static SceneSequence Sequence(int count = 3, SequenceRepeat repeat = SequenceRepeat.Loop, bool lowDetail = false)
    {
        var frames = Enumerable.Range(0, count).Select(i => Frame(-0.6f + 0.6f * i)).ToArray();
        Scene?[]? low = lowDetail ? Enumerable.Range(0, count).Select(i => (Scene?)Frame(-0.6f + 0.6f * i, 0.2f)).ToArray() : null;
        return new SceneSequence(frames, Tenth, repeat, low);
    }

    static (Window Window, Viewer3DControl Control, FrameClock Clock) Shown(SceneSequence sequence)
    {
        var clock = new FrameClock();
        var control = new Viewer3DControl { Sequence = sequence, RequestFrame = clock.Request };
        control.FrameScene();
        Window window = TestWindows.Show(control);
        return (window, control, clock);
    }

    [AvaloniaFact]
    public void Playing_AdvancesByElapsedTime_AndRaisesFrameChangedOncePerChange()
    {
        var (_, control, clock) = Shown(Sequence());
        int changes = 0;
        control.FrameChanged += (_, _) => changes++;

        control.IsPlaying = true;
        clock.Tick(1000);
        Assert.Equal(0, control.FrameIndex);
        clock.Tick(1150);
        Assert.Equal(1, control.FrameIndex);
        clock.Tick(1170);
        Assert.Equal(1, control.FrameIndex);
        clock.Tick(1250);
        Assert.Equal(2, control.FrameIndex);
        clock.Tick(1310);

        Assert.Equal(0, control.FrameIndex);
        Assert.Equal(3, changes);
    }

    [AvaloniaFact]
    public void PlaybackRate_ScalesTheTime()
    {
        var (_, control, clock) = Shown(Sequence(count: 5));
        control.PlaybackRate = 2;

        control.IsPlaying = true;
        clock.Tick(0);
        clock.Tick(150);

        Assert.Equal(3, control.FrameIndex);
    }

    [AvaloniaFact]
    public void Pausing_StopsAskingForFrames()
    {
        var (_, control, clock) = Shown(Sequence());
        control.IsPlaying = true;
        clock.Tick(0);
        clock.Tick(150);

        control.IsPlaying = false;
        // The frame asked for before the pause runs and does nothing.
        clock.Tick(300);

        Assert.Equal(0, clock.Pending);
        Assert.False(control.IsPlaybackClockRunning);
        Assert.Equal(1, control.FrameIndex);
    }

    [AvaloniaFact]
    public void Detached_StopsAskingForFrames_AndAttachedAgain_GoesOn()
    {
        var (window, control, clock) = Shown(Sequence());
        control.IsPlaying = true;
        clock.Tick(0);

        window.Content = null;
        clock.Tick(50);
        Assert.Equal(0, clock.Pending);
        Assert.True(control.IsPlaying);

        window.Content = control;
        Assert.Equal(1, clock.Pending);
        clock.Tick(1000);
        clock.Tick(1150);
        Assert.Equal(1, control.FrameIndex);
    }

    [AvaloniaFact]
    public void Once_StopsOnTheLastFrame_AndSetsIsPlayingBackToFalse()
    {
        var (_, control, clock) = Shown(Sequence(repeat: SequenceRepeat.Once));

        control.IsPlaying = true;
        clock.Tick(0);
        clock.Tick(150);
        clock.Tick(250);
        clock.Tick(350);

        Assert.Equal(2, control.FrameIndex);
        Assert.False(control.IsPlaying);
        Assert.Equal(0, clock.Pending);

        // Playing again starts from the first frame.
        control.IsPlaying = true;
        Assert.Equal(0, control.FrameIndex);
        Assert.Equal(1, clock.Pending);
    }

    [AvaloniaFact]
    public void IsPlayingBoundOneWay_SurvivesTheEndOfAOncePlayback()
    {
        var source = new PlayingSource { Playing = true };
        var clock = new FrameClock();
        var control = new Viewer3DControl { Sequence = Sequence(repeat: SequenceRepeat.Once), RequestFrame = clock.Request };
        control.Bind(Viewer3DControl.IsPlayingProperty, new Binding(nameof(PlayingSource.Playing)) { Source = source, Mode = BindingMode.OneWay });
        TestWindows.Show(control);
        clock.Tick(0);
        clock.Tick(350);
        Assert.False(control.IsPlaying);

        source.Playing = false;
        source.Playing = true;

        Assert.True(control.IsPlaying);
        Assert.Equal(0, control.FrameIndex);
        Assert.Equal(1, clock.Pending);
    }

    [AvaloniaFact]
    public void FrameIndexSetWhilePlaying_GoesOnFromThatFrame()
    {
        var (_, control, clock) = Shown(Sequence(count: 4));
        control.IsPlaying = true;
        clock.Tick(0);
        clock.Tick(50);
        Assert.Equal(0, control.FrameIndex);

        control.FrameIndex = 2;
        clock.Tick(100);
        Assert.Equal(2, control.FrameIndex);
        clock.Tick(160);

        Assert.Equal(3, control.FrameIndex);
    }

    [AvaloniaFact]
    public void FrameIndexSetInPingPong_GoesOnForwardThenBack()
    {
        var (_, control, clock) = Shown(Sequence(repeat: SequenceRepeat.PingPong));

        control.FrameIndex = 1;
        control.IsPlaying = true;
        clock.Tick(0);
        clock.Tick(150);
        Assert.Equal(2, control.FrameIndex);
        clock.Tick(250);

        Assert.Equal(1, control.FrameIndex);
    }

    [AvaloniaFact]
    public void FrameIndexOutOfRange_IsBroughtBackIntoTheSequence()
    {
        var (_, control, _) = Shown(Sequence());

        control.FrameIndex = 9;
        Assert.Equal(2, control.FrameIndex);

        control.Sequence = Sequence(count: 2);
        Assert.Equal(1, control.FrameIndex);
    }

    [AvaloniaFact]
    public void SelectionAndPicking_FollowTheCurrentFrame()
    {
        var (window, control, clock) = Shown(Sequence());
        control.SelectedItemId = 7;
        TestWindows.Capture(window);
        Scene first = control.LastPaintedScene!;
        var clicks = new List<ItemClickedEventArgs>();
        control.ItemClicked += (_, e) => clicks.Add(e);

        control.IsPlaying = true;
        clock.Tick(0);
        clock.Tick(150);
        control.IsPlaying = false;
        TestWindows.Capture(window);

        Assert.Equal(7, control.SelectedItemId);
        Assert.Same(control.Sequence!.Frames[1], control.LastPaintedScene);
        Assert.NotSame(first, control.LastPaintedScene);
        // Frame 1 has its sphere in the middle; frame 0 had it on the left.
        window.MouseDown(new Point(150, 100), MouseButton.Left);
        window.MouseUp(new Point(150, 100), MouseButton.Left);
        Assert.Equal(7, Assert.Single(clicks).Pick?.ItemId);
        Point left = new(150 - 0.6 * 100 / control.Camera!.State.ViewRadius, 100);
        window.MouseDown(left, MouseButton.Left);
        window.MouseUp(left, MouseButton.Left);
        Assert.Null(clicks[1].Pick);
    }

    [AvaloniaFact]
    public void Playing_PaintsTheLowDetailFrame_AndPausedTheFullOne()
    {
        var (window, control, clock) = Shown(Sequence(lowDetail: true));

        control.IsPlaying = true;
        clock.Tick(0);
        clock.Tick(150);
        TestWindows.Capture(window);
        Assert.Same(control.Sequence!.LowDetailFrames![1], control.LastPaintedScene);

        control.IsPlaying = false;
        TestWindows.Capture(window);
        Assert.Same(control.Sequence.Frames[1], control.LastPaintedScene);
    }

    [AvaloniaFact]
    public void MovingTheCamera_DoesNotStopThePlayback()
    {
        var (_, control, clock) = Shown(Sequence());
        control.IsPlaying = true;
        clock.Tick(0);

        control.Camera!.Orbit(30, 10);
        clock.Tick(150);

        Assert.True(control.IsPlaying);
        Assert.Equal(1, control.FrameIndex);
        Assert.Equal(1, clock.Pending);
    }

    [AvaloniaFact]
    public void FrameScene_FramesTheWholeSequence()
    {
        var far = new SceneSequence([Frame(0), Frame(20)], Tenth);
        var control = new Viewer3DControl { Sequence = far };

        control.FrameScene();

        Assert.Equal(far.Center, control.Camera!.State.Target);
    }

    [AvaloniaFact]
    public void FrameRenderer_OnAnotherThread_PaintsWhatRenderToPngPaints()
    {
        var (_, control, _) = Shown(Sequence());
        control.FrameIndex = 1;
        byte[] expected = control.RenderToPng(120, 80);

        SceneFrameRenderer renderer = control.CreateFrameRenderer(120, 80);
        byte[][] painted = Task.Run(() => Enumerable.Range(0, renderer.FrameCount).Select(renderer.Render).ToArray()).GetAwaiter().GetResult();

        Assert.Equal(3, painted.Length);
        Assert.Equal(expected, painted[1]);
        Assert.NotEqual(expected, painted[0]);
    }
}
