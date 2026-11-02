using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGadTorcheLaser: the merc laser sight (spec/equipment/merc-laser.md). While on it moves the shot origin to
/// the gun; held on an enemy for TimeToMark seconds it marks him (the spy bullet).
/// </summary>
public partial class SGadTorcheLaser
{
    private static readonly Name CNAME_TorcheON = new("TorcheON");
    private static readonly Name CNAME_TorcheOFF = new("TorcheOFF");
    private static readonly Name CNAME_ThrowSpyBullet = new("ThrowSpyBullet");
    private static readonly ClassRef GameMessages = new("SBase.SGameMessages");

    /// <summary>The toggle (rule 3): needs the laser option; the messages are skipped for a temporary switch.</summary>
    [Uc("SBase.SGadTorcheLaser.Use", Sha1 = "1881faeeef3f18347a799e1a4770be3a665a23e6")]
    [PortOf("uc:SBase.SGadTorcheLaser:Use", Spec = "spec/equipment/merc-laser.md#rules")]
    public override void Use(float Value)
    {
        GameReplicationInfo? gri = (OwnerPawn?.Controller as PlayerController)?.GameReplicationInfo ?? Level.Game?.GameReplicationInfo;
        if (gri is { bAvlb_MERC_Laser: false })
        {
            return;
        }

        bool quiet = OwnerPawn?.Controller is SPlayerController { bLaserEtaitAllume: true };
        if (bInUse)
        {
            if (!quiet)
            {
                LocalMessage(TextLaserOff, K.TorchMessageTime);
            }

            TorcheOFF();
        }
        else
        {
            if (!quiet)
            {
                LocalMessage(TextLaserOn, K.TorchMessageTime);
            }

            TorcheON();
        }
    }

    /// <summary>The native-callable Use.</summary>
    [Uc("SBase.SGadTorcheLaser.EventUse", Sha1 = "d5dc09d05f2c0198b4b85db06f554609bfa13c3e")]
    [PortOf("uc:SBase.SGadTorcheLaser:EventUse", Spec = "spec/equipment/merc-laser.md#rules")]
    public void EventUse(float _value) => Use(_value);

    /// <summary>The laser on (server): the torch off first, the sound, the merc's laser flag.</summary>
    [Uc("SBase.SGadTorcheLaser.TorcheON", Sha1 = "cb49738582ddfbbe8054d88b72079004118b4cb5")]
    [PortOf("uc:SBase.SGadTorcheLaser:TorcheON", Spec = "spec/equipment/merc-laser.md#rules")]
    public void TorcheON()
    {
        if (ServerRelay(CNAME_TorcheON))
        {
            return;
        }

        if (OwnerPawn is SPAWNDEFENSE merc)
        {
            if (merc.Torche is { bInUse: true } torch)
            {
                torch.TorcheOFF();
            }

            PawnWeapons.LaserON(merc);
        }

        PlaySound(SndUse, ESoundSlot.SLOT_None, null, false, K.WeaponSoundRadius);
        bInUse = true;
    }

    /// <summary>The laser off (server).</summary>
    [Uc("SBase.SGadTorcheLaser.TorcheOFF", Sha1 = "dd7bba1bec462f2c2623bdffe0f07a88316b13b6")]
    [PortOf("uc:SBase.SGadTorcheLaser:TorcheOFF", Spec = "spec/equipment/merc-laser.md#rules")]
    public void TorcheOFF()
    {
        if (ServerRelay(CNAME_TorcheOFF))
        {
            return;
        }

        PlaySound(SndUse, ESoundSlot.SLOT_None, null, false, K.WeaponSoundRadius);
        if (OwnerPawn is SPAWNDEFENSE merc)
        {
            PawnWeapons.LaserOFF(merc);
        }

        bInUse = false;
    }

    [Uc("SBase.SGadTorcheLaser.Unload", Sha1 = "900dca9a9e2981509c039f58194b129c73d51c0f")]
    [PortOf("uc:SBase.SGadTorcheLaser:Unload", Spec = "spec/equipment/merc-laser.md#rules")]
    public override void Unload()
    {
        if (bInUse)
        {
            LocalMessage(TextLaserOff, K.TorchMessageTime);
        }

        TorcheOFF();
    }

    /// <summary>
    /// Every tick off a dedicated server (rule 8): the beam (native), and on the merc's own machine the mark once the
    /// beam stayed on the same enemy for TimeToMark whole seconds; the mark target is dropped while the laser is off.
    /// </summary>
    [Uc("SBase.SGadTorcheLaser.Tick", Sha1 = "bae8c8a90518f6b13438d7d4ec37bac369eacf6f")]
    [PortOf("uc:SBase.SGadTorcheLaser:Tick", Spec = "spec/equipment/merc-laser.md#rules")]
    protected override void Tick(float DeltaTime)
    {
        // A server bot owns its input locally and must run the same beam dwell/mark logic as a human client.
        if (Level.NetMode == ENetMode.NM_DedicatedServer && OwnerPawn?.Controller is not PlayerController { Player: OpenScct.Game.AI.AiViewport })
        {
            return;
        }

        NativeTickTorcheLaser(DeltaTime);
        if (!bInUse || OwnerPawn is not SBasePawn pawn || !(pawn.WeaponLoaded == 2 || pawn.bUsingCamNetwork))
        {
            TimeTarget = 0;
            TargetPawn = null;
            return;
        }

        if (pawn.Controller is PlayerController { IsRemotePlayer: false } && TimeTarget != 0 &&
            TargetPawn is { Health: > 0 } target && target.SPawnType != pawn.SPawnType &&
            Level.TimeSeconds - TimeTarget > TimeToMark)
        {
            ThrowSpyBullet(target);
            TimeTarget = 0;
        }
    }

