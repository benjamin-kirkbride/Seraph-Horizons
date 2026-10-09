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
/// <c>stacksize</c>, <c>stock</c>), so an entry turns into the game's TradeItem as it is, but no
/// price: a listed good is priced from the item value table at every restock
/// (<see cref="SeraphHorizons.Mod.Trading.Economy.Core.Pricing.ListBase"/>). <c>price</c> is an
/// override, gears per the entry's stack, that must say why in <c>priceReason</c>: schematics (hand-set
/// gates) and the special entries the maps system prices. Plus <c>playerSupplied</c>: goods that exist at traders only because players sold them
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
    /// <summary>An override: gears per <see cref="StackSize"/>, in place of the value table's. Only with
    /// a <see cref="PriceReason"/> (<see cref="TradeListResolver.Problems"/>).</summary>
    public double? Price { get; set; }
    /// <summary>Why <see cref="Price"/> overrides the value table.</summary>
    public string? PriceReason { get; set; }
    public bool PlayerSupplied { get; set; }
    /// <summary>The buyer's standing tier from which the entry is in the core (#452, #468): 0, the
    /// default, for everyone. Schematics carry it; their price is the rest of the gate.</summary>
    public int StandingTier { get; set; }
    /// <summary>Rare stock (#452): shelved only when the trader stocks for a tier whose
    /// <c>rareStock</c> unlock is on (the best recent customer's, as the wallet).</summary>
    public bool Rare { get; set; }
    /// <summary>A special entry (#455): not goods but a kind of offer the trader makes up at each
    /// restock (<c>oremap</c>, <c>gravelmap</c>, <c>lead</c>), expanded by
    /// <see cref="TradeOffers.Expand"/>; null for goods. Without an expander it is left out.</summary>
    public string? Kind { get; set; }
    /// <summary>Set on an expanded offer that gives way first when the core is over the slots.</summary>
    public bool Optional { get; set; }

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
/// regional additions, player-supplied entries, and a base wallet that standing multiplies.
/// </summary>
public sealed class TradeListDef
{
    /// <summary>The trader type, the file's <c>{type}</c>.</summary>
    public string Type { get; set; } = "";
    /// <summary>How often the grid picks this type for a cell, against the others (the prospector's
    /// lattice ignores it).</summary>
    public double CampWeight { get; set; } = 1;
    /// <summary>Gears the trader restocks to with strangers; a standing tier's <c>walletFactor</c>
    /// multiplies it (#452).</summary>
    public NatSpec? Wallet { get; set; }
    public TradeSide Selling { get; set; } = new();
    public TradeSide Buying { get; set; } = new();

    /// <summary>The wallet times a standing tier's <paramref name="factor"/>, its spread with it.</summary>
    public NatSpec WalletAt(double factor = 1)
    {
        var w = Wallet ?? new NatSpec(60, 10);
        return new NatSpec((float)(w.Avg * factor), (float)(w.Var * factor));
    }
}
