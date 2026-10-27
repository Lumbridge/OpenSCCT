using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Engine.Pawn possession, used by the game flow's spawn and respawn. Written from
// spec/game/respawn-and-spawning.md.
public abstract partial class Pawn
{
    /// <summary>
    /// The controller takes this pawn: replication info, owner name, roles and eye height. A player pawn becomes
    /// AutonomousProxy off Standalone and the view target (its eye height follows its base from then on, on the
    /// server too). Not ported here: ChangeAnimation (the animation layer).
    /// </summary>
    [Uc("Engine.Pawn.PossessedBy", Sha1 = "9e0270e1809607f856c9392f0ed2b538119887f2")]
    [PortOf("uc:Engine.Pawn:PossessedBy", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public virtual void PossessedBy(Controller C)
    {
        Controller = C;
        NetPriority = 3f;
        if (C.PlayerReplicationInfo is { } pri)
        {
            PlayerReplicationInfo = pri;
            OwnerName = pri.PlayerName;
        }

        if (C is PlayerController)
        {
            if (Level.NetMode != ENetMode.NM_Standalone)
            {
                RemoteRole = ENetRole.ROLE_AutonomousProxy;
            }

            BecomeViewTarget();
        }
        else
        {
            RemoteRole = (GetUcClass().Create() as Actor)?.RemoteRole ?? RemoteRole;
        }

        SetOwner(Controller);
        EyeHeight = BaseEyeHeight;
    }

    [Uc("Engine.Pawn.UnPossessed", Sha1 = "ea182c3ba32de908706db1dfd1f968c5bbfe6d40")]
    [PortOf("uc:Engine.Pawn:UnPossessed", Spec = "spec/game/death-lives-respawn.md#rules")]
    public virtual void UnPossessed()
    {
        PlayerReplicationInfo = null;
        SetOwner(null);
        Controller = null;
    }

    /// <summary>
    /// The owning machine takes the pawn: no velocity or acceleration, the default eye height. Not ported here:
    /// PlayWaiting (the animation layer).
    /// </summary>
    [Uc("Engine.Pawn.ClientRestart", Sha1 = "8cd8d16faa8af38a443d21f389d7a80297c964b9")]
    [PortOf("uc:Engine.Pawn:ClientRestart", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public virtual void ClientRestart()
    {
        Velocity = default;
        Acceleration = default;
        BaseEyeHeight = (GetUcClass().Create() as Pawn)?.BaseEyeHeight ?? BaseEyeHeight;
        EyeHeight = BaseEyeHeight;
    }

    /// <summary>A player's pawn: its eye height is smoothed toward its base every tick (UpdateEyeHeight).</summary>
    [Uc("Engine.Pawn.BecomeViewTarget", Sha1 = "7e98d70248d215de4dd48a3fdbe00efefa95813a")]
    [PortOf("uc:Engine.Pawn:BecomeViewTarget", Spec = "spec/movement/pawn.md#eye-position")]
    public virtual void BecomeViewTarget() => bUpdateEyeheight = true;

    /// <summary>True when the view is behind the pawn.</summary>
    [Uc("Engine.Pawn.PointOfView", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:Engine.Pawn:PointOfView", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public virtual bool PointOfView() => false;
}
