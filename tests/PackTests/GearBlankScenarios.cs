using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod.Gears;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>The stainless gear blanks and their molds as these scenarios read them (seraphhorizons,
/// <c>GearBlanks</c>, mods-src/seraphhorizons/Gears/GearBlanks.cs). Shared with
/// <see cref="SwitchesOffScenarios"/>.</summary>
internal static class GearBlankParts
{
    public const string Stainless = "game:ingot-stainlesssteel";

    public static string Mold(string color, string state, string type) => $"seraphhorizons:toolmold-{color}-{state}-{type}";

    public static Item Item(IWorldAccessor world, string code) =>
        world.GetItem(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no item {code}");

    public static Block Block(IWorldAccessor world, string code) =>
        world.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? block : throw new Xunit.Sdk.XunitException($"no block {code}");

    public static SmithingRecipe Smithing(ICoreAPI api, string output) =>
        Assert.Single(api.GetSmithingRecipes(), r => r.Output.ResolvedItemstack?.Collectible.Code.ToString() == output);

    public static int Voxels(LayeredVoxelRecipe recipe) => recipe.Voxels.Cast<bool>().Count(v => v);

    /// <summary>The layers the recipe's pattern fills (QuantityLayers is the work space's height).</summary>
    public static int Layers(LayeredVoxelRecipe recipe) =>
        Enumerable.Range(0, recipe.Voxels.GetLength(1)).Count(y =>
            Enumerable.Range(0, recipe.Voxels.GetLength(0)).Any(x =>
                Enumerable.Range(0, recipe.Voxels.GetLength(2)).Any(z => recipe.Voxels[x, y, z])));

    /// <summary>A granite floor and air above it, a 5 x 5 room 3 high at <paramref name="pos"/>.</summary>
    public static void Room(IWorldSession world, BlockPos pos)
    {
        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
        {
            world.SetBlock("game:rock-granite", pos.AddCopy(dx, -1, dz));
            for (int dy = 0; dy < 3; dy++)
                world.SetBlock("game:air", pos.AddCopy(dx, dy, dz));
        }
    }

    /// <summary>A hot stainless steel ingot, workable at the anvil and liquid enough to pour.</summary>
    public static ItemStack HotIngot(IWorldAccessor world, float temperature = 1300f)
    {
        var stack = new ItemStack(Item(world, Stainless));
        stack.Collectible.SetTemperature(world, stack, temperature, false);
        return stack;
    }

    public static int Count(IPlayer player, string code) =>
        player.InventoryManager.Inventories.Values
            .Where(inv => inv.ClassName is not GlobalConstants.creativeInvClassName)
            .SelectMany(inv => inv)
            .Where(slot => slot.Itemstack?.Collectible.Code.ToString() == code)
            .Sum(slot => slot.StackSize);
}

// seraphhorizons, GearBlanks (mods-src/seraphhorizons/Gears/GearBlanks.cs, #479).
public partial class SharedWorldScenarios
{
    private static readonly string[] GearMoldTypes = [GearBlanks.MoldType, GearBlanks.LargeMoldType];

    [AtlasScenario]
    public void Gear_blanks_stack_and_store_on_the_ground()
    {
        foreach (var (code, name, layout) in new[]
                 {
                     (GearBlanks.Blank, "Stainless gear blank", EnumGroundStorageLayout.Quadrants),
                     (GearBlanks.LargeBlank, "Large stainless gear blank", EnumGroundStorageLayout.SingleCenter),
                 })
        {
            var item = GearBlankParts.Item(W, code);
            Assert.True(item.MaxStackSize > 1, code);
            var storable = Assert.Single(item.CollectibleBehaviors.OfType<CollectibleBehaviorGroundStorable>());
            Assert.Equal(layout, storable.StorageProps.Layout);
            Assert.Contains("items", item.CreativeInventoryTabs);
            Assert.Equal(name, new ItemStack(item).GetName());
            // The key the handbook looks up (CollectibleBehaviorHandbookTextAndExtraInfo): domain, class,
            // then the code with its domain.
            Assert.Contains("gear blank mold", Lang.GetMatchingIfExists($"{item.Code.Domain}:item-handbooktext-{item.Code.ToShortString()}"));
        }
    }

    // Both molds, raw in the three clays and fired in all ten colours the game's molds come in, the
    // raw one clay-formed (a floor and the walls round the cavity: one layer deep for the small blank,
    // two for the large) and fired in a pit kiln or a beehive kiln to the fired one.
    [AtlasScenario]
    public void Gear_blank_molds_are_clay_formed_and_fired()
    {
        var clayforming = World.Api.GetClayformingRecipes();
        foreach (string type in GearMoldTypes)
        {
            foreach (string clay in new[] { "blue", "fire", "red" })
            {
                var raw = GearBlankParts.Block(W, GearBlankParts.Mold(clay, "raw", type));
                var recipe = Assert.Single(clayforming, r => r.Output.ResolvedItemstack?.Collectible == raw);
                Assert.True(recipe.Ingredient.SatisfiesAsIngredient(new ItemStack(GearBlankParts.Item(W, "game:clay-" + clay))));
                Assert.Equal(type == GearBlanks.MoldType ? 2 : 3, GearBlankParts.Layers(recipe));

                var fired = raw.CombustibleProps!.SmeltedStack.ResolvedItemstack.Collectible;
                Assert.Equal(EnumSmeltType.Fire, raw.CombustibleProps.SmeltingType);
                Assert.IsType<BlockToolMold>(fired);
                Assert.Equal(("fired", type), (fired.Variant["materialtype"], fired.Variant["tooltype"]));
                for (int level = 0; level < 4; level++)
                {
                    var kiln = raw.Attributes["beehivekiln"][level.ToString()].AsObject<JsonItemStack>(null, "seraphhorizons");
                    Assert.True(kiln.Resolve(W, "gear blank mold kiln"), $"{raw.Code} beehive kiln {level}");
                    Assert.Equal(type, kiln.ResolvedItemstack.Collectible.Variant["tooltype"]);
                }
            }
            foreach (string color in new[] { "blue", "fire", "black", "brown", "cream", "earthyorange", "gray", "orange", "red", "tan" })
                Assert.IsType<BlockToolMold>(GearBlankParts.Block(W, GearBlankParts.Mold(color, "fired", type)));
        }
        Assert.Equal("Raw gear blank mold", new ItemStack(GearBlankParts.Block(W, GearBlankParts.Mold("red", "raw", GearBlanks.MoldType))).GetName());
        Assert.Equal("Large gear blank mold", new ItemStack(GearBlankParts.Block(W, GearBlankParts.Mold("tan", "fired", GearBlanks.LargeMoldType))).GetName());
    }

    // A fired mold on the ground takes molten stainless steel as a crucible pours it
    // (ILiquidMetalSink), refuses copper and plain steel (there is no blank of either), and once full
    // and hardened gives the blank to a player's empty hand through the block's own right-click,
    // leaving the empty mold in place. The small mold takes a quarter of an ingot, four to an ingot.
    [AtlasScenario]
    public async Task Gear_blank_mold_casts_a_blank_from_stainless_steel()
    {
        var player = (await World.JoinPlayer("blankcaster")).Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        var origin = World.Spawn.AddCopy(-90, 12, -90);
        GearBlankParts.Room(World, origin);
        player.Entity.TeleportTo(origin.ToVec3d().Add(0.5, 0, 2.5));
        await World.Ticks(2);

        foreach (var (type, units, blank, dx) in new[]
                 {
                     (GearBlanks.MoldType, GearBlanks.MoldUnits, GearBlanks.Blank, -1),
                     (GearBlanks.LargeMoldType, GearBlanks.LargeMoldUnits, GearBlanks.LargeBlank, 1),
                 })
        {
            var pos = origin.AddCopy(dx, 0, 0);
            World.SetBlock(GearBlankParts.Mold("brown", "fired", type), pos);
            await World.Ticks(2);
            var mold = W.BlockAccessor.GetBlockEntity(pos) as BlockEntityToolMold
                       ?? throw new Xunit.Sdk.XunitException($"no mold at {pos}: {W.BlockAccessor.GetBlock(pos).Code} on {W.BlockAccessor.GetBlock(pos.DownCopy()).Code}, entity {W.BlockAccessor.GetBlockEntity(pos)?.GetType().Name}");
            Assert.True(mold.CanReceiveAny);
            Assert.False(mold.CanReceive(new ItemStack(GearBlankParts.Item(W, "game:ingot-copper"))));
            Assert.False(mold.CanReceive(new ItemStack(GearBlankParts.Item(W, "game:ingot-steel"))));
            Assert.True(mold.CanReceive(new ItemStack(GearBlankParts.Item(W, GearBlankParts.Stainless))));

            // As a crucible pours: up to an ingot's worth (100 units) at a time; the small mold takes
            // 25 of an ingot's 100 and leaves the rest in the pour.
            for (int poured = 0; poured < units; poured += 100)
            {
                Assert.False(mold.IsFull);
                int amount = 100;
                mold.ReceiveLiquidMetal(GearBlankParts.HotIngot(W, 1600f), ref amount, 1600f);
                Assert.Equal(Math.Max(0, 100 - (units - poured)), amount);
            }
            Assert.True(mold.IsFull);
            Assert.Equal(units, mold.FillLevel);
            Assert.Null(mold.GetStateAwareMoldedStacks()); // still hot

            mold.MetalContent.Collectible.SetTemperature(W, mold.MetalContent, 20f, false);
            Assert.True(mold.IsHardened);
            var sel = new BlockSelection { Position = pos.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.1, 0.5) };
            Assert.True(W.BlockAccessor.GetBlock(pos).OnBlockInteractStart(W, player, sel));
            Assert.Equal(1, GearBlankParts.Count(player, blank));
            Assert.Equal(0, mold.FillLevel);
            Assert.Same(mold, W.BlockAccessor.GetBlockEntity(pos));
        }
    }

    // Steelmaking Expanded's canal pedestal takes a tool mold that its MoldKinds.FitsPedestal says fits
    // (any BlockToolMold but the anvil and helve hammer molds, which go on its tap) and drains the
    // canal into it by the mold's requiredUnits: so both gear blank molds, with nothing of this mod's.
    [AtlasScenario]
    public void Smex_canal_pedestal_takes_gear_blank_molds()
    {
        var kinds = HarmonyLib.AccessTools.TypeByName("SteelmakingExpanded.BlockNetworkMolten.Blocks.MoldKinds");
        Assert.NotNull(kinds);
        var fits = HarmonyLib.AccessTools.Method(kinds, "FitsPedestal");
        Assert.NotNull(fits);
        foreach (string type in GearMoldTypes)
        {
            var mold = GearBlankParts.Block(W, GearBlankParts.Mold("black", "fired", type));
            Assert.True((bool)fits.Invoke(null, [mold])!, type);
        }
        Assert.False((bool)fits.Invoke(null, [GearBlankParts.Block(W, "game:toolmold-black-fired-anvil")])!);
    }

    // One stainless steel ingot to two blanks, two to a large one: the large blank's 84 voxels are
    // exactly two ingots' worth, more than one gives (42), so the second ingot has to go on the work
    // piece. Stainless steel (tier 5) needs a tier 4 anvil: steel works it, iron does not.
    [AtlasScenario]
    public void Gear_blanks_are_smithed_from_stainless_steel_ingots()
    {
        var steel = new ItemStack(GearBlankParts.Item(W, GearBlankParts.Stainless));
        var iron = new ItemStack(GearBlankParts.Item(W, "game:ingot-steel"));
        var small = GearBlankParts.Smithing(World.Api, GearBlanks.Blank);
        var large = GearBlankParts.Smithing(World.Api, GearBlanks.LargeBlank);
        foreach (var recipe in new[] { small, large })
        {
            Assert.True(recipe.Ingredient.SatisfiesAsIngredient(steel));
            Assert.False(recipe.Ingredient.SatisfiesAsIngredient(iron));
            Assert.Equal("plate", recipe.Name.Path); // what lets the helve hammer work it
            Assert.Equal(1, GearBlankParts.Layers(recipe));
        }
        Assert.Equal(2, small.Output.ResolvedItemstack.StackSize);
        Assert.Equal(1, large.Output.ResolvedItemstack.StackSize);
        Assert.Equal(36, GearBlankParts.Voxels(small));
        Assert.Equal(84, GearBlankParts.Voxels(large));
        int tier = ((IAnvilWorkable)steel.Collectible).GetRequiredAnvilTier(steel);
        Assert.Equal(4, tier);

        Assert.True(GearBlankParts.Voxels(small) <= 42 && GearBlankParts.Voxels(large) > 42);
    }

    // The helve hammer works both blanks as it works a plate (ItemWorkItem.GetHelveWorkableMode):
    // from hot stainless steel on a steel anvil, hit after hit, to the finished blanks (two small
    // ones from an ingot). The large blank does not
    // finish from one ingot, and does once a second ingot is added to the work piece.
    [AtlasScenario]
    public async Task Helve_hammer_forges_gear_blanks()
    {
        var origin = World.Spawn.AddCopy(-90, 12, -100);
        // No player stands here, so make sure the chunk column is loaded before building in it.
        var sapi = (Vintagestory.API.Server.ICoreServerAPI)World.Api;
        int size = sapi.WorldManager.ChunkSize;
        sapi.WorldManager.LoadChunkColumnPriority(origin.X / size, origin.Z / size);
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(origin) != null
                                && W.BlockAccessor.GetChunkAtBlockPos(origin.AddCopy(2, 0, 0)) != null, 30000);
        GearBlankParts.Room(World, origin);
        var pos = origin.Copy();
        World.SetBlock("game:anvil-steel", pos);
        await World.Ticks(2);
        var anvil = Assert.IsType<BlockEntityAnvil>(W.BlockAccessor.GetBlockEntity(pos));
        var ingot = (ItemIngot)GearBlankParts.Item(W, GearBlankParts.Stainless);
        int required = ((IAnvilWorkable)ingot).GetRequiredAnvilTier(GearBlankParts.HotIngot(W));
        Assert.True(anvil.OwnMetalTier >= required, $"a steel anvil is tier {anvil.OwnMetalTier}, stainless steel needs {required}");
        World.SetBlock("game:anvil-iron", pos.AddCopy(2, 0, 0));
        await World.Ticks(2);
        var ironAnvil = Assert.IsType<BlockEntityAnvil>(W.BlockAccessor.GetBlockEntity(pos.AddCopy(2, 0, 0)));
        Assert.True(ironAnvil.OwnMetalTier < required, $"an iron anvil is tier {ironAnvil.OwnMetalTier}, stainless steel needs {required}");

        void Start(SmithingRecipe recipe)
        {
            var work = ingot.TryPlaceOn(GearBlankParts.HotIngot(W), anvil);
            Assert.NotNull(work);
            var tree = new TreeAttribute();
            anvil.ToTreeAttributes(tree);
            tree.SetItemstack("workItemStack", work);
            tree.SetInt("selectedRecipeId", recipe.RecipeId);
            tree.SetBytes("voxels", BlockEntityAnvil.serializeVoxels(anvil.Voxels));
            anvil.FromTreeAttributes(tree, W);
            Assert.True(anvil.CanWorkCurrent);
            Assert.Equal(EnumHelveWorkableMode.TestSufficientVoxelsWorkable,
                ((ItemWorkItem)anvil.WorkItemStack.Collectible).GetHelveWorkableMode(anvil.WorkItemStack, anvil));
        }

        bool Hammer()
        {
            for (int hit = 0; hit < 400 && anvil.WorkItemStack != null; hit++)
            {
                // Keep it hot: a real helve takes minutes, the stack cools by the game clock.
                anvil.WorkItemStack.Collectible.SetTemperature(W, anvil.WorkItemStack, 1300f, false);
                anvil.OnHelveHammerHit();
            }
            return anvil.WorkItemStack == null;
        }

        List<ItemStack> Dropped() =>
            W.GetEntitiesAround(pos.ToVec3d().Add(0.5, 0.5, 0.5), 3, 3, e => e is EntityItem && e.Alive)
                .Cast<EntityItem>().Select(e => e.Itemstack).ToList();

        Start(GearBlankParts.Smithing(World.Api, GearBlanks.Blank));
        Assert.True(Hammer(), "the small blank did not finish under the helve");
        await World.Ticks(2);
        Assert.Contains(Dropped(), s => s.Collectible.Code.ToString() == GearBlanks.Blank && s.StackSize == 2);

        Start(GearBlankParts.Smithing(World.Api, GearBlanks.LargeBlank));
        Assert.False(Hammer(), "the large blank finished from one ingot");
        Assert.NotNull(ingot.TryPlaceOn(GearBlankParts.HotIngot(W), anvil));
        Assert.True(Hammer(), "the large blank did not finish from two ingots");
        await World.Ticks(2);
        Assert.Contains(Dropped(), s => s.Collectible.Code.ToString() == GearBlanks.LargeBlank);
    }
}
