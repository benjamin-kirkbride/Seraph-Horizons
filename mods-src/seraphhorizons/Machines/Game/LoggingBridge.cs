using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// Everything the machines (the bucking mill, the rosser) read from Logging Expanded (loggingmod)
/// that a trunk's own item stack does not carry, found by name at run time so the mod builds from
/// the game alone: the Trunk Storage Rack's block entity (<c>LoggingMod.BETrunkStorage</c>), the
/// logs a wood cuts into, with and without bark (<c>LoggingMod.TreeManager</c>) and the branch rule
/// (<c>LoggingMod.LoggingConfig</c>). Resolved once; when anything is missing it logs one warning
/// and <see cref="Resolve"/> gives null, and the machines neither take nor work trunks.
/// </summary>
public sealed class LoggingBridge
{
    public const string ModId = "loggingmod";

    private readonly Type _rackType;
    private readonly MethodInfo _popTrunk;
    private readonly MethodInfo _getStoredTrunks;
    private readonly MethodInfo _pushTrunk;
    private readonly PropertyInfo _trunkCount;
    private readonly PropertyInfo _treeManagerInstance;
    private readonly MethodInfo _getPlacedLogCode;
    private readonly MethodInfo _getDebarkedLogCode;
    private readonly PropertyInfo _configCurrent;
    private readonly PropertyInfo _requireBranchRemoval;

    private LoggingBridge(Type rackType, MethodInfo popTrunk, MethodInfo getStoredTrunks, MethodInfo pushTrunk, PropertyInfo trunkCount,
                          PropertyInfo treeManagerInstance,
                          MethodInfo getPlacedLogCode, MethodInfo getDebarkedLogCode, PropertyInfo configCurrent,
                          PropertyInfo requireBranchRemoval)
    {
        _rackType = rackType;
        _popTrunk = popTrunk;
        _getStoredTrunks = getStoredTrunks;
        _pushTrunk = pushTrunk;
        _trunkCount = trunkCount;
        _treeManagerInstance = treeManagerInstance;
        _getPlacedLogCode = getPlacedLogCode;
        _getDebarkedLogCode = getDebarkedLogCode;
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
        var pushTrunk = Method(rack, "PushTrunk", [typeof(ItemStack)], typeof(void));
        var trunkCount = Property(rack, "TrunkCount", Instance, typeof(int));
        var instance = Property(treeManager, "Instance", Static, treeManager);
        var placedLog = Method(treeManager, "GetPlacedLogCode", [typeof(string)], typeof(AssetLocation));
        var debarkedLog = Method(treeManager, "GetDebarkedLogCode", [typeof(string)], typeof(AssetLocation));
        var current = Property(config, "Current", Static, config);
        var branches = Property(config, "RequireBranchRemovalForProcessing", Instance, typeof(bool));
        if (rack != null && !typeof(BlockEntity).IsAssignableFrom(rack))
            problems.Add("LoggingMod.BETrunkStorage is not a block entity");

        if (problems.Count > 0)
            return null;
        return new LoggingBridge(rack!, popTrunk!, getStored!, pushTrunk!, trunkCount!, instance!, placedLog!, debarkedLog!, current!, branches!);
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

    /// <summary>The upright debarked log Logging Expanded gives for <paramref name="wood"/>
    /// (<c>debarkedlog-&lt;wood&gt;-ud</c> in any domain), or null (also before its tree manager
    /// exists).</summary>
    public AssetLocation? DebarkedLogCode(string wood) =>
        _treeManagerInstance.GetValue(null) is { } manager
            ? (AssetLocation?)_getDebarkedLogCode.Invoke(manager, [wood])
            : null;

    public bool IsRack(BlockEntity? be) => be != null && _rackType.IsInstanceOfType(be);

    /// <summary>The trunk the rack would give next (it is last in, first out), without taking it.</summary>
    public ItemStack? PeekTrunk(BlockEntity rack) =>
        _getStoredTrunks.Invoke(rack, null) is ItemStack[] { Length: > 0 } trunks ? trunks[^1] : null;

    /// <summary>Takes the rack's next trunk; the rack updates its fill variant itself.</summary>
    public ItemStack? PopTrunk(BlockEntity rack) => (ItemStack?)_popTrunk.Invoke(rack, null);

    /// <summary>How many trunks the rack holds (it holds 4 at most).</summary>
    public int TrunkCount(BlockEntity rack) => (int)_trunkCount.GetValue(rack)!;

    /// <summary>Puts <paramref name="trunk"/> on top of the rack. The rack silently drops it when it
    /// already holds 4, so check <see cref="TrunkCount"/> first; the caller marks the rack dirty.</summary>
    public void PushTrunk(BlockEntity rack, ItemStack trunk) => _pushTrunk.Invoke(rack, [trunk]);

    /// <summary>The rack whose controller or filler cell is at <paramref name="pos"/>, or null.</summary>
    public BlockEntity? FindRack(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlock(pos) is BlockMultiblock filler)
            pos = pos.AddCopy(filler.OffsetInv);
        var be = blockAccessor.GetBlockEntity(pos);
        return IsRack(be) ? be : null;
    }
}
