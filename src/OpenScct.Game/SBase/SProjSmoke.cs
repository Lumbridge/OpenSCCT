using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SProjSmoke: the spy smoke grenade (spec/equipment/smoke-grenade.md rules 1 and 2). When it pops it leaves
/// an SSmokeEmitter cloud at the spot, raised by its collision height plus 1 uu.
/// </summary>
public partial class SProjSmoke
{
    /// <summary>The launch sound unless quick-launched; the trail emitter is a view effect.</summary>
    [Uc("SBase.SProjSmoke.PostBeginPlay", Sha1 = "96c1c74cc252a17a52f0d3b2cca7e91a94dcc859")]
    [PortOf("uc:SBase.SProjSmoke:PostBeginPlay", Spec = "spec/equipment/smoke-grenade.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (!QuickLaunch)
        {
            PlaySound(SoundLaunch, ESoundSlot.SLOT_None, null, false, K.GrenadeSoundRadius);
        }
    }

    /// <summary>
    /// HitWall (native override, decoded game 0x10ac1c60): an aimed smoke grenade bounces as every grenade does; a
    /// quick-launched one pops at its first hit, floors included (nothing more once it has exploded).
    /// </summary>
    [UcNative("ASProjSmoke::HitWall", Exe = 0x10ac1c60)]
    [PortOf("nat:SBase.SProjGrenade.HitWall", Spec = "spec/equipment/grenade-throw.md#rules")]
    protected override void HitWall(FVector HitNormal, Actor? Wall)
    {
        if (!QuickLaunch)
        {
            base.HitWall(HitNormal, Wall);
        }
        else if (!Exploded)
        {
            QuickLaunchPop();
        }
    }

    /// <summary>
    /// ProcessTouch (native override, decoded game 0x10ac1cb0): touching a pawn other than the thrower, the server
    /// stops the grenade and lets the cloud out where it is; a client copy goes.
    /// </summary>
    [UcNative("ASProjSmoke::ProcessTouch", Exe = 0x10ac1cb0)]
    [PortOf("nat:Engine.Projectile.NativeTouch", Spec = "spec/equipment/smoke-grenade.md#rules")]
    protected override void Touch(Actor Other) => PawnTouchPop(Other, Location);

    /// <summary>Explode (native, once): the smoke cloud with its looping sound, then the grenade is gone.</summary>
    [UcNative("ASProjSmoke::Explode", Exe = 0x10ac1b20)]
    [PortOf("nat:Engine.Projectile.Explode", Spec = "spec/equipment/smoke-grenade.md#rules")]
    public override void Explode(FVector HitLocation, FVector HitNormal)
    {
        if (Exploded)
        {
            return;
        }

        Exploded = true;
        PlaySound(SoundExplode, ESoundSlot.SLOT_None, null, false, K.GrenadeSoundRadius);
        var at = new FVector(HitLocation.X, HitLocation.Y, HitLocation.Z + CollisionHeight + K.SmokeRaise);
        Smoke = XLevel.Spawn<SSmokeEmitter>(at, default, null, Instigator);
        if (Smoke is { } cloud)
        {
            cloud.Instigator = Instigator;
            cloud.SoundOn = true;
            cloud.ToggleSoundOn();
        }

        Destroy();
    }
}
