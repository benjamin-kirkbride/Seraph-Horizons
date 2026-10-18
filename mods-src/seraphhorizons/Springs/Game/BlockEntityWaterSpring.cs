using SeraphHorizons.Mod.Springs.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Springs;

/// <summary>
/// A spring's state (README "Springs"), on the server: whether a pipe couples to it
/// (<see cref="SpringTaps"/>), looked at as it loads and whenever a neighbour changes. Untapped, it
/// pours (<see cref="SpringPouring"/>); tapped, it pours nothing and, once a second, offers its rate's
/// water to the pipe networks coupled to it (<see cref="SpringFlow"/>). A rapids spring keeps the
/// one face it pours out of (<see cref="PourFace"/>), and when it turns to another, tells the water
/// round it to look again, so what it poured on the old face drains. No tick runs while it is
/// untapped. A side whose cell beyond is in a chunk not loaded (a spring at its column's edge loads
/// before the pipe in the next) can not be read: with no pipe seen on the others it keeps the state
/// saved with it and looks again each second until every side is loaded. With the <c>Springs</c>
/// switch off, it turns back into the water source it replaced as it loads.
/// </summary>
public class BlockEntityWaterSpring : BlockEntity
{
    /// <summary>Milliseconds from a change to the look it causes (several changes, one look); the
    /// game's water spreads 75 ms after a change.</summary>
    public const int LookDelayMs = 75;

    public const int FeedIntervalMs = 1000;

    /// <summary>Milliseconds to the next look while the cell beyond a side is in a chunk not loaded
    /// (<see cref="SpringFlow.Settle"/>).</summary>
    public const int UnloadedLookDelayMs = 1000;

    private long _feedListener;
    private long _lastFeedMs;
    private bool _lookPending;

    /// <summary>Whether a pipe couples to the spring (synced, for the look-at text).</summary>
    public bool Tapped { get; private set; }

    /// <summary>The one face a rapids spring pours out of (<c>BlockFacing.ALLFACES</c> index, or
    /// <see cref="SpringPour.NoFace"/>); null for a normal spring, and for a rapids spring that has
    /// not looked yet in a world from before it chose one (<see cref="SpringPour.FeedsFace"/>).</summary>
    public int? PourFace { get; private set; }

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
        var (anyTapped, anyUnloaded) = SpringTaps.Read(Api.World, Pos);
        var (tapped, lookAgain) = SpringFlow.Settle(Tapped, anyTapped, anyUnloaded);
        // A side in a chunk not loaded yet: its loading tells the spring nothing, so it looks again.
        if (lookAgain)
        {
            _lookPending = true;
            RegisterDelayedCallback(_ => DoLook(), UnloadedLookDelayMs);
        }
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
            Pour(spring);
    }

    private void Pour(BlockWaterSpring spring)
    {
        var (faces, face) = SpringPouring.Plan(Api.World, Pos, spring, PourFace);
        bool turned = face != PourFace;
        // Chosen before the water is put: what it puts asks at once which cells it feeds.
        PourFace = face;
        SpringPouring.Pour(Api.World, Pos, spring, faces);
        if (!turned)
            return;
        MarkDirty();
        // The face it left has no source now: its water drains.
        SpringPouring.Notify(Api.World, Pos, spring);
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
        if (PourFace is { } face)
            tree.SetInt("pourFace", face);
        else
            tree.RemoveAttribute("pourFace");
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        Tapped = tree.GetBool("tapped");
        PourFace = tree.HasAttribute("pourFace") ? tree.GetInt("pourFace") : null;
    }
}
