namespace Viewer3D.Crystal.Tests;

/// <summary>
/// The scenes the builder gave before the appearance model existed, recorded as hashes of <see cref="SceneDump"/>. A
/// build without appearance, and one with the default appearance, must give them again item by item.
/// </summary>
public class CrystalSceneGoldenTests
{
    static readonly CrystalSceneBuilder Builder = new();

    // Gravados do builder do master (Viewer3D 3422692), antes da E172.
    internal static readonly Dictionary<string, string> Recorded = new()
    {
        ["Fluorite BallAndStick"] = "48245D627C649EA3",
        ["Fluorite SpaceFilling"] = "726F0F8B9B2A1F19",
        ["Fluorite ThermalEllipsoids"] = "61F06DB933211CD7",
        ["Fluorite Polyhedra"] = "48245D627C649EA3",
        ["Fluorite PolyhedraWithEllipsoids"] = "61F06DB933211CD7",
        ["FluoriteWithCubes BallAndStick"] = "48245D627C649EA3",
        ["FluoriteWithCubes SpaceFilling"] = "726F0F8B9B2A1F19",
        ["FluoriteWithCubes ThermalEllipsoids"] = "61F06DB933211CD7",
        ["FluoriteWithCubes Polyhedra"] = "FBB44242C79346E6",
        ["FluoriteWithCubes PolyhedraWithEllipsoids"] = "1165FF7DADC2E365",
        ["FluoriteWithCubes Polyhedra 2x2x1 no vertices"] = "5C47A2D8BCC98F52",
    };

    public static TheoryData<string> Cases() => [.. Recorded.Keys];

    internal static (CrystalStructure Structure, CrystalSceneOptions Options) Case(string name)
    {
        string[] parts = name.Split(' ');
        CrystalStructure structure = parts[0] == "Fluorite" ? Fixtures.Fluorite() : Fixtures.FluoriteWithCubes();
        var options = new CrystalSceneOptions { Style = Enum.Parse<CrystalStyle>(parts[1]) };
        if (parts.Length > 2)
            options = options with { RepeatA = 2, RepeatB = 2, ShowPolyhedronVertices = false, ShowBondsInsidePolyhedra = false };
        return (structure, options);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Build_WithoutAppearance_GivesTheRecordedScene(string name)
    {
        (CrystalStructure structure, CrystalSceneOptions options) = Case(name);

        string hash = SceneDump.Hash(Builder.Build(structure, options));

        Assert.Equal(Recorded[name], hash);
    }
}
