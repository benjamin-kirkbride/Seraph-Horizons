namespace SeraphHorizons.Mod.Eidolon.Core;

public enum EidolonPoseState
{
    /// <summary>On its feet, free to work.</summary>
    Standing,

    /// <summary>Slumped on its knees (<c>slump</c>, held), disabled.</summary>
    Slumped,

    /// <summary>Playing a one-shot that ends standing (<c>standup</c> from a slump, or <c>activate</c>
    /// when it wakes); not yet free to work.</summary>
    Rising,
}

public enum EidolonPoseAction
{
    None,

    /// <summary>Stop what it does and play <c>slump</c> (stopping <c>standup</c> if it was rising).</summary>
    Slump,

    /// <summary>Play <c>standup</c>, then stop <c>slump</c> (the order the shape's README asks).</summary>
    StandUp,

    /// <summary>The rising one-shot has played out: it stands, free to work.</summary>
    Stood,
}

/// <summary>
/// Slumping and standing up (Eidolon/README.md, "Slumping"): fed each server tick whether some upkeep
/// says it must slump, it tells the entity which animation to play. A slump interrupts standing up;
/// standing up takes its animation's length before it can work again.
/// </summary>
public sealed class EidolonPose
{
    public EidolonPoseState State { get; private set; } = EidolonPoseState.Standing;

    /// <summary>When the rising one-shot ends (seconds, the caller's clock).</summary>
    public double RisingUntil { get; private set; }

    /// <summary>Whether it may work: standing, not slumped nor still rising.</summary>
    public bool CanAct => State == EidolonPoseState.Standing;

    /// <summary>Advances the pose; returns what the entity must play now.</summary>
    public EidolonPoseAction Update(bool mustSlump, double now, double standUpSeconds)
    {
        switch (State)
        {
            case EidolonPoseState.Standing when mustSlump:
            case EidolonPoseState.Rising when mustSlump:
                State = EidolonPoseState.Slumped;
                return EidolonPoseAction.Slump;
            case EidolonPoseState.Slumped when !mustSlump:
                Rise(now + standUpSeconds);
                return EidolonPoseAction.StandUp;
            case EidolonPoseState.Rising when now >= RisingUntil:
                State = EidolonPoseState.Standing;
                return EidolonPoseAction.Stood;
            default:
                return EidolonPoseAction.None;
        }
    }

    /// <summary>Starts a rising one-shot of its own (<c>activate</c> as it wakes) that ends at
    /// <paramref name="until"/>.</summary>
    public void Rise(double until)
    {
        State = EidolonPoseState.Rising;
        RisingUntil = until;
    }
}
