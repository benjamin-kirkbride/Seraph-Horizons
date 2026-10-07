using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// Logging Expanded's trunk stations with trunk entities: its sawhorses (<c>BlockWorkstation</c>),
/// Trunk Storage Rack (<c>BlockTrunkStorage</c>) and heating rack (<c>BlockResinRack</c>) load a
/// trunk carried through Carry On and unload one into Carry On's hands, as they loaded one from the
/// hand or inventory and gave one back before. A Harmony prefix on each block's
/// <c>OnBlockInteractStart</c>, found by name:
/// <list type="bullet">
/// <item>Carrying a trunk: the station takes it if it can (sawhorse: empty, and debranched while
/// Logging Expanded's <c>RequireBranchRemovalForProcessing</c> holds, with its message; rack: fewer
/// than 4; heating rack: empty, and debranched as its own empty-hand load asks), with its sound.
/// One that cannot does nothing, so a carried trunk never makes the station unload onto the ground.</item>
/// <item>Hands and Carry On's hands empty, on a station whose original would give a trunk to the
/// inventory: the right button held starts a hold, timed as a trunk entity's pick-up
/// (<see cref="StationTake"/>), and at its end the trunk goes into Carry On's hands
/// (<see cref="TrunkCarry.TryGive"/>); hands full, the hands-full error and it stays. A sawhorse
/// loaded from vanilla logs or firewood gives those back at once, as Logging Expanded does.</item>
/// <item>Everything else (a tool, a log, a knife on the heating rack, Carry On's own sneak click)
/// is the original's.</item>
/// </list>
/// Patched on both sides while trunk entities run with Carry On (<see cref="Hands"/>); the client
/// only says a carried trunk's click is the station's, so Carry On sends it on (it lets a click
/// through while carrying only on blocks with its <c>CarryableInteract</c> behaviour, which
/// <c>patches/trunkentities-stations.json</c> adds). The rosser and bucking mill do the same in
/// their own block entities with <see cref="Carried"/> and <see cref="GiveToHands"/>.
/// </summary>
public static class TrunkStations
{
    private static readonly AssetLocation WoodSound = new("game", "sounds/block/wood");

    private static Type? _workstation, _rackBlock, _resinRack;
    private static PropertyInfo? _wsInventory;
    private static MethodInfo? _wsInvalidateMesh, _wsBuildUnload, _invClear, _canStoreTrunk, _resinStore, _resinRetrieve;
    private static PropertyInfo? _resinIsEmpty;
    private static FieldInfo? _resinTrunk;

    /// <summary>Whether trunks go through Carry On's hands on this side: trunk entities run and
    /// Carry On is there.</summary>
    public static bool Hands(ICoreAPI api) => TrunkEntitySystem.Of(api).Enabled && TrunkCarry.Available(api);

    /// <summary>The trunk <paramref name="player"/> carries in Carry On's hands, or null (also
    /// when something else is carried, or trunks do not go through hands here).</summary>
    public static ItemStack? Carried(ICoreAPI api, IPlayer player) =>
        Hands(api) && TrunkCarry.Carried(player) is { } stack && Trunks.IsTrunk(stack) ? stack : null;

    /// <summary>Puts <paramref name="trunk"/> into the player's Carry On hands (server side);
    /// false, with the hands-full error, when they are full: the trunk then stays with the caller.</summary>
    public static bool GiveToHands(IServerPlayer player, ItemStack trunk)
    {
        if (TrunkCarry.TryGive(player, trunk))
            return true;
        TrunkCarry.HandsFull(player);
        return false;
    }

    /// <summary>
    /// The first live trunk entity lying in one of <paramref name="cells"/> whose stack
    /// <paramref name="accepts"/>: its middle's column, at the height of its underside (a trunk
    /// resting on the floor of a cell is in that cell, however long it is). For a machine's infeed
    /// cells (server side).
    /// </summary>
    public static EntityTrunk? FindInCells(IWorldAccessor world, IReadOnlyList<BlockPos> cells, System.Func<ItemStack, bool> accepts)
    {
        if (cells.Count == 0)
            return null;
        var centre = new Vec3d(cells.Average(c => c.X) + 0.5, cells.Average(c => c.Y) + 0.5, cells.Average(c => c.Z) + 0.5);
        double reach = cells.Max(c => Math.Max(Math.Abs(c.X + 0.5 - centre.X), Math.Abs(c.Z + 0.5 - centre.Z))) + 1;
        foreach (var entity in world.GetEntitiesAround(centre, (float)reach, 2, e => e is EntityTrunk { Alive: true }))
        {
            var trunk = (EntityTrunk)entity;
            int x = (int)Math.Floor(trunk.Pos.X), y = (int)Math.Floor(trunk.Pos.Y + 0.5), z = (int)Math.Floor(trunk.Pos.Z);
            if (cells.Any(c => c.X == x && c.Y == y && c.Z == z && c.dimension == trunk.Pos.Dimension)
                && trunk.Trunk is { } stack && accepts(stack))
                return trunk;
        }
        return null;
    }

