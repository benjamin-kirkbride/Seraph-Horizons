using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class InfoTextRoundingTests
{
    // Vanilla 1.22.7's health line (EntityBehaviorHealth.GetInfoText, lang key "Health: {0}/{1}").
    private const string Health = "Health: {0}/{1}";
    private static readonly InfoTextRounding English = new([Health]);

    [Theory]
    [InlineData("Health: 13.4567/20", "Health: 13/20")]
    [InlineData("Health: 13.5/20", "Health: 14/20")]
    [InlineData("Health: 0.4/12.75", "Health: 0/13")]
    [InlineData("Health: 20/20", "Health: 20/20")]
    [InlineData("Health: 1E-05/20", "Health: 0/20")]
    [InlineData("<font color=\"#ff8888\">Health: 7.25/10</font>", "<font color=\"#ff8888\">Health: 7/10</font>")]
    public void HealthLinesRoundToWholeNumbers(string line, string expected) =>
        Assert.Equal(expected, English.Round(line));

    [Theory]
    [InlineData("Temperature: 1234.5678°C", "Temperature: 1234.6°C")]
    [InlineData("Contents: 3.14159 litres", "Contents: 3.1 litres")]
    [InlineData("Speed 0.05", "Speed 0.1")]
    [InlineData("Speed 0.04", "Speed 0.0")]
    [InlineData("Ratio -0.04", "Ratio 0.0")]
    [InlineData("Ratio -1.25", "Ratio -1.3")]
    [InlineData("Pressure: 2.999 atm", "Pressure: 3.0 atm")]
    [InlineData("Max Well Volume: 12.345 liters", "Max Well Volume: 12.3 liters")]
    [InlineData("Width 0.75m", "Width 0.8m")]
    [InlineData("Wear 12.345%", "Wear 12.3%")]
    [InlineData("1,234.567 units", "1,234.6 units")]
    [InlineData("Ranges 1.25-2.75", "Ranges 1.3-2.8")]
    [InlineData("Tiny 1.5E-05", "Tiny 0.0")]
    [InlineData("(13.45)", "(13.5)")]
    public void OtherNumbersRoundToOneDecimal(string line, string expected) =>
        Assert.Equal(expected, English.Round(line));

    [Theory]
    [InlineData("Generation: 3")]
    [InlineData("Stack 64, 12 left")]
    [InlineData("Temperature: 20.5°C")]
    [InlineData("Game version 1.22.7")]
    [InlineData("Server 192.168.1.20")]
    [InlineData("Code: game:plank-1.25")]
    [InlineData("Code: item_v1.255")]
    [InlineData("Id:12345")]
    [InlineData("Code abc1.25")]
    [InlineData("Nothing numeric here")]
    [InlineData("")]
    public void LeavesIntegersAndNonQuantitiesAlone(string line) =>
        Assert.Equal(line, English.Round(line));

    [Fact]
    public void NullStaysNull() => Assert.Null(English.Round(null));

    [Fact]
    public void TagAttributesAreNotRewritten()
    {
        const string line = "<font size=\"0.875\" lineheight=\"1.25\">Fill 0.875</font>";
        Assert.Equal("<font size=\"0.875\" lineheight=\"1.25\">Fill 0.9</font>", English.Round(line));
    }

    [Fact]
    public void EachLineIsJudgedOnItsOwn()
    {
        string text = "Health: 13.4567/20\nTemperature: 36.666°C\nHealth: 2.5/20 extra\n";
        Assert.Equal("Health: 13/20\nTemperature: 36.7°C\nHealth: 2.5/20 extra\n", English.Round(text));
    }

    [Fact]
    public void WindowsLineEndingsStillMatchTheHealthLine()
    {
        string text = "Health: 13.4567/20\r\nAge: 1.234\r\n";
        Assert.Equal("Health: 13/20\r\nAge: 1.2\r\n", English.Round(text));
    }

    [Fact]
    public void TheHealthLabelIsTheLocalizedOne()
    {
        var german = new InfoTextRounding(["Gesundheit: {0}/{1}"], ",");
        Assert.Equal("Gesundheit: 13/20", german.Round("Gesundheit: 13,4567/20"));
        // The English label is not a health line in that language, and the culture's separator is kept.
        Assert.Equal("Health: 13,5/20", german.Round("Health: 13,4567/20"));
        Assert.Equal("Temperatur: 36,7°C", german.Round("Temperatur: 36,666°C"));
    }

    [Fact]
    public void CommaIsNotADecimalSeparatorUnlessTheCultureSaysSo()
    {
        Assert.Equal("Items 3,456", English.Round("Items 3,456"));
        var german = new InfoTextRounding([Health], ",");
        // A German thousands group and decimal (1.234,56) is a dotted run: left alone.
        Assert.Equal("Mass 1.234,56 kg", german.Round("Mass 1.234,56 kg"));
    }

    [Fact]
    public void TemplatesWithoutAPlaceholderOrBlankAreIgnored()
    {
        var rounding = new InfoTextRounding(["", null, "Health", Health, Health]);
        Assert.True(rounding.IsHealthLine("Health: 1.5/2"));
        Assert.False(rounding.IsHealthLine("Health"));
    }

    [Fact]
    public void FormatSpecifiersInATemplateArePlaceholders()
    {
        var rounding = new InfoTextRounding(["Health: {0:0.##}/{1:0.##}"]);
        Assert.Equal("Health: 13/20", rounding.Round("Health: 13.46/20"));
    }

    [Fact]
    public void TemplateTextIsLiteral()
    {
        var rounding = new InfoTextRounding(["HP (x.y): {0}/{1}"]);
        Assert.True(rounding.IsHealthLine("HP (x.y): 1.5/2"));
        Assert.False(rounding.IsHealthLine("HP (xay): 1.5/2"));
    }
}
