using System.Collections.Immutable;

namespace Viewer3D.Crystal.Tests;

public class CrystalSceneBuilderAppearanceTests
{
    static readonly CrystalSceneBuilder Builder = new();
    static readonly Rgba Red = new(255, 0, 0);

    static CrystalSceneOptions Polyhedra() => new() { Style = CrystalStyle.Polyhedra };

    static CrystalAppearance HideElement(string element) => CrystalAppearance.Default with
    {
        ByElement = ImmutableDictionary<string, AtomStyle>.Empty.Add(element, new AtomStyle { Visible = false }),
    };

    static IEnumerable<(SceneItem Item, CrystalItemInfo Info)> Atoms(CrystalScene scene)
        => scene.Scene.Items.Where(i => i is SphereItem or EllipsoidItem)
            .Select(i => (i, scene.Lookup(i.Id)!));

    static string ElementOf(CrystalStructure s, CrystalItemInfo info) => s.Atoms[info.AtomIndex].Element;

    [Theory]
    [MemberData(nameof(CrystalSceneGoldenTests.Cases), MemberType = typeof(CrystalSceneGoldenTests))]
    public void Build_DefaultAndNeutralAppearance_GiveTheRecordedScene(string name)
    {
        (CrystalStructure structure, CrystalSceneOptions options) = CrystalSceneGoldenTests.Case(name);
        var neutral = CrystalAppearance.Default with
        {
            ByElement = ImmutableDictionary<string, AtomStyle>.Empty.Add("F", AtomStyle.Empty),
            BySite = ImmutableDictionary<int, AtomStyle>.Empty.Add(0, new AtomStyle { Visible = true }),
            ByPolyhedron = ImmutableDictionary<int, PolyhedronStyle>.Empty.Add(0, PolyhedronStyle.Empty),
        };

        Assert.Equal(CrystalSceneGoldenTests.Recorded[name], SceneDump.Hash(Builder.Build(structure, options, CrystalAppearance.Default)));
        Assert.Equal(CrystalSceneGoldenTests.Recorded[name], SceneDump.Hash(Builder.Build(structure, options, neutral)));
    }

    [Fact]
    public void HideElement_TakesEveryImageOut_CompletionsAndRodsIncluded_AndKeepsThePolyhedra()
    {
        CrystalStructure s = Fixtures.FluoriteWithCubes();
        CrystalScene before = Builder.Build(s, Polyhedra());

        CrystalScene scene = Builder.Build(s, Polyhedra(), HideElement("F"));

        Assert.Contains(Atoms(before), a => ElementOf(s, a.Info) == "F" && a.Info.IsCompletion);
        Assert.DoesNotContain(Atoms(scene), a => ElementOf(s, a.Info) == "F");
        Assert.Empty(scene.Scene.Items.OfType<RodItem>());
        Assert.Equal(before.Scene.Items.OfType<MeshItem>().Count(), scene.Scene.Items.OfType<MeshItem>().Count());
        Assert.Equal(
            before.Scene.Items.OfType<MeshItem>().Select(m => m.Positions.Length),
            scene.Scene.Items.OfType<MeshItem>().Select(m => m.Positions.Length));
        Assert.DoesNotContain(scene.Scene.Items.OfType<LabelItem>(), l => l.Text == "F1");
        Assert.True(scene.HiddenAtomCount > 0);
        Assert.Equal(["Ca"], scene.Legend.Select(e => e.Element));
    }

    [Fact]
    public void HiddenSite_WithOneAtomShown_KeepsOnlyThatAtomOfTheSite()
    {
        CrystalStructure s = Fixtures.Fluorite();
        int keep = s.Atoms.ToList().FindIndex(a => a.Element == "F");
        var app = CrystalAppearance.Default with
        {
            BySite = ImmutableDictionary<int, AtomStyle>.Empty.Add(1, new AtomStyle { Visible = false }),
            ByAtom = ImmutableDictionary<int, AtomStyle>.Empty.Add(keep, new AtomStyle { Visible = true }),
        };

        CrystalScene scene = Builder.Build(s, new CrystalSceneOptions { ShowBoundaryAtoms = false, CompleteCoordination = false }, app);

        var f = Atoms(scene).Where(a => ElementOf(s, a.Info) == "F").Select(a => a.Info.AtomIndex).ToList();
        Assert.Equal([keep], f);
        Assert.Equal(7, scene.HiddenAtomCount);
    }

