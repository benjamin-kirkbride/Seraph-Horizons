using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One item the Butchering mod's smoking rack turns into another.</summary>
public sealed record Smoked(Item From, Item Output);

public sealed class SmokingData
{
    public required string Mod;
    public required string BlockEntityClass;

    /// <summary>The racks: every block whose block entity is the mod's smoking rack.</summary>
    public required List<Block> Racks;

    /// <summary>In-game hours an item hangs over the fire before it turns.</summary>
    public required double Hours;
    public List<Smoked> Items = new();
}

/// <summary>
/// The Butchering mod's smoking rack (decompiled 1.14.3, BlockEntityMeatHook): an item whose
/// attribute `transformsWhenSmoked` names another item can be hung on it, one per slot.
/// While the firepit directly below burns, each item's smoking time grows, and past
/// `smokingTimeHours` it becomes one of the named item (`new AssetLocation(code)`, so a
/// domain-less code is in `game`); when the fire is out the time starts over. Blocks cannot
/// be hung (the rack reads `Itemstack.Item`). The class is found by name through the class
/// registry, as the exporter cannot reference the mod.
/// </summary>
public static class Smoking
{
    public const string BlockEntityClassName = "BlockEntityMeatHook";
    public const string Attribute = "transformsWhenSmoked";

    /// <summary>Null when no loaded mod registers the rack, or its time cannot be read.</summary>
    public static SmokingData? Find(ICoreServerAPI api)
    {
        var racks = api.World.Blocks
            .Where(b => b?.Code != null && !b.IsMissing && b.EntityClass != null &&
                        api.ClassRegistry.GetBlockEntity(b.EntityClass)?.Name == BlockEntityClassName)
            .OrderBy(b => b.Code.ToString(), StringComparer.Ordinal).ToList();
        if (racks.Count == 0) return null;
        var type = api.ClassRegistry.GetBlockEntity(racks[0].EntityClass);
        var mod = api.ModLoader.Mods.FirstOrDefault(m => m.Systems.Any(s => s.GetType().Assembly == type.Assembly))?.Info.ModID;
        var hours = type.GetField("smokingTimeHours", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue();
        if (mod == null || hours == null)
        {
            api.Logger.Warning("[seraphexport] the smoking rack {0} was found but its mod or smoking time was not; smoking not exported", type.FullName);
            return null;
        }

        var data = new SmokingData
        {
            Mod = mod,
            BlockEntityClass = type.Name,
            Racks = racks,
            Hours = Convert.ToDouble(hours),
        };
        foreach (var item in api.World.Items.Where(i => i?.Code != null && !i.IsMissing).OrderBy(i => i.Code.ToString(), StringComparer.Ordinal))
        {
            var code = item.Attributes?[Attribute].AsString(null);
            if (string.IsNullOrEmpty(code)) continue;
            var output = api.World.GetItem(new AssetLocation(code));
            if (output == null || output.IsMissing)
            {
                api.Logger.Warning("[seraphexport] {0} smokes into {1}, which is not registered; skipped", item.Code, code);
                continue;
            }
            data.Items.Add(new Smoked(item, output));
        }
        return data;
    }
}
