namespace SeraphHorizons.Mod.Trading.Maps;

/// <summary>
/// Stack attributes of map and lead offers (#455). An <b>offer</b> is the placeholder on a trader's
/// shelf (<see cref="Offer"/>: <c>oremap</c>, <c>gravelmap</c>, <c>lead</c>, or <c>soldout</c>), with
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

    public const string OreMap = "oremap";
    public const string GravelMap = "gravelmap";
    public const string Lead = "lead";
    public const string SoldOut = "soldout";
}
