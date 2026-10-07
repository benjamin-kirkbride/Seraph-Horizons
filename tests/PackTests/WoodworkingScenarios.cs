using Atlas.XUnit;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The woodworking chain's scenarios, one partial file per feature, in two classes, each a server
/// boot of its own (about 30 s each in CI) and a CI shard of its own:
/// <list type="bullet">
/// <item><see cref="WoodworkingScenarios"/>: UnifiedWoodworking, WoodworkingHandbook,
/// ChopperOutput, FellingWear, HeatingRack, TrunkStation, MillFeeder, MachineOilMill and
/// BuckingSawmill (each <c>*Scenarios.cs</c>).</item>
/// <item><see cref="WoodworkingRosserScenarios"/>: Rosser and DebarkedTrunk, the rosser and what
/// it makes.</item>
/// </list>
/// The world is the plain one plus BuckingSawmillSettings and RosserSettings in
/// ModConfig/seraphhorizons.json, seeded from fixtures/buckingsawmill: they only shorten the bucking
/// mill's cycle and the rosser's trip, and every switch keeps its default. Helpers both classes use
/// are in <see cref="WoodworkingScenarioBase"/>. A new woodworking feature adds a partial file to
/// one of the two (the rosser's to <see cref="WoodworkingRosserScenarios"/>, the rest here), not a
/// class of its own, unless a class grows past the CI shard's time.
/// <para>As in <see cref="SharedWorldScenarios"/>, every scenario of a class shares the world with
/// every other, in no set order: its own offsets from <c>World.Spawn</c>, clear of the others'
/// rooms and of where their items land (the bucking mills sit 30 above the ground and their logs
/// fall all the way down, some after their scenario has finished), its own player names, nothing
/// changed world-wide, and <see cref="ReadsBootLogAttribute"/> on a scenario that reads the boot's
/// log. The two classes' worlds are separate, but keep their scenarios' places apart all the same,
/// so a partial file can move between them.</para>
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/buckingsawmill", TargetPath = "ModConfig")]
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public partial class WoodworkingScenarios : WoodworkingScenarioBase
{
    public WoodworkingScenarios(ITestOutputHelper output) : base(output) { }
}
