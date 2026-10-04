using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// Immersive Woodworking's chopping block (<c>immersivewoodworking:choppingblock</c>, its wood in
/// the stack's and block entity's <c>wood</c>/<c>woodDomain</c>) is the splitting block, with
/// Logging Expanded's four splitting log tiers. It keeps all it does as a chopping block (its
/// animations, the log to half-logs to firewood stages, firewood to sticks, the maul's one pass);
/// on top of that:
/// - Tier: <see cref="BEBehaviorSplittingBlockTier"/>, added to the blocktype here, keeps it on the
///   placed block through saves; a postfix on <c>BlockChoppingBlock.OnPickBlock</c> (which
///   Immersive Woodworking's <c>GetDrops</c> drops) puts it on the stack, so breaking the block
///   gives it back with its wood and tier. A block with no wood (placed from a stack without one:
///   the creative or handbook stack, <c>/giveblock</c>, the bed of a chopper assembled before the
///   bed had a wood) gets Immersive Woodworking's plain stack from <c>GetDrops</c> instead, so a
///   postfix there puts the tier on it too; it looks like <see cref="DefaultWood"/>. A postfix on
///   <c>GetHeldItemName</c> names the tier ("Oak bound splitting block").
/// - Look: <see cref="SplittingBlockLook"/>. The collision box rises to the models' 15/16 and the
///   selection box to 1.25, keeping Immersive Woodworking's 5/16 above the top for what lies on it.
/// - Made in the world: Logging Expanded's axe on an upright placed log
///   (<c>BlockBehaviorLogConvert</c>, which made its splitting log) makes a primitive splitting
///   block of that log's wood, at its cost (1 axe durability) and with its sound; its help line
///   says so. Immersive Woodworking's grid recipes are gone (<see cref="RetiredStations"/>).
/// - Upgrades: <see cref="SplittingBlockUpgrades"/>.
/// - Yield: 6 firewood per log by Immersive Woodworking's settings (<see cref="WoodworkingSettings"/>),
///   8 on an advanced block: a prefix on <c>BlockEntityChoppingBlock.Chop</c> raises
///   <c>FirewoodPerLog</c> for that one chop and a finalizer puts it back. A half-log takes half,
///   as Immersive Woodworking divides it; sticks from firewood do not depend on it.
/// - A splitting block cannot be laid on one to be chopped (Immersive Woodworking counts it as a
///   log, which would lose the bound and advanced tiers' iron): a postfix on its
///   <c>IsChoppable</c>, a prefix on <c>TryPut</c> (in case the JIT inlined the former there), and
///   a prefix on <c>AutoRestockUtil.FindRestockSlot</c>, which refills the block after a chop by
///   kind (a splitting block is a "log" to it), on both sides: the client predicts the restock.
/// </summary>
public sealed class SplittingBlock : WoodworkingPart
{
    public const string BlockType = "BlockChoppingBlock";
    public const string BlockEntityType = "BlockEntityChoppingBlock";
    public const string LogConvertType = "BlockBehaviorLogConvert";
    public static readonly AssetLocation BlockCode = new(WoodworkingMods.IwModId, SplittingBlockRules.BlockCode);
    public static readonly AssetLocation BlockAsset = new(WoodworkingMods.IwModId, "blocktypes/choppingblock.json");
    public static readonly AssetLocation PlaceSound = new("game", "sounds/block/wood");

    /// <summary>The wood a splitting block without one looks like: Immersive Woodworking's own
    /// fallback, the oak its chopper draws for a bed without a wood.</summary>
    public const string DefaultWood = "oak";
    public const string DefaultWoodDomain = "game";

    /// <summary>The collision box's top: the models' 15/16. The selection box keeps Immersive
    /// Woodworking's 5/16 over it.</summary>
    public const double CollisionTop = 15 / 16.0;
    public const double SelectionTop = CollisionTop + 5 / 16.0;

    public static readonly LangReplacement[] Replacements =
    [
        new("en", "loggingmod:wi-log-convert", "Chop with an axe to make a splitting block"),
    ];

    private static MethodInfo? _inTemplate;
    private static FieldInfo? _firewoodPerLog;
    private static PropertyInfo? _iwConfig;
    private readonly List<(MethodInfo Target, string? Prefix, string? Postfix, EnumAppSide? Side)> _patches = [];
    private bool _client;

    public override string Name => "the splitting block";

    public override void RegisterClasses(ICoreAPI api) =>
        api.RegisterBlockEntityBehaviorClass(BEBehaviorSplittingBlockTier.Name, typeof(BEBehaviorSplittingBlockTier));

