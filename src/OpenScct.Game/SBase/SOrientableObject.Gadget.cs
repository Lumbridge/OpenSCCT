using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SOrientableObject as the sticky cam's and the network cameras' view: the destroy clean-up and the tick that
// follows the replicated angles and plays the turn and zoom sounds. Written from spec/equipment/sticky-cam.md rule 13
// and spec/devices/cam-network.md rule 10.
public partial class SOrientableObject
{
    /// <summary>The deactivation sound; the server clears the owner pawn's used object when it was this one.</summary>
    [Uc("SBase.SOrientableObject.Destroyed", Sha1 = "30c467ef11fc655b43425ba9b0f4c9cd5cf7ef8a")]
    [PortOf("uc:SBase.SOrientableObject:Destroyed", Spec = "spec/equipment/sticky-cam.md#the-cam-actor")]
    protected override void Destroyed()
    {
        if (!DesactivationSound.IsNone)
        {
            PlaySound(DesactivationSound, ESoundSlot.SLOT_None, 1f, false, K.StickyGasSoundRadius);
        }

        if (Role == ENetRole.ROLE_Authority && (Owner as SBasePawn)?.Controller is SPlayerController { SBPawn: { } pawn } &&
            ReferenceEquals(pawn.UsedObject, this))
        {
            pawn.UsedObject = null;
        }

        base.Destroyed();
    }

    /// <summary>
    /// The orientable tick on every machine: a viewer that is not using the object adopts the replicated angles; the
    /// rotation and zoom sounds; the actor's rotation follows the angles.
    /// </summary>
    [Uc("SBase.SOrientableObject.Tick", Sha1 = "9788ea280e6d283e4a71305738e0bc2d675bc9d0")]
    [PortOf("uc:SBase.SOrientableObject:Tick", Spec = "spec/devices/cam-network.md#turning-and-limits")]
    protected override void Tick(float DeltaTime)
    {
        if (Level.GetLocalPlayerController() is SPlayerController local && !ReferenceEquals(local.SBPawn?.UsedObject, this))
        {
            AdoptReplicatedAngles();
        }

        UpdateObjectSounds();
        Rotation = new FRotator(iPitch, iYaw, Rotation.Roll);
    }

    /// <summary>A viewer that does not steer takes the replicated angles; a jump over 50 units plays the rotation sound.</summary>
    protected void AdoptReplicatedAngles()
    {
        if (iPitch == iReplicatedPitch && iYaw == iReplicatedYaw)
        {
            return;
        }

        if (SvmMath.Abs(iPitch - iReplicatedPitch) + SvmMath.Abs(iYaw - iReplicatedYaw) > K.CamRemoteRotationSound)
        {
            bPlayRotationSound = true;
        }

        iPitch = iReplicatedPitch;
        iYaw = iReplicatedYaw;
    }

    /// <summary>The rotation and zoom requests become the ambient sound; it stops 0.2 s after the last request.</summary>
    private void UpdateObjectSounds()
    {
        float now = Level.TimeSeconds;
        if (bPlayRotationSound)
        {
            AmbientSound = RotationSound;
            LastSoundUpdate = now;
            bPlayRotationSound = false;
        }
        else if (bPlayZoomInSound)
        {
            AmbientSound = ZoomSoundIn;
            LastSoundUpdate = now;
            bPlayZoomInSound = false;
        }
        else if (bPlayZoomOutSound)
        {
            AmbientSound = ZoomSoundOut;
            LastSoundUpdate = now;
            bPlayZoomOutSound = false;
        }

        if (now > LastSoundUpdate + K.CamSoundLinger)
        {
            AmbientSound = AssetRef.None;
            StopSound(RotationSound);
            StopSound(ZoomSoundOut);
            StopSound(ZoomSoundIn);
        }
    }

    /// <summary>On the local player's machine: drawn at 0.1 while <paramref name="Viewing"/>, else at 1.</summary>
    protected void SetSelfViewScale(bool Viewing)
    {
        float scale = Viewing ? K.SelfViewScale : 1f;
        if (DrawScale3D.X != scale)
        {
            DrawScale3D = new FVector(scale, scale, scale);
        }
    }
}
