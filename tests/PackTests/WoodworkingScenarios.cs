using Atlas.XUnit;
using Vintagestory.API.Common;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The woodworking chain's scenarios, one partial file per feature: UnifiedWoodworking,
/// WoodworkingHandbook, ChopperOutput and BuckingSawmill (each <c>*Scenarios.cs</c>), on one server
/// boot (Atlas boots one per scenario class, about 30 s each in CI). The world is the plain one plus
/// BuckingSawmillSettings in ModConfig/seraphhorizons.json, seeded from fixtures/buckingsawmill: it
/// only shortens the bucking mill's cycle, and every switch keeps its default. A new woodworking
/// feature adds a partial file here instead of a class of its own.
/// <para>As in <see cref="SharedWorldScenarios"/>, every scenario shares the world with every other,
/// in no set order: its own offsets from <c>World.Spawn</c>, clear of the others' rooms and of where
/// their items land (the bucking mills sit 30 above the ground and their logs fall all the way
/// down, some after their scenario has finished), its own player names, nothing changed
/// world-wide, and <see cref="ReadsBootLogAttribute"/> on a scenario that reads the boot's log.</para>
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/buckingsawmill", TargetPath = "ModConfig")]
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public partial class WoodworkingScenarios : AtlasScenarioBase
{
    private readonly ITestOutputHelper output;

    public WoodworkingScenarios(ITestOutputHelper output) => this.output = output;

    private IWorldAccessor W => World.Api.World;
}