    /// <summary>Takes the trunk entity's stack and removes the entity, nothing dropped.</summary>
    public static ItemStack? TakeEntity(EntityTrunk trunk)
    {
        if (!trunk.Alive || trunk.Trunk?.Clone() is not { } stack)
            return null;
        trunk.Die(EnumDespawnReason.Removed);
        return stack;
    }

    /// <summary>A machine giving a trunk back with no hands to give it to (trunk entities without
    /// Carry On): it lies two cells beyond <paramref name="cells"/> (a machine's infeed cells) in
    /// the <paramref name="outward"/> direction, across the machine's line, so the machine does not
    /// take it straight back.</summary>
    public static EntityTrunk? DropBeyond(IWorldAccessor world, ItemStack trunk, IReadOnlyList<BlockPos> cells, double outwardX, double outwardZ)
    {
        var at = cells.Count == 0
            ? null
            : new Vec3d(cells.Average(c => c.X) + 0.5 + 2 * outwardX, cells.Min(c => c.Y), cells.Average(c => c.Z) + 0.5 + 2 * outwardZ);
        if (at == null)
            return null;
        float yaw = (float)(Math.Atan2(outwardX, outwardZ) + Math.PI / 2);
        return TrunkSpawns.Spawn(world, trunk, at, yaw, cells[0].dimension);
    }

    /// <summary>Patches the three stations it finds; one warning naming what is missing. Returns
    /// how many were patched.</summary>
    public static int Patch(Harmony harmony, ICoreAPI api)
    {
        var assembly = api.ModLoader.GetMod(TrunkEntitySystem.LeModId)?.Systems.FirstOrDefault()?.GetType().Assembly;
        var problems = new List<string>();
        int patched = 0;
        if (assembly == null)
        {
            api.Logger.Warning("[seraphhorizons] Trunk entities: Logging Expanded is not loaded, so its stations take no carried trunks");
            return 0;
        }
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        Type? TypeOf(string name)
        {
            var type = assembly.GetType(name);
            if (type == null)
                problems.Add($"type {name} not found");
            return type;
        }
        MethodInfo? Interact(Type? type)
        {
            var method = type?.GetMethod(nameof(Block.OnBlockInteractStart), Any | BindingFlags.DeclaredOnly,
                [typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection)]);
            if (type != null && method == null)
                problems.Add($"{type.Name}.OnBlockInteractStart not found");
            return method;
        }
        T? Need<T>(T? member, string what) where T : class
        {
            if (member == null)
                problems.Add(what + " not found");
            return member;
        }

        // the sawhorses
        _workstation = TypeOf("LoggingMod.BlockWorkstation");
        var wsEntity = TypeOf("LoggingMod.BEWorkstation");
        var inventory = TypeOf("LoggingMod.TreeTrunkInventory");
        int before = problems.Count;
        var wsInteract = Interact(_workstation);
        _wsInventory = Need(wsEntity?.GetProperty("Inventory", Any), "BEWorkstation.Inventory");
        _wsInvalidateMesh = Need(wsEntity?.GetMethod("InvalidateMesh", Any, Type.EmptyTypes), "BEWorkstation.InvalidateMesh()");
        _wsBuildUnload = Need(wsEntity?.GetMethod("BuildUnloadStack", Any, [typeof(IWorldAccessor)]), "BEWorkstation.BuildUnloadStack(IWorldAccessor)");
        _invClear = Need(inventory?.GetMethod("Clear", Any | BindingFlags.DeclaredOnly, Type.EmptyTypes), "TreeTrunkInventory.Clear()");
        if (problems.Count == before && wsInteract != null && typeof(InventoryBase).IsAssignableFrom(inventory))
        {
            harmony.Patch(wsInteract, prefix: new HarmonyMethod(typeof(TrunkStations), nameof(WorkstationPrefix)));
            patched++;
        }
        else
            _workstation = null;

