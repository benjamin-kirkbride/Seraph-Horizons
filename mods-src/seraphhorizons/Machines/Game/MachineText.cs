using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// The ore machines' shared block info and messages (#711), so every machine says the same things the
/// same way: its build (the next stage and what it still takes), its wearing parts, why it is or is
/// not working (<see cref="MachineState"/>), and its reservoir. A stage's name is the machine's lang
/// entry <c>seraphhorizons:{machine}-stage-{id}</c> where there is one, else the build file's
/// <c>name</c>, else its id.
/// </summary>
public static class MachineText
{
    private const string P = "seraphhorizons:machine-";

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

    /// <summary>The build's and the wearing parts' lines.</summary>
    public static void AppendAssembly(StringBuilder sb, IWorldAccessor world, string machine, MachineAssembly assembly)
    {
        var build = assembly.Build;
        if (build.Next is { } next)
        {
            sb.AppendLine(Lang.Get(P + "info-stage", next + 1, build.Stages.Count, StageName(machine, build.Stages[next])));
            sb.AppendLine(Lang.Get(P + "info-needs", Needs(world, build)));
            return;
        }
        foreach (var part in assembly.Wearing.Parts)
        {
            string name = Lang.HasTranslation($"seraphhorizons:{machine}-wearing-{part.Id}")
                ? Lang.Get($"seraphhorizons:{machine}-wearing-{part.Id}") : part.Id;
            if (assembly.Wearing.Fitted(part.Id) is null)
                sb.AppendLine(Lang.Get(P + "info-worn", name, part.Count, MaterialName(world, new BuildMaterial(part.Codes, part.Count))));
            else
                sb.AppendLine(Lang.Get(P + "info-wearing", name,
                    Math.Round(100 * assembly.Wearing.ShareLeft(part.Id)).ToString(CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>The state's line ("No power: it needs its shaft turning at 0.05 or faster"): the machine's
    /// own <c>seraphhorizons:{machine}-state-{state}</c> where it has one.</summary>
    public static void AppendState(StringBuilder sb, MachineState state, float minSpeed, string? machine = null)
    {
        string own = $"seraphhorizons:{machine}-state-{state.ToString().ToLowerInvariant()}";
        string key = machine != null && Lang.HasTranslation(own) ? own : MachineRun.LangCode(state);
        sb.AppendLine(Lang.Get(key, minSpeed.ToString("0.##", CultureInfo.InvariantCulture)));
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
