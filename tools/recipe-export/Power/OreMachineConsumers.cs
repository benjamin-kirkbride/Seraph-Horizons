using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace SeraphHorizons.RecipeExport.Power;

/// <summary>
/// The seraphhorizons ore machines (#711, #765) as power consumers. Every one's power cell carries the
/// same behavior (<c>seraphhorizons.MachineMP</c>), so they cannot be told apart by behavior as
/// <see cref="Consumers.Specs"/> are: one entry per build file (<c>config/builds/{machine}.json</c>) whose
/// frame block (its <c>block</c>) is registered, its load the machine's <c>machines</c> entry in
/// <c>config/ore-processing.json</c> (ore-core's <c>OreRecovery.Machine</c>). A constant-load machine
/// draws its working load once built, working or idle, and the shared machine code's 0.005 until then;
/// one whose load follows its job (the jaw breaker, the reverberatory roaster, the Parkes kettle) ranges
/// from its empty load to its working one. The machines that turn on a shaft take oil (<see cref="Oiled"/>),
/// their dry load the MachineOil multiplier's, as every oiled machine's.
/// </summary>
public static class OreMachineConsumers
{
    public const string BuildsFolder = "config/builds/";
    public static readonly AssetLocation RecoveryAsset = new(Consumers.Seraph, "config/ore-processing.json");
    private const string From = "config/ore-processing.json (machines)";

    /// <summary>The machines whose load follows the job (<c>OreRecovery</c>'s LoadFollowsJob).</summary>
    private static readonly HashSet<string> FollowsJob = new(StringComparer.Ordinal) { "jawbreaker", "reverberatory", "parkeskettle" };

    /// <summary>A build file's machine id where ore-processing.json names it otherwise.</summary>
    private static readonly Dictionary<string, string> RecoveryKey = new(StringComparer.Ordinal) { ["shakingtable"] = "table" };

    /// <summary>The MachineOilSettings entry of each machine that takes oil (its block entity's <c>OilKind</c>).</summary>
    private static readonly Dictionary<string, string> Oiled = new(StringComparer.Ordinal)
    {
        ["jawbreaker"] = "JawBreaker", ["stampbattery"] = "StampBattery", ["arrastra"] = "Arrastra", ["ballmill"] = "BallMill",
        ["jig"] = "Jig", ["shakingtable"] = "ShakingTable", ["vanner"] = "Vanner", ["reverberatory"] = "ReverberatoryRoaster",
        ["parkeskettle"] = "ParkesKettle",
    };

    /// <summary>The shared machine code's load until built (<c>MachineLoad.Unassembled</c>).</summary>
    public const double Unassembled = 0.005;

    private static readonly JsonLoadSettings Lenient = new() { CommentHandling = CommentHandling.Ignore };

    internal static void Add(Live live, BlockIndex blocks, Items.ModIndex mods, List<JObject> list)
    {
        if (!live.ModLoaded(Consumers.Seraph)) return;
        var api = live.Api;
        JObject? machines = null;
        foreach (var asset in api.Assets.GetMany(BuildsFolder, Consumers.Seraph, loadAsset: true).OrderBy(a => a.Location.Path, StringComparer.Ordinal))
        {
            JObject build;
            try { build = JObject.Parse(asset.ToText(), Lenient); }
            catch (Newtonsoft.Json.JsonException) { continue; }
            if ((string?)build["machine"] is not { } machine || (string?)build["block"] is not { } code || blocks.Get(code) is not { } block)
                continue;
            if (machines == null)
            {
                try { machines = JObject.Parse(api.Assets.Get(RecoveryAsset).ToText(), Lenient)["machines"] as JObject ?? new JObject(); }
                catch (Exception e)
                {
                    api.Logger.Warning("[seraphexport] power: cannot read {0} ({1}); the ore machines are left out", RecoveryAsset, e.Message);
                    return;
                }
            }
            string key = RecoveryKey.GetValueOrDefault(machine, machine);
            if (machines[key] is not JObject entry || !((double?)entry["load"] is > 0 and var load))
                continue;
            double empty = (double?)entry["emptyLoad"] ?? 0;
            var figures = new List<Figure> { new(load, $"{key}.load", From) };
            string item = blocks.ItemFor(block.Code.ToString()[..block.Code.ToString().LastIndexOf('-')]) ?? block.Code.ToString();
            var o = new JObject
            {
                ["id"] = $"{Consumers.Seraph}:{machine}",
                ["item"] = item,
                ["mod"] = mods.ModForCollectible(block),
                ["name"] = blocks.NameOf(item, machine),
                ["category"] = "machine",
            };
            if (FollowsJob.Contains(key))
            {
                figures.Insert(0, new Figure(empty, $"{key}.emptyLoad", From));
                o["load"] = PowerSection.Num(empty);
                o["loadMax"] = PowerSection.Num(load);
                o["note"] = "from its empty load to its working load as its job fills; 0.005 until built";
            }
            else
            {
                o["load"] = PowerSection.Num(load);
                o["note"] = "its working load once built, working or idle; 0.005 until built";
            }
            o["idleLoad"] = PowerSection.Num(Unassembled);
            figures.Add(Live.Constant("load until built", Unassembled, "MachineLoad.Unassembled"));
            if (Oiled.TryGetValue(machine, out var oil))
                Consumers.AddOil(live, oil, false, o, figures);
            o["sources"] = PowerSection.Sources(figures);
            list.Add(o);
        }
    }
}
