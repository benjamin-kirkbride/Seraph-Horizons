using Atlas.XUnit;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Grade pockets (#944, <see cref="GradePocketsScenarioBase"/>) in copper's, iron's and tin's cells:
/// twelve deposits, the four of 5,000 blocks or more among them (copper 100,100, iron 104,100, tin
/// 104,100 and 100,100 on this seed) carrying the "no longer one grade" check, which needs three.
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard")]
public class GradePocketsCopperIronTinScenarios(ITestOutputHelper output) : GradePocketsScenarioBase(output)
{
    protected override string[] Metals => ["copper", "iron", "tin"];

    protected override bool RequiresVaried => true;

    [AtlasScenario]
    public void Grade_pockets_are_bound_and_recorded_for_the_new_world() => BoundAndRecorded();

    [AtlasScenario(TimeoutMs = 900_000)]
    public Task Generated_deposits_vary_by_pocket_around_one_drawn_grade() => DepositsVaryByPocketAroundOneDrawnGrade();
}
