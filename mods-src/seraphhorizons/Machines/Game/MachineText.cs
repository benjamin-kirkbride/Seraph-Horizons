using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// The shared block info and messages of every machine on the shared base (#711; <see cref="BEMachine"/>),
/// so every machine says the same things the same way, in this order: its build (the next stage and what it
/// still takes) or, once built, its wearing parts; why it is or is not working (<see cref="MachineState"/>,
/// with its progress); its oil; its reservoir; then the machine's own lines. A player holding a wrench
/// (<see cref="HoldsWrench"/>) also sees the detail (<see cref="MachineInfo"/>): the wearing parts' uses
/// left, the oil's drain, the shaft's speed and load, the facing and the axle's face, and the machine's own
/// detail; without one, a last line says a wrench shows more. A stage's name is the machine's lang entry
/// <c>seraphhorizons:{machine}-stage-{id}</c> where there is one, else the build file's <c>name</c>, else its
/// id; a state's is the machine's <c>seraphhorizons:{machine}-state-{state}</c> where it has one.
/// </summary>
public static class MachineText
{
    private const string P = "seraphhorizons:machine-";

    /// <summary>The last line for a player without a wrench, when there is detail to show.</summary>
    public const string WrenchHintKey = P + "info-wrenchhint";

    /// <summary>Whether <paramref name="player"/> holds a wrench (the game's <see cref="EnumTool.Wrench"/>,
    /// <c>game:wrench-*</c>) in either hand: their block info then shows the detail.</summary>
    public static bool HoldsWrench(IPlayer? player) =>
        player?.Entity is { } entity
        && (entity.RightHandItemSlot?.Itemstack?.Collectible?.Tool == EnumTool.Wrench
            || entity.LeftHandItemSlot?.Itemstack?.Collectible?.Tool == EnumTool.Wrench);

    /// <summary>A stage's name for <paramref name="machine"/>.</summary>
    public static string StageName(string machine, MachineStage stage)
    {
        string key = $"seraphhorizons:{machine}-stage-{stage.Id}";
        return Lang.HasTranslation(key) ? Lang.Get(key) : stage.Name ?? stage.Id;
    }

    /// <summary>A material's items, by the name of the first item in the world that is it (patterns
    /// show the first match: "Oak support beam" stands for any wood), or its first code.</summary>
    public static string MaterialName(IWorldAccessor world, BuildMaterial material)
    {
        string code = material.Codes[0];
        CollectibleObject? item = null;
        if (code.Contains('*'))
        {
            var loc = new AssetLocation(code);
            item = (CollectibleObject?)world.SearchItems(loc).FirstOrDefault() ?? world.SearchBlocks(loc).FirstOrDefault();
        }
        else
        {
            var loc = new AssetLocation(code);
            item = (CollectibleObject?)world.GetItem(loc) ?? world.GetBlock(loc);
        }
        return item != null && item.Id != 0 ? new ItemStack(item).GetName() : code;
    }

    /// <summary>The next stage's needs as "41 × Oak support beam, 6 × Oak plank".</summary>
    public static string Needs(IWorldAccessor world, StagedBuild build) =>
        string.Join(", ", build.NextNeeds().Select(n => $"{n.Missing} × {MaterialName(world, n.Material)}"));

    /// <summary>A wearing part's name for <paramref name="machine"/>: its lang entry
    /// <c>seraphhorizons:{machine}-wearing-{id}</c>, else its id.</summary>
    public static string WearingName(string machine, WearingPart part)
    {
        string key = $"seraphhorizons:{machine}-wearing-{part.Id}";
        return Lang.HasTranslation(key) ? Lang.Get(key) : part.Id;
    }

    /// <summary>The build's and the wearing parts' lines: the next stage and what it still takes, or once
    /// built each wearing part's share left (its uses left, with a wrench).</summary>
    public static void AppendAssembly(MachineInfo info, IWorldAccessor world, string machine, MachineAssembly assembly)
    {
        var build = assembly.Build;
        if (build.Next is { } next)
        {
            info.Add(Lang.Get(P + "info-stage", next + 1, build.Stages.Count, StageName(machine, build.Stages[next])));
            info.Add(Lang.Get(P + "info-needs", Needs(world, build)));
            return;
        }
        foreach (var part in assembly.Wearing.Parts)
        {
            string name = WearingName(machine, part);
            if (assembly.Wearing.Fitted(part.Id) is not { } fitted)
            {
                // a machine says it its own way where it has one (the draw bench's die, also taken out by hand)
                string own = $"seraphhorizons:{machine}-info-worn";
                info.Add(Lang.Get(Lang.HasTranslation(own) ? own : P + "info-worn", name, part.Count,
                    MaterialName(world, new BuildMaterial(part.Codes, part.Count))));
            }
            else
            {
                info.Add(Lang.Get(P + "info-wearing", name,
                    Math.Round(100 * assembly.Wearing.ShareLeft(part.Id)).ToString(CultureInfo.InvariantCulture)));
                info.AddDetail(Lang.Get(P + "info-wearing-uses", name, Count(fitted.Left), Count(part.UsesFor(fitted.Code))));
            }
        }
    }

