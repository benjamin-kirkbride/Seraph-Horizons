using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Ore processing's items, crushing and smelting (#686, #687, #688; README "Ore processing: items,
/// crushing and smelting"), on a server of its own with the <c>OreProcessing</c> switch on
/// (fixtures/oreprocessing; it is off by default, and its off check is in
/// <see cref="SwitchesOffScenarios"/>).
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/oreprocessing", TargetPath = "ModConfig")]
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public class OreProcessingScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;

    private Item Item(string code) =>
        W.GetItem(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no item {code}");

    private ItemStack Stack(string code, int size) => new(Item(code), size);

    private OreProcessingSystem System => OreProcessingSystem.Of(World.Api);

    [AtlasScenario(TimeoutMs = 120_000), ReadsBootLog]
    public async Task Boots_with_ore_processing_on_without_errors()
    {
        await World.Ticks(5);
        Assert.True(SeraphHorizonsSystem.ConfigFor(World.Api).OreProcessing);
        var errors = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => !PackLock.KnownErrors.Any(k => k.IsMatch(e.Message)))
            .Select(e => $"[{e.Level}] {e.DescribeSource()}: {e.Message}")
            .ToList();
        Assert.True(errors.Count == 0, "Errors logged:\n" + string.Join("\n", errors));
        // Nothing of ours failed to resolve, and no ore processing warning (smex, the alloy maths, the figures).
        var ours = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error)
            .Where(e => e.Message.Contains("Ore processing", StringComparison.Ordinal)
                        || e.Message.Contains("crushed-", StringComparison.Ordinal)
                        || e.Message.Contains("concentrate-", StringComparison.Ordinal))
            .Select(e => e.Message).ToList();
        Assert.True(ours.Count == 0, "Logged:\n" + string.Join("\n", ours));
        output.WriteLine(string.Join(", ", System.RetargetedRecipes));
    }

    [AtlasScenario]
    public void Items_exist_one_per_ore()
    {
        foreach (var ore in OreProducts.Ores)
        {
            foreach (var grain in new[] { OreGrain.Coarse, OreGrain.Fine })
                Assert.IsType<ItemOreProduct>(Item(OreProducts.CrushedCode(ore, grain)));
            Assert.IsType<ItemOreProduct>(Item(OreProducts.GroundCode(ore)));
            Assert.IsType<ItemOreProduct>(Item(OreProducts.ConcentrateCode(ore)));
        }
        Assert.NotNull(Item(OreProducts.RoastedCode("galena")));
        Assert.NotNull(Item(OreProducts.RoastedCode("galena_nativesilver")));
        Assert.Null(W.GetItem(new AssetLocation(OreProducts.RoastedCode("hematite"))));
        Assert.NotNull(Item(OreProducts.AmalgamCode("quartz_nativegold")));
        Assert.Equal(64, Item(OreProducts.LithargeCode).MaxStackSize);
        Assert.Equal(16, Item(OreProducts.CrushedCode("galena", OreGrain.Coarse)).MaxStackSize);
        Assert.Equal(128, Item(OreProducts.ConcentrateCode("galena")).MaxStackSize);
        // Vanilla's own crushed items stay as they are.
        Assert.NotNull(Item("game:crushed-quartz"));
        Assert.NotNull(Item("game:crushed-chromite"));
        Assert.Equal("Coarse crushed Chromite", new ItemStack(Item(OreProducts.CrushedCode("chromite", OreGrain.Coarse))).GetName());
        Assert.Equal("Galena concentrate", new ItemStack(Item(OreProducts.ConcentrateCode("galena"))).GetName());
    }

    // Raw ore and chunks are vanilla's ore item, by grade; its metalUnits are vanilla's.
    [AtlasScenario]
    public void Ore_items_by_grade()
    {
        var poor = Item("game:ore-poor-galena-shale");
        var medium = Item("game:ore-medium-galena-shale");
        var rich = Item("game:ore-rich-galena-shale");
        var bountiful = Item("game:ore-bountiful-galena-shale");
        foreach (var item in new[] { poor, medium, rich, bountiful })
            Assert.IsType<ItemGradedOre>(item);
        Assert.Equal([4, 4, 16, 16], new[] { poor, medium, rich, bountiful }.Select(i => i.MaxStackSize));
        Assert.Equal([15, 20, 25, 35], new[] { poor, medium, rich, bountiful }.Select(i => i.Attributes["metalUnits"].AsInt()));
        Assert.Equal(20, Item("game:ore-poor-hematite-granite").Attributes["metalUnits"].AsInt());
        Assert.Null(poor.CombustibleProps);
        Assert.Null(medium.CombustibleProps);
        Assert.Equal("Raw Galena ore (medium)", new ItemStack(medium).GetName());
        Assert.Equal("Galena chunk (rich)", new ItemStack(rich).GetName());
        // A hammer no longer breaks it into nuggets where it lies.
        Assert.False(((IContainedInteractable)medium).OnContainedInteractStart(null!, null!, null!, null!));
        Assert.Empty(((IContainedInteractable)medium).GetContainedInteractionHelp(null!, null!, null!, null!));
    }

    // Deposit measurement (sizes, tiers, map prices, district veins) reads the blocks' drops times
    // their metalUnits: a medium ore block still measures 1.25 × its units plus the crystallised ore.
    [AtlasScenario]
    public void Ore_block_still_measures_its_drops()
    {
        var block = W.GetBlock(new AssetLocation("game:ore-medium-galena-shale"));
        Assert.NotNull(block);
        double expected = 1.25 * 20 + 0.01 * Item("game:crystalizedore-medium-galena-shale").Attributes["metalUnits"].AsDouble();
        Assert.Equal(expected, DepositService.BlockUnits(block!), 6);
        var chromite = W.GetBlock(new AssetLocation("game:ore-medium-chromite-peridotite"))!;
        Assert.Equal(1.25 * 20, chromite.Drops.Where(d => d.ResolvedItemstack.Collectible.Code.Path.StartsWith("ore-")).Sum(d => d.Quantity.avg * 20), 6);
    }

    // Crushing: units ÷ 5, no loss, fine from poor ore, coarse from the rest and from nuggets.
    [AtlasScenario]
    public void Crushing_is_units_over_five()
    {
        void Crushes(string input, string output, int count)
        {
            var props = Item(input).CrushingProps;
            Assert.NotNull(props);
            Assert.Equal(output, props!.CrushedStack.ResolvedItemstack.Collectible.Code.ToString());
            Assert.Equal(count, props.CrushedStack.ResolvedItemstack.StackSize);
            Assert.Equal(1f, props.Quantity.avg);
            Assert.Equal(0f, props.Quantity.var);
        }
        Crushes("game:ore-medium-chromite-peridotite", "game:crushed-chromite-coarse", 4);
        Crushes("game:ore-poor-chromite-peridotite", "game:crushed-chromite-fine", 3);
        Crushes("game:ore-bountiful-hematite-granite", "game:crushed-hematite-coarse", 8);
        Crushes("game:ore-poor-cassiterite-granite", "game:crushed-cassiterite-fine", 1);
        Crushes("game:ore-rich-nativecopper-basalt", "game:crushed-nativecopper-coarse", 5);
        // Expanded Matter's nugget crushing (and smex's, which EM switches off) replaced.
        Crushes("game:nugget-hematite", "game:crushed-hematite-coarse", 1);
        Crushes("game:nugget-nativecopper", "game:crushed-nativecopper-coarse", 1);
        Crushes("game:nugget-nativegold", "game:crushed-quartz_nativegold-coarse", 1);
        Crushes("game:nugget-chalcopyrite", "game:crushed-chalcopyrite-coarse", 1);
        // The quern takes no ore form.
        foreach (var code in new[] { "game:ore-medium-galena-shale", "game:nugget-galena", "game:crushed-galena-coarse", "seraphhorizons:concentrate-galena" })
            Assert.Null(Item(code).GrindingProps);
    }

    // The pulverizer crushes by the item's crushing properties: a medium chromite raw ore in its
    // slot comes out as 4 coarse crushed chromite.
    [AtlasScenario]
    public void Pulverizer_takes_the_rule() =>
        Assert.True(Item("game:ore-medium-chromite-peridotite").CrushingProps!.HardnessTier <= 4);

    // No grid recipe hammers ore into nuggets.
    [AtlasScenario]
    public void No_grid_recipe_makes_nuggets_from_ore()
    {
        Assert.True(System.NuggetRecipesOff > 0);
        Assert.DoesNotContain(W.GridRecipes, r => r.Enabled && r.Output?.Code?.Path?.StartsWith("nugget-") == true
                                                  && r.Ingredients.Values.Any(i => i.Code?.Path?.Contains("ore-") == true));
    }

    // Recipes that took a crushed ore nothing makes now take the new crushed ore, either grain.
    [AtlasScenario]
    public void Recipes_take_the_new_crushed_ore()
    {
        var coarse = Stack("game:crushed-chromite-coarse", 1);
        var fine = Stack("game:crushed-ilmenite-fine", 1);
        var barrel = World.Api.GetBarrelRecipes().Where(r => r.Code == "dilutedchromite").ToList();
        Assert.NotEmpty(barrel);
        Assert.All(barrel, r => Assert.Contains(r.Ingredients, i => i.SatisfiesAsIngredient(coarse, false)));
        var bricks = W.GridRecipes.Where(r => r.Output?.Code?.Path == "refractorybrick-raw-tier3").ToList();
        Assert.Contains(bricks, r => r.Ingredients.Values.Any(i => i.SatisfiesAsIngredient(fine, false)));
        Assert.Contains(bricks, r => r.Ingredients.Values.Any(i => i.SatisfiesAsIngredient(coarse, false)));
        Assert.Contains("em:recipes/barrel/verdigris.json", System.RetargetedRecipes);
    }

    // The game's crucible (firepit) and alloys: concentrate whole, crushed ore and chunks a half,
    // raw and ground ore and unroasted sulfide concentrate not at all.
    [AtlasScenario]
    public void Crucible_smelts_by_form()
    {
        double Ingots(params ItemStack[] stacks) => BlockSmeltingContainer.GetSingleSmeltableStack(stacks)?.stackSize ?? 0;
        Assert.Equal(1.0, Ingots(Stack("seraphhorizons:concentrate-malachite", 20)), 6);
        Assert.Equal(1.0, Ingots(Stack("seraphhorizons:roastedconcentrate-galena", 20)), 6);
        Assert.Equal(0.5, Ingots(Stack("game:crushed-malachite-coarse", 20)), 6);
        Assert.Equal(0.5, Ingots(Stack("game:crushed-malachite-fine", 20)), 6);
        Assert.Equal(16 * 25 * 0.5 / 100, Ingots(Stack("game:ore-rich-malachite-limestone", 16)), 6);
        Assert.Equal(16 * 35 * 0.5 / 100, Ingots(Stack("game:ore-bountiful-malachite-limestone", 16)), 6);
        // Cassiterite holds less: a rich chunk 15 units, so 7.5 smelted (3 ingots per 40 chunks).
        Assert.Equal(16 * 15 * 0.5 / 100, Ingots(Stack("game:ore-rich-cassiterite-granite", 16)), 6);
        Assert.Equal(0, Ingots(Stack("seraphhorizons:concentrate-galena", 20)));
        Assert.Equal(0, Ingots(Stack("seraphhorizons:groundore-malachite", 20)));
        Assert.Null(Item("game:ore-medium-malachite-limestone").CombustibleProps);
        Assert.Equal(20.0 / 21, Ingots(Stack("seraphhorizons:litharge", 20)), 6);
        // A chunk fits the crucible, and the concentrate goes in it as a nugget does.
        Assert.True(Item("game:ore-rich-malachite-limestone").Dimensions.Width <= 0.125f);
        Assert.Equal(EnumItemStorageFlags.Metallurgy, Item("seraphhorizons:concentrate-malachite").StorageFlags & EnumItemStorageFlags.Metallurgy);

        // Tin bronze: 16 bountiful copper chunks (280 units at half) and 6 tin concentrate (30): the
        // alloy maths counts the chunk's 7-in-40 rate whole (AlloyStackSize).
        var bronze = World.Api.GetMetalAlloys().Single(a => a.Output.ResolvedItemstack.Collectible.Code.Path == "ingot-tinbronze");
        ItemStack[] charge = [Stack("game:ore-bountiful-nativecopper-basalt", 16), Stack("seraphhorizons:concentrate-cassiterite", 6)];
        Assert.True(bronze.Matches(charge));
        Assert.Equal(3.1, bronze.GetTotalOutputQuantity(charge), 6);
    }

    // The bloomery takes what melts between 1000 and 1500 °C into a bloom, items per bloom by the ratio.
    [AtlasScenario]
    public void Bloomery_takes_iron_concentrate()
    {
        var props = Item("seraphhorizons:concentrate-hematite").CombustibleProps!;
        Assert.InRange(props.MeltingPoint, 1000, 1499);
        Assert.Equal("game:ironbloom", props.SmeltedStack.ResolvedItemstack.Collectible.Code.ToString());
        Assert.Equal(20, props.SmeltedRatio);
        Assert.Equal(40, Item("game:crushed-hematite-coarse").CombustibleProps!.SmeltedRatio);
        Assert.Null(Item("seraphhorizons:concentrate-pyrite").CombustibleProps);
    }

    // Crucibulum's forge counts a charge as the crucible does.
    [AtlasScenario]
    public async Task Crucibulum_forge_takes_concentrate()
    {
        var pos = World.Spawn.AddCopy(60, 8, 60);
        World.SetBlock("game:forge", pos);
        await World.Ticks(2);
        var forge = W.BlockAccessor.GetBlockEntity(pos);
        Assert.NotNull(forge);
        Assert.Equal("BlockEntityCrucibulumForge", forge!.GetType().Name);
        var shares = (double[])AccessTools.Method(forge.GetType(), "ChargeShares").Invoke(forge,
            [new[] { Stack("seraphhorizons:concentrate-cassiterite", 20), Stack("game:crushed-cassiterite-coarse", 20) }])!;
        // Shares of the charge's metal: 20 concentrate (an ingot) and 20 crushed (half of one).
        Assert.Equal(2.0 / 3, shares[0], 6);
        Assert.Equal(1.0 / 3, shares[1], 6);
        World.SetBlock("game:air", pos);
    }

    // smex's blast furnace: a nugget or concentrate is 5 units of iron, a crushed ore 2.5.
    [AtlasScenario]
    public void Smex_burden_counts_concentrate_at_five()
    {
        Assert.Equal(SmexBurden.Status.Applied, System.Smex);
        var compat = AccessTools.TypeByName(SmexBurden.CompatType);
        var burden = AccessTools.TypeByName("SteelmakingExpanded.BlockStructures.BlastFurnace.BurdenValue");
        bool Is(string method, string path) => (bool)AccessTools.Method(compat, method).Invoke(null, [path])!;
        int Get(string property) => (int)AccessTools.Property(burden, property).GetValue(null)!;
        float Iron(int units) => (float)AccessTools.Method(burden, "IronPerOreUnits").Invoke(null, [units])!;
        Assert.True(Is("IsIronNugget", "concentrate-hematite"));
        Assert.True(Is("IsIronNugget", "roastedconcentrate-pyrite"));
        Assert.False(Is("IsIronNugget", "concentrate-pyrite"));
        Assert.True(Is("IsCrushedIronOre", "crushed-magnetite-fine"));
        Assert.Equal(5f, Iron(Get("OrePerNugget")), 4);
        Assert.Equal(2.5f, Iron(Get("OrePerCrushed")), 4);
    }

    // The crucible furnace's ferrochrome takes chromite concentrate as it takes crushed chromite.
    [AtlasScenario]
    public void Crucible_furnace_takes_chromite_concentrate()
    {
        Assert.NotNull(Item("seraphhorizons:concentrate-chromite"));
        var ferrochrome = PotRecipes.FerrochromeRecipe;
        Assert.Equal("crushedchromite", ferrochrome.IngredientFor("seraphhorizons:concentrate-chromite")?.Name);
        var match = PotRecipes.Default.Match([new ChargeItem("seraphhorizons:concentrate-chromite", 20),
            new ChargeItem(Stainless.Ferrosilicon, 12), new ChargeItem("game:lime", 8)]);
        Assert.Equal(ferrochrome, match.Recipe);
    }
}
