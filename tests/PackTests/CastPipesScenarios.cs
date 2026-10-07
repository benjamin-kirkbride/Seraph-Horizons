using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Pipes;
using SeraphHorizons.Mod.Pipes.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, CastPipes (mods-src/seraphhorizons/Pipes/Game/CastPipesSystem.cs): Steelmaking
// Expanded's tool mold with a pipe tool type, casting two pipe sections of iron or steel (an item
// UnifiedPipes adds), and each section banded into a pipe.
public partial class SharedWorldScenarios
{
    private static readonly string[] AllMoldColors = ["blue", "fire", "black", "brown", "cream", "earthyorange", "gray", "orange", "red", "tan"];

    private static Block PipeMold(IWorldAccessor world, string color, string state) =>
        GearBlankParts.Block(world, CastPipeMold.Mold(color, state));

    private static ItemStack HotMetal(IWorldAccessor world, string metal, float temperature = 1600f)
    {
        var stack = new ItemStack(GearBlankParts.Item(world, $"game:ingot-{metal}"));
        stack.Collectible.SetTemperature(world, stack, temperature, false);
        return stack;
    }

    [AtlasScenario]
    public void Cast_pipe_system_is_on_and_smex_molds_passed_the_guard()
    {
        Assert.True(W.Api.ModLoader.GetModSystem<CastPipesSystem>().On);
        Assert.DoesNotContain(World.BootDiagnostics, e => e.Message.Contains("Cast pipes:", StringComparison.Ordinal));
        // smex's own molds are as it ships them.
        foreach (var (type, units) in new[] { ("plate", 200), ("doubleingot", 200), ("quadrod", 400) })
            Assert.Equal(units, GearBlankParts.Block(W, $"smex:toolmold-blue-fired-{type}").Attributes["requiredUnits"].AsInt());
    }

    // Raw in the three clays, fired in all ten colours, the raw one clay-formed (a floor and the
    // walls round a trough with a core) and fired in a pit kiln or a beehive kiln to the fired one.
    [AtlasScenario]
    public void Pipe_molds_are_clay_formed_and_fired()
    {
        var clayforming = World.Api.GetClayformingRecipes();
        foreach (string clay in new[] { "blue", "fire", "red" })
        {
            var raw = PipeMold(W, clay, "raw");
            var recipe = Assert.Single(clayforming, r => r.Output.ResolvedItemstack?.Collectible == raw);
            Assert.True(recipe.Ingredient.SatisfiesAsIngredient(new ItemStack(GearBlankParts.Item(W, "game:clay-" + clay))));
            Assert.Equal(2, GearBlankParts.Layers(recipe));

            var fired = raw.CombustibleProps!.SmeltedStack.ResolvedItemstack.Collectible;
            Assert.Equal(EnumSmeltType.Fire, raw.CombustibleProps.SmeltingType);
            Assert.IsType<BlockToolMold>(fired);
            Assert.Equal(("fired", CastPipeMold.ToolType), (fired.Variant["materialtype"], fired.Variant["tooltype"]));
            for (int level = 0; level < 4; level++)
            {
                var kiln = raw.Attributes["beehivekiln"][level.ToString()].AsObject<JsonItemStack>(null, "smex");
                Assert.True(kiln.Resolve(W, "pipe mold kiln"), $"{raw.Code} beehive kiln {level}");
                Assert.Equal(CastPipeMold.ToolType, kiln.ResolvedItemstack.Collectible.Variant["tooltype"]);
            }
        }
        foreach (string color in AllMoldColors)
            Assert.IsType<BlockToolMold>(PipeMold(W, color, "fired"));
        Assert.Equal("Raw pipe ceramic mold", new ItemStack(PipeMold(W, "red", "raw")).GetName());
        Assert.Equal("Pipe ceramic mold", new ItemStack(PipeMold(W, "tan", "fired")).GetName());
    }

