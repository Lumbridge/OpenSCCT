using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SSpyBulletLauncher: the spy bullet (spec/equipment/spy-bullet.md rules 1 to 5). The owner's machine aims from
/// the drawn Shocker; the server traces the shot, marks the merc it hits or leaves a radar zone marker on a wall.
/// </summary>
public partial class SSpyBulletLauncher
{
    private static readonly Name CNAME_ThrowBullet = new("ThrowBullet");
    private static readonly Name CNAME_MyLocalMessage = new("MyLocalMessage");
    private static readonly Name BoneRightHand = new("B R Hand");

    private readonly List<FCheckResult> _hits = new();

    /// <summary>Alt-fire: the bullet is shot, the gadget rumbles.</summary>
    [Uc("SBase.SSpyBulletLauncher.Use", Sha1 = "e59fa2bc9c214995459d7c4a0c513cde5dc44824")]
    [PortOf("uc:SBase.SSpyBulletLauncher:Use", Spec = "spec/equipment/spy-bullet.md#rules")]
    public override void Use(float Value)
    {
        UseSpyBullet();
        (OwnerPawn?.Controller as SPlayerController)?.Vibration_Gadget(K.GadgetRumble);
        base.Use(Value);
    }

    /// <summary>
    /// UseSpyBullet (native 1295, decoded 0x10aa17d0): with the Shocker drawn and the refire time over, a shot from
    /// the right hand 15000 uu along its X axis goes to the server (ThrowBullet). No charge is taken here.
    /// </summary>
    [UcNative("ASSpyBulletLauncher::execUseSpyBullet", Exe = 0x10aa17d0)]
    [PortOf("nat:SBase.SSpyBulletLauncher.UseSpyBullet", Spec = "spec/equipment/spy-bullet.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "spy-bullet.md rule 2: mesh, weapon and WeaponLoaded, refire, empty sound, LastUsed, 'B R Hand' + 30 X - 4 Y + 8 Z, 15000 uu")]
    [Provenance(ProvenanceKind.Estimated, "The hand pose is the drawn Shocker's estimated pose (CombatPorts.BoneCoords) until skeletal poses run")]
    public void UseSpyBullet()
    {
        // The original also needs the pawn's mesh; the port's pawns do not always name one, so that test is left out.
        if (OwnerPawn is not { } pawn || pawn.MyWeapon is not { } weapon || pawn.WeaponLoaded == 0 ||
            Level.TimeSeconds - LastUsed < RefireRate)
        {
            return;
        }

        if (Charge < 1)
        {
            PlaySound(SND_EmptyCharge, ESoundSlot.SLOT_Misc, 1f, false, K.EmptyChargeRadius);
            return;
        }

        LastUsed = Level.TimeSeconds;

        // The original reads the pawn's hand bone; without a skeleton the drawn weapon's estimate follows the aim.
        FCoords hand = CombatServices.Ports(this).BoneCoords(weapon, BoneRightHand);
        FVector origin = hand.Origin + hand.XAxis * K.SpyBulletHandForward + hand.YAxis * K.SpyBulletHandRight +
            hand.ZAxis * K.SpyBulletHandUp;
        ThrowBullet(origin, origin + hand.XAxis * K.SpyBulletRange);
    }

    /// <summary>
    /// ThrowBullet (native 1296, decoded 0x10aa1a00; client to server): the first hit along the shot that is not a
    /// laser projector decides. A living pawn is marked (a merc also takes himself out of the zone markers around him
    /// and the spies are told); anything else but a mover gets a zone marker. Either way the charge goes and the shot
    /// is heard. Nothing hit, a mover or a dead pawn: no charge.
    /// </summary>
    [UcNative("ASSpyBulletLauncher::execThrowBullet", Exe = 0x10aa1a00)]
    [PortOf("nat:SBase.SSpyBulletLauncher.ThrowBullet", Spec = "spec/equipment/spy-bullet.md#rules")]
    [PortOf("rep:SBase.SSpyBulletLauncher.ThrowBullet", Spec = "spec/equipment/spy-bullet.md#network")]
    [Provenance(ProvenanceKind.Decoded, "spy-bullet.md rule 3: multi line 0xc10bf, mover stops, pawn marks, laser projector passes, else SProjMarqueurSpy; Charge-- on a pawn or wall")]
    [Provenance(ProvenanceKind.Estimated, "The owner's hit sound is an interface sound for him (the native calls his controller's sound hook)")]
    public void ThrowBullet(FVector GrOrigin, FVector GrRotation)
    {
        // GrRotation is the end point of the shot (UseSpyBullet passes origin + 15000 X).
        SBasePawn? shooter = OwnerPawn;
        if (GadgetNet.ToServer(this, shooter?.Controller as PlayerController, new CombatCall(CNAME_ThrowBullet)
        {
            Target = this,
            Vector1 = GrOrigin,
            Vector2 = GrRotation,
        }))
        {
            return;
        }

        if (shooter is null || GadgetWorld.Collision(this) is not { } world)
        {
            return;
        }

        _hits.Clear();
        world.MultiLineCheck(_hits, shooter, GrRotation, GrOrigin, FVector.Zero, SGadStickyCam.StickTraceFlags);
        foreach (FCheckResult hit in _hits)
        {
            if (!hit.bHit)
            {
                continue;
            }

            // A world hit comes without an actor here; the game reports the level.
            Actor a = GadgetWorld.HitActorOf(this, hit)!;
            if (a is Mover)
            {
                return;
            }

            if (a is SBasePawn target)
            {
                HitPawn(shooter, target);
                return;
            }

            if (a is not SLaserProjector)
            {
                HitWall(shooter, hit);
                return;
            }
        }
    }

    /// <summary>Rule 3, a pawn: marked when alive, the spies told of a merc, and his zone markers forget him.</summary>
    private void HitPawn(SBasePawn Shooter, SBasePawn Target)
    {
        if (Target.Health <= 0)
        {
            return;
        }

        Target.HitBySpyBullet(K.SpyBulletTeam);
        (Shooter.Controller as SPlayerController)?.ClientPlaySoundCombat(HitPawnSound);
        if (Target is SPAWNDEFENSE)
        {
            (Level.Game as SGameInfo)?.BroadcastTeamLocalizedMessage(0, SBasePawn.GameMessagesClass, K.SpyBulletMsgEnemyLocked);
            Shooter.Controller?.PlayerReplicationInfo?.Stats?.AddFeat(FeatKeys.SpyBulletHits); // OpenSCCT's own, for achievements
        }

        Shooter.PlaySound(FireSound, ESoundSlot.SLOT_Misc, 1f, false, K.SpyBulletSoundRadius);

        // The direct mark outlives the zone: a zone that drops him later must not clear it.
        if (Target is SPAWNDEFENSE merc && merc.bSpyBulletOn)
        {
            foreach (SProjMarqueurSpy zone in XLevel.AllActors<SProjMarqueurSpy>())
            {
                if (!zone.bDeleteMe && (zone.Location - merc.Location).Size() < zone.fDetectionRadius)
                {
                    zone.RemoveActorInList(merc);
                }
            }
        }

        Charge--;
    }

    /// <summary>Rule 3, anything else: a zone marker at the hit facing out along the normal, the owner's text.</summary>
    private void HitWall(SBasePawn Shooter, in FCheckResult Hit)
    {
        Spawn<SProjMarqueurSpy>(Shooter, Name.None, Hit.Location, Hit.Normal.Rotation());
        (Shooter.Controller as SPlayerController)?.ClientPlaySoundCombat(HitWallSound);
        LocalMessage(MessageOnWall, K.SpyBulletWallMessageTime);
        Shooter.PlaySound(FireSound, ESoundSlot.SLOT_Misc, 1f, false, K.SpyBulletSoundRadius);
        Charge--;
    }

    /// <summary>The native's message event: the owner's "zone radar activated" text.</summary>
    [Uc("SBase.SSpyBulletLauncher.LocalMessage", Sha1 = "760ae4e4357748e39d4000ea1b8b6966899d5f88")]
    [PortOf("uc:SBase.SSpyBulletLauncher:LocalMessage", Spec = "spec/equipment/spy-bullet.md#rules")]
    public new void LocalMessage(string Message, float Time) => MyLocalMessage();

    /// <summary>The owner's HUD shows MessageOnWall for 3 s (server to owner).</summary>
    [Uc("SBase.SSpyBulletLauncher.MyLocalMessage", Sha1 = "7bd2f5db23e9a2f87e6abd27bc9bd10dfcfd05c3")]
    [PortOf("uc:SBase.SSpyBulletLauncher:MyLocalMessage", Spec = "spec/equipment/spy-bullet.md#rules")]
    [PortOf("rep:SBase.SSpyBulletLauncher.MyLocalMessage", Spec = "spec/equipment/spy-bullet.md#network")]
    public void MyLocalMessage()
    {
        SPlayerController? pc = OwnerPawn?.Controller as SPlayerController;
        if (GadgetNet.ToOwner(pc, new CombatCall(CNAME_MyLocalMessage) { Target = this }))
        {
            return;
        }

        GadgetHud.AddMessageToStack(pc, MessageOnWall, K.SpyBulletWallMessageTime);
    }
}
