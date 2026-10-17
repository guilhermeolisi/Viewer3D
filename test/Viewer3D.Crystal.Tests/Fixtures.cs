namespace Viewer3D.Crystal.Tests;

/// <summary>Structures written by hand, with their sources.</summary>
internal static class Fixtures
{
    /// <summary>Fluorita CaF2, Fm-3m, a = 5,463 A (ICSD/AMCSD): Ca em 4a, F em 8c. Ca-F = a sqrt(3)/4 = 2,3656 A.</summary>
    public const double FluoriteA = 5.463;

    public static double FluoriteCaF => FluoriteA * Math.Sqrt(3) / 4;

    public static readonly (double X, double Y, double Z)[] Ca4a = [(0, 0, 0), (0, 0.5, 0.5), (0.5, 0, 0.5), (0.5, 0.5, 0)];

    public static CrystalAtom[] FluoriteAtoms()
    {
        var atoms = new List<CrystalAtom>();
        for (int k = 0; k < Ca4a.Length; k++)
            atoms.Add(new CrystalAtom("Ca1", "Ca", Ca4a[k].X, Ca4a[k].Y, Ca4a[k].Z) { SiteIndex = 0, SourceIndex = 0 });
        foreach (double x in new[] { 0.25, 0.75 })
            foreach (double y in new[] { 0.25, 0.75 })
                foreach (double z in new[] { 0.25, 0.75 })
                    atoms.Add(new CrystalAtom("F1", "F", x, y, z) { SiteIndex = 1, SourceIndex = 1 });
        return [.. atoms];
    }

    /// <summary>The fluorite with every Ca-F bond given once, from Ca (32 bonds, 8 per Ca, 4 per F).</summary>
    public static CrystalStructure Fluorite() => WithBondsBelow(FluoriteCell, FluoriteAtoms(), 2.5, (a, b) => a.Element == "Ca" && b.Element == "F");

    public static CrystalCell FluoriteCell => new(FluoriteA, FluoriteA, FluoriteA, 90, 90, 90);

    /// <summary>Every pair (first, second image in -1..1) closer than <paramref name="limit"/> and accepted by the filter.</summary>
    public static CrystalStructure WithBondsBelow(CrystalCell cell, IReadOnlyList<CrystalAtom> atoms, double limit,
        Func<CrystalAtom, CrystalAtom, bool> accept)
    {
        var bonds = new List<CrystalBond>();
        for (int i = 0; i < atoms.Count; i++)
            for (int j = 0; j < atoms.Count; j++)
            {
                if (!accept(atoms[i], atoms[j]))
                    continue;
                for (int a = -1; a <= 1; a++)
                    for (int b = -1; b <= 1; b++)
                        for (int c = -1; c <= 1; c++)
                        {
                            if (i == j && a == 0 && b == 0 && c == 0)
                                continue;
                            double d = Distance(cell, atoms[i], atoms[j], new LatticeShift(a, b, c));
                            if (d < limit)
                                bonds.Add(new CrystalBond(i, j, new LatticeShift(a, b, c)) { SourceIndex = bonds.Count });
                        }
            }
        return new CrystalStructure(cell, atoms, bonds);
    }

    public static double Distance(CrystalCell cell, CrystalAtom p, CrystalAtom q, LatticeShift s)
        => cell.Basis().Distance(q.X + s.A - p.X, q.Y + s.B - p.Y, q.Z + s.C - p.Z);

    public static CrystalStructure Metal(string element, CrystalCell cell, params (double X, double Y, double Z)[] positions)
        => new(cell, [.. positions.Select(p => new CrystalAtom(element + "1", element, p.X, p.Y, p.Z) { SiteIndex = 0 })], []);

    /// <summary>Polonio cubico simples, a = 3,359 A: vizinho a 3,359 A.</summary>
    public static CrystalStructure Polonium() => Metal("Po", new CrystalCell(3.359, 3.359, 3.359, 90, 90, 90), (0, 0, 0));

    /// <summary>Ferro alfa, CCC, a = 2,8665 A: vizinho a a sqrt(3)/2 = 2,4825 A.</summary>
    public static CrystalStructure Iron() => Metal("Fe", new CrystalCell(2.8665, 2.8665, 2.8665, 90, 90, 90), (0, 0, 0), (0.5, 0.5, 0.5));

    /// <summary>Cobre, CFC, a = 3,615 A: vizinho a a/sqrt(2) = 2,5562 A.</summary>
    public static CrystalStructure Copper() => Metal("Cu", new CrystalCell(3.615, 3.615, 3.615, 90, 90, 90), Ca4a);

    /// <summary>Magnesio HC, a = 3,2094 A, c = 5,2108 A, 2c: vizinho fora do plano a sqrt(a^2/3 + c^2/4) = 3,1966 A.</summary>
    public static CrystalStructure Magnesium()
        => Metal("Mg", new CrystalCell(3.2094, 3.2094, 5.2108, 90, 90, 120), (1.0 / 3, 2.0 / 3, 0.25), (2.0 / 3, 1.0 / 3, 0.75));

