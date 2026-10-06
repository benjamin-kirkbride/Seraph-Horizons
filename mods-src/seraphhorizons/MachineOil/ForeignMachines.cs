using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.MachineOil;

/// <summary>
/// Oil for the machines this mod does not own: the game's helve hammer and pulverizer, and
/// Immersive Woodworking's (<c>immersivewoodworking</c>) plank sawmill and powered chopper. Their
/// block entities cannot carry a field of this mod's, so the tank is kept beside each one (a weak
/// table) and saved into, and read from, its tree under <see cref="Oil.TreeKey"/> by postfixes on
/// its <c>ToTreeAttributes</c> and <c>FromTreeAttributes</c>, which is also how it reaches clients.
/// Per machine, Harmony patches:
/// <list type="bullet">
/// <item>the load: a postfix on its mechanical power behavior's <c>GetResistance</c> multiplies it
/// while the tank is dry (for the helve hammer, the wooden toggle's, which carries the hammer's load);</item>
/// <item>the drain: the job's method (the hammer's <c>onEvery25ms</c>, where it strikes the anvil;
/// the pulverizer's <c>Crush</c>, one item; the sawmill's and the chopper's <c>CompletePass</c>, one
/// log), on the server;</item>
/// <item>pouring: a prefix on the <c>OnBlockInteractStart</c> of each of its blocks, the multiblock
/// cells' too, which takes the click when the player holds oil;</item>
/// <item>the block info: a postfix on its <c>GetBlockInfo</c> (the cells pass theirs to the
/// machine's; the helve hammer has none of its own, so the base one's, for it alone).</item>
/// </list>
/// Immersive Woodworking is not referenced at build time: its types and members are found by name
/// (<see cref="Bind"/>), and if one is missing its two machines are left alone with a warning. Every
/// target is listed in <see cref="Targets"/>, which a pack test holds to the locked versions.
/// Patched on both sides, once per process (a singleplayer game runs both in one): the client
/// predicts the click and shows the info, the server pours, drains and loads the shaft.
/// </summary>
public static class ForeignMachines
{
    public const string IwModId = "immersivewoodworking";
    private const string Iw = "ImmersiveWoodworking.";

    private static readonly ConditionalWeakTable<BlockEntity, OilState> States = new();
    private static readonly List<WeakReference<BlockEntity>> ClientMachines = [];
    private static readonly Dictionary<Type, OilMachine> Machines = [];

    private static MachineOilConfig _config = new();
    private static OilCodes _liquids = new([]);

    // Immersive Woodworking, found by name
    private static Type? _sawmill, _chopper, _sawmillCell, _chopperCell;
    private static PropertyInfo? _sawmillPrincipal, _chopperPrincipal;
    private static MethodInfo? _sawmillMaster, _chopperMaster;
    private static PropertyInfo? _sawmillInput, _chopperInput, _sawmillSpeed, _chopperSpeed, _sawmillComplete, _chopperComplete;

    private static readonly AccessTools.FieldRef<BEHelveHammer, float> AccumHits = AccessTools.FieldRefAccess<BEHelveHammer, float>("accumHits");
    private static readonly AccessTools.FieldRef<BEHelveHammer, BlockEntityAnvil> TargetAnvil = AccessTools.FieldRefAccess<BEHelveHammer, BlockEntityAnvil>("targetAnvil");
    private static readonly AccessTools.FieldRef<BEHelveHammer, BEBehaviorMPToggle> HammerToggle = AccessTools.FieldRefAccess<BEHelveHammer, BEBehaviorMPToggle>("mptoggle");
    private static readonly AccessTools.FieldRef<BEBehaviorMPToggle, BlockPos[]> ToggleSides = AccessTools.FieldRefAccess<BEBehaviorMPToggle, BlockPos[]>("sides");

    /// <summary>A method this tweak patches, how, and whether it was found.</summary>
    public sealed record Target(string Name, OilMachine Machine, MethodBase? Method, string Prefix, string Postfix);

    private static List<Target> _targets = [];

    /// <summary>Every patch target of the bound machines (the game's two always, Immersive
    /// Woodworking's when it is installed), with <see cref="Target.Method"/> null where one was not
    /// found.</summary>
    public static IReadOnlyList<Target> Targets => _targets;

