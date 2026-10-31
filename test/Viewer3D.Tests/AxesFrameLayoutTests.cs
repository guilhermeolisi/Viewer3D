using System.Numerics;

namespace Viewer3D.Tests;

public class AxesFrameLayoutTests
{
    static readonly AxesFrameLimits Unit = new(-1, 1, 0.5);
    static readonly AxesFrameOptions Options = new(["x (nm)", "y (nm)", "z (nm)"]);

    static AxesFrameView Looking(Vector3 direction, Vector3 up, float? eyeDistance = null)
    {
        var camera = new Camera();
        camera.LookAlong(direction, up);
        return new AxesFrameView(camera.Orientation, Vector3.Zero, eyeDistance);
    }

    // Oracle: by hand. Identity view looks along -z: only the face with normal -z has n . d = 1 > 0; the four side faces
    // are edge on (n . d = 0) and the +z face is in front. An edge along x is on the outline when one of its faces is the
    // -z face (z side -1); of the two, the lowest on the screen is at y = -1. The z axis points at the viewer: no ticks.
    [Fact]
    public void Layout_IdentityView_BackIsTheMinusZFaceAndTheXTicksGoOnTheBottomEdge()
    {
        AxesFrameLayout layout = AxesFrame.Layout(Options, Unit, new AxesFrameView(Quaternion.Identity, Vector3.Zero));

        Assert.Equal(new[] { (2, -1) }, layout.BackFaces);
        Assert.Equal((-1, -1), layout.TickEdges[0]);
        Assert.Null(layout.TickEdges[2]);
        Assert.DoesNotContain(layout.Labels, l => l.Axis == 2);
    }

    // Oracle: by hand. Looking along d = (-1, -1, -1) (from the +x +y +z corner), n . d > 0 only for the normals -x, -y,
    // -z: the three back planes. An edge along x sits at (y side, z side); its faces are (y, side) and (z, side), so it is
    // on the outline when the two sides differ: (+1, -1) or (-1, +1). With z up, the lower one is z = -1, so (y +1, z -1).
    // The front edge (+1, +1) has both faces in front and lies over the scene: never chosen.
    [Fact]
    public void Layout_IsometricView_BackPlanesAreTheThreeMinusFacesAndTicksAvoidTheFrontEdge()
    {
        AxesFrameLayout layout = AxesFrame.Layout(Options, Unit, Looking(new Vector3(-1, -1, -1), Vector3.UnitZ));

        Assert.Equal(new[] { (0, -1), (1, -1), (2, -1) }, layout.BackFaces);
        Assert.Equal((1, -1), layout.TickEdges[0]);   // x: y = +1, z = -1
        Assert.Equal((-1, 1), layout.TickEdges[1]);   // y: z = -1, x = +1
        foreach ((int, int)? edge in layout.TickEdges)
            Assert.NotEqual((1, 1), edge);
    }

    // Oracle: by hand. Looking along d = (-2, -1, -1) with z up, the screen up direction is z - (z . d)d/|d|^2 =
    // (-1/3, -1/6, 5/6). The z edges on the outline are at (x -1, y +1) and (x +1, y -1); their screen heights are
    // (-1/3)(-1) + (-1/6)(1) = 1/6 and -1/6, so the lower is (x +1, y -1).
    [Fact]
    public void Layout_ObliqueView_ZTicksGoOnTheLowerOutlineEdge()
    {
        AxesFrameLayout layout = AxesFrame.Layout(Options, Unit, Looking(new Vector3(-2, -1, -1), Vector3.UnitZ));

        Assert.Equal(new[] { (0, -1), (1, -1), (2, -1) }, layout.BackFaces);
        Assert.Equal((1, -1), layout.TickEdges[2]);
    }

    [Fact]
    public void Layout_ViewAlongAnAxis_LeavesThatAxisWithoutTicks()
    {
        AxesFrameLayout layout = AxesFrame.Layout(Options, Unit, Looking(-Vector3.UnitX, Vector3.UnitZ));

        Assert.Null(layout.TickEdges[0]);
        Assert.NotNull(layout.TickEdges[1]);
        Assert.NotNull(layout.TickEdges[2]);
        Assert.DoesNotContain(layout.Front, s => s.Axis == 0);
    }

