namespace SeraphHorizons.Mod.TrunkEntities.Core;

/// <summary>
/// The client's count of a trunk pick-up hold, for the progress circle: the seconds held towards
/// the seconds the server's hold needs (<c>TrunkCarry.PickUpSeconds</c>). Started on the first
/// frame the hold's conditions are met, ended (<see cref="Reset"/>) the first frame they are not.
/// </summary>
public sealed class HoldProgress
{
    private float _held, _total;

    /// <summary>Whether a hold is being counted.</summary>
    public bool Active { get; private set; }

    /// <summary>The trunk entity the hold is on (0 for none).</summary>
    public long Target { get; private set; }

    /// <summary>The hold's share done, 0 to 1 (1 when it needs no time).</summary>
    public float Fraction => !Active ? 0f : _total <= 0 ? 1f : Math.Clamp(_held / _total, 0f, 1f);

    /// <summary>Whether the hold has run its full length.</summary>
    public bool Done => Active && Fraction >= 1f;

    /// <summary>Counts <paramref name="dt"/> seconds of holding on <paramref name="target"/>, which
    /// needs <paramref name="total"/> seconds; a new target starts the count over.</summary>
    public void Advance(long target, float total, float dt)
    {
        if (!Active || target != Target)
        {
            Active = true;
            Target = target;
            _held = 0;
        }
        _total = float.IsFinite(total) ? Math.Max(0, total) : 0;
        if (float.IsFinite(dt) && dt > 0)
            _held += dt;
    }

    /// <summary>Ends the hold: the circle goes.</summary>
    public void Reset()
    {
        Active = false;
        Target = 0;
        _held = 0;
    }
}
