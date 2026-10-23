using System.Collections.Immutable;

namespace Viewer3D.Crystal;

/// <summary>How the atoms are colored when no rule gives a color.</summary>
public enum AtomColorScheme
{
    /// <summary>By element, with the sectors of shared and partly occupied positions.</summary>
    Element,

    /// <summary>One color per crystallographic site (<see cref="CrystalAtom.SiteIndex"/>), from a fixed palette.</summary>
    Site,

    /// <summary>From blue at occupancy 0 to red at occupancy 1.</summary>
    Occupancy,

    /// <summary>
    /// The color of the atom's group (<see cref="CrystalAtom.Group"/>, <see cref="CrystalStructure.Groups"/>); an atom
    /// without a group takes the element color.
    /// </summary>
    Group,
}

/// <summary>The text of an atom label.</summary>
public enum AtomLabelText
{
    /// <summary>The atom label (<see cref="CrystalAtom.Label"/>).</summary>
    Label,

    /// <summary>The element with the largest share of the position.</summary>
    Element,

    /// <summary>The label of the first atom of the same site.</summary>
    Site,
}

/// <summary>
/// The look of an atom at one level of <see cref="CrystalAppearance"/>. Null fields say nothing, so a lower level decides.
/// </summary>
public sealed record AtomStyle
{
    /// <summary>A style that says nothing.</summary>
    public static AtomStyle Empty { get; } = new();

    /// <summary>False hides the atom, with its label and rods; polyhedra around it or through it stay.</summary>
    public bool? Visible { get; init; }

    /// <summary>A color that replaces the color scheme, sectors included.</summary>
    public Rgba? Color { get; init; }

    /// <summary>Opacity from 0 to 1 of the sphere or of each displacement shape.</summary>
    public double? Opacity { get; init; }

    /// <summary>Factor on the sphere radius; the displacement shapes ignore it.</summary>
    public double? RadiusScale { get; init; }

    /// <summary>False leaves the label out.</summary>
    public bool? ShowLabel { get; init; }

    /// <summary>What the label says.</summary>
    public AtomLabelText? LabelText { get; init; }

    /// <summary>A ring of this color around the sphere (<see cref="SphereItem.Halo"/>), to make the atom stand out.</summary>
    public Rgba? Halo { get; init; }

    /// <summary>
    /// Longest bond drawn from this atom, in angstrom, over <see cref="CrystalSceneOptions.MaxBondLength"/>; 0 draws no
    /// bond from it. See <see cref="CrystalSceneOptions.MaxBondLength"/> for the rule.
    /// </summary>
    public double? MaxBondLength { get; init; }

    /// <summary>This style over <paramref name="lower"/>: each field is this one's when set, the lower one's when not.</summary>
    public AtomStyle Over(AtomStyle lower) => new()
    {
        Visible = Visible ?? lower.Visible,
        Color = Color ?? lower.Color,
        Opacity = Opacity ?? lower.Opacity,
        RadiusScale = RadiusScale ?? lower.RadiusScale,
        ShowLabel = ShowLabel ?? lower.ShowLabel,
        LabelText = LabelText ?? lower.LabelText,
        Halo = Halo ?? lower.Halo,
        MaxBondLength = MaxBondLength ?? lower.MaxBondLength,
    };
}

/// <summary>The look of a bond at one level of <see cref="CrystalAppearance"/>; null fields say nothing.</summary>
public sealed record BondStyle
{
    /// <summary>A style that says nothing.</summary>
    public static BondStyle Empty { get; } = new();

    /// <summary>False hides the rod.</summary>
    public bool? Visible { get; init; }

    /// <summary>One color for the whole rod; without it each half takes the color of its atom.</summary>
    public Rgba? Color { get; init; }

    /// <summary>Opacity from 0 to 1.</summary>
    public double? Opacity { get; init; }

    /// <summary>Rod radius in angstrom.</summary>
    public double? Radius { get; init; }

    /// <summary>This style over <paramref name="lower"/>, field by field.</summary>
    public BondStyle Over(BondStyle lower) => new()
    {
        Visible = Visible ?? lower.Visible,
        Color = Color ?? lower.Color,
        Opacity = Opacity ?? lower.Opacity,
        Radius = Radius ?? lower.Radius,
    };
}