    [Fact]
    public void HiddenImage_LeavesTheOtherImagesOfTheAtom()
    {
        CrystalStructure s = Fixtures.Fluorite();
        var options = new CrystalSceneOptions { RepeatA = 2, ShowBoundaryAtoms = false, CompleteCoordination = false };
        var app = CrystalAppearance.Default with
        {
            ByImage = ImmutableDictionary<AtomImage, AtomStyle>.Empty.Add(new AtomImage(4, new LatticeShift(1, 0, 0)), new AtomStyle { Visible = false }),
        };

        CrystalScene scene = Builder.Build(s, options, app);

        var images = Atoms(scene).Where(a => a.Info.AtomIndex == 4).Select(a => a.Info.Offset).ToList();
        Assert.Equal([LatticeShift.Zero], images);
        Assert.Equal(1, scene.HiddenAtomCount);
    }

    [Fact]
    public void NoAtoms_InPolyhedra_DrawsOnlyThePolyhedra()
    {
        CrystalStructure s = Fixtures.FluoriteWithCubes();
        int meshes = Builder.Build(s, Polyhedra()).Scene.Items.OfType<MeshItem>().Count();

        CrystalScene scene = Builder.Build(s, Polyhedra(), CrystalAppearance.Default with { ShowAtoms = false });

        Assert.Empty(scene.Scene.Items.OfType<SphereItem>());
        Assert.Empty(scene.Scene.Items.OfType<RodItem>());
        Assert.Empty(scene.Scene.Items.OfType<LabelItem>());
        Assert.Equal(meshes, scene.Scene.Items.OfType<MeshItem>().Count());
        Assert.True(meshes > 0);
        Assert.Equal(["Ca"], scene.Legend.Select(e => e.Element));
    }

    [Fact]
    public void NoPolyhedronCenters_HidesTheCentersAndKeepsTheVertices()
    {
        CrystalStructure s = Fixtures.FluoriteWithCubes();

        CrystalScene scene = Builder.Build(s, Polyhedra(), CrystalAppearance.Default with { ShowPolyhedronCenters = false });

        Assert.DoesNotContain(Atoms(scene), a => ElementOf(s, a.Info) == "Ca");
        Assert.Contains(Atoms(scene), a => ElementOf(s, a.Info) == "F");
    }

    [Fact]
    public void Opacity_SetsTheAlpha_OfDrawnAndFadedSpheres()
    {
        CrystalStructure s = Fixtures.FluoriteWithCubes();
        var app = CrystalAppearance.Default with { AtomDefault = new AtomStyle { Opacity = 0.5 } };

        CrystalScene scene = Builder.Build(s, Polyhedra(), app);

        var spheres = Atoms(scene).ToList();
        Assert.All(spheres.Where(a => !a.Info.IsCompletion), a => Assert.Equal(128, ((SphereItem)a.Item).Color.A));
        Assert.All(spheres.Where(a => a.Info.IsCompletion), a => Assert.Equal(55, ((SphereItem)a.Item).Color.A));
        Assert.Contains(spheres, a => a.Info.IsCompletion);
    }

    [Fact]
    public void RuleColor_AndRadiusScale_ChangeTheSphere()
    {
        CrystalStructure s = Fixtures.Fluorite();
        var options = new CrystalSceneOptions { ShowBoundaryAtoms = false, CompleteCoordination = false };
        float before = Atoms(Builder.Build(s, options)).Select(a => ((SphereItem)a.Item).Radius).First();
        var app = CrystalAppearance.Default with
        {
            ByAtom = ImmutableDictionary<int, AtomStyle>.Empty.Add(0, new AtomStyle { RadiusScale = 2, Color = Red }),
        };

        CrystalScene scene = Builder.Build(s, options, app);

        var sphere = (SphereItem)Atoms(scene).First(a => a.Info.AtomIndex == 0).Item;
        Assert.Equal(2 * before, sphere.Radius, 1e-5f);
        Assert.Equal(Red, sphere.Color);
    }