    /// <summary>Whether Immersive Woodworking's two machines are oiled.</summary>
    public static bool WoodworkingBound { get; private set; }

    /// <summary>Finds the targets. Logs a warning naming what is missing; the game's machines are
    /// patched regardless, Immersive Woodworking's only when all of theirs were found.</summary>
    public static void Bind(ICoreAPI api, MachineOilConfig config, OilCodes liquids)
    {
        _config = config;
        _liquids = liquids;
        Machines.Clear();
        Machines[typeof(BEHelveHammer)] = OilMachine.HelveHammer;
        Machines[typeof(BEPulverizer)] = OilMachine.Pulverizer;
        var targets = new List<Target>
        {
            T("BEHelveHammer.ToTreeAttributes", OilMachine.HelveHammer, typeof(BEHelveHammer), nameof(BlockEntity.ToTreeAttributes), null, nameof(ToTreePostfix)),
            T("BEHelveHammer.FromTreeAttributes", OilMachine.HelveHammer, typeof(BEHelveHammer), nameof(BlockEntity.FromTreeAttributes), null, nameof(FromTreePostfix)),
            T("BlockEntity.GetBlockInfo (helve hammer)", OilMachine.HelveHammer, typeof(BlockEntity), nameof(BlockEntity.GetBlockInfo), null, nameof(HelveInfoPostfix)),
            T("BEHelveHammer.onEvery25ms", OilMachine.HelveHammer, typeof(BEHelveHammer), "onEvery25ms", nameof(StrikePrefix), nameof(StrikePostfix)),
            T("BEBehaviorMPToggle.GetResistance", OilMachine.HelveHammer, typeof(BEBehaviorMPToggle), nameof(BEBehaviorMPBase.GetResistance), null, nameof(ToggleResistancePostfix)),
            T("BlockHelveHammer.OnBlockInteractStart", OilMachine.HelveHammer, typeof(BlockHelveHammer), nameof(Block.OnBlockInteractStart), nameof(InteractPrefix), ""),

            T("BEPulverizer.ToTreeAttributes", OilMachine.Pulverizer, typeof(BEPulverizer), nameof(BlockEntity.ToTreeAttributes), null, nameof(ToTreePostfix)),
            T("BEPulverizer.FromTreeAttributes", OilMachine.Pulverizer, typeof(BEPulverizer), nameof(BlockEntity.FromTreeAttributes), null, nameof(FromTreePostfix)),
            T("BEPulverizer.GetBlockInfo", OilMachine.Pulverizer, typeof(BEPulverizer), nameof(BlockEntity.GetBlockInfo), null, nameof(InfoPostfix)),
            T("BEPulverizer.Crush", OilMachine.Pulverizer, typeof(BEPulverizer), "Crush", null, nameof(CrushPostfix)),
            T("BEBehaviorMPPulverizer.GetResistance", OilMachine.Pulverizer, typeof(BEBehaviorMPPulverizer), nameof(BEBehaviorMPBase.GetResistance), null, nameof(BehaviorResistancePostfix)),
            T("BlockPulverizer.OnBlockInteractStart", OilMachine.Pulverizer, typeof(BlockPulverizer), nameof(Block.OnBlockInteractStart), nameof(InteractPrefix), ""),
            T("BlockMPMultiblockPulverizer.OnBlockInteractStart", OilMachine.Pulverizer, typeof(BlockMPMultiblockPulverizer), nameof(Block.OnBlockInteractStart), nameof(InteractPrefix), ""),
        };

        WoodworkingBound = false;
        if (api.ModLoader.IsModEnabled(IwModId))
        {
            _sawmill = AccessTools.TypeByName(Iw + "BlockEntitySawmill");
            _chopper = AccessTools.TypeByName(Iw + "BlockEntityChopper");
            _sawmillCell = AccessTools.TypeByName(Iw + "BEMultiblockSawmill");
            _chopperCell = AccessTools.TypeByName(Iw + "BEMultiblockChopper");
            var sawmillMp = AccessTools.TypeByName(Iw + "BEBehaviorSawmillMP");
            var chopperMp = AccessTools.TypeByName(Iw + "BEBehaviorChopperMP");
            _sawmillPrincipal = _sawmillCell == null ? null : AccessTools.DeclaredProperty(_sawmillCell, "Principal");
            _chopperPrincipal = _chopperCell == null ? null : AccessTools.DeclaredProperty(_chopperCell, "Principal");
            _sawmillMaster = sawmillMp == null ? null : AccessTools.DeclaredPropertyGetter(sawmillMp, "Master");
            _chopperMaster = chopperMp == null ? null : AccessTools.DeclaredPropertyGetter(chopperMp, "Master");
            _sawmillInput = _sawmill == null ? null : AccessTools.DeclaredProperty(_sawmill, "InputSlot");
            _chopperInput = _chopper == null ? null : AccessTools.DeclaredProperty(_chopper, "InputSlot");
            _sawmillSpeed = _sawmill == null ? null : AccessTools.DeclaredProperty(_sawmill, "NetworkSpeed");
            _chopperSpeed = _chopper == null ? null : AccessTools.DeclaredProperty(_chopper, "NetworkSpeed");
            _sawmillComplete = _sawmill == null ? null : AccessTools.DeclaredProperty(_sawmill, "IsComplete");
            _chopperComplete = _chopper == null ? null : AccessTools.DeclaredProperty(_chopper, "IsComplete");
            var iw = new List<Target>
            {
                T("BlockEntitySawmill.ToTreeAttributes", OilMachine.Sawmill, _sawmill, nameof(BlockEntity.ToTreeAttributes), null, nameof(ToTreePostfix)),
                T("BlockEntitySawmill.FromTreeAttributes", OilMachine.Sawmill, _sawmill, nameof(BlockEntity.FromTreeAttributes), null, nameof(FromTreePostfix)),
                T("BlockEntitySawmill.GetBlockInfo", OilMachine.Sawmill, _sawmill, nameof(BlockEntity.GetBlockInfo), null, nameof(InfoPostfix)),
                T("BlockEntitySawmill.CompletePass", OilMachine.Sawmill, _sawmill, "CompletePass", nameof(PassPrefix), nameof(PassPostfix)),
                T("BEBehaviorSawmillMP.GetResistance", OilMachine.Sawmill, sawmillMp, nameof(BEBehaviorMPBase.GetResistance), null, nameof(BehaviorResistancePostfix)),
                T("BlockSawmill.OnBlockInteractStart", OilMachine.Sawmill, AccessTools.TypeByName(Iw + "BlockSawmill"), nameof(Block.OnBlockInteractStart), nameof(InteractPrefix), ""),
                T("BlockSawmillGhost.OnBlockInteractStart", OilMachine.Sawmill, AccessTools.TypeByName(Iw + "BlockSawmillGhost"), nameof(Block.OnBlockInteractStart), nameof(InteractPrefix), ""),

                T("BlockEntityChopper.ToTreeAttributes", OilMachine.Chopper, _chopper, nameof(BlockEntity.ToTreeAttributes), null, nameof(ToTreePostfix)),
                T("BlockEntityChopper.FromTreeAttributes", OilMachine.Chopper, _chopper, nameof(BlockEntity.FromTreeAttributes), null, nameof(FromTreePostfix)),
                T("BlockEntityChopper.GetBlockInfo", OilMachine.Chopper, _chopper, nameof(BlockEntity.GetBlockInfo), null, nameof(InfoPostfix)),
                T("BlockEntityChopper.CompletePass", OilMachine.Chopper, _chopper, "CompletePass", nameof(PassPrefix), nameof(PassPostfix)),
                T("BEBehaviorChopperMP.GetResistance", OilMachine.Chopper, chopperMp, nameof(BEBehaviorMPBase.GetResistance), null, nameof(BehaviorResistancePostfix)),
                T("BlockChopper.OnBlockInteractStart", OilMachine.Chopper, AccessTools.TypeByName(Iw + "BlockChopper"), nameof(Block.OnBlockInteractStart), nameof(InteractPrefix), ""),
                T("BlockChopperGhost.OnBlockInteractStart", OilMachine.Chopper, AccessTools.TypeByName(Iw + "BlockChopperGhost"), nameof(Block.OnBlockInteractStart), nameof(InteractPrefix), ""),
            };
            var missing = iw.Where(t => t.Method == null).Select(t => t.Name).ToList();
            if (_sawmillPrincipal?.PropertyType != typeof(BlockPos)) missing.Add("BEMultiblockSawmill.Principal");
            if (_chopperPrincipal?.PropertyType != typeof(BlockPos)) missing.Add("BEMultiblockChopper.Principal");
            if (_sawmillMaster == null || _sawmill == null || !_sawmill.IsAssignableFrom(_sawmillMaster.ReturnType)) missing.Add("BEBehaviorSawmillMP.Master");
            if (_chopperMaster == null || _chopper == null || !_chopper.IsAssignableFrom(_chopperMaster.ReturnType)) missing.Add("BEBehaviorChopperMP.Master");
            foreach (var (name, p, type) in new[]
                     {
                         ("BlockEntitySawmill.InputSlot", _sawmillInput, typeof(ItemSlot)), ("BlockEntityChopper.InputSlot", _chopperInput, typeof(ItemSlot)),
                         ("BlockEntitySawmill.NetworkSpeed", _sawmillSpeed, typeof(float)), ("BlockEntityChopper.NetworkSpeed", _chopperSpeed, typeof(float)),
                         ("BlockEntitySawmill.IsComplete", _sawmillComplete, typeof(bool)), ("BlockEntityChopper.IsComplete", _chopperComplete, typeof(bool)),
                     })
                if (p?.PropertyType != type || p.GetMethod == null)
                    missing.Add(name);
            targets.AddRange(iw);
            if (missing.Count == 0)
            {
                WoodworkingBound = true;
                Machines[_sawmill!] = OilMachine.Sawmill;
                Machines[_chopper!] = OilMachine.Chopper;
            }
            else
                api.Logger.Warning("[seraphhorizons] Machine oil: Immersive Woodworking changed ({0} not found), so its sawmill and chopper "
                                   + "take no oil and turn as they ship", string.Join(", ", missing));
        }
        _targets = targets;
        var vanillaMissing = targets.Where(t => !IsWoodworking(t.Machine) && t.Method == null).Select(t => t.Name).ToList();
        if (vanillaMissing.Count > 0)
            api.Logger.Warning("[seraphhorizons] Machine oil: the game changed ({0} not found)", string.Join(", ", vanillaMissing));
    }

