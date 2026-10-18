using System.Collections.Immutable;

namespace Viewer3D.Crystal.Tests;

public class CrystalAppearanceTests
{
    static readonly Rgba Red = new(255, 0, 0), Green = new(0, 255, 0), Blue = new(0, 0, 255);

    static CrystalStructure Sited()
    {
        CrystalStructure s = Fixtures.FluoriteWithCubes();
        // Ca no sitio 0, F no sitio 1.
        var atoms = s.Atoms.Select(a => a with { SiteIndex = a.Element == "Ca" ? 0 : 1 }).ToArray();
        return s with { Atoms = atoms };
    }

    [Fact]
    public void ResolveAtom_TakesEachFieldFromTheMostParticularLevelThatSetsIt()
    {
        CrystalStructure s = Sited();
        int ca = s.Atoms.ToList().FindIndex(a => a.Element == "Ca");
        var image = new LatticeShift(1, 0, 0);
        var app = CrystalAppearance.Default with
        {
            AtomDefault = new AtomStyle { Color = Red, Opacity = 0.2, RadiusScale = 2 },
            ByElement = ImmutableDictionary<string, AtomStyle>.Empty.Add("Ca", new AtomStyle { Color = Green, Visible = false }),
            BySite = ImmutableDictionary<int, AtomStyle>.Empty.Add(0, new AtomStyle { Opacity = 0.5 }),
            ByAtom = ImmutableDictionary<int, AtomStyle>.Empty.Add(ca, new AtomStyle { Visible = true }),
            ByImage = ImmutableDictionary<AtomImage, AtomStyle>.Empty.Add(new AtomImage(ca, image), new AtomStyle { Color = Blue }),
        };

        AtomStyle plain = app.ResolveAtom(s, ca);
        AtomStyle imaged = app.ResolveAtom(s, ca, image);

        Assert.Equal(Green, plain.Color);
        Assert.Equal(0.5, plain.Opacity);
        Assert.Equal(2, plain.RadiusScale);
        Assert.True(plain.Visible);
        Assert.Equal(Blue, imaged.Color);
        Assert.Equal(0.5, imaged.Opacity);
    }

    [Fact]
    public void ResolveAtom_OtherImage_IgnoresTheImageRule()
    {
        CrystalStructure s = Sited();
        var app = CrystalAppearance.Default with
        {
            ByImage = ImmutableDictionary<AtomImage, AtomStyle>.Empty.Add(new AtomImage(0, new LatticeShift(1, 0, 0)), new AtomStyle { Visible = false }),
        };

        Assert.Null(app.ResolveAtom(s, 0, LatticeShift.Zero).Visible);
        Assert.False(app.ResolveAtom(s, 0, new LatticeShift(1, 0, 0)).Visible);
    }

    [Fact]
    public void ShowAll_TakesOutTheVisibleRules_AndKeepsTheColors()
    {
        CrystalStructure s = Sited();
        var app = CrystalAppearance.Default with
        {
            ShowAtoms = false,
            ShowCell = false,
            ByElement = ImmutableDictionary<string, AtomStyle>.Empty.Add("F", new AtomStyle { Visible = false, Color = Red }),
            ByPolyhedron = ImmutableDictionary<int, PolyhedronStyle>.Empty.Add(0, new PolyhedronStyle { Visible = false, FaceOpacity = 0.8 }),
            ByBond = ImmutableDictionary<int, BondStyle>.Empty.Add(0, new BondStyle { Visible = false }),
        };

        CrystalAppearance shown = app.ShowAll();

        int f = s.Atoms.ToList().FindIndex(a => a.Element == "F");
        Assert.True(shown.ShowAtoms);
        Assert.True(shown.ShowCell);
        Assert.Null(shown.ResolveAtom(s, f).Visible);
        Assert.Equal(Red, shown.ResolveAtom(s, f).Color);
        Assert.Null(shown.ResolvePolyhedron(s, 0).Visible);
        Assert.Equal(0.8, shown.ResolvePolyhedron(s, 0).FaceOpacity);
        Assert.Null(shown.ResolveBond(s, 0).Visible);
    }

    [Fact]
    public void ResolveBond_TakesTheBondOverThePairOverTheDefault()
    {
        CrystalStructure s = Sited();
        var app = CrystalAppearance.Default with
        {
            BondDefault = new BondStyle { Radius = 0.3, Opacity = 0.9 },
            ByElementPair = ImmutableDictionary<ElementPair, BondStyle>.Empty.Add(ElementPair.Of("f", "CA"), new BondStyle { Color = Red, Radius = 0.2 }),
            ByBond = ImmutableDictionary<int, BondStyle>.Empty.Add(1, new BondStyle { Radius = 0.05 }),
        };

        BondStyle first = app.ResolveBond(s, 0);
        BondStyle second = app.ResolveBond(s, 1);

        Assert.Equal(new ElementPair("Ca", "F"), ElementPair.Of("F", "ca"));
        Assert.Equal(0.2, first.Radius);
        Assert.Equal(Red, first.Color);
        Assert.Equal(0.9, first.Opacity);
        Assert.Equal(0.05, second.Radius);
        Assert.Equal(Red, second.Color);
    }

    [Fact]
    public void ResolvePolyhedron_TakesTheIndexOverTheSiteOverTheElementOverTheDefault()
    {
        CrystalStructure s = Sited();
        var app = CrystalAppearance.Default with
        {
            PolyhedronDefault = new PolyhedronStyle { FaceOpacity = 0.1, ShowEdges = false },
            PolyhedronByElement = ImmutableDictionary<string, PolyhedronStyle>.Empty.Add("Ca", new PolyhedronStyle { FaceColor = Red, FaceOpacity = 0.2 }),
            PolyhedronBySite = ImmutableDictionary<int, PolyhedronStyle>.Empty.Add(0, new PolyhedronStyle { FaceColor = Green }),
            ByPolyhedron = ImmutableDictionary<int, PolyhedronStyle>.Empty.Add(1, new PolyhedronStyle { FaceColor = Blue }),
        };

        PolyhedronStyle first = app.ResolvePolyhedron(s, 0);
        PolyhedronStyle second = app.ResolvePolyhedron(s, 1);

        Assert.Equal(Green, first.FaceColor);
        Assert.Equal(0.2, first.FaceOpacity);
        Assert.False(first.ShowEdges);
        Assert.Equal(Blue, second.FaceColor);
    }

    [Fact]
    public void ElementKey_IsTheNormalizedElementWithTheLargestShare()
    {
        var plain = new CrystalAtom("Fe1", "fe", 0, 0, 0);
        var shared = new CrystalAtom("M1", "Mg", 0, 0, 0) { Shares = [new ElementShare("Mg", 0.3), new ElementShare("FE", 0.7)] };

        Assert.Equal("Fe", CrystalAppearance.ElementKey(plain));
        Assert.Equal("Fe", CrystalAppearance.ElementKey(shared));
    }
}
