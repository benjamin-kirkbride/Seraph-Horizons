using Atlas.XUnit;
using SeraphHorizons.Mod.PackCheck;
using SeraphHorizons.Mod.PackCheck.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.PackTests;

/// <summary>mods-src/seraphhorizons' <c>PackVersionCheck</c> on a server with every locked mod at
/// its locked version and this tree's build of the mod, whose embedded lock is this tree's: the one
/// difference is Atlas's own mod, <c>atlasbridge</c>, which the pack does not have (the check has
/// no allow-list), logged as a warning. The off check is in <see cref="SwitchesOffScenarios"/>.</summary>
public partial class SharedWorldScenarios
{
    private const string AtlasOwnMod = "atlasbridge";

    [AtlasScenario, ReadsBootLog]
    public void The_pack_version_check_finds_the_locked_pack_as_locked()
    {
        var check = World.Api.ModLoader.GetModSystem<PackCheckSystem>();
        Assert.NotNull(check);
        Assert.NotNull(check.Pack);
        Assert.Equal(PackLock.Mods.Count(), check.Pack!.Mods.Count);
        var findings = string.Join("\n", check.Findings.Select(f => f.Describe()));
        var finding = Assert.Single(check.Findings);
        Assert.True(finding is { Kind: FindingKind.NotInPack, Subject: AtlasOwnMod }, "Findings:\n" + findings);

        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("Pack version check", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 2 && logged.All(l => l.StartsWith("[Warning]"))
                    && logged[1].EndsWith(finding.Describe(), StringComparison.Ordinal),
            "Logged:\n" + string.Join("\n", logged));
    }
}
