using Atlas.XUnit;
using Vintagestory.API.Common;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The scenarios of the pack's game-bug fix mods (mods-src/allowedvariantsfix, chiselrotationfix,
/// constructionhelpfix), one partial file per fix mod: AllowedVariantsFix, ChiselRotationFix and
/// ConstructionHelpFix (each <c>*FixScenarios.cs</c>), on one server boot of the pack's plain world
/// (Atlas boots one per scenario class, 45-55 s each in CI). A new fix mod adds a partial file of this
/// class, not a class of its own. The CI shard <c>fixes</c> selects this class by
/// <c>FullyQualifiedName~FixScenarios.</c>, so the class name must keep ending in <c>FixScenarios</c>.
/// <para>As in <see cref="SharedWorldScenarios"/>, every scenario shares the world with every other,
/// in no set order: its own offsets from <c>World.Spawn</c> (ChiselRotationFix places its ruin pairs
/// at (-20, 30, -20) and every 20 blocks up from there; the others build nothing), its own player
/// names, nothing changed world-wide (or put back), and <see cref="ReadsBootLogAttribute"/> on a
/// scenario that reads the boot's log.</para>
/// </summary>
[AtlasWorld]
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public partial class PackFixScenarios : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
}
