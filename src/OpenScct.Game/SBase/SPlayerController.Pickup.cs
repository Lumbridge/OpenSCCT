using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;
using K = OpenScct.Game.Objectives.K;
using MK = OpenScct.Game.Modes.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController pickup states: the walk to a pickable object and its pickup (Disk Hunt disks and other
// pickables), and the walk to the Story disk or its drop zone with the drop. Written from
// spec/objectives/interaction-system.md rule 22, spec/objectives/disk-flag.md rules 9 to 14 and 25 to 29 and
// spec/objectives/disk-hunt.md rules 7 and 10; the objective rules are in Objectives/DiskCarryRules.cs and the damage
// handlers in Combat/CombatDispatch.cs.
public partial class SPlayerController
{
    /// <summary>The walking action for the pawn's stance and weapon (the GoTo states' end, on the input machine).</summary>
    private void WalkActionForStance()
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        bool armed = pawn.WeaponLoaded != 0;
        pawn.ChangeEnumAction(pawn.bIsCrouched
            ? (armed ? SBasePawn.SA_WALK_CR_2H : SBasePawn.SA_WALK_CR_0H)
            : (armed ? SBasePawn.SA_WALK_ST_2H : SBasePawn.SA_WALK_ST_0H));
    }

    /// <summary>The GoTo states' end (EndState): the repositioning target cleared, the walking action restored.</summary>
    private void EndPickupWalk()
    {
        if (!IAmOnClient() || SBPawn is not { } pawn)
        {
            return;
        }

        pawn.vVandammePos = pawn.Location;
        pawn.SetInfoForVandamme(pawn.Rotation.Vector());
        WalkActionForStance();
    }

    /// <summary>Back to the unarmed walk for the stance (the pickup and drop animations' end).</summary>
    private void BackToUnarmedWalk()
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        if (pawn.bIsCrouched)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H);
            eventChangeState(K.s_PlayerWalkingCr0H);
        }
        else
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
            eventChangeState(K.s_PlayerWalkingSt0H);
        }
    }

    /// <summary>The still move of the pickup states: camera rotation and zero acceleration.</summary>
    private void PickupStillMove(float DeltaTime, bool bNoJump)
    {
        if (bNoJump)
        {
            bPressedJump = false;
        }

        UpdateCamRotation(DeltaTime);
        SendMove(DeltaTime, FVector.Zero);
    }

    /// <summary>
    /// The generic stand spot in front of a pickable entry (interaction-system.md rule 22): along the line from the
    /// pawn to the entry, at the pawn's height, one collision radius short of it when the entry is low, two
    /// otherwise; facing the entry.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerController.s_Pickup_GoTo.BeginState stand spot")]
    public static void GetPickupSpot(SInteractObject entry, Pawn pawn, out FVector Spot, out FVector Facing)
    {
        FVector off = entry.vRelativePositionForInteraction;
        Facing = entry.Location - pawn.Location;
        Facing.Z = 0f;
        Facing.Rotation().GetAxes(out FVector x, out FVector y, out FVector z);
        Spot = entry.Location + x * off.X + y * off.Y + z * off.Z;
        Spot.Z = pawn.Location.Z;
        float back = entry.Location.Z < pawn.Location.Z - MK.PickupLowEntry ? pawn.CollisionRadius : pawn.CollisionRadius * 2f;
        Spot -= x * back;
    }

    /// <summary>Walking to a pickable object (interaction-system.md rule 22).</summary>
    [Uc("SBase.SPlayerController.s_Pickup_GoTo")]
    public class s_Pickup_GoTo : UcActorState, IPlayerMove, INotifyBump, INotifyHitWall, ITopSynchro, IFire, IAltFire
    {
        public static readonly s_Pickup_GoTo Instance = new(UcMeta.s_Pickup_GoTo, null);

        protected s_Pickup_GoTo(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>On the input machine: reposition to the stand spot; on both machines: drop any hand item.</summary>
        [Uc("SBase.SPlayerController.s_Pickup_GoTo.BeginState", Sha1 = "37febdede86f156c34635ff6b70bba2a8c179932")]
        [PortOf("uc:SBase.SPlayerController:s_Pickup_GoTo.BeginState", Spec = "spec/objectives/interaction-system.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                if (pawn.Physics != EPhysics.PHYS_Repositionnal)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_REPOS);
                }

                if (pc.IManager?.GetCurrentInteraction() is { } entry)
                {
                    GetPickupSpot(entry, pawn, out FVector spot, out FVector facing);
                    pawn.vVandammePos = spot;
                    pawn.SetInfoForVandamme(facing);
                }
            }

            pc.DiscardPickup();
        }

        [Uc("SBase.SPlayerController.s_Pickup_GoTo.EndState", Sha1 = "9597815c295bc8fabc1d0390605bc021623f17e6")]
        [PortOf("uc:SBase.SPlayerController:s_Pickup_GoTo.EndState", Spec = "spec/objectives/interaction-system.md#rules")]
        public override void EndState(Actor self) => ((SPlayerController)self).EndPickupWalk();

        /// <summary>A bump ends the walk where it is and goes on to the pickup test.</summary>
        [Uc("SBase.SPlayerController.s_Pickup_GoTo.NotifyBump", Sha1 = "c4cb019d58e7615618584d82248312dc61739059")]
        [PortOf("uc:SBase.SPlayerController:s_Pickup_GoTo.NotifyBump", Spec = "spec/objectives/interaction-system.md#rules")]
        public bool NotifyBump(SPlayerController self, Actor Other)
        {
            StopWalk(self);
            return false;
        }

        [Uc("SBase.SPlayerController.s_Pickup_GoTo.NotifyHitWall", Sha1 = "f866ac5b71b85106a564001988a92e9990319819")]
        [PortOf("uc:SBase.SPlayerController:s_Pickup_GoTo.NotifyHitWall", Spec = "spec/objectives/interaction-system.md#rules")]
        public bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall)
        {
            StopWalk(self);
            return true;
        }

        [Uc("SBase.SPlayerController.s_Pickup_GoTo.PlayerMove", Sha1 = "cda313c629ee0d1fc36e29188d6582c4f8808730")]
        [PortOf("uc:SBase.SPlayerController:s_Pickup_GoTo.PlayerMove", Spec = "spec/objectives/interaction-system.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.PickupStillMove(DeltaTime, false);

        /// <summary>
        /// At the spot (or stopped): with a clear line to the entry, the pickup with the low or forward action for the
        /// stance and the entry's height; otherwise back to walking.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Pickup_GoTo.TopSynchro", Sha1 = "a3f1a864092aa3f26a1fa89e459586a83331ad5b")]
        [PortOf("uc:SBase.SPlayerController:s_Pickup_GoTo.TopSynchro", Spec = "spec/objectives/interaction-system.md#rules")]
        public void TopSynchro(SPlayerController self)
        {
            if (!self.IAmOnClient() || self.SBPawn is not { } pawn)
            {
                return;
            }

            FVector target = self.IManager?.GetCurrentInteraction()?.Location ?? pawn.Location;
            if (MovementNatives.Trace(self, out _, out _, target, pawn.Location, true) is not null)
            {
                self.eventChangeState(K.s_PlayerWalkingSt0H);
                return;
            }

            self.eventChangeState(K.s_Pickup);
            if (pawn.bIsCrouched)
            {
                pawn.ChangeEnumAction(target.Z < pawn.Location.Z - MK.PickupLowEntry ? SBasePawn.SA_PICKUP_CR_UP : SBasePawn.SA_PICKUP_CR_FD);
            }
            else
            {
                pawn.ChangeEnumAction(target.Z < pawn.Location.Z - MK.PickupLowStanding ? SBasePawn.SA_PICKUP_ST_UP : SBasePawn.SA_PICKUP_ST_FD);
            }
        }

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        private void StopWalk(SPlayerController pc)
        {
            if (pc.IAmOnClient())
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_EXIT_REPOS);
                TopSynchro(pc);
            }
        }
    }

    /// <summary>The pickup animation: the object is taken at its start and rides on the thigh at its end.</summary>
    [Uc("SBase.SPlayerController.s_Pickup")]
    public class s_Pickup : UcActorState, IPlayerMove, IFire, IAltFire
    {
        public static readonly s_Pickup Instance = new(UcMeta.s_Pickup, null);

        protected s_Pickup(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The local player asks the server for the selected object.</summary>
        [Uc("SBase.SPlayerController.s_Pickup.BeginState", Sha1 = "dbcba74040c53d7ef0faac015aedd5274f50ec41")]
        [PortOf("uc:SBase.SPlayerController:s_Pickup.BeginState", Spec = "spec/objectives/disk-hunt.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && ReferenceEquals(pc.Level.GetLocalPlayerController(), pc))
            {
                pc.SetHandItemFromInteraction(pc.IManager?.GetCurrentInteraction());
            }
        }

        /// <summary>
        /// The carried flag, or else the newest disk, moves to the thigh (server). Until script animation notifies are
        /// dispatched, a disk still in the hand gets the pickup notify here first, which frees the hand.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Pickup.EndState", Sha1 = "974d465f9b303f25b1ba69542ff802acea6ffd03")]
        [PortOf("uc:SBase.SPlayerController:s_Pickup.EndState", Spec = "spec/objectives/disk-hunt.md#rules")]
        [Provenance(ProvenanceKind.Estimated, "AnimNotifyPickup run at the end of the pickup when the animation did not raise it (disk-hunt.md open question 2)")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Role == ENetRole.ROLE_Authority && pc.SBPawn is { HandItem: SDisk } pawn)
            {
                DiskCarryRules.AnimNotifyPickup(pawn);
            }

            DiskCarryRules.PickupEndState(pc);
        }

        [Uc("SBase.SPlayerController.s_Pickup.AnimEnd", Sha1 = "c3fb2564a2bd367641e02c1b495d235f60117d65")]
        [PortOf("uc:SBase.SPlayerController:s_Pickup.AnimEnd", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            if (Channel == 0)
            {
                ((SPlayerController)self).BackToUnarmedWalk();
            }
        }

        [Uc("SBase.SPlayerController.s_Pickup.PlayerMove", Sha1 = "db0f937b665261c680d92de08aee560b5b1a1df8")]
        [PortOf("uc:SBase.SPlayerController:s_Pickup.PlayerMove", Spec = "spec/objectives/disk-flag.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.PickupStillMove(DeltaTime, true);

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }
    }

    /// <summary>Walking to the Story disk or to its drop zone (disk-flag.md rules 9, 10 and 27).</summary>
    [Uc("SBase.SPlayerController.s_FlagAndDropZone_GoTo")]
    public class s_FlagAndDropZone_GoTo : UcActorState, IPlayerMove, INotifyBump, INotifyHitWall, ITopSynchro, IFire, IAltFire
    {
        public static readonly s_FlagAndDropZone_GoTo Instance = new(UcMeta.s_FlagAndDropZone_GoTo, null);

        protected s_FlagAndDropZone_GoTo(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>On the input machine: reposition 67 uu in front of the entry; on both machines: drop any hand item.</summary>
        [Uc("SBase.SPlayerController.s_FlagAndDropZone_GoTo.BeginState", Sha1 = "31ee4c89e68649fccce0ce7534fe42e796807626")]
        [PortOf("uc:SBase.SPlayerController:s_FlagAndDropZone_GoTo.BeginState", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                if (pawn.Physics != EPhysics.PHYS_Repositionnal)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_REPOS);
                }

                if (DiskCarryRules.GoToBeginState(pc, out FVector facing))
                {
                    pawn.SetInfoForVandamme(facing);
                }
            }

            pc.DiscardPickup();
        }

        [Uc("SBase.SPlayerController.s_FlagAndDropZone_GoTo.EndState", Sha1 = "9597815c295bc8fabc1d0390605bc021623f17e6")]
        [PortOf("uc:SBase.SPlayerController:s_FlagAndDropZone_GoTo.EndState", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void EndState(Actor self) => ((SPlayerController)self).EndPickupWalk();

        /// <summary>A bump or a wall cancels the walk.</summary>
        [Uc("SBase.SPlayerController.s_FlagAndDropZone_GoTo.NotifyBump", Sha1 = "47783fd4220b19eae85035865592141d054eeeee")]
        [PortOf("uc:SBase.SPlayerController:s_FlagAndDropZone_GoTo.NotifyBump", Spec = "spec/objectives/disk-flag.md#rules")]
        public bool NotifyBump(SPlayerController self, Actor Other)
        {
            Cancel(self);
            return true;
        }

        [Uc("SBase.SPlayerController.s_FlagAndDropZone_GoTo.NotifyHitWall", Sha1 = "47783fd4220b19eae85035865592141d054eeeee")]
        [PortOf("uc:SBase.SPlayerController:s_FlagAndDropZone_GoTo.NotifyHitWall", Spec = "spec/objectives/disk-flag.md#rules")]
        public bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall)
        {
            Cancel(self);
            return true;
        }

        [Uc("SBase.SPlayerController.s_FlagAndDropZone_GoTo.PlayerMove", Sha1 = "d114b5fc111bd343ac612d85ad5b9cb28b0a7705")]
        [PortOf("uc:SBase.SPlayerController:s_FlagAndDropZone_GoTo.PlayerMove", Spec = "spec/objectives/disk-flag.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.PickupStillMove(DeltaTime, false);

        /// <summary>At the spot: the disk pickup for a pickup entry, the drop otherwise.</summary>
        [Uc("SBase.SPlayerController.s_FlagAndDropZone_GoTo.TopSynchro", Sha1 = "19d8027a0c3c321887d60fcb084ddc3af4aca626")]
        [PortOf("uc:SBase.SPlayerController:s_FlagAndDropZone_GoTo.TopSynchro", Spec = "spec/objectives/disk-flag.md#rules")]
        public void TopSynchro(SPlayerController self)
        {
            if (!self.IAmOnClient())
            {
                return;
            }

            if (DiskCarryRules.WalkEnd(self) == EFlagWalkEnd.Pickup)
            {
                self.eventChangeState(K.s_Pickup);
                self.SBPawn?.ChangeEnumAction(SBasePawn.SA_PICKUP_FLAG);
            }
            else
            {
                self.eventChangeState(K.s_PutFlagInDropZone);
                self.SBPawn?.ChangeEnumAction(SBasePawn.SA_DROP_FLAG);
            }
        }

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        private static void Cancel(SPlayerController pc)
        {
            if (pc.IAmOnClient())
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_EXIT_REPOS);
                pc.GotoWaitingState();
            }
        }
    }

    /// <summary>The drop at a drop zone: delivered when the animation ends with the disk still in the hand.</summary>
    [Uc("SBase.SPlayerController.s_PutFlagInDropZone")]
    public class s_PutFlagInDropZone : UcActorState, IPlayerMove, IFire, IAltFire
    {
        public static readonly s_PutFlagInDropZone Instance = new(UcMeta.s_PutFlagInDropZone, null);

        protected s_PutFlagInDropZone(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PutFlagInDropZone.BeginState", Sha1 = "e1808c056a94193a2fc49a39677329ec1dee81e0")]
        [PortOf("uc:SBase.SPlayerController:s_PutFlagInDropZone.BeginState", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void BeginState(Actor self) => DiskCarryRules.PutFlagBeginState((SPlayerController)self);

        /// <summary>The delivery (server) and, on both machines, the unarmed walking action for the stance.</summary>
        [Uc("SBase.SPlayerController.s_PutFlagInDropZone.EndState", Sha1 = "e4bd696c055d00b0c585b452ccdbc26d25fded83")]
        [PortOf("uc:SBase.SPlayerController:s_PutFlagInDropZone.EndState", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            DiskCarryRules.PutFlagEndState(pc);
            if (pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(pawn.bIsCrouched ? SBasePawn.SA_WALK_CR_0H : SBasePawn.SA_WALK_ST_0H);
            }
        }

        [Uc("SBase.SPlayerController.s_PutFlagInDropZone.AnimEnd", Sha1 = "59db46e1a3693d0cf18707392e369380d79d8ac4")]
        [PortOf("uc:SBase.SPlayerController:s_PutFlagInDropZone.AnimEnd", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            if (Channel == 0)
            {
                ((SPlayerController)self).BackToUnarmedWalk();
            }
        }

        [Uc("SBase.SPlayerController.s_PutFlagInDropZone.PlayerMove", Sha1 = "06ca9f1bffaffbeda02c9f422d71ae57b25209a6")]
        [PortOf("uc:SBase.SPlayerController:s_PutFlagInDropZone.PlayerMove", Spec = "spec/objectives/disk-flag.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.PickupStillMove(DeltaTime, true);

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }
    }
}
