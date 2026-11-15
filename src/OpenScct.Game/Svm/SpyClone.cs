using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;

namespace OpenScct.Game.Svm;

/// <summary>A shootable Spy likeness with no controller, player input, or equipment.</summary>
public sealed class SpyClone : SBasePawn
{
    private static readonly SPawnAttaque SpyDefaults = new SPawnAttaque_1Mesh();
    private static readonly SProjFlashBang FlashDefaults = new();
    private const float WallLinger = 2f;
    private const float StepHeight = 22f;
    private const float Gravity = 950f;

    public bool Stopped;
    public int GlowMode;
    public bool WetLook;
    public byte CloakLook;
    public bool PistolDrawn;
    private float _fallSpeed;

    public SpyClone()
    {
        SPawnType = PawnType.SPAWNOFFENSE;
        Health = 0;
        Physics = EPhysics.PHYS_None;
        // Bullets find the body through TraceBone. A decoy never blocks players or touches triggers.
        bCollideActors = bBlockActors = bBlockPlayers = false;
        bCollideWorld = true;
        bAlwaysRelevant = true;
        bReplicateMovement = false; // The clone has its own compact, interpolated position and velocity fields.
        NetUpdateFrequency = 30f;
        Mesh = SpyDefaults.Mesh;
        BoneMirrors.AddRange(SpyDefaults.BoneMirrors);
        FootStepsVolume = SpyDefaults.FootStepsVolume;
        EMFOpacity = SpyDefaults.EMFOpacity;
        HeatIntensity = SpyDefaults.HeatIntensity;
        LifeSpan = WallLinger; // The countdown only runs once the clone stops.
    }

    public void CopyAppearance(SPawnAttaque spy)
    {
        PlayerReplicationInfo = spy.PlayerReplicationInfo;
        WetLook = spy.bWet;
        CloakLook = spy.GlassEffect;
        GlowMode = PawnVision.SpyGlowMode(spy);
        PistolDrawn = spy.WeaponLoaded != 0;
    }

    protected override void PostBeginPlay() => PawnVision.SpawnGoggleBeam(this);

    protected override void NativeTick(float deltaSeconds)
    {
        if (Role == ENetRole.ROLE_Authority)
        {
            if (!Stopped) MoveStraight(deltaSeconds);
            else if ((LifeSpan -= deltaSeconds) <= 0f) Destroy();
            if (bDeleteMe) return;
        }

        bWet = WetLook;
        DesiredGlassEffect = GlassEffect = CloakLook;
        if (GoggleBeam is { } glow && glow.CurrentMode != GlowMode)
        {
            glow.CurrentMode = GlowMode;
            glow.ChangeSkin();
        }

        Name sequence = Stopped ? PistolDrawn ? AWaitSt2 : AWaitSt0 : PistolDrawn ? ARunStFd2 : ARunStFd0;
        if (PawnAnim[0].Sequence != sequence)
        {
            PawnActions.Loop(this, 0, sequence, 0.1f);
            PawnAnim.EnableChannelNotify(0, Role == ENetRole.ROLE_Authority);
        }

        AdvancePredictionAnimation(deltaSeconds);
        XLevel.GetService<Combat.IPillPose>()?.UpdatePose(this);
    }

    private void MoveStraight(float deltaSeconds)
    {
        Velocity = Rotation.Vector() * RunningSpeed;
        FVector delta = Velocity * deltaSeconds;
        if (XLevel.PhysicsDriver is not PhysicsDriver physics)
        {
            Location += delta;
            return;
        }

        // Follow stairs in the same heading. A wall truncates the sweep without sliding along it.
        var rise = new FVector(0f, 0f, StepHeight);
        if (physics.TraceMove(this, Location, delta, out _, bIgnorePawns: true) &&
            !physics.TraceMove(this, Location, rise, out _, bIgnorePawns: true) &&
            !physics.TraceMove(this, Location + rise, delta, out _, bIgnorePawns: true))
            physics.MoveActor(this, rise);

        physics.MoveActor(this, delta, Rotation, out FCheckResult hit, bIgnorePawns: true);
        if (hit.bHit)
        {
            Stopped = true;
            Velocity = FVector.Zero;
            return;
        }

        _fallSpeed += Gravity * deltaSeconds;
        FVector down = new(0f, 0f, -_fallSpeed * deltaSeconds);
        if (physics.TraceMove(this, Location, -rise, out FCheckResult floor, bIgnorePawns: true) && floor.Normal.Z >= 0.7f)
            down = -rise;

        physics.MoveActor(this, down, Rotation, out floor, bIgnorePawns: true);
        if (floor.bHit) _fallSpeed = 0f;
        if (Location.Z < physics.Collision.KillZ) Destroy();
    }

    /// <summary>The hit position and the visual effect are authoritative for both Mercs.</summary>
    public void ShotBy(Pawn shooter)
    {
        if (Role != ENetRole.ROLE_Authority || bDeleteMe || shooter is not SPAWNDEFENSE) return;

        FVector flash = Location + new FVector(0f, 0f, EyeHeight);
        foreach (SPlayerController pc in XLevel.AllActors<SPlayerController>())
        {
            if (pc.SBPawn is not SPAWNDEFENSE { Health: > 0, bDeleteMe: false } merc) continue;

            FVector eye = merc.Location + new FVector(0f, 0f, merc.EyeHeight) + merc.WalkBob;
            FVector toFlash = flash - eye;
            if (toFlash.SizeSquared() >= FlashDefaults.Range * FlashDefaults.Range ||
                (toFlash.SafeNormal() | merc.Rotation.Vector()) <= pc.DES_fSeuilFlashBang_Cos ||
                GadgetWorld.Line(merc, flash, eye, (ETraceFlags)K.FlashSightFlags, out _)) continue;

            pc.ApplyCloneFlash();
        }

        PlaySound(FlashDefaults.SoundExplode, ESoundSlot.SLOT_Misc, K.FlashSoundVolume, false, K.FlashSoundRadius);
        XLevel.SpawnActor(new SFlashBang(), flash, Rotation);
        Destroy();
    }
}
