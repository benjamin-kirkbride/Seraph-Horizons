using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// Carry On as the one way a trunk leaves the ground: into a player's Carry On hands slot (the
/// only "inventory" a trunk stack goes into), out of it onto a station, a cart or the ground.
/// Found by name at run time, so the mod builds without Carry On (<c>CarryOn.CarrySystem</c>'s
/// <c>CarryManager</c>, CarryOnLib's <c>CarriedBlock</c> and <c>CarrySlot</c>); when anything is
/// not as expected there is one warning and <see cref="Available"/> is false.
///
/// While trunk entities run and Carry On is there:
/// <list type="bullet">
/// <item>sneak + right-click with an empty hand on a trunk entity shoulders it
/// (<see cref="EntityBehaviorTrunkCarry"/>, added to the trunk entity types by
/// <c>patches/trunkentities-carryon.json</c>);</item>
/// <item>putting a carried trunk down (Carry On's place-down, on both sides) lays a trunk entity
/// along the player's view at the selected cell and never places a block; Carry On dropping one
/// (death, damage, quick drop, its carried-block entity) lays one where the carrier stands;</item>
/// <item>while a trunk is carried in hands the player's <c>walkspeed</c> stat gets
/// <see cref="SpeedCode"/>, so the walk speed is <see cref="TrunkWeight.CarrySpeed"/> of its logs
/// (Carry On's own slot modifier is cancelled out); removed when it is not;</item>
/// <item>Logging Expanded's Trunk Storage Rack loses its Carryable (<see cref="StripRacks"/>);</item>
/// <item>Cartwright's carts and sleds take a carried trunk into a storage slot through Carry On's
/// <c>attachablecarryable</c> (<c>patches/trunkentities-carts.json</c>), and taking one off, by
/// Carry On's key or the game's own empty-hand take, puts it in the player's hands.</item>
/// </list>
/// The trunk's carry animation (Logging Expanded's <c>trunkcarry</c>, <c>trunkcarryheavy</c> for
/// thick trunks) and Carry On's <c>carry-trunk</c> transform are a second Carryable merged into
/// Logging Expanded's by Carry On (<c>patches/trunkentities-carryon.json</c>).
/// </summary>
public static class TrunkCarry
{
    public const string CarrySystemName = "CarryOn.CarrySystem";
    public const string HarmonyId = "seraphhorizons.trunkcarry";

    /// <summary>The player's <c>walkspeed</c> stat code while carrying a trunk.</summary>
    public const string SpeedCode = "seraphhorizons:trunk";

    /// <summary>Carry On's own <c>walkspeed</c> code for its hands slot (<c>carryon:Hands</c>).</summary>
    public const string CarryOnHandsCode = "carryon:Hands";

    /// <summary>The entity behaviour code of <see cref="EntityBehaviorTrunkCarry"/>.</summary>
    public const string BehaviorCode = "seraphhorizons.trunkcarry";

    /// <summary>How often carried trunks' walk speed is checked, milliseconds.</summary>
    public const int SpeedCheckMs = 250;

    /// <summary>Stack attributes Carry On leaves on a block that went through a cart slot.</summary>
    private static readonly string[] CartLeftovers = ["backpack", "carryonbackup"];

    private sealed class Members
    {
        public required PropertyInfo Manager;
        public required MethodInfo GetCarried, SetCarried, RemoveCarried, HasPermissionAt;
        public required ConstructorInfo NewCarried;
        public required PropertyInfo Stack, Slot;
        public required object Hands;
        public required MethodInfo PlaceDown, DropCarriedBlock, DropAsEntityOrItem;
    }

    private static readonly object Lock = new();
    private static Members? _members;
    private static bool _resolved;
    private static int _patchedBy;
    private static Harmony? _harmony;

    // ---- the API stations and machines use ----

    /// <summary>Whether carrying runs on this side: trunk entities on, Carry On installed and as expected.</summary>
    public static bool Available(ICoreAPI api) => Manager(api) != null;

    /// <summary>The trunk stack in <paramref name="player"/>'s Carry On hands slot, or null (nothing
    /// carried, something else carried, or carrying unavailable).</summary>
    public static ItemStack? Carried(IPlayer player) =>
        player.Entity is { } entity && CarriedIn(entity) is { } carried && StackOf(carried) is { } stack && Trunks.IsTrunk(stack) ? stack : null;

