using SeraphHorizons.Mod.Trading.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Commands;

/// <summary>
/// <c>/sh trade ...</c>, the trader admin commands (privilege controlserver). <c>/sh</c> is the
/// pack's admin root, made with GetOrCreate so other features can hang their own subcommands on it;
/// <c>trade</c> collects the trader ones (#459 adds more under it with <see cref="Trade"/>).
/// <list type="bullet">
/// <item><c>/sh trade camps [radius]</c>: every grid cell within radius blocks (default
/// <see cref="DefaultRadius"/>) of the caller, or of the world spawn from the console: its id, type,
/// and the placed camp or the spot it waits for.</item>
/// <item><c>/sh trade tp &lt;id&gt;</c>: to a cell's camp (id <c>cellX,cellZ</c> as listed); a cell
/// not generated yet is generated first, which places its camp if its spot fits.</item>
/// </list>
/// </summary>
public static class TradeCommands
{
    public const int DefaultRadius = 4096;
    public const int MaxRadius = 32768;

    /// <summary>The <c>/sh trade</c> command, for later waves to add subcommands to.</summary>
    public static IChatCommand? Trade { get; private set; }

    public static void Register(ICoreServerAPI api, TradingSystem system)
    {
        var parsers = api.ChatCommands.Parsers;
        var sh = api.ChatCommands.GetOrCreate("sh")
            .WithDescription("Seraph Horizons admin commands")
            .RequiresPrivilege(Privilege.controlserver);
        Trade = sh.BeginSubCommand("trade")
            .WithDescription("Traders: camps on the grid")
            .RequiresPrivilege(Privilege.controlserver);
        Trade.BeginSubCommand("camps")
                .WithDescription($"List the trader camp cells within radius blocks (default {DefaultRadius}) of you, or of the spawn")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OptionalInt("radius", DefaultRadius))
                .HandleWith(args => Camps(api, system, args))
            .EndSubCommand()
            .BeginSubCommand("tp")
                .WithDescription("Teleport to a cell's trader camp (id as /sh trade camps lists it)")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(parsers.Word("id"))
                .HandleWith(args => Teleport(api, system, args))
            .EndSubCommand();
    }

    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    private static TextCommandResult Camps(ICoreServerAPI api, TradingSystem system, TextCommandCallingArgs args)
    {
        if (!system.GridReady)
            return TextCommandResult.Error(L("trading-grid-off", system.GridOffReason));
        int radius = (int)args[0];
        if (radius < 1 || radius > MaxRadius)
            return TextCommandResult.Error(L("trading-camps-radius", MaxRadius));
        var origin = args.Caller.Entity?.Pos.AsBlockPos ?? api.World.DefaultSpawnPosition.AsBlockPos;
        var rows = CampListing.Rows(system.Grid!, system.Camps!.Registry, origin.X, origin.Z, radius);
        int offX = api.World.DefaultSpawnPosition.XYZInt.X, offZ = api.World.DefaultSpawnPosition.XYZInt.Z;
        var lines = new List<string> { L("trading-camps-header", rows.Count, radius, origin.X - offX, origin.Z - offZ) };
        lines.AddRange(rows.Select(r => CampListing.Line(r, offX, offZ, (key, a) => Lang.Get(key, a))));
        SeraphHorizons.Mod.Admin.AdminCommands.Attach(args, new System.Text.Json.Nodes.JsonObject
        {
            ["radius"] = radius,
            ["camps"] = SeraphHorizons.Mod.Core.AdminOutput.Rows(rows, r => new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = r.Cell.ToString(), ["type"] = r.Type, ["status"] = r.Status.ToString().ToLowerInvariant(),
                ["x"] = r.X, ["y"] = r.Y, ["z"] = r.Z, ["distance"] = (int)Math.Round(r.Distance), ["region"] = r.Region,
                ["settlementReserve"] = TraderGrid.InSettlementReserve(r.X, r.Z),
            }),
        });
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    private static TextCommandResult Teleport(ICoreServerAPI api, TradingSystem system, TextCommandCallingArgs args)
    {
        if (!system.GridReady)
            return TextCommandResult.Error(L("trading-grid-off", system.GridOffReason));
        if (!CellKey.TryParse((string)args[0], out var cell))
            return TextCommandResult.Error(L("trading-tp-badid", (string)args[0]));
        var spots = system.Grid!.Spots(cell);
        var record = system.Camps!.Registry.Get(cell);
        if (spots.Count == 0 || record is { Status: CampStatus.Failed })
            return TextCommandResult.Error(L("trading-tp-nocamp", cell.ToString()));
        var entity = args.Caller.Entity;
        if (record is { Status: CampStatus.Placed })
        {
            Go(api, entity, record.X, record.Z);
            return TextCommandResult.Success(L("trading-tp-done", cell.ToString()));
        }
        // Not generated yet: generating the spot's chunk decides the camp; go once it has.
        var spot = spots[Math.Min(record?.Attempt ?? 0, spots.Count - 1)];
        api.WorldManager.LoadChunkColumnPriority(spot.ChunkX, spot.ChunkZ, new ChunkLoadOptions
        {
            OnLoaded = () =>
            {
                var now = system.Camps!.Registry.Get(cell);
                if (now is { Status: CampStatus.Placed }) Go(api, entity, now.X, now.Z);
                else Go(api, entity, spot.X, spot.Z);
            },
        });
        return TextCommandResult.Success(L("trading-tp-generating", cell.ToString()));
    }

    private static void Go(ICoreServerAPI api, Entity entity, int x, int z)
    {
        var pos = new BlockPos(x, 0, z);
        int y = api.World.BlockAccessor.GetTerrainMapheightAt(pos);
        if (y <= 0) y = api.World.SeaLevel + 40;
        entity.TeleportToDouble(x + 0.5, y + 2, z + 0.5);
    }
}
