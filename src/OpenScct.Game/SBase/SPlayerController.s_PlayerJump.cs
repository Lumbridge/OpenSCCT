using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerJump: the airborne state of a jump (and through PlayerFalling of every fall):
// air steering, the airborne GE grabs, the landing decision and the fall damage. Written from
// spec/movement/jump-fall-land.md.
public partial class SPlayerController
{
    /// <remarks>
    /// Not ported here: PawnTakeDamage, PlayAnimDmg, ExplosionHit, CanBeBlasted and AltFire (combat and equipment
    /// specs), and the split jump start, whose native reports nothing until the split jump states are ported.
    /// </remarks>
    [Uc("SBase.SPlayerController.s_PlayerJump")]
    public class s_PlayerJump : UcActorState, IPlayerMove, IMyProcessMove, IMyNotifyLanded, INotifyBump, IFeetLedge, IPipe,
        INarrowLadder, IWaistLedge, ILedge, IHandOverHand, IZipLine, ICanBeDropAttacked, ICanBeElbowShocked
    {
        public static readonly s_PlayerJump Instance = new(UcMeta.s_PlayerJump, null);

        protected s_PlayerJump(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The normal zones, no back-ledge turn, the zip slide sound stopped, the walking camera; the merc's weapon jumps.</summary>
        [Uc("SBase.SPlayerController.s_PlayerJump.BeginState", Sha1 = "5bfb89f75f148f49af91ed89a66cb1c0d36381b1")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.BeginState", Spec = "spec/movement/jump-fall-land.md#starting-a-jump")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.SBPawn?.ResetZones();
            pc.bTestBackFeetGrab = false;
            pc.SBPawn?.StopSound(pc.Snd_ZLine_Gliss);
            pc.bPressedJump = false;
            pc.MovementCamera(ECamMode.ECM_Walking);
            if (pc.SBPawn is SPAWNDEFENSE { WeaponLoaded: > 0 })
            {
                MovementService.HooksOf(pc).PlayWeaponClip(pc, MovementWeaponClip.Jump);
            }
        }

        /// <summary>
        /// The server applies the fall damage when leaving the air (and again when the jump clip ended mid-fall: the
        /// apex is not reset), from the drop below the apex.
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerJump.EndState", Sha1 = "3d606209e00963f0000d23c5314fbaf05146c1ca")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.EndState", Spec = "spec/movement/jump-fall-land.md#fall-damage")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Role != ENetRole.ROLE_Authority || pc.SBPawn is not { } pawn)
            {
                return;
            }

