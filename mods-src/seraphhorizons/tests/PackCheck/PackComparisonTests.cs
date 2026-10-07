using SeraphHorizons.Mod.PackCheck.Core;

namespace SeraphHorizons.Mod.Tests.PackCheck;

public class PackComparisonTests
{
    private static readonly PackLock Pack = new("seraphhorizons", "1.1.0", "1.22.7",
    [
        new LockedMod("exlib", "1.2.0", "universal"),
        new LockedMod("ServerOnly", "0.3.0", "server"),
        new LockedMod("clientonly", "2.0.0", "client"),
    ]);

    private static List<Finding> Compare(CheckSide side, params (string Id, string Version)[] loaded) =>
        Compare(side, "1.22.7", "1.1.0", loaded);

    private static List<Finding> Compare(CheckSide side, string game, string own, params (string Id, string Version)[] loaded) =>
        PackComparison.Compare(Pack, loaded.Select(m => new LoadedMod(m.Id, m.Version)), side, game, "seraphhorizons", own);

    private static readonly (string, string)[] AsShippedOnServer =
        [("game", "1.22.7"), ("creative", "1.22.7"), ("survival", "1.22.7"), ("seraphhorizons", "1.1.0"),
         ("exlib", "1.2.0"), ("serveronly", "0.3.0")];

    [Fact]
    public void AnInstallAsReleasedHasNoFindings()
    {
        Assert.Empty(Compare(CheckSide.Server, AsShippedOnServer));
        Assert.Empty(Compare(CheckSide.Client,
            ("game", "1.22.7"), ("creative", "1.22.7"), ("survival", "1.22.7"), ("seraphhorizons", "1.1.0"),
            ("exlib", "1.2.0"), ("clientonly", "2.0.0"), ("seraphhorizonspack", "1.1.0")));
    }

    [Fact]
    public void ALockedModAtAnotherVersionHigherOrLower()
    {
        Assert.Equal([new Finding(FindingKind.WrongVersion, "exlib", "1.2.0", "1.3.0")],
            Compare(CheckSide.Server, ("exlib", "1.3.0"), ("serveronly", "0.3.0")));
        Assert.Equal([new Finding(FindingKind.WrongVersion, "exlib", "1.2.0", "1.1.9")],
            Compare(CheckSide.Server, ("exlib", "1.1.9"), ("serveronly", "0.3.0")));
        // Exactly as pinned: a suffix is another version.
        Assert.Equal([new Finding(FindingKind.WrongVersion, "exlib", "1.2.0", "1.2.0-dev")],
            Compare(CheckSide.Server, ("exlib", "1.2.0-dev"), ("serveronly", "0.3.0")));
    }

    [Fact]
    public void AMissingModIsOnlyExpectedOnASideItLoadsOn()
    {
        Assert.Equal([new Finding(FindingKind.Missing, "ServerOnly", "0.3.0", null)],
            Compare(CheckSide.Server, ("exlib", "1.2.0")));
        Assert.Equal([new Finding(FindingKind.Missing, "clientonly", "2.0.0", null)],
            Compare(CheckSide.Client, ("exlib", "1.2.0")));
        Assert.Equal(
            [new Finding(FindingKind.Missing, "exlib", "1.2.0", null), new Finding(FindingKind.Missing, "ServerOnly", "0.3.0", null)],
            Compare(CheckSide.Server));
    }

    [Fact]
    public void AModOfTheOtherSideThatIsLoadedAnywayIsCheckedForItsVersion()
    {
        Assert.Equal([new Finding(FindingKind.WrongVersion, "clientonly", "2.0.0", "1.0.0")],
            Compare(CheckSide.Server, [.. AsShippedOnServer, ("clientonly", "1.0.0")]));
        Assert.Empty(Compare(CheckSide.Server, [.. AsShippedOnServer, ("clientonly", "2.0.0")]));
    }

    [Fact]
    public void AModThePackDoesNotHave()
    {
        Assert.Equal([new Finding(FindingKind.NotInPack, "seraphexport", null, "1.0.0")],
            Compare(CheckSide.Server, [.. AsShippedOnServer, ("seraphexport", "1.0.0")]));
    }

    [Fact]
    public void TheGamesOwnModsTheMetaModAndThisModAreNeverExtras()
    {
        Assert.Empty(Compare(CheckSide.Server,
            [.. AsShippedOnServer, ("Game", "1.22.7"), ("SeraphHorizonsPack", "1.1.0")]));
        Assert.True(PackComparison.BuiltIn.SetEquals(["game", "creative", "survival"]));
    }

    [Fact]
    public void ModIdsCompareWithoutCase()
    {
        Assert.Empty(Compare(CheckSide.Server, ("EXLIB", "1.2.0"), ("serveronly", "0.3.0")));
    }