    public override string? Bind(ICoreAPI api, WoodworkingMods mods)
    {
        var block = WoodworkingMods.IwType(BlockType);
        var blockEntity = WoodworkingMods.IwType(BlockEntityType);
        var logConvert = WoodworkingMods.LeType(LogConvertType);
        var woodName = WoodworkingMods.IwType("WoodName");
        var restock = WoodworkingMods.IwType("AutoRestockUtil");
        if (block == null || !typeof(Block).IsAssignableFrom(block) || blockEntity == null
            || !typeof(BlockEntity).IsAssignableFrom(blockEntity) || logConvert == null
            || !typeof(BlockBehavior).IsAssignableFrom(logConvert) || woodName == null || restock == null)
            return $"{BlockType}, {BlockEntityType}, WoodName, AutoRestockUtil or {LogConvertType} is missing";

        _inTemplate = AccessTools.DeclaredMethod(woodName, "InTemplate", [typeof(string), typeof(string), typeof(string)]);
        _firewoodPerLog = mods.IwSetting<int>("FirewoodPerLog");
        _iwConfig = AccessTools.DeclaredProperty(mods.IwSystem.GetType(), "Config");
        if (_inTemplate is not { IsStatic: true } || _inTemplate.ReturnType != typeof(string) || _firewoodPerLog == null)
            return "WoodName.InTemplate or the FirewoodPerLog setting is missing";
        if (!SplittingBlockLook.Bind(blockEntity))
            return $"{BlockEntityType}.Wood, WoodDomain, contentMesh or toolMesh is missing";
        if (SplittingBlockUpgrades.Bind(mods, blockEntity) is { } reason)
            return reason;

        var interaction = new[] { typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection) };
        var step = new[] { typeof(float), typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection) };
        var wanted = new (Type Type, string Method, Type[] Args, Type Returns, string? Prefix, string? Postfix, EnumAppSide? Side)[]
        {
            (logConvert, "OnBlockInteractStart", [.. interaction, typeof(EnumHandling).MakeByRefType()], typeof(bool),
                nameof(LogConvertPrefix), null, null),
            (block, "OnBlockInteractStart", interaction, typeof(bool), nameof(SplittingBlockUpgrades.InteractStartPrefix), null, null),
            (block, "OnBlockInteractStep", step, typeof(bool), nameof(SplittingBlockUpgrades.InteractStepPrefix), null, null),
            (block, "OnBlockInteractStop", step, typeof(void), nameof(SplittingBlockUpgrades.InteractStopPrefix), null, null),
            (block, "OnBlockInteractCancel", [.. step, typeof(EnumItemUseCancelReason)], typeof(bool),
                nameof(SplittingBlockUpgrades.InteractCancelPrefix), null, null),
            (block, "GetHeldItemName", [typeof(ItemStack)], typeof(string), null, nameof(GetHeldItemNamePostfix), null),
            (block, "OnPickBlock", [typeof(IWorldAccessor), typeof(BlockPos)], typeof(ItemStack), null, nameof(OnPickBlockPostfix), null),
            (block, "GetDrops", [typeof(IWorldAccessor), typeof(BlockPos), typeof(IPlayer), typeof(float)], typeof(ItemStack[]), null,
                nameof(GetDropsPostfix), null),
            (blockEntity, "TryPut", [typeof(ItemSlot), typeof(IPlayer)], typeof(bool), nameof(TryPutPrefix), null, null),
            (blockEntity, "IsChoppable", [typeof(ItemStack), mods.IwConfigClass], typeof(bool), null, nameof(IsChoppablePostfix), null),
            (blockEntity, "Chop", [typeof(IPlayer)], typeof(void), nameof(ChopPrefix), null, EnumAppSide.Server),
            (restock, "FindRestockSlot", [typeof(IPlayer), typeof(System.Func<ItemStack, bool>), typeof(System.Func<ItemStack, bool>)],
                typeof(ItemSlot), nameof(FindRestockSlotPrefix), null, null),
            (blockEntity, "OnTesselation", [typeof(ITerrainMeshPool), typeof(ITesselatorAPI)], typeof(bool),
                nameof(SplittingBlockLook.OnTesselationPrefix), null, EnumAppSide.Client),
            (block, "OnBeforeRender", [typeof(ICoreClientAPI), typeof(ItemStack), typeof(EnumItemRenderTarget),
                typeof(ItemRenderInfo).MakeByRefType()], typeof(void), null, nameof(SplittingBlockLook.OnBeforeRenderPostfix),
                EnumAppSide.Client),
            (block, "GetPlacedBlockInteractionHelp", [typeof(IWorldAccessor), typeof(BlockSelection), typeof(IPlayer)],
                typeof(WorldInteraction[]), null, nameof(SplittingBlockUpgrades.InteractionHelpPostfix), EnumAppSide.Client),
        };
        _patches.Clear();
        foreach (var (type, name, args, returns, prefix, postfix, side) in wanted)
        {
            if (AccessTools.DeclaredMethod(type, name, args) is not { } method || method.ReturnType != returns)
                return $"{type.Name}.{name} is not as expected";
            _patches.Add((method, prefix, postfix, side));
        }

        if (api.Side == EnumAppSide.Server)
        {
            if (WoodworkingAssets.Read(api, BlockAsset) is not { } json || !WoodworkingAssets.HasCode(json, SplittingBlockRules.BlockCode)
                || WoodworkingAssets.Get(json, "entityClass") == null || WoodworkingAssets.Get(json, "collisionbox") is not JObject
                || WoodworkingAssets.Get(json, "selectionbox") is not JObject)
                return $"{BlockAsset} is missing or not the {SplittingBlockRules.BlockCode} type with one box each";
            foreach (var shape in SplittingBlockLook.ShapePaths)
                if (api.Assets.TryGet(shape) == null)
                    return $"{shape} is missing";
        }
        return null;
    }

    public override void Start(ICoreAPI api, Harmony harmony)
    {
        _client = api.Side == EnumAppSide.Client;
        foreach (var (target, prefix, postfix, side) in _patches)
        {
            if (side != null && side != api.Side)
                continue;
            harmony.Patch(target,
                prefix: prefix == null ? null : Method(prefix),
                postfix: postfix == null ? null : Method(postfix),
                finalizer: prefix == nameof(ChopPrefix) ? Method(nameof(ChopFinalizer)) : null);
        }
    }

    // A patch method of this part, its look or its upgrades, by name.
    private static HarmonyMethod Method(string name) =>
        new(new[] { typeof(SplittingBlock), typeof(SplittingBlockLook), typeof(SplittingBlockUpgrades) }
            .Select(type => AccessTools.DeclaredMethod(type, name)).First(method => method != null));

    public override void AssetsLoaded(ICoreAPI api) =>
        LangText.Replace(Replacements, "Logging Expanded", api.Logger);

    public override void EditAssets(ICoreServerAPI api) =>
        WoodworkingAssets.Edit(api, BlockAsset, json =>
        {
            if (!WoodworkingAssets.HasCode(json, SplittingBlockRules.BlockCode)
                || WoodworkingAssets.Get(json, "collisionbox") is not JObject collision
                || WoodworkingAssets.Get(json, "selectionbox") is not JObject selection)
                return false;
            if (WoodworkingAssets.Get(json, "entityBehaviors") is not JArray behaviors)
                json["entityBehaviors"] = behaviors = [];
            behaviors.Add(new JObject { ["name"] = BEBehaviorSplittingBlockTier.Name });
            collision["y2"] = CollisionTop;
            selection["y2"] = SelectionTop;
            return true;
        }, "the splitting block's tier and size",
        "splitting blocks have no tiers, so none is upgraded, each chops 6 firewood per log and looks like Immersive "
        + "Woodworking's when placed, and the chopper takes any as its bed");

    public override void Dispose()
    {
        _patches.Clear();
        if (!_client)
            return;
        SplittingBlockLook.Clear();
        SplittingBlockUpgrades.Clear();
    }

    /// <summary>Whether a stack is a splitting block.</summary>
    private static bool IsSplittingBlock(ItemStack? stack) =>
        stack?.Collectible?.Code is { } code && SplittingBlockRules.IsSplittingBlock(code.Domain, code.Path);

    /// <summary>Prefix on Logging Expanded's <c>BlockBehaviorLogConvert.OnBlockInteractStart</c>
    /// (both sides): an axe on an upright placed log makes a primitive splitting block of its wood
    /// in its place, in place of Logging Expanded's splitting log. Logging Expanded's own checks,
    /// cost and sound; the wood's domain is the log's.</summary>
    public static bool LogConvertPrefix(BlockBehavior __instance, IWorldAccessor __0, IPlayer __1, BlockSelection __2,
        ref EnumHandling __3, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal)
            return false;
        __result = false;
        var slot = __1.InventoryManager.ActiveHotbarSlot;
        var log = __instance.block.Code;
        if (slot?.Itemstack?.Collectible?.Tool != EnumTool.Axe || !log.Path.StartsWith("log-placed-", StringComparison.Ordinal)
            || !log.Path.EndsWith("-ud", StringComparison.Ordinal) || log.Path.Length <= "log-placed--ud".Length
            || __0.GetBlock(BlockCode) is not { Id: not 0 } splittingBlock)
            return false;
        __3 = EnumHandling.PreventDefault;
        if (__0.Side == EnumAppSide.Server)
        {
            var stack = new ItemStack(splittingBlock);
            stack.Attributes.SetString("wood", log.Path["log-placed-".Length..^"-ud".Length]);
            stack.Attributes.SetString("woodDomain", log.Domain);
            __0.BlockAccessor.SetBlock(splittingBlock.Id, __2.Position, stack);
            slot.Itemstack.Collectible.DamageItem(__0, __1.Entity, slot);
            __0.PlaySoundAt(PlaceSound, __2.Position, 0.0, __1, true, 16f);
        }
        (__1 as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemAttack);
        __result = true;
        return false;
    }

    /// <summary>Postfix on <c>BlockChoppingBlock.GetHeldItemName</c>: the tier's name, with the
    /// wood as Immersive Woodworking words it.</summary>
    public static void GetHeldItemNamePostfix(ItemStack __0, ref string __result)
    {
        var tier = BEBehaviorSplittingBlockTier.Of(__0);
        __result = __0.Attributes.GetString("wood") is { } wood
            ? (string)_inTemplate!.Invoke(null, [tier.WoodNameKey(), wood, __0.Attributes.GetString("woodDomain", "game")])!
            : Lang.Get(tier.NameKey());
    }

    /// <summary>Postfix on <c>BlockChoppingBlock.OnPickBlock</c>: the stack carries the block's
    /// tier. A primitive one carries none, like every stack Immersive Woodworking makes.</summary>
    public static void OnPickBlockPostfix(IWorldAccessor __0, BlockPos __1, ItemStack? __result)
    {
        if (__result != null && BEBehaviorSplittingBlockTier.At(__0, __1) is { Tier: not SplittingBlockTier.Primitive } behavior)
            __result.Attributes.SetString(SplittingBlockTiers.AttributeKey, behavior.Tier.Name());
    }

    /// <summary>Postfix on <c>BlockChoppingBlock.GetDrops</c>: a block without a wood drops
    /// Immersive Woodworking's plain stack, not <c>OnPickBlock</c>'s, so the tier goes on it here.
    /// (A block with a wood has it from <see cref="OnPickBlockPostfix"/> already.)</summary>
    public static void GetDropsPostfix(IWorldAccessor __0, BlockPos __1, ItemStack[]? __result)
    {
        if (__result == null || BEBehaviorSplittingBlockTier.At(__0, __1) is not { Tier: not SplittingBlockTier.Primitive } behavior)
            return;
        foreach (var stack in __result)
            if (IsSplittingBlock(stack))
                stack.Attributes.SetString(SplittingBlockTiers.AttributeKey, behavior.Tier.Name());
    }

    public static bool TryPutPrefix(ItemSlot __0, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal || !IsSplittingBlock(__0.Itemstack))
            return __runOriginal;
        __result = false;
        return false;
    }

    public static void IsChoppablePostfix(ItemStack? __0, ref bool __result) => __result = __result && !IsSplittingBlock(__0);

    /// <summary>Leaves splitting blocks out of what a restock may take.</summary>
    public static void FindRestockSlotPrefix(ref System.Func<ItemStack, bool> __1)
    {
        var accept = __1;
        __1 = stack => !IsSplittingBlock(stack) && accept(stack);
    }

    /// <summary>Prefix on <c>BlockEntityChoppingBlock.Chop</c> (server): an advanced block's chop
    /// yields that tier's firewood per log. <paramref name="__state"/> keeps the setting it
    /// replaced, for <see cref="ChopFinalizer"/>.</summary>
    public static void ChopPrefix(BlockEntity __instance, out (object Config, int FirewoodPerLog)? __state)
    {
        __state = null;
        if (__instance.GetBehavior<BEBehaviorSplittingBlockTier>() is not { } behavior)
            return;
        int perLog = behavior.Tier.FirewoodPerLog();
        object config = _iwConfig!.GetValue(__instance.Api.ModLoader.GetModSystem(WoodworkingMods.IwSystemType))!;
        int current = (int)_firewoodPerLog!.GetValue(config)!;
        if (current == perLog)
            return;
        __state = (config, current);
        _firewoodPerLog.SetValue(config, perLog);
    }

    public static void ChopFinalizer((object Config, int FirewoodPerLog)? __state)
    {
        if (__state is var (config, perLog))
            _firewoodPerLog!.SetValue(config, perLog);
    }
}
