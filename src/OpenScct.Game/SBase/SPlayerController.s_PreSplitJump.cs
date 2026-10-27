using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController split jump and Vandamme states: s_PreSplitJump (the split hold until the push), s_SplitJump
// (the push off the first wall), VandammeBegin, Vandamme, VandammeWaitForWeapon and VandammeWeapon (braced between
// two walls). Written from spec/movement/wall-jump-split-vandamme.md (split jump, Vandamme).
public partial class SPlayerController
{
    public interface IImpulsionSplit
    {
        void ImpulsionSplit(SPlayerController self);
    }

    /// <summary>The split clip's push notify (through the pawn's ImpulsionSplit).</summary>
    public void eventImpulsionSplit()
    {
        if (StateNode is IImpulsionSplit s)
        {
            s.ImpulsionSplit(this);
        }
        else
        {
            ImpulsionSplit();
        }
    }

    [Uc("SBase.SPlayerController.ImpulsionSplit", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:ImpulsionSplit", Spec = "spec/movement/wall-jump-split-vandamme.md#split-jump")]
    public virtual void ImpulsionSplit()
    {
    }

    /// <summary>
    /// The split jump start in the air (s_PlayerJump.PlayerMove): a side wall (not facing it within 0.707) gives the wall
    /// direction to the server, the split hold toward that side with the pawn turned along the wall, and
    /// s_PreSplitJump; a wall within the 0.1 dead band of straight ahead or behind does nothing more.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_PlayerJump.PlayerMove", Spec = "spec/movement/wall-jump-split-vandamme.md#split-jump")]
    internal void SplitJumpOffWall(SBasePawn pawn, FVector x)
    {
        FVector n = vWallNormal;
        if (!((n | x) < MovementK.SplitWallFacing))
        {
            return;
        }

        float side = n.X * x.Y - n.Y * x.X;
        pawn.SetWallJumpDir(n);
        var flat = new FVector(n.X, n.Y, 0f);
        flat = flat.SafeNormal();
        FVector along;
        int action;
        if (side < -MovementK.SplitSideDeadBand)
        {
            action = SBasePawn.SA_DBL_JUMP_L;
            along = new FVector(flat.Y, -flat.X, 0f);
        }
        else if (side > MovementK.SplitSideDeadBand)
        {
            action = SBasePawn.SA_DBL_JUMP_R;
            along = new FVector(-flat.Y, flat.X, 0f);
        }
        else
        {
            return;
        }

        pawn.ChangeEnumAction(action);
        pawn.Rotation = new FRotator(0, along.Rotation().Yaw, 0);
        eventChangeState(MovementStateNames.s_PreSplitJump);
    }

    /// <summary>The braced spy's view packed for the simulated proxies (low word yaw, high word pitch).</summary>
    internal void PackVandammeView()
    {
        if (SBPawn is { } pawn)
        {
            pawn.iYawPitchFromView = PackLW(pawn.iYawPitchFromView, Rotation.Yaw);
            pawn.iYawPitchFromView = PackHW(pawn.iYawPitchFromView, Rotation.Pitch);
        }
    }

    /// <summary>Object.PackLW: <paramref name="Packed"/> with its low 16 bits replaced by <paramref name="LowWord"/>'s.</summary>
    [UcNative("UObject::execPackLW")]
    [PortOf("nat:Core.Object.PackLW", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
    [Provenance(ProvenanceKind.Estimated, "Object native 439 is not decoded; read as the low word replaced")]
    public static int PackLW(int Packed, int LowWord) => (int)(((uint)Packed & 0xFFFF0000u) | ((uint)LowWord & 0xFFFFu));

    /// <summary>Object.PackHW: <paramref name="Packed"/> with its high 16 bits replaced by <paramref name="HighWord"/>'s low 16 bits.</summary>
    [UcNative("UObject::execPackHW")]
    [PortOf("nat:Core.Object.PackHW", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
    [Provenance(ProvenanceKind.Estimated, "Object native 440 is not decoded; read as the high word replaced")]
    public static int PackHW(int Packed, int HighWord) => (int)(((uint)Packed & 0xFFFFu) | (((uint)HighWord & 0xFFFFu) << 16));

    /// <summary>The drop from the brace with the crouch latch: the push-off jump, no jump count, the fall.</summary>
    internal void VandammeDrop()
    {
        if (!bCrouchWithRoll || SBPawn is not { } pawn)
        {
            return;
        }

        bCrouchWithRoll = false;
        pawn.MyDoJump();
        pawn.nJumpNumber = 0;
        pawn.ChangeEnumAction(SBasePawn.SA_VANDAMMEFALL);
        eventChangeState(MovementStateNames.s_PlayerJump);
        bPressedJump = false;
    }

    [Uc("SBase.SPlayerController.s_PreSplitJump")]
    public class s_PreSplitJump : s_PlayerJump, IImpulsionSplit
    {
        public static new readonly s_PreSplitJump Instance = new(UcMeta.s_PreSplitJump, s_PlayerJump.Instance);

        protected s_PreSplitJump(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The split clip's end is ignored: the push notify moves on.</summary>
        public override void AnimEnd(Actor self, int Channel)
        {
        }

        /// <summary>The push off the wall: the action with the velocity for the server, the split jump.</summary>
        [Uc("SBase.SPlayerController.s_PreSplitJump.ImpulsionSplit", Sha1 = "79e5a7ae66a6ef6f86bdeb580fc19cc1cd68fed7")]
        [PortOf("uc:SBase.SPlayerController:s_PreSplitJump.ImpulsionSplit", Spec = "spec/movement/wall-jump-split-vandamme.md#split-jump")]
        public virtual void ImpulsionSplit(SPlayerController self)
        {
            if (!self.IAmOnClient() || self.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.bActionsSendWithVelocity = true;
            pawn.ChangeEnumAction(SBasePawn.SA_DBL_JUMP_IMP);
            self.eventChangeState(MovementStateNames.s_SplitJump);
        }

        [Uc("SBase.SPlayerController.s_PreSplitJump.PlayerMove", Sha1 = "1a7a14500032e63a9fa54c7e3eb6bce0cbe5834f")]
        [PortOf("uc:SBase.SPlayerController:s_PreSplitJump.PlayerMove", Spec = "spec/movement/wall-jump-split-vandamme.md#split-jump")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            self.eventUpdateRotation(DeltaTime, 1f);
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_SplitJump")]
    public class s_SplitJump : s_PlayerJump
    {
        public static new readonly s_SplitJump Instance = new(UcMeta.s_SplitJump, s_PlayerJump.Instance);

        protected s_SplitJump(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The split jump's clip end is ignored (the landing or the brace ends it).</summary>
        public override void AnimEnd(Actor self, int Channel)
        {
        }

        /// <summary>After the push: a second wall facing the first one braces the spy between them (Vandamme).</summary>
        [Uc("SBase.SPlayerController.s_SplitJump.PlayerMove", Sha1 = "9afe584a748bbf8b03f57504e785a95a4031333e")]
        [PortOf("uc:SBase.SPlayerController:s_SplitJump.PlayerMove", Spec = "spec/movement/wall-jump-split-vandamme.md#split-jump")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
            FVector w = default;
            if (pawn.nJumpNumber == 2 && self.CheckWallJump(x, y, z, ref w) && w.X * self.vWallNormal.X + w.Y * self.vWallNormal.Y < 0f)
            {
                float side = w.X * x.Y - w.Y * x.X;
                if (side < -MovementK.SplitSideDeadBand)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_VANDAMME_LT);
                    self.eventChangeState(MovementStateNames.VandammeBegin);
                }
                else if (side > MovementK.SplitSideDeadBand)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_VANDAMME_RT);
                    self.eventChangeState(MovementStateNames.VandammeBegin);
                }
            }

            self.eventUpdateRotation(DeltaTime, 1f);
            self.bPressedJump = false;
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.VandammeBegin")]
    public class VandammeBegin : UcActorState, IPlayerMove
    {
        public static readonly VandammeBegin Instance = new(UcMeta.VandammeBegin, null);

        protected VandammeBegin(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>A client tells the server the brace spot and the wall.</summary>
        [Uc("SBase.SPlayerController.VandammeBegin.BeginState", Sha1 = "2110975ee121dfa43881ead4f159f640209d0f42")]
        [PortOf("uc:SBase.SPlayerController:VandammeBegin.BeginState", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Role < ENetRole.ROLE_Authority)
            {
                pc.SBPawn?.SetInfoForVandamme(pc.vWallNormal);
            }
        }

        [Uc("SBase.SPlayerController.VandammeBegin.AnimEnd", Sha1 = "cf0afdfdaadee4cc56a1adcc082e4614b026e005")]
        [PortOf("uc:SBase.SPlayerController:VandammeBegin.AnimEnd", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_VANDAMME_NT);
                pc.eventChangeState(MovementStateNames.Vandamme);
            }
        }

        [Uc("SBase.SPlayerController.VandammeBegin.PlayerMove", Sha1 = "9f18f3db3f43208e5b567ef530fbb8c790696792")]
        [PortOf("uc:SBase.SPlayerController:VandammeBegin.PlayerMove", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.Pawn is { } p)
            {
                self.UpdateLimitedRotation(DeltaTime, -MovementK.VandammeViewYaw, MovementK.VandammeViewYaw, -MovementK.VandammeViewPitch,
                    MovementK.VandammeViewPitch, p.Rotation.Yaw, 0);
            }

            self.bPressedJump = false;
            self.bCrouchWithRoll = false;
            self.PackVandammeView();
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.Vandamme")]
    public class Vandamme : UcActorState, IPlayerMove, ICanUseCam
    {
        public static readonly Vandamme Instance = new(UcMeta.Vandamme, null);

        protected Vandamme(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.Vandamme.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:Vandamme.CanUseCam", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.Vandamme.BeginState", Sha1 = "588d9c9e0c6c3235bb7b5f7abdaae31b3b34e563")]
        [PortOf("uc:SBase.SPlayerController:Vandamme.BeginState", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.MovementCamera(ECamMode.ECM_SplitJump);
            pc.bCrouchWithRoll = false;
        }

        /// <summary>Braced bare-handed: the fire mode draws the pistol, a wide view, crouch drops.</summary>
        [Uc("SBase.SPlayerController.Vandamme.PlayerMove", Sha1 = "9b6de14dce6e4ce4689d54ee9f91c19194ad320b")]
        [PortOf("uc:SBase.SPlayerController:Vandamme.PlayerMove", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            if (self.bForceFireMode && !pawn.bStickyShocking)
            {
                if (self.bForceLaserFireMode)
                {
                    MovementService.HooksOf(self).SwitchLaser(self, true);
                }

                self.bFireMode = 0;
                self.bForceFireMode = false;
                if (!pawn.bWeaponTransit)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_VANDAMME_2H);
                    self.eventChangeState(MovementStateNames.VandammeWeapon);
                }
            }

            self.UpdateLimitedRotation(DeltaTime, -MovementK.VandammeViewYaw, MovementK.VandammeViewYaw, -MovementK.VandammeViewPitch,
                MovementK.VandammeViewPitch, pawn.Rotation.Yaw, 0);
            self.PackVandammeView();
            self.VandammeDrop();
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.VandammeWaitForWeapon")]
    public class VandammeWaitForWeapon : UcActorState, IPlayerMove
    {
        public static readonly VandammeWaitForWeapon Instance = new(UcMeta.VandammeWaitForWeapon, null);

        protected VandammeWaitForWeapon(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.VandammeWaitForWeapon.AnimEnd", Sha1 = "78585103baf8c9a396b97b6e877dd384228ef780")]
        [PortOf("uc:SBase.SPlayerController:VandammeWaitForWeapon.AnimEnd", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient())
            {
                pc.eventChangeState(MovementStateNames.VandammeWeapon);
            }
        }

        [Uc("SBase.SPlayerController.VandammeWaitForWeapon.PlayerMove", Sha1 = "e0ac8e9b64f1a7a75d58fd6a043343d53ea45655")]
        [PortOf("uc:SBase.SPlayerController:VandammeWaitForWeapon.PlayerMove", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.VandammeWeaponView(DeltaTime);
            self.PackVandammeView();
            self.VandammeDrop();
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.VandammeWeapon")]
    public class VandammeWeapon : UcActorState, IPlayerMove, ICanUseCam, ICanActivateLaser, ISwitchLaser
    {
        public static readonly VandammeWeapon Instance = new(UcMeta.VandammeWeapon, null);

        protected VandammeWeapon(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.VandammeWeapon.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:VandammeWeapon.CanUseCam", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.VandammeWeapon.CanActivateLaser", Sha1 = "e08952b783fe7e3a95022cbe3fbf42b35b6156b8")]
        [PortOf("uc:SBase.SPlayerController:VandammeWeapon.CanActivateLaser", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
        public virtual bool CanActivateLaser(SPlayerController self) => self.SBPawn is { bWeaponTransit: false };

        [Uc("SBase.SPlayerController.VandammeWeapon.BeginState", Sha1 = "2a08e918eb9d5bd4ddd323d7c4792350be26e3ff")]
        [PortOf("uc:SBase.SPlayerController:VandammeWeapon.BeginState", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
        public override void BeginState(Actor self) => ((SPlayerController)self).MovementCamera(ECamMode.ECM_SplitJumpFP);

        [Uc("SBase.SPlayerController.VandammeWeapon.SwitchLaser", Sha1 = "c7a4b9666591efeb2990524b26078f1157137c90")]
        [PortOf("uc:SBase.SPlayerController:VandammeWeapon.SwitchLaser", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
        public virtual void SwitchLaser(SPlayerController self) => self.WeaponSwitchLaser();

        /// <summary>Braced with the pistol out: holster on release, the narrow aiming view, crouch drops.</summary>
        [Uc("SBase.SPlayerController.VandammeWeapon.PlayerMove", Sha1 = "c3d35ae80bd43db2f9169bbfe0f5e8cac0518fee")]
        [PortOf("uc:SBase.SPlayerController:VandammeWeapon.PlayerMove", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            if ((self.bForceExitFireMode || (self.bTestBoutonLache && self.bFireMode == 1)) && !pawn.bStickyShocking)
            {
                self.bFireMode = 0;
                self.bForceExitFireMode = false;
                self.bForceFireMode = false;
                self.bTestBoutonLache = false;
                if (!pawn.bWeaponTransit)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_VANDAMME_0H);
                    self.eventChangeState(MovementStateNames.Vandamme);
                }
            }

            self.VandammeWeaponView(DeltaTime);
            self.PackVandammeView();
            self.VandammeDrop();
            self.SendMove(DeltaTime, default);
        }
    }

    /// <summary>The pistol view on the brace: yaw within 12000 of the pawn's yaw, pitch -11500 to 6730.</summary>
    private void VandammeWeaponView(float DeltaTime)
    {
        if (Pawn is { } p)
        {
            UpdateLimitedRotation(DeltaTime, -MovementK.VandammeWeaponViewYaw, MovementK.VandammeWeaponViewYaw,
                MovementK.VandammeWeaponPitchMin, MovementK.VandammeWeaponPitchMax, p.Rotation.Yaw, 0);
        }
    }
}
