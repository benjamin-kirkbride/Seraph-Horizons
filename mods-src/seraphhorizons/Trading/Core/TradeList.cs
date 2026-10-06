namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>An average and spread, as the game's NatFloat in trade lists (<c>{ avg, var }</c>).</summary>
public sealed class NatSpec
{
    public float Avg { get; set; }
    public float Var { get; set; }

    public NatSpec() { }
    public NatSpec(float avg, float var) { Avg = avg; Var = var; }
}

/// <summary>
/// One trade entry: vanilla's trade item fields (<c>type</c>, <c>code</c>, <c>attributes</c>,
/// <c>stacksize</c>, <c>stock</c>, <c>price</c>), so an entry turns into the game's TradeItem as it
/// is, plus <c>playerSupplied</c>: goods that exist at traders only because players sold them
/// (metal and metal goods, glass and fired goods, leather and fine cloth, machine parts). A
/// player-supplied entry on the selling side is never stocked from the list; it is shelved only when
/// the <see cref="ISupplyGate"/> says so, with the stock it gives. On the buying side the flag is
/// informational: a trader buys them like anything else (that is how supply comes in).
/// Fields later waves add (standing gates, value overrides) are read from the same object; unknown
/// fields are ignored.
/// </summary>
public sealed class TradeEntry
{
    public string Type { get; set; } = "item";
    public string Code { get; set; } = "";
    /// <summary>The stack's attributes as the list gives them (a JSON object on the game side).</summary>
    public object? Attributes { get; set; }
    public int StackSize { get; set; } = 1;
    public NatSpec? Stock { get; set; }
    public NatSpec? Price { get; set; }
    public bool PlayerSupplied { get; set; }

    /// <summary>The attributes in a canonical text form, set by the loader; part of <see cref="Key"/>.</summary>
    public string AttributesKey { get; set; } = "";

    /// <summary>What identifies the stack: two entries with the same key are the same goods.</summary>
    public string Key => $"{Type}:{(Code.Contains(':') ? Code : "game:" + Code)}{AttributesKey}";

    public override string ToString() => Key;
}

public sealed class RotatingList
{
    /// <summary>How many rotating slots the trader fills at a restock.</summary>
    public int MaxItems { get; set; }
    public List<TradeEntry> List { get; set; } = [];
}

/// <summary>What one region key adds to a side: always-stocked entries and rotating candidates.</summary>
public sealed class RegionalList
{
    public List<TradeEntry> Core { get; set; } = [];
    public List<TradeEntry> Rotating { get; set; } = [];
}

/// <summary>One side of a list (what the trader sells, or buys).</summary>
public sealed class TradeSide
{
    public List<TradeEntry> Core { get; set; } = [];
    public RotatingList Rotating { get; set; } = new();
    /// <summary>Keyed by a climate band (<c>cold</c>, <c>temperate</c>, <c>hot</c>) or a rock group
    /// (<c>sedimentary</c>, <c>igneous</c>, <c>metamorphic</c>); a trader takes both its keys'.</summary>
    public Dictionary<string, RegionalList> Regional { get; set; } = new();
}

/// <summary>
/// A trader type's list, <c>assets/seraphhorizons/config/tradelists/trader-{type}.json</c>. The format
/// (docs/trading.md) extends vanilla's: a core always in stock, rotating slots drawn from a pool,
/// regional additions, player-supplied entries, and a wallet per standing tier.
/// </summary>
public sealed class TradeListDef
{
    /// <summary>The trader type, the file's <c>{type}</c>.</summary>
    public string Type { get; set; } = "";
    /// <summary>How often the grid picks this type for a cell, against the others (the prospector's
    /// lattice ignores it).</summary>
    public double CampWeight { get; set; } = 1;
    /// <summary>Gears the trader restocks to, by standing tier (index 0 for everyone until standing
    /// exists, #452).</summary>
    public List<NatSpec> Wallet { get; set; } = [];
    public TradeSide Selling { get; set; } = new();
    public TradeSide Buying { get; set; } = new();

    public NatSpec WalletFor(int tier) => Wallet.Count == 0 ? new NatSpec(60, 10) : Wallet[Math.Clamp(tier, 0, Wallet.Count - 1)];
}
