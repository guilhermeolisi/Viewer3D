namespace Viewer3D.Crystal.Tests;

public class ElementTableTests
{
    [Fact]
    public void EveryElementFrom1To118_HasAColorAndAPositiveRadius()
    {
        IReadOnlyList<ElementInfo> all = ElementTable.All;

        Assert.Equal(118, all.Count);
        for (int z = 1; z <= 118; z++)
        {
            ElementInfo e = all[z - 1];
            Assert.Equal(z, e.Z);
            Assert.True(e.Radius > 0, e.Symbol);
            Assert.Equal(255, e.Color.A);
        }
    }

    [Fact]
    public void Symbols_AreUniqueAndNormalized()
    {
        IReadOnlyList<ElementInfo> all = ElementTable.All;

        Assert.Equal(all.Count, all.Select(e => e.Symbol).Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, e => Assert.Equal(ElementTable.Normalize(e.Symbol), e.Symbol));
        Assert.True(ElementTable.TryGet("CA", out ElementInfo ca));
        Assert.Equal("Ca", ca.Symbol);
        Assert.True(ElementTable.TryGet("fe3+", out ElementInfo fe));
        Assert.Equal(26, fe.Z);
        Assert.True(ElementTable.TryGet(" O2- ", out ElementInfo o));
        Assert.Equal(8, o.Z);
        Assert.False(ElementTable.TryGet("Xx", out _));
        Assert.False(ElementTable.TryGet("", out _));
    }

    [Fact]
    public void Radius_IsMetallicForMetals_AndCovalentForTheOthers()
    {
        // Metalico (pymatgen): Fe 1,277, Cu 1,278. Covalente (Cordero 2008): O 0,66, F 0,57, Si 1,11.
        Assert.True(ElementTable.TryGet("Fe", out ElementInfo fe));
        Assert.True(fe.IsMetal);
        Assert.Equal(1.277, fe.Radius, 1e-9);
        Assert.True(ElementTable.TryGet("Cu", out ElementInfo cu));
        Assert.Equal(1.278, cu.Radius, 1e-9);
        Assert.True(ElementTable.TryGet("O", out ElementInfo o));
        Assert.False(o.IsMetal);
        Assert.Equal(0.66, o.Radius, 1e-9);
        Assert.True(ElementTable.TryGet("F", out ElementInfo f));
        Assert.Equal(0.57, f.Radius, 1e-9);
        Assert.True(ElementTable.TryGet("Si", out ElementInfo si));
        Assert.False(si.IsMetal);
        Assert.Equal(1.11, si.Radius, 1e-9);
    }

    [Fact]
    public void ElementsWithoutAReferenceValue_AreMarkedEstimated()
    {
        Assert.True(ElementTable.TryGet("Og", out ElementInfo og));
        Assert.True(og.IsEstimated);
        Assert.True(ElementTable.TryGet("Ca", out ElementInfo ca));
        Assert.False(ca.IsEstimated);
        Assert.All(ElementTable.All.Where(e => e.Z <= 96), e => Assert.False(e.IsEstimated, e.Symbol));
    }
}