    // The fired mold carries the patch's figures and, as smex's own molds, the attributes it shares
    // with them (the game merges attributes and attributes by type); the canal pedestal takes it.
    [AtlasScenario]
    public void Pipe_mold_takes_one_ingot_and_drops_two_pipe_sections()
    {
        var mold = PipeMold(W, "black", "fired");
        Assert.Equal(CastPipeMold.RequiredUnits, mold.Attributes["requiredUnits"].AsInt());
        Assert.Equal(1, mold.Attributes["fillHeight"].AsInt());
        Assert.Equal(CastPipeMold.SectionCode + "-{metal}", mold.Attributes["drop"]["code"].AsString());
        Assert.Equal(CastPipeMold.SectionsPerFill, mold.Attributes["drop"]["quantity"].AsInt());
        Assert.Equal(2, CastPipeMold.SectionsPerFill);
        Assert.True(mold.Attributes["reinforcable"].AsBool(), "smex's shared attributes are gone from the pipe mold");
        Assert.True(mold.Attributes["handbook"]["extraSections"].Exists, "no handbook section on the pipe mold");
        Assert.Contains("construction", mold.CreativeInventoryTabs);
        Assert.Equal("seraphhorizons:pipesection-{metal}", mold.Attributes["drop"]["code"].AsString());
        Assert.Contains("pipe sections", Lang.GetL("en", "seraphhorizons:castpipes-handbook-mold-text"));
        Assert.DoesNotContain("chute", Lang.GetL("en", "seraphhorizons:castpipes-handbook-mold-text"));

        var kinds = AccessTools.TypeByName("SteelmakingExpanded.BlockNetworkMolten.Blocks.MoldKinds");
        Assert.NotNull(kinds);
        Assert.True((bool)AccessTools.Method(kinds, "FitsPedestal").Invoke(null, [mold])!);

        foreach (string metal in CastPipeMold.Metals)
            Assert.NotNull(W.GetItem(new AssetLocation(CastPipeMold.Section(metal))));
    }

    // On the ground a fired mold takes molten iron and steel as a crucible pours them, refuses a metal
    // with no pipe section (tin bronze), and once full and hardened casts two sections of its metal.
    [AtlasScenario]
    public async Task Pipe_mold_casts_pipe_sections_from_iron_and_steel()
    {
        var origin = World.Spawn.AddCopy(-80, 12, -90);
        GearBlankParts.Room(World, origin);
        foreach (var (metal, dx) in new[] { ("iron", -1), ("steel", 1) })
        {
            var pos = origin.AddCopy(dx, 0, 0);
            World.SetBlock(CastPipeMold.Mold("brown", "fired"), pos);
            await World.Ticks(2);
            var mold = W.BlockAccessor.GetBlockEntity(pos) as BlockEntityToolMold
                       ?? throw new Xunit.Sdk.XunitException($"no mold at {pos}: {W.BlockAccessor.GetBlock(pos).Code}");
            Assert.False(mold.CanReceive(new ItemStack(GearBlankParts.Item(W, "game:ingot-tinbronze"))));
            Assert.True(mold.CanReceive(new ItemStack(GearBlankParts.Item(W, $"game:ingot-{metal}"))));

            int amount = CastPipeMold.IngotUnits;
            mold.ReceiveLiquidMetal(HotMetal(W, metal), ref amount, 1600f);
            Assert.Equal(0, amount);
            Assert.True(mold.IsFull);
            Assert.Equal(CastPipeMold.RequiredUnits, mold.FillLevel);

            mold.MetalContent.Collectible.SetTemperature(W, mold.MetalContent, 20f, false);
            Assert.True(mold.IsHardened);
            var cast = Assert.Single(mold.GetStateAwareMoldedStacks()!);
            Assert.Equal(CastPipeMold.Section(metal), cast.Collectible.Code.ToString());
            Assert.Equal(CastPipeMold.SectionsPerFill, cast.StackSize);
        }
    }

