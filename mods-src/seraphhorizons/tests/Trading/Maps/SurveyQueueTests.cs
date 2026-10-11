using SeraphHorizons.Mod.Trading.Maps.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Maps;

/// <summary>#693: background deposit checks run one at a time with a pause between; urgent ones
/// (a trade window opened on a deposit still being surveyed) go first and at once.</summary>
public class SurveyQueueTests
{
    [Fact]
    public void BackgroundChecksRunOneAtATimeWithAPauseBetween()
    {
        var q = new SurveyQueue(5);
        Assert.True(q.Want("copper:1,1", false));
        Assert.True(q.Want("iron:1,1", false));
        Assert.False(q.Want("copper:1,1", false));
        Assert.Equal("copper:1,1", q.Next(0));
        Assert.Null(q.Next(1)); // one at a time
        Assert.False(q.Want("copper:1,1", false)); // running
        q.Done("copper:1,1", 10);
        Assert.Null(q.Next(12)); // the pause
        Assert.Equal("iron:1,1", q.Next(15));
        q.Done("iron:1,1", 20);
        Assert.Equal(0, q.Waiting);
        Assert.Equal(0, q.Running);
    }

    [Fact]
    public void ACheckThatFoundNothingToDoStartsNoPause()
    {
        var q = new SurveyQueue(5);
        q.Want("a:0,0", false);
        q.Want("b:0,0", false);
        Assert.Equal("a:0,0", q.Next(0));
        q.Done("a:0,0", 1, worked: false);
        Assert.Equal("b:0,0", q.Next(1));
    }

    [Fact]
    public void UrgentChecksGoFirstAndAtOnceBesideABackgroundOne()
    {
        var q = new SurveyQueue(60);
        q.Want("a:0,0", false);
        q.Want("b:0,0", false);
        Assert.Equal("a:0,0", q.Next(0));
        // b moves up; it runs though a is running and the pause would hold it.
        Assert.True(q.Want("b:0,0", true));
        Assert.False(q.Want("b:0,0", true));
        Assert.True(q.Want("c:0,0", true));
        Assert.Equal("b:0,0", q.Next(0));
        Assert.Equal("c:0,0", q.Next(0));
        Assert.Null(q.Next(0));
        Assert.Equal(3, q.Running);
        // An urgent check ending doesn't start the background's pause.
        q.Done("b:0,0", 1);
        q.Done("c:0,0", 1);
        q.Done("a:0,0", 2);
        q.Want("d:0,0", false);
        Assert.Null(q.Next(30));
        Assert.Equal("d:0,0", q.Next(62));
    }

    [Fact]
    public void ANegativePauseIsNone() => Assert.Equal(0, new SurveyQueue(-3).PauseSeconds);
}