    private static bool IsWoodworking(OilMachine machine) => machine is OilMachine.Sawmill or OilMachine.Chopper;

    private static Target T(string name, OilMachine machine, Type? type, string method, string? prefix, string? postfix)
    {
        MethodBase? found = type == null ? null : AccessTools.DeclaredMethod(type, method);
        return new Target(name, machine, found, prefix ?? "", postfix ?? "");
    }

    /// <summary>Applies the patches of every bound machine (once per process: see
    /// <c>MachineOilSystem.Start</c>).</summary>
    public static void Patch(Harmony harmony)
    {
        foreach (var target in _targets)
        {
            if (target.Method == null || (IsWoodworking(target.Machine) && !WoodworkingBound))
                continue;
            harmony.Patch(target.Method,
                prefix: target.Prefix == "" ? null : new HarmonyMethod(typeof(ForeignMachines), target.Prefix),
                postfix: target.Postfix == "" ? null : new HarmonyMethod(typeof(ForeignMachines), target.Postfix));
        }
    }

    public static void Unbind()
    {
        lock (ClientMachines)
            ClientMachines.Clear();
    }

    // ---- The tank ----

    /// <summary>The machine <paramref name="be"/> is, or null.</summary>
    public static OilMachine? MachineOf(BlockEntity? be) =>
        be != null && Machines.TryGetValue(be.GetType(), out var machine) ? machine : null;

