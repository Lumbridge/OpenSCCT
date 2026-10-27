using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerWalkingMario: the spy's unarmed "Mario" movement (the pawn turns toward the
// stick and runs where it faces), where the jump, the roll, Use, back to wall and the walking GE grabs start.
// Written from spec/movement/walking.md and spec/movement/roll.md.
public partial class SPlayerController
{

    /// <summary>
    /// ChangeState to a traversal state, only when this port has it. A state that is not ported yet (barrier,
    /// wall jump, fence, ladder, hand over hand, coop) leaves the player where he is instead of in no state.
    /// </summary>
    public bool ChangeStateIfPorted(Name State, Name Label = default)
    {
        if (GetStateTable().Find(State) is null)
        {
            return false;
        }

        eventChangeState(State, Label);
        return true;
    }

    /// <remarks>
    /// Not ported here: GadgetQuickChoice (the spy's quick gadget, equipment spec) and PerformCoopMvt (coop.md).
    /// </remarks>
    [Uc("SBase.SPlayerController.s_PlayerWalkingMario")]
    public class s_PlayerWalkingMario : s_PlayerWalking, IPlayerMove, ICanCrouch, IPipe, IAutoFeetLedge, INarrowLadder,
        ITopPipe, ITopLadder, INotifyHitWall, IProcessWorldInteract, IPerformCoopMvt
    {
        public static new readonly s_PlayerWalkingMario Instance = new(UcMeta.s_PlayerWalkingMario, s_PlayerWalking.Instance);

        protected s_PlayerWalkingMario(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario.CanCrouch", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario.CanCrouch", Spec = "spec/movement/walking.md#spy-mario-walking-s_playerwalkingmarioplayermove")]
        public virtual bool CanCrouch(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario.CanActivateLaser", Spec = "spec/movement/walking.md#permission-predicates")]
        public override bool CanActivateLaser(SPlayerController self) => false;

        /// <summary>
        /// One frame of Mario walking: crouch requests, the Mario stick and rotation, the ground speed, the Use and
        /// back-to-wall edges, the roll, the walking GE search, the jump, then the move.
        /// </summary>
        /// <remarks>
        /// The barrier vault and the wall jump start follow their natives (CheckBarrier, CheckGEForWallJump), which
        /// find nothing until those moves are ported.
        /// </remarks>
        [Uc("SBase.SPlayerController.s_PlayerWalkingMario.PlayerMove", Sha1 = "2168293298b2d905f077784680f516027002a19a")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario.PlayerMove", Spec = "spec/movement/walking.md#spy-mario-walking-s_playerwalkingmarioplayermove")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn || self.DeadInWalking(pawn))
            {
                return;
            }

            self.ProcessCrouch();
            self.ClampStick();
            self.MarioStickScale(pawn);
            self.GroundPitch = 0;
            self.eventUpdateRotation(DeltaTime, 1f);
            FVector newAccel = self.UpdatePawnRotationMario(DeltaTime, self.ForwardRequest, self.StrafeRequest);
            self.MarioGroundSpeed(pawn, ref newAccel);

            if (self.bUsing != self.oldbUsing)
            {
                self.bToggleUsing = self.bUsing;
                self.oldbUsing = self.bUsing;
            }

            if (self.bUseBackToWall != self.oldbUseBackToWall)
            {
                self.bToggleBTW = self.bUseBackToWall;
                self.oldbUseBackToWall = self.bUseBackToWall;
            }

            if (pawn.bIsCrouched && (self.bRollAction || self.bToggleBTW != 0) && self.CeilingAbove(pawn))
            {
                self.bRollAction = false;
                self.bToggleBTW = 0;
            }

            bool bCantRollCuzColl = false;
            if (self.bRollAction)
            {
                self.StartRoll(pawn, ref bCantRollCuzColl);
            }

            self.bRollAction = false;
            if (self.bToggleUsing == 1 && !self.bPressedJump)
            {
                IMovementHooks hooks = MovementService.HooksOf(self);
                hooks.DiscardPickup(self);
                if (!pawn.bIsRolling)
                {
                    _ = hooks.UseSearch(self, 0, bCantRollCuzColl) || hooks.UseSearch(self, 1, bCantRollCuzColl) ||
                        hooks.UseSearch(self, 2, bCantRollCuzColl) || InteractionMenuRules.DoInteraction(self, self.eventCanInteract());
                }
            }
            else if (self.bToggleBTW == 1)
            {
                self.eventProcessWorldInteract();
            }

            float now = self.Level.TimeSeconds;
            if (self.bPressedJump && !pawn.bIsCrouched && pawn.GroundSpeed > MovementK.WallJumpSpeedGate)
            {
                pawn.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
                if (self.CheckGEForWallJump(x, y, z, out float gz))
                {
                    float dz = gz - pawn.Location.Z;
                    self.bPressedJump = false;
                    self.m_LastSpecialMoveTime = now;
                    if (dz > MovementK.WallJumpGeLow && dz < MovementK.WallJumpGeHigh)
                    {
                        self.fGEZLocation = gz;
                        self.ChangeStateIfPorted(MovementStateNames.s_FirstWallJumpPrepareFromJump);
                    }
                    else
                    {
                        self.ChangeStateIfPorted(MovementStateNames.s_FirstWallJumpSaltoPrepareFromJump);
                    }
                }
            }

            self.bToggleUsing = 0;
            self.bToggleBTW = 0;
            pawn.ResetZones();
            self.bFindGE = false;
            if (self.bPressedJump)
            {
                self.fDateLaunchedLastQuickGadget = SvmMath.Max(self.fDateLaunchedLastQuickGadget, now - MovementK.QuickGadgetDateFloor);
                if (self.CheckBarrier())
                {
                    self.bFindGE = true;
                    if (pawn.bIsCrouched)
                    {
                        if (pawn.CanUnCrouch())
                        {
                            pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
                            pawn.ChangeEnumAction(SBasePawn.SA_BARRIER_JUMP_DETECT);
                            pawn.SendZipPToServer();
                            self.ChangeStateIfPorted(MovementStateNames.s_BarrierJumpDetect);
                            self.bPressedJump = false;
                        }
                    }
                    else
                    {
                        pawn.ChangeEnumAction(SBasePawn.SA_BARRIER_JUMP_DETECT);
                        pawn.SendZipPToServer();
                        self.ChangeStateIfPorted(MovementStateNames.s_BarrierJumpDetect);
                        self.bPressedJump = false;
                    }
                }
            }

            if (!self.bFindGE)
            {
                self.eventCheckArmsZone();
                if (!self.bFindGE)
                {
                    self.eventCheckFeetZone();
                }
            }

            if (self.bPressedJump && pawn.Base is not null && pawn.Base is not SBasePawn)
            {
                if (pawn.bIsCrouched)
                {
                    if (pawn.CanUnCrouch())
                    {
                        pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
                        pawn.MyDoJump();
                        pawn.ChangeEnumAction(SBasePawn.SA_JUMP_ST_0H);
                        self.bCrouchWithRoll = false;
                        self.eventChangeState(MovementStateNames.s_PlayerJump);
                    }
                }
                else
                {
                    pawn.MyDoJump();
                    pawn.ChangeEnumAction(SBasePawn.SA_JUMP_ST_0H);
                    self.eventChangeState(MovementStateNames.s_PlayerJump);
                }
            }

            self.bPressedJump = false;
            if (self.bFindGE)
            {
                newAccel = default;
            }

            self.SendMove(DeltaTime, newAccel);
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario.Pipe", Sha1 = "c9b7d7314b80a1fbbd933218a4b319a6c49fb1b8")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario.Pipe", Spec = "spec/movement/walking.md#spy-mario-walking-s_playerwalkingmarioplayermove")]
        public virtual void Pipe(SPlayerController self, FVector locationEnd, FRotator orientationEnd)
        {
            self.bFindGE = true;
            if (self.SBPawn is not { } pawn || pawn.bWeaponTransit)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_PIPE_GRAB);
            pawn.SendZipPAndNormalToServer();
            self.ChangeStateIfPorted(MovementStateNames.s_GrabPipe);
        }

        /// <summary>
        /// The back-to-wall key went down: with the stick near the centre, unarmed and not drawing, a wall behind
        /// the spy puts his back to it, a wall in front turns him round onto it. True when entered.
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingMario.ProcessWorldInteract", Sha1 = "67e45eb248f964e03fe96dd0d5b3fd4287c96642")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario.ProcessWorldInteract", Spec = "spec/movement/back-to-wall.md#entering-s_playerwalkingmarioprocessworldinteract")]
        public virtual bool ProcessWorldInteract(SPlayerController self)
        {
            if (self.bToggleBTW != 1 || self.SBPawn is not { } pawn || pawn.bWeaponTransit || pawn.WeaponLoaded != 0 ||
                !(self.aForward * self.aForward + self.aStrafe * self.aStrafe < MovementK.BtwEntryStickSq) ||
                self.GetStateTable().Find(MovementStateNames.s_EnterBTW) is null)
            {
                return false;
            }

            if (self.CheckBTW(true))
            {
                pawn.ChangeEnumAction(SBasePawn.SA_BTW_ENTER);
                pawn.SetInfoForVandamme(self.vWallNormal);
            }
            else if (self.CheckBTW())
            {
                pawn.SetInfoForVandamme(self.vWallNormal);
                pawn.ChangeEnumAction(SBasePawn.SA_BTW_ENTERBACK);
            }
            else
            {
                return false;
            }

            self.eventChangeState(MovementStateNames.s_EnterBTW);
            return true;
        }

        /// <summary>A low ledge in front of the feet: the spy hops by himself (an ordinary jump this frame).</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingMario.AutoFeetLedge", Sha1 = "86f82e9a49eeace9ed4d5edc19668fe32c4cc5f9")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario.AutoFeetLedge", Spec = "spec/movement/walking.md#spy-mario-walking-s_playerwalkingmarioplayermove")]
        public virtual void AutoFeetLedge(SPlayerController self)
        {
            self.bFindGE = true;
            self.bPressedJump = true;
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario.NarrowLadder", Sha1 = "b16aff330b699fb0b92cb920e4726ba8089ade88")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario.NarrowLadder", Spec = "spec/movement/walking.md#spy-mario-walking-s_playerwalkingmarioplayermove")]
        public virtual void NarrowLadder(SPlayerController self)
        {
            self.bFindGE = true;
            if (self.SBPawn is not { } pawn || pawn.bWeaponTransit)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_LADDER_GRAB);
            pawn.SendZipPAndNormalToServer();
            self.ChangeStateIfPorted(MovementStateNames.s_GrabLadder);
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario.TopPipe", Sha1 = "e57ba232598510c8d0e6f4745f9cfb30358e21f6")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario.TopPipe", Spec = "spec/movement/walking.md#spy-mario-walking-s_playerwalkingmarioplayermove")]
        public virtual void TopPipe(SPlayerController self, bool bLeft)
        {
            self.bFindGE = true;
            if (self.SBPawn is not { } pawn || pawn.bWeaponTransit || self.GetStateTable().Find(MovementStateNames.s_WaitAtTopPipe) is null)
            {
                return;
            }

            pawn.SendZipPAndNormalToServer();
            pawn.SetInfoForVandamme(pawn.m_geoNormal);
            pawn.ChangeEnumAction(SBasePawn.SA_REPOS);
            pawn.bIsLeftPart = bLeft;
            self.eventChangeState(MovementStateNames.s_WaitAtTopPipe);
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario.TopLadder", Sha1 = "44d003e6f0f719a515e61b9ce64e30b354247001")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario.TopLadder", Spec = "spec/movement/walking.md#spy-mario-walking-s_playerwalkingmarioplayermove")]
        public virtual void TopLadder(SPlayerController self)
        {
            self.bFindGE = true;
            if (self.SBPawn is not { } pawn || pawn.bWeaponTransit || self.GetStateTable().Find(MovementStateNames.s_WaitAtTopLadder) is null)
            {
                return;
            }

            pawn.SetInfoForVandamme(pawn.m_geoNormal);
            pawn.SendZipPAndNormalToServer();
            pawn.ChangeEnumAction(SBasePawn.SA_REPOS);
            self.eventChangeState(MovementStateNames.s_WaitAtTopLadder);
        }

        /// <summary>
        /// A wall hit while walking unarmed: a climbable fence face starts the fence grab. The server copy of a remote
        /// player, a crouched spy without room to stand, an armed or rolling spy and a weapon transit leave it to the
        /// native (false).
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingMario.NotifyHitWall", Sha1 = "7eaafdfcf5d3b09f9b6ada010b81691d572d4a43")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario.NotifyHitWall", Spec = "spec/movement/walking.md#spy-mario-walking-s_playerwalkingmarioplayermove")]
        public virtual bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall)
        {
            if (self.SBPawn is not { } pawn || pawn.bWeaponTransit || pawn.bIsRolling || pawn.WeaponLoaded != 0)
            {
                return false;
            }

            if (self.RemoteRole == ENetRole.ROLE_AutonomousProxy || (pawn.bIsCrouched && !pawn.CanUnCrouch()))
            {
                return false;
            }

            if (pawn.CheckFence(out self.vNormalFence, out _, pawn.Location) == 1 && self.GetStateTable().Find(MovementStateNames.GrabFence) is not null)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_FENCEGRAB);
                self.eventChangeState(MovementStateNames.GrabFence);
                return true;
            }

            return false;
        }

        /// <summary>The co-op key: start a co-op move.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingMario.PerformCoopMvt", Sha1 = "17d403d8148963821a153996fb981e55fff4b772")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario.PerformCoopMvt", Spec = "spec/movement/coop.md#starting-docoopinteraction-from-performcoopmvt-in-mario-walking-or-onledge")]
        public virtual void PerformCoopMvt(SPlayerController self) => self.DoCoopInteraction();
    }
}
