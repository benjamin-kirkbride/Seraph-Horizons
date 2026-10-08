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
/// own <c>attachablecarryable</c> (its <c>carryonmore</c> patch gives them it), and taking one off,
/// by Carry On's key or the game's own empty-hand take, puts it in the player's hands.</item>
/// </list>
/// The trunk's carry animation (Logging Expanded's <c>trunkcarry</c>, <c>trunkcarryheavy</c> for
/// thick trunks) and the pack's shoulder transform are a second Carryable merged into Logging
/// Expanded's by Carry On (<c>patches/trunkentities-carryon.json</c>); Carry On's first-person
/// frame is turned into the shoulder frame that transform is made for while a trunk is carried
/// (<see cref="FirstPersonHandsPostfix"/>, <see cref="TrunkCarryPose"/>).
/// </summary>
public static class TrunkCarry
{
    public const string CarrySystemName = "CarryOn.CarrySystem";
    public const string HarmonyId = "seraphhorizons.trunkcarry";

    /// <summary>Carry On's first-person matrix for a carried block (client side).</summary>
    public const string FirstPersonTypeName = "CarryOn.Client.Logic.CarryRenderer.CarryFirstPersonTransform";

    private static readonly float[] FirstPersonAdjust = TrunkCarryPose.FirstPersonAdjust();

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
        public object? Back;
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
        player.Entity is { } entity && CarriedIn(entity) is { } carried && StackOf(carried, entity.World) is { } stack && Trunks.IsTrunk(stack) ? stack : null;

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
        var stack = Clean(Real(trunk, entity.World));
        stack.StackSize = 1;
        Hold(manager, entity, Shown(stack, entity.World));
        UpdateSpeed(entity);
        return true;
    }

    /// <summary>Puts <paramref name="shown"/> into Carry On's hands as it is.</summary>
    private static void Hold(object manager, Entity entity, ItemStack shown)
    {
        var m = _members!;
        // Carry On attaches a block to a cart only with block entity data, so the trunk gets some.
        var data = new TreeAttribute();
        data.SetString("blockCode", shown.Collectible.Code.ToShortString());
        data.SetString("type", "");
        var carried = m.NewCarried.Invoke([m.Hands, shown, data]);
        m.SetCarried.Invoke(manager, [entity, carried, null, true]);
    }

    // ---- the carried block ----

    /// <summary>The attribute of a carried trunk stack that holds the real trunk's block code
    /// (<see cref="Shown"/>).</summary>
    public const string RealCodeKey = "seraphhorizons:trunkCode";

    /// <summary>What Carry On carries for <paramref name="trunk"/>: the block of its class's model
    /// (<see cref="Trunks.ShownBlock"/>: <c>lg</c> for a thin trunk, <c>xxl</c> for a thick one,
    /// debarked when it is), so only those two models are ever seen in hand and the carry animation
    /// goes by class, with the trunk's own attributes (its logs) and its real block code under
    /// <see cref="RealCodeKey"/>. <see cref="Real"/> gives the trunk back. A copy of the trunk when
    /// it is its own shown block.</summary>
    public static ItemStack Shown(ItemStack trunk, IWorldAccessor world)
    {
        var real = Real(trunk, world);
        if (real.Collectible?.Code is not { } code || Trunks.ShownBlock(world, real) is not { } shown || shown.Code == code)
            return real;
        var stack = new ItemStack(shown, real.StackSize) { Attributes = real.Attributes.Clone() };
        stack.Attributes.SetString(RealCodeKey, code.ToShortString());
        return stack;
    }

    /// <summary>The real trunk behind a carried (<see cref="Shown"/>) stack: its block, its
    /// attributes less <see cref="RealCodeKey"/>; a copy of <paramref name="stack"/> when it is no
    /// shown stack (or its block is gone).</summary>
    public static ItemStack Real(ItemStack stack, IWorldAccessor world)
    {
        var copy = stack.Clone();
        if (copy.Attributes.GetString(RealCodeKey) is not { } code)
            return copy;
        copy.Attributes.RemoveAttribute(RealCodeKey);
        if (world.GetBlock(new AssetLocation(code)) is not { Id: > 0 } block)
            return copy;
        return new ItemStack(block, copy.StackSize) { Attributes = copy.Attributes };
    }

    /// <summary>Whether <see cref="TryGive"/> would take a trunk for <paramref name="player"/>:
    /// carrying available, nothing in Carry On's hands, both hands empty.</summary>
    public static bool CanGive(IPlayer player) =>
        player.Entity is { } entity && Manager(entity.Api) != null && CarriedIn(entity) == null && HandsEmpty(entity);

    /// <summary>Removes the trunk from <paramref name="player"/>'s Carry On hands and returns it;
    /// null when they hold none. Server side.</summary>
    public static ItemStack? Take(IServerPlayer player)
    {
        var entity = player.Entity;
        if (entity == null || entity.Api.Side != EnumAppSide.Server || Manager(entity.Api) is not { } manager
            || CarriedIn(entity) is not { } carried || StackOf(carried, entity.World) is not { } stack || !Trunks.IsTrunk(stack))
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
            Back = Enum.GetNames(slotType).Contains("Back") ? Enum.Parse(slotType, "Back") : null,
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
            // A trunk is Hands only: whatever puts one on a back (Carry On's swap key), it is laid down.
            // The interface is CarryOnLib's and the manager Carry On's, so every loaded assembly is
            // looked through for a class that implements it, and its own SetCarried is patched.
            var iface = m.SetCarried.DeclaringType!;
            var setters = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; } })
                .Where(t => t is { IsInterface: false, IsAbstract: false } && iface.IsAssignableFrom(t))
                .Select(t => t.GetInterfaceMap(iface) is var map && Array.IndexOf(map.InterfaceMethods, m.SetCarried) is var i && i >= 0 ? map.TargetMethods[i] : null)
                .Where(x => x != null).Distinct().ToList();
            foreach (var set in setters)
                _harmony.Patch(set, postfix: new HarmonyMethod(typeof(TrunkCarry), nameof(SetCarriedPostfix)));
            if (setters.Count == 0)
                api.Logger.Warning("[seraphhorizons] Trunk entities: Carry On's carry manager has no SetCarried, so a trunk put on a back is only laid down by the periodic check");
            else
                api.Logger.Notification("[seraphhorizons] Trunk entities: a trunk put on a back is laid down at once ({0} SetCarried patched)", setters.Count);
            // First person: Carry On's frame for the hands is its chest carry's, not the shoulder
            // frame the trunk's transform is made for (TrunkCarryPose).
            var firstPerson = AccessTools.TypeByName(FirstPersonTypeName) is { } fpType
                ? AccessTools.Method(fpType, "GetFirstPersonHandsMatrix", [typeof(EntityAgent), typeof(float[]), typeof(float), typeof(long)])
                : null;
            if (firstPerson != null && firstPerson.ReturnType == typeof(float[]))
                _harmony.Patch(firstPerson, postfix: new HarmonyMethod(typeof(TrunkCarry), nameof(FirstPersonHandsPostfix)));
            else
                api.Logger.Warning("[seraphhorizons] Trunk entities: Carry On's {0}.GetFirstPersonHandsMatrix is gone, so a carried trunk lies across the view in first person", FirstPersonTypeName);
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
        StripBackSlots(api);
        return count;
    }

    /// <summary>Removes the Back slot from every trunk's Carryable (Logging Expanded's own patch and
    /// ours give Hands only, but a merge or a config could add one): trunks are Hands only.
    /// Returns the trunks changed.</summary>
    internal static int StripBackSlots(ICoreAPI api)
    {
        if (Resolve(api) is not { Back: { } back })
            return 0;
        int count = 0;
        foreach (var block in api.World.Blocks)
        {
            if (block?.Code is not { Domain: TrunkEntitySystem.LeModId } code || !code.Path.StartsWith("treetrunk-", StringComparison.Ordinal))
                continue;
            foreach (var b in block.BlockBehaviors.Concat<CollectibleBehavior>(block.CollectibleBehaviors).Where(IsCarryable))
                if (AccessTools.Property(b.GetType(), "Slots")?.GetValue(b) is { } slots
                    && AccessTools.Method(slots.GetType(), "RemoveSlot") is { } remove && HasBack(slots, back))
                {
                    remove.Invoke(slots, [back]);
                    count++;
                }
        }
        return count;
    }

    private static bool HasBack(object slots, object back) =>
        AccessTools.Property(slots.GetType(), "SlotSettingsDict")?.GetValue(slots) is System.Collections.IDictionary dict && dict.Contains(back);

    /// <summary>Whether <paramref name="block"/>'s Carryable lists a Back slot (false without one).</summary>
    public static bool HasBackSlot(ICoreAPI api, Block block) =>
        Resolve(api) is { Back: { } back } && block.BlockBehaviors.Concat<CollectibleBehavior>(block.CollectibleBehaviors).Where(IsCarryable)
            .Any(b => AccessTools.Property(b.GetType(), "Slots")?.GetValue(b) is { } slots && HasBack(slots, back));

    /// <summary>The stack in <paramref name="entity"/>'s Carry On hands as Carry On holds it (for a
    /// trunk, the block shown, <see cref="Shown"/>), or null. For diagnostics.</summary>
    public static ItemStack? InHandsAsHeld(Entity entity) =>
        CarriedIn(entity) is { } carried && _members!.Stack.DeclaringType!.IsInstanceOfType(carried) ? _members.Stack.GetValue(carried) as ItemStack : null;

    /// <summary>The stack <paramref name="entity"/> carries on its back, or null.</summary>
    public static ItemStack? OnBack(Entity entity) =>
        Manager(entity.Api) is { } manager && _members!.Back is { } back
            ? StackOf(_members.GetCarried.Invoke(manager, [entity, back]), entity.World) : null;

    /// <summary>Lays a trunk found on <paramref name="entity"/>'s back down at its feet as a trunk
    /// entity (old saves, or anything that got past the Hands-only rule). Server side.</summary>
    public static bool EvictFromBack(Entity entity)
    {
        if (entity.Api is not { Side: EnumAppSide.Server } || OnBack(entity) is not { } stack || !Trunks.IsTrunk(stack)
            || Manager(entity.Api) is not { } manager || !Lay(entity, stack, entity.Pos.AsBlockPos, along: false))
            return false;
        _members!.RemoveCarried.Invoke(manager, [entity, _members.Back!, true]);
        return true;
    }

    private static void SetCarriedPostfix(Entity __0)
    {
        if (__0 == null)
            return;
        EvictFromBack(__0);
        ShowInHands(__0);
    }

    /// <summary>Swaps a trunk Carry On put into <paramref name="entity"/>'s hands as its own block
    /// (from a cart slot filled before, or anything else that did not go through
    /// <see cref="TryGive"/>) for its <see cref="Shown"/> stack. Server side; true when swapped.</summary>
    public static bool ShowInHands(Entity entity)
    {
        if (entity.Api is not { Side: EnumAppSide.Server } || Manager(entity.Api) is not { } manager
            || CarriedIn(entity) is not { } carried || _members!.Stack.GetValue(carried) is not ItemStack raw
            || !Trunks.IsTrunk(raw) || raw.Attributes.HasAttribute(RealCodeKey))
            return false;
        var shown = Shown(Clean(raw.Clone()), entity.World);
        if (!shown.Attributes.HasAttribute(RealCodeKey))
            return false;   // the trunk is its own shown block
        Hold(manager, entity, shown);
        return true;
    }

    /// <summary>Carry On's pick-up hold for <paramref name="block"/>, in seconds: its Carryable's
    /// <c>InteractDelay</c> (Carry On's default 0.8 s when unreadable) over Carry On's configured
    /// <c>InteractSpeedMultiplier</c> when one can be found.</summary>
    public static float PickUpSeconds(ICoreAPI api, Block? block)
    {
        float delay = 0.8f;
        var carryable = block == null ? null : block.BlockBehaviors.Concat<CollectibleBehavior>(block.CollectibleBehaviors).FirstOrDefault(IsCarryable);
        if (carryable != null && AccessTools.Property(carryable.GetType(), "InteractDelay")?.GetValue(carryable) is float d && d >= 0)
            delay = d;
        if (api.ModLoader.GetModSystem(CarrySystemName) is { } system && Multiplier(system, 3) is float k && k > 0)
            delay /= k;
        return delay;
    }

    private static float? Multiplier(object owner, int depth)
    {
        foreach (var prop in owner.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0)
                continue;
            object? value;
            try { value = prop.GetValue(owner); } catch { continue; }
            if (prop.Name == "InteractSpeedMultiplier" && value is float k)
                return k;
            if (depth > 1 && value != null && !prop.PropertyType.IsPrimitive && prop.PropertyType != typeof(string)
                && prop.PropertyType.Namespace?.StartsWith("CarryOn", StringComparison.Ordinal) == true && Multiplier(value, depth - 1) is float found)
                return found;
        }
        return null;
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
            {
                EvictFromBack(entity);
                UpdateSpeed(entity);
            }
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
        var trunk = CarriedIn(entity) is { } carried && StackOf(carried, entity.World) is { } stack && Trunks.IsTrunk(stack) ? stack : null;
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
        if (__0?.Api is not { } api || __2?.Position == null || StackOf(__1, __0.World) is not { } stack || !Trunks.IsTrunk(stack)
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

    // CarryFirstPersonTransform.GetFirstPersonHandsMatrix(entity, viewMat, deltaTime, renderTick):
    // Carry On's frame for the hands in first person (not immersive), a fresh array each call,
    // which the block's hands transform is then applied in. For a carried trunk it becomes the
    // shoulder frame of third person (TrunkCarryPose), so its transform lays it front to back there
    // too instead of on end.
    private static void FirstPersonHandsPostfix(EntityAgent __0, float[] __result)
    {
        if (__result is not { Length: 16 } || __0?.World == null || CarriedIn(__0) is not { } carried
            || StackOf(carried, __0.World) is not { } stack || !Trunks.IsTrunk(stack))
            return;
        Mat4f.Mul(__result, __result, FirstPersonAdjust);
    }

    // CarryDropService.DropCarriedBlock(entity, carriedBlock, range, blockPlacer): death, damage,
    // quick drop. A trunk lies down where the carrier stands, never a block nor an item.
    private static bool DropCarriedPrefix(Entity __0, object __1) => !DropHere(__0, __1);

    // CarryDropService.DropBlockAsEntityOrItem(carriedBlock, centerBlock, player, entity, forceEntity).
    private static bool DropAsEntityPrefix(object __0, Entity __3) => !DropHere(__3, __0);

    private static bool DropHere(Entity? carrier, object? carried)
    {
        if (carrier?.Api is not { Side: EnumAppSide.Server } api || StackOf(carried, carrier.World) is not { } stack || !Trunks.IsTrunk(stack)
            || Manager(api) is not { } manager)
            return false;
        var m = _members!;
        // Not laid (it warns): Carry On's own drop goes on, so the trunk is never simply lost.
        if (!Lay(carrier, stack, carrier.Pos.AsBlockPos, along: false))
            return false;
        m.RemoveCarried.Invoke(manager, [carrier, m.Slot.GetValue(carried), true]);
        if (carrier is EntityAgent agent)
            UpdateSpeed(agent);
        return true;
    }

    // EntityBehaviorAttachable.TryRemoveAttachment(byEntity, selectionBoxIndex), the game's
    // empty-hand take from a cart slot: a survival player is never given a trunk, so it goes to the hands.
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

    /// <summary>The real trunk (<see cref="Real"/>) of a carried block, or its stack when it is no
    /// trunk; null for no carried block.</summary>
    private static ItemStack? StackOf(object? carried, IWorldAccessor world)
    {
        if (carried == null || _members == null || !_members.Stack.DeclaringType!.IsInstanceOfType(carried)
            || _members.Stack.GetValue(carried) is not ItemStack stack)
            return null;
        return Trunks.IsTrunk(stack) ? Real(stack, world) : stack;
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
        if (!TrunkCarry.HandsEmpty(byEntity) || TrunkCarry.Carried(player) != null)
        {
            TrunkCarry.HandsFull(player);
            return;
        }
        if (_holder != null)
            return;
        // Carry On's pick-up hold, timed here: the button held, sneaking, hands empty, near the trunk.
        _holder = player;
        _heldMs = 0;
        _needMs = (long)(TrunkCarry.PickUpSeconds(entity.Api, stack.Block) * 1000);
        _listener = entity.World.RegisterGameTickListener(Tick, HoldTickMs);
    }

    /// <summary>How often the hold is checked, ms.</summary>
    public const int HoldTickMs = 100;
    private IServerPlayer? _holder;
    private long _heldMs, _needMs, _listener;

    /// <summary>Whether a pick-up hold is under way on this trunk.</summary>
    public bool Holding => _holder != null;

    private void Tick(float dt)
    {
        var player = _holder;
        var by = player?.Entity;
        if (player == null || by == null || entity is not EntityTrunk { Alive: true } trunk || trunk.Trunk is not { } stack
            || !by.ServerControls.RightMouseDown
            || !(by.Controls.ShiftKey || by.Controls.Sneak || by.ServerControls.ShiftKey || by.ServerControls.Sneak) || !TrunkCarry.HandsEmpty(by)
            || by.Pos.DistanceTo(entity.Pos) > 6 || player.CurrentEntitySelection?.Entity is { } looked && looked != entity)
        {
            Cancel();
            return;
        }
        _heldMs += (long)(dt * 1000);
        if (_heldMs < _needMs)
            return;
        Cancel();
        if (!TrunkCarry.TryGive(player, stack))
        {
            TrunkCarry.HandsFull(player);
            return;
        }
        Finish(player, trunk, stack);
    }

    private void Cancel()
    {
        if (_listener != 0)
            entity.World.UnregisterGameTickListener(_listener);
        _listener = 0;
        _holder = null;
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        Cancel();
        base.OnEntityDespawn(despawn);
    }

    private void Finish(IServerPlayer player, EntityTrunk trunk, ItemStack stack)
    {
        entity.World.PlaySoundAt(stack.Block?.Sounds?.Place ?? GlobalConstants.DefaultBuildSound, entity, player);
        trunk.Die(EnumDespawnReason.Removed);
    }
}
