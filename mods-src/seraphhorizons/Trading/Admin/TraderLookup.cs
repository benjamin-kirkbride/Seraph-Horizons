using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Standing;
using SeraphHorizons.Mod.Trading.Standing.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Admin;

/// <summary>
/// The trader an admin command names (#459): <c>near</c> (ours nearest the caller within
/// <see cref="NearRange"/> blocks), a trader id as the standing views list it (<c>camp:x,z</c>,
/// <c>entity:n</c>), or a camp cell as <c>/sh trade camps</c> lists it (<c>x,z</c>). Only loaded
/// traders are found: what the commands change lives on the entity.
/// </summary>
public static class TraderLookup
{
    public const double NearRange = 16;

    /// <summary>The loaded trader, or null with the reason in <paramref name="why"/>.</summary>
    public static EntitySeraphTrader? Find(ICoreServerAPI api, TextCommandCallingArgs args, string? text, out string why)
    {
        why = "";
        text = (text ?? "near").Trim().ToLowerInvariant();
        if (text is "near" or "")
        {
            var pos = args.Caller.Entity?.Pos.XYZ ?? args.Caller.Pos;
            if (pos == null)
            {
                why = "near needs a caller in the world";
                return null;
            }
            if (api.World.GetNearestEntity(pos, (float)NearRange, (float)NearRange, e => e is EntitySeraphTrader) is EntitySeraphTrader near)
                return near;
            why = $"no trader of ours within {NearRange} blocks";
            return null;
        }
        var traders = Loaded(api);
        if (text.StartsWith("entity:", StringComparison.Ordinal) && long.TryParse(text["entity:".Length..], out long id))
        {
            if (api.World.GetEntityById(id) is EntitySeraphTrader byId) return byId;
            why = $"no loaded trader {text}";
            return null;
        }
        string cellText = text.StartsWith("camp:", StringComparison.Ordinal) ? text["camp:".Length..] : text;
        if (!CellKey.TryParse(cellText, out var cell))
        {
            why = $"'{text}' is not a trader: near, camp:x,z, entity:n or a camp cell x,z";
            return null;
        }
        string campId = TraderIds.Camp(cell.X, cell.Z);
        var system = StandingSystem.Of(api);
        var found = traders.FirstOrDefault(t => system is { Enabled: true } ? system.TraderIdOf(t) == campId : TraderGrid.CellOf((int)t.Pos.X, (int)t.Pos.Z) == cell);
        if (found != null) return found;
        why = $"no loaded trader in camp {cell}";
        return null;
    }

    public static List<EntitySeraphTrader> Loaded(ICoreServerAPI api) =>
        api.World.LoadedEntities.Values.OfType<EntitySeraphTrader>().Where(t => t.State != EnumEntityState.Despawned).ToList();

    /// <summary>The trader's id: the standing system's when on, else its entity id.</summary>
    public static string IdOf(ICoreServerAPI api, EntitySeraphTrader trader) =>
        StandingSystem.Of(api) is { Enabled: true } s ? s.TraderIdOf(trader) : TraderIds.Entity(trader.EntityId);
}
