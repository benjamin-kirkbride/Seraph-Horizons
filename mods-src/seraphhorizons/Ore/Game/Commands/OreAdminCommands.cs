using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using SeraphHorizons.Mod.Admin;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// The ore admin tools (#458) that <see cref="OreCommands"/> and <see cref="DepositCommands"/> don't
/// have, under the same <c>/sh ore</c> (<c>controlserver</c>; <c>--json</c> on all, from
/// <see cref="AdminCommands"/>). Positions are from the caller, or the spawn from the console.
/// <list type="bullet">
/// <item><c>cells [radius]</c>: every metal's (and gravel's) cells a circle reaches, each with its
/// active spot and how every spot before it stands.</item>
/// <item><c>count [radius]</c>: ore blocks by metal and grade in the loaded chunks around, in blocks
/// and ingots.</item>
/// <item><c>districts [radius]</c>: Interesting Ore Gen's hydrothermal district tiles (<see cref="OreDistricts"/>).</item>
/// <item><c>markers [radius]</c>, <c>markers clear</c>: deposits and gravel fields as waypoints.</item>
/// <item><c>survey &lt;chunks&gt; &lt;file&gt;</c>: the survey tool's scan of a square around the
/// caller, written as the tool writes it (<see cref="SurveyCounter"/>).</item>
/// <item><c>registry export|import &lt;file&gt;</c>, <c>registry clear &lt;metal|gravel|all&gt;</c>.</item>
/// <item><c>log on|off</c>: placement decisions and verifications to <c>Logs/seraphhorizons-ore.log</c>.</item>
/// <item><c>map on|off [radius]</c>: the ore overlay on the caller's world map.</item>
/// </list>
/// Files go to the server's <c>seraphhorizons-admin</c> folder (<see cref="AdminFiles"/>).
/// </summary>
internal sealed class OreAdminCommands
{
    public const int DefaultCellsRadius = 2500;
    public const int DefaultCountRadius = 48;
    public const int MaxCountRadius = 256;
    public const int DefaultDistrictRadius = 20000;
    public const int DefaultMarkerRadius = 6000;
    public const int DefaultMapRadius = 12000;
    public const int MaxSurveyChunks = 256;
    public const string MarkerPrefix = "[sh] ";

    private readonly ICoreServerAPI _api;
    private readonly OreSystem _system;
    private LiveSurvey? _survey;

    public OreAdminCommands(ICoreServerAPI api, OreSystem system)
    {
        _api = api;
        _system = system;
    }

    private static string Msg(string lang, string key, params object[] values) =>
        Lang.GetL(lang, "seraphhorizons:ore-" + key, values);

    private static string LangOf(TextCommandCallingArgs args) => args.LanguageCode ?? Lang.DefaultLocale;

    private static string F(double v, string format = "0.#") => v.ToString(format, CultureInfo.InvariantCulture);

