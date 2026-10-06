using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

public class RegistryReplaceTests
{
    private static readonly DepositKey Copper = new("copper", new CellPos(1, 2));
    private static readonly DepositKey Iron = new("iron", new CellPos(0, 0));
    private static readonly DepositKey Gravel = new("gravel", new CellPos(3, 3));

    [Fact]
    public void ExportThenImportRoundTrips()
    {
        var registry = new DepositRegistry();
        registry.MarkSold(Copper, "uid", "Alice", 12.5);
        registry.RecordMeasure(Iron, 40, SizeTier.Small, 1, 2, 3, 4, workedOut: true);
        var other = new DepositRegistry();
        other.MarkSoldOut(Gravel);
        other.ReplaceWith(DepositRegistry.Parse(registry.Serialize()));
        Assert.Equal(registry.Serialize(), other.Serialize());
        Assert.Equal(DepositState.Unsold, other.Get(Gravel).State);
        Assert.Equal("Alice", other.Get(Copper).SoldToName);
        Assert.Equal(DepositState.SoldOut, other.Get(Iron).State);
    }

    [Fact]
    public void RemovesByKind()
    {
        var registry = new DepositRegistry();
        registry.MarkSold(Copper, null, null, 1);
        registry.MarkSoldOut(Iron);
        registry.MarkSoldOut(Gravel);
        Assert.Equal(1, registry.RemoveAll(k => k.Kind == "iron"));
        Assert.Equal(DepositState.Unsold, registry.Get(Iron).State);
        Assert.Equal(2, registry.RemoveAll(_ => true));
        Assert.Empty(registry.All());
    }
}
