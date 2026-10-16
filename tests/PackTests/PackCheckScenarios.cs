using Atlas.XUnit;
using SeraphHorizons.Mod.PackCheck;
using SeraphHorizons.Mod.PackCheck.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.PackTests;

/// <summary>mods-src/seraphhorizons' <c>PackVersionCheck</c> on a server with every locked mod at
/// its locked version and this tree's build of the mod, whose embedded lock is this tree's: the one
/// differences are Atlas's own mod, <c>atlasbridge</c>, which the pack does not have (the check has
/// no allow-list), and any separate mods-src mod the scenarios load from this tree's build that is
/// not pinned yet (it is pinned only after its ModDB upload, which follows its merge), each logged
/// as a warning. The off check is in <see cref="SwitchesOffScenarios"/>.</summary>
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
        var notInPack = UnpinnedModsSrcMods().Append(AtlasOwnMod).Order(StringComparer.Ordinal).ToList();
        Assert.True(check.Findings.All(f => f.Kind == FindingKind.NotInPack)
                    && check.Findings.Select(f => f.Subject).Order(StringComparer.Ordinal).SequenceEqual(notInPack),
            $"Expected only these as not in the pack: {string.Join(", ", notInPack)}. Findings:\n" + findings);

        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("Pack version check", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 1 + check.Findings.Count && logged.All(l => l.StartsWith("[Warning]"))
                    && check.Findings.All(f => logged.Skip(1).Any(l => l.EndsWith(f.Describe(), StringComparison.Ordinal))),
            "Logged:\n" + string.Join("\n", logged));
    }

    /// <summary>The modids of mods-src/ mods other than the pack's own that pack/lock.json does not
    /// have yet. PackTests.csproj loads every one of them from this tree's build.</summary>
    private static IEnumerable<string> UnpinnedModsSrcMods()
    {
        var dir = Path.GetDirectoryName(typeof(SharedWorldScenarios).Assembly.Location)!;
        var root = new DirectoryInfo(dir);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "pack", "pack.toml")))
            root = root.Parent;
        if (root == null)
            throw new Xunit.Sdk.XunitException("the repository root (pack/pack.toml) is not above " + dir);
        var locked = PackLock.Mods.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.GetDirectories(Path.Combine(root.FullName, "mods-src"))
            .Select(d => Path.Combine(d, "modinfo.json"))
            .Where(File.Exists)
            .Select(f => System.Text.Json.JsonDocument.Parse(File.ReadAllText(f)).RootElement.GetProperty("modid").GetString()!)
            .Where(id => id != "seraphhorizons" && !locked.Contains(id))
            .ToList();
    }
}