/// <summary>The look of a polyhedron at one level of <see cref="CrystalAppearance"/>; null fields say nothing.</summary>
public sealed record PolyhedronStyle
{
    /// <summary>A style that says nothing.</summary>
    public static PolyhedronStyle Empty { get; } = new();

    /// <summary>False hides every copy of the polyhedron.</summary>
    public bool? Visible { get; init; }

    /// <summary>Color of the faces.</summary>
    public Rgba? FaceColor { get; init; }

    /// <summary>Opacity of the faces from 0 to 1.</summary>
    public double? FaceOpacity { get; init; }

    /// <summary>False draws the edges alone.</summary>
    public bool? ShowFaces { get; init; }

    /// <summary>Color of the edges.</summary>
    public Rgba? EdgeColor { get; init; }

    /// <summary>Width of the edges in pixels.</summary>
    public float? EdgeWidth { get; init; }

    /// <summary>False draws the faces alone.</summary>
    public bool? ShowEdges { get; init; }

    /// <summary>This style over <paramref name="lower"/>, field by field.</summary>
    public PolyhedronStyle Over(PolyhedronStyle lower) => new()
    {
        Visible = Visible ?? lower.Visible,
        FaceColor = FaceColor ?? lower.FaceColor,
        FaceOpacity = FaceOpacity ?? lower.FaceOpacity,
        ShowFaces = ShowFaces ?? lower.ShowFaces,
        EdgeColor = EdgeColor ?? lower.EdgeColor,
        EdgeWidth = EdgeWidth ?? lower.EdgeWidth,
        ShowEdges = ShowEdges ?? lower.ShowEdges,
    };
}

/// <summary>One image of an atom: the atom and its cell offset, the same as <see cref="CrystalItemInfo.Offset"/>.</summary>
public readonly record struct AtomImage(int Atom, LatticeShift Offset);

/// <summary>An unordered pair of element symbols, normalized.</summary>
public readonly record struct ElementPair(string First, string Second)
{
    /// <summary>The pair of <paramref name="a"/> and <paramref name="b"/>, normalized and in order.</summary>
    public static ElementPair Of(string a, string b)
    {
        string x = ElementTable.Normalize(a), y = ElementTable.Normalize(b);
        return string.CompareOrdinal(x, y) <= 0 ? new ElementPair(x, y) : new ElementPair(y, x);
    }
}

/// <summary>
/// How a structure is drawn, apart from the style: what is shown or hidden, colors, opacities and sizes, in rules from
/// general to particular. Appearance is never data: hiding an atom leaves the structure as it is.
/// </summary>
/// <remarks>
/// <para>
/// An atom takes, field by field, the most particular rule that sets the field: its image (<see cref="ByImage"/>) over
/// the atom (<see cref="ByAtom"/>) over its group (<see cref="ByGroup"/>) over its site (<see cref="BySite"/>) over its
/// element (<see cref="ByElement"/>, the element with the largest share) over <see cref="AtomDefault"/>.
/// </para>
/// <para>
/// A bond takes <see cref="ByBond"/> over <see cref="ByElementPair"/> over <see cref="BondDefault"/>. A polyhedron takes
/// <see cref="ByPolyhedron"/> (its index) over <see cref="PolyhedronBySite"/> and <see cref="PolyhedronByElement"/> (of
/// its center) over <see cref="PolyhedronDefault"/>.
/// </para>
/// </remarks>
public sealed record CrystalAppearance
{
    /// <summary>The appearance that changes nothing: the scene is the one of the style alone.</summary>
    public static CrystalAppearance Default { get; } = new();

    /// <summary>False hides every atom; polyhedra stay.</summary>
    public bool ShowAtoms { get; init; } = true;

    /// <summary>False hides every rod.</summary>
    public bool ShowBonds { get; init; } = true;

    /// <summary>False hides the cell edges.</summary>
    public bool ShowCell { get; init; } = true;

    /// <summary>False hides the atoms at the center of a drawn polyhedron.</summary>
    public bool ShowPolyhedronCenters { get; init; } = true;

    /// <summary>False hides <see cref="CrystalStructure.Annotations"/>.</summary>
    public bool ShowAnnotations { get; init; } = true;

