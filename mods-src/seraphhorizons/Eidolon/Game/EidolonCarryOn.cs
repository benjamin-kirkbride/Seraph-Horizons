using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Carry On, found by name (README "Eidolon", carrying; the mod builds without it), for the eidolon's
/// carry job (#676): which blocks it may lift (any Carry On lets a player carry, in its hands or on its
/// back: Carry On's <c>ICarryManager.IsCarryable</c>), whether a player may take one from where it
/// stands (<c>HasPermissionAt</c>: their claims, reinforcement), and the lift itself
/// (<c>GetCarriedFromWorld</c>: Carry On's own pick-up checks and events, too hot to carry among
/// them, the block and its block entity data taken out of the world, wall signs on it with it),
/// written in Carry On's own form (CarryOnLib's <c>CarriedBlockTreeSerializer</c>: <c>Stack</c>,
/// <c>Data</c>, <c>OriginalBlockCode</c>, <c>OriginalMeshAngle</c>, <c>Children</c>).
/// Setting it down needs no Carry On (<see cref="EntityBehaviorEidolonCarry.TryPlace"/>), so a load
/// outlives the mod. When anything is not as expected there is one warning and the job refuses.
/// </summary>
public static class EidolonCarryOn
{
    public const string CarrySystemName = "CarryOn.CarrySystem";

    private sealed class Members
    {
        public required PropertyInfo Manager;
        public required MethodInfo IsCarryable, IsCarryableIn, HasPermissionAt, GetCarriedFromWorld, Serialize;
        public required object[] Slots;
    }

    private static readonly object Lock = new();
    private static Members? _members;
    private static bool _resolved;

    /// <summary>Whether Carry On is here and as expected on this side.</summary>
    public static bool Available(ICoreAPI api) => Manager(api) != null;

    /// <summary>Whether Carry On lets a player carry <paramref name="block"/> in any slot.</summary>
    public static bool IsCarryable(ICoreAPI api, Block block) =>
        block.Id != 0 && Manager(api) is { } manager && (bool)_members!.IsCarryable.Invoke(manager, [block])!;

    /// <summary>Whether <paramref name="entity"/> (a player: their claims; anything else: only that it
    /// is not reinforced) may take the block at <paramref name="pos"/>, as Carry On asks.</summary>
    public static bool MayTake(Entity entity, BlockPos pos) =>
        Manager(entity.Api) is { } manager && (bool)_members!.HasPermissionAt.Invoke(manager, [entity, pos, false])!;

    /// <summary>
    /// Takes the block at <paramref name="pos"/> out of the world for <paramref name="carrier"/>, as
    /// Carry On lifts a block (its checks and events, the block entity's data, wall signs on it), and
    /// returns it in Carry On's carried form; null, with Carry On's reason in
    /// <paramref name="failure"/> when it gives one, when it cannot be carried. Server side.
    /// </summary>
    public static ITreeAttribute? Lift(Entity carrier, BlockPos pos, out string? failure)
    {
        failure = null;
        if (Manager(carrier.Api) is not { } manager)
            return null;
        var m = _members!;
        var block = carrier.World.BlockAccessor.GetBlock(pos);
        object? slot = m.Slots.FirstOrDefault(s => (bool)m.IsCarryableIn.Invoke(manager, [block, s])!);
        if (slot == null)
            return null;
        object?[] args = [carrier, pos, slot, "__ignore__", true];
        var carried = m.GetCarriedFromWorld.Invoke(manager, args);
        if (carried == null)
        {
            failure = args[3] as string is { Length: > 0 } code && code != "__ignore__" ? code : null;
            return null;
        }
        return m.Serialize.Invoke(null, [carried]) as ITreeAttribute;
    }

    private static object? Manager(ICoreAPI api)
    {
        if (api.ModLoader.GetModSystem(CarrySystemName) is not { } system || Resolve(api) is not { } m)
            return null;
        return m.Manager.GetValue(system);
    }

    private static Members? Resolve(ICoreAPI api)
    {
        lock (Lock)
        {
            if (_resolved)
                return _members;
            _resolved = true;
            var problems = new List<string>();
            _members = Bind(problems);
            if (_members == null)
                api.Logger.Warning("[seraphhorizons] Eidolon: Carry On is not as expected, so eidolons cannot carry blocks: {0}", string.Join("; ", problems));
            return _members;
        }
    }

    private static Members? Bind(List<string> problems)
    {
        Type? TypeOf(string name)
        {
            var type = AccessTools.TypeByName(name);
            if (type == null)
                problems.Add($"no type {name}");
            return type;
        }
        MethodInfo? MethodOf(Type? type, string name, Type[] args)
        {
            if (type == null)
                return null;
            var method = AccessTools.Method(type, name, args);
            if (method == null)
                problems.Add($"no {type.Name}.{name}({string.Join(", ", args.Select(a => a.Name))})");
            return method;
        }

        var system = TypeOf(CarrySystemName);
        var managerType = TypeOf("CarryOn.API.Common.Interfaces.ICarryManager");
        var carriedType = TypeOf("CarryOn.API.Common.Models.CarriedBlock");
        var slotType = TypeOf("CarryOn.API.Common.Models.CarrySlot");
        var serializer = TypeOf("CarryOn.Utility.CarriedBlockTreeSerializer");
        if (system == null || managerType == null || carriedType == null || slotType == null || serializer == null)
            return null;
        var manager = AccessTools.Property(system, "CarryManager");
        if (manager == null)
            problems.Add("no CarrySystem.CarryManager");
        var isCarryable = MethodOf(managerType, "IsCarryable", [typeof(Block)]);
        var isCarryableIn = MethodOf(managerType, "IsCarryable", [typeof(Block), slotType]);
        var permission = MethodOf(managerType, "HasPermissionAt", [typeof(Entity), typeof(BlockPos), typeof(bool)]);
        var fromWorld = MethodOf(managerType, "GetCarriedFromWorld",
            [typeof(Entity), typeof(BlockPos), slotType, typeof(string).MakeByRefType(), typeof(bool)]);
        var serialize = MethodOf(serializer, "Serialize", [carriedType]);
        // Hands first (where the eidolon holds it), then the back: any slot a player may carry it in.
        var slots = new[] { "Hands", "Back" }.Where(n => Enum.GetNames(slotType).Contains(n)).Select(n => Enum.Parse(slotType, n)).ToArray();
        if (slots.Length == 0)
            problems.Add("no CarrySlot.Hands or .Back");
        if (problems.Count > 0)
            return null;
        return new Members
        {
            Manager = manager!, IsCarryable = isCarryable!, IsCarryableIn = isCarryableIn!, HasPermissionAt = permission!,
            GetCarriedFromWorld = fromWorld!, Serialize = serialize!, Slots = slots,
        };
    }
}