    /// <summary>
    /// The beam and the mark target (natives 1299 and 1297; placement decoded at game 0x10a98e00). The beam starts at
    /// the rifle's FX bone pulled back 50 uu and runs toward a point 15000 uu along the FX direction from the merc's
    /// location raised by 55.5 uu (48.29 crouched); inside a network camera it starts 20 uu ahead, 11 uu aside and
    /// 11 uu above the camera and runs along its view. Actors bullets pass (bBulletGoTru) do not stop it. The first
    /// living pawn on it becomes the mark target, stamped in whole seconds when it changes; anything else clears it.
    /// </summary>
    [UcNative("ASGadTorcheLaser::execNativeTickTorcheLaser", Exe = 0x10a99cc0)]
    [UcNative("ASGadTorcheLaser::execNativeReplaceLaserLocal", Exe = 0x10a98de0)]
    [PortOf("nat:SBase.SGadTorcheLaser.NativeTickTorcheLaser", Spec = "spec/equipment/merc-laser.md#rules")]
    [PortOf("nat:SBase.SGadTorcheLaser.NativeReplaceLaserLocal", Spec = "spec/equipment/merc-laser.md#rules")]
    public void NativeTickTorcheLaser(float dt)
    {
        if (!bInUse || OwnerPawn is not SBasePawn pawn)
        {
            return;
        }

        FVector start;
        FVector end;
        if (pawn.bUsingCamNetwork && pawn.UsedObject is SCamNetwork cam)
        {
            cam.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
            start = cam.Location + (x * K.CamLaserForward) - (y * K.CamLaserSide) + (z * K.CamLaserSide);
            end = start + (x * K.TraceLength);
        }
        else if (pawn.WeaponLoaded == 2 && pawn.MyWeapon is SWeaponDefense rifle)
        {
            FCoords fx = CombatServices.Ports(this).BoneCoords(rifle, SWeaponDefense.FxBone);
            start = fx.Origin - (fx.XAxis * K.LaserFireStartPullBack);
            FVector dir = fx.XAxis.Rotation().Vector();
            float raise = pawn.bIsCrouched ? K.LaserEndRaiseCrouched : K.LaserEndRaiseStanding;
            end = pawn.Location + (dir * K.TraceLength) + new FVector(0f, 0f, raise);
        }
        else
        {
            return;
        }

        BeamStart = start;
        BoneTraceResult hit = CombatServices.HitZones(this).TraceBone(pawn.MyWeapon ?? (Actor)pawn, end, start, false);
        HitLocation = hit.bHit ? hit.HitLocation : end;
        if (hit.Actor is SBasePawn { Health: >= 1 } spotted && !ReferenceEquals(spotted, pawn))
        {
            if (!ReferenceEquals(spotted, TargetPawn))
            {
                TargetPawn = spotted;
                TimeTarget = SvmMath.Trunc(Level.TimeSeconds);
            }
        }
        else
        {
            TargetPawn = null;
            TimeTarget = 0;
        }
    }

    /// <summary>Where the beam started on its last placement (the view draws it from here to <see cref="HitLocation"/>).</summary>
    public FVector BeamStart { get; private set; }

    /// <summary>The mark (owner to server, rule 10): a living target is marked for the mercs, who are told.</summary>
    [Uc("SBase.SGadTorcheLaser.ThrowSpyBullet", Sha1 = "a8cd487ebc11df33cf73e7de28f686417f3516c1")]
    [PortOf("uc:SBase.SGadTorcheLaser:ThrowSpyBullet", Spec = "spec/equipment/merc-laser.md#rules")]
    public void ThrowSpyBullet(SBasePawn? TargetPawnSpy)
    {
        if (Role < ENetRole.ROLE_Authority && OwnerPawn?.Controller is PlayerController pc)
        {
            CombatServices.Net(this).ServerCall(pc, new CombatCall(CNAME_ThrowSpyBullet) { Target = this, Actor1 = TargetPawnSpy });
            return;
        }

        if (TargetPawnSpy is not { Health: > 0 } target)
        {
            return;
        }

        target.HitBySpyBullet(1);
        if (target is SPawnAttaque && OwnerPawn?.Controller?.PlayerReplicationInfo?.Stats is { } stats)
        {
            stats.AddFeat(FeatKeys.MercMarks); // OpenSCCT's own, for achievements
        }

        (Level.Game as SGameInfo)?.BroadcastTeamLocalizedMessage(1, GameMessages, K.MsgSpyMarked, target.PlayerReplicationInfo);
    }

    [Uc("SBase.SGadTorcheLaser.Destroyed", Sha1 = "b90af98c62b1f999e00eff82245b336aaeaa6e35")]
    [PortOf("uc:SBase.SGadTorcheLaser:Destroyed", Spec = "spec/equipment/merc-laser.md#rules")]
    protected override void Destroyed()
    {
        LaserBeam?.Destroy();
        LaserBeam = null;
        base.Destroyed();
    }

    private bool ServerRelay(Name Function)
    {
        if (Role < ENetRole.ROLE_Authority && OwnerPawn?.Controller is PlayerController pc)
        {
            CombatServices.Net(this).ServerCall(pc, new CombatCall(Function) { Target = this });
            return true;
        }

        return false;
    }
}
