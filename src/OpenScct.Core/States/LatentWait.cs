namespace OpenScct.Core;

/// <summary>What a state's latent code is waiting for (the UE2 latent action of the state frame).</summary>
public enum LatentWait : byte
{
    None = 0,

    /// <summary>Sleep(Seconds): wakes on <see cref="LatentFrame.UntilTick"/>.</summary>
    Sleep = 1,

    /// <summary>FinishAnim(Channel): wakes when the animation on <see cref="LatentFrame.Channel"/> ends.</summary>
    FinishAnim = 2,

    /// <summary>FinishInterpolation(): wakes when bInterpolating clears.</summary>
    FinishInterpolation = 3,

    /// <summary>MoveTo / MoveToward (controllers): wakes when the move completes.</summary>
    MoveTo = 4,

    /// <summary>WaitForLanding(): wakes when the actor leaves PHYS_Falling.</summary>
    WaitForLanding = 5,
}
