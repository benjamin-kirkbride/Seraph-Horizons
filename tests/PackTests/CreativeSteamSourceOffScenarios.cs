using Atlas.XUnit;
using SeraphHorizons.SeraphTweaks;
using Vintagestory.API.Common;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphtweaks, the creative steam source with its switch off
/// (<c>"CreativeSteamSource": false</c> in ModConfig/seraphtweaks.json, seeded from
/// fixtures/creativesteamsource-off before the server boots): its blocktype is disabled before the
/// game loads blocktypes, so the block does not exist. Its own server: a class boots a fresh one.
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/creativesteamsource-off", TargetPath = "ModConfig")]
public class CreativeSteamSourceOffScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public void Switched_off_the_block_does_not_exist()
    {
        var config = World.Api.LoadModConfig("seraphtweaks.json");
        Assert.False(config["CreativeSteamSource"].AsBool(true));
        Assert.True(config["BoilerLidBlowsOpen"].AsBool(false));
        Assert.Null(World.Api.World.GetBlock(new AssetLocation("seraphtweaks", CreativeSteamSource.BlockCode)));
        Assert.DoesNotContain(World.Api.World.Blocks, b => b.Code?.Domain == "seraphtweaks");
    }
}
