using Atlas.XUnit;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.PackTests;

/// <summary>Immersive Woodworking's machine parts as a recipe registers them, for the scenarios of
/// seraphhorizons' <c>IronWoodworkingMachines</c> and its off check.</summary>
public static class MachineParts
{
    public const string Nails = "game:metalnailsandstrips-*";
    public const string Plate = "game:metalplate-*";
    public const string Rod = "game:rod-*";

    /// <summary>The part's one recipe. Immersive Woodworking registers it itself, from the recipe
    /// file it ships disabled.</summary>
    public static GridRecipe Recipe(IWorldAccessor world, WoodworkingMachineCosts.Part part) =>
        Assert.Single(world.GridRecipes, r => r.Output?.Code?.ToString() == WoodworkingMachineCosts.ModId + ":" + part.Output);

    /// <summary>The ingredient of <paramref name="code"/> in the first slot that takes it, or null
    /// when the recipe takes none.</summary>
    public static CraftingRecipeIngredient? Ingredient(GridRecipe recipe, string code) =>
        recipe.ResolvedIngredients!.FirstOrDefault(i => i?.Code?.ToString() == code);

    /// <summary>How many of <paramref name="code"/> the recipe takes over all its slots.</summary>
    public static int Count(GridRecipe recipe, string code) =>
        recipe.ResolvedIngredients!.Where(i => i?.Code?.ToString() == code).Sum(i => i!.Quantity);
}

// seraphhorizons, IronWoodworkingMachines (mods-src/seraphhorizons/WoodworkingMachineCosts.cs).
public partial class SharedWorldScenarios
{
    [AtlasScenario]
    public void Machine_parts_take_iron_and_many_nails()
    {
        foreach (var part in WoodworkingMachineCosts.Parts)
        {
            var recipe = MachineParts.Recipe(W, part);
            Assert.True(recipe.Enabled, part.Output);
            Assert.Equal((part.Nails, part.Plates, part.Rods),
                (MachineParts.Count(recipe, MachineParts.Nails), MachineParts.Count(recipe, MachineParts.Plate),
                    MachineParts.Count(recipe, MachineParts.Rod)));
            Assert.True(part.Nails >= 4 * part.ShippedNails && part.Nails >= 8, part.Output);
            foreach (string code in new[] { MachineParts.Nails, MachineParts.Plate, MachineParts.Rod })
                if (MachineParts.Ingredient(recipe, code) is { } metal)
                    Assert.Equal(WoodworkingMachineCosts.Metals, metal.AllowedVariants);
        }
    }

    // This mod's own frames: iron work of the same metals (the mill's and the rosser's own scenarios
    // check the rest of their recipes).
    [AtlasScenario]
    public void Machine_frames_take_iron_nails_too()
    {
        foreach (var frame in WoodworkingMachineCosts.Frames)
        {
            var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == frame.Output);
            Assert.Equal((frame.Nails, frame.SawmillFrames),
                (MachineParts.Count(recipe, MachineParts.Nails), MachineParts.Count(recipe, WoodworkingMachineCosts.ModId + ":sawmill-frame-north")));
            Assert.Equal(WoodworkingMachineCosts.Metals, MachineParts.Ingredient(recipe, MachineParts.Nails)!.AllowedVariants);
        }
    }

    // The saw sash on the grid: "NPN,PRP,NPN", 4 nails and strips to a corner, a rod in the middle.
    [AtlasScenario]
    public async Task Saw_sash_is_crafted_only_from_iron_work()
    {
        var player = (await World.JoinPlayer("sashwright")).Player;
        var recipe = MachineParts.Recipe(W, WoodworkingMachineCosts.Parts.Single(p => p.Output == "sawmillsash"));
        ItemSlot Slot(string code, int quantity = 1) => new DummySlot(new ItemStack(W.GetItem(new AssetLocation(code))!, quantity));
        bool Crafts(string nails, string rod, int perSlot) => recipe.Matches(player, W, "NPNPRPNPN".Select(c => c switch
        {
            'N' => Slot("game:metalnailsandstrips-" + nails, perSlot),
            'R' => Slot("game:rod-" + rod),
            _ => Slot("game:plank-oak"),
        }).ToArray(), 3);

        Assert.True(Crafts("iron", "iron", 4));
        Assert.True(Crafts("steel", "meteoriciron", 4));
        Assert.False(Crafts("iron", "iron", 3));
        Assert.False(Crafts("tinbronze", "iron", 4));
        Assert.False(Crafts("iron", "copper", 4));
    }

    [AtlasScenario]
    public void Machines_chapter_gives_the_iron_costs()
    {
        string text = Lang.GetL("en", "seraphhorizons:woodworking-machines-text");
        (int, int, int) Total(string machine)
        {
            var parts = WoodworkingMachineCosts.Parts.Where(p => p.Recipe.StartsWith(machine)).ToList();
            return (parts.Sum(p => p.Nails), parts.Sum(p => p.Plates), parts.Sum(p => p.Rods));
        }
        Assert.Equal((48, 1, 2), Total("sawmill"));
        Assert.Equal((24, 4, 1), Total("chopper"));
        Assert.Contains("must be iron, meteoric iron or steel", text);
        Assert.Contains("A sawmill takes 48 nails and strips, 1 plate and 2 rods in all, a chopper 24 nails and strips, 4 plates and 1 rod.", text);
        foreach (string item in new[] { "metalnailsandstrips-iron", "metalplate-iron", "rod-iron" })
        {
            Assert.Contains($"handbook://item-{item}\"", text);
            Assert.NotNull(W.GetItem(new AssetLocation("game", item)));
        }
    }
}
