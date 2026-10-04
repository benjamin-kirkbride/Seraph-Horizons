using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>A Food Shelves barrel rack on a granite floor and a player using it with their own
/// hands, through the rack block's <c>OnBlockInteractStart</c> as the game calls it for a
/// right-click. Shared with <see cref="SwitchesOffScenarios"/>.</summary>
internal sealed class RackSite(IWorldSession world, BlockPos pos, ITestPlayer player)
{
    public const string Rack = "foodshelves:barrelrack-normal-east";
    public const string Untapped = "hydrateordiedrate:keg-untapped";
    public const string Tapped = "hydrateordiedrate:keg-tapped";
    public const string Barrel = "game:barrel";
    public const string Water = "game:waterportion";
    public const int WaterPerLitre = 100;

    public BlockPos Pos { get; } = pos;
    private IWorldAccessor W => world.Api.World;

    public async Task Build()
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            world.SetBlock("game:rock-granite", Pos.AddCopy(dx, -1, dz));
            for (int dy = 0; dy < 3; dy++)
                world.SetBlock("game:air", Pos.AddCopy(dx, dy, dz));
        }
        world.SetBlock(Rack, Pos);
        await world.Ticks(2);
        Assert.NotNull(Entity);
    }

    public BlockLiquidContainerBase RackBlock => (BlockLiquidContainerBase)W.BlockAccessor.GetBlock(Pos);
    public BlockEntityContainer Entity => (BlockEntityContainer)W.BlockAccessor.GetBlockEntity(Pos)!;
    public ItemSlot Cask => Entity.Inventory[0];
    public ItemSlot Liquid => Entity.Inventory[1];
    public ItemSlot Hand => player.Player.InventoryManager.ActiveHotbarSlot;

    public ItemStack Stack(string code, int size = 1) =>
        W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? new ItemStack(block, size)
        : new ItemStack(W.GetItem(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no {code}"), size);

    /// <summary>A keg item of <paramref name="code"/> holding <paramref name="litres"/> of water.</summary>
    public ItemStack Keg(string code, int litres)
    {
        var keg = Stack(code);
        if (litres > 0)
            ((BlockLiquidContainerBase)keg.Block).SetContent(keg, Stack(Water, litres * WaterPerLitre));
        return keg;
    }

    /// <summary>Right-clicks the rack holding <paramref name="held"/> (null: an empty hand).
    /// Returns what the game's interaction returned.</summary>
    public bool RightClick(ItemStack? held)
    {
        Hand.Itemstack = held;
        Hand.MarkDirty();
        var sel = new BlockSelection { Position = Pos.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        return W.BlockAccessor.GetBlock(Pos).OnBlockInteractStart(W, player.Player, sel);
    }

    /// <summary>The rack's liquid in litres.</summary>
    public float Litres => RackBlock.GetCurrentLitres(Pos);

    public static float LitresIn(ItemStack container) =>
        ((BlockLiquidContainerBase)container.Block).GetCurrentLitres(container);

    /// <summary>The player's hotbar, backpack and mouse slot (the creative inventory cannot be
    /// enumerated on a server).</summary>
    private IEnumerable<IInventory> Carried() =>
        player.Player.InventoryManager.InventoriesOrdered.Where(inv => inv.ClassName
            is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName or GlobalConstants.mousecursorInvClassName);

    /// <summary>Everything the player carries, and loose items around the rack.</summary>
    public List<ItemStack> PlayerStacks() =>
        Carried().SelectMany(inv => inv)
            .Where(s => !s.Empty).Select(s => s.Itemstack!).ToList();

    public List<ItemStack> Loose() =>
        W.GetEntitiesAround(Pos.ToVec3d().Add(0.5, 0.5, 0.5), 4, 4, e => e is EntityItem && e.Alive)
            .Cast<EntityItem>().Select(e => e.Itemstack).ToList();

    public void ClearPlayer()
    {
        foreach (var slot in Carried().SelectMany(inv => inv))
        {
            slot.Itemstack = null;
            slot.MarkDirty();
        }
    }

    /// <summary>Whether the rack's restriction marked <paramref name="code"/> as a barrel rack cask.</summary>
    public bool Takes(string code) => Stack(code).Collectible.Attributes?["fsbarrelrack"].AsBool() == true;

    public static bool Patched() =>
        Harmony.GetPatchInfo(AccessTools.Method(AccessTools.TypeByName(BarrelRackKegs.RackEntityType), "OnInteract"))
            ?.Postfixes.Any(p => p.owner == BarrelRackKegs.HarmonyId) == true;
}

/// <summary>
/// mods-src/seraphhorizons, BarrelRackKegs: Food Shelves' barrel rack takes Hydrate or Diedrate's
/// kegs, holding a keg's worth, with the liquid moving between the keg item and the rack, and
/// perishing at the keg's rate times the rack's. Vanilla barrels in the rack stay as they ship.
/// </summary>
public partial class SharedWorldScenarios
{
    private async Task<RackSite> Site(string player, int dx)
    {
        var p = await World.JoinPlayer(player);
        var pos = World.Spawn.AddCopy(dx, 12, 40);
        await p.TeleportTo(pos.AddCopy(2, 0, 0));
        var site = new RackSite(World, pos, p);
        await site.Build();
        site.ClearPlayer();
        return site;
    }

    [AtlasScenario]
    public void The_rack_takes_both_kegs_and_still_barrels()
    {
        var site = new RackSite(World, World.Spawn, null!);
        Assert.True(RackSite.Patched());
        Assert.True(site.Takes(RackSite.Untapped));
        Assert.True(site.Takes(RackSite.Tapped));
        Assert.True(site.Takes(RackSite.Barrel));
        // Its capacity is the keg's (Hydrate's KegCapacityLitres, 100 by default), and the rack's own 50.
        Assert.Equal(100f, ((BlockLiquidContainerBase)site.Stack(RackSite.Untapped).Block).CapacityLitres);
        Assert.Equal(50f, ((BlockLiquidContainerBase)World.Api.World.GetBlock(new AssetLocation(RackSite.Rack))!).CapacityLitres);
        // Food Shelves' English text names kegs (fails after a Food Shelves update that rewords it).
        foreach (var edit in BarrelRackKegs.LangEdits)
            Assert.Contains(edit.New, Lang.GetL(edit.Language, edit.Key));
    }

    [AtlasScenario]
    public async Task A_keg_goes_in_with_its_liquid_fills_to_a_kegs_worth_and_leaves_with_it()
    {
        var site = await Site("kegracker", 0);

        // In: the keg's 80 L move to the rack's liquid slot, the racked item holds none.
        Assert.True(site.RightClick(site.Keg(RackSite.Untapped, 80)));
        Assert.True(site.Hand.Empty);
        Assert.Equal(RackSite.Untapped, site.Cask.Itemstack?.Collectible.Code.ToString());
        Assert.Equal(0f, RackSite.LitresIn(site.Cask.Itemstack!));
        Assert.Equal(80f, site.Litres);

        // Filling: up to the keg's 100 L, past the rack's own 50.
        var water = site.Stack(RackSite.Water, 50 * RackSite.WaterPerLitre);
        int moved = site.RackBlock.TryPutLiquid(site.Pos, water, 50);
        Assert.Equal(20 * RackSite.WaterPerLitre, moved);
        Assert.Equal(100f, site.Litres);
        Assert.Equal(0, site.RackBlock.TryPutLiquid(site.Pos, site.Stack(RackSite.Water, 100), 1));
        // The rack block's own capacity is back after the pour.
        Assert.Equal(50f, site.RackBlock.CapacityLitres);

        // Out with an empty hand: the keg leaves holding all 100 L, the rack is empty.
        Assert.True(site.RightClick(null));
        Assert.True(site.Entity.Inventory.Empty);
        var kegs = site.PlayerStacks().Concat(site.Loose()).Where(s => s.Collectible.Code.ToString() == RackSite.Untapped).ToList();
        var keg = Assert.Single(kegs);
        Assert.Equal(100f, RackSite.LitresIn(keg));
    }

    [AtlasScenario]
    public async Task A_racked_keg_perishes_at_its_own_rate_times_the_racks()
    {
        var site = await Site("kegperish", 8);
        var probe = site.Stack(RackSite.Water, 100);
        float Speed() => site.Entity.Inventory.GetTransitionSpeedMul(EnumTransitionType.Perish, probe);

        Assert.True(site.RightClick(site.Stack(RackSite.Barrel)));
        float barrel = Speed();
        Assert.True(site.RightClick(null));

        Assert.True(site.RightClick(site.Keg(RackSite.Untapped, 0)));
        float untapped = Speed();
        Assert.True(site.RightClick(null));

        Assert.True(site.RightClick(site.Keg(RackSite.Tapped, 0)));
        float tapped = Speed();
        Assert.True(site.RightClick(null));

        output.WriteLine($"perish speed in the rack: barrel {barrel}, untapped keg {untapped}, tapped keg {tapped}");
        // Hydrate's defaults: SpoilRateUntapped 0.15, SpoilRateTapped 0.65; a barrel's own rate is 1.
        Assert.True(barrel > 0f);
        Assert.Equal(0.15f, untapped / barrel, 3);
        Assert.Equal(0.65f, tapped / barrel, 3);
    }

    [AtlasScenario]
    public async Task A_barrel_in_the_rack_still_holds_50_litres_and_must_be_emptied()
    {
        var site = await Site("barrelracker", 16);
        Assert.True(site.RightClick(site.Stack(RackSite.Barrel)));
        int moved = site.RackBlock.TryPutLiquid(site.Pos, site.Stack(RackSite.Water, 60 * RackSite.WaterPerLitre), 60);
        Assert.Equal(50 * RackSite.WaterPerLitre, moved);
        Assert.Equal(50f, site.Litres);
        // As Food Shelves ships it: a barrel with liquid stays in the rack.
        Assert.False(site.RightClick(null));
        Assert.Equal(RackSite.Barrel, site.Cask.Itemstack?.Collectible.Code.ToString());
        Assert.Equal(50f, site.Litres);
    }

    [AtlasScenario]
    public async Task Breaking_the_rack_drops_the_keg_with_its_liquid()
    {
        var site = await Site("kegbreaker", 24);
        Assert.True(site.RightClick(site.Keg(RackSite.Tapped, 30)));
        Assert.Equal(30f, site.Litres);
        site.RackBlock.OnBlockBroken(World.Api.World, site.Pos, null!);
        await World.Ticks(2);
        var loose = site.Loose();
        foreach (var stack in loose)
            output.WriteLine($"dropped {stack.StackSize}x {stack.Collectible.Code}");
        var keg = Assert.Single(loose, s => s.Collectible.Code.ToString() == RackSite.Tapped);
        Assert.Equal(30f, RackSite.LitresIn(keg));
        Assert.DoesNotContain(loose, s => s.Collectible.Code.ToString() == RackSite.Water);
    }
}
