using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// Immersive Woodworking's mechanical chopper (<c>BlockEntityChopper</c>) takes only an advanced
/// splitting block as its bed, so its yield is that tier's (<see cref="WoodworkingSettings"/> sets
/// <c>ChopperFirewoodPerLog</c> to it), and the bed looks like one.
///
/// The chopper keeps only a bed's wood (<c>bedWood</c>, <c>bedWoodDomain</c>), so everything here
/// follows from every installed bed being advanced:
/// - <c>TryAddPart(ItemSlot, IPlayer)</c>, both sides: a prefix refuses a chopping block of a lower
///   tier with an in-game error, before Immersive Woodworking takes it. The client predicts the
///   install with the same method, so it runs there too, or the bed would flash in and out.
/// - <c>BedStack()</c>, the bed breaking the frame gives back: a postfix stamps the advanced tier,
///   and gives a bed without a wood (a chopper assembled before the assembled one's bed had one)
///   the oak Immersive Woodworking drew it as (<see cref="SplittingBlock.DefaultWood"/>).
/// - <c>BuildBedMesh(ICoreClientAPI, string, string, int)</c>, client: a prefix builds Logging
///   Expanded's advanced splitting block for the bed's wood (<see cref="SplittingBlockLook.BuildBed"/>).
/// - <c>BlockChopper.OnLoaded(ICoreAPI)</c>, client: the bed the frame's interaction help shows for
///   a missing bed is an advanced one.
///
/// Tiers exist only where the splitting block's blocktype carries their behavior
/// (<see cref="SplittingBlock.EditAssets"/>). If another mod's patch kept that edit from applying,
/// no block could be upgraded, so the chopper takes a splitting block of any tier, as Immersive
/// Woodworking does, rather than none (the server's log says so).
///
/// In singleplayer both sides' patches sit on the same methods, so each runs twice: the prefix on
/// <c>TryAddPart</c> does nothing once a prefix has skipped the original, and the rest only set
/// the same values again.
/// </summary>
public sealed class ChopperBed : WoodworkingPart
{
    public const string ChopperType = "BlockEntityChopper";
    public const string FrameType = "BlockChopper";
    public const string BedCode = "choppingblock";
    public const string ErrorCode = "chopperbed-notadvanced";
    public const string ErrorKey = "seraphhorizons:ingameerror-" + ErrorCode;

    private static readonly AssetLocation BedShape = SplittingBlockLook.ShapePaths[(int)SplittingBlockTier.Advanced];

    private static FieldInfo? _hasBed;
    private static FieldInfo? _partStacks;
    private MethodInfo? _tryAddPart;
    private MethodInfo? _bedStack;
    private MethodInfo? _buildBedMesh;
    private MethodInfo? _frameLoaded;

    public override string Name => "the chopper's bed";

    public override string? Bind(ICoreAPI api, WoodworkingMods mods)
    {
        var chopper = WoodworkingMods.IwType(ChopperType);
        var frame = WoodworkingMods.IwType(FrameType);
        if (chopper == null || !typeof(BlockEntity).IsAssignableFrom(chopper)
            || frame == null || !typeof(Block).IsAssignableFrom(frame))
            return $"{WoodworkingMods.IwNamespace}.{ChopperType} or {FrameType} is missing";
        _tryAddPart = AccessTools.DeclaredMethod(chopper, "TryAddPart", [typeof(ItemSlot), typeof(IPlayer)]);
        _bedStack = AccessTools.DeclaredMethod(chopper, "BedStack", []);
        _buildBedMesh = AccessTools.DeclaredMethod(chopper, "BuildBedMesh",
            [typeof(ICoreClientAPI), typeof(string), typeof(string), typeof(int)]);
        _frameLoaded = AccessTools.DeclaredMethod(frame, "OnLoaded", [typeof(ICoreAPI)]);
        _hasBed = AccessTools.DeclaredField(chopper, "hasBed");
        _partStacks = AccessTools.DeclaredField(frame, "partStacks");
        if (_tryAddPart?.ReturnType != typeof(bool) || _bedStack?.ReturnType != typeof(ItemStack)
            || _buildBedMesh is not { IsStatic: true } || _buildBedMesh.ReturnType != typeof(MeshData)
            || _frameLoaded == null || _hasBed?.FieldType != typeof(bool)
            || _partStacks?.FieldType != typeof(Dictionary<string, ItemStack>))
            return $"{ChopperType}.TryAddPart, BedStack, BuildBedMesh, hasBed or {FrameType}.OnLoaded, partStacks "
                   + "is not as expected";
        if (api.Side == EnumAppSide.Server && api.Assets.TryGet(BedShape) == null)
            return $"{BedShape} is missing";
        return null;
    }