    // Oracle: by hand. Identity orientation with the eye at (0, 0, 1.5), inside the slab of the side faces: for the face
    // x = +1, n = (1, 0, 0) and center - eye = (1, 0, -1.5), dot 1 > 0, so it is seen from inside (back); the same for
    // x = -1, y = +1 and y = -1. The -z face is back too; the +z face, center - eye = (0, 0, -0.5), is in front. The
    // orthographic view of the same orientation has only the -z face at the back.
    [Fact]
    public void Layout_NearPerspectiveCamera_ChoosesTheFacesFromTheEyePosition()
    {
        AxesFrameLayout near = AxesFrame.Layout(Options, Unit, new AxesFrameView(Quaternion.Identity, Vector3.Zero, 1.5f));
        AxesFrameLayout ortho = AxesFrame.Layout(Options, Unit, new AxesFrameView(Quaternion.Identity, Vector3.Zero));

        Assert.Equal(new[] { (0, -1), (0, 1), (1, -1), (1, 1), (2, -1) }, near.BackFaces);
        Assert.Equal(new[] { (2, -1) }, ortho.BackFaces);
    }

    [Fact]
    public void Layout_TicksNumbersAndTitles_OnePerTickAndOneTitlePerAxis()
    {
        AxesFrameLayout layout = AxesFrame.Layout(Options, Unit, Looking(new Vector3(-1, -1, -1), Vector3.UnitZ));

        for (int axis = 0; axis < 3; axis++)
        {
            Assert.Equal(5, layout.Front.Count(s => s.Axis == axis && s.Kind == AxesFrameSegmentKind.Tick));
            Assert.Equal(
                new[] { "-1.0", "-0.5", "0.0", "0.5", "1.0" },
                layout.Labels.Where(l => l.Axis == axis && !l.IsTitle).Select(l => l.Text));
            Assert.Equal(Options.AxisTitles[axis], Assert.Single(layout.Labels, l => l.Axis == axis && l.IsTitle).Text);
        }
    }

    // Three back planes with five ticks have three inner grid lines each way on each plane, and nine distinct edges.
    [Fact]
    public void Layout_Grid_OnlyOnTheBackPlanesAndOffWhenAsked()
    {
        AxesFrameView view = Looking(new Vector3(-1, -1, -1), Vector3.UnitZ);
        AxesFrameLayout withGrid = AxesFrame.Layout(Options, Unit, view);
        AxesFrameLayout noGrid = AxesFrame.Layout(Options with { ShowGrid = false }, Unit, view);

        Assert.Equal(3 * 2 * 3, withGrid.Back.Count(s => s.Kind == AxesFrameSegmentKind.Grid));
        Assert.Equal(9, withGrid.Back.Count(s => s.Kind == AxesFrameSegmentKind.Edge));
        Assert.All(
            withGrid.Back.Where(s => s.Kind == AxesFrameSegmentKind.Grid),
            s => Assert.True(Math.Min(Math.Min(s.Start.X, s.Start.Y), s.Start.Z) == -1f && Math.Min(Math.Min(s.End.X, s.End.Y), s.End.Z) == -1f));
        Assert.Empty(noGrid.Back.Where(s => s.Kind == AxesFrameSegmentKind.Grid));
    }

    [Fact]
    public void Layout_TickMarksPointOutOfTheBox()
    {
        AxesFrameLayout layout = AxesFrame.Layout(Options, Unit, Looking(new Vector3(-1, -1, -1), Vector3.UnitZ));

        Assert.All(layout.Front, s => Assert.True(s.End.Length() > s.Start.Length()));
    }

    [Fact]
    public void Layout_KeepsTheTitle()
    {
        AxesFrameLayout layout = AxesFrame.Layout(Options with { Title = "Size (nm)" }, Unit, AxesFrameView.FromCamera(new Camera()));

        Assert.Equal("Size (nm)", layout.Title);
    }
}