    [Fact]
    public void ElementRuleColor_GoesToTheLegendAndTheRods()
    {
        CrystalStructure s = Fixtures.Fluorite();
        var app = CrystalAppearance.Default with
        {
            ByElement = ImmutableDictionary<string, AtomStyle>.Empty.Add("F", new AtomStyle { Color = Red }),
        };

        CrystalScene scene = Builder.Build(s, new CrystalSceneOptions(), app);

        Assert.Equal(Red, scene.Legend.Single(e => e.Element == "F").Color);
        Assert.Contains(scene.Scene.Items.OfType<RodItem>(), r => r.EndColor == Red || r.StartColor == Red);
    }

    [Fact]
    public void FacesOnlyAndEdgesOnly_ShapeTheMesh()
    {
        CrystalStructure s = Fixtures.FluoriteWithCubes();
        var app = CrystalAppearance.Default with
        {
            ByPolyhedron = ImmutableDictionary<int, PolyhedronStyle>.Empty
                .Add(0, new PolyhedronStyle { ShowFaces = false, EdgeColor = Red, EdgeWidth = 3 })
                .Add(1, new PolyhedronStyle { ShowEdges = false, FaceOpacity = 1 })
                .Add(2, new PolyhedronStyle { Visible = false }),
        };

        CrystalScene scene = Builder.Build(s, Polyhedra(), app);

        var meshes = scene.Scene.Items.OfType<MeshItem>().Select(m => (m, scene.Lookup(m.Id)!.PolyhedronIndex)).ToList();
        Assert.All(meshes.Where(x => x.PolyhedronIndex == 0), x =>
        {
            Assert.Empty(x.m.Indices);
            Assert.NotEmpty(x.m.Edges!);
            Assert.Equal(Red, x.m.EdgeColor);
            Assert.Equal(3f, x.m.EdgeWidth);
        });
        Assert.All(meshes.Where(x => x.PolyhedronIndex == 1), x =>
        {
            Assert.Null(x.m.Edges);
            Assert.Equal(255, x.m.Color.A);
        });
        Assert.DoesNotContain(meshes, x => x.PolyhedronIndex == 2);
        Assert.Contains(meshes, x => x.PolyhedronIndex == 0);
    }

    [Fact]
    public void NoCell_AndNoBonds_LeaveTheEdgesAndRodsOut()
    {
        CrystalStructure s = Fixtures.Fluorite();

        CrystalScene scene = Builder.Build(s, new CrystalSceneOptions(), CrystalAppearance.Default with { ShowCell = false, ShowBonds = false });

        Assert.Empty(scene.Scene.Items.OfType<LineItem>());
        Assert.Empty(scene.Scene.Items.OfType<RodItem>());
        Assert.NotEmpty(scene.Scene.Items.OfType<SphereItem>());
    }

    [Fact]
    public void BondRule_GivesOneColorAndTheRadius_OrHidesTheRod()
    {
        CrystalStructure s = Fixtures.Fluorite();
        var app = CrystalAppearance.Default with
        {
            ByElementPair = ImmutableDictionary<ElementPair, BondStyle>.Empty.Add(ElementPair.Of("Ca", "F"), new BondStyle { Color = Red, Radius = 0.3 }),
            ByBond = ImmutableDictionary<int, BondStyle>.Empty.Add(0, new BondStyle { Visible = false }),
        };

        CrystalScene scene = Builder.Build(s, new CrystalSceneOptions { CompleteCoordination = false }, app);

        var rods = scene.Scene.Items.OfType<RodItem>().Select(r => (r, scene.Lookup(r.Id)!)).ToList();
        Assert.NotEmpty(rods);
        Assert.DoesNotContain(rods, x => x.Item2.BondIndex == 0);
        Assert.All(rods, x =>
        {
            Assert.Equal(Red, x.r.StartColor);
            Assert.Equal(Red, x.r.EndColor);
            Assert.Equal(0.3f, x.r.Radius, 1e-6f);
        });
    }

