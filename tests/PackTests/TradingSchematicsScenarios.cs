using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Schematics;
using SeraphHorizons.Mod.Trading.Schematics.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Trading/Schematics (#468, #469) against the pinned mods: the machines'
/// first-stage recipes take their schematic (kept on crafting), no recipe makes or copies a
/// schematic, recipes using one keep it, the loot lists and structures hand none out, and the trade
/// lists sell each from the table's sellers at its tier. The plain world is enough, so it shares
/// <see cref="TradingScenarios"/>' boot with the other plain-world trading features.
/// </summary>
public partial class TradingScenarios
{
    private SchematicsSystem Schematics => SchematicsSystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no SchematicsSystem");
    private SchematicTable Table => Schematics.Table ?? throw new Xunit.Sdk.XunitException("the schematic table did not load");

    private IEnumerable<GridRecipe> Making(string output) => W.GridRecipes.Where(r => r.Output?.Code?.ToString() == output);

    private static bool Takes(GridRecipe recipe, string schematic) =>
        recipe.ResolvedIngredients!.Any(i => i?.Code?.ToString() == schematic && !i.Consume);

    [AtlasScenario]
    [ReadsBootLog]
    public void The_boot_logs_nothing_about_schematics()
    {
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("Schematics:", StringComparison.Ordinal) || e.Message.Contains("seraphhorizons:schematic", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
        var report = Schematics.Report ?? throw new Xunit.Sdk.XunitException("the recipe pass did not run");
        Assert.Empty(report.Failed);
        output.WriteLine($"Gated ({report.Gated.Count}):\n  " + string.Join("\n  ", report.Gated.Distinct().OrderBy(s => s)));
        output.WriteLine($"Removed ({report.Removed.Count}):\n  " + string.Join("\n  ", report.Removed));
        output.WriteLine($"Kept on crafting: {report.Kept}");
    }

    [AtlasScenario]
    public void Every_machine_schematic_exists()
    {
        foreach (var gate in Table.Gates)
            Assert.True(W.GetItem(new AssetLocation(gate.Schematic)) is { Id: > 0 }, gate.Schematic);
    }

    [AtlasScenario]
    public void The_sampled_machines_take_their_schematic()
    {
        foreach (var (output, schematic) in new[]
                 {
                     ("game:windmillrotor-wood-north", "seraphhorizons:schematic-windmill"),
                     ("game:helvehammerbase-north", "seraphhorizons:schematic-helvehammer"),
                     ("flyingmachine:trestles", "seraphhorizons:schematic-biplane"),
                     ("gondolacablecar:routeplanner", "seraphhorizons:schematic-cablecar"),
                     ("immersivewoodworking:sawmill-frame-north", "seraphhorizons:schematic-sawmill"),
                     ("game:largegear3", "seraphhorizons:schematic-transmission"),
                     ("game:waterwheel-3m-north", "seraphhorizons:schematic-waterwheel"),
                 })
        {
            var recipes = Making(output).ToList();
            Assert.True(recipes.Count > 0, $"no recipe makes {output}");
            Assert.All(recipes, r => Assert.True(Takes(r, schematic), $"{r.Name} ({output}) does not take {schematic}: {r.IngredientPattern}"));
        }
    }

    [AtlasScenario]
    public void Every_gated_output_takes_its_schematic_in_every_recipe()
    {
        bool ModLoaded(string mod) => mod == "game" || Api.ModLoader.IsModEnabled(mod);
        var gatedMachines = new HashSet<string>();
        foreach (var recipe in W.GridRecipes)
        {
            string? output = recipe.Output?.Code?.ToString();
            if (output is null || Table.GateFor(output, ModLoaded) is not { } gate) continue;
            var filled = recipe.ResolvedIngredients!.Where(i => i != null).ToList();
            if (filled.Count == 1) continue; // a conversion (MadMechanics' clutch and transmission)
            Assert.True(Takes(recipe, gate.Schematic), $"{recipe.Name} ({output}) does not take {gate.Schematic}");
            gatedMachines.Add(gate.Machine);
        }
        // Every machine whose mod is in the pack has at least one gated recipe.
        Assert.Equal(Table.Gates.Where(g => g.Outputs.Any(o => ModLoaded(o.Mod))).Select(g => g.Machine).OrderBy(m => m), gatedMachines.OrderBy(m => m));
    }

    [AtlasScenario]
    public async Task A_gated_recipe_crafts_with_the_schematic_and_keeps_it()
    {
        // The helve hammer base, "H_P,CP_,PR_" in the game (one recipe per wood): the schematic takes
        // the first empty slot.
        var recipe = Making("game:helvehammerbase-north").First();
        var slots = recipe.ResolvedIngredients!.Select(i => (ItemSlot)new DummySlot(i == null ? null : Stack(i))).ToArray();
        var player = (await Customer()).Player;
        Assert.True(recipe.Matches(player, W, slots, recipe.Width));
        int schematicSlot = Array.FindIndex(recipe.ResolvedIngredients!, i => i?.Code?.ToString() == "seraphhorizons:schematic-helvehammer");
        var without = slots.Select((s, i) => i == schematicSlot ? new DummySlot() : s).ToArray();
        Assert.False(recipe.Matches(player, W, without, recipe.Width));
        Assert.True(recipe.ConsumeInput(player, slots, recipe.Width));
        Assert.Equal("seraphhorizons:schematic-helvehammer", slots[schematicSlot].Itemstack?.Collectible.Code.ToString());
    }

    // The first collectible the ingredient takes (tags included: the hammer and chisel are by tag).
    private ItemStack Stack(CraftingRecipeIngredient i) =>
        i.ResolvedItemStack?.Clone()
        ?? W.Collectibles.Where(c => c.Code != null && c.ItemClass == i.Type).Select(c => new ItemStack(c, i.Quantity))
            .First(stack => i.SatisfiesAsIngredient(stack));

    [AtlasScenario]
    public void A_BetterRuins_door_recipe_keeps_its_schematic()
    {
        // Not Scrolled's rolling, which takes it to make the rolled one.
        var doors = W.GridRecipes.Where(r => r.ResolvedIngredients!.Any(i => i?.Code?.ToString() == "betterruins:br-schematic-door")
                                             && r.ResolvedIngredients!.Count(i => i != null) > 1).ToList();
        Assert.NotEmpty(doors);
        Assert.All(doors, r => Assert.True(Takes(r, "betterruins:br-schematic-door"), r.Output!.Code!.ToString()));
    }

    [AtlasScenario]
    public void No_recipe_makes_or_copies_a_schematic_and_every_one_using_one_keeps_it()
    {
        foreach (var recipe in W.GridRecipes)
        {
            var filled = recipe.ResolvedIngredients!.Where(i => i != null).ToList();
            bool conversion = filled.Count == 1;
            string output = recipe.Output!.Code!.ToString();
            if (!conversion)
                Assert.False(Table.IsSold(output), $"{recipe.Name} makes {output}");
            foreach (var i in filled.Where(i => Table.IsSold(i!.Code!.ToString())))
            {
                if (conversion) continue; // Scrolled rolls them up, one for one
                Assert.False(i!.Consume, $"{recipe.Name} ({output}) consumes {i.Code}");
                Assert.Null(i.ReturnedStack);
            }
        }
        // The game's glider copy and Cartwright's two parchment recipes (Abyssal Depths' copy and craft
        // are not registered in the pack, and BetterRuins ships its copy disabled).
        var removed = string.Join(", ", Schematics.Report!.Removed);
        foreach (string name in new[] { "game:schematiccopy", "cartschematics-carts", "cartschematics-signs" })
            Assert.Contains(name, removed);
    }

    [AtlasScenario]
    public void Loot_lists_hold_no_schematics()
    {
        int randomizers = 0;
        foreach (var randomizer in W.Collectibles.OfType<ItemStackRandomizer>())
        {
            randomizers++;
            // Its stacks, read from the attributes in OnLoaded (a private field).
            foreach (var stack in Traverse.Create(randomizer).Field("Stacks").GetValue<RandomStack[]>() ?? [])
                Assert.False(stack.Code is { } code && Table.IsSold(code), $"{randomizer.Code} still holds {stack.Code}");
        }
        Assert.True(randomizers > 0);
        // And nowhere in any collectible's attributes, as a loot entry (a code with a chance).
        foreach (var c in W.Collectibles)
            if (c.Attributes?.Token is { } token)
                Assert.Equal(0, LootScrub.Strip(token.DeepClone(), Table));
    }

    [AtlasScenario]
    public void Structures_hand_out_parchment_instead()
    {
        foreach (string path in new[]
                 {
                     "betterruins:worldgen/schematics/overground/main/mediumruins/mediumruins-femursnapper-o3-1004.json",
                     "betterruins:worldgen/schematics/overground/main/village-prison/prison-3-medium.json",
                 })
        {
            var asset = Api.Assets.Get(new AssetLocation(path));
            var structure = asset.ToObject<BlockSchematicStructure>();
            Assert.Contains(structure.ItemCodes.Values, c => Table.IsSold(c.ToString()));
            structure.Remap();
            Assert.DoesNotContain(structure.ItemCodes.Values, c => Table.IsSold(c.ToString()));
            Assert.Contains(structure.ItemCodes.Values, c => c.ToString() == Table.Replacement);
            // A player's own schematic (WorldEdit) keeps what it holds.
            var plain = asset.ToObject<BlockSchematic>();
            plain.Remap();
            Assert.Contains(plain.ItemCodes.Values, c => Table.IsSold(c.ToString()));
        }
    }

    [AtlasScenario]
    public void Every_schematic_is_in_its_sellers_core_at_its_tier()
    {
        var lists = TradingSystem.Of(Api)?.Lists ?? throw new Xunit.Sdk.XunitException("the trade lists did not load");
        Assert.DoesNotContain(lists.Unresolved, u => u.Contains("schematic"));
        foreach (var sale in Table.Sales)
            foreach (string seller in sale.Sellers)
            {
                var def = lists.For(seller)!;
                var entry = def.Selling.Core.SingleOrDefault(e => CodePattern.Normalise(e.Code) == sale.Code);
                Assert.True(entry != null, $"{seller} does not sell {sale.Code}");
                Assert.Equal(sale.Tier, entry!.StandingTier);
                Assert.NotNull(lists.ItemFor(entry).Resolve(W).Stack);
                var region = Region.All.First();
                Assert.DoesNotContain(TradeListResolver.Resolve(def, region, sale.Tier - 1).Selling.Core, e => e == entry);
                Assert.Contains(TradeListResolver.Resolve(def, region, sale.Tier).Selling.Core, e => e == entry);
            }
    }
}
