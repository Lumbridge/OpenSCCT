using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController knock-out states: s_Inactive and the states that share its behaviour (falling down, lying,
// standing up, being revived, landing dead, being finished), the inert falls, and the revive requests. Written from
// spec/combat/knockout-and-terminate.md and spec/combat/death-and-respawn.md (rules 8, 9 and 11).
//
// The decompiler index records no super state for most of these (the specs read the extends chains from the source),
// so the classes share s_Inactive's behaviour through C# inheritance while their state chains stay as the index has
// them.
public partial class SPlayerController
{
    private static readonly Name CNAME_s_LyingOnGround = new("s_LyingOnGround");
    private static readonly Name CNAME_s_StandUp = new("s_StandUp");
    private static readonly Name CNAME_s_Reanimating = new("s_Reanimating");
    private static readonly Name CNAME_s_LandingInerte = new("s_LandingInerte");
    private static readonly Name CNAME_s_ReanimateFriend_0H = new("s_ReanimateFriend_0H");
    private static readonly Name CNAME_s_ReanimateFriend_2H = new("s_ReanimateFriend_2H");
    private static readonly Name CNAME_DIE_InPlaceWhileGrabbing = new("DIE_InPlaceWhileGrabbing");
    private static readonly ClassRef CombatGasMaskClass = new("SBase.SGadGasMask");

    /// <summary>A state version of <see cref="CanBeReanimated"/>.</summary>
    public interface ICanBeReanimated
    {
        bool CanBeReanimated(SPlayerController self);
    }

    /// <summary>CanBeReanimated through the current state (true only while lying on the ground).</summary>
    public bool eventCanBeReanimated() => StateNode is ICanBeReanimated s ? s.CanBeReanimated(this) : CanBeReanimated();

    [Uc("SBase.SPlayerController.CanBeReanimated", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:CanBeReanimated", Spec = "spec/combat/knockout-and-terminate.md#reviving-a-downed-team-mate")]
    public bool CanBeReanimated() => false;

    /// <summary>
    /// The spy's revive search (rule 33): another living pawn of the same side within 250 uu that sleeps and is in
    /// front; the server is asked to revive it.
    /// </summary>
    [Uc("SBase.SPlayerController.FindPawnToReanimate", Sha1 = "498bfd65904e483007603664f81ca937ef91f592")]
    [PortOf("uc:SBase.SPlayerController:FindPawnToReanimate", Spec = "spec/combat/knockout-and-terminate.md#reviving-a-downed-team-mate")]
    public bool FindPawnToReanimate()
    {
        if (SBPawn is not { } me)
        {
            return false;
        }

        foreach (SBasePawn other in XLevel.AllActors<SBasePawn>())
        {
            if (ReviveTarget(me, other))
            {
                AskServerToReanimatePawn(other);
                return true;
            }
        }

        return false;
    }

    /// <summary>The revive test of rule 33.</summary>
    private static bool ReviveTarget(SBasePawn me, SBasePawn other) =>
        !ReferenceEquals(other, me) && other.SPawnType == me.SPawnType && other.Health > 0 && other.bSleeping &&
        FVector.Dist(other.Location, me.Location) < K.ReviveRange && Facing(me, other) > K.ReviveFacing;

    /// <summary><c>Normal(target - me) . Normal(vector(my Rotation))</c>.</summary>
    private static float Facing(SBasePawn me, SBasePawn other) =>
        (other.Location - me.Location).SafeNormal() | me.Rotation.Vector().SafeNormal();

    /// <summary>
    /// The server revives a team mate (rule 34, reliable owner to server): only a living reviver and a target
    /// lying on the ground; the target stands up revived, the reviver plays the revive by its weapon state.
    /// </summary>
    [Uc("SBase.SPlayerController.AskServerToReanimatePawn", Sha1 = "840323fdb43fe8fdae7e014a9252b663091465d0")]
    [PortOf("uc:SBase.SPlayerController:AskServerToReanimatePawn", Spec = "spec/combat/knockout-and-terminate.md#reviving-a-downed-team-mate")]
    [PortOf("rep:SBase.SPlayerController.AskServerToReanimatePawn", Spec = "spec/combat/knockout-and-terminate.md#network")]
    public void AskServerToReanimatePawn(SBasePawn? Target)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(CNAME_AskServerToReanimatePawn) { Actor1 = Target });
            return;
        }

        if (!CombatAlive || Target?.Controller is not SPlayerController mate || !mate.eventCanBeReanimated())
        {
            return;
        }

        mate.eventMirrorState(CNAME_s_Reanimating);
        eventMirrorState(CombatWeaponLoaded == 0 ? CNAME_s_ReanimateFriend_0H : CNAME_s_ReanimateFriend_2H);
    }

    private static readonly Name CNAME_AskServerToReanimatePawn = new("AskServerToReanimatePawn");

    // ---- s_Inactive and its family --------------------------------------------------------------------------

    /// <summary>
    /// s_Inactive: fire, gadgets, goggles, binoculars, smoke and the cloak are ignored, the laser is off, a taser is an
    /// ordinary shock, and the pawn gets no acceleration (a merc's view follows the pawn, a spy's camera turns).
    /// </summary>
    [Uc("SBase.SPlayerController.s_Inactive")]
    public class s_Inactive : UcActorState, IPlayerTick, IPlayerMove, ICanActivateLaser, ICheckshock, IGoInSmoke, IFire, IAltFire,
        IGadgetQuickChoice, IGadLaserSwitch, IGadTorcheSwitch, ISwitchBinocularVision, ISwitchHeadset, ISwitchROF, ICanActivateCloak,
        IHearPC
    {
        public static readonly s_Inactive Instance = new(UcMeta.s_Inactive, null);

        /// <summary>Downed chat: see <see cref="SPlayerController.InactiveHearPC"/>.</summary>
        public void HearPC(SPlayerController self, SPlayerController PCToTalkWith, bool bHear) => self.InactiveHearPC(PCToTalkWith, bHear);

        protected s_Inactive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Inactive.PlayerTick", Sha1 = "31334389aca0233ad9e80325579475d4677d24b0")]
        [PortOf("uc:SBase.SPlayerController:s_Inactive.PlayerTick", Spec = "spec/combat/knockout-and-terminate.md#common-behaviour-of-the-s_inactive-family")]
        public virtual void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.bFire = 0;
            pc.PlayerTick(DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_Inactive.PlayerMove", Sha1 = "c2d68f0e092d59a5a159a42e5e9248046a5fe2bf")]
        [PortOf("uc:SBase.SPlayerController:s_Inactive.PlayerMove", Spec = "spec/combat/knockout-and-terminate.md#common-behaviour-of-the-s_inactive-family")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime) => self.InactiveMove(DeltaTime);

        [Uc("SBase.SPlayerController.s_Inactive.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Inactive.CanActivateLaser", Spec = "spec/combat/knockout-and-terminate.md#common-behaviour-of-the-s_inactive-family")]
        public bool CanActivateLaser(SPlayerController self) => false;

        public void Checkshock(SPlayerController self, int PillTag, bool bPlayAnim) => self.InactiveCheckshock(PillTag, bPlayAnim);

        /// <summary>Ignored.</summary>
        public void GoInSmoke(SPlayerController self, float tCough, float tFaint, float tHud, float blur)
        {
        }

        /// <summary>Ignored.</summary>
        public void Fire(SPlayerController self, float F)
        {
        }

        /// <summary>Ignored.</summary>
        public void AltFire(SPlayerController self, float F)
        {
        }

        /// <summary>Ignored.</summary>
        public void GadgetQuickChoice(SPlayerController self, int i)
        {
        }

        /// <summary>Ignored.</summary>
        public void GadLaserSwitch(SPlayerController self)
        {
        }

        /// <summary>Ignored.</summary>
        public void GadTorcheSwitch(SPlayerController self)
        {
        }

        /// <summary>Ignored.</summary>
        public void SwitchBinocularVision(SPlayerController self)
        {
        }

        /// <summary>Ignored.</summary>
        public void SwitchHeadset(SPlayerController self, int i)
        {
        }

        /// <summary>Ignored.</summary>
        public void SwitchROF(SPlayerController self)
        {
        }

        /// <summary>Ignored.</summary>
        public bool CanActivateCloak(SPlayerController self) => false;
    }

    /// <summary>
    /// The s_Inactive move (rule 2): no acceleration; a merc's view is locked to the pawn yaw, a spy's camera turns
    /// with the look inputs (32 units per input per second, the horizontal invert option, pitch kept in 15000 up and
    /// 50536 down).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "s_Inactive.PlayerMove: 32 * DeltaTime turn rate, pitch limits 15000 / 50536")]
    internal void InactiveMove(float DeltaTime)
    {
        if (Pawn is not { } pawn)
        {
            return;
        }

        pawn.Acceleration = FVector.Zero;
        if (SPawnType == PawnType.SPAWNDEFENSE)
        {
            Rotation = new FRotator(Rotation.Pitch, pawn.Rotation.Yaw, Rotation.Roll);
            return;
        }

        float turn = bInvertHorizontalAxisCamSpy ? -aTurn : aTurn;
        int yaw = Rotation.Yaw + SvmMath.Trunc(turn * InactiveTurnRate * DeltaTime);
        int pitch = (Rotation.Pitch + SvmMath.Trunc(aLookUp * InactiveTurnRate * DeltaTime)) & 65535;
        if (pitch > InactivePitchUp && pitch < 32768)
        {
            pitch = InactivePitchUp;
        }
        else if (pitch >= 32768 && pitch < InactivePitchDown)
        {
            pitch = InactivePitchDown;
        }

        Rotation = new FRotator(pitch, yaw, Rotation.Roll);
    }

    [Provenance(ProvenanceKind.Decoded, "s_Inactive.PlayerMove turn rate 32")]
    private const float InactiveTurnRate = 32f;

    [Provenance(ProvenanceKind.Decoded, "s_Inactive.PlayerMove pitch limit 15000 up")]
    private const int InactivePitchUp = 15000;

    [Provenance(ProvenanceKind.Decoded, "s_Inactive.PlayerMove pitch limit 50536 down")]
    private const int InactivePitchDown = 50536;

    /// <summary>
    /// s_FallDown: the knock-down. Its label plays the fall direction; at the end of the fall the player dies or lies
    /// on the ground. Damage: state-damage-reactions.md rule 21 (the handler table).
    /// </summary>
    [Uc("SBase.SPlayerController.s_FallDown")]
    public class s_FallDown : s_Inactive
    {
        public static new readonly s_FallDown Instance = new(UcMeta.s_FallDown, null);

        private readonly int[] _labelActions;

        protected s_FallDown(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
            _labelActions = LabelActions(info);
        }

        private static int[] LabelActions(UcStateInfo info)
        {
            int max = 0;
            foreach ((string _, int pc) in info.LabelPcs)
            {
                max = SvmMath.Max(max, pc);
            }

            var actions = new int[max + 1];
            foreach ((string name, int pc) in info.LabelPcs)
            {
                actions[pc] = name switch
                {
                    "DIE_Forward" => SBasePawn.SA_DIE_FORWARD,
                    "DIE_Backward" => SBasePawn.SA_DIE_BACKWARD,
                    "DIE_Left" => SBasePawn.SA_DIE_LEFT,
                    "DIE_Right" => SBasePawn.SA_DIE_RIGHT,
                    "DIE_InPlace" => SBasePawn.SA_DIE_INPLACE,
                    "DIE_DropAttacked" => SBasePawn.SA_DIE_DROPATTACK,
                    "DIE_InPlaceWhileGrabbing" => SBasePawn.SA_GRAB_DROPATTACKED,
                    _ => SBasePawn.SA_NONE,
                };
            }

            return actions;
        }

        /// <summary>
        /// Binoculars, reticle and laser off; the server releases a spy this merc was finishing; the owner resets the
        /// vision, leaves the back-to-wall pose, shows the knock-out effects while alive and ends a running shock.
        /// </summary>
        [Uc("SBase.SPlayerController.s_FallDown.BeginState", Sha1 = "19bae1a35a3cf743b40a6d730f3d07e00acb89aa")]
        [PortOf("uc:SBase.SPlayerController:s_FallDown.BeginState", Spec = "spec/combat/knockout-and-terminate.md#s_falldown-falling-down")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.KnockDownBegin();
        }

        [Uc("SBase.SPlayerController.s_FallDown.PlayerTick", Sha1 = "5c26905e924e871afa989271c2e7544eca08c27d")]
        [PortOf("uc:SBase.SPlayerController:s_FallDown.PlayerTick", Spec = "spec/combat/knockout-and-terminate.md#s_falldown-falling-down")]
        public override void PlayerTick(PlayerController self, float DeltaTime) => base.PlayerTick(self, DeltaTime);

        /// <summary>Owner, channel 0: dead gives s_Dead (not in GameEnded), alive gives s_LyingOnGround.</summary>
        [Uc("SBase.SPlayerController.s_FallDown.AnimEnd", Sha1 = "428d195977b603fc3133eb030d97e316bd72f59a")]
        [PortOf("uc:SBase.SPlayerController:s_FallDown.AnimEnd", Spec = "spec/combat/knockout-and-terminate.md#s_falldown-falling-down")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient())
            {
                return;
            }

            if (pc.Pawn is { Health: <= 0 })
            {
                if (!pc.CombatInGameEnded)
                {
                    pc.eventChangeState(CNAME_s_Dead);
                }
            }
            else
            {
                pc.eventChangeState(CNAME_s_LyingOnGround);
            }
        }

        /// <summary>The label plays the fall direction on the owner; no label plays nothing.</summary>
        [Uc("SBase.SPlayerController.s_FallDown@code", Sha1 = "f4e097fd29cb6454bab799d3a983478ff71032b8")]
        [PortOf("uc:SBase.SPlayerController:s_FallDown@code", Spec = "spec/combat/knockout-and-terminate.md#s_falldown-falling-down")]
        public override void Code(Actor self, ref LatentFrame frame)
        {
            var pc = (SPlayerController)self;
            int action = frame.Pc >= 0 && frame.Pc < _labelActions.Length ? _labelActions[frame.Pc] : SBasePawn.SA_NONE;
            frame.Pc = LatentFrame.NoCode;
            if (action != SBasePawn.SA_NONE && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, action, true);
            }
        }
    }

    /// <summary>The common entry of the knock-down states (s_FallDown rule 6, s_FallDownDying death spec rule 11).</summary>
    private void KnockDownBegin()
    {
        bBinocularVision = false;
        bNoReticule = true;
        EteintLaser();
        if (Role == ENetRole.ROLE_Authority && SBPawn?.m_PawnToTerminate?.Controller is SPlayerController victim &&
            !ReferenceEquals(victim, this))
        {
            victim.eventMercCantTerminate();
        }

        if (!IAmOnClient())
        {
            return;
        }

        Goggle?.ResetVisionMode(true);
        if (SBPawn is { } pawn && pawn.BTW != BTW_.NOBTW)
        {
            CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_BTW_OUT, true);
        }

        if (CombatAlive)
        {
            CombatFx.Notify(this, CombatFx.HeadCamera, Slot: 1);
            SetHudPerturbationLength(KnockDownPerturbation);
            if (SPawnType == PawnType.SPAWNDEFENSE)
            {
                CombatFx.Notify(this, CombatFx.FirstPersonView, 1f);
                CombatFx.Notify(this, CombatFx.OverlayFade, 0f, 255f, KnockDownOverlayTime);
                CombatFx.Notify(this, CombatFx.WeirdEffect, 1f);
            }
        }

        if (iDontUpdatePadInput != 0)
        {
            bTazered = false;
            EndStickyShockNow();
        }
    }

    [Provenance(ProvenanceKind.Decoded, "s_FallDown.BeginState HUD perturbation 1.5 s")]
    private const float KnockDownPerturbation = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "s_FallDown.BeginState merc sleep overlay 0 to 255 over 7.5 s")]
    private const float KnockDownOverlayTime = 7.5f;

    /// <summary>s_FallDownDying: a grabbing spy drop-attacked to death falls and dies at the end of the clip.</summary>
    [Uc("SBase.SPlayerController.s_FallDownDying")]
    public class s_FallDownDying : s_FallDown
    {
        public static new readonly s_FallDownDying Instance = new(UcMeta.s_FallDownDying, s_FallDown.Instance);

        protected s_FallDownDying(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_FallDownDying.BeginState", Sha1 = "cedc24c69ee8e24bd65d66f710ac6a1b198552db")]
        [PortOf("uc:SBase.SPlayerController:s_FallDownDying.BeginState", Spec = "spec/combat/death-and-respawn.md#paths-into-s_dead")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bBinocularVision = false;
            pc.bNoReticule = true;
            pc.EteintLaser();
            if (pc.SBPawn is { } pawn && pawn.BTW != BTW_.NOBTW)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_BTW_OUT, true);
            }
        }

        [Uc("SBase.SPlayerController.s_FallDownDying.AnimEnd", Sha1 = "9e934790fa11e744397ed19b91b87465b9d10c4c")]
        [PortOf("uc:SBase.SPlayerController:s_FallDownDying.AnimEnd", Spec = "spec/combat/death-and-respawn.md#paths-into-s_dead")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient())
            {
                pc.eventChangeState(CNAME_s_Dead);
            }
        }

        [Uc("SBase.SPlayerController.s_FallDownDying@code", Sha1 = "5381ba787a8300db8fce290a82464c7c09a57807")]
        [PortOf("uc:SBase.SPlayerController:s_FallDownDying@code", Spec = "spec/combat/death-and-respawn.md#paths-into-s_dead")]
        public override void Code(Actor self, ref LatentFrame frame) => base.Code(self, ref frame);
    }

    /// <summary>
    /// s_LyingOnGround: the downed player lies for fLyingOnGroundDuration, can be revived, and takes full damage
    /// (global handler). Voice routing (OtherTeamCantHearMe, TalkToMate) belongs to the voice spec.
    /// </summary>
    [Uc("SBase.SPlayerController.s_LyingOnGround")]
    public class s_LyingOnGround : s_Inactive, ICanBeReanimated, IPlayAnimDeath
    {
        public static new readonly s_LyingOnGround Instance = new(UcMeta.s_LyingOnGround, null);

        protected s_LyingOnGround(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_LyingOnGround.BeginState", Sha1 = "e571356412bfa2013644805345f7582875e23eec")]
        [PortOf("uc:SBase.SPlayerController:s_LyingOnGround.BeginState", Spec = "spec/combat/knockout-and-terminate.md#s_lyingonground")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_REMOVE_COLLISION, true);
            pc.fLyingOnGroundTime = pc.Level.TimeSeconds;
            pc.SetTimer(pc.fLyingOnGroundDuration, false);
            if (pawn.SPawnType == PawnType.SPAWNDEFENSE && pawn.bGasMask &&
                Equipment.PawnInventory.FindGadgetType(pawn, CombatGasMaskClass) is SGadget mask)
            {
                mask.Use(1f);
            }
        }

        [Uc("SBase.SPlayerController.s_LyingOnGround.PlayerTick", Sha1 = "19040d47d375059d738c1a47f7280f39bf71a5eb")]
        [PortOf("uc:SBase.SPlayerController:s_LyingOnGround.PlayerTick", Spec = "spec/combat/knockout-and-terminate.md#s_lyingonground")]
        public override void PlayerTick(PlayerController self, float DeltaTime) => base.PlayerTick(self, DeltaTime);

        /// <summary>The lying time is over: stand up (owner).</summary>
        [Uc("SBase.SPlayerController.s_LyingOnGround.Timer", Sha1 = "ac9b063437042cd6b7721c47d3dfa6d1d9c701ce")]
        [PortOf("uc:SBase.SPlayerController:s_LyingOnGround.Timer", Spec = "spec/combat/knockout-and-terminate.md#s_lyingonground")]
        public override void Timer(Actor self) => ((SPlayerController)self).eventChangeState(CNAME_s_StandUp);

        [Uc("SBase.SPlayerController.s_LyingOnGround.CanBeReanimated", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_LyingOnGround.CanBeReanimated", Spec = "spec/combat/knockout-and-terminate.md#s_lyingonground")]
        public bool CanBeReanimated(SPlayerController self) => true;

        /// <summary>Ignored.</summary>
        public void PlayAnimDeath(SPlayerController self, FVector Momentum, int PawnHealth)
        {
        }

        /// <summary>A new shock and wake window, the lying time back to 7 s, the merc's overlay fades.</summary>
        [Uc("SBase.SPlayerController.s_LyingOnGround.EndState", Sha1 = "f7f1d9ea27d4a352a9f00671e270e39e86e3ef63")]
        [PortOf("uc:SBase.SPlayerController:s_LyingOnGround.EndState", Spec = "spec/combat/knockout-and-terminate.md#s_lyingonground")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            float now = pc.Level.TimeSeconds;
            pc.TimeShock = now;
            pc.fLastTimeWakeUp = now;
            pc.fLyingOnGroundDuration = K.DefaultLyingTime;
            if (pc.IAmOnClient())
            {
                CombatFx.Notify(pc, CombatFx.WeirdEffect, 0f);
                if (pc.SPawnType == PawnType.SPAWNDEFENSE)
                {
                    CombatFx.Notify(pc, CombatFx.OverlayFade, 255f, 1f, WakeOverlayFade);
                }
            }
        }
    }

    [Provenance(ProvenanceKind.Decoded, "s_LyingOnGround.EndState / s_StandUp.BeginState merc overlay 255 to 1 over 0.25 s")]
    private const float WakeOverlayFade = 0.25f;

    [Provenance(ProvenanceKind.Decoded, "s_StandUp / s_Reanimating cross-eye blur 1 to 0 over 4.5 s")]
    private const float WakeBlurTime = 4.5f;

    /// <summary>
    /// s_StandUp: getting up. A merc gets the wake-up shock on the server (rule 16); the owner returns to walking at
    /// the end of the clip; an explosion blasts like a standing player.
    /// </summary>
    [Uc("SBase.SPlayerController.s_StandUp")]
    public class s_StandUp : s_Inactive, IExplosionHit
    {
        public static new readonly s_StandUp Instance = new(UcMeta.s_StandUp, null);

        protected s_StandUp(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_StandUp.BeginState", Sha1 = "39145f0d05b23463caa3cd30f12553d8a6537003")]
        [PortOf("uc:SBase.SPlayerController:s_StandUp.BeginState", Spec = "spec/combat/knockout-and-terminate.md#s_standup-and-s_reanimating")]
        [PortOf("uc:SBase.SPlayerController:s_StandUp.BeginState", Spec = "spec/combat/shock-and-taser.md#merc-wake-up-shock")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Role == ENetRole.ROLE_Authority && pc.SPawnType == PawnType.SPAWNDEFENSE)
            {
                pc.TimeShock = pc.Level.TimeSeconds - pc.RealTimeInvulSticky - 1f;
                pc.eventCheckshock(0, false);
            }

            pc.StandUpOwnerEffects(merc: true);
        }

        [Uc("SBase.SPlayerController.s_StandUp.PlayerTick", Sha1 = "8aef3275a02fa5b52d82d17acc60abc87a56e4a4")]
        [PortOf("uc:SBase.SPlayerController:s_StandUp.PlayerTick", Spec = "spec/combat/knockout-and-terminate.md#s_standup-and-s_reanimating")]
        public override void PlayerTick(PlayerController self, float DeltaTime) => base.PlayerTick(self, DeltaTime);

        /// <summary>Owner, channel 0: the head camera detaches and the player walks again.</summary>
        [Uc("SBase.SPlayerController.s_StandUp.AnimEnd", Sha1 = "010846897ba3d7f12f7748e9b26449eb1ede4b04")]
        [PortOf("uc:SBase.SPlayerController:s_StandUp.AnimEnd", Spec = "spec/combat/knockout-and-terminate.md#s_standup-and-s_reanimating")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient())
            {
                return;
            }

            CombatFx.Notify(pc, CombatFx.HeadCamera, 0f);
            pc.GotoWaitingStateCombat();
        }

        /// <summary>
        /// The reticle and laser back, the smoke cough cleared, a new shock and wake window; a merc's view, weapon
        /// and overlay return; the server clears the lying flag.
        /// </summary>
        [Uc("SBase.SPlayerController.s_StandUp.EndState", Sha1 = "801bf2b5e7a25bb774338c9278a008c9ba8264c4")]
        [PortOf("uc:SBase.SPlayerController:s_StandUp.EndState", Spec = "spec/combat/knockout-and-terminate.md#s_standup-and-s_reanimating")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.bLaserEtaitAllume)
            {
                pc.bNoReticule = false;
            }

            pc.RallumeLaserEteint();
            pc.EndCoughNow();
            float now = pc.Level.TimeSeconds;
            pc.TimeShock = now;
            pc.fLastTimeWakeUp = now;
            if (pc.SPawnType == PawnType.SPAWNDEFENSE && pc.SBPawn is { } pawn)
            {
                CombatFx.Notify(pc, CombatFx.OverlayFade, 1f, 1f, 0f);
                if (pawn.MyWeapon is SWeaponDefense rifle)
                {
                    rifle.GotoState(CNAME_s_Selected);
                    rifle.AnimateWaiting();
                    rifle.bDrawWeapon = true;
                }

                CombatFx.Notify(pc, CombatFx.FirstPersonView, 1f);
            }

            if (pc.Role == ENetRole.ROLE_Authority && pc.SBPawn is { } lying)
            {
                lying.bLyingOnGround = false;
                pc.ReleaseChatPartners();
            }
        }

        public void ExplosionHit(SPlayerController self, FVector Momentum) => self.StandingExplosionHit(Momentum);
    }

    /// <summary>The owner side of standing up (s_StandUp and s_Reanimating, rules 16 and 17).</summary>
    private void StandUpOwnerEffects(bool merc)
    {
        bBinocularVision = false;
        if (!IAmOnClient() || SBPawn is not { } pawn)
        {
            return;
        }

        CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_STANDUP, true);
        if (SPawnType == PawnType.SPAWNDEFENSE)
        {
            CombatFx.Notify(this, CombatFx.OverlayFade, 255f, 1f, WakeOverlayFade);
        }

        if (SPawnType == PawnType.SPAWNDEFENSE || !merc)
        {
            CombatFx.Notify(this, CombatFx.CrossEyeBlur, 1f, 0f, WakeBlurTime);
        }
    }

    /// <summary>s_Reanimating: getting up revived by a mate (no wake-up shock).</summary>
    [Uc("SBase.SPlayerController.s_Reanimating")]
    public class s_Reanimating : s_StandUp
    {
        public static new readonly s_Reanimating Instance = new(UcMeta.s_Reanimating, s_StandUp.Instance);

        protected s_Reanimating(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Reanimating.BeginState", Sha1 = "811946800ac5deb85d43450975cfd29f99fd7eb6")]
        [PortOf("uc:SBase.SPlayerController:s_Reanimating.BeginState", Spec = "spec/combat/knockout-and-terminate.md#s_standup-and-s_reanimating")]
        public override void BeginState(Actor self) => ((SPlayerController)self).StandUpOwnerEffects(merc: false);
    }

    /// <summary>s_LandingInerte: a dead body that landed; any animation end gives s_Dead, falling again PlayerFallingInerte.</summary>
    [Uc("SBase.SPlayerController.s_LandingInerte")]
    public class s_LandingInerte : s_Inactive, IMyFalling
    {
        public static new readonly s_LandingInerte Instance = new(UcMeta.s_LandingInerte, s_Inactive.Instance);

        protected s_LandingInerte(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_LandingInerte.AnimEnd", Sha1 = "14024b8d0d0c0742416f37de076f25c2cbbd981f")]
        [PortOf("uc:SBase.SPlayerController:s_LandingInerte.AnimEnd", Spec = "spec/combat/death-and-respawn.md#paths-into-s_dead")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (!pc.CombatInGameEnded)
            {
                pc.eventChangeState(CNAME_s_Dead);
            }
        }

        [Uc("SBase.SPlayerController.s_LandingInerte.MyFalling", Sha1 = "3b7b952bcbcf58c2dda9c1e80e9692c92fb37d76")]
        [PortOf("uc:SBase.SPlayerController:s_LandingInerte.MyFalling", Spec = "spec/combat/death-and-respawn.md#paths-into-s_dead")]
        public void MyFalling(SPlayerController self, float LeftTime) => self.eventChangeState(CNAME_PlayerFallingInerte);

        [Uc("SBase.SPlayerController.s_LandingInerte.PlayerMove", Sha1 = "8491ea3e5262e6c6a00fe853c16fe3a182bdb697")]
        [PortOf("uc:SBase.SPlayerController:s_LandingInerte.PlayerMove", Spec = "spec/combat/death-and-respawn.md#paths-into-s_dead")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) => self.InactiveMove(DeltaTime);
    }

    // ---- inert falls ---------------------------------------------------------------------------------------

    /// <summary>
    /// PlayerFallingInerte: a player killed in the air falls as a body (death spec rule 8): 10 s at most, then
    /// s_Dead; landing gives s_LandingInerte. Drop attacks are ignored, but the falling body that lands on a pawn's
    /// head drop-attacks him (or sets off his sticky skin).
    /// </summary>
    [Uc("SBase.SPlayerController.PlayerFallingInerte")]
    public class PlayerFallingInerte : UcActorState, IMyNotifyLanded, IDropAttack, IMyFalling, INotifyBump
    {
        public static readonly PlayerFallingInerte Instance = new(UcMeta.PlayerFallingInerte, null);

        protected PlayerFallingInerte(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.PlayerFallingInerte.BeginState", Sha1 = "a1e637791049a6cd78bab641930bbac9d89c967d")]
        [PortOf("uc:SBase.SPlayerController:PlayerFallingInerte.BeginState", Spec = "spec/combat/death-and-respawn.md#paths-into-s_dead")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.CombatPorts.ResetZones(pc);
            if (!pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.SetTimer(K.FallingInerteTimeout, false);
            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_FALL_INERTE, true);
            if (pawn.Physics is EPhysics.PHYS_OnLadder or EPhysics.PHYS_OnPipe or EPhysics.PHYS_OnVerticalPlane)
            {
                pawn.SetVelocityForDyingPawn(pawn.Rotation.Vector() * -K.DyingPushBack);
            }

            if (pawn.SPawnType == PawnType.SPAWNDEFENSE)
            {
                pc.bBehindView = true;
                CombatFx.Notify(pc, CombatFx.HeadCamera, 0f);
                pc.MovementCamera(ECamMode.ECM_Walking);
                (pawn.MyWeapon as SWeaponDefense)?.AnimateFall();
            }
        }

        /// <summary>Ten seconds without landing: the body is dead where it is.</summary>
        [Uc("SBase.SPlayerController.PlayerFallingInerte.Timer", Sha1 = "26d89e25bb31225a6ebe543a638bdd14c170441c")]
        [PortOf("uc:SBase.SPlayerController:PlayerFallingInerte.Timer", Spec = "spec/combat/death-and-respawn.md#paths-into-s_dead")]
        public override void Timer(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_LAND_INERTE, true);
            }

            pc.eventChangeState(CNAME_s_Dead);
        }

        [Uc("SBase.SPlayerController.PlayerFallingInerte.MyNotifyLanded", Sha1 = "b3ebe213eedd8c492aba03f4589e5117203dbcea")]
        [PortOf("uc:SBase.SPlayerController:PlayerFallingInerte.MyNotifyLanded", Spec = "spec/combat/death-and-respawn.md#paths-into-s_dead")]
        public virtual bool MyNotifyLanded(SPlayerController self, FVector HitNormal, float fTimeLeft, Actor? LandedOn)
        {
            if (!self.IAmOnClient() || self.SBPawn is not { } pawn)
            {
                return true;
            }

            self.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_LAND_INERTE, true);
            if (!self.CombatInGameEnded)
            {
                self.eventChangeState(CNAME_s_LandingInerte);
            }

            if (pawn.WeaponLoaded != 0)
            {
                (pawn.MyWeapon as SWeaponDefense)?.AnimateLand();
            }

            return true;
        }

        /// <summary>Coming down on a head (owner): a sticky-skinned pawn blasts, anyone else is drop-attacked.</summary>
        [Uc("SBase.SPlayerController.PlayerFallingInerte.NotifyBump", Sha1 = "d06cb3f59a430f67708596d3088746b58b7fdf69")]
        [PortOf("uc:SBase.SPlayerController:PlayerFallingInerte.NotifyBump", Spec = "spec/combat/death-and-respawn.md#paths-into-s_dead")]
        public bool NotifyBump(SPlayerController self, Actor Other)
        {
            if (Other is SBasePawn below && self.Pawn is { } me &&
                below.Location.Z + below.CollisionHeight * K.LedgeFallHeadShare <= me.Location.Z - me.CollisionHeight && self.IAmOnClient())
            {
                if (below.bStickySkinned)
                {
                    self.eventStickyBlast(below);
                }
                else
                {
                    self.TellServerToFallAttack(below);
                }
            }

            return true;
        }

        [Uc("SBase.SPlayerController.PlayerFallingInerte.DropAttack", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:PlayerFallingInerte.DropAttack", Spec = "spec/combat/death-and-respawn.md#paths-into-s_dead")]
        public void DropAttack(SPlayerController self, SBasePawn PawnToAttack)
        {
        }

        /// <summary>Ignored.</summary>
        public void MyFalling(SPlayerController self, float LeftTime)
        {
        }

        /// <summary>Ignored.</summary>
        public override void AnimEnd(Actor self, int Channel)
        {
        }
    }

    /// <summary>
    /// PlayerFallingKO: knocked off something by a charge (rules 27 and 28). It shares PlayerFallingInerte's
    /// timeout; landing applies the fall damage (10 hp at least) through the client damage RPC, then lies 3 s.
    /// </summary>
    [Uc("SBase.SPlayerController.PlayerFallingKO")]
    public class PlayerFallingKO : PlayerFallingInerte
    {
        public static new readonly PlayerFallingKO Instance = new(UcMeta.PlayerFallingKO, null);

        protected PlayerFallingKO(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.PlayerFallingKO.BeginState", Sha1 = "88224b4cb8f9ff66be667022dabe4e8e40f1ff1b")]
        [PortOf("uc:SBase.SPlayerController:PlayerFallingKO.BeginState", Spec = "spec/combat/knockout-and-terminate.md#playerfallingko-knocked-off-something-by-a-charge")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.CombatPorts.ResetZones(pc);
            pc.bNoReticule = true;
            if (pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pc.SetTimer(K.FallingInerteTimeout, false);
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_FALL_INERTE, true);
            }
        }

        [Uc("SBase.SPlayerController.PlayerFallingKO.MyNotifyLanded", Sha1 = "5958ff7440d358b6afa6defd6063503cf233fdde")]
        [PortOf("uc:SBase.SPlayerController:PlayerFallingKO.MyNotifyLanded", Spec = "spec/combat/knockout-and-terminate.md#playerfallingko-knocked-off-something-by-a-charge")]
        public override bool MyNotifyLanded(SPlayerController self, FVector HitNormal, float fTimeLeft, Actor? LandedOn)
        {
            if (!self.IAmOnClient() || self.SBPawn is not { } pawn)
            {
                return true;
            }

            self.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_LAND_INERTE, true);
            if (pawn.Health <= 0)
            {
                self.MercOverlayFade();
                self.eventChangeState(CNAME_s_LandingInerte);
                return true;
            }

            float h = self.fZMaxFalling - pawn.Location.Z;
            float dmg = 0f;
            if (h > pawn.Fell_MinHeightForDmg)
            {
                dmg = CombatRules.FallDamage(h, pawn.Fell_MinHeightForDmg, pawn.Fell_MaxHeightForDmg, K.DefaultHealth);
                self.fZMaxFalling = pawn.Location.Z;
            }

            dmg = SvmMath.Max(dmg, K.KoFallMinDamage);
            var down = new FVector(0f, 0f, -1f);
            if (pawn.Health - dmg <= 0f)
            {
                self.MercOverlayFade();
                self.eventChangeState(CNAME_s_LandingInerte);
                self.DamagePawn_C2S(pawn, pawn.Health, pawn, pawn.Location, down, SDamageType.SFell);
            }
            else
            {
                self.DamagePawn_C2S(pawn, SvmMath.Trunc(dmg), pawn, pawn.Location, down, SDamageType.SFell);
                self.fLyingOnGroundDuration = self.fKOAfterBerserkOrCharge;
                self.eventChangeState(CNAME_s_LyingOnGround);
            }

            return true;
        }
    }

    /// <summary>The merc's overlay fade of a dying landing.</summary>
    private void MercOverlayFade()
    {
        if (SPawnType == PawnType.SPAWNDEFENSE)
        {
            CombatFx.Notify(this, CombatFx.OverlayFade, 255f, 1f, WakeOverlayFade);
        }
    }
}
