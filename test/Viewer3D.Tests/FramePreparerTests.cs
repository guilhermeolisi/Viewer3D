using System.Numerics;

namespace Viewer3D.Tests;

public class FramePreparerTests
{
    static MeshItem Triangle(float z, Rgba color, int id = 0, bool reversed = false, bool doubleSided = false)
    {
        Vector3[] p = [new(-1, -1, z), new(1, -1, z), new(0, 1, z)];
        return new MeshItem
        {
            Id = id,
            Positions = p,
            Indices = reversed ? [0, 2, 1] : [0, 1, 2],
            Color = color,
            DoubleSided = doubleSided,
        };
    }

    static DrawList Prepare(params SceneItem[] items) => new FramePreparer().Prepare(new Scene(items), new Camera { ViewRadius = 2 }, 200, 200);

    [Fact]
    public void Prepare_KeepsTrianglesFacingTheViewer()
    {
        DrawList list = Prepare(Triangle(0, Rgba.White));

        Assert.Equal(1, list.TriangleCount);
    }

    [Fact]
    public void Prepare_DropsBackFaces()
    {
        DrawList list = Prepare(Triangle(0, Rgba.White, reversed: true));

        Assert.Equal(0, list.TriangleCount);
    }

    [Fact]
    public void Prepare_KeepsBackFacesOfDoubleSidedMeshes()
    {
        DrawList list = Prepare(Triangle(0, Rgba.White, reversed: true, doubleSided: true));

        Assert.Equal(1, list.TriangleCount);
    }

    [Fact]
    public void Prepare_FaceTowardsTheViewerIsFullyLit_TiltedFaceIsDarker()
    {
        // Face tilted 60 degrees: light = ambient + (1 - ambient) cos 60.
        var tilted = new MeshItem
        {
            Positions = [new(0, 0, 0), new(1, 0, 0), new(0, 0.5f, -MathF.Sqrt(3) / 2)],
            Indices = [0, 1, 2],
            Color = Rgba.White,
        };

        DrawList facing = Prepare(Triangle(0, Rgba.White));
        DrawList away = Prepare(tilted);

        Assert.Equal(Rgba.White.ToArgb(), facing.TriangleColors[0]);
        float expected = FramePreparer.Ambient + (1 - FramePreparer.Ambient) * 0.5f;
        Assert.Equal((byte)(255 * expected + 0.5f), Rgba.FromArgb(away.TriangleColors[0]).R);
    }

