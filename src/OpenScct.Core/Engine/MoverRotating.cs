using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>
/// Engine.MoverRotating: a Mover whose legs also turn it, written from spec/devices/mover-rotating.md. It packs the
/// rotation the leg starts from for clients (SimOldRotPitch/Yaw/Roll) and keeps a RealRotation next to RealPosition
/// for the client drift check. Every mover of the same Tag is put in one return group (Tag) without a leader.
/// </summary>
public partial class MoverRotating
{
    /// <summary>As Mover.Timer, and a client also snaps its rotation to RealRotation when it corrects.</summary>
    [Uc("Engine.MoverRotating.Timer", Sha1 = "c017263bab019aecadce7edb3bd3dfac3665fda8")]
    [PortOf("uc:Engine.MoverRotating:Timer", Spec = "spec/devices/mover-rotating.md#rules")]
    [PortOf("rep:Engine.MoverRotating.RealRotation", Spec = "spec/devices/mover-rotating.md#network")]
    protected internal override void Timer()
    {
        if (!Velocity.IsZero())
        {
            bClientPause = false;
            return;
        }

        if (Level.NetMode == ENetMode.NM_Client)
        {
            if (ClientUpdate == 0)
            {
                if (bClientPause)
                {
                    if ((RealPosition - Location).Size() > MoverK.ClientSnapDistance)
                    {
                        SetLocation(RealPosition);
                    }
                    else
                    {
                        RealPosition = Location;
                    }

                    Rotation = RealRotation;
                    bClientPause = false;
                }
                else if (RealPosition != Location)
                {
                    bClientPause = true;
                }
            }
            else
            {
                bClientPause = false;
            }
        }
        else
        {
            RealPosition = Location;
            RealRotation = Rotation;
        }
    }

    /// <summary>Mover.InterpolateTo, and the start rotation goes to clients in SimOldRotPitch/Yaw/Roll.</summary>
    [Uc("Engine.MoverRotating.InterpolateTo", Sha1 = "cf7f58abd8f44de152a010c3045e21abfc950e98")]
    [PortOf("uc:Engine.MoverRotating:InterpolateTo", Spec = "spec/devices/mover-rotating.md#rules")]
    [PortOf("rep:Engine.MoverRotating.SimOldRotPitch", Spec = "spec/devices/mover-rotating.md#network")]
    [PortOf("rep:Engine.MoverRotating.SimOldRotYaw", Spec = "spec/devices/mover-rotating.md#network")]
    [PortOf("rep:Engine.MoverRotating.SimOldRotRoll", Spec = "spec/devices/mover-rotating.md#network")]
    public override void InterpolateTo(byte NewKeyNum, float Seconds)
    {
        base.InterpolateTo(NewKeyNum, Seconds);
        SimOldRotYaw = OldRot.Yaw;
        SimOldRotPitch = OldRot.Pitch;
        SimOldRotRoll = OldRot.Roll;
    }

    /// <summary>Mover.KeyFrameReached, without the StopSound, and the server also keeps RealRotation.</summary>
    [Uc("Engine.MoverRotating.KeyFrameReached", Sha1 = "6d5a6d0e2fd5f06b66051dbbe8c7169b6d22eedb")]
    [PortOf("uc:Engine.MoverRotating:KeyFrameReached", Spec = "spec/devices/mover-rotating.md#rules")]
    public override void KeyFrameReached()
    {
        byte OldKeyNum = PrevKeyNum;
        PrevKeyNum = KeyNum;
        PhysAlpha = 0f;
        ClientUpdate--;
        if (KeyNum > 0 && KeyNum < OldKeyNum)
        {
            InterpolateTo((byte)(KeyNum - 1), MoveTime);
        }
        else if (KeyNum < NumKeys - 1 && KeyNum > OldKeyNum)
        {
            InterpolateTo((byte)(KeyNum + 1), MoveTime);
        }
        else
        {
            AmbientSound = AssetRef.None;
            if (ClientUpdate == 0 && Level.NetMode != ENetMode.NM_Client)
            {
                RealPosition = Location;
                RealRotation = Rotation;
            }
        }
    }

    /// <summary>
    /// Mover.BeginPlay run a second time from here (the script calls Super.BeginPlay after its own copy of the
    /// first half), then the pose of the current key, RealRotation, and the Tag as return group with no leader.
    /// </summary>
    [Uc("Engine.MoverRotating.BeginPlay", Sha1 = "1b782c8258bb3a30b370cfef59f0c7e2dbd3f944")]
    [PortOf("uc:Engine.MoverRotating:BeginPlay", Spec = "spec/devices/mover-rotating.md#rules")]
    protected internal override void BeginPlay()
    {
        OriginalDelayTime = DelayTime;
        OriginalDelayTimeLastKey = DelayTimeLastKey;
        OriginalMoveTime = MoveTime;
        OriginalStayOpenTime = StayOpenTime;
        OriginalMoverEncroachType = MoverEncroachType;
        if (!AntiPortalTag.IsNone)
        {
            foreach (AntiPortalActor antiPortal in XLevel.AllActors<AntiPortalActor>(AntiPortalTag))
            {
                AntiPortals.Add(antiPortal);
            }
        }

        if (Level.NetMode != ENetMode.NM_Standalone)
        {
            SetTimer(Level.NetMode == ENetMode.NM_Client ? MoverK.ClientTimer : MoverK.ServerTimer, true);
            if (Role < ENetRole.ROLE_Authority)
            {
                return;
            }
        }

        base.BeginPlay();
        KeyNum = (byte)SvmMath.Clamp(KeyNum, 0, MoverK.MaxKeys - 1);
        PhysAlpha = 0f;
        Location = BasePos + KeyPos[KeyNum];
        Rotation = BaseRot + KeyRot[KeyNum];
        if (Level.NetMode != ENetMode.NM_Client)
        {
            RealPosition = Location;
            RealRotation = Rotation;
        }

        if (ReturnGroup.IsNone)
        {
            ReturnGroup = Tag;
        }

        Leader = null;
        Follower = null;
    }

    /// <summary>
    /// Client start of a leg (Mover's native net receive), with the start rotation taken from the replicated
    /// SimOldRotPitch/Yaw/Roll instead of the current rotation.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "AMoverRotating net receive not decoded: the SimOldRot fields exist only to carry the start rotation, so they are taken as OldRot")]
    public override void ClientStartInterpolation()
    {
        base.ClientStartInterpolation();
        OldRot = new FRotator(SimOldRotPitch, SimOldRotYaw, SimOldRotRoll);
    }
}
