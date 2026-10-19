using Atlas.XUnit;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The ore machines (the mill) on the pack's plain world (<c>[AtlasWorld]</c>, no data files), one
/// partial file per machine: OreMachines (their shared code), JawBreaker, StampBattery, BallMill,
/// Arrastra, Riddle, Rocker, Sluice, Jig, Vanner, ShakingTable, AmalgamPan, AmalgamationPlates,
/// StallRoaster, Reverberatory, CupellationFurnace, LiquationFurnace, ParkesKettle and AcidParting
/// (each <c>*Scenarios.cs</c>). They were parts of <see cref="SharedWorldScenarios"/>, on the same
/// world, and are a class apart only for time: the two on one server ran past a CI shard's limit
/// (ci/atlas-shards.md). So the rules are that class's: a new ore machine that needs only the plain
/// world adds a partial file here, at its own offsets from <c>World.Spawn</c>, under player names of
/// its own (or the shared gearcutterhand, <see cref="PlainWorldScenarioBase.CutterHand"/>), changing
/// nothing world-wide; what it checks in the recipe export goes in <see cref="RecipeExportScenarios"/>.
/// </summary>
[AtlasWorld]
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public partial class MillWorldScenarios : PlainWorldScenarioBase
{
}