    /// <summary>The colors of atoms without a color rule.</summary>
    public AtomColorScheme ColorScheme { get; init; } = AtomColorScheme.Element;

    /// <summary>Label size in pixels; null keeps the size of the viewer.</summary>
    public double? LabelSize { get; init; }

    /// <summary>The rule of every atom.</summary>
    public AtomStyle AtomDefault { get; init; } = AtomStyle.Empty;

    /// <summary>Rules by element symbol, normalized (<see cref="ElementTable.Normalize"/>).</summary>
    public ImmutableDictionary<string, AtomStyle> ByElement { get; init; } = ImmutableDictionary<string, AtomStyle>.Empty;

    /// <summary>Rules by <see cref="CrystalAtom.SiteIndex"/>.</summary>
    public ImmutableDictionary<int, AtomStyle> BySite { get; init; } = ImmutableDictionary<int, AtomStyle>.Empty;

    /// <summary>Rules by <see cref="CrystalAtom.Group"/>.</summary>
    public ImmutableDictionary<int, AtomStyle> ByGroup { get; init; } = ImmutableDictionary<int, AtomStyle>.Empty;

    /// <summary>Rules by atom index, for every image of the atom.</summary>
    public ImmutableDictionary<int, AtomStyle> ByAtom { get; init; } = ImmutableDictionary<int, AtomStyle>.Empty;

    /// <summary>Rules for one image of an atom.</summary>
    public ImmutableDictionary<AtomImage, AtomStyle> ByImage { get; init; } = ImmutableDictionary<AtomImage, AtomStyle>.Empty;

    /// <summary>The rule of every bond.</summary>
    public BondStyle BondDefault { get; init; } = BondStyle.Empty;

    /// <summary>Rules by the pair of main elements of the two atoms.</summary>
    public ImmutableDictionary<ElementPair, BondStyle> ByElementPair { get; init; } = ImmutableDictionary<ElementPair, BondStyle>.Empty;

    /// <summary>Rules by bond index in <see cref="CrystalStructure.Bonds"/>.</summary>
    public ImmutableDictionary<int, BondStyle> ByBond { get; init; } = ImmutableDictionary<int, BondStyle>.Empty;

    /// <summary>The rule of every polyhedron.</summary>
    public PolyhedronStyle PolyhedronDefault { get; init; } = PolyhedronStyle.Empty;

    /// <summary>Rules by the main element of the center, normalized.</summary>
    public ImmutableDictionary<string, PolyhedronStyle> PolyhedronByElement { get; init; } = ImmutableDictionary<string, PolyhedronStyle>.Empty;

    /// <summary>Rules by the site of the center.</summary>
    public ImmutableDictionary<int, PolyhedronStyle> PolyhedronBySite { get; init; } = ImmutableDictionary<int, PolyhedronStyle>.Empty;

    /// <summary>Rules by polyhedron index in <see cref="CrystalStructure.Polyhedra"/>.</summary>
    public ImmutableDictionary<int, PolyhedronStyle> ByPolyhedron { get; init; } = ImmutableDictionary<int, PolyhedronStyle>.Empty;

    /// <summary>
    /// The key of <paramref name="atom"/> in <see cref="ByElement"/> and of a polyhedron around it in
    /// <see cref="PolyhedronByElement"/>: the element with the largest share of the position, normalized.
    /// </summary>
    public static string ElementKey(CrystalAtom atom)
    {
        ArgumentNullException.ThrowIfNull(atom);
        return ElementTable.Normalize(CrystalSceneBuilder.MainElement(atom));
    }

    /// <summary>
    /// The style of atom <paramref name="atom"/>, of its image at <paramref name="offset"/> when given, from the rules
    /// in order.
    /// </summary>
    public AtomStyle ResolveAtom(CrystalStructure structure, int atom, LatticeShift? offset = null)
    {
        ArgumentNullException.ThrowIfNull(structure);
        CrystalAtom a = structure.Atoms[atom];
        AtomStyle style = AtomDefault;
        if (ByElement.TryGetValue(ElementTable.Normalize(CrystalSceneBuilder.MainElement(a)), out AtomStyle? e))
            style = e.Over(style);
        if (a.SiteIndex >= 0 && BySite.TryGetValue(a.SiteIndex, out AtomStyle? s))
            style = s.Over(style);
        if (a.Group >= 0 && ByGroup.TryGetValue(a.Group, out AtomStyle? g))
            style = g.Over(style);
        if (ByAtom.TryGetValue(atom, out AtomStyle? x))
            style = x.Over(style);
        if (offset is { } o && ByImage.TryGetValue(new AtomImage(atom, o), out AtomStyle? i))
            style = i.Over(style);
        return style;
    }

