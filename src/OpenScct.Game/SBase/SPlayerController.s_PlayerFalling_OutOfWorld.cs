using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerFalling_OutOfWorld: a fall below the world: the player is dead at once and
// the fall goes on until a respawn line or a stop. Written from spec/movement/jump-fall-land.md (falling out of the
// world).
//
// The source declares it as extending s_Inactive; the generated state metadata lists no super state, so this port
// keeps the parts it needs (no input movement, view only) itself.
public partial class SPlayerController
{
    /// <remarks>
    /// Not ported here: the camera position memory and the screen fades (view), the flag drop, the fire and throw
    /// input clears and the suicide score (objectives, equipment and scoring specs); the ignored
    /// ClientUpdatePosition, PawnTakeDamage and ReinitPawnBecauseOfDivergences (netcode and combat).
    /// </remarks>
    [Uc("SBase.SPlayerController.s_PlayerFalling_OutOfWorld")]
    public class s_PlayerFalling_OutOfWorld : UcActorState, IPlayerTick, IPlayerMove, IMyNotifyLanded, IMyFalling, IFellOutOfWorld
    {
        public static readonly s_PlayerFalling_OutOfWorld Instance = new(UcMeta.s_PlayerFalling_OutOfWorld, null);

        protected s_PlayerFalling_OutOfWorld(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// The dead fall: the respawn line 8 cylinder heights above the ground straight below (or 1500 below the
        /// pawn), the world counted as the killer on the server, no health and an instant respawn.
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerFalling_OutOfWorld.BeginState", Sha1 = "55579f97f14cd73daf2772edbfc5bac657e95143")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerFalling_OutOfWorld.BeginState", Spec = "spec/movement/jump-fall-land.md#falling-out-of-the-world")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ResetZones();
            if (pc.IAmOnClient())
            {
                pawn.ChangeEnumAction(SBasePawn.SA_FALL_DEAD);
                FVector end = pawn.Location - new FVector(0f, 0f, MovementK.OutOfWorldProbe);
                pc.fRespawnZLocation = MovementNatives.Trace(pawn, out FVector hit, out _, end, pawn.Location, true) is not null
                    ? hit.Z + MovementK.OutOfWorldRespawnHeights * pawn.CollisionHeight
                    : pawn.Location.Z - MovementK.OutOfWorldNoHitDrop;
            }

            if (pc.Role == ENetRole.ROLE_Authority && pawn.Health > 0)
            {
                pc.Level.Game?.Killed(pc, pc, pawn, SDamageType.SFell);
            }

            pawn.Health = 0;
            pc.bInstantRespawn = true;
        }

        /// <summary>Back to the start spot, hidden.</summary>
        [Uc("SBase.SPlayerController.s_PlayerFalling_OutOfWorld.EndState", Sha1 = "5a560429ff2306dd68b22e62aaf600fc2ca75a0b")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerFalling_OutOfWorld.EndState", Spec = "spec/movement/jump-fall-land.md#falling-out-of-the-world")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.StartSpot is { } start && pc.Pawn is { } pawn)
            {
                MovementNatives.FarMoveActor(pawn, start.Location);
                pawn.Rotation = start.Rotation;
                pc.Location = start.Location;
                pc.Rotation = start.Rotation;
            }

            if (pc.Pawn is { } p)
            {
                p.bHidden = true;
            }
        }

        /// <summary>Below the respawn line or nearly still: the dead state, then the tick.</summary>
        [Uc("SBase.SPlayerController.s_PlayerFalling_OutOfWorld.PlayerTick", Sha1 = "48143db8f3ef2fcf60cf836b17f35a014ec62344")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerFalling_OutOfWorld.PlayerTick", Spec = "spec/movement/jump-fall-land.md#falling-out-of-the-world")]
        public virtual void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && pc.SBPawn is { } pawn &&
                (pawn.Location.Z <= pc.fRespawnZLocation || pawn.Velocity.Size() < MovementK.OutOfWorldSpeedExit))
            {
                pawn.ChangeEnumAction(SBasePawn.SA_NONE);
                pc.eventChangeState(MovementStateNames.s_Dead);
            }

            pc.PlayerTick(DeltaTime);
        }

        /// <summary>No input movement: the view turns and the pawn gets no acceleration.</summary>
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.eventUpdateRotation(DeltaTime, 1f);
            self.SendMove(DeltaTime, default);
        }

        [Uc("SBase.SPlayerController.s_PlayerFalling_OutOfWorld.MyNotifyLanded", Sha1 = "c50f18143042b8355e3f2965a6fb0762fc85a349")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerFalling_OutOfWorld.MyNotifyLanded", Spec = "spec/movement/jump-fall-land.md#falling-out-of-the-world")]
        public virtual bool MyNotifyLanded(SPlayerController self, FVector HitNormal, float fTimeLeft, Actor? LandedOn)
        {
            if (self.IAmOnClient() && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_NONE);
                self.eventChangeState(MovementStateNames.s_Dead);
            }

            return true;
        }

        /// <summary>Ignored.</summary>
        public void MyFalling(SPlayerController self, float LeftTime)
        {
        }

        /// <summary>Ignored.</summary>
        public void FellOutOfWorld(SPlayerController self)
        {
        }
    }
}