    public void Register()
    {
        var parsers = _api.ChatCommands.Parsers;
        var ore = _api.ChatCommands.GetOrCreate(OreCommands.Root).BeginSubCommand("ore");
        if (string.IsNullOrEmpty(ore.Description)) ore.WithDescription("Ore cells, deposits and maps (admin).");
        ore.RequiresPrivilege(Privilege.controlserver);
        ore.BeginSubCommand("cells")
                .WithDescription($"Every metal's ore cells within a radius (default {DefaultCellsRadius}) of you: active spot and how the earlier ones failed")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OptionalInt("radius", DefaultCellsRadius))
                .HandleWith(OnCells)
            .EndSubCommand()
            .BeginSubCommand("count")
                .WithDescription($"Ore blocks by metal and grade in the loaded chunks within a radius (default {DefaultCountRadius}, at most {MaxCountRadius}) of you")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OptionalInt("radius", DefaultCountRadius))
                .HandleWith(OnCount)
            .EndSubCommand()
            .BeginSubCommand("districts")
                .WithDescription($"Hydrothermal district tiles within a radius (default {DefaultDistrictRadius}) of you")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OptionalInt("radius", DefaultDistrictRadius))
                .HandleWith(OnDistricts)
            .EndSubCommand()
            .BeginSubCommand("markers")
                .WithDescription($"Deposits and gravel fields within a radius (default {DefaultMarkerRadius}) as waypoints on your map; 'markers clear' removes them")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(parsers.OptionalWord("radius|clear"))
                .HandleWith(OnMarkers)
            .EndSubCommand()
            .BeginSubCommand("survey")
                .WithDescription($"The ore survey tool's scan of chunks x chunks columns around you (at most {MaxSurveyChunks}), written to <file> and <file>.cells.csv in the server's {AdminFiles.Folder} folder")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.Int("chunks"), parsers.Word("file"))
                .HandleWith(OnSurvey)
            .EndSubCommand()
            .BeginSubCommand("log")
                .WithDescription("Log every ore cell and placer field decision and every verification to Logs/" + AdminLogs.OreFile)
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.WordRange("state", "on", "off"))
                .HandleWith(OnLog)
            .EndSubCommand()
            .BeginSubCommand("map")
                .WithDescription($"The ore overlay on your world map (cells, deposits by metal and state, gravel fields, districts) within a radius (default {DefaultMapRadius})")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(parsers.WordRange("state", "on", "off"), parsers.OptionalInt("radius", DefaultMapRadius))
                .HandleWith(OnMap)
            .EndSubCommand()
            .BeginSubCommand("registry")
                .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("export")
                    .WithDescription($"Write the deposit registry to a file in the server's {AdminFiles.Folder} folder")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Word("file"))
                    .HandleWith(OnRegistryExport)
                .EndSubCommand()
                .BeginSubCommand("import")
                    .WithDescription("Replace the deposit registry with one an export wrote")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Word("file"))
                    .HandleWith(OnRegistryImport)
                .EndSubCommand()
                .BeginSubCommand("clear")
                    .WithDescription("Forget every record of a metal (or gravel, or all): unsold and unmeasured again")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Word("metal|gravel|all"))
                    .HandleWith(OnRegistryClear)
                .EndSubCommand()
            .EndSubCommand();
        AdminSystem.Of(_api)?.MapProviders.TryAdd("ore", (player, arg) =>
            Overlay((int)player.Entity.Pos.X, (int)player.Entity.Pos.Z, int.TryParse(arg, out int r) ? r : DefaultMapRadius));
    }

    /// <summary>The admin files folder, made if missing.</summary>
    internal static string FilesFolder()
    {
        string dir = Path.Combine(GamePaths.DataPath, AdminFiles.Folder);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private (int X, int Z) Origin(TextCommandCallingArgs args)
    {
        var pos = args.Caller.Entity?.Pos;
        if (pos != null) return ((int)Math.Floor(pos.X), (int)Math.Floor(pos.Z));
        return Spawn();
    }

    private (int X, int Z) Spawn()
    {
        var spawn = _api.World.DefaultSpawnPosition;
        return spawn == null ? (0, 0) : ((int)spawn.X, (int)spawn.Z);
    }

    // ---- cells ----

    private TextCommandResult OnCells(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        int radius = (int)args[0];
        if (radius < 0 || radius > DepositCommands.MaxRadius) return TextCommandResult.Error(Msg(lang, "list-radius", DepositCommands.MaxRadius));
        if (_system.Placement is null && _system.Placer is null) return TextCommandResult.Error(Msg(lang, "off"));
        var (x, z) = Origin(args);
        var (sx, sz) = Spawn();
        var output = new AdminOutput("ore cells");
        var rows = new JsonArray();
        if (_system.Placement is { } placement)
            foreach (var metal in placement.Managed)
                foreach (var cell in CellSearch.Within(placement.Cells.CellSize(metal), x, z, radius))
                {
                    var state = placement.StateOf(metal, cell);
                    var spots = placement.SpotsOf(metal, cell);
                    var statuses = spots.Select(s => placement.StatusOf(metal, cell, s.Index)).ToArray();
                    rows.Add(CellRow(metal, cell, state, spots, statuses, null));
                    output.Lines.Add(CellLine(lang, metal, cell, state, spots, statuses, sx, sz, null));
                }
        if (_system.Placer is { } placer)
            foreach (var cell in CellSearch.Within(placer.Cells.CellSize, x, z, radius))
            {
                var state = placer.StateOf(cell);
                var spots = placer.Cells.Spots(cell);
                var statuses = spots.Select(s => placer.StatusOf(cell, s.Index)).ToArray();
                var field = placer.FieldIn(cell);
                rows.Add(CellRow(PlacerCells.Kind, cell, state, spots, statuses, field));
                output.Lines.Add(CellLine(lang, PlacerCells.Kind, cell, state, spots, statuses, sx, sz, field));
            }
        output.Summary = Msg(lang, "admin-cells-head", rows.Count, radius, x - sx, z - sz);
        output.Data["radius"] = radius;
        output.Data["origin"] = new JsonObject { ["x"] = x, ["z"] = z };
        output.Data["cells"] = rows;
        return AdminCommands.Answer(args, output);
    }

    private static JsonObject CellRow(string kind, CellPos cell, CellState state, OreSpot[] spots, SpotStatus[] statuses, PlacedField? field)
    {
        var row = new JsonObject
        {
            ["kind"] = kind,
            ["cell"] = new JsonObject { ["x"] = cell.X, ["z"] = cell.Z },
            ["active"] = state.None ? null : state.Active,
            ["placed"] = state.Placed,
            ["none"] = state.None,
            ["spots"] = new JsonArray(spots.Select((s, i) => (JsonNode?)new JsonObject
            {
                ["index"] = s.Index,
                ["x"] = s.X,
                ["z"] = s.Z,
                ["status"] = statuses[i].ToString().ToLowerInvariant(),
            }).ToArray()),
        };
        if (field != null) row["field"] = new JsonObject { ["x"] = field.X, ["y"] = field.Y, ["z"] = field.Z, ["rock"] = field.Rock, ["blocks"] = field.Blocks };
        return row;
    }

    private string CellLine(string lang, string kind, CellPos cell, CellState state, OreSpot[] spots, SpotStatus[] statuses, int sx, int sz, PlacedField? field)
    {
        // The spots up to the active one (or all, with none left), then the active one's place.
        int upTo = state.None ? spots.Length - 1 : state.Active;
        var earlier = Enumerable.Range(0, upTo).Where(i => statuses[i] is SpotStatus.Failed or SpotStatus.Consumed)
            .Select(i => $"{i} {Msg(lang, "status-" + statuses[i].ToString().ToLowerInvariant())}").ToList();
        string head = state.None
            ? Msg(lang, "admin-cells-none", kind, cell.X, cell.Z)
            : Msg(lang, "admin-cells-line", kind, cell.X, cell.Z, state.Active, spots[state.Active].X - sx, spots[state.Active].Z - sz,
                Msg(lang, "status-" + statuses[state.Active].ToString().ToLowerInvariant()),
                state.Active == 0 ? Msg(lang, "admin-cells-primary") : Msg(lang, "admin-cells-fallback", state.Active));
        if (field != null) head += " " + Msg(lang, "line-field", field.Blocks, field.Rock);
        return earlier.Count == 0 ? head : head + "; " + string.Join(", ", earlier);
    }

    // ---- count ----

    private (string? Metal, string Grade, double Units)[]? _blockOre;

    /// <summary>Per block id, its metal and grade (null metal for anything else) and metal units.</summary>
    private (string? Metal, string Grade, double Units)[] BlockOre()
    {
        if (_blockOre != null) return _blockOre;
        var blocks = _api.World.Blocks;
        var table = new (string?, string, double)[blocks.Count];
        for (int id = 0; id < table.Length; id++)
        {
            var block = blocks[id];
            if (block?.Code is not { } code || OreTally.Classify(code.Path) is not { } c)
            {
                table[id] = (null, "", 0);
                continue;
            }
            double units = 0;
            foreach (var drop in block.Drops ?? [])
                if (drop?.ResolvedItemstack?.Collectible?.Attributes?["metalUnits"] is { Exists: true } mu)
                    units += drop.Quantity.avg * mu.AsDouble();
            table[id] = (c.Metal, c.Grade, units);
        }
        return _blockOre = table;
    }

    private TextCommandResult OnCount(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        int radius = (int)args[0];
        if (radius < 1 || radius > MaxCountRadius) return TextCommandResult.Error(Msg(lang, "admin-count-radius", MaxCountRadius));
        var (x, z) = Origin(args);
        var table = BlockOre();
        var tally = new OreTally();
        var blocks = new long[table.Length];
        const int size = OreCells.ChunkSize;
        int cx0 = OreCells.FloorDiv(x - radius, size), cx1 = OreCells.FloorDiv(x + radius, size);
        int cz0 = OreCells.FloorDiv(z - radius, size), cz1 = OreCells.FloorDiv(z + radius, size);
        int chunksHigh = _api.WorldManager.MapSizeY / size;
        int columns = 0, missing = 0;
        for (int cx = cx0; cx <= cx1; cx++)
            for (int cz = cz0; cz <= cz1; cz++)
            {
                if (_api.WorldManager.GetMapChunk(cx, cz) == null)
                {
                    missing++;
                    continue;
                }
                columns++;
                for (int cy = 0; cy < chunksHigh; cy++)
                {
                    if (_api.WorldManager.GetChunk(cx, cy, cz) is not { } chunk) continue;
                    chunk.Unpack();
                    var data = chunk.Data;
                    for (int i = 0; i < size * size * size; i++)
                    {
                        int id = data.GetBlockIdUnsafe(i);
                        if (id > 0 && id < table.Length && table[id].Metal != null) blocks[id]++;
                    }
                }
            }
        for (int id = 0; id < blocks.Length; id++)
            if (blocks[id] > 0) tally.Add(table[id].Metal!, table[id].Grade, blocks[id], blocks[id] * table[id].Units);
        var output = new AdminOutput("ore count")
        {
            Summary = Msg(lang, "admin-count-head", tally.Blocks, columns, missing, radius),
        };
        foreach (var m in tally.ByMetal())
        {
            output.Lines.Add(Msg(lang, "admin-count-metal", m.Metal, m.Blocks, F(m.Ingots)));
            foreach (var g in tally.Rows().Where(r => r.Metal == m.Metal))
                output.Lines.Add("  " + Msg(lang, "admin-count-grade", g.Grade, g.Blocks, F(g.Ingots)));
        }
        output.Data["radius"] = radius;
        output.Data["columns"] = columns;
        output.Data["unloadedColumns"] = missing;
        output.Data["blocks"] = tally.Blocks;
        output.Data["rows"] = AdminOutput.Rows(tally.Rows(), r => new JsonObject
        {
            ["metal"] = r.Metal, ["grade"] = r.Grade, ["blocks"] = r.Blocks, ["units"] = Math.Round(r.Units, 2), ["ingots"] = Math.Round(r.Ingots, 2),
        });
        return AdminCommands.Answer(args, output);
    }

    // ---- districts ----

    private TextCommandResult OnDistricts(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        int radius = (int)args[0];
        if (radius < 0 || radius > 4 * DepositCommands.MaxRadius) return TextCommandResult.Error(Msg(lang, "list-radius", 4 * DepositCommands.MaxRadius));
        if (OreDistricts.Unsupported(_api) is { } why) return TextCommandResult.Error(Msg(lang, "admin-districts-off", why));
        var (x, z) = Origin(args);
        var (sx, sz) = Spawn();
        var tiles = OreDistricts.Around(_api, x, z, radius);
        var output = new AdminOutput("ore districts")
        {
            Summary = Msg(lang, "admin-districts-head", tiles.Count(t => t.Rolled), tiles.Count, OreDistricts.TileSize(_api), radius, x - sx, z - sz),
        };
        foreach (var t in tiles)
        {
            int d = (int)Math.Round(Math.Sqrt((double)(t.CentreX - x) * (t.CentreX - x) + (double)(t.CentreZ - z) * (t.CentreZ - z)));
            output.Lines.Add(!t.Rolled ? Msg(lang, "admin-districts-empty", t.TileX, t.TileZ)
                : t.Generated ? Msg(lang, "admin-districts-built", t.TileX, t.TileZ, t.Config ?? "?", t.CentreX - sx, t.CentreZ - sz, d,
                    (int)Math.Round(t.Radius), t.MajorFaults, t.MinorFaults, t.Horsetails, t.OreZones)
                : Msg(lang, "admin-districts-rolled", t.TileX, t.TileZ, t.CentreX - sx, t.CentreZ - sz, d));
        }
        output.Data["tileSize"] = OreDistricts.TileSize(_api);
        output.Data["tiles"] = AdminOutput.Rows(tiles, t => new JsonObject
        {
            ["tile"] = new JsonObject { ["x"] = t.TileX, ["z"] = t.TileZ },
            ["district"] = t.Rolled,
            ["centre"] = t.Rolled ? new JsonObject { ["x"] = t.CentreX, ["z"] = t.CentreZ } : null,
            ["built"] = t.Generated,
            ["config"] = t.Config,
            ["radius"] = t.Generated ? Math.Round(t.Radius) : null,
            ["majorFaults"] = t.Generated ? t.MajorFaults : null,
            ["minorFaults"] = t.Generated ? t.MinorFaults : null,
            ["horsetails"] = t.Generated ? t.Horsetails : null,
            ["oreZones"] = t.Generated ? t.OreZones : null,
        });
        return AdminCommands.Answer(args, output);
    }

    // ---- markers ----

    private static readonly System.Reflection.MethodInfo? ResendWaypoints = AccessTools.Method(typeof(WaypointMapLayer), "ResendWaypoints");

    private TextCommandResult OnMarkers(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        if (args.Caller.Player is not IServerPlayer player) return TextCommandResult.Error(Msg(lang, "givemap-noplayer"));
        var layer = _api.ModLoader.GetModSystem<WorldMapManager>()?.MapLayers.OfType<WaypointMapLayer>().FirstOrDefault();
        if (layer == null) return TextCommandResult.Error(Msg(lang, "admin-markers-nomap"));
        string word = (args[0] as string ?? "").Trim().ToLowerInvariant();
        var output = new AdminOutput("ore markers");
        if (word == "clear")
        {
            int removed = layer.Waypoints.RemoveAll(w => w.OwningPlayerUid == player.PlayerUID && w.Title?.StartsWith(MarkerPrefix, StringComparison.Ordinal) == true);
            ResendWaypoints?.Invoke(layer, [player]);
            output.Summary = Msg(lang, "admin-markers-cleared", removed);
            output.Data["removed"] = removed;
            return AdminCommands.Answer(args, output);
        }
        int radius = DefaultMarkerRadius;
        if (word.Length > 0 && !int.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out radius))
            return TextCommandResult.Error(Msg(lang, "admin-markers-usage"));
        if (radius < 1 || radius > DepositCommands.MaxRadius) return TextCommandResult.Error(Msg(lang, "list-radius", DepositCommands.MaxRadius));
        if (_system.Deposits is not { } deposits) return TextCommandResult.Error(Msg(lang, "off"));
        var (x, z) = Origin(args);
        var candidates = deposits.Candidates(x, z, radius).Concat(deposits.GravelFields(x, z, radius)).ToList();
        int added = 0;
        foreach (var c in candidates)
        {
            var pos = new Vec3d(c.X + 0.5, c.Y > 0 ? c.Y + 0.5 : _api.World.SeaLevel, c.Z + 0.5);
            string title = MarkerPrefix + c.Key.Id + " " + c.Record.State.ToString().ToLowerInvariant();
            if (layer.Waypoints.Any(w => w.OwningPlayerUid == player.PlayerUID && w.Title == title)) continue;
            layer.Waypoints.Add(new Waypoint
            {
                Color = c.Key.IsGravel ? ColorUtil.ColorFromRgba(220, 190, 90, 255) : MapOverlay.ColorFor(c.Metal),
                Icon = c.Key.IsGravel ? "rocks" : "pick",
                Pinned = false,
                Position = pos,
                OwningPlayerUid = player.PlayerUID,
                Title = title,
                Guid = Guid.NewGuid().ToString(),
            });
            added++;
        }
        ResendWaypoints?.Invoke(layer, [player]);
        output.Summary = Msg(lang, "admin-markers-added", added, candidates.Count, radius);
        output.Data["added"] = added;
        output.Data["deposits"] = AdminOutput.Rows(candidates, c => new JsonObject { ["id"] = c.Key.Id, ["x"] = c.X, ["z"] = c.Z, ["state"] = c.Record.State.ToString().ToLowerInvariant() });
        return AdminCommands.Answer(args, output);
    }

    // ---- survey ----

    private TextCommandResult OnSurvey(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        int chunks = (int)args[0];
        if (chunks < 1 || chunks > MaxSurveyChunks) return TextCommandResult.Error(Msg(lang, "admin-survey-size", MaxSurveyChunks));
        if (AdminFiles.Resolve(FilesFolder(), (string)args[1]) is not { } path) return TextCommandResult.Error(Msg(lang, "admin-badfile", (string)args[1]));
        if (_survey is { Done: false }) return TextCommandResult.Error(Msg(lang, "admin-survey-busy", _survey.Path));
        var (x, z) = Origin(args);
        int originX = OreCells.FloorDiv(x, OreCells.ChunkSize) - chunks / 2, originZ = OreCells.FloorDiv(z, OreCells.ChunkSize) - chunks / 2;
        var caller = args.Caller;
        _survey = new LiveSurvey(_api, path, originX, originZ, chunks, text => Tell(caller, text), lang);
        _survey.Start();
        var output = new AdminOutput("ore survey") { Summary = Msg(lang, "admin-survey-started", chunks, chunks, originX, originZ, path) };
        output.Data["file"] = path;
        output.Data["cellsFile"] = path + ".cells.csv";
        output.Data["origin"] = new JsonObject { ["chunkX"] = originX, ["chunkZ"] = originZ };
        output.Data["chunks"] = chunks;
        return AdminCommands.Answer(args, output);
    }

    private void Tell(Caller caller, string text)
    {
        if (caller.Player is IServerPlayer p) p.SendMessage(caller.FromChatGroupId, text, EnumChatType.CommandSuccess);
        else _api.Logger.Notification("[seraphhorizons] {0}", text);
    }

    // ---- registry ----

    private TextCommandResult OnRegistryExport(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        if (_system.Deposits is not { } deposits) return TextCommandResult.Error(Msg(lang, "off"));
        if (AdminFiles.Resolve(FilesFolder(), (string)args[0]) is not { } path) return TextCommandResult.Error(Msg(lang, "admin-badfile", (string)args[0]));
        File.WriteAllText(path, deposits.Registry.Serialize());
        int n = deposits.Registry.All().Count;
        var output = new AdminOutput("ore registry export") { Summary = Msg(lang, "admin-registry-exported", n, path) };
        output.Data["file"] = path;
        output.Data["records"] = n;
        return AdminCommands.Answer(args, output);
    }

    private TextCommandResult OnRegistryImport(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        if (_system.Deposits is not { } deposits) return TextCommandResult.Error(Msg(lang, "off"));
        if (AdminFiles.Resolve(FilesFolder(), (string)args[0]) is not { } path) return TextCommandResult.Error(Msg(lang, "admin-badfile", (string)args[0]));
        if (!File.Exists(path)) return TextCommandResult.Error(Msg(lang, "admin-nofile", path));
        DepositRegistry read;
        try
        {
            string text = File.ReadAllText(path);
            // An admin state export (/sh trade export) holds the registry as its "deposits" section.
            if (JsonNode.Parse(text) is JsonObject o && o["sections"]?["deposits"] is { } section) text = section.ToJsonString();
            read = DepositRegistry.Parse(text);
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            return TextCommandResult.Error(Msg(lang, "admin-badjson", path, e.Message));
        }
        deposits.Registry.ReplaceWith(read);
        int n = read.All().Count;
        var output = new AdminOutput("ore registry import") { Summary = Msg(lang, "admin-registry-imported", n, path) };
        output.Data["file"] = path;
        output.Data["records"] = n;
        return AdminCommands.Answer(args, output);
    }

    private TextCommandResult OnRegistryClear(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        if (_system.Deposits is not { } deposits) return TextCommandResult.Error(Msg(lang, "off"));
        string kind = ((string)args[0]).ToLowerInvariant();
        if (kind != "all" && kind != PlacerCells.Kind && !deposits.Metals.Contains(kind))
            return TextCommandResult.Error(Msg(lang, "list-badarg", kind, string.Join(", ", deposits.Metals.Append(PlacerCells.Kind).Append("all"))));
        int removed = deposits.Registry.RemoveAll(key => kind == "all" || key.Kind == kind);
        var output = new AdminOutput("ore registry clear") { Summary = Msg(lang, "admin-registry-cleared", removed, kind) };
        output.Data["removed"] = removed;
        return AdminCommands.Answer(args, output);
    }

    // ---- log ----

    private TextCommandResult OnLog(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        if (AdminLogs.Ore is not { } log) return TextCommandResult.Error(Msg(lang, "admin-tools-off"));
        bool on = (string)args[0] == "on";
        log.Set(null, on);
        var output = new AdminOutput("ore log") { Summary = Msg(lang, on ? "admin-log-on" : "admin-log-off", log.Path) };
        output.Data["file"] = log.Path;
        output.Data["channels"] = new JsonArray(log.On.Select(c => (JsonNode?)c).ToArray());
        return AdminCommands.Answer(args, output);
    }

    // ---- map ----

    private TextCommandResult OnMap(TextCommandCallingArgs args)
    {
        string lang = LangOf(args);
        if (AdminSystem.Of(_api)?.Map is not { } map) return TextCommandResult.Error(Msg(lang, "admin-tools-off"));
        if (args.Caller.Player is not IServerPlayer player) return TextCommandResult.Error(Msg(lang, "givemap-noplayer"));
        bool on = (string)args[0] == "on";
        int radius = (int)args[1];
        if (radius < 1 || radius > DepositCommands.MaxRadius) return TextCommandResult.Error(Msg(lang, "list-radius", DepositCommands.MaxRadius));
        int drawn = map.Set(player, "ore", on, radius.ToString(CultureInfo.InvariantCulture));
        var output = new AdminOutput("ore map") { Summary = on ? Msg(lang, "admin-map-on", drawn, radius) : Msg(lang, "admin-map-off") };
        output.Data["on"] = on;
        output.Data["drawn"] = drawn;
        return AdminCommands.Answer(args, output);
    }

    /// <summary>The ore overlay around (x, z): cell grid, deposit spots coloured by metal (smaller and
    /// paler once sold, a grey dot once sold out), gravel fields, district rings.</summary>
    internal MapOverlay Overlay(int x, int z, int radius)
    {
        var overlay = new MapOverlay { Legend = "ore: cells, deposits (by metal; small = sold, grey = sold out), gravel, districts" };
        if (_system.Placement is { } placement)
            foreach (int size in placement.Managed.Select(m => placement.Cells.CellSize(m)).Distinct())
                foreach (var cell in CellSearch.Within(size, x, z, radius))
                    overlay.Rects.Add(new OverlayRect(cell.X * size, cell.Z * size, cell.X * size + size, cell.Z * size + size,
                        MapOverlay.Argb(200, 200, 200, 160), $"ore cell {cell.X},{cell.Z} ({size} m)"));
        if (_system.Deposits is { } deposits)
        {
            foreach (var c in deposits.Candidates(x, z, radius))
            {
                var (color, size) = c.Record.State switch
                {
                    DepositState.Sold => (MapOverlay.ColorFor(c.Metal, 200), 6),
                    DepositState.SoldOut => (MapOverlay.Argb(120, 120, 120), 4),
                    _ => (MapOverlay.ColorFor(c.Metal), 10),
                };
                overlay.Marks.Add(new OverlayMark(c.X, c.Z, color,
                    $"{c.Key.Id} spot {c.Spot}, {c.Record.State.ToString().ToLowerInvariant()}{(c.Record.Ingots is { } i ? $", {i:0} ingots" : "")}{(c.Generated ? "" : ", not generated")}", size));
            }
            foreach (var c in deposits.GravelFields(x, z, radius))
                overlay.Marks.Add(new OverlayMark(c.X, c.Z, MapOverlay.Argb(230, 200, 90),
                    $"{c.Key.Id}{(deposits.FieldOf(c.Key) is { } f ? $", {f.Blocks} blocks of {f.Rock}" : ", not generated")}", 6));
        }
        if (OreDistricts.Unsupported(_api) is null)
            foreach (var t in OreDistricts.Around(_api, x, z, radius).Where(t => t.Rolled))
                overlay.Rings.Add(new OverlayRing(t.CentreX, t.CentreZ, t.Generated ? (int)t.Radius : 2000,
                    MapOverlay.Argb(255, 120, 60, t.Generated ? 255 : 140), $"district {t.TileX},{t.TileZ} {t.Config ?? "(not built yet)"}"));
        return overlay;
    }
}