    /// <summary>Puts <paramref name="trunk"/> into <paramref name="player"/>'s empty Carry On hands.
    /// False when Carry On's hands hold anything, either of the player's hands holds an item (Carry
    /// On locks both hand slots while carrying, and starts nothing, a put-down included, unless both
    /// are empty, so the trunk could not be put down), the stack is not a trunk, or carrying is
    /// unavailable; <see cref="HandsFull"/> says which. Server side.</summary>
    public static bool TryGive(IServerPlayer player, ItemStack trunk)
    {
        var entity = player.Entity;
        if (entity == null || entity.Api.Side != EnumAppSide.Server || Manager(entity.Api) is not { } manager
            || !trunk.ResolveBlockOrItem(entity.World) || !Trunks.IsTrunk(trunk) || CarriedIn(entity) != null
            || !HandsEmpty(entity))
            return false;
        var m = _members!;
        var stack = Clean(trunk.Clone());
        stack.StackSize = 1;
        // Carry On attaches a block to a cart only with block entity data, so the trunk gets some.
        var data = new TreeAttribute();
        data.SetString("blockCode", stack.Collectible.Code.ToShortString());
        data.SetString("type", "");
        var carried = m.NewCarried.Invoke([m.Hands, stack, data]);
        m.SetCarried.Invoke(manager, [entity, carried, null, true]);
        UpdateSpeed(entity);
        return true;
    }

    /// <summary>Removes the trunk from <paramref name="player"/>'s Carry On hands and returns it;
    /// null when they hold none. Server side.</summary>
    public static ItemStack? Take(IServerPlayer player)
    {
        var entity = player.Entity;
        if (entity == null || entity.Api.Side != EnumAppSide.Server || Manager(entity.Api) is not { } manager
            || CarriedIn(entity) is not { } carried || StackOf(carried) is not { } stack || !Trunks.IsTrunk(stack))
            return null;
        _members!.RemoveCarried.Invoke(manager, [entity, _members.Hands, true]);
        UpdateSpeed(entity);
        return Clean(stack);
    }

    /// <summary>Whether both of <paramref name="entity"/>'s hands are empty (the active hotbar slot
    /// and the offhand), as Carry On needs them to carry.</summary>
    public static bool HandsEmpty(EntityAgent entity) =>
        entity.RightHandItemSlot is not { Empty: false } && entity.LeftHandItemSlot is not { Empty: false };

    /// <summary>Tells <paramref name="player"/> why a trunk does not go into their hands (in-game
    /// error): Carry On's hands are full, or else a hand holds an item.</summary>
    public static void HandsFull(IServerPlayer player)
    {
        string code = player.Entity is { } entity && CarriedIn(entity) == null && !HandsEmpty(entity)
            ? "trunkentities-hands-not-empty" : "trunkentities-hands-full";
        player.SendIngameError(code, Lang.GetL(player.LanguageCode, "seraphhorizons:" + code));
    }

    // ---- set-up ----

    /// <summary>Carry On's carry manager on this side, or null: trunk entities off here, Carry On
    /// missing, or not as expected.</summary>
    internal static object? Manager(ICoreAPI api)
    {
        var system = TrunkEntitySystem.Of(api);
        if (system == null || !system.Enabled || !system.CarryOn || Resolve(api) is not { } m)
            return null;
        return api.ModLoader.GetModSystem(CarrySystemName) is { } carry ? m.Manager.GetValue(carry) : null;
    }

    private static Members? Resolve(ICoreAPI api)
    {
        lock (Lock)
        {
            if (_resolved)
                return _members;
            _resolved = true;
            var problems = new List<string>();
            _members = Bind(api, problems);
            if (_members == null)
                api.Logger.Warning("[seraphhorizons] Trunk entities: Carry On is not as expected, so trunks cannot be carried: {0}", string.Join("; ", problems));
            return _members;
        }
    }