        // the Trunk Storage Rack (its block entity through LoggingBridge)
        _rackBlock = TypeOf("LoggingMod.BlockTrunkStorage");
        before = problems.Count;
        var rackInteract = Interact(_rackBlock);
        _canStoreTrunk = Need(_rackBlock?.GetMethod("CanStoreTrunk", Any, [typeof(ItemStack)]), "BlockTrunkStorage.CanStoreTrunk(ItemStack)");
        if (problems.Count == before && rackInteract != null)
        {
            harmony.Patch(rackInteract, prefix: new HarmonyMethod(typeof(TrunkStations), nameof(RackPrefix)));
            patched++;
        }
        else
            _rackBlock = null;

        // the heating rack
        var resinBlock = TypeOf("LoggingMod.BlockResinRack");
        _resinRack = TypeOf("LoggingMod.BEResinRack");
        before = problems.Count;
        var resinInteract = Interact(resinBlock);
        _resinIsEmpty = Need(_resinRack?.GetProperty("IsEmpty", Any), "BEResinRack.IsEmpty");
        _resinStore = Need(_resinRack?.GetMethod("TryStoreTrunk", Any, [typeof(ItemStack), typeof(IPlayer)]), "BEResinRack.TryStoreTrunk(ItemStack, IPlayer)");
        _resinRetrieve = Need(_resinRack?.GetMethod("TryRetrieveTrunk", Any, Type.EmptyTypes), "BEResinRack.TryRetrieveTrunk()");
        _resinTrunk = Need(_resinRack?.GetField("_trunk", Any), "BEResinRack._trunk");
        if (problems.Count == before && resinInteract != null)
        {
            harmony.Patch(resinInteract, prefix: new HarmonyMethod(typeof(TrunkStations), nameof(ResinRackPrefix)));
            patched++;
        }
        else
            _resinRack = null;