/// <summary>
/// <c>/sh ore survey</c>'s scan, a tile of 8 x 8 chunk columns at a time on the main thread, as the
/// survey tool does it: load (generating if need be), count, and unload the columns nobody had
/// loaded before, so memory stays flat whatever the size.
/// </summary>
internal sealed class LiveSurvey
{
    private const int Tile = 8;

    private readonly ICoreServerAPI _api;
    private readonly int _originX, _originZ, _size, _tiles;
    private readonly Action<string> _tell;
    private readonly string _lang;
    private readonly Stopwatch _clock = new();
    private SurveyCounter? _counter;
    private long _listener;
    private int _next;
    private bool _busy;

    public LiveSurvey(ICoreServerAPI api, string path, int originX, int originZ, int size, Action<string> tell, string lang)
    {
        _api = api;
        Path = path;
        _originX = originX;
        _originZ = originZ;
        _size = size;
        _tiles = (size + Tile - 1) / Tile;
        _tell = tell;
        _lang = lang;
    }

    public string Path { get; }

    public bool Done { get; private set; }

    public void Start()
    {
        var blocks = _api.World.Blocks;
        _counter = new SurveyCounter(blocks.Select(b => b?.Code != null && SurveyCounter.Recorded(b.Code.Path, b.Class)).ToArray(), _originX, _originZ);
        _clock.Start();
        _listener = _api.Event.RegisterGameTickListener(_ => Step(), 20);
    }

