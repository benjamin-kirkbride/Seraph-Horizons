using Atlas.XUnit;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Grade pockets (#944, <see cref="GradePocketsScenarioBase"/>) in zinc's, lead's and bismuth's cells:
/// twelve deposits, each fitting one drawn grade by pocket in every column. None reaches 5,000 blocks
/// on this seed, so the "no longer one grade" check is <see cref="GradePocketsCopperIronTinScenarios"/>'.
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard")]
public class GradePocketsZincLeadBismuthScenarios(ITestOutputHelper output) : GradePocketsScenarioBase(output)
{
    protected override string[] Metals => ["zinc", "lead", "bismuth"];

    protected override bool RequiresVaried => false;

    [AtlasScenario]
    public void Grade_pockets_are_bound_and_recorded_for_the_new_world() => BoundAndRecorded();

    [AtlasScenario(TimeoutMs = 900_000)]
    public Task Generated_deposits_vary_by_pocket_around_one_drawn_grade() => DepositsVaryByPocketAroundOneDrawnGrade();
}