    private static Members? Bind(ICoreAPI api, List<string> problems)
    {
        Type? TypeOf(string name)
        {
            var type = AccessTools.TypeByName(name);
            if (type == null)
                problems.Add($"no type {name}");
            return type;
        }
        MethodInfo? MethodOf(Type? type, string name, Type?[] args)
        {
            if (type == null || args.Any(a => a == null))
                return null;
            var method = AccessTools.Method(type, name, args!);
            if (method == null)
                problems.Add($"no {type.Name}.{name}({string.Join(", ", args.Select(a => a!.Name))})");
            return method;
        }

        var system = TypeOf(CarrySystemName);
        var managerType = TypeOf("CarryOn.API.Common.Interfaces.ICarryManager");
        var carriedType = TypeOf("CarryOn.API.Common.Models.CarriedBlock");
        var slotType = TypeOf("CarryOn.API.Common.Models.CarrySlot");
        var placement = TypeOf("CarryOn.Common.Services.CarryPlacementService");
        var drop = TypeOf("CarryOn.Common.Services.CarryDropService");
        var placer = TypeOf("CarryOn.Server.Logic.BlockPlacer");
        if (system == null || managerType == null || carriedType == null || slotType == null)
            return null;

        var manager = AccessTools.Property(system, "CarryManager");
        if (manager == null)
            problems.Add("no CarrySystem.CarryManager");
        var nullableSlot = typeof(Nullable<>).MakeGenericType(slotType);
        var getCarried = MethodOf(managerType, "GetCarried", [typeof(Entity), slotType]);
        var setCarried = MethodOf(managerType, "SetCarried", [typeof(Entity), carriedType, nullableSlot, typeof(bool)]);
        var removeCarried = MethodOf(managerType, "RemoveCarried", [typeof(Entity), slotType, typeof(bool)]);
        var permission = MethodOf(managerType, "HasPermissionAt", [typeof(Entity), typeof(BlockPos), typeof(bool)]);
        var ctor = AccessTools.Constructor(carriedType, [slotType, typeof(ItemStack), typeof(ITreeAttribute)]);
        if (ctor == null)
            problems.Add("no CarriedBlock(CarrySlot, ItemStack, ITreeAttribute)");
        var stack = AccessTools.Property(carriedType, "ItemStack");
        var slot = AccessTools.Property(carriedType, "Slot");
        if (stack == null || slot == null)
            problems.Add("no CarriedBlock.ItemStack or .Slot");
        object? hands = Enum.GetNames(slotType).Contains("Hands") ? Enum.Parse(slotType, "Hands") : null;
        if (hands == null)
            problems.Add("no CarrySlot.Hands");
        var placeDown = MethodOf(placement, "TryPlaceDown",
            [typeof(Entity), carriedType, typeof(BlockSelection), typeof(string).MakeByRefType(), typeof(bool), typeof(bool)]);
        var dropCarried = MethodOf(drop, "DropCarriedBlock", [typeof(Entity), carriedType, typeof(int), placer]);
        var dropAs = MethodOf(drop, "DropBlockAsEntityOrItem", [carriedType, typeof(BlockPos), typeof(IServerPlayer), typeof(Entity), typeof(bool)]);
        if (problems.Count > 0)
            return null;
        return new Members
        {
            Manager = manager!, GetCarried = getCarried!, SetCarried = setCarried!, RemoveCarried = removeCarried!,
            HasPermissionAt = permission!, NewCarried = ctor!, Stack = stack!, Slot = slot!, Hands = hands!,
            PlaceDown = placeDown!, DropCarriedBlock = dropCarried!, DropAsEntityOrItem = dropAs!,
        };
    }

    /// <summary>Patches Carry On's place-down and drops and the game's take from an attachment slot,
    /// once per process however many sides start (singleplayer runs both in one); false, with one
    /// warning, when Carry On is not as expected.</summary>
    internal static bool Start(ICoreAPI api)
    {
        if (Resolve(api) is not { } m)
            return false;
        lock (Lock)
        {
            if (_patchedBy++ > 0)
                return true;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(m.PlaceDown, prefix: new HarmonyMethod(typeof(TrunkCarry), nameof(PlaceDownPrefix)));
            _harmony.Patch(m.DropCarriedBlock, prefix: new HarmonyMethod(typeof(TrunkCarry), nameof(DropCarriedPrefix)));
            _harmony.Patch(m.DropAsEntityOrItem, prefix: new HarmonyMethod(typeof(TrunkCarry), nameof(DropAsEntityPrefix)));
            var take = AccessTools.Method(typeof(EntityBehaviorAttachable), "TryRemoveAttachment", [typeof(EntityAgent), typeof(int)]);
            if (take != null)
                _harmony.Patch(take, prefix: new HarmonyMethod(typeof(TrunkCarry), nameof(TakeAttachmentPrefix)));
            else
                api.Logger.Warning("[seraphhorizons] Trunk entities: the game's EntityBehaviorAttachable.TryRemoveAttachment is gone, so only Carry On's key takes a trunk off a cart");
            return true;
        }
    }

