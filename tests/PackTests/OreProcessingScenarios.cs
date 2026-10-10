using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.RecipeExport;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;
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
        // A hammer no longer breaks it into nuggets where it lies (shift + right-click): it spalls it
        // with a left-click, which the help says, with every hammer.
        Assert.False(((IContainedInteractable)medium).OnContainedInteractStart(null!, null!, null!, null!));
        var help = Assert.Single(((IContainedInteractable)medium).GetContainedInteractionHelp(null!, null!, null!, null!));
        Assert.Equal(EnumMouseButton.Left, help.MouseButton);
        Assert.Contains(help.Itemstacks, s => s.Collectible.Code.ToString() == "game:hammer-copper");
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
                                                  && (r.ResolvedIngredients ?? []).Any(i => i?.Code?.Path?.Contains("ore-") == true));
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
        // The resolved ingredients: the server frees a grid recipe's Ingredients once a player has joined.
        Assert.Contains(bricks, r => (r.ResolvedIngredients ?? []).Any(i => i != null && i.SatisfiesAsIngredient(fine, false)));
        Assert.Contains(bricks, r => (r.ResolvedIngredients ?? []).Any(i => i != null && i.SatisfiesAsIngredient(coarse, false)));
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
        // A sulfide's concentrate smelts only into its roasted concentrate, with no container, which
        // the crucible refuses outright (BlockSmeltingContainer.CanSmelt): it does not smelt to metal.
        var sulfide = Item("seraphhorizons:concentrate-galena").CombustibleProps!;
        Assert.False(sulfide.RequiresContainer);
        Assert.Equal("seraphhorizons:roastedconcentrate-galena", sulfide.SmeltedStack.ResolvedItemstack.Collectible.Code.ToString());
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
        // Pyrite's concentrate roasts first (#720), far below a bloomery's heat.
        Assert.InRange(Item("seraphhorizons:concentrate-pyrite").CombustibleProps!.MeltingPoint, 1, 999);
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

    // #690: argentiferous galena is named so and is a lead ore: every form smelts to lead, its loose
    // bits drop galena, and its metal group is lead.
    [AtlasScenario]
    public void Argentiferous_galena_is_a_lead_ore()
    {
        Assert.Equal("Argentiferous galena concentrate", new ItemStack(Item(OreProducts.ConcentrateCode("galena_nativesilver"))).GetName());
        Assert.Equal("Raw Argentiferous galena ore (medium)", new ItemStack(Item("game:ore-medium-galena_nativesilver-shale")).GetName());
        Assert.Equal("Argentiferous galena ore", new ItemStack(W.GetBlock(new AssetLocation("game:ore-poor-galena_nativesilver-shale"))).GetName());
        Assert.Equal("Argentiferous galena bits", new ItemStack(W.GetBlock(new AssetLocation("game:looseores-galena_nativesilver-shale-free"))).GetName());
        foreach (var code in new[] { OreProducts.RoastedCode("galena_nativesilver"), OreProducts.CrushedCode("galena_nativesilver", OreGrain.Coarse),
                     "game:crystalizedore-rich-galena_nativesilver-granite" })
            Assert.Equal("game:ingot-lead", Item(code).CombustibleProps?.SmeltedStack?.ResolvedItemstack?.Collectible.Code.ToString());
        Assert.Equal("game:nugget-galena",
            W.GetBlock(new AssetLocation("game:looseores-galena_nativesilver-shale-free"))!.Drops.Single().ResolvedItemstack.Collectible.Code.ToString());
        Assert.Equal("game:nugget-nativesilver",
            W.GetBlock(new AssetLocation("game:looseores-quartz_nativesilver-granite-free"))!.Drops.Single().ResolvedItemstack.Collectible.Code.ToString());
        Assert.Equal("lead", OreMetals.MetalOf("galena_nativesilver"));
    }

    private sealed class LooseSlots(ItemSlot[] slots) : ISlotProvider
    {
        public ItemSlot[] Slots => slots;
    }

    private (BlockEntity Forge, ISlotProvider Charge) Forge(BlockPos pos)
    {
        World.SetBlock("game:forge", pos);
        var forge = W.BlockAccessor.GetBlockEntity(pos)!;
        Assert.Equal(CupelForge.ForgeType, forge.GetType().FullName);
        return (forge, (ISlotProvider)AccessTools.Field(forge.GetType(), "chargeProvider").GetValue(forge)!);
    }

    private void Gate(BlockEntity forge, string position)
    {
        AccessTools.Property(forge.GetType(), "GateStack").SetValue(forge, Stack("game:metalplate-iron", 1));
        var gate = AccessTools.Property(forge.GetType(), "GatePosition");
        gate.SetValue(forge, Enum.Parse(gate.PropertyType, position));
    }

    // #722: the cupel goes in crucibulum's forge as a crucible; its gate sets the pace; done, it is
    // the bead, which breaks into litharge (the recipe's output) and silver bits.
    [AtlasScenario]
    public async Task Cupel_in_crucibulums_forge()
    {
        Assert.True(CupelForge.Bound);
        var pos = World.Spawn.AddCopy(64, 8, 60);
        var (forge, charge) = Forge(pos);
        await World.Ticks(2);
        var cupelBlock = W.GetBlock(new AssetLocation("seraphhorizons:cupel-fired"));
        var cupel = Assert.IsType<BlockCupel>(cupelBlock);
        var work = ((BlockEntityForge)forge).WorkItemSlot;
        work.Itemstack = new ItemStack(cupelBlock);
        Assert.True((bool)AccessTools.Method(forge.GetType(), "IsCrucible").Invoke(null, [work.Itemstack])!);
        // The charge slots take roasted concentrate as they take a crucible's charge.
        Assert.True((bool)AccessTools.Method(AccessTools.TypeByName("Crucibulum.ItemSlotCrucibleCharge"), "Admits")
            .Invoke(null, [Stack(OreProducts.RoastedCode("galena_nativesilver"), 1), work.Itemstack])!);

        charge.Slots[0].Itemstack = Stack(OreProducts.RoastedCode("galena_nativesilver"), 40);
        Assert.True(cupel.CanSmelt(W, charge, work.Itemstack, null));
        Assert.Equal(950, cupel.GetMeltingPoint(W, charge, work), 3);
        Assert.Equal(120, cupel.GetMeltingDuration(W, charge, work), 3);
        Assert.StartsWith("Will part 64.6 units of Silver", cupel.OutputText(W, charge));
        Gate(forge, "Quarter");
        Assert.Equal(120 / 0.7, cupel.GetMeltingDuration(W, charge, work), 2);
        Gate(forge, "Shut");
        Assert.False(cupel.CanSmelt(W, charge, work.Itemstack, null));
        Gate(forge, "Open");

        // Not a forge's slots (a firepit's): refused.
        var elsewhere = new LooseSlots([new DummySlot(Stack(OreProducts.RoastedCode("galena_nativesilver"), 40))]);
        Assert.False(cupel.CanSmelt(W, elsewhere, work.Itemstack, null));
        Assert.Null(CupelForge.Air(elsewhere));
        // Too much, or something else in the charge: refused.
        charge.Slots[1].Itemstack = Stack(OreProducts.RoastedCode("galena"), 1);
        Assert.False(cupel.CanSmelt(W, charge, work.Itemstack, null));
        charge.Slots[1].Itemstack = Stack("seraphhorizons:roastedconcentrate-chalcopyrite", 1);
        Assert.False(cupel.CanSmelt(W, charge, work.Itemstack, null));
        charge.Slots[1].Itemstack = null;

        AccessTools.Method(forge.GetType(), "DoSmelt").Invoke(forge, []);
        var bead = work.Itemstack;
        Assert.IsType<BlockCupelBead>(bead?.Collectible);
        Assert.All(charge.Slots, s => Assert.True(s.Empty));
        Assert.Equal(40, BlockCupelBead.Litharge(bead!));
        var bits = BlockCupelBead.Bits(bead!).ToList();
        Assert.Equal("game:metalbit-silver", Assert.Single(bits).Code);
        Assert.InRange(bits[0].Count, 12, 13);

        // Broken in the grid: litharge out, as many as the bead holds.
        var recipe = W.GridRecipes.Single(r => r.Enabled && r.Output.Code.ToString() == OreProducts.LithargeCode);
        var output = new DummySlot(Stack(OreProducts.LithargeCode, 1));
        Item(OreProducts.LithargeCode).OnCreatedByCrafting([new DummySlot(bead), new DummySlot(Stack("game:hammer-iron", 1))], output, recipe);
        Assert.Equal(40, output.Itemstack.StackSize);
        World.SetBlock("game:air", pos);
    }

    // Tetrahedrite and freibergite want their own weight of lead.
    [AtlasScenario]
    public async Task Cupel_takes_copper_ores_with_lead()
    {
        var pos = World.Spawn.AddCopy(68, 8, 60);
        var (forge, charge) = Forge(pos);
        await World.Ticks(2);
        var cupel = (BlockCupel)W.GetBlock(new AssetLocation("seraphhorizons:cupel-fired"));
        var work = ((BlockEntityForge)forge).WorkItemSlot;
        work.Itemstack = new ItemStack(cupel);
        charge.Slots[0].Itemstack = Stack(OreProducts.RoastedCode("freibergite"), 20);
        Assert.False(cupel.CanSmelt(W, charge, work.Itemstack, null));
        charge.Slots[1].Itemstack = Stack("game:metalbit-lead", 20);
        Assert.True(cupel.CanSmelt(W, charge, work.Itemstack, null));
        AccessTools.Method(forge.GetType(), "DoSmelt").Invoke(forge, []);
        var bits = BlockCupelBead.Bits(work.Itemstack!).ToDictionary(b => b.Code, b => b.Count);
        Assert.Equal(20, bits["game:metalbit-silver"]);
        Assert.InRange(bits["game:metalbit-copper"], 5, 6);
        Assert.Equal(20, BlockCupelBead.Litharge(work.Itemstack!));
        World.SetBlock("game:air", pos);
    }

    // The raw cupel is four bone meal, fired like clay into the cupel.
    [AtlasScenario]
    public void Cupel_is_made_of_bone_meal_and_fired()
    {
        var raw = W.GetBlock(new AssetLocation("seraphhorizons:cupel-raw"));
        Assert.Contains(W.GridRecipes, r => r.Enabled && r.Output?.Code?.ToString() == "seraphhorizons:cupel-raw");
        Assert.Equal(EnumSmeltType.Fire, raw.CombustibleProps.SmeltingType);
        Assert.Equal("seraphhorizons:cupel-fired", raw.CombustibleProps.SmeltedStack.ResolvedItemstack.Collectible.Code.ToString());
        Assert.Equal("Bone-ash cupel", new ItemStack(W.GetBlock(new AssetLocation("seraphhorizons:cupel-fired"))).GetName());
    }

    // The recipe browser's record per ore the cupel takes.
    [AtlasScenario(TimeoutMs = 600_000)]
    public void Cupellation_is_exported()
    {
        var doc = ExportUnderTest.Get(World.Api);
        Assert.Equal("generic", (string)doc["recipeTypes"]!["cupellation"]!["shape"]!);
        var records = doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == "cupellation").ToList();
        Assert.Equal(["cupellation|seraphhorizons:roastedconcentrate-freibergite|0", "cupellation|seraphhorizons:roastedconcentrate-galena_nativesilver|0",
                      "cupellation|seraphhorizons:roastedconcentrate-galena|0", "cupellation|seraphhorizons:roastedconcentrate-tetrahedrite|0"],
            records.Select(r => (string)r["id"]!).Order(StringComparer.Ordinal));
        var argentiferous = records.Single(r => (string)r["extra"]!["ore"]! == "galena_nativesilver");
        Assert.Equal("OreProcessing", (string?)argentiferous["switch"]);
        Assert.Equal(64.6, (double)argentiferous["extra"]!["metalUnits"]!["silver"]!, 6);
        Assert.Equal(40, (double)argentiferous["ingredients"]![1]!["quantity"]!);
        var tetrahedrite = records.Single(r => (string)r["extra"]!["ore"]! == "tetrahedrite");
        Assert.Contains(tetrahedrite["ingredients"]!, i => (string)i["code"]! == "game:metalbit-lead" && (double)i["quantity"]! == 20);
    }

    private const string Pan = "seraphhorizons:liquationpan-fired";

    // #724: a firepit at its fuel's full heat with the pan, run by the firepit's own burn tick: what
    // the fire's heat does to a full pan of teallite. Returns the firepit's output and the hottest the charge got.
    private async Task<(ItemStack? Output, float Hottest)> LiquateInFirepit(BlockPos pos, string fuel)
    {
        World.SetBlock("game:firepit-cold", pos);
        await World.Ticks(2);
        var firepit = Assert.IsType<BlockEntityFirepit>(W.BlockAccessor.GetBlockEntity(pos));
        var inv = (InventorySmelting)firepit.Inventory;
        var tick = AccessTools.Method(typeof(BlockEntityFirepit), "OnBurnTick");
        // The fire burning a while first, at its full heat, as a player would have it.
        inv[0].Itemstack = Stack(fuel, 16);
        firepit.igniteFuel();
        for (int i = 0; i < 3000 && firepit.furnaceTemperature < firepit.maxTemperature - 2; i++)
        {
            if (!firepit.IsBurning) firepit.igniteFuel();
            tick.Invoke(firepit, [0.1f]);
        }
        inv[1].Itemstack = new ItemStack(W.GetBlock(new AssetLocation(Pan)));
        Assert.True(inv.HaveCookingContainer);
        inv.CookingSlots[0].Itemstack = Stack(OreProducts.RoastedCode("teallite"), 20);
        float hottest = 0;
        for (int i = 0; i < 1200 && inv[2].Empty; i++)
        {
            if (!firepit.IsBurning) firepit.igniteFuel();
            tick.Invoke(firepit, [0.1f]);
            if (OreContainers.ChargeTemperature(W, inv) is { } t) hottest = Math.Max(hottest, t);
        }
        var result = inv[2].Itemstack;
        World.SetBlock("game:air", pos);
        return (result, hottest);
    }

    // A wood fire is gentle enough: the tin runs off under the lead point and the lead stays in the pan.
    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Liquation_pan_in_a_wood_fire()
    {
        var pan = Assert.IsType<BlockLiquationPan>(W.GetBlock(new AssetLocation(Pan)));
        var slots = new LooseSlots([new DummySlot(Stack(OreProducts.RoastedCode("teallite"), 20)), new DummySlot(), new DummySlot(), new DummySlot()]);
        var input = new DummySlot(new ItemStack(pan));
        Assert.True(pan.CanSmelt(W, slots, input.Itemstack, null));
        Assert.Equal(240, pan.GetMeltingPoint(W, slots, input), 3);
        Assert.Equal(15, pan.GetMeltingDuration(W, slots, input), 3);
        Assert.StartsWith("Will pour 100 units of Tin, leaving 34 units of Lead in the pan", pan.OutputText(W, slots));
        // Nothing but teallite and franckeite, and no more than 100 units.
        slots.Slots[1].Itemstack = Stack(OreProducts.RoastedCode("galena"), 1);
        Assert.False(pan.CanSmelt(W, slots, input.Itemstack, null));
        slots.Slots[1].Itemstack = Stack(OreProducts.RoastedCode("franckeite"), 1);
        Assert.False(pan.CanSmelt(W, slots, input.Itemstack, null));
        slots.Slots[1].Itemstack = null;
        // A full output slot stops it.
        Assert.False(pan.CanSmelt(W, slots, input.Itemstack, Stack("game:ingot-tin", 1)));

        var (done, hottest) = await LiquateInFirepit(World.Spawn.AddCopy(72, 8, 60), "game:firewood");
        output.WriteLine($"wood fire: the charge at {hottest} °C when done");
        Assert.IsType<BlockLiquationPanSmelted>(done?.Collectible);
        Assert.InRange(hottest, 240, 327);
        var smelted = (BlockLiquationPanSmelted)done!.Collectible;
        var contents = smelted.GetContents(W, done);
        Assert.Equal("game:ingot-tin", contents.Key.Collectible.Code.ToString());
        Assert.Equal(100, contents.Value);
        var lead = Assert.Single(BlockLiquationPanSmelted.Residue(done));
        Assert.Equal("game:metalbit-lead", lead.Code);
        Assert.InRange(lead.Count, 6, 7);
        Assert.Equal("Liquation pan (molten Tin)", done.GetName());

        // Poured out, it is the pan with lead residue, which the hammer knocks out in the grid.
        var residue = BlockLiquationPanSmelted.EmptiedStack(W, done, new ItemStack(pan));
        Assert.IsType<BlockLiquationResidue>(residue.Collectible);
        Assert.Equal(lead, Assert.Single(BlockLiquationPanSmelted.Residue(residue)));
        // (the only recipe whose output is the fired pan)
        Assert.Single(W.GridRecipes, r => r.Enabled && r.Output?.Code?.ToString() == Pan);
    }

    // Charcoal in a firepit is too hot for a full pan: the lead runs with the tin and is lost.
    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Liquation_pan_overheated_in_a_charcoal_fire()
    {
        var (done, hottest) = await LiquateInFirepit(World.Spawn.AddCopy(76, 8, 60), "game:charcoal");
        output.WriteLine($"charcoal fire: the charge at {hottest} °C when done");
        Assert.True(hottest > 327, $"hottest {hottest}");
        Assert.IsType<BlockLiquationPanSmelted>(done?.Collectible);
        Assert.Equal(100, ((BlockLiquationPanSmelted)done!.Collectible).GetContents(W, done).Value);
        Assert.Empty(BlockLiquationPanSmelted.Residue(done));
        // Poured out, it is the plain pan.
        var pan = new ItemStack(W.GetBlock(new AssetLocation(Pan)));
        Assert.Same(pan, BlockLiquationPanSmelted.EmptiedStack(W, done, pan));
    }

    // Crucibulum's forge takes the pan as a crucible, with no air needed: the gate shut, it still works.
    [AtlasScenario]
    public async Task Liquation_pan_in_crucibulums_forge()
    {
        var pos = World.Spawn.AddCopy(80, 8, 60);
        var (forge, charge) = Forge(pos);
        await World.Ticks(2);
        var pan = (BlockLiquationPan)W.GetBlock(new AssetLocation(Pan));
        var work = ((BlockEntityForge)forge).WorkItemSlot;
        work.Itemstack = new ItemStack(pan);
        Assert.True((bool)AccessTools.Method(forge.GetType(), "IsCrucible").Invoke(null, [work.Itemstack])!);
        Assert.True((bool)AccessTools.Method(AccessTools.TypeByName("Crucibulum.ItemSlotCrucibleCharge"), "Admits")
            .Invoke(null, [Stack(OreProducts.RoastedCode("franckeite"), 1), work.Itemstack])!);
        charge.Slots[0].Itemstack = Stack(OreProducts.RoastedCode("franckeite"), 20);
        Gate(forge, "Shut");
        Assert.True(pan.CanSmelt(W, charge, work.Itemstack, null));
        foreach (var s in charge.Slots.Where(s => !s.Empty))
            s.Itemstack.Collectible.SetTemperature(W, s.Itemstack, 300);
        Assert.Contains("25.5 units of Lead", pan.OutputText(W, charge));
        AccessTools.Method(forge.GetType(), "DoSmelt").Invoke(forge, []);
        var smelted = work.Itemstack;
        Assert.IsType<BlockLiquationPanSmelted>(smelted?.Collectible);
        Assert.All(charge.Slots, s => Assert.True(s.Empty));
        Assert.InRange(Assert.Single(BlockLiquationPanSmelted.Residue(smelted!)).Count, 5, 6);

        // Over the lead point when done: the dialog warns, and the charge gives no lead.
        work.Itemstack = new ItemStack(pan);
        charge.Slots[0].Itemstack = Stack(OreProducts.RoastedCode("franckeite"), 20);
        charge.Slots[0].Itemstack.Collectible.SetTemperature(W, charge.Slots[0].Itemstack, 340);
        Assert.StartsWith("Too hot", pan.OutputText(W, charge));
        AccessTools.Method(forge.GetType(), "DoSmelt").Invoke(forge, []);
        Assert.Empty(BlockLiquationPanSmelted.Residue(work.Itemstack!));
        World.SetBlock("game:air", pos);
    }

    // The raw pan is clay-formed and fired like clay into the pan.
    [AtlasScenario]
    public void Liquation_pan_is_formed_and_fired()
    {
        var raw = W.GetBlock(new AssetLocation("seraphhorizons:liquationpan-raw"));
        Assert.Contains(World.Api.GetClayformingRecipes(), r => r.Output?.Code?.ToString() == "seraphhorizons:liquationpan-raw");
        Assert.Equal(EnumSmeltType.Fire, raw.CombustibleProps.SmeltingType);
        Assert.Equal(Pan, raw.CombustibleProps.SmeltedStack.ResolvedItemstack.Collectible.Code.ToString());
        Assert.Equal("Liquation pan", new ItemStack(W.GetBlock(new AssetLocation(Pan))).GetName());
    }

    // The recipe browser's record per ore the pan takes, and the guide page.
    [AtlasScenario(TimeoutMs = 600_000)]
    public void Liquation_is_exported()
    {
        var doc = ExportUnderTest.Get(World.Api);
        Assert.Equal("generic", (string)doc["recipeTypes"]!["liquation"]!["shape"]!);
        var records = doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == "liquation").ToList();
        Assert.Equal(["liquation|seraphhorizons:roastedconcentrate-franckeite|0", "liquation|seraphhorizons:roastedconcentrate-teallite|0"],
            records.Select(r => (string)r["id"]!).Order(StringComparer.Ordinal));
        var teallite = records.Single(r => (string)r["extra"]!["ore"]! == "teallite");
        Assert.Equal("OreProcessing", (string?)teallite["switch"]);
        Assert.Equal(34, (double)teallite["extra"]!["residueUnits"]!["lead"]!, 6);
        Assert.Contains(teallite["outputs"]!, o => (string)o["code"]! == "game:ingot-tin" && (double)o["quantity"]! == 1);
        Assert.Contains(teallite["outputs"]!, o => (string)o["code"]! == "game:metalbit-lead" && Math.Abs((double)o["quantity"]! - 6.8) < 1e-6);
        Assert.Contains(doc["guides"]!.Cast<JObject>(), g => (string?)g["code"] == OreProcessingSystem.LiquationGuidePage);
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

    // ---- Spalling (#747) ----
    // Every blow is a real left-click with a hammer through the hammer's own attack call, as the
    // server makes it from a client's packet, a few ticks apart (a player strikes no faster than
    // Spalling.BlowIntervalMs). Each scenario works on a granite floor of its own 30 above spawn.

    private static ITestPlayer? _spaller;
    private static object? _spallerWorld;

    private async Task<IPlayer> Spaller()
    {
        if (_spaller == null || !ReferenceEquals(_spallerWorld, World.Api))
        {
            _spaller = await World.JoinPlayer("spaller");
            _spallerWorld = World.Api;
        }
        var player = _spaller.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        player.Entity.Controls.ShiftKey = player.Entity.Controls.CtrlKey = false;
        return player;
    }

    private async Task<BlockPos> SpallSite(int dx, int dz)
    {
        var origin = World.Spawn.AddCopy(dx, 30, dz);
        if (World.Api is Vintagestory.API.Server.ICoreServerAPI sapi)
            sapi.WorldManager.LoadChunkColumnPriority(origin.X / GlobalConstants.ChunkSize, origin.Z / GlobalConstants.ChunkSize);
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(origin) != null, 30000);
        int floor = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = -3; x <= 3; x++)
        for (int z = -3; z <= 3; z++)
        {
            W.BlockAccessor.SetBlock(floor, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= 3; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        KillItems(origin);
        return origin;
    }

    private Dictionary<string, int> ItemsNear(BlockPos around) =>
        World.EntitiesIn(new Cuboidi(around.X - 4, around.Y - 2, around.Z - 4, around.X + 4, around.Y + 4, around.Z + 4))
            .OfType<EntityItem>().Where(e => e.Alive)
            .GroupBy(e => e.Itemstack.Collectible.Code.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Itemstack.StackSize));

    private void KillItems(BlockPos around)
    {
        foreach (var e in World.EntitiesIn(new Cuboidi(around.X - 4, around.Y - 2, around.Z - 4, around.X + 4, around.Y + 4, around.Z + 4)).OfType<EntityItem>())
            e.Die(EnumDespawnReason.Removed);
    }

    /// <summary>Shift + right-click on <paramref name="at"/> (its top, or <paramref name="face"/>)
    /// holding <paramref name="held"/>, as the held item's interaction; returns what is left in hand.</summary>
    private ItemStack? SetDown(IPlayer player, BlockPos at, ItemStack held, BlockFacing? face = null)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        player.Entity.Controls.ShiftKey = true;
        try
        {
            var sel = new BlockSelection { Position = at.Copy(), Face = face ?? BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
            EnumHandHandling handling = EnumHandHandling.NotHandled;
            if (W.BlockAccessor.GetBlockEntity(at) is BlockEntityGroundStorage)
                W.BlockAccessor.GetBlock(at).OnBlockInteractStart(W, player, sel);
            else
                held.Collectible.OnHeldInteractStart(slot, player.Entity, sel, null, true, ref handling);
        }
        finally
        {
            player.Entity.Controls.ShiftKey = false;
        }
        return slot.Itemstack;
    }

    /// <summary>One left-click on <paramref name="at"/> with <paramref name="held"/>, as the server
    /// runs it; returns the hand handling.</summary>
    private EnumHandHandling LeftClick(IPlayer player, BlockPos at, ItemStack held)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        var sel = new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.1, 0.5) };
        EnumHandHandling handling = EnumHandHandling.NotHandled;
        held.Collectible.OnHeldAttackStart(slot, player.Entity, sel, null, ref handling);
        return handling;
    }

    /// <summary>One blow: left-clicks a tick apart until the server counts it (or the ore breaks).</summary>
    private async Task Blow(IPlayer player, BlockPos at, ItemStack hammer)
    {
        var spalling = System.Spalling!;
        int before = spalling.BlowsAt(at);
        for (int i = 0; i < 60; i++)
        {
            await World.Ticks(1);
            Assert.Equal(EnumHandHandling.PreventDefault, LeftClick(player, at, hammer));
            if (spalling.BlowsAt(at) != before || OreSpalling.Target(W, at) == null)
                return;
        }
        throw new Xunit.Sdk.XunitException("no blow was struck");
    }

    [AtlasScenario]
    public void Hammers_spall_and_ore_sets_down_one_to_a_block()
    {
        Assert.NotNull(System.Spalling);
        Assert.Equal(9, System.SpallingHammers);
        foreach (var metal in new[] { "copper", "tinbronze", "iron", "steel" })
        {
            var hammer = Item($"game:hammer-{metal}");
            Assert.IsType<CollectibleBehaviorSpalling>(hammer.CollectibleBehaviors[0]);
            Assert.Contains(hammer.CollectibleBehaviors, b => b is CollectibleBehaviorAnimationAuthoritative);
        }
        Assert.False(Item("game:pickaxe-iron").HasBehavior<CollectibleBehaviorSpalling>());
        foreach (var code in new[] { "game:ore-medium-galena-shale", "game:ore-rich-limonite-shale", "game:ore-bountiful-hematite-granite", "game:crystalizedore-poor-galena-shale" })
        {
            var props = Item(code).GetBehavior<CollectibleBehaviorGroundStorable>()!.StorageProps;
            Assert.Equal(EnumGroundStorageLayout.SingleCenter, props.Layout);
            Assert.Equal(1, props.TransferQuantity);
        }
        var config = SeraphHorizonsSystem.ConfigFor(World.Api).SpallingSettings;
        Assert.Equal((6, 3, 1), (config.BlowsRawOre, config.BlowsChunk, config.HammerWearPerBlow));
    }

    // A medium ore: one from the hand goes down, six blows break it into four coarse crushed ore where
    // it lay, the hammer worn a point a blow.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Spalling_breaks_raw_ore_into_its_crushed_ore()
    {
        var pos = await SpallSite(70, 70);
        var player = await Spaller();
        var floor = pos.DownCopy();
        Assert.Equal(3, SetDown(player, floor, Stack("game:ore-medium-galena-shale", 4))?.StackSize);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BlockEntityGroundStorage;
        Assert.NotNull(be);
        Assert.Equal(1, be!.Inventory.Sum(s => s.StackSize));
        Assert.NotNull(OreSpalling.Target(W, pos));

        // a pick does not spall: the click is left to the game (it breaks the block and picks it up)
        Assert.NotEqual(EnumHandHandling.PreventDefaultAction, LeftClick(player, pos, Stack("game:pickaxe-iron", 1)));
        Assert.Equal(0, System.Spalling!.BlowsAt(pos));

        var hammer = Stack("game:hammer-copper", 1);
        int durability = hammer.Collectible.GetRemainingDurability(hammer);
        for (int b = 1; b <= 5; b++)
        {
            await Blow(player, pos, hammer);
            Assert.Equal(b, System.Spalling.BlowsAt(pos));
        }
        Assert.NotNull(OreSpalling.Target(W, pos));
        Assert.Equal(durability - 5, hammer.Collectible.GetRemainingDurability(hammer));
        Assert.Empty(ItemsNear(pos));

        await Blow(player, pos, hammer);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        await World.Ticks(5);
        var near = ItemsNear(pos);
        Assert.Equal(4, near.GetValueOrDefault("game:crushed-galena-coarse"));
        Assert.Single(near);
        Assert.Equal(durability - 6, hammer.Collectible.GetRemainingDurability(hammer));
        KillItems(pos);
    }

    // Poor ore is fine-grained and gives fine crushed ore, by its units (15: three); a chunk takes
    // three blows (bountiful galena, 35 units: seven coarse).
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Spalling_gives_fine_from_poor_and_breaks_a_chunk_in_fewer_blows()
    {
        var pos = await SpallSite(-70, 70);
        var player = await Spaller();
        var hammer = Stack("game:hammer-iron", 1);

        SetDown(player, pos.DownCopy(), Stack("game:ore-poor-galena-shale", 1));
        for (int b = 0; b < 6; b++)
            await Blow(player, pos, hammer);
        await World.Ticks(5);
        Assert.Equal(3, ItemsNear(pos).GetValueOrDefault("game:crushed-galena-fine"));
        KillItems(pos);

        SetDown(player, pos.DownCopy(), Stack("game:ore-bountiful-galena-shale", 1));
        for (int b = 0; b < 2; b++)
            await Blow(player, pos, hammer);
        Assert.NotNull(OreSpalling.Target(W, pos));
        await Blow(player, pos, hammer);
        Assert.Null(OreSpalling.Target(W, pos));
        await World.Ticks(5);
        Assert.Equal(7, ItemsNear(pos).GetValueOrDefault("game:crushed-galena-coarse"));
        KillItems(pos);
    }

    // Shift + right-click on a placed ore with another in hand sets it on the next block (beside the
    // face clicked), never on top and never picking the placed one up; with an empty hand it is taken back.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Another_ore_goes_on_the_next_block()
    {
        var pos = await SpallSite(70, -70);
        var player = await Spaller();
        Assert.Equal(3, SetDown(player, pos.DownCopy(), Stack("game:ore-medium-hematite-granite", 4))?.StackSize);
        Assert.Equal(2, SetDown(player, pos, Stack("game:ore-medium-hematite-granite", 3), BlockFacing.EAST)?.StackSize);
        Assert.Equal(1, OreSpalling.Target(W, pos)!.StackSize);
        Assert.Equal(1, OreSpalling.Target(W, pos.EastCopy())!.StackSize);
        // a chunk too, beside the north face
        Assert.Null(SetDown(player, pos, Stack("game:ore-rich-hematite-granite", 1), BlockFacing.NORTH));
        Assert.Equal("game:ore-rich-hematite-granite", OreSpalling.Target(W, pos.NorthCopy())!.Itemstack.Collectible.Code.ToString());
        Assert.Equal(1, OreSpalling.Target(W, pos)!.StackSize);
        // an empty hand takes the placed ore back
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = null;
        player.Entity.Controls.ShiftKey = true;
        W.BlockAccessor.GetBlock(pos).OnBlockInteractStart(W, player,
            new BlockSelection { Position = pos.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.1, 0.5) });
        player.Entity.Controls.ShiftKey = false;
        Assert.Null(OreSpalling.Target(W, pos));
        var held = player.InventoryManager.Inventories.Values
            .Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName)
            .SelectMany(inv => inv).Where(s => s.Itemstack?.Collectible.Code.ToString() == "game:ore-medium-hematite-granite").ToList();
        Assert.Equal(1, held.Sum(s => s.StackSize));
        foreach (var s in held)
            s.Itemstack = null;
        foreach (var p in new[] { pos.EastCopy(), pos.NorthCopy() })
            W.BlockAccessor.SetBlock(0, p);
    }

    // The recipe export has a spalling record per ore kind: by hand, its turns the blows, the hammer
    // worn a point a blow, no station; the 5-unit rule's crushed ore.
    [AtlasScenario(TimeoutMs = 600_000)]
    public void Spalling_is_exported_as_a_hand_job_per_ore_kind()
    {
        var doc = ExportUnderTest.Get(World.Api);
        var type = doc["recipeTypes"]![RecipeSection.SpallingType]!;
        Assert.Equal("machine", (string?)type["shape"]);
        var records = doc["recipes"]!.Cast<JObject>().Where(r => (string?)r["type"] == RecipeSection.SpallingType).ToList();
        Assert.Equal((int)type["count"]!, records.Count);
        Assert.Equal(2 * 4 * OreProducts.Ores.Count, records.Count);
        var medium = records.Single(r => (string?)r["id"] == "spalling|game:ore-medium-galena-*|0");
        Assert.Equal("game:ore-medium-galena-*", (string?)medium["ingredients"]![0]!["code"]);
        Assert.Equal(6, (int)medium["ingredients"]![1]!["toolDurabilityCost"]!);
        Assert.Equal("game:crushed-galena-coarse", (string?)medium["outputs"]![0]!["code"]);
        Assert.Equal(4, (int)medium["outputs"]![0]!["quantity"]!);
        Assert.Equal("hand", (string?)medium["machine"]!["power"]);
        Assert.Equal(6, (int)medium["machine"]!["turns"]!);
        Assert.Equal("strikes", (string?)medium["machine"]!["work"]!["unit"]);
        Assert.DoesNotContain(medium["ingredients"]!, i => (string?)i["role"] == "station");
        Assert.Equal(RecipeSection.SpallingRequirement, (string?)medium["requirements"]![0]);
        Assert.Contains(medium["variants"]![0]!["ingredients"]![0]!, s => (string?)s["code"] == "game:ore-medium-galena-shale");
        var poor = records.Single(r => (string?)r["id"] == "spalling|game:ore-poor-galena-*|0");
        Assert.Equal("game:crushed-galena-fine", (string?)poor["outputs"]![0]!["code"]);
        Assert.Equal(3, (int)poor["outputs"]![0]!["quantity"]!);
        var chunk = records.Single(r => (string?)r["id"] == "spalling|game:ore-bountiful-galena-*|0");
        Assert.Equal(3, (int)chunk["machine"]!["turns"]!);
        Assert.Equal(7, (int)chunk["outputs"]![0]!["quantity"]!);
        // The guide page is in the export with the switch on.
        Assert.Contains(((JArray)doc["guides"]!).OfType<JObject>(), g => (string?)g["code"] == OreProcessingSystem.SpallingGuidePage);
        var dump = Environment.GetEnvironmentVariable("SPALLING_EXPORT_DUMP");
        if (!string.IsNullOrEmpty(dump))
            File.WriteAllText(dump, doc.ToString());
    }

    // Roasting in the firepit (#720): any fuel, one item at a time, 85 % with the fraction carried,
    // so 20 concentrate give 17 roasted; no sulfur.
    [AtlasScenario]
    public async Task Firepit_roasts_sulfide_concentrate()
    {
        var concentrate = (ItemOreProduct)Item("seraphhorizons:concentrate-galena");
        var props = concentrate.CombustibleProps!;
        Assert.Equal(OreRoasting.MeltingPoint, props.MeltingPoint);
        Assert.Equal((float)OreRoasting.DefaultSeconds, props.MeltingDuration);
        Assert.Equal(1, props.SmeltedRatio);
        Assert.Equal(EnumSmeltType.Convert, props.SmeltingType);
        Assert.Equal(0.85, concentrate.RoastShare, 9);
        Assert.Equal(OreProducts.Ores.Count(o => System.Recovery!.Ore(o).IsSulfide), System.Applied!.Roasting);
        // Not a sulfide: smelts as before and does not roast.
        Assert.Equal(0, ((ItemOreProduct)Item("seraphhorizons:concentrate-malachite")).RoastShare);
        // Dry grass, the coolest firepit fuel, reaches the point.
        Assert.True(Item("game:drygrass").CombustibleProps!.BurnTemperature >= OreRoasting.MeltingPoint);

        var pos = World.Spawn.AddCopy(64, 8, 64);
        World.SetBlock("game:firepit-cold", pos);
        await World.Ticks(2);
        var firepit = Assert.IsType<BlockEntityFirepit>(W.BlockAccessor.GetBlockEntity(pos));
        var input = firepit.Inventory[1];
        var output = firepit.Inventory[2];
        input.Itemstack = Stack("seraphhorizons:concentrate-galena", 20);
        Assert.True(firepit.canSmeltInput());
        string key = firepit.Inventory.InventoryID + "|seraphhorizons:roastedconcentrate-galena";

        firepit.smeltItems();
        Assert.Equal(19, input.StackSize);
        Assert.True(output.Empty);
        Assert.Equal(4.25, System.RoastCarry.HeldFor(key), 6);
        for (int i = 0; i < 19; i++)
            firepit.smeltItems();
        Assert.True(input.Empty);
        Assert.Equal("seraphhorizons:roastedconcentrate-galena", output.Itemstack!.Collectible.Code.ToString());
        Assert.Equal(17, output.StackSize);
        Assert.Equal(0, System.RoastCarry.HeldFor(key), 6);
        Assert.DoesNotContain(firepit.Inventory, s => s.Itemstack?.Collectible.Code.Path.Contains("sulfur") == true);
        World.SetBlock("game:air", pos);
    }

    // Leaching (#742): borax and alum ore and raw saltpeter are heavy raw forms that no quern or
    // crusher takes, and the leaching section is on their handbook pages.
    [AtlasScenario]
    public void Leached_minerals_come_raw()
    {
        Assert.NotNull(System.Leached);
        Assert.Empty(System.Leached!.Missing);
        foreach (var m in OreLeaching.Minerals)
        {
            var raw = Item(m.RawCode);
            Assert.Equal(OreProducts.RawStack, raw.MaxStackSize);
            Assert.Null(raw.GrindingProps);
            Assert.Null(raw.CrushingProps);
            Assert.Contains(raw.Attributes["handbook"]["extraSections"].AsArray(),
                s => s["title"].AsString() == Leaching.SectionTitle);
            Assert.NotNull(Item(m.LiquorCode));
            Assert.NotNull(Item(m.CrystalCode));
        }
        Assert.Equal("Raw saltpeter", new ItemStack(Item(OreLeaching.RawSaltpeter)).GetName());
        Assert.Equal("Crude borax liquor", new ItemStack(Item(OreLeaching.LiquorCode("borax"))).GetName());
        // Today's crystals are as they were.
        Assert.Equal(64, Item("game:saltpeter").MaxStackSize);
    }

    // Every block that dropped saltpeter (vanilla's cave coating, Interesting Ore Gen's saltpeter ore,
    // Saltpeter Production's buds) drops raw saltpeter at the same rate.
    [AtlasScenario]
    public void Saltpeter_blocks_drop_raw_saltpeter()
    {
        var coating = W.GetBlock(new AssetLocation("game:saltpeter-d"))!;
        var drop = Assert.Single(coating.Drops);
        Assert.Equal(OreLeaching.RawSaltpeter, drop.ResolvedItemstack.Collectible.Code.ToString());
        Assert.Equal(0.5f, drop.Quantity.avg);
        var changed = System.Leached!.SaltpeterBlocks;
        Assert.Contains(changed, c => c.StartsWith("interestingoregen:saltpeterore", StringComparison.Ordinal));
        Assert.Contains(changed, c => c.StartsWith("saltpeterproduction:saltpeterbud", StringComparison.Ordinal));
        Assert.DoesNotContain(W.Blocks, b => b?.Drops?.Any(d => d?.ResolvedItemstack?.Collectible.Code.ToString() == OreLeaching.Saltpeter) == true);
    }

    // A barrel of raw borax and water, sealed a day, holds crude borax liquor: a litre a piece.
    [AtlasScenario]
    public async Task Barrel_leaches_raw_mineral_into_crude_liquor()
    {
        // (Hydrate or Diedrate copies every recipe taking water for each of its waters.)
        Assert.Equal(OreLeaching.Minerals.Select(m => m.LiquorCode).Order(),
            World.Api.GetBarrelRecipes().Where(r => r.Code.StartsWith("seraphhorizons-leach-", StringComparison.Ordinal))
                .Select(r => r.Output.Code.ToString()).Distinct().Order());
        var pos = World.Spawn.AddCopy(64, 8, 56);
        World.SetBlock("game:rock-granite", pos.DownCopy());
        World.SetBlock("game:barrel", pos);
        await World.Ticks(2);
        var barrel = Assert.IsType<BlockEntityBarrel>(W.BlockAccessor.GetBlockEntity(pos));
        barrel.Inventory[0].Itemstack = Stack("game:ore-alum", 4);
        barrel.Inventory[0].MarkDirty();
        barrel.Inventory[1].Itemstack = Stack("game:waterportion", 400);
        barrel.Inventory[1].MarkDirty();
        AccessTools.Method(typeof(BlockEntityBarrel), "FindMatchingRecipe", []).Invoke(barrel, []);
        Assert.Equal("seraphhorizons-leach-alum", barrel.CurrentRecipe?.Code);
        Assert.Equal(OreLeaching.SealHours, barrel.CurrentRecipe!.SealHours);
        barrel.SealBarrel();
        barrel.SealedSinceTotalHours -= OreLeaching.SealHours + 0.1;
        AccessTools.Method(typeof(BlockEntityBarrel), "OnEvery3Second").Invoke(barrel, [3f]);
        Assert.False(barrel.Sealed);
        var liquor = barrel.Inventory.Select(s => s.Itemstack).Single(s => s != null);
        Assert.Equal(OreLeaching.LiquorCode("alum"), liquor.Collectible.Code.ToString());
        Assert.Equal(400, liquor.StackSize);
        World.SetBlock("game:air", pos);
    }

    // A cooking pot boils crude liquor down into today's crystals, a crystal a litre.
    [AtlasScenario]
    public void Pot_boils_crude_liquor_into_crystals()
    {
        Assert.Equal(3, World.Api.GetCookingRecipes().Count(r => r.Code.StartsWith("seraphhorizons-evaporate-", StringComparison.Ordinal)));
        var pot = (BlockCookingContainer)W.GetBlock(new AssetLocation("game:claypot-blue-fired"))!;
        foreach (var m in OreLeaching.Minerals)
        {
            var cooking = new CookingSlots(Stack(m.LiquorCode, 600));
            Assert.Equal($"seraphhorizons-evaporate-{m.Mineral}",
                pot.GetMatchingCookingRecipe(W, pot.GetCookingStacks(cooking), out int servings)!.Code);
            Assert.Equal(6, servings);
            pot.DoSmelt(W, cooking, new DummySlot(new ItemStack(pot)), new DummySlot());
            Assert.Equal(m.CrystalCode, cooking.Slots[0].Itemstack.Collectible.Code.ToString());
            Assert.Equal(6, cooking.Slots[0].Itemstack.StackSize);
        }
    }

    // Vanilla's diluted alum took crushed alum, which raw alum no longer crushes to: it takes alum powder.
    [AtlasScenario]
    public void Diluted_alum_takes_alum_powder()
    {
        var recipes = World.Api.GetBarrelRecipes().Where(r => r.Code == "dilutedalum").ToList();
        Assert.NotEmpty(recipes);
        Assert.All(recipes, r => Assert.Contains(r.Ingredients, i => i.SatisfiesAsIngredient(Stack("game:powder-alum", 1), false)));
        Assert.Contains("game:recipes/barrel/dilutedalum.json", System.RetargetedRecipes);
    }

    // The recipe export (the one the spalling scenario reads, built once per server): a roasting
    // record per sulfide, the firepit a station, 0.85 of a roasted concentrate out of one
    // concentrate; and the roasting guide page.
    [AtlasScenario(TimeoutMs = 600_000)]
    public void Roasting_is_exported()
    {
        var doc = ExportUnderTest.Get(World.Api);
        var type = doc["recipeTypes"]![RecipeSection.OreRoastingType]!;
        Assert.Equal("generic", (string?)type["shape"]);
        var records = doc["recipes"]!.Cast<JObject>().Where(r => (string?)r["type"] == RecipeSection.OreRoastingType).ToList();
        Assert.Equal(System.Applied!.Roasting, records.Count);
        Assert.Equal(records.Count, (int)type["count"]!);
        var galena = records.Single(r => (string?)r["id"] == "oreroasting|seraphhorizons:concentrate-galena|0");
        Assert.Equal("OreProcessing", (string?)galena["switch"]);
        var ingredients = (JArray)galena["ingredients"]!;
        Assert.Equal("seraphhorizons:concentrate-galena", (string?)ingredients[0]["code"]);
        Assert.Equal(1, (double)ingredients[0]["quantity"]!);
        Assert.Equal("game:firepit-cold", (string?)ingredients[1]["code"]);
        Assert.Equal("station", (string?)ingredients[1]["role"]);
        var output = galena["outputs"]![0]!;
        Assert.Equal("seraphhorizons:roastedconcentrate-galena", (string?)output["code"]);
        Assert.Equal(0.85, (double)output["quantity"]!, 9);
        Assert.Equal(OreRoasting.MeltingPoint, (int)galena["extra"]!["meltingPoint"]!);
        Assert.Contains(doc["guides"]!.Cast<JObject>(), g => (string?)g["code"] == OreProcessingSystem.RoastingGuidePage);
    }

    // Retorting in the still (#726): Expanded Matter's cooking pot recipe for mercury is off; cinnabar
    // and amalgam go into the boiler and their mercury into the condenser's bucket, one portion a step;
    // an amalgam's gold stays behind as sponge, which smelts whole.
    [AtlasScenario]
    public async Task Still_retorts_cinnabar_and_amalgam()
    {
        var still = MercuryStillSystem.Of(World.Api);
        Assert.Equal(["game:crushed-cinnabar", "game:powder-cinnabar", "seraphhorizons:amalgam-quartz_nativegold",
            "seraphhorizons:amalgam-quartz_nativesilver"], still.Marked.Order(StringComparer.Ordinal));
        Assert.True(Harmony.HasAnyPatches(MercuryStill.HarmonyId));
        Assert.Equal(5, still.MercuryRecipesOff);
        Assert.DoesNotContain(World.Api.ModLoader.GetModSystem<RecipeRegistrySystem>().CookingRecipes,
            r => r.CooksInto?.Code?.ToString() == "em:mercuryportion");

        // The sponge smelts whole: 20 to a gold ingot.
        var sponge = Item("seraphhorizons:sponge-quartz_nativegold").CombustibleProps!;
        Assert.Equal("game:ingot-gold", sponge.SmeltedStack.ResolvedItemstack.Collectible.Code.ToString());
        Assert.Equal(100.0 / 5, sponge.SmeltedRatio / (double)sponge.SmeltedStack.StackSize, 6);

        var pos = World.Spawn.AddCopy(70, 8, 64);
        World.SetBlock("game:verticalboiler-west", pos);
        World.SetBlock("game:condenser-west", pos.EastCopy());
        await World.Ticks(2);
        var boiler = Assert.IsType<BlockEntityBoiler>(W.BlockAccessor.GetBlockEntity(pos));
        var condenser = Assert.IsType<BlockEntityCondenser>(W.BlockAccessor.GetBlockEntity(pos.EastCopy()));
        var bucketBlock = Assert.IsAssignableFrom<BlockLiquidContainerTopOpened>(W.GetBlock(new AssetLocation("game:woodbucket")));
        condenser.Inventory[1].Itemstack = new ItemStack(bucketBlock);
        condenser.Inventory[0].Itemstack = Stack("game:waterportion", 1000);
        int Mercury() => bucketBlock.GetContent(condenser.Inventory[1].Itemstack)?.StackSize ?? 0;
        var input = boiler.Inventory[0];

        // A plain liquid still has no distillation; the amalgam has the still's.
        input.Itemstack = Stack("seraphhorizons:amalgam-quartz_nativegold", 3);
        var props = boiler.DistProps;
        Assert.NotNull(props);
        Assert.Equal("em:mercuryportion", props.DistilledStack.Code.ToString());

        // Taken out part way: the item under way comes back whole, with the sponge so far.
        for (int i = 0; i < 13; i++)
            Assert.True(condenser.ReceiveDistillate(input, props));
        Assert.Equal(13, Mercury());
        Assert.Equal(2, input.StackSize);
        var back = MercuryStill.TakeBack(W, input.Itemstack!);
        Assert.Equal(2, back[0].StackSize);
        Assert.False(back[0].Attributes.HasAttribute(MercuryStill.OwedKey));
        Assert.Equal("seraphhorizons:sponge-quartz_nativegold", back[1].Collectible.Code.ToString());
        Assert.Equal(1, back[1].StackSize);

        // Run to the end: 27 portions from 3 amalgam (9 each), and 3 sponge left in the boiler.
        for (int i = 0; i < 14; i++)
            Assert.True(condenser.ReceiveDistillate(input, props));
        Assert.Equal(27, Mercury());
        Assert.Equal("seraphhorizons:sponge-quartz_nativegold", input.Itemstack!.Collectible.Code.ToString());
        Assert.Equal(3, input.StackSize);
        Assert.Null(boiler.DistProps);

        // Cinnabar: 10 portions a powder, nothing left behind.
        input.Itemstack = Stack("game:powder-cinnabar", 2);
        props = boiler.DistProps!;
        for (int i = 0; i < 20; i++)
            Assert.True(condenser.ReceiveDistillate(input, props));
        Assert.Equal(47, Mercury());
        Assert.True(input.Empty);

        // A bucket holding something else takes none, and nothing is used up.
        bucketBlock.SetContent(condenser.Inventory[1].Itemstack, Stack("game:waterportion", 10));
        input.Itemstack = Stack("game:powder-cinnabar", 1);
        Assert.False(condenser.ReceiveDistillate(input, boiler.DistProps!));
        Assert.Equal(1, input.StackSize);
        Assert.False(input.Itemstack.Attributes.HasAttribute(MercuryStill.OwedKey));

        // The game's own still, lit and hot, runs it (the boiler's tick asks DistProps).
        condenser.Inventory[1].Itemstack = new ItemStack(bucketBlock);
        boiler.firepitStage = 6;
        boiler.fuelHours = 10;
        boiler.InputStackTemp = 100;
        for (int i = 0; i < 100 && !input.Empty; i++)
            await World.Ticks(5);
        Assert.True(input.Empty, "the still did not retort the cinnabar");
        Assert.Equal(10, Mercury());

        World.SetBlock("game:air", pos);
        World.SetBlock("game:air", pos.EastCopy());
    }

    // Retorting in the still (#726), in the same export: a record per input, the boiler and condenser
    // stations, the mercury in portions and litres, an amalgam's sponge; and the guide page.
    [AtlasScenario(TimeoutMs = 600_000)]
    public void Retorting_is_exported()
    {
        var doc = ExportUnderTest.Get(World.Api);
        var type = doc["recipeTypes"]![RecipeSection.OreRetortingType]!;
        Assert.Equal("generic", (string?)type["shape"]);
        var records = doc["recipes"]!.Cast<JObject>().Where(r => (string?)r["type"] == RecipeSection.OreRetortingType).ToList();
        Assert.Equal(MercuryStillSystem.Of(World.Api).Marked.Count, records.Count);
        var gold = records.Single(r => (string?)r["id"] == "oreretorting|seraphhorizons:amalgam-quartz_nativegold|0");
        Assert.Equal("OreProcessing", (string?)gold["switch"]);
        var ingredients = (JArray)gold["ingredients"]!;
        Assert.Equal("seraphhorizons:amalgam-quartz_nativegold", (string?)ingredients[0]["code"]);
        Assert.Equal("game:verticalboiler-west", (string?)ingredients[1]["code"]);
        Assert.Equal("station", (string?)ingredients[2]["role"]);
        var outputs = (JArray)gold["outputs"]!;
        Assert.Equal("em:mercuryportion", (string?)outputs[0]["code"]);
        Assert.Equal(9, (double)outputs[0]["quantity"]!, 9);
        Assert.Equal(0.09, (double)outputs[0]["litres"]!, 9);
        Assert.Equal("seraphhorizons:sponge-quartz_nativegold", (string?)outputs[1]["code"]);
        var cinnabar = records.Single(r => (string?)r["id"] == "oreretorting|game:powder-cinnabar|0");
        Assert.Single((JArray)cinnabar["outputs"]!);
        Assert.Contains(doc["guides"]!.Cast<JObject>(), g => (string?)g["code"] == MercuryStillSystem.GuidePage);
    }
}
