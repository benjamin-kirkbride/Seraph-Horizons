using System.Text;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// <c>/sh ore</c>, for admins (<c>controlserver</c>): where the ore cell rule puts deposits, and
/// why. Enough to test the rule; the survey tools come later (#458), as further subcommands here.
/// <list type="bullet">
/// <item><c>/sh ore cell &lt;x&gt; &lt;z&gt; &lt;metal&gt;</c>: the cell holding the absolute block
/// position, its spots in order and how each stands.</item>
/// <item><c>/sh ore here</c>: for every managed metal, the active spot of the caller's cell, its
/// distance and direction.</item>
/// </list>
/// <c>/sh</c> is the pack's command root, shared by features (<c>GetOrCreate</c>).
/// </summary>
internal sealed class OreCommands
{
    public const string Root = "sh";

    private readonly ICoreServerAPI _api;
    private readonly OreSystem _system;

    public OreCommands(ICoreServerAPI api, OreSystem system)
    {
        _api = api;
        _system = system;
    }

    public void Register()
    {
        var parsers = _api.ChatCommands.Parsers;
        var root = _api.ChatCommands.GetOrCreate(Root);
        if (string.IsNullOrEmpty(root.Description))
            root.WithDescription("Seraph Horizons commands.");
        root.RequiresPrivilege(Privilege.chat)
            .BeginSubCommand("ore")
                .WithDescription("Ore cells: where the seed puts each metal's deposit.")
                .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("cell")
                    .WithDescription("The ore cell holding an absolute block position, for a metal: its spots and how each stands.")
                    .WithArgs(parsers.Int("x"), parsers.Int("z"), parsers.Word("metal", OreMetals.All.ToArray()))
                    .HandleWith(OnCell)
                .EndSubCommand()
                .BeginSubCommand("here")
                    .WithDescription("Every metal's deposit spot in the ore cells where you stand.")
                    .RequiresPlayer()
                    .HandleWith(OnHere)
                .EndSubCommand()
            .EndSubCommand();
    }

