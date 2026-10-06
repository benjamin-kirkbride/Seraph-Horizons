using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// The deposit registry's and the maps' admin commands (#443, #444), under <c>/sh ore</c>
/// (<c>controlserver</c>), hung on the tree <see cref="OreCommands"/> makes (the game's
/// <c>BeginSubCommand</c> returns an existing subcommand):
/// <list type="bullet">
/// <item><c>/sh ore list [metal] [radius] [--unsold|--sold|--soldout]</c>: deposits near you (or the
/// spawn), nearest first.</item>
/// <item><c>/sh ore gravel [radius]</c>: gravel fields near you.</item>
/// <item><c>/sh ore verify &lt;id&gt;</c>: generates and measures a deposit; answers when done.</item>
/// <item><c>/sh ore tp &lt;id&gt;</c>: to a deposit (generated first if need be).</item>
/// <item><c>/sh ore registry mark &lt;id&gt; sold|soldout</c>, <c>/sh ore registry reset &lt;id&gt;</c>.</item>
/// <item><c>/sh ore givemap &lt;player&gt; &lt;metal|gravel&gt; &lt;precision 1-3&gt;</c>: the nearest unsold
/// deposit to the player, verified, sold to them as a map.</item>
/// </list>
/// Ids are <c>metal:cellX,cellZ</c> (<see cref="DepositKey"/>).
/// </summary>
internal sealed class DepositCommands
{
    public const int DefaultRadius = 6000;
    public const int MaxRadius = 50000;

    private readonly ICoreServerAPI _api;
    private readonly OreSystem _system;

    public DepositCommands(ICoreServerAPI api, OreSystem system)
    {
        _api = api;
        _system = system;
    }