    /// <summary>A triangle counter-clockwise around <paramref name="normal"/> (the default camera keeps world = view).</summary>
    static MeshItem Facing(Vector3 normal, Rgba color, bool reversed = false, bool doubleSided = false)
    {
        Vector3 n = Vector3.Normalize(normal);
        Vector3 u = Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));
        Vector3 v = Vector3.Cross(n, u);
        return new MeshItem
        {
            Positions = [u, v, -u - v],
            Indices = reversed ? [0, 2, 1] : [0, 1, 2],
            Color = color,
            DoubleSided = doubleSided,
            Lighting = MeshLighting.KeyLight,
        };
    }

    static byte Red(MeshItem mesh) => Rgba.FromArgb(Prepare(mesh).TriangleColors[0]).R;

    [Fact]
    public void MeshItem_IsLitByTheHeadlightByDefault()
    {
        Assert.Equal(MeshLighting.Headlight, Triangle(0, Rgba.White).Lighting);
    }

    [Fact]
    public void Prepare_KeyLight_SlopeTowardsTheUpperLeftIsBrighterThanTheFrontAndTheFrontThanTheLowerRight()
    {
        byte upperLeft = Red(Facing(new(-0.4f, 0.4f, 1), Rgba.White));
        byte front = Red(Facing(Vector3.UnitZ, Rgba.White));
        byte lowerRight = Red(Facing(new(0.4f, -0.4f, 1), Rgba.White));

        Assert.True(upperLeft > front, $"{upperLeft} > {front}");
        Assert.True(front > lowerRight, $"{front} > {lowerRight}");
    }

    [Fact]
    public void Prepare_KeyLight_FaceTurnedAwayFromBothLightsKeepsTheAmbient_SoItsColorShows()
    {
        // Normal (-0.5, -0.5, 0.02): the dot is negative with the key light (-0.45, 0.55, 0.70) and with the fill
        // light (0.5, -0.4, 0.75), and the face still looks a little towards the viewer.
        Vector3 away = new(-0.5f, -0.5f, 0.02f);
        Assert.True(Vector3.Dot(Vector3.Normalize(away), FramePreparer.KeyLightDirection) < 0);
        Assert.True(Vector3.Dot(Vector3.Normalize(away), FramePreparer.FillLightDirection) < 0);

        Rgba color = Rgba.FromArgb(Prepare(Facing(away, new Rgba(40, 80, 200))).TriangleColors[0]);

        Assert.Equal(new Rgba(40, 80, 200).Shade(FramePreparer.KeyLightAmbient), color);
        Assert.True(FramePreparer.KeyLightAmbient > FramePreparer.Ambient);
    }

    [Fact]
    public void Prepare_KeyLight_FillLightBrightensTheSideTheKeyLightLeavesDark()
    {
        // Normal (0.6, -0.6, 0.2): away from the key light, towards the fill light.
        byte red = Red(Facing(new(0.6f, -0.6f, 0.2f), Rgba.White));

        Assert.True(red > (byte)(255 * FramePreparer.KeyLightAmbient + 0.5f), $"{red}");
    }

    [Fact]
    public void Prepare_KeyLight_FaceHalfwayBetweenLightAndEyeShowsTheHighlight()
    {
        // A black face has no diffuse color to show: what it shows is the highlight, Highlight at its peak.
        Vector3 halfway = Vector3.Normalize(FramePreparer.KeyLightDirection + Vector3.UnitZ);

        byte red = Red(Facing(halfway, Rgba.Black));

        int peak = (int)(255 * FramePreparer.Highlight + 0.5f);
        Assert.InRange(red, peak - 2, peak);
    }

    [Fact]
    public void Prepare_KeyLight_BackOfADoubleSidedSurfaceIsLitLikeItsFront()
    {
        Vector3 normal = new(-0.3f, 0.2f, 1);

        uint front = Prepare(Facing(normal, new Rgba(200, 120, 40), doubleSided: true)).TriangleColors[0];
        uint back = Prepare(Facing(normal, new Rgba(200, 120, 40), reversed: true, doubleSided: true)).TriangleColors[0];

        Assert.Equal(front, back);
    }

    [Fact]
    public void Prepare_KeyLight_KeepsTheAlpha()
    {
        Vector3 halfway = Vector3.Normalize(FramePreparer.KeyLightDirection + Vector3.UnitZ);

        byte alpha = Rgba.FromArgb(Prepare(Facing(halfway, new Rgba(10, 20, 30, 128))).TriangleColors[0]).A;

        Assert.Equal(128, alpha);
    }

    [Fact]
    public void Prepare_UsesVertexColorsBeforeFaceColors()
    {
        var mesh = new MeshItem
        {
            Positions = [new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
            VertexColors = [new(255, 0, 0), new(0, 255, 0), new(0, 0, 255)],
            FaceColors = [Rgba.Black],
        };

        DrawList list = Prepare(mesh);

        Assert.Equal(new Rgba(255, 0, 0).ToArgb(), list.TriangleColors[0]);
        Assert.Equal(new Rgba(0, 255, 0).ToArgb(), list.TriangleColors[1]);
        Assert.Equal(new Rgba(0, 0, 255).ToArgb(), list.TriangleColors[2]);
    }

    [Fact]
    public void Prepare_OrdersOverlappingTrianglesFromBackToFront()
    {
        // The near triangle is given first; it must still be painted last.
        DrawList list = Prepare(Triangle(0.5f, Rgba.White, id: 1), Triangle(-0.5f, Rgba.Black, id: 2));

        Assert.Equal(2, list.CommandCount);
        Assert.Equal(1, list.TriangleItems[DrawList.IndexOf(list.Commands[0])]);
        Assert.Equal(0, list.TriangleItems[DrawList.IndexOf(list.Commands[1])]);
    }

    [Fact]
    public void Prepare_OrdersOverlappingSpheresFromBackToFront()
    {
        var near = new SphereItem { Center = new Vector3(0, 0, 1), Radius = 0.5f };
        var far = new SphereItem { Center = new Vector3(0, 0, -1), Radius = 0.5f };

        DrawList list = Prepare(near, far);

        Assert.Equal(DrawKind.Circle, DrawList.KindOf(list.Commands[0]));
        Assert.Equal(1, list.CircleItems[DrawList.IndexOf(list.Commands[0])]);
        Assert.Equal(0, list.CircleItems[DrawList.IndexOf(list.Commands[1])]);
    }

    [Fact]
    public void Prepare_SphereRadiusIsScaledToPixels()
    {
        // 200 px for a view radius of 2: 50 px per unit.
        DrawList list = Prepare(new SphereItem { Center = Vector3.Zero, Radius = 0.5f });

        Assert.Equal(100f, list.Circles[0], 3);
        Assert.Equal(100f, list.Circles[1], 3);
        Assert.Equal(25f, list.Circles[2], 3);
    }

    [Fact]
    public void Prepare_RodIsCutAtTheSpheresAndSplitInTwoHalves()
    {
        var rod = new RodItem
        {
            Start = new Vector3(-1, 0, 0),
            End = new Vector3(1, 0, 0),
            Radius = 0.1f,
            StartTrim = 0.5f,
            EndTrim = 0.5f,
            StartColor = new Rgba(255, 0, 0),
            EndColor = new Rgba(0, 0, 255),
        };

        DrawList list = Prepare(rod);

        // 50 px per unit: the visible part runs from x = -0.5 (75 px) to 0.5 (125 px), split at 0 (100 px).
        Assert.Equal(2, list.SegmentCount);
        ReadOnlySpan<float> s = list.Segments;
        Assert.Equal(75f, s[0], 3);
        Assert.Equal(100f, s[2], 3);
        Assert.Equal(100f, s[5], 3);
        Assert.Equal(125f, s[7], 3);
        Assert.Equal(10f, s[4], 3);
        Assert.Equal(new Rgba(255, 0, 0).ToArgb(), list.SegmentColors[0]);
        Assert.Equal(new Rgba(0, 0, 255).ToArgb(), list.SegmentColors[1]);
        Assert.True(list.SegmentIsRod[0]);
    }

    [Fact]
    public void Prepare_RodHiddenInsideItsSpheresIsNotDrawn()
    {
        var rod = new RodItem { Start = new Vector3(-0.4f, 0, 0), End = new Vector3(0.4f, 0, 0), StartTrim = 0.5f, EndTrim = 0.5f };

        Assert.Equal(0, Prepare(rod).SegmentCount);
    }

    [Fact]
    public void Prepare_RodIsPaintedBeforeTheSphereAtItsEnd()
    {
        var sphere = new SphereItem { Center = new Vector3(0, 0, 1), Radius = 0.3f };
        var rod = new RodItem { Start = new Vector3(0, 0, 1), End = new Vector3(1, 0, 1), StartTrim = 0.3f };

        DrawList list = Prepare(sphere, rod);

        uint lastCommand = list.Commands[^1];
        Assert.Equal(DrawKind.Circle, DrawList.KindOf(lastCommand));
    }

    [Fact]
    public void Prepare_LabelsAreKeptApartAndCanBeLeftOut()
    {
        var label = new LabelItem { Position = Vector3.Zero, Text = "a", Offset = new Vector2(3, -4) };

        DrawList with = Prepare(label);
        DrawList without = new FramePreparer { IncludeLabels = false }.Prepare(new Scene([label]), new Camera { ViewRadius = 2 }, 200, 200);

        Assert.Equal(1, with.LabelCount);
        Assert.Equal(0, with.CommandCount);
        Assert.Equal(103f, with.Labels[0], 3);
        Assert.Equal(96f, with.Labels[1], 3);
        Assert.Equal("a", with.LabelTexts[0]);
        Assert.Null(with.LabelColorAt(0));
        Assert.Equal(0, without.LabelCount);
    }

    [Fact]
    public void Prepare_LinePartInsideASphereIsCutOut()
    {
        // An atom at a cell corner hides the end of the edge: nothing of the line is left between x = -0.5 and 0.5,
        // which is 75 to 125 px. Half a pixel is the overlap of the joints.
        var sphere = new SphereItem { Center = Vector3.Zero, Radius = 0.5f };
        var line = new LineItem { Start = new Vector3(-1, 0, 0), End = new Vector3(1, 0, 0), Width = 1 };

        DrawList list = Prepare(sphere, line);

        Assert.True(list.SegmentCount > 0);
        ReadOnlySpan<float> s = list.Segments;
        for (int i = 0; i < list.SegmentCount; i++)
        {
            float lo = MathF.Min(s[5 * i], s[5 * i + 2]), hi = MathF.Max(s[5 * i], s[5 * i + 2]);
            Assert.True(hi <= 75.5f || lo >= 124.5f, $"segment {i} runs from {lo} to {hi} px");
        }
    }

    [Fact]
    public void Prepare_BackOfALineIsPaintedBeforeASphereInFrontOfIt()
    {
        // The line runs from z = -2, right behind the sphere, to z = 2: its middle is in front of the sphere, but the
        // part over the sphere's disc is behind it and must be painted first.
        var sphere = new SphereItem { Center = new Vector3(0, 0, -1), Radius = 0.3f };
        var line = new LineItem { Start = new Vector3(0, 0, -2), End = new Vector3(2, 0, 2), Width = 1 };

        DrawList list = Prepare(sphere, line);

        ReadOnlySpan<float> c = list.Circles;
        float cx = c[0], cy = c[1], radius = c[2];
        int circleAt = -1;
        for (int k = 0; k < list.CommandCount; k++)
        {
            uint command = list.Commands[k];
            if (DrawList.KindOf(command) == DrawKind.Circle)
            {
                circleAt = k;
                continue;
            }
            if (circleAt < 0)
                continue;
            int i = DrawList.IndexOf(command);
            ReadOnlySpan<float> s = list.Segments.Slice(5 * i, 4);
            Assert.True(DistanceToSegment(cx, cy, s[0], s[1], s[2], s[3]) >= radius,
                $"segment {i}, painted after the sphere, crosses its disc");
        }
        Assert.True(circleAt > 0);
    }

    static float DistanceToSegment(float px, float py, float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1, dy = y2 - y1;
        float t = Math.Clamp(((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy), 0, 1);
        float ex = x1 + t * dx - px, ey = y1 + t * dy - py;
        return MathF.Sqrt(ex * ex + ey * ey);
    }

    [Fact]
    public void Prepare_Ellipsoid_IsAnEllipseWithTheDepthOfItsCenter()
    {
        // ViewRadius 2 em 200 px: 50 px por unidade, centro em (100, 100).
        var ellipsoid = new EllipsoidItem
        {
            Id = 4,
            Center = new Vector3(0, 0, -0.5f),
            Axes = [new(1, 0, 0), new(0, 0.5f, 0), new(0, 0, 0.25f)],
            Color = Rgba.White,
        };

        DrawList list = Prepare(Triangle(0, Rgba.White), ellipsoid);

        Assert.Equal(1, list.EllipseCount);
        uint first = list.Commands[0];
        Assert.Equal(DrawKind.Ellipse, DrawList.KindOf(first));
        ReadOnlySpan<float> e = list.Ellipses;
        Assert.Equal(100f, e[0], 3);
        Assert.Equal(100f, e[1], 3);
        // L = raiz de diag(50^2, 25^2) mais o piso de 0,25 px^2: 0,01 px cobre o piso.
        Assert.Equal(50f, e[2], 1);
        Assert.Equal(0f, e[3], 3);
        Assert.Equal(25f, e[5], 1);
        Assert.True(list.EllipseHasArcs[0]);
    }

    [Fact]
    public void Prepare_LineThroughAnEllipsoid_IsCutWhereItIsInside()
    {
        var ellipsoid = new EllipsoidItem { Center = Vector3.Zero, Axes = [new(1, 0, 0), new(0, 0.5f, 0), new(0, 0, 0.25f)] };
        var line = new LineItem { Start = new Vector3(-1.8f, 0, 0), End = new Vector3(1.8f, 0, 0) };

        DrawList list = Prepare(ellipsoid, line);

        Assert.True(list.SegmentCount > 0);
        ReadOnlySpan<float> s = list.Segments;
        for (int i = 0; i < list.SegmentCount; i++)
        {
            // Fora do elipsoide: |x - 100| >= 50 px, menos o meio pixel de sobreposicao das juntas.
            Assert.True(MathF.Abs(s[5 * i] - 100) >= 49.4f && MathF.Abs(s[5 * i + 2] - 100) >= 49.4f,
                $"segment {i} from {s[5 * i]} to {s[5 * i + 2]} enters the ellipsoid");
        }
    }

    [Fact]
    public void Prepare_ReusesTheListBetweenFrames()
    {
        var preparer = new FramePreparer();
        var list = new DrawList();
        var camera = new Camera { ViewRadius = 2 };

        preparer.Prepare(new Scene([Triangle(0, Rgba.White), Triangle(0.5f, Rgba.White)]), camera, 200, 200, list);
        preparer.Prepare(new Scene([Triangle(0, Rgba.White)]), camera, 200, 200, list);

        Assert.Equal(1, list.TriangleCount);
        Assert.Equal(1, list.CommandCount);
    }
}
