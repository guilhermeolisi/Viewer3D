using System.Collections.Immutable;
using System.Numerics;

namespace Viewer3D.Crystal.Tests;

public class CrystalGroupAnnotationTests
{
    static readonly CrystalSceneBuilder Builder = new();
    static readonly Rgba Red = new(230, 30, 30);
    static readonly Rgba Green = new(30, 200, 60);
    static readonly Rgba Blue = new(30, 60, 230);
    static readonly Rgba Gold = new(240, 190, 20);

    /// <summary>
    /// Three atoms of one site in a cubic cell of 4 A, far apart: the first in group 1 ("Defect"), the second in group 0
    /// ("Host"), the third without group.
    /// </summary>
    static CrystalStructure Grouped(params SceneItem[] annotations) => new(
        new CrystalCell(4, 4, 4, 90, 90, 90),
        [
            new CrystalAtom("Fe1", "Fe", 0.1, 0.1, 0.1) { SiteIndex = 0, Group = 1 },
            new CrystalAtom("Fe2", "Fe", 0.5, 0.5, 0.5) { SiteIndex = 0, Group = 0 },
            new CrystalAtom("C1", "C", 0.8, 0.8, 0.8) { SiteIndex = 1 },
        ],
        [])
    {
        Groups = [new CrystalAtomGroup("Host", Green), new CrystalAtomGroup("Defect", Red)],
        Annotations = annotations,
    };

    static CrystalSceneOptions OneCell() => new() { ShowBoundaryAtoms = false };

    static SphereItem SphereOf(CrystalScene scene, int atom)
        => scene.Scene.Items.OfType<SphereItem>().Single(s => scene.Lookup(s.Id) is { Kind: CrystalItemKind.Atom } i && i.AtomIndex == atom);

    [Fact]
    public void ResolveAtom_GroupRule_IsOverTheSiteAndUnderTheAtom()
    {
        CrystalStructure s = Grouped();
        var app = CrystalAppearance.Default with
        {
            BySite = ImmutableDictionary<int, AtomStyle>.Empty.Add(0, new AtomStyle { Color = Blue, Opacity = 0.5 }),
            ByGroup = ImmutableDictionary<int, AtomStyle>.Empty.Add(1, new AtomStyle { Color = Red }),
            ByAtom = ImmutableDictionary<int, AtomStyle>.Empty.Add(0, new AtomStyle { Opacity = 0.9 }),
        };

        AtomStyle first = app.ResolveAtom(s, 0);
        AtomStyle second = app.ResolveAtom(s, 1);

        Assert.Equal(Red, first.Color);
        Assert.Equal(0.9, first.Opacity);
        // Group 0 has no rule: the site decides.
        Assert.Equal(Blue, second.Color);
        Assert.Equal(0.5, second.Opacity);
    }

    [Fact]
    public void GroupScheme_PaintsByGroupColor_AndListsTheGroupsInOrderThenTheLooseElements()
    {
        CrystalScene scene = Builder.Build(Grouped(), OneCell(), CrystalAppearance.Default with { ColorScheme = AtomColorScheme.Group });

        Assert.Equal(Red, SphereOf(scene, 0).Color);
        Assert.Equal(Green, SphereOf(scene, 1).Color);
        Assert.True(ElementTable.TryGet("C", out ElementInfo carbon));
        Assert.Equal(carbon.Color, SphereOf(scene, 2).Color);
        Assert.Equal(["Host", "Defect", "C"], scene.ColorLegend.Select(e => e.Text));
        Assert.Equal([Green, Red, carbon.Color], scene.ColorLegend.Select(e => e.Color));
    }

    [Fact]
    public void GroupRule_HaloAndOpacity_ReachTheSphere()
    {
        var app = CrystalAppearance.Default with
        {
            ByGroup = ImmutableDictionary<int, AtomStyle>.Empty
                .Add(1, new AtomStyle { Halo = Gold })
                .Add(0, new AtomStyle { Opacity = 0.25 }),
        };

        CrystalScene scene = Builder.Build(Grouped(), OneCell(), app);

        Assert.Equal(Gold, SphereOf(scene, 0).Halo);
        Assert.Null(SphereOf(scene, 1).Halo);
        // 0.25 of an opaque color: round(255 x 0.25) = 64.
        Assert.Equal(64, SphereOf(scene, 1).Color.A);
        Assert.Equal(255, SphereOf(scene, 2).Color.A);
    }

    [Fact]
    public void ShowAll_KeepsTheGroupColorsAndHalos_AndTakesOutOnlyTheirVisibility()
    {
        var app = CrystalAppearance.Default with
        {
            ByGroup = ImmutableDictionary<int, AtomStyle>.Empty.Add(0, new AtomStyle { Visible = false, Halo = Gold, Color = Blue }),
            ShowAnnotations = false,
        };

        CrystalAppearance all = app.ShowAll();

        Assert.Null(all.ByGroup[0].Visible);
        Assert.Equal(Gold, all.ByGroup[0].Halo);
        Assert.Equal(Blue, all.ByGroup[0].Color);
        Assert.True(all.ShowAnnotations);
    }

    [Fact]
    public void Annotations_AreDrawnOverTheStructure_AndPickedAsAnnotations()
    {
        MeshItem arrow = MeshShapes.Arrow(Vector3.Zero, new Vector3(4, 0, 0), 0.05f, 0.15f, 0.4f, Gold, id: -2);
        var line = new LineItem { Id = -5, Start = Vector3.Zero, End = new Vector3(0, 0, 4), Color = Red, Width = 3 };
        var again = new LineItem { Id = -5, Start = Vector3.Zero, End = new Vector3(0, 4, 0), Color = Red, Width = 3 };

        CrystalScene scene = Builder.Build(Grouped(arrow, line, again), OneCell());

        Assert.Contains(arrow, scene.Scene.Items);
        Assert.Contains(line, scene.Scene.Items);
        Assert.Contains(again, scene.Scene.Items);
        Assert.Equal(new CrystalItemInfo(CrystalItemKind.Annotation, -1, -1, LatticeShift.Zero, false) { AnnotationIndex = 0 },
            scene.Lookup(-2));
        // Two items with one id: the first annotation.
        Assert.Equal(1, scene.Lookup(-5)!.AnnotationIndex);
        Assert.Equal(-1, scene.Lookup(SphereOf(scene, 0).Id)!.AnnotationIndex);
    }

    [Fact]
    public void ShowAnnotationsOff_LeavesThemOut()
    {
        MeshItem arrow = MeshShapes.Arrow(Vector3.Zero, new Vector3(4, 0, 0), 0.05f, 0.15f, 0.4f, Gold, id: -2);

        CrystalScene scene = Builder.Build(Grouped(arrow), OneCell(), CrystalAppearance.Default with { ShowAnnotations = false });

        Assert.DoesNotContain(arrow, scene.Scene.Items);
        Assert.Null(scene.Lookup(-2));
    }
}
