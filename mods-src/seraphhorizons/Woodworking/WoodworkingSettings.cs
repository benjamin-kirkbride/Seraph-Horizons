using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// The settings of Immersive Woodworking the unified system needs, set in memory on the server
/// (never written to the player's files, which Immersive Woodworking has already written in its
/// StartPre). This runs in Start, after Immersive Woodworking's StartPre has loaded them and before
/// its AssetsLoaded strips recipes by them and its StartServerSide takes the copy it sends every
/// joining client, so clients get these values too. With the tweak off nothing is set.
///
/// Logging Expanded's settings need no change: its splitting log yields go with its splitting
/// logs, and its sawhorses keep its own.
/// </summary>
public sealed class WoodworkingSettings : WoodworkingPart
{
    /// <summary>One setting: its field, its type, and its value given the current one.</summary>
    private sealed record Setting(string Field, Type Type, System.Func<object, object> Value);

    private static readonly Setting[] Settings =
    [
        // The pit saw is retired with Immersive Woodworking's sawhorse, the only station it works
        // at: this strips its grid and smithing recipes (RetiredStations hides the items).
        new("RemovePitSaw", typeof(bool), _ => true),
        // Its sawhorse is retired: keep its grid recipe unregistered whatever the player's file says.
        new("CraftableSawhorse", typeof(bool), _ => false),
        // Bark comes off a log with a bark spud, or an axe with a hammer in the offhand; the knife
        // only stripped bark on the retired sawhorse. Off, the handbook's bark table drops its row.
        new("AllowKnifeDebark", typeof(bool), _ => false),
        // A hand splitting block yields the primitive tier's 6 per log; the splitting block part
        // raises it to the advanced tier's for a chop on an advanced block.
        new("FirewoodPerLog", typeof(int), _ => SplittingBlockTier.Primitive.FirewoodPerLog()),
        // Immersive Woodworking requires its piles per log to be at most the firewood per log.
        new("FirewoodDropCount", typeof(int),
            current => Math.Min((int)current, SplittingBlockTier.Primitive.FirewoodPerLog())),
        // The chopper takes only an advanced splitting block as its bed, so it yields that tier's.
        new("ChopperFirewoodPerLog", typeof(int), _ => SplittingBlockTier.Advanced.FirewoodPerLog()),
    ];

    private readonly List<FieldInfo> _fields = [];
    // The values Start replaced, for Undo.
    private readonly List<(FieldInfo Field, object? Value)> _before = [];
    private WoodworkingMods? _mods;

    public override string Name => "Immersive Woodworking's settings";

    public override string? Bind(ICoreAPI api, WoodworkingMods mods)
    {
        _mods = mods;
        _fields.Clear();
        foreach (var setting in Settings)
        {
            var field = AccessTools.DeclaredField(mods.IwConfigClass, setting.Field);
            if (field == null || field.IsStatic || field.FieldType != setting.Type)
                return $"{WoodworkingMods.IwConfigType}.{setting.Field} ({setting.Type.Name}) is missing";
            _fields.Add(field);
        }
        return null;
    }

    public override void Start(ICoreAPI api, Harmony harmony)
    {
        // A client takes the server's settings when it joins.
        if (api.Side != EnumAppSide.Server)
            return;
        object config = _mods!.IwConfig;
        _before.Clear();
        for (int i = 0; i < Settings.Length; i++)
        {
            object current = _fields[i].GetValue(config)!;
            _before.Add((_fields[i], current));
            _fields[i].SetValue(config, Settings[i].Value(current));
        }
    }

    public override void Undo(ICoreAPI api)
    {
        if (_mods == null)
            return;
        object config = _mods.IwConfig;
        foreach (var (field, value) in _before)
            field.SetValue(config, value);
        _before.Clear();
    }

    public override void Dispose()
    {
        _mods = null;
        _before.Clear();
    }
}