    private void Step()
    {
        if (_busy || Done) return;
        if (_next >= _tiles * _tiles)
        {
            Finish();
            return;
        }
        _busy = true;
        int tx = _next % _tiles, tz = _next / _tiles;
        int x1 = _originX + tx * Tile, z1 = _originZ + tz * Tile;
        int x2 = Math.Min(x1 + Tile, _originX + _size) - 1, z2 = Math.Min(z1 + Tile, _originZ + _size) - 1;
        var fresh = new List<(int, int)>();
        for (int cx = x1; cx <= x2; cx++)
            for (int cz = z1; cz <= z2; cz++)
                if (_api.WorldManager.GetMapChunk(cx, cz) == null) fresh.Add((cx, cz));
        _api.WorldManager.LoadChunkColumnPriority(x1, z1, x2, z2, new ChunkLoadOptions
        {
            OnLoaded = () =>
            {
                try
                {
                    Count(x1, z1, x2, z2);
                }
                catch (Exception e)
                {
                    _api.Logger.Error("[seraphhorizons] Ore survey: {0}", e);
                }
                foreach (var (cx, cz) in fresh) _api.WorldManager.UnloadChunkColumn(cx, cz);
                _next++;
                _busy = false;
            },
        });
    }

    private void Count(int x1, int z1, int x2, int z2)
    {
        int chunksY = _api.WorldManager.MapSizeY / 32;
        for (int cx = x1; cx <= x2; cx++)
            for (int cz = z1; cz <= z2; cz++)
            {
                var heights = _api.WorldManager.GetMapChunk(cx, cz)?.WorldGenTerrainHeightMap;
                if (heights == null) continue;
                for (int cy = 0; cy < chunksY; cy++)
                {
                    if (_api.WorldManager.GetChunk(cx, cy, cz) is not { } chunk) continue;
                    chunk.Unpack();
                    var data = chunk.Data;
                    _counter!.Count(cx, cy, cz, i => data[i], heights);
                }
            }
    }

