using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SNoiseEmitter: the placed alarm snare (spec/equipment/alarm-snare.md rules 5 to 10). Every machine makes its
/// own fake footsteps, breaths and merc sound arrows; the server sets off the alarms of nearby security devices.
/// </summary>
public partial class SNoiseEmitter
{
    /// <summary>Any damage destroys the snare.</summary>
    [Uc("SBase.SNoiseEmitter.TakeDamage", Sha1 = "842d47d9443f6db763a74955d860559be935f9af")]
    [PortOf("uc:SBase.SNoiseEmitter:TakeDamage", Spec = "spec/equipment/alarm-snare.md#rules")]
    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0) => Destroy();

    [Uc("SBase.SNoiseEmitter.PreBeginPlay", Sha1 = "58d539988487946529bd5a3b223f76e647e723f0")]
    [PortOf("uc:SBase.SNoiseEmitter:PreBeginPlay", Spec = "spec/equipment/alarm-snare.md#rules")]
    protected override void PreBeginPlay() => base.PreBeginPlay();

    /// <summary>The first noise in 0.5 s off a dedicated server; the server's first fake alarms.</summary>
    [Uc("SBase.SNoiseEmitter.PostBeginPlay", Sha1 = "3166709b76a9dc5aa9c777812dfa2088c140a010")]
    [PortOf("uc:SBase.SNoiseEmitter:PostBeginPlay", Spec = "spec/equipment/alarm-snare.md#rules")]
    protected override void PostBeginPlay()
    {
        if (Level.NetMode != ENetMode.NM_DedicatedServer)
        {
            SetTimer(K.SnareFirstNoise, false);
            fSpeed = K.SnareStartSpeed;
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            iStartedFakeTime = SvmMath.Trunc(Level.TimeSeconds);
            FakeAlarms();
        }
    }

    /// <summary>
    /// Security devices within fMaxDistToJam in sight of the snare (lasers with a beam, cameras, volumetric sensors):
    /// the first alarm bound to each one goes off as if a spy were seen.
    /// </summary>
    [Uc("SBase.SNoiseEmitter.FakeAlarms", Sha1 = "567ba5adc82f1218c2b416c98c3308ba5a8d139f")]
    [PortOf("uc:SBase.SNoiseEmitter:FakeAlarms", Spec = "spec/equipment/alarm-snare.md#rules")]
    public void FakeAlarms()
    {
        foreach (Actor a in XLevel.AllActors<Actor>())
        {
            if ((Location - a.Location).Size() >= fMaxDistToJam)
            {
                continue;
            }

            if (a is not (SLaserTrigger { Laser: not null } or SCamera or SVolumetricSensor))
            {
                continue;
            }

            a.Rotation.GetAxes(out FVector x, out _, out _);
            if (MovementNatives.Trace(this, out _, out _, a.Location + x * K.SnareDeviceProbe, Location, false) is not null)
            {
                continue;
            }

            foreach (SAlarm alarm in XLevel.AllActors<SAlarm>(a.Event))
            {
                alarm.eventSimulateActivated(a, FirstNonMercPawn());
                iTriggeredAlarmDuration = alarm.Duration;
                break;
            }
        }
    }

    /// <summary>The pawn FakeAlarms names as the detected one: the first that is not a merc, or none.</summary>
    private SBasePawn? FirstNonMercPawn()
    {
        foreach (SBasePawn p in XLevel.AllActors<SBasePawn>())
        {
            if (p.SPawnType != PawnType.SPAWNDEFENSE)
            {
                return p;
            }
        }

        return null;
    }

    /// <summary>A merc (or anyone not a spy) playing on this machine gets a flickering sound arrow at the snare.</summary>
    [Uc("SBase.SNoiseEmitter.Tick", Sha1 = "b73cca961b8421d132c2b1c05171905ac1f7a4bf")]
    [PortOf("uc:SBase.SNoiseEmitter:Tick", Spec = "spec/equipment/alarm-snare.md#rules")]
    protected override void Tick(float DeltaTime)
    {
        if (Level.GetLocalPlayerController() is SPlayerController pc && pc.SPawnType != PawnType.SPAWNOFFENSE)
        {
            fVal += FRand() * K.SnareArrowStep;
            if (fVal > 1f)
            {
                fVal = FRand() * K.SnareArrowRestart;
            }

            pc.ClientNotifyNoise(this, fVal);
        }

        base.Tick(DeltaTime);
    }

    /// <summary>
    /// Off a dedicated server: a breath or a footstep and its noise, the next one 40 / fSpeed seconds later. On the
    /// authority: the alarms again once the last one has run out, for the first iHackFakeDuration seconds.
    /// </summary>
    [Uc("SBase.SNoiseEmitter.Timer", Sha1 = "c0dee49609ee50800ac62b9fb262c9d078707173")]
    [PortOf("uc:SBase.SNoiseEmitter:Timer", Spec = "spec/equipment/alarm-snare.md#rules")]
    protected override void Timer()
    {
        if (Level.NetMode != ENetMode.NM_DedicatedServer)
        {
            if (FRand() < K.SnareBreathChance)
            {
                int action = SvmMath.Trunc(FRand() * K.SnareBreathPick);
                NotifyNoise(fNoiseCoeff);
                PlaySound(ActionSounds[action].SoundResource, ESoundSlot.SLOT_Misc, 1f, false, ActionSounds[action].Radius);
                fSpeed = FRand() * K.SnareSpeedRange + K.SnareSpeedMin;
            }
            else
            {
                NotifyNoise(fNoiseCoeff * fSpeed / K.SnareNoiseSpeed);
                PlaySound(WalkingSounds[SvmMath.Trunc(fSpeed / K.SnareFootstepBand)], ESoundSlot.SLOT_Misc, 1f, false, K.SnareFootstepRadius);
            }

            SetTimer(K.SnareStepDistance / fSpeed, true);
        }

        if (Role == ENetRole.ROLE_Authority && Level.TimeSeconds - iLastFakeUpdateTime > iTriggeredAlarmDuration + Rand(K.SnareFakeJitter) &&
            Level.TimeSeconds - iStartedFakeTime < iHackFakeDuration)
        {
            iLastFakeUpdateTime = SvmMath.Trunc(Level.TimeSeconds);
            FakeAlarms();
        }
    }

    /// <summary>The owner's "noise emitter off" text, where his HUD is on this machine.</summary>
    [Uc("SBase.SNoiseEmitter.Destroyed", Sha1 = "4e946016429707177e42fef945a67b593d005ce4")]
    [PortOf("uc:SBase.SNoiseEmitter:Destroyed", Spec = "spec/equipment/alarm-snare.md#rules")]
    protected override void Destroyed()
    {
        if (Owner is { myHUD: not null } pc)
        {
            pc.AddMessageOnHud(K.SnareHudOff);
        }
    }
}
