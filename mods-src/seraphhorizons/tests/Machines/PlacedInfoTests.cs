using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests;

/// <summary>Taking a machine's description back out of its placed block info
/// (<see cref="PlacedInfo"/>), whatever line breaks the game put around it.</summary>
public class PlacedInfoTests
{
    private const string Desc = "The oak bed, iron ways and die stock of a chain draw bench.";

    [Fact]
    public void DescriptionOnItsOwnLineIsDropped() =>
        Assert.Equal("Next: fit a draw die\nOil: 4 of 100",
            PlacedInfo.WithoutDescription($"Next: fit a draw die\nOil: 4 of 100\n{Desc}", Desc));

    [Fact]
    public void LinesAfterItStay() =>
        Assert.Equal("Next: fit a draw die\nRequires tool tier 1",
            PlacedInfo.WithoutDescription($"Next: fit a draw die\n{Desc}\nRequires tool tier 1", Desc));

    [Fact]
    public void WindowsLineBreaksAreHandled() =>
        Assert.Equal("Next: fit a draw die",
            PlacedInfo.WithoutDescription($"Next: fit a draw die\r\n{Desc}\r\n", Desc));

    [Fact]
    public void TextSharingItsLineStays() =>
        Assert.Equal("Oil: 4 of 100",
            PlacedInfo.WithoutDescription($"Oil: 4 of 100{Desc}", Desc));

    [Fact]
    public void OnlyTheDescriptionIsTheInfo() =>
        Assert.Equal("", PlacedInfo.WithoutDescription(Desc, Desc));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("blockdesc-drawbench-frame-north")]
    public void NoDescriptionLeavesTheInfo(string? desc) =>
        Assert.Equal("Next: fit a draw die\nOil: 4 of 100",
            PlacedInfo.WithoutDescription("Next: fit a draw die\nOil: 4 of 100\n", desc));
}