    public override void Start(ICoreAPI api, Harmony harmony)
    {
        harmony.Patch(_tryAddPart, prefix: new HarmonyMethod(typeof(ChopperBed), nameof(TryAddPartPrefix)));
        harmony.Patch(_bedStack, postfix: new HarmonyMethod(typeof(ChopperBed), nameof(BedStackPostfix)));
        if (api.Side != EnumAppSide.Client)
            return;
        // Blocks are loaded, and the bed meshes built, after this on a client.
        harmony.Patch(_buildBedMesh, prefix: new HarmonyMethod(typeof(ChopperBed), nameof(BuildBedMeshPrefix)));
        harmony.Patch(_frameLoaded, postfix: new HarmonyMethod(typeof(ChopperBed), nameof(FrameLoadedPostfix)));
    }

    public override void Dispose()
    {
        _hasBed = null;
        _partStacks = null;
        _tryAddPart = _bedStack = _buildBedMesh = _frameLoaded = null;
    }

    /// <summary>Whether the stack is a chopping block (the splitting block) the chopper takes:
    /// Immersive Woodworking's own test, by code.</summary>
    private static bool IsBed(ItemStack? stack) =>
        stack?.Collectible?.Code is { Domain: WoodworkingMods.IwModId, Path: BedCode };

    private static SplittingBlockTier Tier(ItemStack stack) =>
        SplittingBlockTiers.Parse(stack.Attributes.GetString(SplittingBlockTiers.AttributeKey));

    /// <summary>Refuses a bed below the advanced tier: handled (true), so the chopper does not
    /// try it as a log either. A chopper that has its bed already says so as before.</summary>
    public static bool TryAddPartPrefix(BlockEntity __instance, ItemSlot __0, ref bool __result, bool __runOriginal)
    {
        // Null-safe: in singleplayer the server's Dispose clears the statics while the client's
        // patches are still in.
        if (!__runOriginal || !IsBed(__0.Itemstack) || _hasBed?.GetValue(__instance) is true
            || Tier(__0.Itemstack!).IsChopperBed() || !HasTiers(__0.Itemstack!.Block))
            return __runOriginal;
        (__instance.Api as ICoreClientAPI)?.TriggerIngameError(__instance, ErrorCode, Lang.Get(ErrorKey));
        __result = true;
        return false;
    }

    /// <summary>Whether the splitting block's blocktype carries the tier behavior.</summary>
    private static bool HasTiers(Block? block) =>
        block?.BlockEntityBehaviors?.Any(behavior => behavior.Name == BEBehaviorSplittingBlockTier.Name) == true;

    public static void BedStackPostfix(ItemStack __result)
    {
        if (!__result.Attributes.HasAttribute("wood"))
        {
            __result.Attributes.SetString("wood", SplittingBlock.DefaultWood);
            __result.Attributes.SetString("woodDomain", SplittingBlock.DefaultWoodDomain);
        }
        __result.Attributes.SetString(SplittingBlockTiers.AttributeKey, SplittingBlockTier.Advanced.Name());
    }

    /// <summary>The advanced splitting block in place of Immersive Woodworking's chopping block;
    /// Immersive Woodworking's own when that cannot be built.</summary>
    public static bool BuildBedMeshPrefix(ICoreClientAPI __0, string __1, string __2, int __3, ref MeshData? __result)
    {
        __result = SplittingBlockLook.BuildBed(__0, __1, __2, __3);
        return __result == null;
    }

    /// <summary>The interaction help's bed for a frame without one: an advanced oak splitting block.</summary>
    public static void FrameLoadedPostfix(Block __instance, ICoreAPI __0)
    {
        if (__0.Side != EnumAppSide.Client
            || _partStacks?.GetValue(__instance) is not Dictionary<string, ItemStack> stacks
            || !stacks.TryGetValue(BedCode, out var bed))
            return;
        if (!bed.Attributes.HasAttribute("wood"))
        {
            bed.Attributes.SetString("wood", AssembledMachines.BedWood);
            bed.Attributes.SetString("woodDomain", AssembledMachines.BedWoodDomain);
        }
        bed.Attributes.SetString(SplittingBlockTiers.AttributeKey, SplittingBlockTier.Advanced.Name());
    }
}
