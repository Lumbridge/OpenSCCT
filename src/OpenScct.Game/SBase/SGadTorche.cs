using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGadTorche: the merc flashlight, a pad-up gadget toggled with its own key (spec/equipment/merc-flashlight.md).
/// The owner switches its local in-use flag at once; the server lights the spot (LT_Steady and the beam). A shock or
/// a chaff burst makes it flicker for a while.
/// </summary>
public partial class SGadTorche
{
    private static readonly Name CNAME_TorcheON = new("TorcheON");
    private static readonly Name CNAME_TorcheOFF = new("TorcheOFF");
    private static readonly Name CNAME_Malfunction = new("Malfunction");

    public static new readonly StateTable ClassStates = new(SGadget.ClassStates, s_Malfunction.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>Client copies point their instigator at the owner pawn; the light's normal influence is copied.</summary>
    [Uc("SBase.SGadTorche.PostNetBeginPlay", Sha1 = "f2dc9504e3e34716f7eb70f23a6377791ec11e7d")]
    [PortOf("uc:SBase.SGadTorche:PostNetBeginPlay", Spec = "spec/equipment/merc-flashlight.md#rules")]
    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        if (Owner is Pawn pawn)
        {
            Instigator = pawn;
        }

        EchelonNormalInfluence = WantedNormalInfluence;
    }

    /// <summary>The toggle (rule 4): needs the torch option; the local flag flips at once, the server lights it.</summary>
    [Uc("SBase.SGadTorche.Use", Sha1 = "e112759ad2de428b6ba83ea0ae88ed917d3b17c1")]
    [PortOf("uc:SBase.SGadTorche:Use", Spec = "spec/equipment/merc-flashlight.md#rules")]
    public override void Use(float Value)
    {
        GameReplicationInfo? gri = (OwnerPawn?.Controller as PlayerController)?.GameReplicationInfo ?? Level.Game?.GameReplicationInfo;
        if (gri is { bAvlb_MERC_Torche: false })
        {
            return;
        }

        if (bInUse)
        {
            LocalMessage(TextTorcheOff, K.TorchMessageTime);
            TorcheOFF();
            bInUse = false;
        }
        else
        {
            LocalMessage(TextTorcheOn, K.TorchMessageTime);
            TorcheON();
            bInUse = true;
        }
    }

    /// <summary>The torch on (server): the laser off first, the steady light and its beam, the switch sound.</summary>
    [Uc("SBase.SGadTorche.TorcheON", Sha1 = "de24df747273332f44eabdc4f61079288e385505")]
    [PortOf("uc:SBase.SGadTorche:TorcheON", Spec = "spec/equipment/merc-flashlight.md#rules")]
    public void TorcheON()
    {
        if (ServerRelay(CNAME_TorcheON))
        {
            return;
        }

        if (OwnerPawn is SPAWNDEFENSE { TorcheLaser: { bInUse: true } laser })
        {
            laser.TorcheOFF();
        }

        LightType = ELightType.LT_Steady;
        UsesLightBeam = true;
        PlaySound(SndUse, ESoundSlot.SLOT_None, null, false, K.WeaponSoundRadius);
        bInUse = true;
    }

    /// <summary>The torch off (server), when it is on.</summary>
    [Uc("SBase.SGadTorche.TorcheOFF", Sha1 = "922c5cbea1a7a2947d7cd2ec4dc47f8e946ba1cb")]
    [PortOf("uc:SBase.SGadTorche:TorcheOFF", Spec = "spec/equipment/merc-flashlight.md#rules")]
    public void TorcheOFF()
    {
        if (ServerRelay(CNAME_TorcheOFF) || !bInUse)
        {
            return;
        }

        LightType = ELightType.LT_None;
        UsesLightBeam = false;
        PlaySound(SndUse, ESoundSlot.SLOT_None, null, false, K.WeaponSoundRadius);
        bInUse = false;
    }

    /// <summary>Off (scope, vision modes, laser selection): the message when it was on, then off.</summary>
    [Uc("SBase.SGadTorche.Unload", Sha1 = "d85ebd732f4bfc79239535f6c682901185c5ec3c")]
    [PortOf("uc:SBase.SGadTorche:Unload", Spec = "spec/equipment/merc-flashlight.md#rules")]
    public override void Unload()
    {
        if (bInUse)
        {
            LocalMessage(TextTorcheOff, K.TorchMessageTime);
        }

        TorcheOFF();
        bInUse = false;
    }

    /// <summary>The flicker (client to server; local on the server): s_Malfunction for <paramref name="Time"/> seconds.</summary>
    [Uc("SBase.SGadTorche.Malfunction", Sha1 = "4a277a659a3f0595b7e08eecd01042eb0018131f")]
    [PortOf("uc:SBase.SGadTorche:Malfunction", Spec = "spec/equipment/merc-flashlight.md#rules")]
    public void Malfunction(float Time)
    {
        if (Role < ENetRole.ROLE_Authority && OwnerPawn?.Controller is PlayerController pc)
        {
            CombatServices.Net(this).ServerCall(pc, new CombatCall(CNAME_Malfunction) { Target = this, Float1 = Time });
            return;
        }

        GotoState(s_Malfunction.Instance.Name);
        SetTimer(Time, false);
    }

    /// <summary>The script tick runs the native placement every tick on every machine (rule 7).</summary>
    [Uc("SBase.SGadTorche.Tick", Sha1 = "2444ae6ce3111652a6d94b746a914a22a57ae2bd")]
    [PortOf("uc:SBase.SGadTorche:Tick", Spec = "spec/equipment/merc-flashlight.md#rules")]
    protected override void Tick(float DeltaTime) => NativeTickTorche();

