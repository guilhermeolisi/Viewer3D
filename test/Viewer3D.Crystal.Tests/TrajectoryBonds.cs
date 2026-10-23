namespace Viewer3D.Crystal.Tests;

/// <summary>Counts the bonds a frame of <see cref="CrystalTrajectory.ToSequence"/> draws.</summary>
static class TrajectoryBonds
{
    /// <summary>
    /// The distinct bonds among the items of <paramref name="drawn"/>, each id resolved by the lookup of a build with the
    /// options and appearance <see cref="CrystalTrajectory.ToSequence"/> gives that frame: the caller's for a periodic
    /// frame, one block of the box without its edges for a molecule. An id the build does not know counts nothing.
    /// </summary>
    public static int InFrame(CrystalSceneBuilder builder, CrystalTrajectoryFrame frame, CrystalSceneOptions options, Scene drawn)
    {
        CrystalScene reference = frame.IsPeriodic
            ? builder.Build(frame.Structure, options)
            : builder.Build(frame.Structure, options with { RepeatA = 1, RepeatB = 1, RepeatC = 1, Clip = CrystalClip.Cell },
                CrystalAppearance.Default with { ShowCell = false });
        return drawn.Items.Select(i => reference.Lookup(i.Id)).Where(i => i?.Kind == CrystalItemKind.Bond)
            .Select(i => i!.BondIndex).Distinct().Count();
    }
}