    internal static void Stop()
    {
        lock (Lock)
        {
            if (_patchedBy == 0 || --_patchedBy > 0)
                return;
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }
    }

    /// <summary>Removes Carry On's Carryable from Logging Expanded's Trunk Storage Racks
    /// (<c>loggingmod:trunkstorage-*</c>): racks are not shouldered. Runs after Carry On's own
    /// asset pass, which merges and maps Carryables. Returns the racks changed.</summary>
    internal static int StripRacks(ICoreAPI api)
    {
        int count = 0;
        foreach (var block in api.World.Blocks)
        {
            if (block?.Code is not { Domain: TrunkEntitySystem.LeModId } code || !code.Path.StartsWith("trunkstorage-", StringComparison.Ordinal))
                continue;
            int before = block.BlockBehaviors.Length + block.CollectibleBehaviors.Length;
            block.BlockBehaviors = block.BlockBehaviors.Where(b => !IsCarryable(b)).ToArray();
            block.CollectibleBehaviors = block.CollectibleBehaviors.Where(b => !IsCarryable(b)).ToArray();
            if (block.BlockBehaviors.Length + block.CollectibleBehaviors.Length != before)
                count++;
        }
        return count;
    }

    /// <summary>Whether <paramref name="behavior"/> is Carry On's Carryable.</summary>
    public static bool IsCarryable(CollectibleBehavior? behavior) =>
        behavior?.GetType().FullName == "CarryOn.Common.Behaviors.BlockBehaviorCarryable";

    /// <summary>Lets every trunk into an attachment slot through Logging Expanded's own bypass
    /// (<c>LoggingMod.AttachableStorageBypass</c>), which it registers only while its
    /// <c>TreeTrunkBackpackOnly</c> is on: the pack's storage flag fits no slot either way.</summary>
    internal static void RegisterCartBypass(ICoreAPI api)
    {
        var register = AccessTools.TypeByName("LoggingMod.AttachableStorageBypass") is { } type
            ? AccessTools.Method(type, "Register", [typeof(string)]) : null;
        if (register == null)
        {
            api.Logger.Warning("[seraphhorizons] Trunk entities: Logging Expanded's AttachableStorageBypass is gone, so carts may refuse trunks");
            return;
        }
        foreach (var block in api.World.Blocks)
            if (block?.Code is { Domain: TrunkEntitySystem.LeModId } code && code.Path.StartsWith("treetrunk-", StringComparison.Ordinal))
                register.Invoke(null, [code.Path]);
    }

    // ---- walk speed ----

    /// <summary>Gives every online player carrying a trunk the trunk's walk speed, and takes it
    /// from those who no longer do. Server side, every <see cref="SpeedCheckMs"/>.</summary>
    internal static void UpdateSpeeds(ICoreServerAPI api)
    {
        foreach (var player in api.World.AllOnlinePlayers)
            if (player.Entity is { } entity)
                UpdateSpeed(entity);
    }