            int dmg = FallDamage(pawn, pc.fZMaxFalling - pawn.Location.Z);
            if (dmg > 0)
            {
                pc.PawnTakeDamage(dmg, pawn, pawn.Location, new FVector(0f, 0f, 1f), SDamageType.SFell);
            }
        }

        /// <summary>The jump clip ended (base channel): the fall clip and PlayerFalling for the rest of the air time.</summary>
        [Uc("SBase.SPlayerController.s_PlayerJump.AnimEnd", Sha1 = "61d2debd3131d8879b62d863a902cc5498a80249")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.AnimEnd", Spec = "spec/movement/jump-fall-land.md#in-the-air-s_playerjumpplayermove-playerfalling-inherits-it")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(pawn.WeaponLoaded == 0 ? SBasePawn.SA_FALL_ST_0H : SBasePawn.SA_FALL_ST_2H);
            pc.eventChangeState(MovementStateNames.PlayerFalling);
        }

        /// <summary>
        /// One airborne frame: stand up, keep the raw stick for a landing roll, the fence and the split jump, the GE
        /// grabs, then the steering (the back-ledge turn or the spy's air control) and the merc's facing.
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerJump.PlayerMove", Sha1 = "ca0f0b3a96b62b8c95f335f324362e7b25464d6a")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.PlayerMove", Spec = "spec/movement/jump-fall-land.md#in-the-air-s_playerjumpplayermove-playerfalling-inherits-it")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            float now = self.Level.TimeSeconds;
            self.ZoneHorsCrouch();
            self.iOldMove = self.iMove;
            self.aStrafeRoulade = self.aStrafe;
            self.aForwardRoulade = self.aForward;

            if (pawn is SPawnAttaque && pawn.Health > 0 && pawn.WeaponLoaded == 0 &&
                now - self.m_LastSpecialMoveTime >= MovementK.FenceGrabCooldown && (!pawn.bIsCrouched || pawn.CanUnCrouch()) &&
                self.GetStateTable().Find(MovementStateNames.GrabFence) is not null &&
                pawn.CheckFence(out self.vNormalFence, out _, pawn.Location) == 1)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_FENCEGRAB_JUMP);
                self.eventChangeState(MovementStateNames.GrabFence);
                self.bPressedJump = false;
            }

            float sq = MovementK.StickDeadSquare;
            self.bWantsToMove = !(self.aForward > -sq && self.aForward < sq && self.aStrafe > -sq && self.aStrafe < sq);

            if (self.bPressedJump)
            {
                self.bPressedJump = false;
                if (pawn.SPawnType == PawnType.SPAWNOFFENSE && pawn.nJumpNumber == 1 && pawn.Velocity.Z < MovementK.SplitJumpApexWindow &&
                    pawn.Velocity.Z > -MovementK.SplitJumpApexWindow)
                {
                    pawn.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
                    if (self.CheckWallJump(x, y, z, ref self.vWallNormal))
                    {
                        self.SplitJumpOffWall(pawn, x);
                    }
                }
            }

            if (self.bUsing != self.oldbUsing)
            {
                self.bToggleUsing = self.bUsing;
                self.oldbUsing = self.bUsing;
            }

            if (pawn.m_AttackTarget is null)
            {
                self.bFindGE = false;
                self.eventCheckArmsZone();
                if (!self.bFindGE)
                {
                    self.eventCheckFeetZone();
                }
            }

            FVector newAccel = default;
            if (self.bTestBackFeetGrab)
            {
                self.eventUpdateRotation(DeltaTime, 1f);
                FRotator r = pawn.Rotation;
                float step = MovementK.BackLedgeTurnRate * DeltaTime;
                float d = MovementMath.Int((float)(MovementMath.Yaw(pawn.vBackFeetNormal) - r.Yaw)) & 65535;
                if (d < 32768f && d > step)
                {
                    d = step;
                }
                else if (d >= 32768f && d < 65536f - step)
                {
                    d = 65536f - step;
                }

                r.Yaw += MovementMath.Int(d);
                pawn.Rotation = r;
            }
            else
            {
                self.ClampStick();
                self.eventUpdateRotation(DeltaTime, 1f);
                if (!self.bBlockControlForJump)
                {
                    newAccel = self.UpdatePawnRotationMarioJump(DeltaTime, self.ForwardRequest, self.StrafeRequest);
                }

                if (newAccel.Size() < 1f)
                {
                    newAccel = default;
                }
            }

            pawn.GroundSpeed = GroundSpeedFor(pawn, newAccel);
            if (pawn is SPAWNDEFENSE)
            {
                pawn.Rotation = new FRotator(0, self.Rotation.Yaw, 0);
                self.MyDesiredRotation = self.Rotation;
                self.UpdateDesiredRotation(self.MyDesiredRotation);
                self.LocalMyDesiredRotation = self.MyDesiredRotation;
            }

            self.SendMove(DeltaTime, newAccel);
        }

        /// <summary>Only the pawn's acceleration.</summary>
        [Uc("SBase.SPlayerController.s_PlayerJump.MyProcessMove", Sha1 = "2865c07e4531add9d95668618fd88e9ab9efc288")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.MyProcessMove", Spec = "spec/movement/jump-fall-land.md#in-the-air-s_playerjumpplayermove-playerfalling-inherits-it")]
        public virtual void MyProcessMove(SPlayerController self, float DeltaTime, FVector newAccel)
        {
            if (self.Pawn is { } pawn)
            {
                pawn.Acceleration = newAccel;
            }
        }

        /// <summary>The landing table: by the drop below the apex, the stick, the hands and the crouch wish.</summary>
        [Uc("SBase.SPlayerController.s_PlayerJump.MyNotifyLanded", Sha1 = "613b153073494038f9a2db6bb81107310c101147")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.MyNotifyLanded", Spec = "spec/movement/jump-fall-land.md#landing-s_playerjumpmynotifylanded-playerfalling-inherits-it")]
        public virtual bool MyNotifyLanded(SPlayerController self, FVector HitNormal, float fTimeLeft, Actor? LandedOn)
        {
            self.MyNotifyLanded(HitNormal, fTimeLeft, LandedOn);
            if (self.RemoteRole == ENetRole.ROLE_AutonomousProxy || self.SBPawn is not { } pawn)
            {
                return true;
            }

            if (pawn.bIsRolling)
            {
                self.eventChangeState(MovementStateNames.s_PlayerRolling);
                return true;
            }

            if (pawn.Velocity.Size2D() < MovementK.LandingStillSpeed)
            {
                self.bWantsToMove = false;
            }

            if (!self.IAmOnClient())
            {
                return true;
            }

            self.LandingTable(pawn);
            return true;
        }

        /// <summary>A mover carrying the pawn up slower than it rises is not a fall: the apex restarts here.</summary>
        [Uc("SBase.SPlayerController.s_PlayerJump.NotifyBump", Sha1 = "061be96c8fcb42f695dec09b8c3c226c9b787054")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.NotifyBump", Spec = "spec/movement/jump-fall-land.md#in-the-air-s_playerjumpplayermove-playerfalling-inherits-it")]
        public virtual bool NotifyBump(SPlayerController self, Actor Other)
        {
            if (Other is Mover && self.Pawn is { } pawn && Other.Velocity.Z < pawn.Velocity.Z)
            {
                self.fZMaxFalling = pawn.Location.Z;
            }

            return false;
        }

        [Uc("SBase.SPlayerController.s_PlayerJump.FeetLedge", Sha1 = "463dfb89a99aaf222f76a99cdac9191f24ae6dfb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.FeetLedge", Spec = "spec/movement/jump-fall-land.md#ge-grabs-while-airborne")]
        public virtual void FeetLedge(SPlayerController self)
        {
            if (self.SBPawn is not { } pawn || pawn.bIsRolling || pawn.bIsCrouched ||
                self.GetStateTable().Find(MovementStateNames.s_FeetLedgeDetect) is null)
            {
                return;
            }

            self.bFindGE = true;
            pawn.ChangeEnumAction(SBasePawn.SA_FEETLEDGE_DETECT);
            pawn.SendZipPToServer();
            self.eventChangeState(MovementStateNames.s_FeetLedgeDetect);
        }

        [Uc("SBase.SPlayerController.s_PlayerJump.Pipe", Sha1 = "664c788ce361c84c6ed16d02b03089e710cf049e")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.Pipe", Spec = "spec/movement/jump-fall-land.md#ge-grabs-while-airborne")]
        public virtual void Pipe(SPlayerController self, FVector locationEnd, FRotator orientationEnd)
        {
            if (self.SBPawn is not SPawnAttaque pawn || pawn.bWeaponTransit || pawn.WeaponLoaded != 0 ||
                self.GetStateTable().Find(MovementStateNames.s_GrabPipe) is null)
            {
                return;
            }

            self.bFindGE = true;
            self.MovementCameraSmooth(SmoothDefault);
            pawn.ChangeEnumAction(SBasePawn.SA_PIPE_GRABFALL);
            pawn.SendZipPAndNormalToServer();
            self.eventChangeState(MovementStateNames.s_GrabPipe);
        }

        [Uc("SBase.SPlayerController.s_PlayerJump.NarrowLadder", Sha1 = "0bfef4d63f8f1cd19d248a48209bd705ea0947f6")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.NarrowLadder", Spec = "spec/movement/jump-fall-land.md#ge-grabs-while-airborne")]
        public virtual void NarrowLadder(SPlayerController self)
        {
            if (self.SBPawn is not SPawnAttaque pawn || pawn.bWeaponTransit || pawn.WeaponLoaded != 0 ||
                self.Level.TimeSeconds - self.m_LastSpecialMoveTime < MovementK.LadderGrabCooldown ||
                self.GetStateTable().Find(MovementStateNames.s_LadderGrabFall) is null)
            {
                return;
            }

            self.bFindGE = true;
            self.MovementCameraSmooth(SmoothDefault);
            pawn.ChangeEnumAction(pawn.Velocity.Z < MovementK.LadderCatchSpeed ? SBasePawn.SA_LADDER_CATCH : SBasePawn.SA_LADDER_GRABFALL);
            pawn.SendZipPAndNormalToServer();
            self.eventChangeState(MovementStateNames.s_LadderGrabFall);
        }

        [Uc("SBase.SPlayerController.s_PlayerJump.WaistLedge", Sha1 = "514372f37f4e1c91ddcc508ac85ac27537ca5a52")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.WaistLedge", Spec = "spec/movement/jump-fall-land.md#ge-grabs-while-airborne")]
        public virtual void WaistLedge(SPlayerController self, bool bCanGoNoCrouch)
        {
            if (self.SBPawn is not SPawnAttaque pawn || pawn.ZipP1 == pawn.LastZipP || pawn.WeaponLoaded != 0 ||
                self.GetStateTable().Find(MovementStateNames.s_WaistLedgeDetect) is null)
            {
                return;
            }

            self.bFindGE = true;
            self.bWaistLedgeCanGoUnCrouch = bCanGoNoCrouch;
            pawn.ChangeEnumAction(SBasePawn.SA_WAISTLEDGE_DETECT);
            pawn.SendZipPToServer();
            self.eventChangeState(MovementStateNames.s_WaistLedgeDetect);
        }

        /// <summary>A ledge at hand height: the spy (standing, unarmed, not the ledge just left) hangs from it.</summary>
        [Uc("SBase.SPlayerController.s_PlayerJump.Ledge", Sha1 = "de2b2c923f6c5601121099d5e600d0c4e33d1e58")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.Ledge", Spec = "spec/movement/ledge.md#catching-a-ledge")]
        public virtual void Ledge(SPlayerController self)
        {
            self.ZoneHorsCrouch();
            if (self.SBPawn is not SPawnAttaque pawn || pawn.bIsCrouched || pawn.WeaponLoaded != 0 ||
                !(SvmMath.Abs(pawn.ZipP1.Z - pawn.LastZipP.Z) > MovementK.LedgeRegrabHeight) ||
                self.GetStateTable().Find(MovementStateNames.GrabLedge) is null)
            {
                return;
            }

            self.bFindGE = true;
            pawn.ChangeEnumAction(SBasePawn.SA_LEDGE_GRAB);
            self.m_LastSpecialMoveTime = self.Level.TimeSeconds;
            pawn.SendZipPToServer();
            self.eventChangeState(MovementStateNames.GrabLedge);
        }

        [Uc("SBase.SPlayerController.s_PlayerJump.HandOverHand", Sha1 = "df1acd351b3da16317cb0240d82fde6b450a9818")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.HandOverHand", Spec = "spec/movement/jump-fall-land.md#ge-grabs-while-airborne")]
        public virtual void HandOverHand(SPlayerController self)
        {
            if (self.SBPawn is not { } pawn || pawn is SPAWNDEFENSE || pawn.bWeaponTransit || pawn.ZipP1 == pawn.LastZipP ||
                self.GetStateTable().Find(MovementStateNames.s_GrabPipH) is null)
            {
                return;
            }

            self.bFindGE = true;
            pawn.SendZipPToServer();
            pawn.ChangeEnumAction(SBasePawn.SA_PIPH_GRAB);
            self.eventChangeState(MovementStateNames.s_GrabPipH);
        }

        /// <summary>A zip line within reach and a clear path to it: the spy grabs it (2 s after the last special move).</summary>
        [Uc("SBase.SPlayerController.s_PlayerJump.ZipLine", Sha1 = "c3e1c27dc5310995f72e698b3f24aa47eef09999")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.ZipLine", Spec = "spec/movement/zip-line.md")]
        public virtual void ZipLine(SPlayerController self, FVector locationEnd, FRotator orientationEnd)
        {
            if (self.SBPawn is not { } pawn || pawn is SPAWNDEFENSE || pawn.bWeaponTransit || pawn.WeaponLoaded != 0 ||
                self.Level.TimeSeconds - self.m_LastSpecialMoveTime < MovementK.ZipGrabCooldown ||
                self.GetStateTable().Find(MovementStateNames.s_GrabZip) is null)
            {
                return;
            }

            var extent = new FVector(pawn.CollisionRadius, pawn.CollisionRadius, pawn.CollisionHeight);
            if (MovementNatives.Trace(pawn, out _, out _, locationEnd, pawn.Location, true, extent) is not null)
            {
                return;
            }

            self.bFindGE = true;
            pawn.SendZipPToServer();
            pawn.ChangeEnumAction(SBasePawn.SA_ZIP_GRAB);
            self.eventChangeState(MovementStateNames.s_GrabZip);
        }

        [Uc("SBase.SPlayerController.s_PlayerJump.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.CanBeDropAttacked", Spec = "spec/movement/jump-fall-land.md")]
        public virtual bool CanBeDropAttacked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PlayerJump.CanBeElbowShocked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerJump.CanBeElbowShocked", Spec = "spec/movement/jump-fall-land.md")]
        public virtual bool CanBeElbowShocked(SPlayerController self) => true;
    }

    [Provenance(ProvenanceKind.Estimated, "The camera smoothing value of the airborne grabs (camera spec)")]
    private const float SmoothDefault = 5f;

    /// <summary>
    /// The landing table of s_PlayerJump.MyNotifyLanded (controlling side): the result by the drop below the apex
    /// (450 is a big landing), the stick, the hands and the crouch wish.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "jump-fall-land.md landing table")]
    public void LandingTable(SBasePawn pawn)
    {
        float h = fZMaxFalling - pawn.Location.Z;
        bool moving = bWantsToMove;
        bool armed = pawn.WeaponLoaded != 0;
        bool crouchWanted = bCrouchWithRoll || pawn.bIsCrouched;
        bool merc = pawn is SPAWNDEFENSE;
        bool big = !(h < pawn.Fell_MaxHeightForBigLand);
        IMovementHooks hooks = MovementService.HooksOf(this);
        if (merc && armed)
        {
            hooks.PlayWeaponClip(this, big ? MovementWeaponClip.LandHigh : MovementWeaponClip.Land);
        }

        if (big)
        {
            if (crouchWanted)
            {
                MakeCrouched(pawn);
            }

            int action = armed
                ? (crouchWanted ? SBasePawn.SA_LAND_CR_2H_QUIET : SBasePawn.SA_LAND_ST_2H_HIGH)
                : SBasePawn.SA_LAND_ST_0H_HIGH;
            pawn.ChangeEnumAction(action);
            eventChangeState(MovementStateNames.PlayerLanding);
            return;
        }

        if (!moving)
        {
            if (crouchWanted)
            {
                MakeCrouched(pawn);
                pawn.ChangeEnumAction(armed ? SBasePawn.SA_LAND_CR_2H_QUIET : SBasePawn.SA_LAND_CR_0H_QUIET);
                eventChangeState(MovementStateNames.PlayerLandingSilent);
            }
            else
            {
                pawn.ChangeEnumAction(armed ? SBasePawn.SA_LAND_ST_2H_LOW : merc ? SBasePawn.SA_LAND_ST_0H_HIGH : SBasePawn.SA_LAND_ST_0H_LOW);
                eventChangeState(MovementStateNames.PlayerLanding);
            }

            return;
        }

        if (armed)
        {
            pawn.PlayLandingSound();
            if (crouchWanted)
            {
                MakeCrouched(pawn);
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_2H);
                eventChangeState(MovementStateNames.s_PlayerWalkingCr2H);
            }
            else
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_2H);
                eventChangeState(MovementStateNames.s_PlayerWalkingSt2H);
            }

            return;
        }

        if (merc)
        {
            if (crouchWanted)
            {
                if (pawn.bIsCrouched)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
                }

                bCrouchWithRoll = false;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_LAND_ST_0H_HIGH);
            eventChangeState(MovementStateNames.PlayerLanding);
            return;
        }

        if (crouchWanted)
        {
            MakeCrouched(pawn);
            if ((iOldMove & cMoveForward) != 0)
            {
                FVector dir = new FVector(aStrafeRoulade, aForwardRoulade, 0f).SafeNormal();
                aStrafeRoulade = MovementK.CrouchedRollMagnitude * dir.X;
                aForwardRoulade = MovementK.CrouchedRollMagnitude * dir.Y;
                pawn.ChangeEnumAction(SBasePawn.SA_ROLLING);
                eventChangeState(MovementStateNames.s_PlayerRolling);
            }
            else
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H);
                eventChangeState(MovementStateNames.s_PlayerWalkingCr0H);
            }

            return;
        }

        pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
        pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_LAND_OH);
        eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
    }

    /// <summary>The landing's "make crouched": the crouch action when not crouched yet.</summary>
    private static void MakeCrouched(SBasePawn pawn)
    {
        if (!pawn.bIsCrouched)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
        }
    }
}
