namespace OpenScct.Core;

/// <summary>Result of a state change (UE2 EGotoState).</summary>
public enum EGotoState : byte
{
    /// <summary>The state was not found; the actor is now in no state.</summary>
    GOTOSTATE_NotFound = 0,

    GOTOSTATE_Success = 1,

    /// <summary>BeginState or EndState started another state change, which won.</summary>
    GOTOSTATE_Preempted = 2,
}
