using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests;

/// <summary>Machines/Core's <see cref="HeldWorkFollower"/>: the W a hand machine's renderer shows while
/// the player holds right-click, predicted at frame rate and eased toward the server's synced W. The
/// simulations play the server as the press brake and the squaring shear run it (a 50 ms tick that
/// fires on the server's frames, W synced every 0.02) against a 60 fps client.</summary>
public class HeldWorkFollowerTests
{
    private const double Frame = 1 / 60.0;

    /// <summary>The server's W as the client receives it: (arrival time, W) for every sync, with the
    /// server starting to work <paramref name="latency"/> after the client and each packet taking as
    /// long again to arrive. <paramref name="ticks"/> are the server tick intervals, in turn.</summary>
    private static List<(double At, double Work)> Packets(double rate, double latency, double[] ticks, int seed = 1)
    {
        var rnd = new Random(seed);
        var packets = new List<(double, double)>();
        double w = 0, t = latency;
        for (int i = 0; w < 1; i++)
        {
            double dt = ticks[i % ticks.Length] + (rnd.NextDouble() - 0.5) * 0.008;
            double before = w;
            w = Math.Min(1, w + rate * dt);
            t += dt;
            if (Math.Floor(before / 0.02) != Math.Floor(w / 0.02))
                packets.Add((t + latency + rnd.NextDouble() * Frame, w));
        }
        return packets;
    }

    /// <summary>Plays the client's frames against <paramref name="packets"/>, held throughout, and
    /// returns the shown W at each frame.</summary>
    private static List<double> Play(Func<double, double, double, double> advance, double rate, List<(double At, double Work)> packets)
    {
        var shown = new List<double>();
        double server = 0, t = 0;
        int next = 0;
        while (t < 1.2 / rate)
        {
            t += Frame;
            while (next < packets.Count && packets[next].At <= t)
                server = packets[next++].Work;
            shown.Add(advance(Frame, server, rate));
        }
        return shown;
    }

    /// <summary>Each frame's step of the shown W as a share of the work's own pace, from 0.3 s in
    /// (the start settled) until W nears the end.</summary>
    private static List<double> Paces(List<double> shown, double rate) =>
        Enumerable.Range((int)(0.3 / Frame), shown.Count - 1 - (int)(0.3 / Frame))
            .Where(i => shown[i + 1] < 0.95)
            .Select(i => (shown[i + 1] - shown[i]) / (rate * Frame))
            .ToList();

    public static TheoryData<double, double, double[]> Runs()
    {
        var data = new TheoryData<double, double, double[]>();
        // the shear's lead (1 a second) and copper; the brake's lead and copper
        foreach (double rate in new[] { 1, 1 / 1.5, 1 / 2.25 })
            foreach (double latency in new[] { 0.002, 0.05 })
                foreach (var ticks in new[] { new[] { 0.05 }, new[] { 0.066 }, new[] { 0.05, 0.066, 0.066 } })
                    data.Add(rate, latency, ticks);
        return data;
    }

    [Theory]
    [MemberData(nameof(Runs))]
    public void Held_W_moves_every_frame_near_the_works_pace_and_never_jumps(double rate, double latency, double[] ticks)
    {
        var follower = new HeldWorkFollower();
        follower.Reset(0);
        var shown = Play((dt, server, r) => { follower.Advance(dt, server, r); return follower.Work; }, rate, Packets(rate, latency, ticks));
        var paces = Paces(shown, rate);
        Assert.NotEmpty(paces);
        // never back, never stopped, never more than a fraction ahead of the pace: no snap
        Assert.All(paces, p => Assert.InRange(p, HeldWorkFollower.MinPace - 1e-9, 1.3));
        // and it finishes with the server
        Assert.Equal(1, shown[^1], 9);
    }

    [Fact]
    public void A_prediction_clamped_to_a_band_round_the_servers_W_snaps_the_shear()
    {
        // what the shear's and the brake's clocks did: advance at pace, but snap to the server's W
        // whenever behind it or more than 0.06 ahead. The shear's lead plate is a whole plate a
        // second, so a server tick of 66 ms moves it 0.066, more than the band: at nearly every packet
        // the shown W is snapped back, and it moves in the server's steps (the stutter).
        double work = 0;
        double Clamped(double dt, double server, double rate)
        {
            work += rate * dt;
            if (work < server || work > server + 0.06)
                work = server;
            return work;
        }
        var paces = Paces(Play(Clamped, 1, Packets(1, 0.002, [0.066])), 1);
        Assert.Contains(paces, p => p < 0);
        Assert.Contains(paces, p => p > 2);
    }

    [Fact]
    public void Not_held_W_eases_to_the_server_and_a_new_job_or_a_jump_is_taken_at_once()
    {
        var follower = new HeldWorkFollower();
        follower.Reset(0.3);
        Assert.Equal(0.3, follower.Work);
        // let go a little ahead of the server: W eases back to it, never past it
        follower.Reset(0.32);
        for (int i = 0; i < 60; i++)
            follower.Advance(Frame, 0.3, 0);
        Assert.InRange(follower.Work, 0.3, 0.3 + 1e-3);
        // the server moves on while another player works it: W eases after it
        follower.Advance(Frame, 0.35, 0);
        Assert.InRange(follower.Work, 0.3, 0.35);
        // the next plate (W back to 0) or a gap past Resync is taken at once
        follower.Advance(Frame, 0, 0);
        Assert.Equal(0, follower.Work);
        follower.Advance(Frame, 0.9, 1);
        Assert.Equal(0.9, follower.Work, 9);
    }

    [Fact]
    public void Held_W_runs_on_from_a_stale_server_for_a_while_then_waits_for_it_without_turning_back()
    {
        var follower = new HeldWorkFollower();
        follower.Reset(0);
        // the server says nothing new for two seconds while held: W runs at pace while the server's
        // W is carried forward, then slows, and stops a whole Resync ahead of it
        double last = 0;
        for (int i = 0; i < 120; i++)
        {
            follower.Advance(Frame, 0, 1);
            Assert.True(follower.Work >= last);
            if (i < 15)
                Assert.True(follower.Work > last);
            last = follower.Work;
        }
        double carried = HeldWorkFollower.MaxExtrapolateSeconds;
        Assert.InRange(follower.Work, carried + HeldWorkFollower.Resync - 0.02, carried + HeldWorkFollower.Resync + 0.02);
        // when it moves on, W goes on from there
        follower.Advance(Frame, 0.6, 1);
        Assert.True(follower.Work > last);
        // W and the rate are bounded
        follower.Advance(10, 1, 1);
        Assert.Equal(1, follower.Work);
        follower.Advance(-1, 2, -1);
        Assert.Equal(1, follower.Work);
    }
}