    /// <summary>The tank of <paramref name="be"/>: on the server one is made (empty) for every
    /// machine; on a client only the one the server sent is there.</summary>
    public static OilState? StateOf(BlockEntity? be)
    {
        if (MachineOf(be) is not { } machine)
            return null;
        if (States.TryGetValue(be!, out var state))
            return state;
        if (be!.Api?.Side != EnumAppSide.Server)
            return null;
        state = Oil.New(machine, _config);
        States.AddOrUpdate(be, state);
        return state;
    }

    /// <summary>The machine whose cell is at <paramref name="pos"/>: its own block entity, or the
    /// one a multiblock cell points at.</summary>
    public static BlockEntity? MachineAt(IWorldAccessor world, BlockPos pos)
    {
        var be = world.BlockAccessor.GetBlockEntity(pos);
        BlockPos? principal = be switch
        {
            BEMPMultiblock cell => cell.Principal,
            not null when _sawmillCell?.IsInstanceOfType(be) == true => _sawmillPrincipal!.GetValue(be) as BlockPos,
            not null when _chopperCell?.IsInstanceOfType(be) == true => _chopperPrincipal!.GetValue(be) as BlockPos,
            _ => null,
        };
        if (principal != null)
            be = world.BlockAccessor.GetBlockEntity(principal);
        return MachineOf(be) != null ? be : null;
    }

