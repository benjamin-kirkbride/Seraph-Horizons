using Atlas.XUnit;
using Vintagestory.API.Common;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The scenarios that need nothing but the pack's plain world (<c>[AtlasWorld]</c>, no data files),
/// one partial file per feature: PackLoad, BetterRuins, HydrationCoverage, SeraphHorizonsMod, Tun,
/// IrrigationVessel, BarrelRackKegs, AgeOfFlaxRebalance, CreativeModTabs, MapReveal, GearboxSourceRatio (each
/// <c>*Scenarios.cs</c>). Atlas boots one server per scenario class, about 30 s each in CI, so these
/// share a single boot. A new feature that needs only the plain world adds a partial file of this
/// class instead of a class of its own; a class of its own is for a different world (a play style,
/// ModConfig fixtures: <see cref="ClearCommandScenarios"/>, <see cref="SwitchesOffScenarios"/>).
/// <para>Every scenario shares the world with every other, in no set order, so each one: builds at
/// its own offsets from <c>World.Spawn</c> (pick ones no file here uses, keeping clear of their
/// rooms and of where their items fall); joins players under names of its own (16 at most per
/// server); changes nothing world-wide (time, weather, game rules) or puts it back; and, if it reads
/// <c>World.BootDiagnostics</c> for the boot, is marked <see cref="ReadsBootLogAttribute"/>.</para>
/// </summary>
[AtlasWorld]
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public partial class SharedWorldScenarios : AtlasScenarioBase
{
    private readonly ITestOutputHelper output;

    public SharedWorldScenarios(ITestOutputHelper output) => this.output = output;

    private IWorldAccessor W => World.Api.World;
}
