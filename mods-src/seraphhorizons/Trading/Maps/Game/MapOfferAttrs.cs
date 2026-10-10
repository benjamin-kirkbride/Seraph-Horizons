using SeraphHorizons.Mod.Ore;

namespace SeraphHorizons.Mod.Trading.Maps;

/// <summary>
/// Stack attributes of map and lead offers (#455). An <b>offer</b> is the placeholder on a trader's
/// shelf (<see cref="Offer"/>: <c>oremap</c>, <c>gravelmap</c>, <c>lead</c>, <c>soldout</c> or
/// <c>surveying</c>), with
/// what it is for: the deposit (<see cref="Deposit"/>, <see cref="Metal"/>, <see cref="Precision"/>,
/// <see cref="SizeTier"/>, the ore map's own keys) or the lead's target (<see cref="LeadKind"/>,
/// <see cref="Cell"/>, <see cref="Type"/>, <see cref="X"/>, <see cref="Z"/>). A bought offer becomes
/// <b>pending</b> (<see cref="Pending"/>, a token) in the buyer's inventory until the sale is settled
/// and it is replaced by the real map or lead, or taken back with a refund.
/// </summary>
public static class MapOfferAttrs
{
    public const string Offer = "offer";
    public const string Pending = "pending";
    public const string Deposit = "depositId";
    public const string Metal = "metal";
    public const string Precision = "precision";
    public const string SizeTier = "sizeTier";
    public const string LeadKind = "leadKind";
    public const string Cell = "cell";
    public const string Type = "type";
    public const string X = "x";
    public const string Y = "y";
    public const string Z = "z";
    /// <summary>How far the deposit is from the trader, in blocks (ore and gravel map offers).</summary>
    public const string Distance = "distance";
    /// <summary>What the deposit is (#692), the map's own keys: <see cref="ItemOreMap.AttrOres"/>,
    /// <see cref="ItemOreMap.AttrGrades"/>, <see cref="ItemOreMap.AttrRock"/> (an ore deposit's host
    /// rock, a gravel field's rock) and <see cref="ItemOreMap.AttrMetals"/> (what a field pans).</summary>
    public const string Ores = ItemOreMap.AttrOres;
    public const string Grades = ItemOreMap.AttrGrades;
    public const string Rock = ItemOreMap.AttrRock;
    public const string Metals = ItemOreMap.AttrMetals;

    public const string OreMap = "oremap";
    public const string GravelMap = "gravelmap";
    public const string Lead = "lead";
    public const string SoldOut = "soldout";
    /// <summary>A deposit being checked (#693): shown unavailable, with its metal (ore) or as a
    /// gravel field, until the check lands and the real offer takes its place.</summary>
    public const string Surveying = "surveying";
}
