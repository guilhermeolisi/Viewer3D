namespace Viewer3D.Crystal.Tests;

public class CrystalSceneSphereTests
{
    static readonly CrystalSceneBuilder Builder = new();

    /// <summary>NaCl, Fm-3m, a = 5,64 A: Na em 4a, Cl em 4b. Na-Cl = a/2 = 2,82 A.</summary>
    const double NaClA = 5.64;

    static CrystalStructure NaCl()
    {
        var atoms = new List<CrystalAtom>();
        foreach ((double x, double y, double z) in Fixtures.Ca4a)
            atoms.Add(new CrystalAtom("Na1", "Na", x, y, z) { SiteIndex = 0 });
        foreach ((double x, double y, double z) in Fixtures.Ca4a)
            atoms.Add(new CrystalAtom("Cl1", "Cl", (x + 0.5) % 1, y, z) { SiteIndex = 1 });
        var cell = new CrystalCell(NaClA, NaClA, NaClA, 90, 90, 90);
        return Fixtures.WithBondsBelow(cell, atoms, 3.0, (a, b) => a.Element == "Na" && b.Element == "Cl");
    }

    /// <summary>A sphere around the Cl at (1/2, 1/2, 1/2) of the cell.</summary>
    static CrystalSceneOptions OnChlorine(double radius) => new()
    {
        Clip = CrystalClip.Sphere,
        SphereCenter = new FractionalPoint(0.5, 0.5, 0.5),
        SphereRadius = radius,
    };

    static int Count(CrystalStructure s, CrystalScene scene, string element)
        => scene.Scene.Items.OfType<SphereItem>().Count(x => s.Atoms[scene.Lookup(x.Id)!.AtomIndex].Element == element);

    [Theory]
    // Camadas em volta do Cl: 6 Na a a/2 = 2,82; 12 Cl a a/sqrt(2) = 3,99; 8 Na a a sqrt(3)/2 = 4,88; 6 Cl a a = 5,64.
    [InlineData(2.9, 6, 1)]
    [InlineData(4.2, 6, 13)]
    [InlineData(5.0, 14, 13)]
    public void Sphere_NaCl_HoldsTheShellsAroundCl(double radius, int sodium, int chlorine)
    {
        CrystalStructure s = NaCl();

        CrystalScene scene = Builder.Build(s, OnChlorine(radius));

        Assert.Null(scene.Warning);
        Assert.Equal(sodium, Count(s, scene, "Na"));
        Assert.Equal(chlorine, Count(s, scene, "Cl"));
        Assert.Equal(sodium + chlorine, scene.AtomCount);
    }

    [Fact]
    public void Sphere_AtTheShellDistance_KeepsTheShell()
    {
        // O raio igual a distancia da camada (a/2) a inclui: o limite e fechado.
        CrystalStructure s = NaCl();

        CrystalScene scene = Builder.Build(s, OnChlorine(NaClA / 2));

        Assert.Equal(7, scene.AtomCount);
    }

    [Fact]
    public void Sphere_BondCrossingTheSurface_IsNotDrawn()
    {
        // R = 4,2: o Cl central, 6 Na e 12 Cl. Cada Cl da segunda camada tem 2 Na dentro e 4 fora: so as 2 entram.
        CrystalStructure s = NaCl();

        CrystalScene scene = Builder.Build(s, OnChlorine(4.2));

        var rods = scene.Scene.Items.OfType<RodItem>().ToList();
        Assert.Equal(6 + 12 * 2, rods.Count);
        System.Numerics.Vector3 center = s.Cell.ToCartesian(0.5, 0.5, 0.5);
        Assert.All(rods, r =>
        {
            Assert.InRange((r.Start - center).Length(), 0, 4.2f);
            Assert.InRange((r.End - center).Length(), 0, 4.2f);
        });
        Assert.All(scene.Scene.Items.OfType<SphereItem>(), x => Assert.False(scene.Lookup(x.Id)!.IsCompletion));
    }

    [Fact]
    public void Sphere_OffAnAtom_FindsTheNeighborsOfTheHole()
    {
        // Fluorita, centro no buraco octaedrico (1/2, 1/2, 1/2): 8 F a a sqrt(3)/4 = 2,366 A e 6 Ca a a/2 = 2,73 A.
        CrystalStructure s = Fixtures.Fluorite();
        var options = new CrystalSceneOptions
        {
            Clip = CrystalClip.Sphere,
            SphereCenter = new FractionalPoint(0.5, 0.5, 0.5),
            SphereRadius = 2.5,
        };

        CrystalScene eight = Builder.Build(s, options);
        CrystalScene fourteen = Builder.Build(s, options with { SphereRadius = 3.0 });

        Assert.Equal(8, Count(s, eight, "F"));
        Assert.Equal(0, Count(s, eight, "Ca"));
        Assert.Equal(8, Count(s, fourteen, "F"));
        Assert.Equal(6, Count(s, fourteen, "Ca"));
    }

    [Fact]
    public void Sphere_FindsTheImageAcrossTheCellFace()
    {
        // O atomo em x = 0,97 so entra pela imagem t = -1 (x = -0,03), a 0,8 A do centro em x = 0,05.
        var cell = new CrystalCell(10, 10, 10, 90, 90, 90);
        var s = new CrystalStructure(cell, [new CrystalAtom("O1", "O", 0.97, 0.5, 0.5)], []);
        var options = new CrystalSceneOptions
        {
            Clip = CrystalClip.Sphere,
            SphereCenter = new FractionalPoint(0.05, 0.5, 0.5),
            SphereRadius = 1,
        };

        CrystalScene scene = Builder.Build(s, options);

        Assert.Equal(1, scene.AtomCount);
        SphereItem sphere = scene.Scene.Items.OfType<SphereItem>().Single();
        Assert.Equal(-0.3f, sphere.Center.X, 1e-4f);
        Assert.Equal(new LatticeShift(-1, 0, 0), scene.Lookup(sphere.Id)!.Offset);
    }

