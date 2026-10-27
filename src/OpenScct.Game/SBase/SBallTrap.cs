using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SBallTrap: a hidden launcher, written from spec/devices/ball-trap.md. Triggered, it throws a projectile of
/// ProjectileClass along its rotation every fFireRate seconds, at a random force between iVelocityMin and
/// iVelocityMax, until untriggered.
/// </summary>
public partial class SBallTrap
{
    /// <summary>Spawns one projectile at the origin and launches it (a grenade class only) with the force.</summary>
    [Uc("SBase.SBallTrap.ThrowGren", Sha1 = "0e50866949cd9a0273abab5b301b2dab8676bb51")]
    [PortOf("uc:SBase.SBallTrap:ThrowGren", Spec = "spec/devices/ball-trap.md#rules")]
    [PortOf("rep:SBase.SBallTrap.ThrowGren", Spec = "spec/devices/ball-trap.md#network")]
    public void ThrowGren(int Force, FVector GrOrigin, FRotator GrRotation)
    {
        Actor? proj = DeviceClasses.Spawn(this, ProjectileClass, this, Name.None, GrOrigin, GrRotation);

        // SProjGrenade(Proj).Launch: a projectile of another class is an Access None (no launch).
        (proj as SProjGrenade)?.Launch(Force, false);
    }

    /// <summary>Starts firing: a looping timer of fFireRate seconds (each trigger restarts it).</summary>
    [Uc("SBase.SBallTrap.Trigger", Sha1 = "a2d4e927ffc120dab74784b17c830919bf7ad234")]
    [PortOf("uc:SBase.SBallTrap:Trigger", Spec = "spec/devices/ball-trap.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator) => SetTimer(fFireRate, true);

    /// <summary>Stops firing.</summary>
    [Uc("SBase.SBallTrap.UnTrigger", Sha1 = "057abcab77d799acded157e2806b0d9b76a36765")]
    [PortOf("uc:SBase.SBallTrap:UnTrigger", Spec = "spec/devices/ball-trap.md#rules")]
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator) => SetTimer(0f, false);

    /// <summary>One shot at a force drawn between iVelocityMin and iVelocityMax, from the trap along its rotation.</summary>
    [Uc("SBase.SBallTrap.Timer", Sha1 = "7996d5bf6166f0235d5cdd25a1f36a05a3301335")]
    [PortOf("uc:SBase.SBallTrap:Timer", Spec = "spec/devices/ball-trap.md#rules")]
    protected override void Timer() => ThrowGren((int)RandRange(iVelocityMin, iVelocityMax), Location, Rotation);
}
