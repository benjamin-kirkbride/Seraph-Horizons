using SeraphHorizons.Mod.Trading.Window.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Window;

public class HoldTimerTests
{
    [Fact]
    public void AHoldFillsOverItsSecondsAndFiresOnce()
    {
        var t = new HoldTimer(0.8);
        t.Press("buy:3");
        Assert.Equal(HoldState.Holding, t.State);
        Assert.False(t.Update(0.4));
        Assert.Equal(0.5, t.Progress, 3);
        Assert.True(t.Update(0.5));
        Assert.Equal(HoldState.Waiting, t.State);
        Assert.Equal(1, t.Progress);
        // Waiting: no second trade until the server answers.
        Assert.False(t.Update(0.9));
        Assert.Equal("buy:3", t.Target);
    }

    [Fact]
    public void LettingGoBeforeTheRingIsFullTradesNothing()
    {
        var t = new HoldTimer(0.8);
        t.Press("sell");
        Assert.False(t.Update(0.7));
        t.Release();
        Assert.Equal(HoldState.Idle, t.State);
        Assert.Equal(0, t.Progress);
        Assert.False(t.Update(1));
        Assert.Null(t.Target);
    }

    [Fact]
    public void HoldingOnAfterAConfirmedTradeStartsTheNextUnit()
    {
        var t = new HoldTimer(0.5);
        t.Press("sell");
        Assert.True(t.Update(0.5));
        t.Confirmed();
        Assert.Equal(HoldState.Holding, t.State);
        Assert.Equal(0, t.Progress);
        Assert.Equal(1, t.Completed);
        Assert.True(t.Update(0.6));
        t.Confirmed();
        Assert.Equal(2, t.Completed);
    }

    [Fact]
    public void ReleasedWhileWaitingTheAnswerStillCountsThenStops()
    {
        var t = new HoldTimer(0.5);
        t.Press("buy:0");
        Assert.True(t.Update(0.5));
        t.Release();
        Assert.Equal(HoldState.Waiting, t.State);
        t.Confirmed();
        Assert.Equal(HoldState.Idle, t.State);
    }

    [Fact]
    public void ARefusalBlocksUntilTheButtonIsLetGo()
    {
        var t = new HoldTimer(0.5);
        t.Press("buy:0");
        Assert.True(t.Update(0.5));
        t.Refused();
        Assert.Equal(HoldState.Blocked, t.State);
        Assert.False(t.Update(5));
        Assert.Equal(0, t.Progress);
        t.Release();
        Assert.Equal(HoldState.Idle, t.State);
        t.Press("buy:0");
        Assert.Equal(HoldState.Holding, t.State);
    }

    [Fact]
    public void NoAnswerInTimeCountsAsARefusal()
    {
        var t = new HoldTimer(0.5, waitSeconds: 2);
        t.Press("buy:0");
        Assert.True(t.Update(0.5));
        Assert.False(t.Update(1.9));
        Assert.Equal(HoldState.Waiting, t.State);
        Assert.False(t.Update(0.2));
        Assert.Equal(HoldState.Blocked, t.State);
    }

    [Fact]
    public void AnAnswerWithNothingSentIsIgnored()
    {
        var t = new HoldTimer();
        t.Confirmed();
        Assert.Equal(HoldState.Idle, t.State);
        t.Press("sell");
        t.Confirmed();
        Assert.Equal(HoldState.Holding, t.State);
        Assert.Equal(0, t.Completed);
    }

    [Fact]
    public void TheDefaultIsCarryOnsInteractDelay() => Assert.Equal(0.8, new HoldTimer().Seconds);
}

public class TradeGuardTests
{
    [Theory]
    [InlineData(true, "p1", "p1", 4.0, GuardRefusal.None)]
    [InlineData(true, "p1", "p1", 7.0, GuardRefusal.None)]
    [InlineData(true, "p1", "p1", 7.5, GuardRefusal.TooFar)]
    [InlineData(true, "p2", "p1", 1.0, GuardRefusal.NotTrading)]
    [InlineData(true, null, "p1", 1.0, GuardRefusal.NotTrading)]
    [InlineData(true, "", "p1", 1.0, GuardRefusal.NotTrading)]
    [InlineData(false, "p1", "p1", 1.0, GuardRefusal.Gone)]
    public void OnlyTheTradingPlayerNextToALiveTraderGetsThrough(bool alive, string? trading, string player, double distSq, GuardRefusal expected) =>
        Assert.Equal(expected, TradeGuard.Check(alive, trading, player, distSq));

    [Fact]
    public void EveryRefusalHasAKey()
    {
        foreach (var r in Enum.GetValues<GuardRefusal>().Where(r => r != GuardRefusal.None))
            Assert.StartsWith("trading-window-", TradeGuard.Key(r));
    }

    [Theory]
    // A map (stack limit 1) needs an empty slot.
    [InlineData(1, new int[0], false)]
    [InlineData(1, new[] { 0, 0, 0 }, false)]
    [InlineData(1, new[] { 0, 1, 0 }, true)]
    // Four bread: partial room merging into stacks counts, across slots.
    [InlineData(4, new[] { 3 }, false)]
    [InlineData(4, new[] { 3, 1 }, true)]
    [InlineData(4, new[] { 2, 0, 2 }, true)]
    [InlineData(4, new[] { 32 }, true)]
    // Nothing to give always fits; a negative figure is no room.
    [InlineData(0, new int[0], true)]
    [InlineData(2, new[] { -5, 1 }, false)]
    public void ABuyFitsWhenTheRoomLeftAddsUpToIt(int quantity, int[] room, bool fits) =>
        Assert.Equal(fits, TradeGuard.Fits(quantity, room));

    [Fact]
    public void NoRoomIsAWindowString() => Assert.StartsWith("trading-window-", TradeGuard.NoRoomKey);
}
