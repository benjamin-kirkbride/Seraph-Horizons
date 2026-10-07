using Atlas.XUnit;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The rosser's scenarios (<c>RosserScenarios.cs</c>, <c>DebarkedTrunkScenarios.cs</c>): the
/// second woodworking class and CI shard, on the same world and ModConfig as
/// <see cref="WoodworkingScenarios"/> (whose summary has the rules both follow), on a server of its
/// own.
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/buckingsawmill", TargetPath = "ModConfig")]
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public partial class WoodworkingRosserScenarios : WoodworkingScenarioBase
{
    public WoodworkingRosserScenarios(ITestOutputHelper output) : base(output) { }
}
