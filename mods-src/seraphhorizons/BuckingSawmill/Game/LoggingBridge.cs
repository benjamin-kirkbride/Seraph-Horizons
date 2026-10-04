using System.Reflection;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.BuckingSawmill;

/// <summary>
/// Everything the mill reads from Logging Expanded (loggingmod) that a trunk's own item stack does
/// not carry, found by name at run time so the mod builds from the game alone: the Trunk Storage
/// Rack's block entity (<c>LoggingMod.BETrunkStorage</c>), the log a wood cuts into
/// (<c>LoggingMod.TreeManager</c>) and the branch rule (<c>LoggingMod.LoggingConfig</c>).
/// Resolved once; when anything is missing it logs one warning and <see cref="Resolve"/> gives
/// null, and the mill neither takes nor cuts trunks.
/// </summary>
public sealed class LoggingBridge
{
    public const string ModId = "loggingmod";

    private readonly Type _rackType;
    private readonly MethodInfo _popTrunk;
    private readonly MethodInfo _getStoredTrunks;
    private readonly PropertyInfo _treeManagerInstance;
    private readonly MethodInfo _getPlacedLogCode;
    private readonly PropertyInfo _configCurrent;
    private readonly PropertyInfo _requireBranchRemoval;

    private LoggingBridge(Type rackType, MethodInfo popTrunk, MethodInfo getStoredTrunks, PropertyInfo treeManagerInstance,
                          MethodInfo getPlacedLogCode, PropertyInfo configCurrent, PropertyInfo requireBranchRemoval)
    {
        _rackType = rackType;
        _popTrunk = popTrunk;
        _getStoredTrunks = getStoredTrunks;
        _treeManagerInstance = treeManagerInstance;
        _getPlacedLogCode = getPlacedLogCode;
        _configCurrent = configCurrent;
        _requireBranchRemoval = requireBranchRemoval;
    }

    /// <summary>Finds every member in the loaded loggingmod; on failure fills
    /// <paramref name="problems"/> and returns null.</summary>
    public static LoggingBridge? Resolve(ICoreAPI api, out List<string> problems)
    {
        problems = [];
        var assembly = api.ModLoader.GetMod(ModId)?.Systems.FirstOrDefault()?.GetType().Assembly;
        if (assembly == null)
        {
            problems.Add("loggingmod is not loaded");
            return null;
        }
        const BindingFlags Instance = BindingFlags.Public | BindingFlags.Instance;
        const BindingFlags Static = BindingFlags.Public | BindingFlags.Static;
        var list = problems;

        Type? TypeOf(string name)
        {
            var type = assembly.GetType(name);
            if (type == null)
                list.Add($"type {name} not found");
            return type;
        }
        MethodInfo? Method(Type? type, string name, Type[] args, Type returns)
        {
            if (type == null)
                return null;
            var method = type.GetMethod(name, Instance, args);
            if (method == null || !returns.IsAssignableFrom(method.ReturnType))
                list.Add($"{type.Name}.{name}({string.Join(", ", args.Select(a => a.Name))}) returning {returns.Name} not found");
            return method != null && returns.IsAssignableFrom(method.ReturnType) ? method : null;
        }
        PropertyInfo? Property(Type? type, string name, BindingFlags flags, Type? of = null)
        {
            if (type == null)
                return null;
            var property = type.GetProperty(name, flags);
            if (property == null || (of != null && property.PropertyType != of))
            {
                list.Add($"{type.Name}.{name}{(of != null ? " (" + of.Name + ")" : "")} not found");
                return null;
            }
            return property;
        }

        var rack = TypeOf("LoggingMod.BETrunkStorage");
        var treeManager = TypeOf("LoggingMod.TreeManager");
        var config = TypeOf("LoggingMod.LoggingConfig");
        var popTrunk = Method(rack, "PopTrunk", Type.EmptyTypes, typeof(ItemStack));
        var getStored = Method(rack, "GetStoredTrunks", Type.EmptyTypes, typeof(ItemStack[]));
        var instance = Property(treeManager, "Instance", Static, treeManager);
        var placedLog = Method(treeManager, "GetPlacedLogCode", [typeof(string)], typeof(AssetLocation));
        var current = Property(config, "Current", Static, config);
        var branches = Property(config, "RequireBranchRemovalForProcessing", Instance, typeof(bool));
        if (rack != null && !typeof(BlockEntity).IsAssignableFrom(rack))
            problems.Add("LoggingMod.BETrunkStorage is not a block entity");

        if (problems.Count > 0)
            return null;
        return new LoggingBridge(rack!, popTrunk!, getStored!, instance!, placedLog!, current!, branches!);
    }

    /// <summary>Logging Expanded's RequireBranchRemovalForProcessing (its default, true, while its
    /// config is not loaded).</summary>
    public bool RequireBranchRemoval =>
        _configCurrent.GetValue(null) is not { } config || (bool)_requireBranchRemoval.GetValue(config)!;

    /// <summary>The upright placed log Logging Expanded makes from <paramref name="wood"/>, or null
    /// (also before its tree manager exists).</summary>
    public AssetLocation? PlacedLogCode(string wood) =>
        _treeManagerInstance.GetValue(null) is { } manager
            ? (AssetLocation?)_getPlacedLogCode.Invoke(manager, [wood])
            : null;

    public bool IsRack(BlockEntity? be) => be != null && _rackType.IsInstanceOfType(be);

    /// <summary>The trunk the rack would give next (it is last in, first out), without taking it.</summary>
    public ItemStack? PeekTrunk(BlockEntity rack) =>
        _getStoredTrunks.Invoke(rack, null) is ItemStack[] { Length: > 0 } trunks ? trunks[^1] : null;

    /// <summary>Takes the rack's next trunk; the rack updates its fill variant itself.</summary>
    public ItemStack? PopTrunk(BlockEntity rack) => (ItemStack?)_popTrunk.Invoke(rack, null);
}