    /// <summary>
    /// NativeTickTorche (native 1300, decoded game 0x10ab6eb0): the merc is never lit by his own torch; the light is
    /// steady exactly while bInUse (so the malfunction's random switch of the same tick is overwritten), with a corona
    /// for everyone but its owner. It sits on the merc's Laser bone (the drawn skeleton's, placed by the view; here the
    /// eye point stands in) and points along <see cref="AimRotation"/>; inside a network camera it sits 20 uu ahead,
    /// 11 uu aside and 11 uu above the camera and points along its view.
    /// </summary>
    [UcNative("ASGadTorche::execNativeTickTorche", Exe = 0x10ab6eb0)]
    [PortOf("nat:SBase.SGadTorche.NativeTickTorche", Spec = "spec/equipment/merc-flashlight.md#rules")]
    public void NativeTickTorche()
    {
        if (Instigator is Pawn owner)
        {
            owner.bIgnoreOwnEchelonLights = true;
        }

        if (!bInUse || Instigator is not SBasePawn pawn)
        {
            LightType = ELightType.LT_None;
            bCorona = false;
            return;
        }

        LightType = ELightType.LT_Steady;
        bCorona = !(pawn.Controller is PlayerController { IsRemotePlayer: false } && Level.NetMode != ENetMode.NM_DedicatedServer);
        if (pawn.bUsingCamNetwork && pawn.UsedObject is SCamNetwork cam)
        {
            cam.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
            SetLocation(cam.Location + (x * K.CamLaserForward) - (y * K.CamLaserSide) + (z * K.CamLaserSide));
            Rotation = cam.Rotation;
            return;
        }

        SetLocation(pawn.Location + CombatServices.Ports(this).EyePosition(pawn));
        if (AimRotation(pawn) is { } aim)
        {
            Rotation = aim;
        }
    }

    /// <summary>
    /// Where the torch points (decoded game 0x10ab717d..0x10ab7236): from the merc's packed desired rotation
    /// (iDesiredYawPitch), its pitch, and its yaw leaned 30 % toward the body's yaw. Null while IK_Type is IKT_NONE,
    /// when the torch follows the Laser bone's own direction (the view's).
    /// </summary>
    public static FRotator? AimRotation(SBasePawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        if (pawn.IK_Type == IK_Type_.IKT_NONE)
        {
            return null;
        }

        int packed = pawn.iDesiredYawPitch;
        int desiredYaw = packed & 0xFFFF;
        int diff = NormalizeHalf(pawn.Rotation.Yaw - desiredYaw);
        int lean = (int)(diff * K.TorchBodyYawShare);
        int yaw = NormalizeHalf(desiredYaw - lean);
        int pitch = (packed >> 16) & 0xFFFF;
        return new FRotator(pitch, yaw, 0);
    }

    /// <summary>The native's wrap of a rotation difference into -32768..32767.</summary>
    private static int NormalizeHalf(int v) => v > 0x7FFF ? v - 0x10000 : v < -0x8000 ? v + 0x10000 : v;

    [Uc("SBase.SGadTorche.Destroyed", Sha1 = "7f74c71fbb5c354a26701d02d2a7f898da887cb9")]
    [PortOf("uc:SBase.SGadTorche:Destroyed", Spec = "spec/equipment/merc-flashlight.md#rules")]
    protected override void Destroyed()
    {
        LightType = ELightType.LT_None;
        Instigator = null;
        base.Destroyed();
    }

    /// <summary>A reliable owner-to-server function: true when it was sent (this is a client copy).</summary>
    private bool ServerRelay(Name Function)
    {
        if (Role < ENetRole.ROLE_Authority && OwnerPawn?.Controller is PlayerController pc)
        {
            CombatServices.Net(this).ServerCall(pc, new CombatCall(Function) { Target = this });
            return true;
        }

        return false;
    }

    /// <summary>s_Malfunction: while on, the light is lit one tick in three at random; the timer restores it.</summary>
    [Uc("SBase.SGadTorche.s_Malfunction")]
    public class s_Malfunction : UcActorState
    {
        public static readonly s_Malfunction Instance = new(UcMeta.s_Malfunction, null);

        protected s_Malfunction(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SGadTorche.s_Malfunction.Tick", Sha1 = "abbe9b4e93a16887b1a187f2bfa70b6a2e0d5bf0")]
        [PortOf("uc:SBase.SGadTorche:s_Malfunction.Tick", Spec = "spec/equipment/merc-flashlight.md#rules")]
        public override void Tick(Actor self, float DeltaTime)
        {
            var torch = (SGadTorche)self;
            if (!torch.bInUse)
            {
                return;
            }

            bool lit = torch.Rand(K.MalfunctionOdds) == 0;
            torch.LightType = lit ? ELightType.LT_Steady : ELightType.LT_None;
            torch.UsesLightBeam = lit;

            // Global.Tick: the native placement runs after the switch, in the same tick.
            torch.NativeTickTorche();
        }

        [Uc("SBase.SGadTorche.s_Malfunction.Timer", Sha1 = "30d1b467ab4d7f424a77b407734c4195f71defe2")]
        [PortOf("uc:SBase.SGadTorche:s_Malfunction.Timer", Spec = "spec/equipment/merc-flashlight.md#rules")]
        public override void Timer(Actor self)
        {
            var torch = (SGadTorche)self;
            torch.LightType = torch.bInUse ? ELightType.LT_Steady : ELightType.LT_None;
            torch.UsesLightBeam = torch.bInUse;
            torch.GotoState(Name.None);
        }
    }
}
