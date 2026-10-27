using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>Engine.Pawn: a body in the world, possessed by a Controller. Only the core fields so far.</summary>
public abstract partial class Pawn : Actor
{
    protected Pawn()
    {
        bIsAPawn = true;
    }

    public Controller? Controller;
    public PlayerReplicationInfo? PlayerReplicationInfo;

    public int Health;
    public float EyeHeight;
    public float BaseEyeHeight;

    public float GroundSpeed;
    public float AirSpeed;
    public float AccelRate;
    public float JumpZ;
    public float AirControl;
    public float MaxFallSpeed;
    public float CrouchHeight;
    public float CrouchRadius;

    public bool bIsCrouched;
    public bool bWantsToCrouch;
    public bool bIsWalking;
    public bool bCanCrouch;
    public bool bCanJump;
    public bool bCanWalk;
    public bool bJustLanded;

    protected internal override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        Controller = Live(Controller);
        PlayerReplicationInfo = Live(PlayerReplicationInfo);
    }

    /// <summary>
    /// A pawn is its own Instigator, which the replication's autonomous downgrade relies on (a pawn goes to every
    /// connection but its owner's as a simulated proxy). Only the Instigator part of Pawn.PreBeginPlay is ported here.
    /// </summary>
    [Uc("Engine.Pawn.PreBeginPlay", Sha1 = "989a8b64d39787a9557cd95313e9cbc480e55a16")]
    [PortOf("uc:Engine.Pawn:PreBeginPlay", Spec = "spec/native/net-replication.md#building-an-update-replicateactor")]
    protected internal override void PreBeginPlay()
    {
        base.PreBeginPlay();
        Instigator = this;
    }
}
