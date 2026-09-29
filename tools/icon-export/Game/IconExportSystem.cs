using SeraphHorizons.IconExport.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.IconExport.Game;

/// <summary>
/// The <c>.seraphicons</c> client command: renders item and block icons for the recipe browser
/// into <c>&lt;data path&gt;/seraph-icons/</c>, one file per code, with a manifest.json that
/// tools/icons.py imports. See docs/recipe-browser/icons.md.
/// </summary>
public class IconExportSystem : ModSystem
{
    private ICoreClientAPI? _api;
    private ExportRenderer? _renderer;
    private GlReader? _gl;
    private bool _loading;
    private bool _cancelLoad;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _api = api;
        _gl = new GlReader();
        _renderer = new ExportRenderer(api);
        api.Event.RegisterRenderer(_renderer, EnumRenderStage.Ortho, "seraphicons-export");

        IChatCommand cmd = api.ChatCommands.Create("seraphicons")
            .WithDescription("Export item and block icons for the Seraph Horizons recipe browser. " + ExportRequest.Usage);
        foreach (string sub in new[] { "one", "hand", "list", "all" })
        {
            string name = sub;
            cmd.BeginSubCommand(name)
                .WithDescription(name switch
                {
                    "one" => "Render one code: one <code> [size] [--out dir] [--reset uniform|all|none]",
                    "hand" => "Render the held stack: hand [size]",
                    "list" => "Render the codes in a recipe export or a text file: list <file> [size] [--force]",
                    _ => "Render every registered item and block: all [size] [domain] [--force]",
                })
                .WithArgs(api.ChatCommands.Parsers.OptionalAll("args"))
                .HandleWith(args => Start(name, args[0] as string))
                .EndSubCommand();
        }
        cmd.BeginSubCommand("stop").WithDescription("Stop the running export").HandleWith(_ => Stop()).EndSubCommand();
        cmd.BeginSubCommand("status").WithDescription("Progress of the running export").HandleWith(_ => Status()).EndSubCommand();
        cmd.BeginSubCommand("probe")
            .WithDescription("Log the GUI shader state at points across one frame (client log, [seraphiconfix] lines)")
            .HandleWith(_ => Probe())
            .EndSubCommand();
        Diagnostics.Log(api, "loaded: .seraphicons is available");
    }

    private TextCommandResult Start(string sub, string? rest)
    {
        ICoreClientAPI api = _api!;
        if (_renderer!.Session != null || _loading)
        {
            return TextCommandResult.Error("an export is already running; `.seraphicons stop` first");
        }
        if (!ExportRequest.TryParse(sub, rest, out ExportRequest req, out string error))
        {
            return TextCommandResult.Error(error + "\nUsage: " + ExportRequest.Usage);
        }
        if (!GuiUniforms.TrySelect(req.Reset, out IReadOnlyList<UniformDefault> reset))
        {
            return TextCommandResult.Error($"--reset {req.Reset}: not a GUI shader uniform; one of all, none, "
                + string.Join(", ", GuiUniforms.Defaults.Select(u => u.Name)));
        }
        string outDir = Path.GetFullPath(req.OutDir ?? Path.Combine(GamePaths.DataPath, "seraph-icons"));

        switch (req.Mode)
        {
            case ExportMode.List:
                string file = Path.GetFullPath(req.Argument!);
                if (!File.Exists(file))
                {
                    return TextCommandResult.Error($"{file}: no such file");
                }
                // A recipe export is ~100 MB: read and parse it off the main thread.
                _loading = true;
                Task.Run(() =>
                {
                    ListResult? list = null;
                    string? problem = null;
                    try
                    {
                        list = ListFile.Parse(File.ReadAllBytes(file));
                    }
                    catch (Exception e)
                    {
                        problem = $"{file}: {e.Message}";
                    }
                    api.Event.EnqueueMainThreadTask(() =>
                    {
                        _loading = false;
                        if (_cancelLoad)
                        {
                            _cancelLoad = false;
                            api.ShowChatMessage("seraphicons: stopped before it started");
                            return;
                        }
                        if (list == null)
                        {
                            api.ShowChatMessage("seraphicons: " + problem);
                            return;
                        }
                        Launch(req, outDir, $"list {Path.GetFileName(file)}", FromList(list, req, file), reset);
                    }, "seraphicons-list");
                });
                return TextCommandResult.Success($"reading {file} ...");
            case ExportMode.One:
                Resolved one = Resolve(new[] { new ListEntry(req.Argument!, null) });
                if (one.Targets.Count == 0)
                {
                    return TextCommandResult.Error($"{req.Argument}: {one.Failed.FirstOrDefault()?.Reason}");
                }
                return Launch(req, outDir, $"one {req.Argument}", one, reset, force: true);
            case ExportMode.Hand:
                ItemStack? held = api.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                if (held?.Collectible?.Code == null)
                {
                    return TextCommandResult.Error("nothing in the selected hotbar slot");
                }
                string code = held.Collectible.Code.ToString();
                if (!IconCode.IsValid(code))
                {
                    return TextCommandResult.Error($"{code} is not a code the site can use");
                }
                var kind = held.Class == EnumItemClass.Block ? IconKind.Block : IconKind.Item;
                var fromHand = new Resolved();
                fromHand.Add(new RenderTarget(code, kind), held.Clone());
                return Launch(req, outDir, $"hand {code}", fromHand, reset, force: true);
            default:
                return Launch(req, outDir, "all" + (req.Argument != null ? " " + req.Argument : ""), All(req.Argument), reset);
        }
    }

    /// <summary>Targets with their stacks, and the codes that have none.</summary>
    private sealed class Resolved
    {
        public List<RenderTarget> Targets { get; } = new();
        public Dictionary<string, ItemStack> Stacks { get; } = new(StringComparer.Ordinal);
        public List<FailedEntry> Failed { get; } = new();

        public void Add(RenderTarget t, ItemStack stack)
        {
            string rel = IconPaths.ToRelativePath(t.Code, t.Kind);
            if (Stacks.TryAdd(rel, stack))
            {
                Targets.Add(t);
            }
        }
    }

    private Resolved FromList(ListResult list, ExportRequest req, string file)
    {
        ICoreClientAPI api = _api!;
        Diagnostics.Log(api, $"{file}: {list.Format}, {list.Entries.Count} code(s), {list.Duplicates} duplicate(s), {list.Rejected.Count} rejected");
        foreach (RejectedLine r in list.Rejected.Take(20))
        {
            Diagnostics.Warn(api, $"{file}:{r.Line}: {r.Text}: {r.Reason}");
        }
        Resolved resolved = Resolve(list.Entries);
        foreach (RejectedLine r in list.Rejected)
        {
            resolved.Failed.Add(new FailedEntry(r.Text, null, $"line {r.Line} of the list: {r.Reason}"));
        }
        return resolved;
    }

    private Resolved Resolve(IEnumerable<ListEntry> entries)
    {
        IWorldAccessor world = _api!.World;
        var resolved = new Resolved();
        foreach (ListEntry e in entries)
        {
            var loc = new AssetLocation(e.Code);
            Item? item = e.Kind is null or IconKind.Item ? world.GetItem(loc) : null;
            Block? block = e.Kind is null or IconKind.Block ? world.GetBlock(loc) : null;
            if (item?.Code == null && block?.Code == null)
            {
                string what = e.Kind is { } k ? $"no {IconKinds.Name(k)}" : "no item or block";
                resolved.Failed.Add(new FailedEntry(e.Code, e.Kind, $"{what} with this code is registered in this game"));
                continue;
            }
            // Without a kind, a code registered as both an item and a block gets both icons.
            if (item?.Code != null)
            {
                resolved.Add(new RenderTarget(e.Code, IconKind.Item), new ItemStack(item));
            }
            if (block?.Code != null)
            {
                resolved.Add(new RenderTarget(e.Code, IconKind.Block), new ItemStack(block));
            }
        }
        return resolved;
    }

    private Resolved All(string? domain)
    {
        IWorldAccessor world = _api!.World;
        var resolved = new Resolved();
        foreach (Block b in world.Blocks)
        {
            if (b?.Code == null || b.Id == 0 || (domain != null && b.Code.Domain != domain))
            {
                continue;
            }
            AddCollectible(resolved, b.Code.ToString(), IconKind.Block, () => new ItemStack(b));
        }
        foreach (Item i in world.Items)
        {
            if (i?.Code == null || (domain != null && i.Code.Domain != domain))
            {
                continue;
            }
            AddCollectible(resolved, i.Code.ToString(), IconKind.Item, () => new ItemStack(i));
        }
        return resolved;
    }

    private static void AddCollectible(Resolved resolved, string code, IconKind kind, Func<ItemStack> stack)
    {
        if (!IconCode.IsValid(code))
        {
            resolved.Failed.Add(new FailedEntry(code, kind, "not a code the site can use"));
            return;
        }
        try
        {
            resolved.Add(new RenderTarget(code, kind), stack());
        }
        catch (Exception e)
        {
            resolved.Failed.Add(new FailedEntry(code, kind, "could not make a stack: " + e.Message));
        }
    }

    private TextCommandResult Launch(ExportRequest req, string outDir, string label, Resolved resolved,
        IReadOnlyList<UniformDefault> reset, bool force = false)
    {
        ICoreClientAPI api = _api!;
        Manifest manifest;
        try
        {
            Directory.CreateDirectory(outDir);
            manifest = Manifest.LoadOrNew(outDir);
            int adopted = manifest.AdoptFiles(outDir, 0);
            if (adopted > 0)
            {
                Diagnostics.Log(api, $"{adopted} icon file(s) were missing from the manifest (an earlier run did not finish); added");
            }
        }
        catch (Exception e)
        {
            string msg = $"cannot use {outDir}: {e.Message}";
            if (req.Mode == ExportMode.List)
            {
                api.ShowChatMessage("seraphicons: " + msg);
            }
            return TextCommandResult.Error(msg);
        }
        manifest.Generator = $"seraphiconfix {Mod.Info.Version}, game {GameVersion.OverallVersion}";
        foreach (FailedEntry f in resolved.Failed)
        {
            manifest.AddFailure(f);
        }

        bool forced = force || req.Force;
        var targets = resolved.Targets;
        // An item that crashed the game last time is skipped (and recorded) unless forced.
        string marker = Path.Combine(outDir, InflightMarker.FileName);
        var crashed = new List<RenderTarget>();
        if (File.Exists(marker))
        {
            crashed = InflightMarker.Parse(File.ReadAllText(marker));
            foreach (RenderTarget t in crashed)
            {
                Diagnostics.Warn(api, $"the last run ended while rendering {IconKinds.Name(t.Kind)} {t.Code} (a crash?)");
            }
            if (!forced)
            {
                var skip = new HashSet<RenderTarget>(crashed);
                targets = targets.Where(t => !skip.Contains(t)).ToList();
                foreach (RenderTarget t in crashed)
                {
                    manifest.AddFailure(new FailedEntry(t.Code, t.Kind, "the game stopped while rendering this; retry with --force"));
                }
            }
            File.Delete(marker);
        }

        Plan plan = ExportPlanner.Build(targets, rel => File.Exists(Path.Combine(outDir, rel.Replace('/', Path.DirectorySeparatorChar))), forced);
        int failedBefore = resolved.Failed.Count + (forced ? 0 : crashed.Count);
        foreach (FailedEntry f in resolved.Failed.Take(20))
        {
            Diagnostics.Warn(api, $"{f.Code}: {f.Reason}");
        }
        Diagnostics.Log(api, $"{label}: size {req.Size}, {plan.ToRender.Count} to render, {plan.SkippedExisting.Count} skipped as existing, "
            + $"{failedBefore} not renderable, reset {(req.Reset ?? "all")}, output {outDir}");
        _renderer!.Session = new ExportSession(api, _gl!, outDir, label, manifest, plan, resolved.Stacks, req.Size, reset, failedBefore);
        return TextCommandResult.Success($"exporting {plan.ToRender.Count} icon(s) to {outDir}");
    }

    private TextCommandResult Stop()
    {
        ExportSession? s = _renderer?.Session;
        if (s == null && _loading)
        {
            _cancelLoad = true;
            return TextCommandResult.Success("the export will not start");
        }
        if (s == null)
        {
            return TextCommandResult.Success("no export is running");
        }
        s.RequestStop();
        return TextCommandResult.Success("stopping after this frame; run the same command again to resume");
    }

    private TextCommandResult Status() =>
        TextCommandResult.Success(_renderer?.Session?.Status() ?? (_loading ? "reading the list file" : "no export is running"));

    private TextCommandResult Probe()
    {
        string result = GuiStateProbe.Start(_api!, _gl!);
        return TextCommandResult.Success(result);
    }

    public override void Dispose()
    {
        _renderer?.Dispose();
    }
}
