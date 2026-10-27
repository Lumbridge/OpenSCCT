using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// The recoil the local merc sees with Reloaded (spec/equipment/merc-rifle.md rule 7a). Stock script loses both kicks
/// of ApplyWeaponJump on the next UpdateRotation (the view is reset to the desired rotation, which is reset to its
/// local copy). Reloaded's SetRotation wrapper (rva 0x24180) catches the camera kick of ApplyWeaponJump on the local
/// player's controller when not scoped: the rotation is put back and the kick added to two pending offsets. Every
/// frame Reloaded's view step (rva 0x117b0) turns pending kicks into a short animation: 35 % of the kick (plus what is
/// still applied, capped at 2400) reached in 0.033 s, then eased down to 10 % over the next 0.033 s; the 10 % stays.
/// The animation moves the local desired rotation (the aim) and the view together.
/// </summary>
public partial class SWeaponDefense
{
    /// <summary>Camera kicks of ApplyWeaponJump not yet animated (pitch, yaw).</summary>
    public int RecoilPendingPitch;

    public int RecoilPendingYaw;

    /// <summary>The animation's target offsets, start fraction, time, the offsets applied so far and whether it runs.</summary>
    public float RecoilTargetPitch;

    public float RecoilTargetYaw;

    public float RecoilStart;

    public float RecoilTime;

    public int RecoilAppliedPitch;

    public int RecoilAppliedYaw;

    public bool RecoilActive;

    /// <summary>True when the camera kick goes to the Reloaded offsets: the level's local player, merc, not scoped.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded SetRotation wrapper rva 0x24180: local player controller, caller ApplyWeaponJump, bBinocularVision off")]
    public bool RecoilCaptured(SPlayerController pc) =>
        ReferenceEquals(pc, Level.LocalPlayerController) && Level.NetMode != ENetMode.NM_DedicatedServer && !pc.bBinocularVision;

    /// <summary>
    /// One frame of Reloaded's recoil view step (decoded rva 0x117b0) for the local merc: starts an animation from the
    /// pending kicks, advances it by <paramref name="dt"/> and moves LocalMyDesiredRotation and Rotation by the change.
    /// Off (shot dead, charging, game ended, scoped, a frame of 0.5 s or more), whatever is still applied is taken back.
    /// </summary>
    [PortOf("rl:Engine.Actor.SetRotation", Spec = "spec/native/reloaded-hooks.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded view step rva 0x117b0: 0.35 share, 2400 cap, 800 kick limit, 0.033 s ramps, 0.9/0.1 ease, pitch -12000..13000")]
    public void ReloadedRecoilStep(float dt)
    {
        if (OwnerPC is not { } pc || !ReferenceEquals(pc, Level.LocalPlayerController) || Level.NetMode == ENetMode.NM_DedicatedServer)
        {
            return;
        }

        bool on = pc.Pawn is { Health: > 0 } && pc.SBPawn is SPAWNDEFENSE && !pc.bIsCharging && !pc.bGameEnded && !pc.bBinocularVision &&
            dt > 0f && dt < K.RecoilMaxFrame;
        if (!on)
        {
            TakeBackRecoil(pc);
            return;
        }

        int kickPitch = RecoilPendingPitch;
        int kickYaw = RecoilPendingYaw;
        RecoilPendingPitch = 0;
        RecoilPendingYaw = 0;
        bool none = kickPitch == 0 && kickYaw == 0;
        bool tooBig = (uint)kickPitch > K.RecoilKickLimit || SvmMath.Abs(kickYaw) > K.RecoilKickLimit;
        if (none || tooBig)
        {
            if (!RecoilActive)
            {
                return;
            }
        }
        else
        {
            RecoilTargetPitch = SvmMath.Clamp((kickPitch * K.RecoilShare) + RecoilAppliedPitch, -K.RecoilCap, K.RecoilCap);
            RecoilTargetYaw = SvmMath.Clamp((kickYaw * K.RecoilShare) + RecoilAppliedYaw, -K.RecoilCap, K.RecoilCap);
            RecoilStart = RecoilTargetPitch == 0f ? 0f : SvmMath.Clamp(RecoilAppliedPitch / RecoilTargetPitch, 0f, 1f);
            RecoilActive = true;
            RecoilTime = 0f;
        }

        RecoilTime += dt;
        bool done = false;
        float f;
        if (RecoilTime < K.RecoilRamp)
        {
            f = (RecoilTime / K.RecoilRamp * (1f - RecoilStart)) + RecoilStart;
        }
        else
        {
            float g = (RecoilTime - K.RecoilRamp) / K.RecoilRamp;
            if (g < 1f)
            {
                float h = 1f - g;
                f = (h * h * K.RecoilEase) + K.RecoilRest;
            }
            else
            {
                f = K.RecoilRest;
                done = true;
            }
        }

        int pitch = RoundAway(f * RecoilTargetPitch);
        int yaw = RoundAway(f * RecoilTargetYaw);
        FRotator local = pc.LocalMyDesiredRotation;
        int oldPitch = local.Pitch;
        int newPitch = SvmMath.Clamp(pitch - RecoilAppliedPitch + oldPitch, K.RecoilPitchMin, K.RecoilPitchMax);
        int dYaw = yaw - RecoilAppliedYaw;
        local.Pitch = newPitch;
        local.Yaw += dYaw;
        pc.LocalMyDesiredRotation = local;
        FRotator view = pc.Rotation;
        view.Yaw += dYaw;
        view.Pitch = newPitch + (view.Pitch - oldPitch);
        pc.Rotation = view;
        if (done)
        {
            RecoilAppliedPitch = 0;
            RecoilAppliedYaw = 0;
            RecoilActive = false;
            return;
        }

        RecoilAppliedPitch += newPitch - oldPitch;
        RecoilAppliedYaw = yaw;
    }

    /// <summary>The step's off branch: the offsets still applied are removed from the aim and the view.</summary>
    private void TakeBackRecoil(SPlayerController pc)
    {
        if (RecoilAppliedPitch != 0 || RecoilAppliedYaw != 0)
        {
            FRotator local = pc.LocalMyDesiredRotation;
            int oldPitch = local.Pitch;
            int newPitch = SvmMath.Clamp(oldPitch - RecoilAppliedPitch, K.RecoilPitchMin, K.RecoilPitchMax);
            local.Pitch = newPitch;
            local.Yaw -= RecoilAppliedYaw;
            pc.LocalMyDesiredRotation = local;
            FRotator view = pc.Rotation;
            view.Pitch = newPitch + (view.Pitch - oldPitch);
            view.Yaw -= RecoilAppliedYaw;
            pc.Rotation = view;
        }

        RecoilActive = false;
        RecoilAppliedPitch = 0;
        RecoilAppliedYaw = 0;
        RecoilTime = 0f;
        RecoilStart = 0f;
        RecoilTargetPitch = 0f;
        RecoilTargetYaw = 0f;
        RecoilPendingPitch = 0;
        RecoilPendingYaw = 0;
    }

    /// <summary>lroundf: to the nearest integer, halves away from zero.</summary>
    private static int RoundAway(float v) => v >= 0f ? (int)(v + 0.5f) : -(int)(-v + 0.5f);
}