        if (problems.Count > 0)
            api.Logger.Warning("[seraphhorizons] Trunk entities: Logging Expanded's stations are not as expected, so some take no carried trunks: {0}",
                string.Join("; ", problems));
        return patched;
    }

    private enum Station { Workstation, Rack, ResinRack }

    private static bool WorkstationPrefix(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref bool __result) =>
        Handle(Station.Workstation, null, world, byPlayer, blockSel, ref __result);

    private static bool RackPrefix(Block __instance, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref bool __result) =>
        Handle(Station.Rack, __instance, world, byPlayer, blockSel, ref __result);

    private static bool ResinRackPrefix(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref bool __result) =>
        Handle(Station.ResinRack, null, world, byPlayer, blockSel, ref __result);

    // Returns whether the original runs.
    private static bool Handle(Station station, Block? block, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
    {
        if (byPlayer?.Entity == null || blockSel?.Position == null || !Hands(world.Api))
            return true;
        var be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
        var logging = TrunkEntitySystem.Of(world.Api).Logging;
        if (StationOf(be, logging) != station)
            return true;

        if (Carried(world.Api, byPlayer) is { } carried)
        {
            __result = true;
            if (world.Side == EnumAppSide.Server && byPlayer is IServerPlayer sp)
                Load(station, block, be!, logging!, carried, sp, blockSel.Position);
            return false;
        }

        // Unloading: empty hands, nothing carried; the client's original says the click is the station's.
        if (world.Side != EnumAppSide.Server || byPlayer is not IServerPlayer player
            || byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack != null || TrunkCarry.Carried(byPlayer) != null)
            return true;
        // A trunk is taken after a hold, as one off the ground is (StationTake); logs, firewood or
        // nothing are Logging Expanded's.
        if (Offer(world, blockSel.Position) is not { } offer)
            return true;
        if (!TrunkCarry.CanGive(player))
            TrunkCarry.HandsFull(player);
        else
            StationTake.Start(player, blockSel.Position.Copy(), TrunkCarry.PickUpSeconds(world.Api, offer.Block));
        __result = true;
        return false;
    }

    /// <summary>Which of the three stations <paramref name="be"/> is, or null.</summary>
    private static Station? StationOf(BlockEntity? be, LoggingBridge? logging) =>
        be == null ? null
        : _workstation != null && _wsInventory?.DeclaringType?.IsInstanceOfType(be) == true ? Station.Workstation
        : _rackBlock != null && logging?.IsRack(be) == true ? Station.Rack
        : _resinRack?.IsInstanceOfType(be) == true ? Station.ResinRack
        : null;

    /// <summary>
    /// The trunk an empty hand would take off the station at <paramref name="pos"/> into Carry
    /// On's hands, without taking it, or null: a sawhorse's trunk (not its logs or firewood), a
    /// Trunk Storage Rack's top trunk, a heating rack's trunk. Either side: the client's circle
    /// (<see cref="TrunkHoldCircle"/>) asks it too, for the hold's length.
    /// </summary>
    public static ItemStack? Offer(IWorldAccessor world, BlockPos pos)
    {
        if (!Hands(world.Api))
            return null;
        var be = world.BlockAccessor.GetBlockEntity(pos);
        var logging = TrunkEntitySystem.Of(world.Api).Logging;
        ItemStack? trunk = StationOf(be, logging) switch
        {
            Station.Workstation => _wsBuildUnload!.Invoke(be, [world]) as ItemStack,
            Station.Rack => logging!.TrunkCount(be!) > 0 ? logging.PeekTrunk(be!) : null,
            Station.ResinRack => _resinTrunk!.GetValue(be) as ItemStack,
            _ => null,
        };
        trunk?.ResolveBlockOrItem(world);
        return trunk != null && Trunks.IsTrunk(trunk) ? trunk : null;
    }

    /// <summary>Takes the station's trunk at <paramref name="pos"/> into the player's Carry On
    /// hands (server side), with its sound: the end of a <see cref="StationTake"/> hold. Hands
    /// full, the hands-full error and the station keeps it. False when nothing was taken.</summary>
    public static bool TakeInto(IServerPlayer player, BlockPos pos)
    {
        var world = player.Entity?.World;
        if (world == null || Offer(world, pos) == null)
            return false;
        var be = world.BlockAccessor.GetBlockEntity(pos)!;
        var logging = TrunkEntitySystem.Of(world.Api).Logging;
        switch (StationOf(be, logging))
        {
            case Station.Workstation:
                if (_wsBuildUnload!.Invoke(be, [world]) is not ItemStack unload || !GiveToHands(player, unload))
                    return false;
                _invClear!.Invoke(_wsInventory!.GetValue(be), null);
                _wsInvalidateMesh!.Invoke(be, null);
                be.MarkDirty(true);
                break;
            case Station.Rack:
                if (logging!.PeekTrunk(be) is not { } top || !GiveToHands(player, top))
                    return false;
                logging.PopTrunk(be);
                be.MarkDirty(true);
                break;
            case Station.ResinRack:
                if (_resinRetrieve!.Invoke(be, null) is not ItemStack trunk)
                    return false;
                if (!GiveToHands(player, trunk))
                {
                    _resinStore!.Invoke(be, [trunk, player]);   // back as it was: the retrieve wrote its state into the stack
                    return false;
                }
                break;
            default:
                return false;
        }
        Sound(world, pos, player);
        return true;
    }

    private static void Load(Station station, Block? block, BlockEntity be, LoggingBridge logging, ItemStack carried, IServerPlayer player, BlockPos pos)
    {
        bool branchedRefused = Trunks.IsBranched(carried) && logging.RequireBranchRemoval;
        bool takes = station switch
        {
            Station.Workstation => ((InventoryBase)_wsInventory!.GetValue(be)!).Empty && !branchedRefused,
            Station.Rack => logging.TrunkCount(be) < 4 && (bool)_canStoreTrunk!.Invoke(block, [carried])!,
            _ => (bool)_resinIsEmpty!.GetValue(be)! && !branchedRefused,
        };
        if (!takes)
        {
            if (branchedRefused && station != Station.Rack)
                player.SendIngameError("", Lang.GetL(player.LanguageCode, "loggingmod:treetrunk-branches-first"));
            return;
        }
        if (TrunkCarry.Take(player) is not { } trunk)
            return;
        switch (station)
        {
            case Station.Workstation:
                ((InventoryBase)_wsInventory!.GetValue(be)!).FromTreeAttributes(trunk.Attributes);
                _wsInvalidateMesh!.Invoke(be, null);
                be.MarkDirty(true);
                break;
            case Station.Rack:
                logging.PushTrunk(be, trunk);
                be.MarkDirty(true);
                break;
            default:
                if (!(bool)_resinStore!.Invoke(be, [trunk, player])!)
                {
                    // cannot happen (it was empty); never lose the trunk
                    TrunkCarry.TryGive(player, trunk);
                    return;
                }
                break;
        }
        Sound(world: be.Api.World, pos, player);
    }

    private static void Sound(IWorldAccessor world, BlockPos pos, IPlayer player) =>
        world.PlaySoundAt(WoodSound, pos, 0, player, true, 16f, 0.75f);
}

