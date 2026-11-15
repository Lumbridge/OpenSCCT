using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.Objectives;
using MK = OpenScct.Game.Modes.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// Source PS2 class 179930 (COM 0x18A75F): a placed, shootable modem. The existing
/// runtime class is registered with the inherited SGamePlayObject layout; mesh,
/// skin, ambient sound, collision and destruction use ordinary actor replication.
/// </summary>
public sealed class Ps2Modem : SGamePlayObject
{
    public SBombTargetObjectiveTrigger? Target;
    public AssetRef ActiveSound;
    public ClassRef DestroyedEmitter;
    public AssetRef SmokeTexture;
    public AssetRef FireTexture;
    private bool _waitingForUnlock;

    protected override void SetDefaults()
    {
        base.SetDefaults();
        bStatic = false;
        bNoDelete = false;
        bDamageable = true;
        HitPoints = 1;
        HeatIntensity = 1f;
        EMFIntensity = 1f;
        LifeSpan = 180f;
        SoundRadius = 16f;
        SoundVolume = 255;
        DrawType = EDrawType.DT_StaticMesh;
        bBlockActors = true;
        bBlockPlayers = false;
        bSkipActorPropertyReplication = false;
        InteractionClass = new ClassRef("SBase.SBombInteraction");
    }

    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (Target is not { } target) return;
        target.ControllerWhoTakesIt = (Owner as Pawn)?.Controller as PlayerController;
        SetOwner(null);
        AmbientSound = ActiveSound;
        if (Role == ENetRole.ROLE_Authority)
        {
            _waitingForUnlock = target.IsInState(K.s_TemporaryInactive);
            if (_waitingForUnlock) SetTimer(1f, true);
            else { target.eventTriggering(true); SetTimer(target.CaptureTime, false); }
        }
        ObjectiveMessages.Broadcast(this, MK.MsgBombUnderAttack, target);
    }

    protected override void Timer()
    {
        if (Role != ENetRole.ROLE_Authority || Target is not { bDeleteMe: false } target) return;
        if (_waitingForUnlock)
        {
            if (!target.IsInState(K.s_Initial)) return;
            _waitingForUnlock = false;
            target.eventTriggering(true);
            SetTimer(target.CaptureTime, false);
            return;
        }
        if (target.bActive && target.Ps2Contains(Location)) target.eventTakeObjective();
        eventTakeDamage(InitialHitPoints, target.ControllerWhoTakesIt?.Pawn, Location, FVector.Zero, SDamageType.SDamage_Explosion);
    }

    protected override void TakeDamage(int damage, Pawn? instigator, FVector hit, FVector momentum, SDamageType type, int pillTag = 0)
    {
        if (Role != ENetRole.ROLE_Authority || bDeleteMe) return;
        StopCapture();
        base.TakeDamage(damage, instigator, hit, momentum, type, pillTag);
        if (!SmokeTexture.IsNone && !FireTexture.IsNone)
            XLevel.SpawnActor(new Ps2ModemEffect { SmokeTexture = SmokeTexture, FireTexture = FireTexture }, Location, Rotation, this, Instigator);
        else if (!DestroyedEmitter.IsNone) DeviceClasses.Spawn(this, DestroyedEmitter, this, Name.None, Location, Rotation);
        Destroy();
    }

    private void StopCapture()
    {
        if (Target is not { bActive: true } target || !target.Ps2Contains(Location)) return;
        ObjectiveMessages.Broadcast(this, MK.MsgBombSecured, target);
        target.eventTriggering(false);
    }

    protected override void Destroyed()
    {
        if (Role == ENetRole.ROLE_Authority) StopCapture();
        InteractionObj?.Destroy();
        base.Destroyed();
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        Target = Live(Target);
    }
}
