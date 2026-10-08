using SeraphHorizons.Mod;
using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Pipes;
using SeraphHorizons.Mod.Pipes.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// <c>UnifiedPipes</c> (README "Unified pipes"), on as it is by default: Pipes and Power Expanded's
/// pipes in copper and lead, its valves in bronze, each metal at its burst figure, lead bursting on
/// steam, ppex's plate-and-nails pipe and iron and steel valve recipes off and this mod's resolving:
/// the chain of angle, hollow section (the game's chute section, in lead too, its anvil and plate
/// recipes off) and pipe section, and pipe from pipe sections. Its sites are at x 35 to 45, z -95
/// from the spawn. The off check is in
/// <see cref="SwitchesOffScenarios"/>; <c>tools/tests/test_unified_pipes.py</c> holds the patch to
/// ppex's zip.
/// </summary>
public partial class SharedWorldScenarios
{
    private static readonly string[] PipeShapes = ["straight-ns", "bend-nw", "tjunction-uns", "xjunction-nswe"];

    private float BurstPressure(string code)
    {
        var block = W.GetBlock(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no block {code}");
        Assert.False(block.IsMissing, code);
        var getter = block.GetType().GetProperty("BurstPressure", BindingFlags.Public | BindingFlags.Instance)
                     ?? throw new Xunit.Sdk.XunitException($"{code} has no BurstPressure");
        return (float)getter.GetValue(block)!;
    }

    [AtlasScenario, ReadsBootLog]
    public void UnifiedPipes_bind_and_patch_without_a_warning()
    {
        Assert.True(World.Api.ModLoader.GetModSystem<UnifiedPipesSystem>().On);
        Assert.True(Harmony.HasAnyPatches(UnifiedPipesSystem.HarmonyId));
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("Unified pipes", StringComparison.Ordinal)
                        || e.Message.Contains("unifiedpipes", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("chutesection", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    [AtlasScenario]
    public void UnifiedPipes_copper_and_lead_pipes_exist_at_their_burst_figures()
    {
        var figures = new UnifiedPipesConfig();
        foreach (var shape in PipeShapes)
        {
            Assert.Equal(figures.LeadBurstPressure, BurstPressure($"ppex:pipe-{shape}-lead"));
            Assert.Equal(figures.CopperBurstPressure, BurstPressure($"ppex:pipe-{shape}-copper"));
            Assert.Equal(figures.IronBurstPressure, BurstPressure($"ppex:pipe-{shape}-iron"));
            Assert.Equal(figures.SteelBurstPressure, BurstPressure($"ppex:pipe-{shape}-steel"));
        }
        // ppex's own model (a server's block holds no resolved textures: test_unified_pipes.py checks the patch's)
        var copper = W.GetBlock(new AssetLocation("ppex:pipe-straight-ns-copper"))!;
        Assert.Equal(W.GetBlock(new AssetLocation("ppex:pipe-straight-ns-iron"))!.Shape.Base, copper.Shape.Base);
        Assert.Contains("ppex", copper.CreativeInventoryTabs);
        Assert.Contains("Copper", copper.GetHeldItemName(new ItemStack(copper)));
    }

    [AtlasScenario]
    public void UnifiedPipes_valves_are_bronze_and_the_iron_and_steel_ones_are_not_listed()
    {
        var figures = new UnifiedPipesConfig();
        foreach (var valve in new[] { "valve", "pressurevalve" })
        {
            foreach (var bronze in PipeRules.Bronzes)
            {
                var code = $"ppex:pipe-{valve}-sn-{bronze}";
                Assert.Equal(figures.BronzeBurstPressure, BurstPressure(code));
                var block = W.GetBlock(new AssetLocation(code))!;
                Assert.Contains("ppex", block.CreativeInventoryTabs);
            }
            // kept as blocks, so placed ones survive, but no longer listed or made
            var iron = W.GetBlock(new AssetLocation($"ppex:pipe-{valve}-sn-iron"))!;
            Assert.False(iron.IsMissing);
            Assert.True(iron.CreativeInventoryTabs is null or { Length: 0 });
        }
    }

    [AtlasScenario]
    public void UnifiedPipes_ppex_plate_and_valve_pipe_recipes_are_off_and_ours_resolve()
    {
        string[] retired = ["ppex:pipe-straight-", "ppex:pipe-bend-", "ppex:pipe-tjunction-", "ppex:pipe-xjunction-",
                            "ppex:pipe-valve-", "ppex:pipe-pressurevalve-"];
        var ppexLeft = W.GridRecipes
            .Where(r => r.Name?.Domain == "ppex" && retired.Any(p => r.Output?.Code?.ToString().StartsWith(p) == true))
            .Select(r => $"{r.Name}: {r.Output?.Code}").ToList();
        Assert.True(ppexLeft.Count == 0, "ppex still makes:\n" + string.Join("\n", ppexLeft));

        // A grid recipe's name is its file's domain and its own "name".
        var ours = W.GridRecipes.Where(r => r.Name?.Domain == "seraphhorizons" && r.Output?.Code?.Path.StartsWith("pipe-") == true).ToList();
        Assert.All(ours, r => Assert.NotNull(r.Output?.ResolvedItemStack));
        // every ingredient resolved, a wildcard one (the hammer, a pipe of any metal) to at least one stack
        Assert.All(ours, r => Assert.All(r.ResolvedIngredients.Where(i => i != null),
            i => Assert.True(i.ResolvedItemStack != null || i.IsWildCard, $"{r.Name}: {i.Code} unresolved")));
        var outputs = ours.Select(r => (Code: r.Output.Code.ToString(), r.Output.Quantity)).ToList();
        foreach (var bronze in PipeRules.Bronzes)
        {
            Assert.Contains(($"ppex:pipe-valve-sn-{bronze}", 1), outputs);
            Assert.Contains(($"ppex:pipe-pressurevalve-sn-{bronze}", 1), outputs);
        }
        // nothing makes a fitting from straight pipe any more
        Assert.DoesNotContain(ours, r => !r.Output.Code.Path.Contains("valve")
                                         && r.ResolvedIngredients.Any(i => i?.Code?.Path.StartsWith("pipe-straight") == true));

        // Every shape in every metal, from pipe sections of that metal and its joint, in the right number.
        foreach (var metal in PipeRules.PipeMaterials)
        {
            bool soldered = PipeSections.SolderedMetals.Contains(metal);
            foreach (var (shape, sections, pipes) in PipeSections.PipeShapes)
            {
                var code = PipeSections.Pipe(shape, metal);
                var made = ours.Where(r => r.Output.Code.ToString() == code).ToList();
                // copper and lead: tin or silver solder; iron and steel: one recipe
                Assert.Equal(soldered ? 2 : 1, made.Count);
                foreach (var r in made)
                {
                    Assert.Equal(pipes, r.Output.Quantity);
                    var cells = r.ResolvedIngredients.Where(i => i != null).ToList();
                    Assert.Equal(sections, cells.Count(i => i.Code?.ToString() == PipeSections.PipeSection(metal)));
                    Assert.DoesNotContain(cells, i => i.Code?.Path.StartsWith("pipesection-") == true && i.Code.ToString() != PipeSections.PipeSection(metal));
                    if (soldered)
                    {
                        var solder = Assert.Single(cells, i => i.Code?.Path.StartsWith("solderbar-") == true);
                        Assert.Equal(sections, solder.Quantity); // a bar per section, as a chute
                        Assert.Single(cells, i => i.IsTool && i.Code?.ToString() == "game:solderingiron");
                        Assert.DoesNotContain(cells, i => i.Code?.Path.StartsWith("hammer") == true);
                        Assert.Equal(sections + 2, cells.Count);
                    }
                    else
                    {
                        Assert.Equal(1, Assert.Single(cells, i => i.Code?.ToString() == $"game:metalnailsandstrips-{metal}").Quantity);
                        Assert.Contains(cells, i => i.IsTool && i.SatisfiesAsIngredient(new ItemStack(W.GetItem(new AssetLocation("game:hammer-iron")))));
                        Assert.Equal(sections + 2, cells.Count);
                    }
                }
            }
        }
        // no pipe, of any mod's recipe, is made from a chute section
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Domain == "ppex" && r.Output.Code.Path.StartsWith("pipe-")
                                                  && r.ResolvedIngredients.Any(i => i?.Code?.Path.StartsWith("chutesection-") == true));
    }

    /// <summary>The chain: the angle (copper, lead) forged from an ingot; the hollow section, the game's
    /// chute section in copper and lead (no iron or steel), made only from two angles soldered, the
    /// game's anvil and plate recipes for it off; the pipe section in every metal, made by no grid or
    /// anvil recipe; and the game's chutes taking copper chute sections only.</summary>
    [AtlasScenario]
    public void UnifiedPipes_the_chain_angle_hollow_section_pipe_section()
    {
        static string Title(string metal) => $"{char.ToUpperInvariant(metal[0])}{metal[1..]}";
        var smithing = World.Api.GetSmithingRecipes();

        // the angle: an item per metal, named, with a handbook section, forged from one ingot
        foreach (var metal in PipeSections.AngleMetals)
        {
            var angle = W.GetItem(new AssetLocation(PipeSections.Angle(metal)));
            Assert.NotNull(angle);
            Assert.Equal($"{Title(metal)} angle", new ItemStack(angle).GetName());
            Assert.Contains("items", angle!.CreativeInventoryTabs);
            Assert.True(angle.Attributes?["handbook"]?["extraSections"].Exists == true, $"no handbook section on {angle.Code}");
            var forged = Assert.Single(smithing, r => r.Output?.Code?.ToString() == PipeSections.Angle(metal));
            Assert.Equal($"game:ingot-{metal}", forged.Ingredient.Code.ToString());
            Assert.NotNull(forged.Output.ResolvedItemStack);
            Assert.Equal(1, forged.Output.Quantity);
            Assert.Equal(42, forged.Voxels.Cast<bool>().Count(v => v));
        }
        Assert.DoesNotContain(W.Items, i => i?.Code is { Domain: "seraphhorizons" } c && c.Path.StartsWith("angle-")
                                            && !PipeSections.AngleMetals.Contains(c.Path["angle-".Length..]));
        Assert.Contains("press brake", Lang.GetL("en", "seraphhorizons:angle-handbook-text"));

        // the hollow section (named the tube blank): copper and lead only, named alike, mechanics lists copper alone
        Assert.Equal(ChuteSections.Metals.Select(ChuteSections.Section).Order(),
            W.Items.Where(i => i?.Code is { Domain: "game" } c && c.Path.StartsWith("chutesection-")).Select(i => i.Code.ToString()).Order());
        foreach (var metal in ChuteSections.Metals)
        {
            var item = W.GetItem(new AssetLocation(ChuteSections.Section(metal)))!;
            Assert.Equal($"{Title(metal)} Tube Blank", new ItemStack(item).GetName());
            Assert.Contains("items", item.CreativeInventoryTabs);
            Assert.Equal(metal == PipeRules.Copper, item.CreativeInventoryTabs.Contains("mechanics"));
            Assert.True(item.Attributes?["handbook"]?["extraSections"].Exists == true, $"no handbook section on {item.Code}");
        }
        Assert.Contains("mandrel station", Lang.GetL("en", "seraphhorizons:chutesection-handbook-text"));
        Assert.Contains("draw bench", Lang.GetL("en", "seraphhorizons:chutesection-handbook-text"));

        // the game's anvil recipe for it is off, and nothing else forges one
        Assert.DoesNotContain(smithing, r => r.Output?.Code?.Path.StartsWith("chutesection-") == true);
        // on the grid, only two angles of its metal, two solder bars and a soldering iron make one
        var hollows = W.GridRecipes.Where(r => r.Output?.Code?.Path.StartsWith("chutesection-") == true).ToList();
        Assert.Equal(ChuteSections.Metals.Length * 2, hollows.Count); // tin or silver solder
        foreach (var metal in ChuteSections.Metals)
        {
            var made = hollows.Where(r => r.Output.Code.ToString() == ChuteSections.Section(metal)).ToList();
            Assert.Equal(2, made.Count);
            foreach (var r in made)
            {
                Assert.Equal("seraphhorizons", r.Name?.Domain);
                Assert.Equal(1, r.Output.Quantity);
                var cells = r.ResolvedIngredients.Where(i => i != null).ToList();
                Assert.Equal(ChuteSections.AnglesPerSection, cells.Count(i => i.Code?.ToString() == PipeSections.Angle(metal)));
                Assert.Equal(ChuteSections.SolderBarsPerSection, Assert.Single(cells, i => i.Code?.Path.StartsWith("solderbar-") == true).Quantity);
                Assert.Single(cells, i => i.IsTool && i.Code?.ToString() == "game:solderingiron");
                Assert.Equal(ChuteSections.AnglesPerSection + 2, cells.Count);
            }
        }

        // the pipe section: every metal, named, with a handbook section; no grid or anvil recipe makes it
        foreach (var metal in PipeSections.Metals)
        {
            var item = W.GetItem(new AssetLocation(PipeSections.PipeSection(metal)));
            Assert.NotNull(item);
            Assert.Equal($"{Title(metal)} pipe section", new ItemStack(item).GetName());
            Assert.Contains("items", item!.CreativeInventoryTabs);
            Assert.True(item.Attributes?["handbook"]?["extraSections"].Exists == true, $"no handbook section on {item.Code}");
        }
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Path.StartsWith("pipesection-") == true);
        Assert.DoesNotContain(smithing, r => r.Output?.Code?.Path.StartsWith("pipesection-") == true);
        Assert.Contains("mandrel station", Lang.GetL("en", "seraphhorizons:pipesection-handbook-text"));

        // chutes: copper sections only, a solder bar per section and the soldering iron, no hammer, the
        // game's yields; Better Ruins' solderless blueprint chutes are gone
        var chutes = W.GridRecipes.Where(r => r.Output?.Code is { Domain: "game" } c && c.Path.StartsWith("chute-")).ToList();
        Assert.Equal(ChuteSections.ChuteRecipes.Length * 2, chutes.Count); // tin or silver solder
        Assert.DoesNotContain(chutes, r => r.ResolvedIngredients.Any(i => i?.Code?.ToString() == ChuteSections.BetterRuinsBlueprint));
        foreach (var chute in ChuteSections.ChuteRecipes)
        {
            var made = chutes.Where(r => r.Output.Code.Path == chute.Output).ToList();
            Assert.Equal(2, made.Count);
            foreach (var r in made)
            {
                Assert.Equal(1, r.Output.Quantity);
                var cells = r.ResolvedIngredients.Where(i => i != null).ToList();
                Assert.Equal(chute.Sections, cells.Count(i => i.Code?.ToString() == ChuteSections.Section(PipeRules.Copper)));
                Assert.All(cells.Where(i => i.Code?.Path.StartsWith("chutesection") == true),
                    i => Assert.Equal(ChuteSections.Section(PipeRules.Copper), i.Code.ToString()));
                Assert.DoesNotContain(cells, i => i.Code?.Path.StartsWith("pipesection") == true);
                Assert.Equal(chute.Sections, Assert.Single(cells, i => i.Code?.Path.StartsWith("solderbar-") == true).Quantity);
                Assert.Contains(cells, i => i.IsTool && i.Code?.ToString() == "game:solderingiron");
                Assert.DoesNotContain(cells, i => i.Code?.Path.StartsWith("hammer") == true);
            }
        }

        // Better Ruins' blueprint keeps the rest of its recipes: its riveted metal block resolves
        var riveted = W.GridRecipes.Where(r => r.Output?.Code?.Path.StartsWith("metalblock-new-riveted-") == true
                                               && r.ResolvedIngredients.Any(i => i?.Code?.ToString() == ChuteSections.BetterRuinsBlueprint)).ToList();
        Assert.NotEmpty(riveted);
        Assert.All(riveted, r => Assert.NotNull(r.Output.ResolvedItemStack));
    }

    [AtlasScenario]
    public void UnifiedPipes_ppex_recipe_costs_see_only_ppex_recipes()
    {
        var costs = AccessTools.TypeByName(UnifiedPipesSystem.RecipeCostsType)!;
        var gridRecipesFor = AccessTools.DeclaredMethod(costs, "GridRecipesFor", [typeof(ICoreAPI), typeof(AssetLocation)])!;
        Assert.True(Harmony.GetPatchInfo(gridRecipesFor)?.Postfixes.Any(p => p.owner == UnifiedPipesSystem.HarmonyId) == true);
        var found = ((IEnumerable<GridRecipe>)gridRecipesFor.Invoke(null, [World.Api, new AssetLocation("ppex:pipe-*")])!).ToList();
        Assert.DoesNotContain(found, r => r.Name?.Domain != "ppex");
        // its fittings (passthroughs, outlet) are ppex's and still seen
        Assert.Contains(found, r => r.Output?.Code?.Path.StartsWith("pipe-passthrough") == true);
        Assert.Contains(W.GridRecipes, r => r.Name?.Domain == "seraphhorizons" && r.Output?.Code?.Path.StartsWith("pipe-") == true);
    }

    [AtlasScenario]
    public void UnifiedPipes_the_steam_power_page_lists_every_metal_and_lead_is_water_only()
    {
        var text = Lang.GetL("en", "ppex:handbook-steampower-text");
        Assert.Contains("<strong>lead</strong> pipe holds up to <strong>0.5 atm</strong>", text);
        Assert.Contains("<strong>copper</strong> <strong>3 atm</strong>", text);
        Assert.Contains("<strong>steel</strong> <strong>10 atm</strong>", text);
        Assert.Contains("Lead pipe is for water only", text);
        Assert.DoesNotContain("pipe bursts above <strong>5 atm</strong>", text);
        Assert.Contains("Valves and pressure valves are bronze", Lang.GetL("en", "ppex:handbook-fittings-text"));
        Assert.Contains("Water only", Lang.GetL("en", "ppex:blockdesc-pipe-straight-ns-lead"));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task UnifiedPipes_a_lead_pipe_bursts_on_steam_and_copper_holds_it()
    {
        if (SteamSourceLeftOutForOldPpex())
            return;
        // source | copper | copper | rock, and source | lead | rock: each run opens only onto its source.
        var copperAt = World.Spawn.AddCopy(35, 2, -95);
        World.SetBlock(SteamSource, copperAt);
        World.SetBlock("ppex:pipe-straight-we-copper", copperAt.EastCopy());
        World.SetBlock("ppex:pipe-straight-we-copper", copperAt.EastCopy(2));
        World.SetBlock("game:rock-granite", copperAt.EastCopy(3));
        var leadAt = World.Spawn.AddCopy(40, 2, -95);
        World.SetBlock(SteamSource, leadAt);
        World.SetBlock("ppex:pipe-straight-we-lead", leadAt.EastCopy());
        World.SetBlock("game:rock-granite", leadAt.EastCopy(2));
        await World.Ticks(2);
        foreach (var at in new[] { copperAt, leadAt })
            Assert.IsType<BlockEntityCreativeSteamSource>(W.BlockAccessor.GetBlockEntity(at))
                .Configure(pressure: 2, flow: CreativeSteamSource.MaxFlowSetting);

        // Lead: gone within seconds, long before exlib's 30 s over-pressure timer, and dropped as itself.
        await World.Until(() => W.BlockAccessor.GetBlock(leadAt.EastCopy()).Id == 0, 300);
        var dropped = W.GetEntitiesAround(leadAt.EastCopy().ToVec3d().Add(0.5, 0.5, 0.5), 3, 3,
            e => e is EntityItem item && item.Itemstack?.Collectible?.Code?.ToString() == "ppex:pipe-straight-ns-lead");
        Assert.NotEmpty(dropped);
        foreach (var e in dropped)
            e.Die(EnumDespawnReason.Removed);

        // Copper: holds 2 atm of steam (its figure is 3).
        var pipe = W.BlockAccessor.GetBlockEntity(copperAt.EastCopy())!;
        await World.Until(() => PipeValue<string>(pipe, "Medium") == "Steam" && PipeValue<float>(pipe, "Pressure") > 1.9f, 900);
        await World.Ticks(60);
        Assert.Equal("ppex:pipe-straight-we-copper", W.BlockAccessor.GetBlock(copperAt.EastCopy()).Code.ToString());
        Assert.Equal("ppex:pipe-straight-we-copper", W.BlockAccessor.GetBlock(copperAt.EastCopy(2)).Code.ToString());

        foreach (var at in new[] { copperAt, leadAt })
            for (int dx = 0; dx <= 3; dx++)
                World.SetBlock("game:air", at.EastCopy(dx));
    }
}