    private TextCommandResult OnCell(TextCommandCallingArgs args)
    {
        int x = (int)args[0], z = (int)args[1];
        string metal = ((string)args[2]).ToLowerInvariant();
        string lang = args.LanguageCode ?? Lang.DefaultLocale;
        if (!OreMetals.All.Contains(metal))
            return TextCommandResult.Error(Msg(lang, "unknown-metal", metal, string.Join(", ", OreMetals.All)));
        if (_system.Placement is not { } placement)
            return TextCommandResult.Success(Msg(lang, "off"));
        var cell = placement.Cells.CellOf(metal, x, z);
        var state = placement.StateOf(metal, cell);
        Admin.AdminCommands.Attach(args, new System.Text.Json.Nodes.JsonObject
        {
            ["metal"] = metal,
            ["cell"] = new System.Text.Json.Nodes.JsonObject { ["x"] = cell.X, ["z"] = cell.Z },
            ["cellSize"] = placement.Cells.CellSize(metal),
            ["managed"] = placement.Managed.Contains(metal),
            ["active"] = state.None ? null : state.Active,
            ["placed"] = state.Placed,
            ["spots"] = new System.Text.Json.Nodes.JsonArray(placement.SpotsOf(metal, cell).Select(s => (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonObject
            {
                ["index"] = s.Index, ["x"] = s.X, ["z"] = s.Z,
                ["status"] = placement.StatusOf(metal, cell, s.Index).ToString().ToLowerInvariant(),
            }).ToArray()),
        });
        return TextCommandResult.Success(CellTrace(placement, lang, metal, x, z));
    }

    /// <summary>The decision trace for one metal's cell.</summary>
    internal string CellTrace(OreCellPlacement placement, string lang, string metal, int x, int z)
    {
        var cells = placement.Cells;
        var cell = cells.CellOf(metal, x, z);
        var state = placement.StateOf(metal, cell);
        var sb = new StringBuilder();
        sb.Append(Msg(lang, "cell-head", metal, x, z, cell.X, cell.Z, cells.CellSize(metal), cells.Seed));
        if (!placement.Managed.Contains(metal))
            sb.Append('\n').Append(Msg(lang, "not-managed", metal));
        var (sx, sz) = Spawn();
        foreach (var spot in placement.SpotsOf(metal, cell))
            sb.Append('\n').Append(Msg(lang, "cell-spot", spot.Index, spot.X, spot.Z, spot.Chunk.X, spot.Chunk.Z,
                spot.X - sx, spot.Z - sz, Msg(lang, "status-" + placement.StatusOf(metal, cell, spot.Index).ToString().ToLowerInvariant())));
        sb.Append('\n');
        if (state.None)
            sb.Append(Msg(lang, "cell-none", metal));
        else
        {
            var active = placement.SpotsOf(metal, cell)[state.Active];
            sb.Append(Msg(lang, "cell-rule", metal, active.Chunk.X, active.Chunk.Z));
        }
        return sb.ToString();
    }

    private TextCommandResult OnHere(TextCommandCallingArgs args)
    {
        string lang = args.LanguageCode ?? Lang.DefaultLocale;
        if (_system.Placement is not { } placement)
            return TextCommandResult.Success(Msg(lang, "off"));
        var pos = args.Caller.Entity.Pos;
        int x = (int)Math.Floor(pos.X), z = (int)Math.Floor(pos.Z);
        var (sx, sz) = Spawn();
        var sb = new StringBuilder(Msg(lang, "here-head", x - sx, z - sz));
        var rows = new System.Text.Json.Nodes.JsonArray();
        foreach (var metal in placement.Managed)
        {
            var cell = placement.Cells.CellOf(metal, x, z);
            var state = placement.StateOf(metal, cell);
            sb.Append('\n');
            if (state.None)
            {
                sb.Append(Msg(lang, "here-none", metal));
                continue;
            }
            var spot = placement.SpotsOf(metal, cell)[state.Active];
            int dx = spot.X - x, dz = spot.Z - z;
            rows.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["metal"] = metal, ["cell"] = new System.Text.Json.Nodes.JsonObject { ["x"] = cell.X, ["z"] = cell.Z }, ["spot"] = spot.Index,
                ["x"] = spot.X, ["z"] = spot.Z, ["distance"] = (int)Math.Round(Math.Sqrt((double)dx * dx + (double)dz * dz)), ["bearing"] = Compass(dx, dz),
                ["status"] = placement.StatusOf(metal, cell, spot.Index).ToString().ToLowerInvariant(),
            });
            sb.Append(Msg(lang, "here-line", metal, spot.Index, spot.X - sx, spot.Z - sz,
                (int)Math.Round(Math.Sqrt((double)dx * dx + (double)dz * dz)), Compass(dx, dz),
                Msg(lang, "status-" + placement.StatusOf(metal, cell, spot.Index).ToString().ToLowerInvariant())));
        }
        Admin.AdminCommands.Attach(args, new System.Text.Json.Nodes.JsonObject { ["x"] = x, ["z"] = z, ["deposits"] = rows });
        return TextCommandResult.Success(sb.ToString());
    }

    private (int X, int Z) Spawn()
    {
        var spawn = _api.World.DefaultSpawnPosition;
        return spawn == null ? (0, 0) : ((int)spawn.X, (int)spawn.Z);
    }

    // North is -Z in the game.
    private static string Compass(int dx, int dz)
    {
        if (dx == 0 && dz == 0) return "-";
        string[] names = ["E", "SE", "S", "SW", "W", "NW", "N", "NE"];
        double angle = Math.Atan2(dz, dx) * 180 / Math.PI;
        return names[(int)Math.Round((angle + 360) % 360 / 45) % 8];
    }

    private static string Msg(string lang, string key, params object[] values) =>
        Lang.GetL(lang, "seraphhorizons:ore-" + key, values);
}
