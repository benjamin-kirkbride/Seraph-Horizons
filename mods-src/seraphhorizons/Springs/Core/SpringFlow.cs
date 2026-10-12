namespace SeraphHorizons.Mod.Springs.Core;

/// <summary>What happened to a spring's taps since it last looked.</summary>
public enum TapChange
{
    None,

    /// <summary>A pipe couples to it now and none did: it stops pouring and feeds the pipes.</summary>
    Tapped,

    /// <summary>No pipe couples to it any more: it pours again.</summary>
    Untapped,
}

/// <summary>
/// A tapped spring's water (README "Springs"): endless, but no more than its rate. Each second the
/// spring offers the seconds gone by times its rate (at most <see cref="MaxCatchUpSeconds"/> of
/// them, so a stalled server does not flood a run) to the pipe networks coupled to it, shared
/// evenly; what one network cannot take is offered to the others, and what none takes is lost (a
/// spring keeps no store).
/// </summary>
public static class SpringFlow
{
    /// <summary>The most seconds one offer makes up for.</summary>
    public const float MaxCatchUpSeconds = 2f;

    /// <summary>The litres a spring offers for <paramref name="seconds"/> at
    /// <paramref name="litresPerSecond"/>.</summary>
    public static float Budget(float litresPerSecond, float seconds)
    {
        if (!(litresPerSecond > 0) || !(seconds > 0) || !float.IsFinite(litresPerSecond) || !float.IsFinite(seconds))
            return 0;
        return litresPerSecond * Math.Min(seconds, MaxCatchUpSeconds);
    }

    /// <summary>Shares <paramref name="budget"/> among <paramref name="takers"/> networks:
    /// <paramref name="offer"/>(index, litres) gives one network that much and returns what it took.
    /// Each round offers what is left evenly to the networks that took all they were offered so
    /// far; a network that took less is full. Returns the litres taken in all.</summary>
    public static float Share(float budget, int takers, Func<int, float, float> offer)
    {
        if (!(budget > 0) || takers <= 0)
            return 0;
        var open = Enumerable.Range(0, takers).ToList();
        float left = budget, taken = 0;
        // Every round closes a network or uses the budget up, so takers rounds are enough.
        for (int round = 0; round < takers && open.Count > 0 && left > 1e-6f; round++)
        {
            float each = left / open.Count;
            var stillOpen = new List<int>(open.Count);
            foreach (int i in open)
            {
                float got = Math.Clamp(offer(i, each), 0, each);
                taken += got;
                left -= got;
                if (got >= each - 1e-6f)
                    stillOpen.Add(i);
            }
            open = stillOpen;
        }
        return taken;
    }

    /// <summary>The change from <paramref name="wasTapped"/> to <paramref name="nowTapped"/>.</summary>
    public static TapChange Change(bool wasTapped, bool nowTapped) =>
        wasTapped == nowTapped ? TapChange.None : nowTapped ? TapChange.Tapped : TapChange.Untapped;
}