    // Steelmaking Expanded's canal pedestal, driven by its own members: the pipe mold goes on, molten
    // iron pushed into the pedestal's canal cell drains into it by the mold's requiredUnits while it
    // pours, and the mold taken off hardened and set down casts two iron pipe sections.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Canal_pedestal_casts_pipe_sections()
    {
        var origin = World.Spawn.AddCopy(-80, 12, -85);
        GearBlankParts.Room(World, origin);
        var pedestalBlock = W.Blocks.FirstOrDefault(b => b?.Code is { Domain: "smex" } c
                                                         && c.Path.StartsWith("moltencanal-moldpedestal-") && c.Path.EndsWith("-s"))
                            ?? throw new Xunit.Sdk.XunitException("no smex mold pedestal");
        var pos = origin.Copy();
        W.BlockAccessor.SetBlock(pedestalBlock.Id, pos);
        await World.Ticks(5);
        var pedestal = W.BlockAccessor.GetBlockEntity(pos)
                       ?? throw new Xunit.Sdk.XunitException($"no pedestal entity at {pos}: placed {pedestalBlock.Code} ({pedestalBlock.EntityClass}), found {W.BlockAccessor.GetBlock(pos).Code}");
        var type = pedestal.GetType();
        Assert.Equal("BlockEntityMoltenCanalMoldPedestal", type.Name);
        T Get<T>(string property) => (T)AccessTools.Property(type, property).GetValue(pedestal)!;

        AccessTools.Method(type, "AddMold", [typeof(ItemStack)]).Invoke(pedestal, [new ItemStack(PipeMold(W, "blue", "fired"))]);
        Assert.True(Get<bool>("IsMold"));
        Assert.Equal(CastPipeMold.RequiredUnits, Get<int>("MoldMaxUnits"));
        if (!Get<bool>("IsPouring"))
            AccessTools.Method(type, "TryTogglePouring").Invoke(pedestal, []);
        Assert.True(Get<bool>("IsPouring"));

        var push = AccessTools.Method(type, "PushMetal", [typeof(int), typeof(ItemStack), typeof(IWorldAccessor)]);
        for (int tick = 0; tick < 200 && Get<int>("MoldCurrentUnits") < CastPipeMold.RequiredUnits; tick++)
        {
            if (Get<int>("CellAmount") == 0)
                push.Invoke(pedestal, [20, HotMetal(W, "iron"), W]);
            await World.Ticks(1);
        }
        Assert.Equal(CastPipeMold.RequiredUnits, Get<int>("MoldCurrentUnits"));
        var metal = Get<ItemStack>("MoldMetalContent");
        Assert.Equal("game:ingot-iron", metal.Collectible.Code.ToString());

        // Hardened (the game clock is slow to cool it), then off the pedestal with its contents.
        metal.Collectible.SetTemperature(W, metal, 20f, false);
        var moldStack = (ItemStack)AccessTools.Method(type, "RemoveMold").Invoke(pedestal, [])!;
        Assert.False(Get<bool>("IsMold"));
        Assert.Equal(CastPipeMold.Mold("blue", "fired"), moldStack.Collectible.Code.ToString());

        // Set down, as a player puts it on the ground: smex's placement patch puts the contents in.
        var ground = origin.AddCopy(2, 0, 0);
        W.BlockAccessor.SetBlock(moldStack.Block.Id, ground, moldStack);
        await World.Ticks(2);
        var mold = Assert.IsType<BlockEntityToolMold>(W.BlockAccessor.GetBlockEntity(ground));
        Assert.Equal(CastPipeMold.RequiredUnits, mold.FillLevel);
        mold.MetalContent.Collectible.SetTemperature(W, mold.MetalContent, 20f, false);
        var cast = Assert.Single(mold.GetStateAwareMoldedStacks()!);
        Assert.Equal(CastPipeMold.Section("iron"), cast.Collectible.Code.ToString());
        Assert.Equal(CastPipeMold.SectionsPerFill, cast.StackSize);
    }

    // A cast section, nails and strips of its metal and a hammer: a straight ppex pipe of that metal
    // (UnifiedPipes' recipe), so an ingot casts two pipes.
    [AtlasScenario]
    public void Cast_pipe_sections_are_banded_into_pipes()
    {
        foreach (string metal in CastPipeMold.Metals)
        {
            var section = new ItemStack(GearBlankParts.Item(W, CastPipeMold.Section(metal)));
            var recipes = W.GridRecipes.Where(r => r.Output?.Code?.ToString() == CastPipeMold.StraightPipe(metal)
                                                   && r.ResolvedIngredients.Any(i => i != null && i.SatisfiesAsIngredient(section))).ToList();
            var recipe = Assert.Single(recipes);
            Assert.Equal(1, recipe.Output.Quantity);
            Assert.Equal(CastPipeMold.PipesPerIngot(), recipe.Output.Quantity * (double)CastPipeMold.SectionsPerFill);
            var cells = recipe.ResolvedIngredients.Where(i => i != null).ToList();
            Assert.Equal(3, cells.Count);
            Assert.Equal(1, cells.Count(i => i.SatisfiesAsIngredient(section)));
            Assert.Contains(cells, i => i.SatisfiesAsIngredient(new ItemStack(GearBlankParts.Item(W, $"game:metalnailsandstrips-{metal}"))));
            string other = metal == "iron" ? "steel" : "iron";
            Assert.DoesNotContain(cells, i => i.SatisfiesAsIngredient(new ItemStack(GearBlankParts.Item(W, $"game:metalnailsandstrips-{other}"))));
            Assert.Contains(cells, i => i.IsTool && i.SatisfiesAsIngredient(new ItemStack(GearBlankParts.Item(W, "game:hammer-iron"))));
        }
    }
}
