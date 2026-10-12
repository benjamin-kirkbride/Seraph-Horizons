using SeraphHorizons.Mod.Springs.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Springs;

/// <summary>
/// A spring's state (README "Springs"), on the server: whether a pipe couples to it
/// (<see cref="SpringTaps"/>), looked at as it loads and whenever a neighbour changes. Untapped, it
/// pours (<see cref="SpringPouring"/>); tapped, it pours nothing and, once a second, offers its rate's
/// water to the pipe networks coupled to it (<see cref="SpringFlow"/>). No tick runs while it is
/// untapped. With the <c>Springs</c> switch off, it turns back into the water source it replaced
/// as it loads.
/// </summary>
public class BlockEntityWaterSpring : BlockEntity
{
    /// <summary>Milliseconds from a change to the look it causes (several changes, one look); the
    /// game's water spreads 75 ms after a change.</summary>
    public const int LookDelayMs = 75;

    public const int FeedIntervalMs = 1000;

    private long _feedListener;
    private long _lastFeedMs;
    private bool _lookPending;

    /// <summary>Whether a pipe couples to the spring (synced, for the look-at text).</summary>
    public bool Tapped { get; private set; }

    /// <summary>The litres it put into its pipes on its last feed.</summary>
    public float LastFed { get; private set; }

    public BlockWaterSpring? Spring => Block as BlockWaterSpring;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side != EnumAppSide.Server)
            return;
        if (!SpringsSystem.On)
        {
            // The switch is off: back to the source it replaced, after this load.
            var pos = Pos.Copy();
            api.Event.RegisterCallback(_ => SpringsSystem.Revert(api.World, pos), 0);
            return;
        }
        // Pipe networks are rebuilt as their chunks load: look a little later.
        _lookPending = true;
        RegisterDelayedCallback(_ => DoLook(), 500);
    }

    /// <summary>Looks again shortly: at the taps, then pours or feeds.</summary>
    public void Look()
    {
        if (_lookPending || Api?.Side != EnumAppSide.Server || !SpringsSystem.On)
            return;
        _lookPending = true;
        RegisterDelayedCallback(_ => DoLook(), LookDelayMs);
    }

    private void DoLook()
    {
        _lookPending = false;
        if (Api == null || Spring is not { } spring || Api.World.BlockAccessor.GetBlockEntity(Pos) != this)
            return;
        bool tapped = SpringTaps.AnyTapped(Api.World, Pos);
        switch (SpringFlow.Change(Tapped, tapped))
        {
            case TapChange.Tapped:
                Tapped = true;
                StartFeeding();
                MarkDirty();
                // What it poured has no source now: it drains.
                SpringPouring.Notify(Api.World, Pos, spring);
                break;
            case TapChange.Untapped:
                Tapped = false;
                StopFeeding();
                LastFed = 0;
                MarkDirty();
                break;
        }
        if (Tapped)
            StartFeeding();
        else
            SpringPouring.Pour(Api.World, Pos, spring);
    }

    private void StartFeeding()
    {
        if (_feedListener != 0)
            return;
        _lastFeedMs = Api.World.ElapsedMilliseconds;
        _feedListener = RegisterGameTickListener(_ => Feed(), FeedIntervalMs);
    }

    private void StopFeeding()
    {
        if (_feedListener != 0)
            UnregisterGameTickListener(_feedListener);
        _feedListener = 0;
    }

    private void Feed()
    {
        if (Spring is not { } spring || !Tapped)
            return;
        long now = Api.World.ElapsedMilliseconds;
        float seconds = (now - _lastFeedMs) / 1000f;
        _lastFeedMs = now;
        float budget = SpringFlow.Budget(SpringsSystem.Settings.LitresPerSecond(spring.Kind), seconds);
        var networks = SpringTaps.Networks(Api, Pos);
        LastFed = SpringFlow.Share(budget, networks.Count, (i, litres) => SpringTaps.Produce(Api, networks[i], litres));
    }

    public override void OnBlockRemoved()
    {
        StopFeeding();
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        StopFeeding();
        base.OnBlockUnloaded();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool("tapped", Tapped);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        Tapped = tree.GetBool("tapped");
    }
}