    private void Finish()
    {
        Done = true;
        _api.Event.UnregisterGameTickListener(_listener);
        var blocks = _api.World.Blocks;
        var doc = new
        {
            seed = _api.World.Seed,
            seaLevel = _api.World.SeaLevel,
            mapSizeY = _api.WorldManager.MapSizeY,
            chunkColumns = _size * _size,
            areaKm2 = _size * 32.0 * _size * 32.0 / 1e6,
            seconds = _clock.Elapsed.TotalSeconds,
            origin = new { chunkX = _originX, chunkZ = _originZ },
            blocks = _counter!.Blocks(id => (blocks[id].Code.ToString(), blocks[id].Class)),
        };
        try
        {
            File.WriteAllText(Path, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path + ".cells.csv", _counter.CellsCsv(id => blocks[id].Code.ToString()));
            _tell(Lang.GetL(_lang, "seraphhorizons:ore-admin-survey-done", Path, F(_clock.Elapsed.TotalSeconds), _counter.Cells));
        }
        catch (IOException e)
        {
            _tell(Lang.GetL(_lang, "seraphhorizons:ore-admin-survey-failed", Path, e.Message));
        }
    }

    private static string F(double v) => v.ToString("0", CultureInfo.InvariantCulture);
}

/// <summary>The JSON rows the ore commands share (docs/admin-tools.md).</summary>
internal static class OreJson
{
    public static JsonObject Deposit(DepositCandidate c, int fromX, int fromZ)
    {
        var r = c.Record;
        return new JsonObject
        {
            ["id"] = c.Key.Id,
            ["kind"] = c.Key.Kind,
            ["spot"] = c.Spot,
            ["x"] = c.X,
            ["y"] = c.Y,
            ["z"] = c.Z,
            ["distance"] = (int)Math.Round(Math.Sqrt((double)(c.X - fromX) * (c.X - fromX) + (double)(c.Z - fromZ) * (c.Z - fromZ))),
            ["generated"] = c.Generated,
            ["state"] = r.State.ToString().ToLowerInvariant(),
            ["soldTo"] = r.SoldToName,
            ["soldAtDays"] = r.SoldAtDays,
            ["ingots"] = r.Ingots,
            ["tier"] = r.Tier?.ToString().ToLowerInvariant(),
        };
    }

    public static JsonObject Verify(VerifyResult result) => new()
    {
        ["status"] = result.Status.ToString().ToLowerInvariant(),
        ["id"] = result.Candidate?.Key.Id,
        ["oreBlocks"] = result.OreBlocks,
        ["ingots"] = Math.Round(result.Ingots, 1),
        ["tier"] = result.Tier?.ToString().ToLowerInvariant(),
        ["workedOut"] = result.WorkedOut,
        ["x"] = result.Candidate?.X,
        ["y"] = result.Candidate?.Y,
        ["z"] = result.Candidate?.Z,
    };
}