    /// <summary>
    /// The polyhedron around atom <paramref name="center"/>: the atoms accepted by the filter closer than
    /// <paramref name="limit"/> (images -1..1), with their convex hull as faces (<see cref="HullFaces"/>).
    /// </summary>
    public static CrystalPolyhedron PolyhedronAround(CrystalCell cell, IReadOnlyList<CrystalAtom> atoms, int center, double limit,
        Func<CrystalAtom, bool> accept)
    {
        CellBasis basis = cell.Basis();
        CrystalAtom c = atoms[center];
        var vertices = new List<PolyhedronVertex>();
        var relative = new List<System.Numerics.Vector3>();
        for (int j = 0; j < atoms.Count; j++)
        {
            if (j == center || !accept(atoms[j]))
                continue;
            for (int a = -1; a <= 1; a++)
                for (int b = -1; b <= 1; b++)
                    for (int k = -1; k <= 1; k++)
                    {
                        var s = new LatticeShift(a, b, k);
                        if (Distance(cell, c, atoms[j], s) >= limit)
                            continue;
                        vertices.Add(new PolyhedronVertex(j, s));
                        relative.Add(basis.ToCartesian(atoms[j].X + a - c.X, atoms[j].Y + b - c.Y, atoms[j].Z + k - c.Z));
                    }
        }
        return new CrystalPolyhedron(center, vertices, HullFaces(relative));
    }

    /// <summary>
    /// Faces of the convex hull of points around the origin, coplanar triangles merged into one polygon, each
    /// counter-clockwise seen from outside. Brute force over every triple: fine for a dozen points.
    /// </summary>
    public static List<int[]> HullFaces(IReadOnlyList<System.Numerics.Vector3> p)
    {
        var faces = new List<int[]>();
        var seen = new HashSet<string>();
        for (int i = 0; i < p.Count; i++)
            for (int j = i + 1; j < p.Count; j++)
                for (int k = j + 1; k < p.Count; k++)
                {
                    System.Numerics.Vector3 normal = System.Numerics.Vector3.Cross(p[j] - p[i], p[k] - p[i]);
                    if (normal.Length() < 1e-6f)
                        continue;
                    normal = System.Numerics.Vector3.Normalize(normal);
                    float d = System.Numerics.Vector3.Dot(normal, p[i]);
                    bool above = false, below = false;
                    var on = new List<int>();
                    for (int m = 0; m < p.Count; m++)
                    {
                        float s = System.Numerics.Vector3.Dot(normal, p[m]) - d;
                        if (MathF.Abs(s) < 1e-4f) on.Add(m);
                        else if (s > 0) above = true;
                        else below = true;
                    }
                    if (above && below)
                        continue;
                    if (above)
                        normal = -normal;
                    if (!seen.Add(string.Join(",", on.Order())))
                        continue;
                    var centroid = System.Numerics.Vector3.Zero;
                    foreach (int m in on)
                        centroid += p[m];
                    centroid /= on.Count;
                    System.Numerics.Vector3 u = System.Numerics.Vector3.Normalize(p[on[0]] - centroid);
                    System.Numerics.Vector3 w = System.Numerics.Vector3.Cross(normal, u);
                    faces.Add([.. on.OrderBy(m => MathF.Atan2(System.Numerics.Vector3.Dot(p[m] - centroid, w),
                        System.Numerics.Vector3.Dot(p[m] - centroid, u)))]);
                }
        return faces;
    }

    /// <summary>The fluorite with a cube of F around each of its four Ca.</summary>
    public static CrystalStructure FluoriteWithCubes()
    {
        CrystalStructure s = Fluorite();
        return s with
        {
            Polyhedra = [.. Enumerable.Range(0, 4).Select(i => PolyhedronAround(s.Cell, s.Atoms, i, 2.5, a => a.Element == "F"))],
        };
    }

    /// <summary>
    /// Coordenacao 8 em bipiramide hexagonal (12 faces triangulares, 18 arestas), numa celula cubica de 10 A: Zr no
    /// centro, seis O no equador e dois nos polos, todos a 2 A.
    /// </summary>
    public static CrystalStructure HexagonalBipyramid()
    {
        var atoms = new List<CrystalAtom> { new("Zr1", "Zr", 0.5, 0.5, 0.5) };
        for (int k = 0; k < 6; k++)
            atoms.Add(new CrystalAtom("O1", "O", 0.5 + 0.2 * Math.Cos(k * Math.PI / 3), 0.5 + 0.2 * Math.Sin(k * Math.PI / 3), 0.5));
        atoms.Add(new CrystalAtom("O2", "O", 0.5, 0.5, 0.3));
        atoms.Add(new CrystalAtom("O2", "O", 0.5, 0.5, 0.7));
        var cell = new CrystalCell(10, 10, 10, 90, 90, 90);
        return new CrystalStructure(cell, atoms, [])
        {
            Polyhedra = [PolyhedronAround(cell, atoms, 0, 2.1, a => a.Element == "O")],
        };
    }
}
