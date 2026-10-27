namespace OpenScct.Core;

/// <summary>
/// The serializable part of an actor's state machine (UE2 FStateFrame): which state it is in, where its latent
/// state code is and what that code waits for. Plain data, so snapshots and replays can copy it as is.
/// <para>
/// <see cref="StateId"/> and <see cref="LabelId"/> are stable hashes of the state and label names (see
/// <see cref="ActorState.Id"/> and <see cref="StateLabel.Id"/>), so they mean the same thing in every process.
/// <see cref="Pc"/> is the resume point inside the latent code of the state that owns the label; 0 means no code
/// is running.
/// </para>
/// </summary>
public record struct LatentFrame
{
    /// <summary>Pc value meaning "no latent code" (UE2 Code == NULL).</summary>
    public const int NoCode = 0;

    /// <summary>Id of the current state, 0 when the actor is in no state.</summary>
    public int StateId;

    /// <summary>Id of the label the code was last entered at, 0 when no code is running.</summary>
    public int LabelId;

    /// <summary>Resume point in the code owner's latent code. Label entry points are always 1 or more.</summary>
    public int Pc;

    /// <summary>Current latent wait, or None.</summary>
    public LatentWait Wait;

    /// <summary>Sleep: first tick on which the code runs again. long.MaxValue means never.</summary>
    public long UntilTick;

    /// <summary>FinishAnim: the animation channel waited on.</summary>
    public int Channel;
}
