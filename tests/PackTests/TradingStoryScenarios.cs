using Atlas.XUnit;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Trading/Game/TraderCamps.cs: the grid's spawner rewrite leaves the
/// traders of story structures alone. A class of its own: the story locations need a survival world
/// (Atlas' default creative play style turns lore content off, and with it the story structures),
/// standard, so the grid is on. The locations are decided at worldgen init; nothing is generated.
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard", PlayStyle = "surviveandbuild")]
public class TradingStoryScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    public const int Seed = 436447448;

    private ICoreServerAPI Api => World.Api;
    private IWorldAccessor W => World.Api.World;
    private TradingSystem Trading => TradingSystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no TradingSystem");

    [AtlasScenario]
    public void A_trader_spawner_in_a_story_structure_keeps_its_story_npc()
    {
        // Vanilla's treasure hunter: a meta-spawner of trader-{male,female}-treasurehunter-temperate
        // in its house. Rewritten into the cell's type, it lost its story dialogue (and, as a
        // prospector, sold a map to itself).
        var story = Api.ModLoader.GetModSystem<GenStoryStructures>();
        output.WriteLine("story locations: " + string.Join(", ", story.Structures.Select(s => $"{s.Key} {s.Value.Location}")));
        Assert.True(Trading.GridActive, Trading.GridOffReason);
        var hunter = story.Structures.Get("treasurehunter");
        Assert.True(hunter != null, "the world has no treasure hunter location");
        var area = hunter!.Location;
        var inside = new BlockPos(area.CenterX, area.Y1 + 2, area.CenterZ);
        const string code = "game:trader-male-treasurehunter-temperate";
        Assert.NotNull(W.GetEntityType(new AssetLocation(code)));
        var tree = new TreeAttribute();
        tree.SetString("type", code);
        tree.SetBlockPos("pos", inside);
        Api.Event.PushEvent("onattemptspawnerspawn", tree);
        Assert.Equal(code, tree.GetString("type"));
        // Its corner too: the schematic's whole area.
        var corner = new TreeAttribute();
        corner.SetString("type", code);
        corner.SetBlockPos("pos", new BlockPos(area.X1, area.Y1, area.Z1));
        Api.Event.PushEvent("onattemptspawnerspawn", corner);
        Assert.Equal(code, corner.GetString("type"));

        // Outside the house but within its landform radius (200 blocks), where the grid may place a
        // camp (structures keep only 100 blocks off): a camp's spawner there is still the cell's.
        var near = new BlockPos(area.CenterX + 150, area.Y1, area.CenterZ);
        Assert.DoesNotContain(story.Structures, s => s.Value.Location.Contains(near.X, near.Z));
        var camp = new TreeAttribute();
        camp.SetString("type", "game:trader-female-agriculture-temperate");
        camp.SetBlockPos("pos", near);
        Api.Event.PushEvent("onattemptspawnerspawn", camp);
        string type = Trading.Grid!.TypeOf(TraderGrid.CellOf(near.X, near.Z));
        Assert.Matches($"^seraphhorizons:trader-female-{type}-(cold|temperate|desert)$", camp.GetString("type"));
    }
}
