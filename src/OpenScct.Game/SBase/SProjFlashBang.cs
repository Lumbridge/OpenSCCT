using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SProjFlashBang: the spy's flash-bang grenade (spec/equipment/flash-bang.md). Aimed it bounces like every
/// grenade until the fuse, quick-launched it pops at its first hit, and it goes off when it touches a pawn other
/// than its thrower. When it goes off every merc within Range who faces it and has a clear line to it is flashed
/// (a zero-damage SDTFlash hit: the after-image, the white-out and the whistle on his machine), every player hears
/// the bang and the SFlashBang flare is left at the spot.
/// </summary>
public partial class SProjFlashBang
{
    /// <summary>The launch sound unless quick-launched; the launch trail (SSmokeLaunchEmitter) is a view effect.</summary>
    [Uc("SBase.SProjFlashBang.PostBeginPlay", Sha1 = "53403a48337c491ec68a8e19cf204ee5481d528a")]
    [PortOf("uc:SBase.SProjFlashBang:PostBeginPlay", Spec = "spec/equipment/flash-bang.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (QuickLaunch)
        {
            return;
        }

        PlaySound(SoundLaunch, ESoundSlot.SLOT_None, 1f, false, K.GrenadeSoundRadius);
    }

    /// <summary>
    /// HitWall (native override shared with the chaff grenade, decoded game 0x10ab8760): a quick-launched flash-bang
    /// goes off at its first hit, floors included; an aimed one bounces.
    /// </summary>
    [UcNative("ASProjFlashBang::HitWall", Exe = 0x10ab8760)]
    [PortOf("nat:SBase.SProjGrenade.HitWall", Spec = "spec/equipment/grenade-throw.md#rules")]
    protected override void HitWall(FVector HitNormal, Actor? Wall)
    {
        if (!QuickLaunchPop())
        {
            base.HitWall(HitNormal, Wall);
        }
    }

    /// <summary>
    /// ProcessTouch (native override, decoded game 0x10ab87b0): touching a pawn other than the thrower, the server
    /// stops the grenade and sets it off 100 uu back along its flight; a client copy goes.
    /// </summary>
    [UcNative("ASProjFlashBang::ProcessTouch", Exe = 0x10ab87b0)]
    [PortOf("nat:Engine.Projectile.NativeTouch", Spec = "spec/equipment/flash-bang.md#rules")]
    protected override void Touch(Actor Other) =>
        PawnTouchPop(Other, Location - (Velocity.SafeNormal() * K.FlashTouchPullBack));

    /// <summary>
    /// Explode (native, decoded game 0x10ab83d0). The launch trail goes. Each SPlayerController playing a merc
    /// (SPawnType 1) whose SBPawn stands within Range of the grenade is tested from that pawn's eye (Location,
    /// EyeHeight up, WalkBob): when the grenade lies within the controller's DES_fSeuilFlashBang_Cos of the pawn's
    /// facing and a line from the grenade to the pawn's location (flags 0x80086) is clear, the controller gets
    /// PawnTakeDamage(0, Instigator, Location, 0, SDTFlash, 0, PercentDamageRefillable). Then every player hears the
    /// bang (volume 1, radius 100), the SFlashBang flare is spawned at HitLocation (turned to the rotator of that
    /// vector, as the native does) with the grenade's owner and instigator, and the grenade is destroyed.
    /// </summary>
    [UcNative("ASProjFlashBang::Explode", Exe = 0x10ab83d0)]
    [PortOf("nat:Engine.Projectile.Explode", Spec = "spec/equipment/flash-bang.md#rules")]
    public override void Explode(FVector HitLocation, FVector HitNormal)
    {
        SmokeLaunchEmitter = null;
        float range = Range;
        foreach (SPlayerController pc in XLevel.AllActors<SPlayerController>())
        {
            if (pc.bDeleteMe || pc.SPawnType != PawnType.SPAWNDEFENSE || pc.SBPawn is not { bDeleteMe: false } merc)
            {
                continue;
            }

            if (!((merc.Location - Location).Size() < range))
            {
                continue;
            }

            if (FlashSeenBy(pc, merc))
            {
                pc.PawnTakeDamage(0, Instigator, Location, FVector.Zero, SDamageType.SDTFlash, 0, PercentDamageRefillable);
                CountFlash(pc);
            }
        }

        PlaySound(SoundExplode, ESoundSlot.SLOT_Misc, K.FlashSoundVolume, false, K.FlashSoundRadius);
        XLevel.SpawnActor(new SFlashBang(), HitLocation, HitLocation.Rotation(), Owner, Instigator);
        Destroy();
    }

    /// <summary>OpenSCCT's own, for achievements: counts a flashed enemy merc for the throwing spy (server only).</summary>
    private void CountFlash(SPlayerController victim)
    {
        if (Role != ENetRole.ROLE_Authority || Instigator?.Controller is not { } thrower ||
            SGameInfo.TeamIndexOf(thrower) == SGameInfo.TeamIndexOf(victim))
        {
            return;
        }

        thrower.PlayerReplicationInfo?.Stats?.AddFeat(FeatKeys.SpyFlashes);
    }

    /// <summary>The server's facing and sight test of one merc (Explode, game 0x10ab8512 to 0x10ab8637).</summary>
    private bool FlashSeenBy(SPlayerController PC, SBasePawn Merc)
    {
        FVector eye = Merc.Location + new FVector(0f, 0f, Merc.EyeHeight) + Merc.WalkBob;
        FVector toFlash = (Location - eye).SafeNormal();
        if (!((Merc.Rotation.Vector() | toFlash) > PC.DES_fSeuilFlashBang_Cos))
        {
            return false;
        }

        return !Equipment.GadgetWorld.Line(Merc, Merc.Location, Location, (OpenScct.Physics.Collision.ETraceFlags)K.FlashSightFlags, out _);
    }
}
