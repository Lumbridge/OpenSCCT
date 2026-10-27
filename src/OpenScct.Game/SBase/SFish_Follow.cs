using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SFish_Follow: a fish of a school that keeps its placed offset (distance, relative yaw, height) from its Pilot
/// SFish (one with bPilote), re-aiming at that point every fRefreshTime, slowing to a stop and looping AnimsFish[1]
/// when there, and kept inside the pilot's X and Y box (spec/devices/fish.md). Simulated on every machine.
/// </summary>
public partial class SFish_Follow
{
    /// <summary>The sequence the fish loops on its mesh (a skeletal fish), or None (the native channel, for views).</summary>
    public Name LoopedAnim { get; private set; }

    /// <summary>Records the placed offset from the pilot and the pilot's box; a missing pilot leaves the fish still.</summary>
    [Uc("SBase.SFish_Follow.PostBeginPlay", Sha1 = "c8fd195fda2fdfc5e1cef98316e28f746bf797c2")]
    [PortOf("uc:SBase.SFish_Follow:PostBeginPlay", Spec = "spec/devices/fish.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        SetPhysics(EPhysics.PHYS_MovingBrush);

        // The script logs a missing or non-pilot Pilot (SFish(Pilot) of another class reads as None and its fields as 0).
        if (Pilot is null)
        {
            return;
        }

        var pilot = Pilot as SFish;
        fSpeedMax = (pilot?.fSpeedMax ?? 0f) * 1.2f;
        FVector vDiff = Pilot.Location - Location;
        vDiff.Z = 0f;
        FRotator rot = Pilot.Rotation - vDiff.Rotation();
        fRelativeYaw = rot.Yaw;
        fRelativeDist = vDiff.Size();
        fRelativeZ = Pilot.Location.Z - Location.Z;
        fBorneXmin = Pilot.Location.X - (pilot?.fEloignementMaxX ?? 0f);
        fBorneXmax = Pilot.Location.X + (pilot?.fEloignementMaxX ?? 0f);
        fBorneYmin = Pilot.Location.Y - (pilot?.fEloignementMaxY ?? 0f);
        fBorneYmax = Pilot.Location.Y + (pilot?.fEloignementMaxY ?? 0f);
        UpdateYawPitch();
    }

    /// <summary>
    /// Following a pilot fish: within 5 units of the wanted point it slows to a stop on AnimsFish[1]; further away it
    /// speeds up (to fSpeedMax) on AnimsFish[0] and turns towards the point, harder the further it is, pitch kept
    /// within 3000; the new location stays inside the pilot's box.
    /// </summary>
    [Uc("SBase.SFish_Follow.Tick", Sha1 = "0166156d9135e0e24928bfdf17d8483a8adb0f1e")]
    [PortOf("uc:SBase.SFish_Follow:Tick", Spec = "spec/devices/fish.md#rules")]
    protected override void Tick(float DeltaTime)
    {
        if (Pilot is not SFish { bPilote: true })
        {
            return;
        }

        if (Level.TimeSeconds > fRefreshDate)
        {
            UpdateYawPitch();
        }

        FVector vDir = vWantedPos - Location;
        FVector vTmp = vDir;
        vTmp.Z = 0f;
        float fDist = vTmp.Size();
        if (fDist < 5f)
        {
            if (iCurAnim != 1)
            {
                iCurAnim = 1;
                if (!AnimsFish[1].IsNone)
                {
                    LoopAnim(AnimsFish[1]);
                }
            }

            fCurrentSpeed -= DeltaTime * 100f;
            if (fCurrentSpeed < 0f)
            {
                fCurrentSpeed = 0f;
            }

            vDir = Rotation.Vector() * DeltaTime * fCurrentSpeed;
            vDir += Location;
        }
        else
        {
            if (iCurAnim != 0)
            {
                iCurAnim = 0;
                if (!AnimsFish[0].IsNone)
                {
                    LoopAnim(AnimsFish[0]);
                }
            }

            fCurrentSpeed = ((10f * fCurrentSpeed) + (fDist - 5f)) / 11f;
            if (fCurrentSpeed > fSpeedMax)
            {
                fCurrentSpeed = fSpeedMax;
            }

            if (fDist > 200f)
            {
                fDist = 200f;
            }

            fDist = ((200f - fDist) / 20f) + 1f;
            vDir = ((Rotation.Vector() * fDist) + vDir.SafeNormal()) / (fDist + 1f);
            FRotator rot = vDir.Rotation();
            if (rot.Pitch > 3000)
            {
                rot.Pitch = 3000;
            }
            else if (rot.Pitch < -3000)
            {
                rot.Pitch = -3000;
            }

            Rotation = rot;
            vDir *= fCurrentSpeed * DeltaTime;
            vDir += Location;
        }

        if (vDir.X < fBorneXmin)
        {
            vDir.X = fBorneXmin;
        }
        else if (vDir.X > fBorneXmax)
        {
            vDir.X = fBorneXmax;
        }

        if (vDir.Y < fBorneYmin)
        {
            vDir.Y = fBorneYmin;
        }
        else if (vDir.Y > fBorneYmax)
        {
            vDir.Y = fBorneYmax;
        }

        SetLocation(vDir);
    }

    /// <summary>The wanted point: the placed offset turned with the pilot's yaw; the next refresh in fRefreshTime.</summary>
    [Uc("SBase.SFish_Follow.UpdateYawPitch", Sha1 = "61158afd962d9f1504dedb6168058c37ef64386d")]
    [PortOf("uc:SBase.SFish_Follow:UpdateYawPitch", Spec = "spec/devices/fish.md#rules")]
    private void UpdateYawPitch()
    {
        if (Pilot is null)
        {
            return;
        }

        FRotator rot = Pilot.Rotation;
        rot.Yaw += SvmMath.Trunc(fRelativeYaw);
        FVector vPos = rot.Vector() * fRelativeDist;
        vPos.Z = fRelativeZ;
        vWantedPos = Pilot.Location - vPos;
        fRefreshDate = Level.TimeSeconds + (FRand() * (fRefreshTimeMax - fRefreshTimeMin)) + fRefreshTimeMin;
    }

    /// <summary>Native LoopAnim on channel 0 at rate 1: the views loop the sequence on the fish's skeletal mesh.</summary>
    [UcNative("AActor::execLoopAnim")]
    [PortOf("nat:Engine.Actor.LoopAnim", Spec = "spec/devices/fish.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Only the looped sequence is kept (no notifies, tween or rate): the fish meshes are cosmetic")]
    private void LoopAnim(Name Sequence) => LoopedAnim = Sequence;
}