    // Arguments by position here and below, so a renamed parameter does not break a patch.
    public static void ToTreePostfix(BlockEntity __instance, ITreeAttribute __0)
    {
        if (StateOf(__instance) is { } state)
            Oil.Write(__0, state);
    }

    public static void FromTreePostfix(BlockEntity __instance, ITreeAttribute __0, IWorldAccessor __1)
    {
        var tree = __0;
        var worldAccessForResolve = __1;
        if (MachineOf(__instance) is not { } machine)
            return;
        if (worldAccessForResolve.Side == EnumAppSide.Server)
        {
            States.AddOrUpdate(__instance, Oil.Load(tree, machine, _config));
            return;
        }
        if (Oil.Read(tree, machine) is { } synced)
        {
            bool known = States.TryGetValue(__instance, out _);
            States.AddOrUpdate(__instance, synced);
            if (!known)
                lock (ClientMachines)
                    ClientMachines.Add(new WeakReference<BlockEntity>(__instance));
        }
        else
            States.Remove(__instance);
    }

    public static void InfoPostfix(BlockEntity __instance, StringBuilder __1) => Oil.Info(StateOf(__instance), __1);

    // The helve hammer has no GetBlockInfo of its own; the base one is patched for it alone.
    public static void HelveInfoPostfix(BlockEntity __instance, StringBuilder __1)
    {
        if (__instance is BEHelveHammer)
            Oil.Info(StateOf(__instance), __1);
    }

    private static void Drain(BlockEntity be, double points)
    {
        if (be.Api?.Side != EnumAppSide.Server || points <= 0 || StateOf(be) is not { } state)
            return;
        bool wasDry = state.Dry;
        state.Tank = state.Tank.Drain(points);
        // the client hears of every job anyway; mark it so a tank that just ran dry is shown at once
        if (state.Dry != wasDry)
            be.MarkDirty(true);
        else
            be.MarkDirty();
    }

    // ---- Jobs ----

    /// <summary>Before the hammer's tick: its swing so far, and whether the anvil has work on it.</summary>
    public static void StrikePrefix(BEHelveHammer __instance, out (float Hits, bool Work) __state) =>
        __state = (AccumHits(__instance), TargetAnvil(__instance)?.WorkItemStack != null);

    /// <summary>A strike: the swing wound back by a quarter turn (the tick's only decrease) onto
    /// an anvil with work on it.</summary>
    public static void StrikePostfix(BEHelveHammer __instance, (float Hits, bool Work) __state)
    {
        if (__state.Work && AccumHits(__instance) < __state.Hits - 0.5f)
            Drain(__instance, _config.HelveHammer.DrainPerJob);
    }

    /// <summary><c>Crush</c> takes one item from the slot each call.</summary>
    public static void CrushPostfix(BEPulverizer __instance) => Drain(__instance, _config.Pulverizer.DrainPerJob);

    public static void PassPrefix(BlockEntity __instance, out bool __state) => __state = InputSlot(__instance)?.Empty == false;

    /// <summary>A finished pass empties the input slot; one that found nothing to cut leaves it.</summary>
    public static void PassPostfix(BlockEntity __instance, bool __state)
    {
        if (!__state || InputSlot(__instance)?.Empty != true || MachineOf(__instance) is not { } machine)
            return;
        Drain(__instance, _config.For(machine).DrainPerJob);
    }

