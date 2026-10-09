using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>
/// A melting hole of the crucible furnace (<c>meltinghole-{lid}</c>): one block set into the floor,
/// lined with tier-3 refractory brick, its iron lid flush with its top. Right-clicks go to
/// <see cref="BEMeltingHole"/>; a firestarter or a lit torch (Shift) lights its coke, as the forge is
/// lit (<see cref="IIgnitable"/>). Breaking it drops the hole (lid shut), its pot and unburnt coke.
/// </summary>
public class BlockMeltingHole : Block, IIgnitable
{
    private WorldInteraction[]? _help;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        _help = ObjectCacheUtil.GetOrCreate(api, "meltinghole-help", () =>
        {
            ItemStack[] Stacks(params string[] codes) => codes
                .Select(c => CrucibleFurnaceSystem.Resolve(api.World, c)).OfType<CollectibleObject>()
                .Select(c => new ItemStack(c)).ToArray();
            string Key(string action) => CrucibleFurnaceSystem.Domain + ":blockhelp-meltinghole-" + action;
            return new[]
            {
                new WorldInteraction { ActionLangCode = Key("lid"), MouseButton = EnumMouseButton.Right, HotKeyCode = "shift" },
                new WorldInteraction { ActionLangCode = Key("pot"), MouseButton = EnumMouseButton.Right, Itemstacks = Stacks(CrucibleFurnaceSystem.FiredPot) },
                new WorldInteraction { ActionLangCode = Key("coke"), MouseButton = EnumMouseButton.Right, Itemstacks = Stacks("game:coke") },
                new WorldInteraction { ActionLangCode = Key("light"), MouseButton = EnumMouseButton.Right, Itemstacks = BlockBehaviorCanIgnite.CanIgniteStacks(api, true).ToArray() },
                new WorldInteraction { ActionLangCode = Key("pull"), MouseButton = EnumMouseButton.Right },
            };
        });
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;
        return world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEMeltingHole hole
            ? hole.OnInteract(byPlayer)
            : base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = new List<ItemStack>();
        if (world.GetBlock(new AssetLocation(CrucibleFurnaceSystem.ClosedHole)) is { Id: > 0 } closed)
            drops.Add(new ItemStack(closed));
        if (world.BlockAccessor.GetBlockEntity(pos) is BEMeltingHole hole)
            drops.AddRange(hole.ContentDrops());
        return drops.ToArray();
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) =>
        new(world.GetBlock(new AssetLocation(CrucibleFurnaceSystem.ClosedHole)));

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer) =>
        this.WithoutDescription(base.GetPlacedBlockInfo(world, pos, forPlayer));

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer) =>
        (_help ?? []).Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));

    // ---- IIgnitable: lit as the forge is ----

    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting)
    {
        if (byEntity.World.BlockAccessor.GetBlockEntity(pos) is not BEMeltingHole hole)
            return EnumIgniteState.NotIgnitable;
        if (hole.CannotLight() is not null)
        {
            if (secondsIgniting == 0 && byEntity.World.Side == EnumAppSide.Server)
                hole.TryLight((byEntity as EntityPlayer)?.Player);   // says why not
            return EnumIgniteState.NotIgnitablePreventDefault;
        }
        return secondsIgniting >= 1.5f ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
    }

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        if (secondsIgniting < 1.45f)
            return;
        handling = EnumHandling.PreventDefault;
        if (byEntity.World.Side == EnumAppSide.Server && byEntity.World.BlockAccessor.GetBlockEntity(pos) is BEMeltingHole hole)
            hole.TryLight((byEntity as EntityPlayer)?.Player);
    }

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting) =>
        byEntity.World.BlockAccessor.GetBlockEntity(pos) is BEMeltingHole { Lit: true }
            ? secondsIgniting > 2 ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable
            : EnumIgniteState.NotIgnitable;
}