    /// <summary>Sets or removes <paramref name="entity"/>'s <see cref="SpeedCode"/> walk speed for
    /// what it carries in hands. Server side.</summary>
    public static void UpdateSpeed(EntityAgent entity)
    {
        if (entity.Api?.Side != EnumAppSide.Server)
            return;
        var stats = entity.Stats["walkspeed"];
        if (stats == null)
            return;
        bool has = stats.ValuesByKey.TryGetValue(SpeedCode, out var current);
        var trunk = CarriedIn(entity) is { } carried && StackOf(carried) is { } stack && Trunks.IsTrunk(stack) ? stack : null;
        if (trunk == null)
        {
            if (has)
                entity.Stats.Remove("walkspeed", SpeedCode);
            return;
        }
        var system = TrunkEntitySystem.Of(entity.Api);
        float speed = TrunkWeight.CarrySpeed(Trunks.StoredLogs(trunk, entity.World), system.Config);
        float carryOn = stats.ValuesByKey.TryGetValue(CarryOnHandsCode, out var own) ? own.Value * own.Weight : 0f;
        float value = speed - 1f - carryOn;
        if (!has || Math.Abs(current!.Value - value) > 1e-4f)
            entity.Stats.Set("walkspeed", SpeedCode, value);
    }

    // ---- Harmony ----

    // CarryPlacementService.TryPlaceDown(entity, carriedBlock, selection, ref failureCode, dropped, playSound):
    // a trunk lies down as an entity, on the client as on the server (the client predicts, then asks).
    private static bool PlaceDownPrefix(Entity __0, object __1, BlockSelection __2, ref string __3, bool __4, ref bool __result)
    {
        if (__0?.Api is not { } api || __2?.Position == null || StackOf(__1) is not { } stack || !Trunks.IsTrunk(stack)
            || Manager(api) is not { } manager)
            return true;
        var m = _members!;
        if (api.Side == EnumAppSide.Server)
        {
            if (!(bool)m.HasPermissionAt.Invoke(manager, [__0, __2.Position, !__4])!)
            {
                __3 = "no-permission";
                __result = false;
                return false;
            }
            if (!Lay(__0, stack, __2.Position, along: !__4))
            {
                __result = false;
                return false;
            }
        }
        m.RemoveCarried.Invoke(manager, [__0, m.Slot.GetValue(__1), true]);
        if (__0 is EntityAgent agent)
            UpdateSpeed(agent);
        if (api.Side == EnumAppSide.Server)
            api.World.PlaySoundAt(stack.Block?.Sounds?.Place ?? GlobalConstants.DefaultBuildSound, __0, (__0 as EntityPlayer)?.Player);
        __result = true;
        return false;
    }

    // CarryDropService.DropCarriedBlock(entity, carriedBlock, range, blockPlacer): death, damage,
    // quick drop. A trunk lies down where the carrier stands, never a block nor an item.
    private static bool DropCarriedPrefix(Entity __0, object __1) => !DropHere(__0, __1);

    // CarryDropService.DropBlockAsEntityOrItem(carriedBlock, centerBlock, player, entity, forceEntity).
    private static bool DropAsEntityPrefix(object __0, Entity __3) => !DropHere(__3, __0);

    private static bool DropHere(Entity? carrier, object? carried)
    {
        if (carrier?.Api is not { Side: EnumAppSide.Server } api || StackOf(carried) is not { } stack || !Trunks.IsTrunk(stack)
            || Manager(api) is not { } manager)
            return false;
        var m = _members!;
        Lay(carrier, stack, carrier.Pos.AsBlockPos, along: false);
        m.RemoveCarried.Invoke(manager, [carrier, m.Slot.GetValue(carried), true]);
        if (carrier is EntityAgent agent)
            UpdateSpeed(agent);
        return true;
    }

    // EntityBehaviorAttachable.TryRemoveAttachment(byEntity, selectionBoxIndex), the game's
    // empty-hand take from a cart slot: a trunk fits no inventory, so it goes to the hands.
    private static bool TakeAttachmentPrefix(EntityBehaviorAttachable __instance, EntityAgent __0, int __1, ref bool __result)
    {
        var slot = __instance.GetSlotFromSelectionBoxIndex(__1);
        if (slot?.Itemstack is not { } stack || !Trunks.IsTrunk(stack) || __0?.Api is not { } api || Manager(api) == null)
            return true;
        __result = false;
        if (api.Side != EnumAppSide.Server || __0 is not EntityPlayer { Player: IServerPlayer player })
            return false;
        if (CarriedIn(__0) != null || !HandsEmpty(__0))
        {
            HandsFull(player);
            return false;
        }
        var target = __instance.entity;
        if (target.GetBehavior<EntityBehaviorOwnable>() is { } ownable && !ownable.IsOwner(__0))
            return false;
        if (!TryGive(player, stack))
            return false;
        slot.Itemstack = null;
        __instance.storeInv();
        target.MarkShapeModified();
        target.World.BlockAccessor.GetChunkAtBlockPos(target.Pos.AsBlockPos)?.MarkModified();
        api.World.PlaySoundAt(stack.Block?.Sounds?.Place ?? GlobalConstants.DefaultBuildSound, target, player);
        __result = true;
        return false;
    }

