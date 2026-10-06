using Atlas.Api;
using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Gears;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>The steel gear blanks and their molds as these scenarios read them (seraphhorizons,
/// <c>GearBlanks</c>, mods-src/seraphhorizons/Gears/GearBlanks.cs). Shared with
/// <see cref="SwitchesOffScenarios"/>.</summary>
internal static class GearBlankParts
{
    public const string Steel = "game:ingot-steel";

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

    /// <summary>A hot steel ingot, workable at the anvil and liquid enough to pour.</summary>
    public static ItemStack HotIngot(IWorldAccessor world, float temperature = 1300f)
    {
        var stack = new ItemStack(Item(world, Steel));
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
                     (GearBlanks.Blank, "Steel gear blank", EnumGroundStorageLayout.Quadrants),
                     (GearBlanks.LargeBlank, "Large steel gear blank", EnumGroundStorageLayout.SingleCenter),
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
    // three for the large) and fired in a pit kiln or a beehive kiln to the fired one.
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
                Assert.Equal(type == GearBlanks.MoldType ? 2 : 4, GearBlankParts.Layers(recipe));

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

    // A fired mold on the ground takes molten steel as a crucible pours it (ILiquidMetalSink), refuses
    // copper (there is no copper blank), and once full and hardened gives the blank to a player's
    // empty hand through the block's own right-click, leaving the empty mold in place.
    [AtlasScenario]
    public async Task Gear_blank_mold_casts_a_blank_from_steel()
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
            Assert.True(mold.CanReceive(new ItemStack(GearBlankParts.Item(W, GearBlankParts.Steel))));

            // As a crucible pours: an ingot's worth (100 units) at a time.
            for (int poured = 0; poured < units; poured += 100)
            {
                Assert.False(mold.IsFull);
                int amount = 100;
                mold.ReceiveLiquidMetal(GearBlankParts.HotIngot(W, 1600f), ref amount, 1600f);
                Assert.Equal(0, amount);
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

    // One steel ingot to a blank, two to a large one: the large blank's 84 voxels are exactly two
    // ingots' worth, more than one gives (42), so the second ingot has to go on the work piece.
    [AtlasScenario]
    public void Gear_blanks_are_smithed_from_steel_ingots()
    {
        var steel = new ItemStack(GearBlankParts.Item(W, GearBlankParts.Steel));
        var iron = new ItemStack(GearBlankParts.Item(W, "game:ingot-iron"));
        var small = GearBlankParts.Smithing(World.Api, GearBlanks.Blank);
        var large = GearBlankParts.Smithing(World.Api, GearBlanks.LargeBlank);
        foreach (var recipe in new[] { small, large })
        {
            Assert.True(recipe.Ingredient.SatisfiesAsIngredient(steel));
            Assert.False(recipe.Ingredient.SatisfiesAsIngredient(iron));
            Assert.Equal("plate", recipe.Name.Path); // what lets the helve hammer work it
            Assert.Equal(1, GearBlankParts.Layers(recipe));
        }
        Assert.Equal(36, GearBlankParts.Voxels(small));
        Assert.Equal(84, GearBlankParts.Voxels(large));
        Assert.True(GearBlankParts.Voxels(small) <= 42 && GearBlankParts.Voxels(large) > 42);
    }

    // The helve hammer works both blanks as it works a plate (ItemWorkItem.GetHelveWorkableMode):
    // from hot steel on a steel anvil, hit after hit, to the finished blank. The large blank does not
    // finish from one ingot, and does once a second ingot is added to the work piece.
    [AtlasScenario]
    public async Task Helve_hammer_forges_gear_blanks()
    {
        var origin = World.Spawn.AddCopy(-90, 12, -100);
        GearBlankParts.Room(World, origin);
        var pos = origin.Copy();
        World.SetBlock("game:anvil-steel", pos);
        await World.Ticks(2);
        var anvil = Assert.IsType<BlockEntityAnvil>(W.BlockAccessor.GetBlockEntity(pos));
        var ingot = (ItemIngot)GearBlankParts.Item(W, GearBlankParts.Steel);

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
        Assert.Contains(Dropped(), s => s.Collectible.Code.ToString() == GearBlanks.Blank);

        Start(GearBlankParts.Smithing(World.Api, GearBlanks.LargeBlank));
        Assert.False(Hammer(), "the large blank finished from one ingot");
        Assert.NotNull(ingot.TryPlaceOn(GearBlankParts.HotIngot(W), anvil));
        Assert.True(Hammer(), "the large blank did not finish from two ingots");
        await World.Ticks(2);
        Assert.Contains(Dropped(), s => s.Collectible.Code.ToString() == GearBlanks.LargeBlank);
    }

    // The export has both routes: the molds' clay-forming recipes and the raw mold's firing to the
    // fired one (the casting itself is the fired mold's drop, which the export does not model for any
    // mold; the blank's description says it), and the two smithing recipes.
    [AtlasScenario(TimeoutMs = 900_000)]
    public void Gear_blank_routes_are_in_the_export()
    {
        var doc = ExportUnderTest.Get(World.Api);
        var recipes = ((JArray)doc["recipes"]!).Cast<JObject>().ToList();
        JObject Of(string type, string output) => Assert.Single(recipes, r => (string?)r["type"] == type
            && r["outputs"]!.Any(o => (string?)o["code"] == output));

        var smallSmith = Of("smithing", "seraphhorizons:gearblank-{metal}");
        var largeSmith = Of("smithing", "seraphhorizons:largegearblank-{metal}");
        foreach (var r in new[] { smallSmith, largeSmith })
        {
            Assert.Equal("seraphhorizons", (string?)r["mod"]);
            Assert.Equal("game:ingot-steel", (string?)Assert.Single(r["variants"]!)["ingredients"]![0]![0]!["code"]);
        }
        foreach (string type in GearMoldTypes)
        {
            var clay = Of("clayforming", $"seraphhorizons:toolmold-{{color}}-raw-{type}");
            Assert.Equal(3, clay["variants"]!.Count());
        }

        var items = (JObject)doc["items"]!;
        foreach (string code in new[] { GearBlanks.Blank, GearBlanks.LargeBlank })
            Assert.Contains("gear blank mold", (string?)items[code]?["description"]);
        foreach (string type in GearMoldTypes)
            Assert.Equal(GearBlankParts.Mold("blue", "fired", type),
                (string?)items[GearBlankParts.Mold("blue", "raw", type)]?["attributes"]?["smelting"]?["output"]?["code"]);
    }
}
