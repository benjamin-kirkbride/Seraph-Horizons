using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// Immersive Woodworking and Logging Expanded as the tweak's parts reach them: neither is
/// referenced at build time, so their types and members are found by name here, once, and handed
/// to every part's <see cref="WoodworkingPart.Bind"/>.
///
/// Immersive Woodworking keeps its settings in an object on its mod system
/// (<c>ImmersiveWoodworkingModSystem.Config</c>, public fields), loaded in its server
/// <c>StartPre</c> and sent to each joining client, which replaces its own copy with it. Logging
/// Expanded keeps them in a static singleton (<c>LoggingConfig.Current</c>, properties), loaded on
/// each side in its <c>StartPre</c> and never synced. Both are read live through
/// <see cref="IwConfig"/> and <see cref="LeConfig"/>, never cached: the objects get replaced.
/// </summary>
public sealed class WoodworkingMods
{
    public const string IwModId = "immersivewoodworking";
    public const string LeModId = "loggingmod";
    public const string IwNamespace = "ImmersiveWoodworking";
    public const string LeNamespace = "LoggingMod";
    public const string IwSystemType = IwNamespace + ".ImmersiveWoodworkingModSystem";
    public const string IwConfigType = IwNamespace + ".ImmersiveWoodworkingConfig";
    public const string LeConfigType = LeNamespace + ".LoggingConfig";

    private readonly PropertyInfo _iwConfig;
    private readonly PropertyInfo _leConfig;

    /// <summary>Immersive Woodworking's mod system on this side.</summary>
    public ModSystem IwSystem { get; }

    /// <summary>The type of <see cref="IwConfig"/>.</summary>
    public Type IwConfigClass { get; }

    /// <summary>The type of <see cref="LeConfig"/>.</summary>
    public Type LeConfigClass { get; }

    private WoodworkingMods(ModSystem iwSystem, PropertyInfo iwConfig, PropertyInfo leConfig)
    {
        IwSystem = iwSystem;
        _iwConfig = iwConfig;
        _leConfig = leConfig;
        IwConfigClass = iwConfig.PropertyType;
        LeConfigClass = leConfig.PropertyType;
    }

    /// <summary>Whether both mods are installed: the tweak does nothing otherwise.</summary>
    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(IwModId) && api.ModLoader.IsModEnabled(LeModId);

    /// <summary>Immersive Woodworking's settings on this side, as its code reads them now.</summary>
    public object IwConfig => _iwConfig.GetValue(IwSystem)!;

    /// <summary>Logging Expanded's settings on this side, as its code reads them now.</summary>
    public object LeConfig => _leConfig.GetValue(null)!;

    /// <summary>An Immersive Woodworking type by its name in the mod's namespace, e.g.
    /// <c>BlockEntityChoppingBlock</c>; null if there is none.</summary>
    public static Type? IwType(string name) => AccessTools.TypeByName(IwNamespace + "." + name);

    /// <summary>A Logging Expanded type by its name in the mod's namespace, e.g.
    /// <c>BlockSawhorse</c>; null if there is none.</summary>
    public static Type? LeType(string name) => AccessTools.TypeByName(LeNamespace + "." + name);

    /// <summary>A field of Immersive Woodworking's settings, if it is there with that type.</summary>
    public FieldInfo? IwSetting<T>(string name) =>
        AccessTools.DeclaredField(IwConfigClass, name) is { IsStatic: false } field && field.FieldType == typeof(T) ? field : null;

    /// <summary>A settable property of Logging Expanded's settings, if it is there with that type.</summary>
    public PropertyInfo? LeSetting<T>(string name) =>
        AccessTools.DeclaredProperty(LeConfigClass, name) is { CanRead: true, CanWrite: true } property
        && property.PropertyType == typeof(T) ? property : null;

    /// <summary>Finds both mods' settings. Returns null, with what is missing in
    /// <paramref name="missing"/>, when either is not where it was.</summary>
    public static WoodworkingMods? Bind(ICoreAPI api, out string? missing)
    {
        var systemType = AccessTools.TypeByName(IwSystemType);
        var system = systemType == null ? null : api.ModLoader.GetModSystem(IwSystemType);
        var iwConfig = systemType == null ? null : AccessTools.DeclaredProperty(systemType, "Config");
        var leConfig = AccessTools.TypeByName(LeConfigType) is { } leType ? AccessTools.DeclaredProperty(leType, "Current") : null;
        if (system == null || !systemType!.IsInstanceOfType(system) || iwConfig?.GetMethod is not { IsStatic: false }
            || iwConfig.PropertyType.FullName != IwConfigType)
        {
            missing = $"{IwSystemType}.Config";
            return null;
        }
        if (leConfig?.GetMethod is not { IsStatic: true } || leConfig.PropertyType.FullName != LeConfigType)
        {
            missing = $"{LeConfigType}.Current";
            return null;
        }
        missing = null;
        return new WoodworkingMods(system, iwConfig, leConfig);
    }
}
