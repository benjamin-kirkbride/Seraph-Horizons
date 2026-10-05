using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// The creative shortcut on Immersive Woodworking's chopper and sawmill
/// (<see cref="CreativeUpgrades.Machines"/>), as the bucking sawmill has it: a player in creative
/// mode who right-clicks the machine with Ctrl held (not Shift) gets its next part fitted at once,
/// with nothing in hand and nothing taken. The parts go in the order the machine itself lists them
/// as missing, then a steel head or blade kit (<see cref="AssembledMachines.DefaultMetal"/>); the
/// chopper's bed is an advanced splitting block of oak, the only one it takes
/// (<see cref="ChopperBed"/>). On a finished machine Ctrl takes the log or the tool out, as
/// Immersive Woodworking has it.
///
/// Immersive Woodworking fits each part itself: a prefix on the block entity's
/// <c>OnInteract(IPlayer, BlockSelection)</c>, which the frame and its ghosts all call on both
/// sides, hands the part to the machine's own <c>TryAddPart(ItemSlot, IPlayer)</c> in a slot of its
/// own. So the part is checked, fitted, sounded and drawn as one put in by hand, and the client
/// predicts it as it does a hand's. A postfix on the frame's <c>GetPlacedBlockInteractionHelp</c>
/// (which the ghosts' help goes through) adds the shortcut's line for a player in creative.
///
/// A machine's members are found by name. Missing ones do not fail the tweak, as with
/// <see cref="FrameUpgrades"/>: that machine gets no shortcut, with one warning.
/// </summary>
public sealed class MachineUpgrades : WoodworkingPart
{
    private sealed record Bound(MethodInfo Missing, MethodInfo TryAddPart, FieldInfo Tool, string ToolCode);

    // By block entity class, bound; replaced whole by Bind.
    private static IReadOnlyDictionary<Type, Bound> _machines = new Dictionary<Type, Bound>();
    private readonly List<(MethodInfo Interact, MethodInfo Help)> _patches = [];
    private string? _missing;

    public override string Name => "the chopper's and sawmill's creative upgrades";

    public override string? Bind(ICoreAPI api, WoodworkingMods mods)
    {
        var machines = new Dictionary<Type, Bound>();
        var missing = new List<string>();
        _patches.Clear();
        foreach (var machine in CreativeUpgrades.Machines)
        {
            var entity = WoodworkingMods.IwType(machine.EntityClass);
            var block = WoodworkingMods.IwType(machine.BlockClass);
            var interact = entity == null ? null
                : AccessTools.DeclaredMethod(entity, "OnInteract", [typeof(IPlayer), typeof(BlockSelection)]);
            var parts = entity == null ? null : AccessTools.DeclaredMethod(entity, "MissingMandatoryParts", []);
            var tryAdd = entity == null ? null : AccessTools.DeclaredMethod(entity, "TryAddPart", [typeof(ItemSlot), typeof(IPlayer)]);
            var tool = entity == null ? null : AccessTools.DeclaredField(entity, machine.ToolField);
            var help = block == null ? null
                : AccessTools.DeclaredMethod(block, FrameUpgrades.HelpMethod, [typeof(IWorldAccessor), typeof(BlockSelection), typeof(IPlayer)]);
            if (entity == null || !typeof(BlockEntity).IsAssignableFrom(entity) || interact?.ReturnType != typeof(bool)
                || parts?.ReturnType != typeof(string[]) || parts.IsStatic || tryAdd?.ReturnType != typeof(bool) || tryAdd.IsStatic
                || tool is not { IsStatic: false } || tool.FieldType != typeof(ItemStack)
                || help?.ReturnType != typeof(WorldInteraction[]))
            {
                missing.Add(machine.EntityClass);
                continue;
            }
            machines[entity] = new Bound(parts, tryAdd, tool, machine.ToolCode);
            _patches.Add((interact, help));
        }
        _machines = machines;
        _missing = missing.Count == 0 ? null : string.Join(", ", missing);
        return null;
    }

    public override void Start(ICoreAPI api, Harmony harmony)
    {
        if (_missing != null)
            api.Logger.Warning("[seraphhorizons] Unified woodworking: Immersive Woodworking changed its machines "
                               + $"({_missing}), so those have no creative upgrade");
        foreach (var (interact, help) in _patches)
        {
            harmony.Patch(interact, prefix: new HarmonyMethod(typeof(MachineUpgrades), nameof(InteractPrefix)));
            if (api.Side == EnumAppSide.Client)
                harmony.Patch(help, postfix: new HarmonyMethod(typeof(MachineUpgrades), nameof(HelpPostfix)));
        }
    }

    // _machines stays: in singleplayer the other side's patches may still run after this side
    // disposes, and the next Bind sets it again.
    public override void Dispose() => _patches.Clear();

    private static bool IsCreative(IPlayer? player) => player?.WorldData?.CurrentGameMode == EnumGameMode.Creative;

    /// <summary>The part the shortcut fits next on a machine, with its bound members; null on a
    /// finished machine, one that is not bound, or when the part's item is not in the game.</summary>
    private static (ItemStack Part, Bound Machine)? NextPart(BlockEntity? machine)
    {
        if (machine?.Api?.World is not { } world || !_machines.TryGetValue(machine.GetType(), out var bound))
            return null;
        string? path = CreativeUpgrades.NextMachinePart(bound.Missing.Invoke(machine, null) as string[],
            bound.Tool.GetValue(machine) != null, bound.ToolCode, AssembledMachines.DefaultMetal);
        if (path == null)
            return null;
        var code = new AssetLocation(WoodworkingMods.IwModId, path);
        CollectibleObject? part = world.GetItem(code);
        part ??= world.GetBlock(code) is { Id: not 0 } block ? block : null;
        if (part == null)
            return null;
        var stack = new ItemStack(part);
        if (path == ChopperBed.BedCode)
        {
            stack.Attributes.SetString("wood", SplittingBlock.DefaultWood);
            stack.Attributes.SetString("woodDomain", SplittingBlock.DefaultWoodDomain);
            stack.Attributes.SetString(SplittingBlockTiers.AttributeKey, SplittingBlockTier.Advanced.Name());
        }
        return (stack, bound);
    }

    /// <summary>Prefix on a machine's <c>OnInteract</c> (both sides): in creative with Ctrl, on a
    /// machine with a part still to fit, has the machine fit it.</summary>
    public static bool InteractPrefix(BlockEntity __instance, IPlayer __0, ref bool __result, bool __runOriginal)
    {
        var controls = __0?.Entity?.Controls;
        if (!__runOriginal || controls == null
            || !CreativeUpgrades.Applies(IsCreative(__0), controls.CtrlKey, controls.ShiftKey)
            || NextPart(__instance) is not var (part, machine))
            return __runOriginal;
        machine.TryAddPart.Invoke(__instance, [new DummySlot(part), __0]);
        __result = true;
        return false;
    }

    /// <summary>Postfix on a frame's <c>GetPlacedBlockInteractionHelp</c> (client): the creative
    /// shortcut's line, to a player in creative mode, while the machine has a part to fit.</summary>
    public static void HelpPostfix(IWorldAccessor __0, BlockSelection __1, IPlayer __2, ref WorldInteraction[] __result)
    {
        if (IsCreative(__2) && __1?.Position != null && NextPart(__0.BlockAccessor.GetBlockEntity(__1.Position)) != null)
            __result = (__result ?? []).Append(SplittingBlockUpgrades.CreativeUpgradeHelp);
    }
}