    /// <summary>The oil's lines (<see cref="MachineOiling"/>'s, every oiled machine's): the tank, and while dry
    /// how much harder it turns (a machine whose dry tank wears its cutter instead says only that it is dry); with
    /// a wrench, the load on the shaft against the load oiled while dry, the load as it is otherwise for a machine
    /// with no shaft line of its own (<paramref name="withLoad"/>: the game's and Immersive Woodworking's), and
    /// what a job drains.</summary>
    public static void AppendOil(MachineInfo info, OilState oil, OilDryEffect effect, double drainPerJob, bool withLoad = false)
    {
        const string O = "seraphhorizons:machineoil-";
        info.Add(Lang.Get(O + "info-tank", Oil.Shown(oil.Tank.Points), Oil.Shown(oil.Tank.Capacity)));
        bool loads = effect == OilDryEffect.Load;
        if (oil.Dry)
            info.Add(loads
                ? Lang.Get(O + "info-dry", oil.DryMultiplier.ToString("0.##", CultureInfo.InvariantCulture))
                : Lang.Get(P + "info-dry-wear"));
        if (loads && oil.Dry && oil.Load is { } load && oil.OiledLoad is { } oiled)
            info.AddDetail(Lang.Get(O + "info-dry-load", oil.DryMultiplier.ToString("0.##", CultureInfo.InvariantCulture),
                OilText.Load(load), OilText.Load(oiled)));
        else if (withLoad && oil.Load is { } asked)
            info.AddDetail(Lang.Get(O + "info-load", OilText.Load(asked)));
        if (drainPerJob > 0)
            info.AddDetail(Lang.Get(P + "info-oil-drain", Count(drainPerJob),
                Math.Floor(OilDrain.JobsPerTank(oil.Tank.Points, drainPerJob) + 1e-9).ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The shaft's line, for a wrench: its speed, and the load the machine last put on it.</summary>
    public static void AppendShaft(StringBuilder sb, float speed, float? load) =>
        sb.AppendLine(load is { } l
            ? Lang.Get(P + "info-shaft-load", Math.Abs(speed).ToString("0.00", CultureInfo.InvariantCulture), OilText.Load(l))
            : Lang.Get(P + "info-shaft", Math.Abs(speed).ToString("0.00", CultureInfo.InvariantCulture)));

    /// <summary>The footprint's line, for a wrench: the way the machine faces, and the face its axle meets.</summary>
    public static void AppendFootprint(StringBuilder sb, Side side, BlockFacing? powerFace)
    {
        string facing = Lang.Get("seraphhorizons:machine-facing-" + side.Code());
        sb.AppendLine(powerFace is { } face
            ? Lang.Get(P + "info-facing-axle", facing, Lang.Get("seraphhorizons:machine-facing-" + face.Code))
            : Lang.Get(P + "info-facing", facing));
    }

    private static string Count(double n) => n.ToString(n < 10 && Math.Abs(n - Math.Round(n)) > 1e-9 ? "0.#" : "0", CultureInfo.InvariantCulture);

    /// <summary>The state's line ("No power: it needs its shaft turning at 0.05 or faster"): the machine's
    /// own <c>seraphhorizons:{machine}-state-{state}</c> where it has one, and the work's
    /// <paramref name="percent"/> after it where given ("Working (42%)").</summary>
    public static void AppendState(StringBuilder sb, MachineState state, float minSpeed, string? machine = null, int? percent = null)
    {
        string own = $"seraphhorizons:{machine}-state-{state.ToString().ToLowerInvariant()}";
        string key = machine != null && Lang.HasTranslation(own) ? own : MachineRun.LangCode(state);
        string line = Lang.Get(key, minSpeed.ToString("0.##", CultureInfo.InvariantCulture));
        sb.AppendLine(percent is { } p ? Lang.Get(P + "state-progress", line, p) : line);
    }

    /// <summary>The reservoir's line: litres in it of its capacity, and an item's water.</summary>
    public static void AppendWater(StringBuilder sb, MachineReservoir water, double capacity, double perItem) =>
        sb.AppendLine(Lang.Get(P + "info-water", Litres(water.Litres), Litres(capacity), Litres(perItem)));

    private static string Litres(double l) => l.ToString(l < 10 ? "0.#" : "0", CultureInfo.InvariantCulture);

    /// <summary>The message for a right-click that took nothing; null when it took something.</summary>
    public static string? Error(AssemblyClick click, IWorldAccessor world, string machine, MachineAssembly assembly)
    {
        if (click.ErrorLangCode is not { } key)
            return null;
        string next = assembly.Build.Next is { } n ? StageName(machine, assembly.Build.Stages[n]) : "";
        return Lang.Get(key, next, Needs(world, assembly.Build));
    }
}