    public void Register()
    {
        var parsers = _api.ChatCommands.Parsers;
        var ore = _api.ChatCommands.GetOrCreate(OreCommands.Root).BeginSubCommand("ore");
        ore.BeginSubCommand("list")
                .WithDescription($"Ore deposits within a radius (default {DefaultRadius}) of you: [metal] [radius] [--unsold|--sold|--soldout]")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OptionalAll("filters"))
                .HandleWith(OnList)
            .EndSubCommand()
            .BeginSubCommand("gravel")
                .WithDescription($"Placer gravel fields within a radius (default {DefaultRadius}) of you")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OptionalInt("radius", DefaultRadius))
                .HandleWith(OnGravel)
            .EndSubCommand()
            .BeginSubCommand("verify")
                .WithDescription("Generate a deposit's chunks and measure what ore is left (id as /sh ore list shows it)")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.Word("id"))
                .HandleWith(OnVerify)
            .EndSubCommand()
            .BeginSubCommand("tp")
                .WithDescription("Teleport to a deposit or gravel field (id as listed)")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(parsers.Word("id"))
                .HandleWith(OnTeleport)
            .EndSubCommand()
            .BeginSubCommand("registry")
                .WithDescription("Set a deposit's sale state by hand")
                .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("mark")
                    .WithDescription("Mark a deposit sold (to no one) or sold out")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Word("id"), parsers.WordRange("state", "sold", "soldout"))
                    .HandleWith(OnMark)
                .EndSubCommand()
                .BeginSubCommand("reset")
                    .WithDescription("Make a deposit unsold again")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Word("id"))
                    .HandleWith(OnReset)
                .EndSubCommand()
            .EndSubCommand()
            .BeginSubCommand("givemap")
                .WithDescription("Give a player a map to the nearest unsold deposit of a metal (or gravel field), precision 1 (±400 m) to 3 (exact); marks it sold to them")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OnlinePlayer("player"), parsers.Word("metal"), parsers.IntRange("precision", MapPrecision.Rough, MapPrecision.Exact))
                .HandleWith(OnGiveMap)
            .EndSubCommand();
    }

    private static string Msg(string lang, string key, params object[] values) =>
        Lang.GetL(lang, "seraphhorizons:ore-" + key, values);

    private static string LangOf(TextCommandCallingArgs args) => args.LanguageCode ?? Lang.DefaultLocale;

    private (int X, int Z) Origin(TextCommandCallingArgs args)
    {
        var pos = args.Caller.Entity?.Pos;
        if (pos != null) return ((int)Math.Floor(pos.X), (int)Math.Floor(pos.Z));
        var (sx, sz) = Spawn();
        return (sx, sz);
    }

    private (int X, int Z) Spawn()
    {
        var spawn = _api.World.DefaultSpawnPosition;
        return spawn == null ? (0, 0) : ((int)spawn.X, (int)spawn.Z);
    }

    private TextCommandResult OnList(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        if (_system.Deposits is not { HasOre: true } deposits) return TextCommandResult.Error(Msg(lang, "off"));
        string? metal = null;
        int radius = DefaultRadius;
        DepositState? only = null;
        foreach (var token in ((string?)args[0] ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.ToLowerInvariant();
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r)) radius = r;
            else if (t == "--unsold") only = DepositState.Unsold;
            else if (t == "--sold") only = DepositState.Sold;
            else if (t == "--soldout") only = DepositState.SoldOut;
            else if (deposits.Metals.Contains(t)) metal = t;
            else return TextCommandResult.Error(Msg(lang, "list-badarg", token, string.Join(", ", deposits.Metals)));
        }
        if (radius < 1 || radius > MaxRadius) return TextCommandResult.Error(Msg(lang, "list-radius", MaxRadius));
        var (x, z) = Origin(args);
        var rows = deposits.Candidates(x, z, radius, metal).Where(c => only == null || c.Record.State == only).ToList();
        var (sx, sz) = Spawn();
        var sb = new StringBuilder(Msg(lang, "list-head", rows.Count, radius, x - sx, z - sz));
        foreach (var c in rows) sb.Append('\n').Append(Line(lang, c, x, z));
        Admin.AdminCommands.Attach(args, new System.Text.Json.Nodes.JsonObject
        {
            ["radius"] = radius, ["metal"] = metal, ["filter"] = only?.ToString().ToLowerInvariant(),
            ["deposits"] = SeraphHorizons.Mod.Core.AdminOutput.Rows(rows, c => OreJson.Deposit(c, x, z)),
        });
        return TextCommandResult.Success(sb.ToString());
    }

    private TextCommandResult OnGravel(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        if (_system.Deposits is not { HasGravel: true } deposits) return TextCommandResult.Error(Msg(lang, "gravel-off"));
        int radius = (int)args[0];
        if (radius < 1 || radius > MaxRadius) return TextCommandResult.Error(Msg(lang, "list-radius", MaxRadius));
        var (x, z) = Origin(args);
        var rows = deposits.GravelFields(x, z, radius);
        var (sx, sz) = Spawn();
        var sb = new StringBuilder(Msg(lang, "gravel-head", rows.Count, radius, x - sx, z - sz));
        foreach (var c in rows) sb.Append('\n').Append(Line(lang, c, x, z));
        Admin.AdminCommands.Attach(args, new System.Text.Json.Nodes.JsonObject
        {
            ["radius"] = radius,
            ["fields"] = SeraphHorizons.Mod.Core.AdminOutput.Rows(rows, c =>
            {
                var row = OreJson.Deposit(c, x, z);
                if (_system.Deposits?.FieldOf(c.Key) is { } f) { row["rock"] = f.Rock; row["blocks"] = f.Blocks; }
                return row;
            }),
        });
        return TextCommandResult.Success(sb.ToString());
    }

    /// <summary>One deposit: id, spot, position (absolute and from spawn), distance and direction,
    /// generated or not, sale state, last measurement or field.</summary>
    internal string Line(string lang, DepositCandidate c, int fromX, int fromZ)
    {
        var (sx, sz) = Spawn();
        var r = c.Record;
        string state = r.State switch
        {
            DepositState.Sold => Msg(lang, "state-sold", r.SoldToName ?? "-", r.SoldAtDays is { } d ? Math.Floor(d).ToString(CultureInfo.InvariantCulture) : "?"),
            DepositState.SoldOut => Msg(lang, "state-soldout"),
            _ => Msg(lang, "state-unsold"),
        };
        string size = c.Key.IsGravel
            ? (_system.Deposits?.FieldOf(c.Key) is { } f ? Msg(lang, "line-field", f.Blocks, f.Rock) : "")
            : r.Ingots is { } ingots ? Msg(lang, "line-measured", Math.Round(ingots), Msg(lang, "size-" + r.Tier.ToString()!.ToLowerInvariant())) : "";
        return Msg(lang, "line", c.Key.Id, c.Spot, c.X, c.Z, c.X - sx, c.Z - sz, (int)Math.Round(Dist(c.X, c.Z, fromX, fromZ)),
            Compass(c.X - fromX, c.Z - fromZ), Msg(lang, c.Generated ? "line-generated" : "line-ungenerated"), state, size);
    }

    private TextCommandResult OnVerify(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        if (_system.Deposits is not { } deposits) return TextCommandResult.Error(Msg(lang, "off"));
        if (!DepositKey.TryParse((string)args[0], out var key)) return TextCommandResult.Error(Msg(lang, "badid", (string)args[0]));
        if (deposits.Candidate(key) == null) return TextCommandResult.Error(Msg(lang, "nodeposit", key.Id));
        var caller = args.Caller;
        bool answered = false;
        string? immediate = null;
        var started = System.Diagnostics.Stopwatch.StartNew();
        deposits.Verify(key, result =>
        {
            string text = Describe(lang, result) + $" ({started.Elapsed.TotalSeconds:0.0} s)";
            if (!answered)
            {
                immediate = text;
                var data = OreJson.Verify(result);
                data["seconds"] = Math.Round(started.Elapsed.TotalSeconds, 2);
                Admin.AdminCommands.Attach(args, new System.Text.Json.Nodes.JsonObject { ["verify"] = data });
            }
            else Tell(caller, text);
        });
        answered = true;
        return TextCommandResult.Success(immediate ?? Msg(lang, "verify-started", key.Id));
    }

    internal string Describe(string lang, VerifyResult result) => result.Status switch
    {
        VerifyStatus.Measured => Msg(lang, "verify-measured", result.Candidate!.Key.Id, result.OreBlocks, Math.Round(result.Ingots),
            Msg(lang, "size-" + result.Tier.ToString()!.ToLowerInvariant()), result.Candidate.X, result.Candidate.Y, result.Candidate.Z)
            + (result.WorkedOut ? " " + Msg(lang, "verify-workedout") : ""),
        VerifyStatus.Field => result.Field is { } f
            ? Msg(lang, "verify-field", result.Candidate!.Key.Id, f.Blocks, f.Rock, f.X, f.Y, f.Z)
            : Msg(lang, "nodeposit", result.Candidate?.Key.Id ?? "?"),
        _ => Msg(lang, "verify-none", result.Candidate?.Key.Id ?? "?"),
    };

    private TextCommandResult OnTeleport(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        if (_system.Deposits is not { } deposits) return TextCommandResult.Error(Msg(lang, "off"));
        if (!DepositKey.TryParse((string)args[0], out var key)) return TextCommandResult.Error(Msg(lang, "badid", (string)args[0]));
        if (deposits.Candidate(key) is not { } c) return TextCommandResult.Error(Msg(lang, "nodeposit", key.Id));
        var entity = args.Caller.Entity;
        if (c.Generated)
        {
            Go(entity, c.X, c.Z);
            return TextCommandResult.Success(Msg(lang, "tp-done", key.Id));
        }
        deposits.Verify(key, result =>
        {
            if (result.Candidate is { } now) Go(entity, now.X, now.Z);
        });
        return TextCommandResult.Success(Msg(lang, "tp-generating", key.Id));
    }

    private void Go(Entity entity, int x, int z)
    {
        int y = _api.World.BlockAccessor.GetTerrainMapheightAt(new Vintagestory.API.MathTools.BlockPos(x, 0, z));
        if (y <= 0) y = _api.World.SeaLevel + 40;
        entity.TeleportToDouble(x + 0.5, y + 2, z + 0.5);
    }

    private TextCommandResult OnMark(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        if (_system.Deposits is not { } deposits) return TextCommandResult.Error(Msg(lang, "off"));
        if (!DepositKey.TryParse((string)args[0], out var key)) return TextCommandResult.Error(Msg(lang, "badid", (string)args[0]));
        if ((string)args[1] == "soldout") deposits.Registry.MarkSoldOut(key);
        else if (!deposits.Registry.MarkSold(key, null, null, _api.World.Calendar.TotalDays))
            return TextCommandResult.Error(Msg(lang, "mark-notunsold", key.Id));
        return TextCommandResult.Success(Msg(lang, "mark-done", key.Id, (string)args[1]));
    }

    private TextCommandResult OnReset(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        if (_system.Deposits is not { } deposits) return TextCommandResult.Error(Msg(lang, "off"));
        if (!DepositKey.TryParse((string)args[0], out var key)) return TextCommandResult.Error(Msg(lang, "badid", (string)args[0]));
        deposits.Registry.Reset(key);
        return TextCommandResult.Success(Msg(lang, "reset-done", key.Id));
    }

    private TextCommandResult OnGiveMap(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        if (_system.Deposits is not { } deposits || _system.Maps is not { } maps) return TextCommandResult.Error(Msg(lang, "off"));
        if (args[0] is not IServerPlayer player || player.Entity == null)
            return TextCommandResult.Error(Msg(lang, "givemap-noplayer"));
        string metal = ((string)args[1]).ToLowerInvariant();
        int precision = (int)args[2];
        bool gravel = metal == PlacerCells.Kind;
        if (!gravel && !deposits.Metals.Contains(metal))
            return TextCommandResult.Error(Msg(lang, "list-badarg", metal, string.Join(", ", deposits.Metals.Append(PlacerCells.Kind))));
        int x = (int)player.Entity.Pos.X, z = (int)player.Entity.Pos.Z;
        var candidates = (gravel ? deposits.GravelFields(x, z, MaxRadius) : deposits.Candidates(x, z, MaxRadius, metal))
            .Where(c => c.Record.State == DepositState.Unsold)
            .Take(GiveMapTries)
            .ToList();
        if (candidates.Count == 0) return TextCommandResult.Error(Msg(lang, "givemap-none", metal));
        var caller = args.Caller;
        TryGive(lang, caller, player, candidates, 0, precision);
        return TextCommandResult.Success(Msg(lang, "givemap-started", candidates[0].Key.Id, player.PlayerName));
    }

    /// <summary>Unsold deposits tried in turn by <c>givemap</c> when the nearest turns out to have
    /// none, or to be worked out.</summary>
    private const int GiveMapTries = 3;

    private void TryGive(string lang, Caller caller, IServerPlayer player, List<DepositCandidate> candidates, int i, int precision)
    {
        if (i >= candidates.Count)
        {
            Tell(caller, Msg(lang, "givemap-none", candidates[0].Metal));
            return;
        }
        var deposits = _system.Deposits!;
        var key = candidates[i].Key;
        deposits.Verify(key, result =>
        {
            bool sellable = result.Status == VerifyStatus.Field || (result.Status == VerifyStatus.Measured && !result.WorkedOut);
            if (!sellable || _system.Maps!.Issue(player, key, precision) is not { } stack)
            {
                TryGive(lang, caller, player, candidates, i + 1, precision);
                return;
            }
            if (!player.InventoryManager.TryGiveItemstack(stack, true))
                _api.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
            Tell(caller, Msg(lang, "givemap-done", key.Id, player.PlayerName, Describe(lang, result)));
        });
    }

    private void Tell(Caller caller, string text)
    {
        if (caller.Player is IServerPlayer p) p.SendMessage(caller.FromChatGroupId, text, EnumChatType.CommandSuccess);
        else _api.Logger.Notification("[seraphhorizons] {0}", text);
    }

    private static double Dist(int x, int z, int fx, int fz) =>
        Math.Sqrt((double)(x - fx) * (x - fx) + (double)(z - fz) * (z - fz));

    // North is -Z in the game.
    private static string Compass(int dx, int dz)
    {
        if (dx == 0 && dz == 0) return "-";
        string[] names = ["E", "SE", "S", "SW", "W", "NW", "N", "NE"];
        double angle = Math.Atan2(dz, dx) * 180 / Math.PI;
        return names[(int)Math.Round((angle + 360) % 360 / 45) % 8];
    }
}