    private static ItemSlot? InputSlot(BlockEntity be) =>
        (_sawmill?.IsInstanceOfType(be) == true ? _sawmillInput : _chopperInput)?.GetValue(be) as ItemSlot;

    // ---- The load ----

    public static void ToggleResistancePostfix(BEBehaviorMPToggle __instance, ref float __result)
    {
        var sides = ToggleSides(__instance);
        if (sides == null || __instance.Api == null)
            return;
        foreach (var side in sides)
            if (__instance.Api.World.BlockAccessor.GetBlockEntity(side) is BEHelveHammer { HammerStack: not null } hammer)
            {
                if (StateOf(hammer) is { } state)
                    __result = state.Resistance(__result);
                return;
            }
    }

    public static void BehaviorResistancePostfix(BEBehaviorMPBase __instance, ref float __result)
    {
        BlockEntity? machine = __instance switch
        {
            BEBehaviorMPPulverizer => __instance.Blockentity,
            _ when _sawmillMaster?.DeclaringType?.IsInstanceOfType(__instance) == true => _sawmillMaster.Invoke(__instance, null) as BlockEntity,
            _ when _chopperMaster?.DeclaringType?.IsInstanceOfType(__instance) == true => _chopperMaster.Invoke(__instance, null) as BlockEntity,
            _ => null,
        };
        if (StateOf(machine) is { } state)
            __result = state.Resistance(__result);
    }

    // ---- Pouring ----

    /// <summary>Holding oil, a click on any cell of the machine pours it, whatever else the click
    /// would have done there. The client only says the click is taken.</summary>
    public static bool InteractPrefix(IWorldAccessor __0, IPlayer __1, BlockSelection __2, ref bool __result)
    {
        IWorldAccessor world = __0;
        IPlayer byPlayer = __1;
        BlockSelection blockSel = __2;
        if (world == null || byPlayer == null || blockSel?.Position == null)
            return true;
        var held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        if (Oil.KindOf(held, _config, _liquids) == OilKind.None)
            return true;
        var be = MachineAt(world, blockSel.Position);
        if (StateOf(be) is not { } state)
            return true;
        __result = true;
        if (world.Side == EnumAppSide.Server && Oil.Pour(world, byPlayer, state, _config, _liquids, be!.Pos) > 0)
            be.MarkDirty(true);
        return false;
    }

    // ---- Smoke ----

    /// <summary>The client's machines with a tank (alive ones), for the smoke.</summary>
    public static List<(BlockEntity Machine, OilState State)> ClientStates(IWorldAccessor world)
    {
        var alive = new List<(BlockEntity, OilState)>();
        lock (ClientMachines)
        {
            ClientMachines.RemoveAll(w => !w.TryGetTarget(out var be) || world.BlockAccessor.GetBlockEntity(be.Pos) != be);
            foreach (var weak in ClientMachines)
                if (weak.TryGetTarget(out var be) && States.TryGetValue(be, out var state))
                    alive.Add((be, state));
        }
        return alive;
    }

    /// <summary>Whether <paramref name="be"/> is assembled and its shaft turns.</summary>
    public static bool Turning(BlockEntity be)
    {
        float speed = be switch
        {
            BEHelveHammer hammer when hammer.HammerStack != null && HammerToggle(hammer) is { Network: { } net } toggle => net.Speed * toggle.GearedRatio,
            BEPulverizer { hasAxle: true } pulverizer => pulverizer.GetBehavior<BEBehaviorMPPulverizer>()?.Network?.Speed ?? 0,
            _ when _sawmill?.IsInstanceOfType(be) == true && (bool)_sawmillComplete!.GetValue(be)! => (float)_sawmillSpeed!.GetValue(be)!,
            _ when _chopper?.IsInstanceOfType(be) == true && (bool)_chopperComplete!.GetValue(be)! => (float)_chopperSpeed!.GetValue(be)!,
            _ => 0,
        };
        return Math.Abs(speed) >= Oil.TurningSpeed;
    }

    /// <summary>Where a machine's smoke rises from (world).</summary>
    public static Vec3d SmokeAt(BlockEntity be) => be.Pos.ToVec3d().Add(0.5, be is BEHelveHammer ? 0.6 : 1.1, 0.5);
}