    [Fact]
    public void SphereSpan_UsesTheReciprocalLength()
    {
        // a = 10 A: |a*| = 0,1/A, e R = 6 alcanca 0,6 de cela. Com |a*| = 1 (o vetor unitario) a faixa iria de -6 a 6.
        CellBasis basis = new CrystalCell(10, 10, 10, 90, 90, 90).Basis();

        (double ra, _, _) = basis.ReciprocalLengths();

        Assert.Equal(0.1, ra, 12);
        Assert.Equal((-1, 1), CrystalSceneBuilder.SphereSpan(0.5, 6, ra));
    }

    [Fact]
    public void ReciprocalLengths_OfATriclinicCell_AreTheInverseSpacings()
    {
        var cell = new CrystalCell(5, 6, 7, 70, 80, 100);
        CellBasis basis = cell.Basis();
        double volume = basis.Volume;

        (double ra, double rb, double rc) = basis.ReciprocalLengths();

        // |a*| = |b x c| / V, conferido pelos vetores da cela.
        Assert.Equal(System.Numerics.Vector3.Cross(cell.BVector, cell.CVector).Length() / volume, ra, 5);
        Assert.Equal(System.Numerics.Vector3.Cross(cell.CVector, cell.AVector).Length() / volume, rb, 5);
        Assert.Equal(System.Numerics.Vector3.Cross(cell.AVector, cell.BVector).Length() / volume, rc, 5);
    }

    [Fact]
    public void Sphere_Triclinic_MatchesBruteForce()
    {
        var cell = new CrystalCell(5, 6, 7, 70, 80, 100);
        CrystalAtom[] atoms =
        [
            new("Si1", "Si", 0.1, 0.2, 0.3),
            new("O1", "O", 0.9, 0.05, 0.6),
            new("O2", "O", 0.45, 0.8, 0.95),
        ];
        var s = new CrystalStructure(cell, atoms, []);
        var center = new FractionalPoint(0.3, 0.6, 0.1);
        const double radius = 8;
        CellBasis basis = cell.Basis();
        int expected = 0;
        foreach (CrystalAtom atom in atoms)
            for (int a = -8; a <= 8; a++)
                for (int b = -8; b <= 8; b++)
                    for (int c = -8; c <= 8; c++)
                    {
                        if (basis.Distance(atom.X + a - center.X, atom.Y + b - center.Y, atom.Z + c - center.Z) <= radius)
                            expected++;
                    }

        CrystalScene scene = Builder.Build(s, new CrystalSceneOptions
        {
            Clip = CrystalClip.Sphere,
            SphereCenter = center,
            SphereRadius = radius,
        });

        Assert.Null(scene.Warning);
        Assert.Equal(expected, scene.AtomCount);
    }

    [Fact]
    public void Sphere_HasNoCellEdges()
    {
        CrystalStructure s = NaCl();

        CrystalScene sphere = Builder.Build(s, OnChlorine(4.2));
        CrystalScene cell = Builder.Build(s, new CrystalSceneOptions());

        Assert.Empty(sphere.Scene.Items.OfType<LineItem>());
        Assert.Equal(12, cell.Scene.Items.OfType<LineItem>().Count());
    }

    [Fact]
    public void Sphere_AboveTheCeiling_FallsBackToOneCell_AndWarns()
    {
        // R = 30 A no NaCl: ~5000 imagens, acima do teto de 3000.
        CrystalStructure s = NaCl();

        CrystalScene scene = Builder.Build(s, OnChlorine(30));
        CrystalScene oneCell = Builder.Build(s, new CrystalSceneOptions { ShowBoundaryAtoms = true });

        Assert.NotNull(scene.Warning);
        Assert.Contains("sphere of radius 30", scene.Warning);
        Assert.Equal(oneCell.AtomCount, scene.AtomCount);
        Assert.Equal(12, scene.Scene.Items.OfType<LineItem>().Count());
    }

    [Fact]
    public void Sphere_FarAboveTheCeiling_IsNotBuilt()
    {
        CrystalStructure s = NaCl();

        CrystalScene scene = Builder.Build(s, OnChlorine(200));

        Assert.NotNull(scene.Warning);
        Assert.Contains("about", scene.Warning);
        Assert.Equal(Builder.Build(s, new CrystalSceneOptions()).AtomCount, scene.AtomCount);
    }

    [Fact]
    public void Sphere_PolyhedronAroundACenterInside_IsWhole()
    {
        // Fluorita com os cubos de F: esfera de 0,5 A no Ca da origem so pega o Ca, e o cubo vem inteiro, com os 8 F
        // esmaecidos.
        CrystalStructure s = Fixtures.FluoriteWithCubes();

        CrystalScene scene = Builder.Build(s, new CrystalSceneOptions
        {
            Style = CrystalStyle.Polyhedra,
            Clip = CrystalClip.Sphere,
            SphereCenter = new FractionalPoint(0, 0, 0),
            SphereRadius = 0.5,
        });

        Assert.Equal(1, scene.PolyhedronCount);
        var faded = scene.Scene.Items.OfType<SphereItem>().Where(x => scene.Lookup(x.Id)!.IsCompletion).ToList();
        Assert.Equal(8, faded.Count);
        Assert.Equal(9, scene.AtomCount);
    }
}