    // ---- helpers ----

    /// <summary>Lays <paramref name="trunk"/> down as an entity in the cell <paramref name="cell"/>:
    /// along the carrier's view, reaching away from it from that cell when <paramref name="along"/>,
    /// centred on the cell otherwise.</summary>
    private static bool Lay(Entity carrier, ItemStack trunk, BlockPos cell, bool along)
    {
        var world = carrier.World;
        float yaw = carrier.Pos.Yaw;
        var pos = new Vec3d(cell.X + 0.5, cell.Y, cell.Z + 0.5);
        if (along)
        {
            var size = TrunkBox.Size(TrunkBox.ClassOf(trunk.Block?.Variant["size"]));
            double reach = Math.Max(0, size.Length / 2.0 - 0.5);
            pos.Add(-Math.Sin(yaw) * reach, 0, -Math.Cos(yaw) * reach);
        }
        var entity = TrunkSpawns.Spawn(world, Clean(trunk.Clone()), pos, yaw, cell.dimension);
        if (entity == null)
            world.Logger.Warning("[seraphhorizons] Trunk entities: a carried trunk ({0}) could not be laid down, so it stays carried", trunk.Collectible?.Code);
        return entity != null;
    }

    /// <summary>The carried block in <paramref name="entity"/>'s hands, or null.</summary>
    private static object? CarriedIn(Entity entity) =>
        Manager(entity.Api) is { } manager ? _members!.GetCarried.Invoke(manager, [entity, _members.Hands]) : null;

    private static ItemStack? StackOf(object? carried)
    {
        if (carried == null || _members == null || !_members.Stack.DeclaringType!.IsInstanceOfType(carried))
            return null;
        return _members.Stack.GetValue(carried) as ItemStack;
    }

    /// <summary>The stack without what Carry On's cart slots leave on it.</summary>
    private static ItemStack Clean(ItemStack stack)
    {
        foreach (var key in CartLeftovers)
            stack.Attributes.RemoveAttribute(key);
        if (stack.Attributes.HasAttribute("type") && string.IsNullOrEmpty(stack.Attributes.GetString("type")))
            stack.Attributes.RemoveAttribute("type");
        return stack;
    }
}

/// <summary>
/// Server side, on the trunk entity types (<c>patches/trunkentities-carryon.json</c>, with Carry
/// On): sneak + right-click with an empty hand shoulders the trunk into Carry On's hands and the
/// entity goes; with the hands already full, an in-game error.
/// </summary>
public class EntityBehaviorTrunkCarry(Entity entity) : EntityBehavior(entity)
{
    public override string PropertyName() => TrunkCarry.BehaviorCode;

    /// <summary>Whether a click is a pick-up: sneaking, with an empty hand. (A full offhand is still
    /// a pick-up, refused with the error that says to empty it.)</summary>
    public static bool Wants(EntityAgent byEntity, ItemSlot? slot) =>
        byEntity is EntityPlayer && (slot == null || slot.Empty) && (byEntity.Controls.ShiftKey || byEntity.Controls.Sneak);

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
        if (mode != EnumInteractMode.Interact || entity.Api.Side != EnumAppSide.Server || entity is not EntityTrunk { Alive: true } trunk
            || !Wants(byEntity, itemslot) || byEntity is not EntityPlayer { Player: IServerPlayer player } || !TrunkCarry.Available(entity.Api)
            || trunk.Trunk is not { } stack)
            return;
        handled = EnumHandling.PreventSubsequent;
        if (!TrunkCarry.TryGive(player, stack))
        {
            TrunkCarry.HandsFull(player);
            return;
        }
        entity.World.PlaySoundAt(stack.Block?.Sounds?.Place ?? GlobalConstants.DefaultBuildSound, entity, player);
        trunk.Die(EnumDespawnReason.Removed);
    }
}