    [Fact]
    public void AnotherGameVersion()
    {
        Assert.Equal([new Finding(FindingKind.GameVersion, "game", "1.22.7", "1.22.8")],
            Compare(CheckSide.Server, "1.22.8", "1.1.0", AsShippedOnServer));
    }

    [Fact]
    public void ThisModAtAnotherVersionThanThePack()
    {
        Assert.Equal([new Finding(FindingKind.OwnVersion, "seraphhorizons", "1.1.0", "1.0.0")],
            Compare(CheckSide.Server, "1.22.7", "1.0.0", AsShippedOnServer));
    }

    [Fact]
    public void FindingsAreSortedByKindThenModid()
    {
        var found = Compare(CheckSide.Server, "1.22.8", "1.0.0",
            ("zeta", "1"), ("Alpha", "1"), ("exlib", "9.9.9"), ("beta", "2"));
        Assert.Equal(
        [
            new Finding(FindingKind.GameVersion, "game", "1.22.7", "1.22.8"),
            new Finding(FindingKind.OwnVersion, "seraphhorizons", "1.1.0", "1.0.0"),
            new Finding(FindingKind.WrongVersion, "exlib", "1.2.0", "9.9.9"),
            new Finding(FindingKind.Missing, "ServerOnly", "0.3.0", null),
            new Finding(FindingKind.NotInPack, "Alpha", null, "1"),
            new Finding(FindingKind.NotInPack, "beta", null, "2"),
            new Finding(FindingKind.NotInPack, "zeta", null, "1"),
        ], found);
    }

    [Fact]
    public void TheFingerprintIgnoresOrderAndChangesWithTheFindings()
    {
        var a = new Finding(FindingKind.NotInPack, "beta", null, "2");
        var b = new Finding(FindingKind.WrongVersion, "exlib", "1.2.0", "9.9.9");
        var fp = PackComparison.Fingerprint([a, b]);
        Assert.Matches("^[0-9a-f]{64}$", fp);
        Assert.Equal(fp, PackComparison.Fingerprint([b, a]));
        // Stable across runs and builds: a hash of the findings alone.
        Assert.Equal(fp, PackComparison.Fingerprint([new Finding(FindingKind.NotInPack, "Beta", null, "2"), b]));
        Assert.NotEqual(fp, PackComparison.Fingerprint([a]));
        Assert.NotEqual(fp, PackComparison.Fingerprint([a, b with { Found = "9.9.10" }]));
        Assert.Equal("", PackComparison.Fingerprint([]));
    }

    [Fact]
    public void DescribeSaysWhatWasExpectedAndFound()
    {
        Assert.Equal("exlib: expected 1.2.0, found 1.3.0", new Finding(FindingKind.WrongVersion, "exlib", "1.2.0", "1.3.0").Describe());
        Assert.Equal("exlib: expected 1.2.0, missing", new Finding(FindingKind.Missing, "exlib", "1.2.0", null).Describe());
        Assert.Equal("extra 1.0: not in the pack", new Finding(FindingKind.NotInPack, "extra", null, "1.0").Describe());
        Assert.Equal("game: expected 1.22.7, found 1.22.8", new Finding(FindingKind.GameVersion, "game", "1.22.7", "1.22.8").Describe());
    }

    [Fact]
    public void ReadsTheRealLock()
    {
        var lockJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "pack-lock.json"));
        var pack = PackComparison.ParseLock(lockJson);
        Assert.Equal("seraphhorizons", pack.PackId);
        Assert.Matches(@"^\d+\.\d+\.\d+$", pack.PackVersion);
        Assert.Matches(@"^\d+\.\d+\.\d+", pack.GameVersion);
        Assert.True(pack.Mods.Count > 100, $"{pack.Mods.Count} mods");
        Assert.All(pack.Mods, m => Assert.Contains(m.Side, new[] { "universal", "server", "client" }));
        // The pack's own mod is released with the pack, never locked.
        Assert.DoesNotContain(pack.Mods, m => m.Id.Equals("seraphhorizons", StringComparison.OrdinalIgnoreCase));
        // As released, with the game's own mods and the meta-mod: nothing to report.
        var asReleased = pack.Mods.Where(m => PackComparison.LoadsOn(m.Side, CheckSide.Server))
            .Select(m => new LoadedMod(m.Id, m.Version))
            .Concat([new LoadedMod("game", pack.GameVersion), new LoadedMod("seraphhorizons", pack.PackVersion)]);
        Assert.Empty(PackComparison.Compare(pack, asReleased, CheckSide.Server, pack.GameVersion, "seraphhorizons", pack.PackVersion));
    }

    [Fact]
    public void RefusesSomethingThatIsNotALock()
    {
        Assert.Throws<FormatException>(() => PackComparison.ParseLock("{}"));
        Assert.Throws<FormatException>(() => PackComparison.ParseLock("not json"));
        Assert.Throws<FormatException>(() => PackComparison.ParseLock("""{"pack": {"id": "x", "version": "1", "game_version": ""}, "mods": []}"""));
    }
}
