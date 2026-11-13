namespace Viewer3D.Crystal;

/// <summary>
/// The arrangement <see cref="CrystalCell.FromParameters"/> uses to turn the six cell parameters into three Cartesian
/// edge vectors: the axes of M. Järvinen, J. Appl. Cryst. 26 (1993) 525-531, the frame of the spherical harmonics. Each
/// arrangement reads only the parameters its lattice leaves free.
/// </summary>
public enum CrystalLattice
{
    /// <summary>
    /// c along z, b in the yz plane, a with x &gt; 0 (x along a*); a_y = a (cos γ - cos α cos β) / sin α (Giacovazzo,
    /// Fundamentals of Crystallography, eq. 2.31b). Reads all six parameters.
    /// </summary>
    Triclinic,

    /// <summary>Monoclinic with unique axis b: b along z, a along y, c in the xy plane. Reads a, b, c and β.</summary>
    MonoclinicUniqueB,

    /// <summary>Monoclinic with unique axis c: c along z, b along y, a in the xy plane. Reads a, b, c and γ.</summary>
    MonoclinicUniqueC,

    /// <summary>Monoclinic with unique axis a: a along z, c along y, b in the xy plane. Reads a, b, c and α.</summary>
    MonoclinicUniqueA,

    /// <summary>a along x, b along y, c along z. Reads a, b and c.</summary>
    Orthorhombic,

    /// <summary>a along x, b along y, c along z. Reads a, b and c.</summary>
    Tetragonal,

    /// <summary>Rhombohedral axes: [111] along z, b in the xz plane. Reads a and α (a = b = c, α = β = γ).</summary>
    Rhombohedral,

    /// <summary>Hexagonal axes: b along y, c along z, a 30 degrees below x (γ = 120). Reads a, b and c.</summary>
    Hexagonal,

    /// <summary>a along x, b along y, c along z. Reads a, b and c.</summary>
    Cubic,
}