/// <summary>
/// Server side: taking a trunk off one of Logging Expanded's stations (a sawhorse, the Trunk
/// Storage Rack, the heating rack) into Carry On's hands with an empty hand is a hold, timed as a
/// trunk entity's pick-up (<see cref="TrunkCarry.PickUpSeconds"/> of the trunk taken): the right
/// button held, both hands empty, nothing carried, within reach, not looking at another block, the
/// station still holding a trunk. Let go early and the trunk stays. At the end the station's take
/// (<see cref="TrunkStations.TakeInto"/>) runs. The client shows the same circle
/// (<see cref="TrunkHoldCircle"/>). One hold per player; a new click starts it over.
/// </summary>
public static class StationTake
{
    /// <summary>How often a hold is checked, ms.</summary>
    public const int TickMs = 100;

    private sealed class Hold
    {
        public required IServerPlayer Player;
        public required BlockPos Pos;
        public required System.Func<bool> Has;
        public required System.Action<IServerPlayer> Take;
        public long NeedMs, HeldMs, Listener;
    }

    private static readonly Dictionary<string, Hold> Holds = new();

    /// <summary>Whether <paramref name="player"/> is holding to take a trunk off a station.</summary>
    public static bool Holding(IPlayer player) => Holds.ContainsKey(player.PlayerUID);

    /// <summary>Starts a hold on the Logging Expanded station at <paramref name="pos"/>.</summary>
    public static void Start(IServerPlayer player, BlockPos pos, float seconds)
    {
        if (player.Entity?.World is not { } world)
            return;
        Start(player, pos, seconds, () => TrunkStations.Offer(world, pos) != null, p => TrunkStations.TakeInto(p, pos));
    }

    /// <summary>Starts a hold on whatever is at <paramref name="pos"/>: <paramref name="has"/>
    /// says whether it still has something to take (else the hold ends), <paramref name="take"/>
    /// takes it at the end.</summary>
    public static void Start(IServerPlayer player, BlockPos pos, float seconds, System.Func<bool> has, System.Action<IServerPlayer> take)
    {
        if (player.Entity?.World is not { } world)
            return;
        if (Holds.TryGetValue(player.PlayerUID, out var old) && old.Pos == pos)
            return;   // already holding here
        Stop(player.PlayerUID);
        var hold = new Hold { Player = player, Pos = pos, Has = has, Take = take, NeedMs = (long)(Math.Max(0, seconds) * 1000) };
        hold.Listener = world.RegisterGameTickListener(dt => Tick(hold, dt), TickMs);
        Holds[player.PlayerUID] = hold;
    }

    private static void Stop(string uid)
    {
        if (Holds.Remove(uid, out var hold) && hold.Listener != 0)
            hold.Player.Entity?.World.UnregisterGameTickListener(hold.Listener);
    }

    private static void Tick(Hold hold, float dt)
    {
        var player = hold.Player;
        var by = player.Entity;
        if (by?.World == null || !hold.Has()
            || !by.ServerControls.RightMouseDown || !TrunkCarry.CanGive(player)
            || by.Pos.DistanceTo(hold.Pos.ToVec3d().Add(0.5, 0.5, 0.5)) > TrunkHoldCircle.Reach
            || player.CurrentBlockSelection?.Position is { } looked && looked != hold.Pos)
        {
            Stop(player.PlayerUID);
            return;
        }
        hold.HeldMs += (long)(dt * 1000);
        if (hold.HeldMs < hold.NeedMs)
            return;
        Stop(player.PlayerUID);
        hold.Take(player);
    }
}
