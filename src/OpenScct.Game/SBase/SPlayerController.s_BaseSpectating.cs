using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_BaseSpectating: the auto state of every new controller and the root of the
// spectating states. Written from spec/game/spectating.md (rules 1 to 6).
public partial class SPlayerController
{
    /// <remarks>
    /// Not ported here: the free-fly PlayerMove and MyProcessMove (they need the MoveSmooth native), the vision
    /// SwitchHeadset (vision spec) and the HUD toggle of GadTorcheSwitch, and the ignored input functions that have no
    /// state dispatch yet. Fire and AltFire are ignored (the spectating states below give Fire its meaning); damage is
    /// ignored by the combat dispatch.
    /// </remarks>
    [Uc("SBase.SPlayerController.s_BaseSpectating")]
    public class s_BaseSpectating : UcActorState, IPlayerTick, IAntiBugTellServerIAmDead, IFire, IAltFire
    {
        public static readonly s_BaseSpectating Instance = new(UcMeta.s_BaseSpectating, null);

        protected s_BaseSpectating(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_BaseSpectating.BeginState", Sha1 = "e8843a4cbfa520331982a18eb02ec92e3a49b4d0")]
        [PortOf("uc:SBase.SPlayerController:s_BaseSpectating.BeginState", Spec = "spec/game/spectating.md#rules")]
        public override void BeginState(Actor self)
        {
            self.bCollideWorld = true;
        }

        /// <summary>Leaving any spectating state: HUD back, world collision off, vision reset, no target.</summary>
        [Uc("SBase.SPlayerController.s_BaseSpectating.EndState", Sha1 = "f7c3a9e0c9c7b06e41a7b7c8ffba0b54e88de0d9")]
        [PortOf("uc:SBase.SPlayerController:s_BaseSpectating.EndState", Spec = "spec/game/spectating.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.myHUD is { } hud)
            {
                hud.bHideHUD = false;
            }

            pc.bCollideWorld = false;
            if (pc.Goggle is not null)
            {
                pc.Ports.ResetVisionMode(pc);
            }

            pc.SpecTarget = null;
            pc.bForceReplicationForSpecTarget = true;
        }

        /// <summary>An only-spectator moves on to the visitor state.</summary>
        [Uc("SBase.SPlayerController.s_BaseSpectating.PlayerTick", Sha1 = "4af0e3a60aa301b3a9a98d22697d1658fccf2834")]
        [PortOf("uc:SBase.SPlayerController:s_BaseSpectating.PlayerTick", Spec = "spec/game/spectating.md#rules")]
        public virtual void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            if (pc.PlayerReplicationInfo is { bOnlySpectator: true })
            {
                pc.eventMirrorState(NAME_s_Spectating_Visitor);
            }
        }

        /// <summary>No view rotation in the base spectating state.</summary>
        [Uc("SBase.SPlayerController.s_BaseSpectating.UpdateRotation", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_BaseSpectating.UpdateRotation", Spec = "spec/game/spectating.md#rules")]
        public virtual void UpdateRotation(SPlayerController self, float DeltaTime, float maxPitch)
        {
        }

        /// <summary>Ignored while spectating.</summary>
        public void AntiBugTellServerIAmDead(SPlayerController self)
        {
        }

        /// <summary>Ignored in the base spectating state.</summary>
        public virtual void Fire(SPlayerController self, float F)
        {
        }

        /// <summary>Ignored while spectating.</summary>
        public void AltFire(SPlayerController self, float F)
        {
        }
    }

    /// <remarks>Not ported here: Say and TeamSay (chat spec) and SnapShot (screenshot).</remarks>
    [Uc("SBase.SPlayerController.s_MultiSpectating")]
    public class s_MultiSpectating : s_BaseSpectating
    {
        public static new readonly s_MultiSpectating Instance = new(UcMeta.s_MultiSpectating, null);

        protected s_MultiSpectating(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Does nothing here; the dead and visitor states use it to change target.</summary>
        [Uc("SBase.SPlayerController.s_MultiSpectating.Fire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_MultiSpectating.Fire", Spec = "spec/game/spectating.md#rules")]
        public override void Fire(SPlayerController self, float F)
        {
        }

        /// <summary>
        /// Input, the view rotation, then the camera report to the server. Not ported here: the client position update.
        /// </summary>
        [Uc("SBase.SPlayerController.s_MultiSpectating.PlayerTick", Sha1 = "10e92149ac517996057b4f088a41342797a016cf")]
        [PortOf("uc:SBase.SPlayerController:s_MultiSpectating.PlayerTick", Spec = "spec/game/spectating.md#rules")]
        public override void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.Ports.NativePlayerInput(pc, DeltaTime);
            PlayerMove(pc, DeltaTime);
            pc.SendUpdatePositionForServer();
        }

        /// <summary>Rotation only, no movement.</summary>
        [Uc("SBase.SPlayerController.s_MultiSpectating.PlayerMove", Sha1 = "0e7726633f6677a9dc875925dc73a13518837c07")]
        [PortOf("uc:SBase.SPlayerController:s_MultiSpectating.PlayerMove", Spec = "spec/game/spectating.md#rules")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime) => UpdateRotation(self, DeltaTime, 1f);

        /// <summary>
        /// Look rate 50 * sensitivity * input per second, truncated toward zero, yaw mirrored for a spy with the
        /// inverted spy camera, pitch mirrored with the inverted vertical axis, then pitch wrapped and clamped to
        /// -15000..15000.
        /// </summary>
        [Uc("SBase.SPlayerController.s_MultiSpectating.UpdateRotation", Sha1 = "abe7d41e70bff18e355a68f54336bdd713ea505e")]
        [PortOf("uc:SBase.SPlayerController:s_MultiSpectating.UpdateRotation", Spec = "spec/game/spectating.md#rules")]
        public override void UpdateRotation(SPlayerController self, float DeltaTime, float maxPitch)
        {
            FRotator view = self.Rotation.Normalize();
            int yaw = SvmMath.Trunc(GameFlow.K.SpectateLookRate * self.fSensibiliteSouris * DeltaTime * self.aTurn);
            if (self.SPawnType == PawnType.SPAWNOFFENSE && self.bInvertHorizontalAxisCamSpy)
            {
                view.Yaw -= yaw;
            }
            else
            {
                view.Yaw += yaw;
            }

            float pitchRate = self.bInvertVerticalAxis ? -GameFlow.K.SpectateLookRate : GameFlow.K.SpectateLookRate;
            view.Pitch += SvmMath.Trunc(pitchRate * self.fSensibiliteSouris * DeltaTime * self.aLookUp);
            view.Pitch &= 65535;
            if (view.Pitch > 32767)
            {
                view.Pitch -= 65536;
            }

            view.Pitch = SvmMath.Clamp(view.Pitch, -GameFlow.K.SpectatePitchLimit, GameFlow.K.SpectatePitchLimit);
            self.Rotation = view;
        }
    }

    /// <remarks>Not ported here: the ignored SwitchEnhancedReality (no state dispatch yet).</remarks>
    [Uc("SBase.SPlayerController.s_MultiSpectating_WhenKilled")]
    public class s_MultiSpectating_WhenKilled : s_MultiSpectating
    {
        public static new readonly s_MultiSpectating_WhenKilled Instance = new(UcMeta.s_MultiSpectating_WhenKilled, null);

        protected s_MultiSpectating_WhenKilled(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// The respawn timer (TimesRespawn, at least 0.2 s, with the due time kept for the HUD), every spectator
        /// watching this body loses it, and the player looks for a living teammate. TalkToMate (voice) is not ported.
        /// </summary>
        [Uc("SBase.SPlayerController.s_MultiSpectating_WhenKilled.BeginState", Sha1 = "463f217b6f0a9a7fcdd5215733a7fa777acc3876")]
        [PortOf("uc:SBase.SPlayerController:s_MultiSpectating_WhenKilled.BeginState", Spec = "spec/game/death-lives-respawn.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.fRespawnDate = pc.GameReplicationInfo?.TimesRespawn ?? 0;
            pc.SetTimer(SvmMath.Max(GameFlow.K.MinRespawnTimer, pc.fRespawnDate), false);
            pc.fRespawnDate += pc.Level.TimeSeconds;
            if (pc.Role == ENetRole.ROLE_Authority)
            {
                foreach (SPlayerController other in pc.XLevel.AllActors<SPlayerController>())
                {
                    if (pc.SBPawn is not null && ReferenceEquals(other.SpecTarget, pc.SBPawn))
                    {
                        other.SpecTarget = null;
                        other.PC_spectated = other;
                        other.bForceReplicationForSpecTarget = true;
                    }
                }
            }

            pc.fGrabDate = pc.Level.TimeSeconds + GameFlow.K.SpectateRecheck;
            pc.Server_FindNextFriend(false);
        }

        /// <summary>On the server, a player with lives left gets the HUD back and respawns; otherwise it stays here.</summary>
        [Uc("SBase.SPlayerController.s_MultiSpectating_WhenKilled.Timer", Sha1 = "8b465213337e4325f613e2f2bc5a49e2e1f240cb")]
        [PortOf("uc:SBase.SPlayerController:s_MultiSpectating_WhenKilled.Timer", Spec = "spec/game/death-lives-respawn.md#rules")]
        public override void Timer(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Role != ENetRole.ROLE_Authority || pc.PlayerReplicationInfo is not { NumLives: > 0 } pri)
            {
                return;
            }

            if (pc.myHUD is { } hud)
            {
                hud.bHideHUD = false;
            }

            pri.bIsSpectator = false;
            pc.ServerRespawn();
        }

        /// <summary>Fire: the next teammate.</summary>
        [Uc("SBase.SPlayerController.s_MultiSpectating_WhenKilled.Fire", Sha1 = "e787d4290240c9f0e9e63031733a9c0e10584310")]
        [PortOf("uc:SBase.SPlayerController:s_MultiSpectating_WhenKilled.Fire", Spec = "spec/game/spectating.md#rules")]
        public override void Fire(SPlayerController self, float F) => self.Server_FindNextFriend(false);

        /// <summary>Every second the target is checked again (and replaced when it is no longer valid).</summary>
        [Uc("SBase.SPlayerController.s_MultiSpectating_WhenKilled.PlayerTick", Sha1 = "9b9d65f0f770cab7521a49e5d6ac10cabe88dca1")]
        [PortOf("uc:SBase.SPlayerController:s_MultiSpectating_WhenKilled.PlayerTick", Spec = "spec/game/spectating.md#rules")]
        public override void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            if (pc.Level.TimeSeconds > pc.fGrabDate)
            {
                pc.fGrabDate = pc.Level.TimeSeconds + GameFlow.K.SpectateRecheck;
                pc.Server_FindNextFriend(true);
            }

            base.PlayerTick(self, DeltaTime);
        }
    }

    /// <remarks>Not ported here: the non-playing host's menu (ShowMenu) and the ignored SwitchEnhancedReality.</remarks>
    [Uc("SBase.SPlayerController.s_Spectating_Visitor")]
    public class s_Spectating_Visitor : s_MultiSpectating
    {
        public static new readonly s_Spectating_Visitor Instance = new(UcMeta.s_Spectating_Visitor, null);

        protected s_Spectating_Visitor(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Spectating_Visitor.BeginState", Sha1 = "8e3c5f22b8867a70fe0dc60fead266c4520ec72b")]
        [PortOf("uc:SBase.SPlayerController:s_Spectating_Visitor.BeginState", Spec = "spec/game/spectating.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.fGrabDate = pc.Level.TimeSeconds + GameFlow.K.SpectateRecheck;
            if (!pc.GameHost.IsDedicatedServerMode())
            {
                pc.Server_FindNextVisitor(false);
            }
        }

        /// <summary>Fire: the next player (the non-playing host leaves its spying mode instead).</summary>
        [Uc("SBase.SPlayerController.s_Spectating_Visitor.Fire", Sha1 = "b7ffc376512bf1a300782bb9ea7fc883fae99416")]
        [PortOf("uc:SBase.SPlayerController:s_Spectating_Visitor.Fire", Spec = "spec/game/spectating.md#rules")]
        public override void Fire(SPlayerController self, float F)
        {
            if (self.GameHost.IsDedicatedServerMode())
            {
                self.bDedicatedServerinSpyingMode = false;
                self.GameHost.ClientCall(self, new GameCall("ShowMenu"));
            }
            else
            {
                self.Server_FindNextVisitor(false);
            }
        }

        /// <summary>Every second the target is checked again; the non-playing host's spying mode ends with its target.</summary>
        [Uc("SBase.SPlayerController.s_Spectating_Visitor.PlayerTick", Sha1 = "f72354953d07a496e2491b71f3f1bc13dd4de3be")]
        [PortOf("uc:SBase.SPlayerController:s_Spectating_Visitor.PlayerTick", Spec = "spec/game/spectating.md#rules")]
        public override void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            if (pc.Level.TimeSeconds > pc.fGrabDate)
            {
                pc.fGrabDate = pc.Level.TimeSeconds + GameFlow.K.SpectateRecheck;
                if (!pc.GameHost.IsDedicatedServerMode())
                {
                    pc.Server_FindNextVisitor(true);
                }
                else if (pc.bDedicatedServerinSpyingMode && !pc.SpectateTargetStillValid())
                {
                    pc.bDedicatedServerinSpyingMode = false;
                    pc.GameHost.ClientCall(pc, new GameCall("ShowMenu"));
                }
            }

            base.PlayerTick(self, DeltaTime);
        }
    }

    // The original state name is all lower case.
#pragma warning disable CS8981
    /// <summary>The empty Engine-style spectating state kept by SPlayerController.</summary>
    [Uc("SBase.SPlayerController.spectating")]
    public class spectating : UcActorState
    {
        public static readonly spectating Instance = new(UcMeta.spectating, null);

        protected spectating(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }
    }
#pragma warning restore CS8981
}
