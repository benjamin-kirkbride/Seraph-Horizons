using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// Makes ore and gravel maps (#444) and reserves their deposits, for the traders that sell them
/// (#455) and the admin <c>/sh ore givemap</c>. Server side: <see cref="OreSystem.Maps"/>. See
/// <c>docs/oregen.md</c> "Maps" for how a trader uses it:
/// <code>
/// var deposits = oreSystem.Deposits;               // null: no ore cells or placer fields here
/// var near = deposits.Candidates(x, z, 4000, "copper").Where(c => c.Record.State == DepositState.Unsold);
/// deposits.Verify(near.First().Key, result =>       // generates and measures, on the main thread
/// {
///     if (result.Status != VerifyStatus.Measured || result.WorkedOut) return;   // sold out
///     ItemStack? map = oreSystem.Maps.Issue(buyer, result.Candidate!.Key, MapPrecision.Fair);
///     // null: sold meanwhile; otherwise the deposit is now sold to the buyer
/// });
/// </code>
/// </summary>
public sealed class MapIssuer
{
    public static readonly AssetLocation OreMapCode = new("seraphhorizons", "oremap");
    public static readonly AssetLocation GravelMapCode = new("seraphhorizons", "gravelmap");

    private readonly ICoreServerAPI _api;
    private readonly DepositService _deposits;

    public MapIssuer(ICoreServerAPI api, DepositService deposits)
    {
        _api = api;
        _deposits = deposits;
    }

    /// <summary>
    /// A map to an unsold deposit, which is marked sold to <paramref name="player"/> (null: to no one
    /// in particular) now. Null, and nothing changed, when the deposit is sold or sold out already,
    /// the cell has none, or the precision is not 1–3. Gravel maps are always exact, whatever
    /// <paramref name="precision"/> says. The map's size tier is the deposit's last measurement
    /// (<see cref="DepositService.Verify"/> first, or the map shows none).
    /// </summary>
    public ItemStack? Issue(IPlayer? player, DepositKey deposit, int precision)
    {
        if (Build(deposit, precision) is not { } stack) return null;
        if (!_deposits.Registry.MarkSold(deposit, player?.PlayerUID, player?.PlayerName, _api.World.Calendar.TotalDays))
            return null;
        _api.Logger.Notification("[seraphhorizons] Deposits: map to {0} (precision {1}) issued to {2}",
            deposit, deposit.IsGravel ? MapPrecision.Exact : precision, player?.PlayerName ?? "nobody");
        return stack;
    }

    /// <summary>The map to a deposit, without reserving it (it may be sold already); null if the
    /// cell has none, the map item is missing, or the precision is not 1–3.</summary>
    public ItemStack? Build(DepositKey deposit, int precision)
    {
        if (deposit.IsGravel) precision = MapPrecision.Exact;
        if (!MapPrecision.IsValid(precision) || _deposits.Candidate(deposit) is not { } candidate) return null;
        var item = _api.World.GetItem(deposit.IsGravel ? GravelMapCode : OreMapCode);
        if (item == null) return null;
        var (dx, dz) = MapPrecision.Offset(_api.World.Seed, deposit, precision);
        var stack = new ItemStack(item);
        var a = stack.Attributes;
        a.SetString(ItemOreMap.AttrDeposit, deposit.Id);
        a.SetString(ItemOreMap.AttrMetal, deposit.Kind);
        a.SetInt(ItemOreMap.AttrPrecision, precision);
        if (candidate.Record.Tier is { } tier) a.SetString(ItemOreMap.AttrSizeTier, tier.ToString().ToLowerInvariant());
        // What the ore is (#692): the ores, their grades and the host rock; for a gravel field its
        // rock and the metals the pan gives from it.
        if (candidate.Record.Makeup is { } makeup) ItemOreMap.SetMakeup(a, makeup);
        if (_deposits.FieldOf(deposit)?.Rock is { } rock)
        {
            a.SetString(ItemOreMap.AttrRock, rock);
            if (_deposits.PanMetals(rock) is { Count: > 0 } metals) a.SetString(ItemOreMap.AttrMetals, OreNames.Csv(metals));
        }
        a.SetInt(ItemOreMap.AttrX, candidate.X + dx);
        a.SetInt(ItemOreMap.AttrY, candidate.Y);
        a.SetInt(ItemOreMap.AttrZ, candidate.Z + dz);
        return stack;
    }
}
