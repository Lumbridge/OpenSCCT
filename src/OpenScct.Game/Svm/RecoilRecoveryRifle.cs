using System;
using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Svm;

/// <summary>Mercenary rifles that use the M4's per-shot aim rise and recovery.</summary>
public abstract class RecoilRecoveryRifle : MercRifle
{
    // M4 ControllerRecoil values, including the saved receiver/loadout's vertical and horizontal modifiers.
    private const float VerticalFactor = .70f, HorizontalFactor = .90f;
    private const float Smoothing = 50f, ReleaseDamping = 15f;
    private const float UnitsPerDegree = 65536f / 360f;
    private float _targetPitch, _targetYaw, _smoothPitch, _smoothYaw;
    private FRotator _lastAim;
    private float _manualPitch, _manualYaw;
    private bool _triggerActive, _shotSinceTick;
    private SPlayerController? _recoilOwner;

    public override void ApplyWeaponJump()
    {
        if (OwnerPC is not { IsRemotePlayer: false } pc) return;
        if (!ReferenceEquals(pc, _recoilOwner))
        {
            ClearRecoil();
            _recoilOwner = pc;
        }
        if (!_triggerActive)
        {
            _lastAim = pc.LocalMyDesiredRotation;
            _manualPitch = _manualYaw = 0f;
            _triggerActive = true;
        }
        float yaw = RandRange(-.3f, .3f) * HorizontalFactor;
        float pitch = RandRange(.2f, .3f) * VerticalFactor;
        _targetYaw = Math.Clamp(_targetYaw + yaw, -90f, 90f);
        _targetPitch = Math.Clamp(_targetPitch + pitch, -90f, 90f);
        _shotSinceTick = true;
    }

    protected override void NativeTick(float dt)
    {
        base.NativeTick(dt);
        if (_recoilOwner is not { } pc) return;
        if (!ReferenceEquals(OwnerPC, pc) || pc.SBPawn is not { Health: > 0 } pawn || pc.bGameEnded)
        {
            ClearRecoil();
            return;
        }

        bool burstPending = ROFMode == SRateOfFireMode.ROF_Burst && BulletsToFire > 0 && ClipAmmo > 0;
        bool firing = _shotSinceTick || ((FireHeld || burstPending) && ReferenceEquals(pawn.MyWeapon, this) && IsInState("s_Firing"));
        _shotSinceTick = false;
        if (_triggerActive)
        {
            // Both look devices have already been calibrated by the controller. Only motion since our own
            // previous update is player movement. Wrapping yaw must not turn a small correction into a full turn.
            _manualPitch += (short)(pc.LocalMyDesiredRotation.Pitch - _lastAim.Pitch) / UnitsPerDegree;
            _manualYaw += (short)(pc.LocalMyDesiredRotation.Yaw - _lastAim.Yaw) / UnitsPerDegree;
            if (!firing)
            {
                _triggerActive = false;
                _smoothPitch = _targetPitch = RemainingAfterCounterInput(_smoothPitch, _manualPitch);
                _smoothYaw = _targetYaw = RemainingAfterCounterInput(_smoothYaw, _manualYaw);
            }
        }
        float follow = Math.Clamp(dt * Smoothing, 0f, 1f);
        float previousPitch = _smoothPitch, previousYaw = _smoothYaw;
        _smoothPitch += (_targetPitch - _smoothPitch) * follow;
        _smoothYaw += (_targetYaw - _smoothYaw) * follow;
        if (!firing)
        {
            float keep = 1f - Math.Clamp(dt * ReleaseDamping, 0f, 1f);
            _targetPitch *= keep;
            _targetYaw *= keep;
        }

        // Apply the change in absolute quantized offsets; rounding each incremental step would introduce drift.
        int pitch = ControllerUnits(_smoothPitch) - ControllerUnits(previousPitch);
        int yaw = ControllerUnits(_smoothYaw) - ControllerUnits(previousYaw);
        if (pitch != 0 || yaw != 0)
        {
            FRotator local = pc.LocalMyDesiredRotation;
            int reached = Math.Clamp(local.Pitch + pitch, MovementK.PitchMin, MovementK.PitchMax) - local.Pitch;
            if (reached != pitch)
            {
                _smoothPitch += (reached - pitch) / UnitsPerDegree;
                _targetPitch = _smoothPitch;
            }
            local.Pitch += reached;
            local.Yaw += yaw;
            pc.LocalMyDesiredRotation = pc.MyDesiredRotation = local;
            FRotator view = pc.Rotation;
            view.Pitch += reached;
            view.Yaw += yaw;
            pc.Rotation = view;
        }
        _lastAim = pc.LocalMyDesiredRotation;
        if (!firing && ControllerUnits(_targetPitch) == 0 && ControllerUnits(_targetYaw) == 0 &&
            ControllerUnits(_smoothPitch) == 0 && ControllerUnits(_smoothYaw) == 0)
            ClearRecoil();
    }

    private static int ControllerUnits(float degrees) => (int)MathF.Round(degrees * UnitsPerDegree);

    private static float RemainingAfterCounterInput(float recoil, float input) =>
        recoil * input < 0f ? MathF.CopySign(Math.Max(0f, Math.Abs(recoil) - Math.Abs(input)), recoil) : recoil;

    private void ClearRecoil()
    {
        _targetPitch = _targetYaw = _smoothPitch = _smoothYaw = 0f;
        _triggerActive = _shotSinceTick = false;
        _recoilOwner = null;
    }
}