    /// <summary>The style of bond <paramref name="bond"/>.</summary>
    public BondStyle ResolveBond(CrystalStructure structure, int bond)
    {
        ArgumentNullException.ThrowIfNull(structure);
        BondStyle style = BondDefault;
        CrystalBond b = structure.Bonds[bond];
        int count = structure.Atoms.Count;
        if ((uint)b.First < (uint)count && (uint)b.Second < (uint)count && ByElementPair.Count > 0
            && ByElementPair.TryGetValue(ElementPair.Of(CrystalSceneBuilder.MainElement(structure.Atoms[b.First]),
                CrystalSceneBuilder.MainElement(structure.Atoms[b.Second])), out BondStyle? pair))
        {
            style = pair.Over(style);
        }
        if (ByBond.TryGetValue(bond, out BondStyle? own))
            style = own.Over(style);
        return style;
    }

    /// <summary>The style of polyhedron <paramref name="polyhedron"/>.</summary>
    public PolyhedronStyle ResolvePolyhedron(CrystalStructure structure, int polyhedron)
    {
        ArgumentNullException.ThrowIfNull(structure);
        PolyhedronStyle style = PolyhedronDefault;
        int center = structure.Polyhedra[polyhedron].Center;
        if ((uint)center < (uint)structure.Atoms.Count)
        {
            CrystalAtom a = structure.Atoms[center];
            if (PolyhedronByElement.TryGetValue(ElementTable.Normalize(CrystalSceneBuilder.MainElement(a)), out PolyhedronStyle? e))
                style = e.Over(style);
            if (a.SiteIndex >= 0 && PolyhedronBySite.TryGetValue(a.SiteIndex, out PolyhedronStyle? s))
                style = s.Over(style);
        }
        if (ByPolyhedron.TryGetValue(polyhedron, out PolyhedronStyle? own))
            style = own.Over(style);
        return style;
    }

    /// <summary>
    /// The same appearance with every <c>Visible</c> rule taken out and the atoms, rods, cell, polyhedron centers and
    /// annotations shown; colors, opacities, halos and sizes stay.
    /// </summary>
    public CrystalAppearance ShowAll() => this with
    {
        ShowAtoms = true,
        ShowBonds = true,
        ShowCell = true,
        ShowPolyhedronCenters = true,
        ShowAnnotations = true,
        AtomDefault = AtomDefault with { Visible = null },
        ByElement = Clear(ByElement, s => s with { Visible = null }),
        BySite = Clear(BySite, s => s with { Visible = null }),
        ByGroup = Clear(ByGroup, s => s with { Visible = null }),
        ByAtom = Clear(ByAtom, s => s with { Visible = null }),
        ByImage = Clear(ByImage, s => s with { Visible = null }),
        BondDefault = BondDefault with { Visible = null },
        ByElementPair = Clear(ByElementPair, s => s with { Visible = null }),
        ByBond = Clear(ByBond, s => s with { Visible = null }),
        PolyhedronDefault = PolyhedronDefault with { Visible = null },
        PolyhedronByElement = Clear(PolyhedronByElement, s => s with { Visible = null }),
        PolyhedronBySite = Clear(PolyhedronBySite, s => s with { Visible = null }),
        ByPolyhedron = Clear(ByPolyhedron, s => s with { Visible = null }),
    };

    static ImmutableDictionary<TKey, TStyle> Clear<TKey, TStyle>(ImmutableDictionary<TKey, TStyle> rules, Func<TStyle, TStyle> clear)
        where TKey : notnull
        => rules.Count == 0 ? rules : rules.ToImmutableDictionary(p => p.Key, p => clear(p.Value), rules.KeyComparer, rules.ValueComparer);
}
