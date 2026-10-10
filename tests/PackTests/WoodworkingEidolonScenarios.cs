using Atlas.XUnit;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The eidolon's woodworking scenarios (<c>EidolonHaulScenarios.cs</c>, <c>EidolonCrewScenarios.cs</c>):
/// a woodworking class and CI shard of their own, on the same world and ModConfig as
/// <see cref="WoodworkingScenarios"/> (whose summary has the rules both follow), on a server of its
/// own. They run the rosser in wall-clock time for a minute or more, which would take the
/// woodworking shard over its five minutes.
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/buckingsawmill", TargetPath = "ModConfig")]
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public partial class WoodworkingEidolonScenarios : WoodworkingScenarioBase
{
    public WoodworkingEidolonScenarios(ITestOutputHelper output) : base(output) { }
}
