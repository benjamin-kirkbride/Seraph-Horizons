using HarmonyLib;
using SeraphHorizons.Mod.Trading.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading;

/// <summary>
/// The handbook's "Sold by" and "Purchased by" leave out traders never met in a world with the
/// trader grid (README "Traders"): the game's and the other mods' traders whose spawners the grid
/// rewrites (<see cref="HandbookTraders"/>). A story structure's trader (vanilla's treasure
/// hunter) stays: its spawner is left alone.
///
/// <list type="bullet">
/// <item>Server: at worldgen init, after the game's story structures have loaded their schematics
/// and the grid has decided (<see cref="TradingSystem.GridActive"/>), writes the hidden traders'
/// codes to the world config (<see cref="HiddenKey"/>; none without the grid), which goes to every
/// client, and which the recipe exporter reads for its "Sold by trader" too.</item>
/// <item>Client: a prefix on the game's private <c>TradeHandbookInfo.AddTraderHandbookInfo</c>,
/// which adds one trader's name to an item's section, skips the hidden traders' names.</item>
/// </list>
/// </summary>
public class TraderHandbookSystem : ModSystem
{
    public const string HarmonyId = "seraphhorizons.traderhandbook";

    /// <summary>The world config key of the hidden traders' entity codes (a string array).</summary>
    public const string HiddenKey = "seraphhorizons:handbookHiddenTraders";

    private static ICoreClientAPI? s_capi;
    private static HashSet<string>? s_hiddenNames;
    private Harmony? _harmony;

    // After TradingSystem (0.6): its grid decision and camp capture run first at worldgen init.
    public override double ExecuteOrder() => 0.61;

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Event.InitWorldGenerator(() => Publish(api), "standard");
    }

    /// <summary>The traders the handbook lists: every entity type with a vanilla-format trade list,
    /// as the game's TradeHandbookInfo finds them.</summary>
    public static IEnumerable<EntityProperties> Listed(ICoreAPI api) =>
        api.World.EntityTypes.Where(t => t?.Code != null && t.Attributes is { } a
                                         && (a["tradePropsFile"].AsString(null) != null || a["tradeProps"].Exists));

    private static readonly System.Reflection.FieldInfo? SchematicField =
        AccessTools.Field(typeof(Vintagestory.ServerMods.WorldGenStoryStructure), "schematicData");

    /// <summary>The entity codes the story structures' spawners spawn (none in a world without
    /// lore content, where the game loads no story structures); null when the game's story
    /// structure no longer keeps its schematic where this looks for it.</summary>
    public static List<string>? StoryTraderCodes(ICoreServerAPI api)
    {
        if (SchematicField is null) return null;
        var codes = new List<string>();
        if (api.ModLoader.GetModSystem<GenStoryStructures>()?.scfg?.Structures is not { } structures) return codes;
        foreach (var structure in structures)
        {
            if (structure is null || SchematicField.GetValue(structure) is not BlockSchematic schematic) continue;
            foreach (string data in schematic.BlockEntities.Values)
            {
                try
                {
                    if (schematic.DecodeBlockEntityData(data)?["entityCodes"] is StringArrayAttribute { value: { } spawned })
                        codes.AddRange(spawned.Where(c => !string.IsNullOrEmpty(c)));
                }
                catch (Exception e)
                {
                    api.Logger.VerboseDebug("[seraphhorizons] Trading: a block entity of story structure {0} does not decode: {1}", structure.Code, e.Message);
                }
            }
        }
        return codes;
    }

    private static void Publish(ICoreServerAPI api)
    {
        List<string> hidden = [];
        if (TradingSystem.Of(api)?.GridActive == true)
        {
            if (StoryTraderCodes(api) is { } story)
                hidden = HandbookTraders.Hidden(Listed(api).Select(t => new ListedTrader(t.Code.ToString(), t.Class)), story);
            else
                api.Logger.Warning("[seraphhorizons] Trading: the game's story structures look different (no schematicData); "
                                   + "the handbook keeps listing the traders the grid replaces");
        }
        api.World.Config[HiddenKey] = new StringArrayAttribute(hidden.ToArray());
        if (hidden.Count > 0)
            api.Logger.Notification("[seraphhorizons] Trading: the handbook leaves out {0} traders the grid replaces", hidden.Count);
    }

    /// <summary>The hidden traders' codes this side knows: the server's, from the world config.</summary>
    public static string[] HiddenCodes(ICoreAPI api) =>
        (api.World?.Config?[HiddenKey] as StringArrayAttribute)?.value ?? [];

    public override void StartClientSide(ICoreClientAPI api)
    {
        s_capi = api;
        s_hiddenNames = null;
        var target = AccessTools.DeclaredMethod(typeof(TradeHandbookInfo), "AddTraderHandbookInfo",
            [typeof(TradeItem), typeof(string), typeof(string)]);
        if (target == null)
        {
            api.Logger.Warning("[seraphhorizons] Trading: TradeHandbookInfo.AddTraderHandbookInfo is not as expected; the game changed, "
                               + "so the handbook still lists the traders the grid replaces");
            return;
        }
        _harmony = new Harmony(HarmonyId);
        _harmony.Patch(target, prefix: new HarmonyMethod(typeof(TraderHandbookSystem), nameof(AddTraderPrefix)));
    }

    public override void Dispose()
    {
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
        s_capi = null;
        s_hiddenNames = null;
    }

    private static bool AddTraderPrefix(string traderName)
    {
        if (s_capi is not { } capi) return true;
        s_hiddenNames ??= HiddenNames(capi);
        return !s_hiddenNames.Contains(traderName);
    }

    private static HashSet<string> HiddenNames(ICoreClientAPI capi)
    {
        try
        {
            string[] codes = HiddenCodes(capi);
            if (codes.Length == 0) return [];
            // The game names a trader by Lang.GetMatching of its creature key, as here.
            return HandbookTraders.HiddenNames(
                Listed(capi).Select(t => (t.Code.ToString(), Lang.GetMatching(t.Code.Domain + ":item-creature-" + t.Code.Path))), codes);
        }
        catch (Exception e)
        {
            capi.Logger.Error("[seraphhorizons] Trading: could not work out which traders the handbook leaves out: {0}", e);
            return [];
        }
    }
}