    [Fact]
    public void SiteScheme_ColorsBySite_WithOneLegendEntryPerSite()
    {
        CrystalStructure s = Fixtures.Fluorite();

        CrystalScene scene = Builder.Build(s, new CrystalSceneOptions(), CrystalAppearance.Default with { ColorScheme = AtomColorScheme.Site });

        Assert.Equal(["Ca1", "F1"], scene.ColorLegend.Select(e => e.Text));
        Assert.NotEqual(scene.ColorLegend[0].Color, scene.ColorLegend[1].Color);
        Assert.All(Atoms(scene).Where(a => !a.Info.IsCompletion), a =>
            Assert.Equal(scene.ColorLegend[ElementOf(s, a.Info) == "Ca" ? 0 : 1].Color, ((SphereItem)a.Item).Color));
    }

    [Fact]
    public void OccupancyScheme_GoesFromBlueToRed()
    {
        CrystalStructure s = Fixtures.Fluorite();
        var atoms = s.Atoms.Select((a, i) => i == 0 ? a with { Occupancy = 0 } : a).ToArray();
        s = s with { Atoms = atoms };
        var options = new CrystalSceneOptions { ShowBoundaryAtoms = false, CompleteCoordination = false };

        CrystalScene scene = Builder.Build(s, options, CrystalAppearance.Default with { ColorScheme = AtomColorScheme.Occupancy });

        Assert.Equal(["0", "0.5", "1"], scene.ColorLegend.Select(e => e.Text));
        Assert.Equal(scene.ColorLegend[0].Color, ((SphereItem)Atoms(scene).First(a => a.Info.AtomIndex == 0).Item).Color);
        Assert.Equal(scene.ColorLegend[2].Color, ((SphereItem)Atoms(scene).First(a => a.Info.AtomIndex == 1).Item).Color);
        Assert.True(scene.ColorLegend[0].Color.B > scene.ColorLegend[0].Color.R);
        Assert.True(scene.ColorLegend[2].Color.R > scene.ColorLegend[2].Color.B);
    }

    [Fact]
    public void Labels_FollowTheTextTheSizeAndTheRule()
    {
        CrystalStructure s = Fixtures.Fluorite();
        var options = new CrystalSceneOptions { ShowBoundaryAtoms = false, CompleteCoordination = false };
        var app = CrystalAppearance.Default with
        {
            LabelSize = 20,
            AtomDefault = new AtomStyle { LabelText = AtomLabelText.Element },
            ByAtom = ImmutableDictionary<int, AtomStyle>.Empty.Add(0, new AtomStyle { ShowLabel = false }),
        };

        CrystalScene scene = Builder.Build(s, options, app);

        var labels = scene.Scene.Items.OfType<LabelItem>().ToList();
        Assert.Equal(11, labels.Count);
        Assert.All(labels, l => Assert.Equal(20f, l.Size));
        Assert.Equal(3, labels.Count(l => l.Text == "Ca"));
        Assert.Equal(8, labels.Count(l => l.Text == "F"));
    }

    [Fact]
    public void SiteLabel_IsTheLabelOfTheFirstAtomOfTheSite()
    {
        CrystalStructure s = Fixtures.Fluorite();
        var atoms = s.Atoms.Select((a, i) => a.Element == "F" ? a with { Label = "F" + i } : a).ToArray();
        s = s with { Atoms = atoms };
        var options = new CrystalSceneOptions { ShowBoundaryAtoms = false, CompleteCoordination = false };

        CrystalScene scene = Builder.Build(s, options, CrystalAppearance.Default with { AtomDefault = new AtomStyle { LabelText = AtomLabelText.Site } });

        Assert.Equal(8, scene.Scene.Items.OfType<LabelItem>().Count(l => l.Text == "F4"));
    }
}
