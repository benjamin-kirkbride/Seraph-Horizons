using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>The pack's two tuns as these scenarios read them: Hydrate or Diedrate's
/// (<c>hydrateordiedrate:tun-*</c>) and Food Shelves' tun rack (<c>foodshelves:tunrack-*</c>), each
/// placed in clear air and filled with water. Shared with <see cref="SwitchesOffScenarios"/>.</summary>
internal static class Tuns
{
    public const string HydrateTun = SeraphHorizons.Mod.HydrateTun.Block;
    public const string Rack = "foodshelves:tunrack-normal-north";
    public const string RackTun = "foodshelves:tun-normal";
    public const string Water = "game:waterportion";

    public static Block Block(IWorldAccessor world, string code) =>
        world.GetBlock(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no block {code}");

    public static bool InCreative(Block block) =>
        block.CreativeInventoryTabs is { Length: > 0 } || block.CreativeInventoryStacks is { Length: > 0 };

    public static bool HandbookExcluded(Block block) => block.Attributes?["handbook"]?["exclude"].AsBool() == true;

    public static IEnumerable<GridRecipe> Recipes(IWorldAccessor world, string domain, string pathPrefix) =>
        world.GridRecipes.Where(r => r.Output?.Code is { } c && c.Domain == domain && c.Path.StartsWith(pathPrefix));

    public static bool RackPatched() =>
        Harmony.GetPatchInfo(AccessTools.DeclaredConstructor(AccessTools.TypeByName(TunRackCapacity.RackType), Type.EmptyTypes))
            ?.Postfixes.Any(p => p.owner == SeraphHorizonsSystem.HarmonyId) == true;

    /// <summary>Clears a 4 x 3 x 4 room of air on a granite floor at <paramref name="pos"/> and
    /// places <paramref name="code"/> there (the master cell only, as SetBlock does).</summary>
    public static async Task<BlockEntity> Place(IWorldSession world, string code, BlockPos pos)
    {
        for (int dx = -1; dx <= 2; dx++)
        for (int dz = -1; dz <= 2; dz++)
        {
            world.SetBlock("game:rock-granite", pos.AddCopy(dx, -1, dz));
            for (int dy = 0; dy < 3; dy++)
                world.SetBlock("game:air", pos.AddCopy(dx, dy, dz));
        }
        world.SetBlock(code, pos);
        await world.Ticks(2);
        return world.Api.World.BlockAccessor.GetBlockEntity(pos)
               ?? throw new Xunit.Sdk.XunitException($"{code} placed without a block entity");
    }

    /// <summary>Pours far more water than any tun holds into the container at <paramref name="pos"/>
    /// through the block's own <c>TryPutLiquid</c> (what a bucket or a pipe uses), and returns the
    /// litres it then holds.</summary>
    public static float FillWithWater(IWorldAccessor world, BlockPos pos)
    {
        var block = (BlockLiquidContainerBase)world.BlockAccessor.GetBlock(pos);
        var water = new ItemStack(world.GetItem(new AssetLocation(Water)), 300_000);
        block.TryPutLiquid(pos, water, 3000);
        return block.GetCurrentLitres(pos);
    }

    /// <summary>The tun rack's capacity as each of its two places has it: the block's, and the
    /// block entity's own field and liquid slot.</summary>
    public static (float Block, int Field, float Slot) RackCapacity(BlockEntityContainer rack)
    {
        var block = (BlockLiquidContainerBase)rack.Block;
        int field = (int)AccessTools.Field(rack.GetType(), TunRackCapacity.CapacityField).GetValue(rack)!;
        float slot = ((ItemSlotLiquidOnly)rack.Inventory[TunRackCapacity.LiquidSlot]).CapacityLitres;
        return (block.CapacityLitres, field, slot);
    }
}

/// <summary>
/// mods-src/seraphhorizons, HydrateTun and TunRackCapacity: Hydrate or Diedrate's tun can no longer
/// be made and is out of the creative inventory and the handbook, but a placed one still works; Food
/// Shelves' tun rack holds 950 litres, in the block and the block entity alike.
/// </summary>
[AtlasWorld]
public class TunScenarios : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;

    [AtlasScenario]
    public async Task Hydrate_tun_is_retired_but_a_placed_one_still_works()
    {
        var tun = Tuns.Block(W, Tuns.HydrateTun);
        Assert.Empty(Tuns.Recipes(W, "hydrateordiedrate", "tun"));
        Assert.False(Tuns.InCreative(tun));
        Assert.True(Tuns.HandbookExcluded(tun));

        // Its classes are still Hydrate or Diedrate's, so a tun in an existing world loads and holds
        // its liquid as before (Hydrate or Diedrate's TunCapacityLitres, 950 by default).
        var pos = World.Spawn.AddCopy(40, 12, 40);
        var be = await Tuns.Place(World, Tuns.HydrateTun, pos);
        Assert.Equal("HydrateOrDiedrate.Keg.BlockEntityTun", be.GetType().FullName);
        Assert.Equal(950f, Tuns.FillWithWater(W, pos));
    }

    [AtlasScenario]
    public async Task Tun_rack_holds_950_litres()
    {
        Assert.True(Tuns.RackPatched());
        Assert.Equal(TunRackCapacity.Litres, ((BlockLiquidContainerBase)Tuns.Block(W, Tuns.Rack)).CapacityLitres);
        // Food Shelves' tun and rack stay craftable.
        Assert.NotEmpty(Tuns.Recipes(W, "foodshelves", "tun-"));
        Assert.NotEmpty(Tuns.Recipes(W, "foodshelves", "tunrack-"));

        var pos = World.Spawn.AddCopy(-40, 12, 40);
        var rack = (BlockEntityContainer)await Tuns.Place(World, Tuns.Rack, pos);
        Assert.Equal((950f, 950, 950f), Tuns.RackCapacity(rack));
        rack.Inventory[0].Itemstack = new ItemStack(Tuns.Block(W, Tuns.RackTun));
        Assert.Equal(950f, Tuns.FillWithWater(W, pos));
    }
}
