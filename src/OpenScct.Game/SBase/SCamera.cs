using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SCamera: a map security camera (distinct from the merc's SCamNetwork ring) that sweeps PatrolAngle either
/// side of its placed yaw, spots a living spy inside its cone and range, turns to follow him and, once he has been
/// seen for DetectionDelay seconds, fires its Event (an SAlarm) again every DetectionDelay while he stays in view.
/// Written from spec/devices/security-camera.md. The authority decides (state code, CheckPresence, the native
/// tick); LightState and CameraDesiredRotation replicate and every machine turns the head and swaps the LED skin.
/// </summary>
public partial class SCamera
{
    public static new readonly StateTable ClassStates = new(SBasicGamePlayObject.ClassStates,
        s_Inactive.Instance, s_LockedByChaff.Instance, s_Patrol.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>LightState values: chaff-locked (black LED), inactive (black), patrolling (green), alert (red).</summary>
    public const byte LightLocked = 0;

    public const byte LightInactive = 1;

    public const byte LightPatrol = 2;

    public const byte LightAlert = 3;

    /// <summary>Rotator units per degree used for angles (PatrolAngle, half the cone angle).</summary>
    [Provenance(ProvenanceKind.Decoded, "SCamera.PreBeginPlay PatrolAngle * 91; game 0x10ab5f25 VisibilityConeAngle * 91.0")]
    public const int UnitsPerDegree = 91;

    [Provenance(ProvenanceKind.Decoded, "SCamera.PreBeginPlay: trace 200 uu behind, camera set 24 uu off the wall")]
    public const float WallProbe = 200f;

    [Provenance(ProvenanceKind.Decoded, "SCamera.PreBeginPlay")]
    public const float WallOffset = 24f;

    [Provenance(ProvenanceKind.Decoded, "SCamera s_Patrol: Sleep(fCheckPeriode), 0.5 s idle and 0.2 s after a detection")]
    public const float IdleCheckPeriod = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "SCamera s_Patrol")]
    public const float AlertCheckPeriod = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "SCamera s_Patrol: PlaySound(CamDzzz, SLOT_Misc, 0.7, , 64, , true)")]
    public const float AlertSoundVolume = 0.7f;

    [Provenance(ProvenanceKind.Decoded, "SCamera s_Patrol / s_LockedByChaff sound radius 64")]
    public const float CamSoundRadius = 64f;

    /// <summary>The pitch an inactive or locked camera droops to.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10ab63b3: CameraDesiredRotation.Pitch = 0xffffd120")]
    public const int DroopPitch = -12000;

    /// <summary>Pitch turn rate of the head (the yaw uses RotationVelocity).</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10ab64df: pitch step 10000 * DeltaTime")]
    public const int PitchRate = 10000;

    /// <summary>How close to a sweep end the yaw must be before the sweep turns back.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10ab6336: |yaw offset - end| < 100")]
    public const int SweepEndTolerance = 100;

    /// <summary>The line test from the camera to a spy: level, movers, level geometry, stop at first hit.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10ab5e5d: SingleLineCheck flags 0x286")]
    public const uint SightTraceFlags = 0x286;

    /// <summary>Radians to rotator units in CheckPresence (the engine's 10430.2 constant, not 32768 / pi).</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10c10c5c = 10430.21875f, used at 0x10ab5ea5 and 0x10ab5eda")]
    public const float RadiansToRotator = 10430.21875f;

    private static readonly AssetRef ChaffExplodeSound = new("Armes_impacts.Impacts.RA_camSurveil_EXPLO");
    private static readonly ClassRef WallSupportClass = new("SBase.SBasicGamePlayObject");
    private static readonly ClassRef MalfunctionEmitterClass = new("SBase.SMalfunctionEmitter");

    /// <summary>The emitter dies and the wall mount goes with the camera.</summary>
    [Uc("SBase.SCamera.Destroyed", Sha1 = "3c58d74ab47e29b796a9d6ce4c15a17776c5b23f")]
    [PortOf("uc:SBase.SCamera:Destroyed", Spec = "spec/devices/security-camera.md#start-up")]
    [Provenance(ProvenanceKind.Estimated, "Emitter.Kill is not ported: the malfunction emitter is destroyed")]
    protected override void Destroyed()
    {
        MalfunctionEmitter?.Destroy();
        WallSupport?.Destroy();
        base.Destroyed();
    }

    /// <summary>
    /// Keeps the placed rotation, the cone cosine, sets the camera 24 uu off the wall behind it and spawns the wall
    /// mount (bAutoWallSupport), then aims the first sweep PatrolAngle to the positive side.
    /// </summary>
    [Uc("SBase.SCamera.PreBeginPlay", Sha1 = "10cb4600ce43f99b3eb7de6f25983686a218765d")]
    [PortOf("uc:SBase.SCamera:PreBeginPlay", Spec = "spec/devices/security-camera.md#start-up")]
    protected override void PreBeginPlay()
    {
        InitialRotation = Rotation;
        VisibilityConeAngleCos = SvmMath.Cos(VisibilityConeAngle / 2f * 3.1415927f / 180f);
        if (bAutoWallSupport)
        {
            Rotation.GetAxes(out FVector X, out _, out _);
            FVector start = Location;
            FVector end = start - (X * WallProbe);
            FRotator wallRotation;
            if (MovementNatives.Trace(this, out FVector hitLocation, out FVector hitNormal, end, start, false) is not null)
            {
                SetLocation(hitLocation + (hitNormal * WallOffset));
                wallRotation = hitNormal.Rotation();
            }
            else
            {
                wallRotation = Rotation;
                wallRotation.Pitch = 0;
                wallRotation.Roll = 0;
            }

            WallSupport = DeviceClasses.Spawn(this, WallSupportClass, this, Name.None, Location, wallRotation) as SBasicGamePlayObject;
            if (WallSupport is not null)
            {
                WallSupport.SetStaticMesh(WallSupportStaticmesh);
                WallSupport.SetDrawType(EDrawType.DT_StaticMesh);
                WallSupport.bHidden = false;
                WallSupport.RemoteRole = ENetRole.ROLE_SimulatedProxy;
                WallSupport.HeatIntensity = WallSupportHeatIntensity;
            }
        }

        CameraDesiredRotation = InitialRotation;
        PatrolDirection = 1;
        CameraDesiredRotation.Yaw += PatrolAngle * UnitsPerDegree;
        bSomethingIsDetected = false;
        fCheckPeriode = IdleCheckPeriod;
    }

    /// <summary>A lock always lasts StickyNeutralizationTime, whatever the chaff asks.</summary>
    [Uc("SBase.SCamera.SetLockedByChaff", Sha1 = "0a81162abb30c5c8d48bda129c0e2aa6d6b3be5a")]
    [PortOf("uc:SBase.SCamera:SetLockedByChaff", Spec = "spec/devices/security-camera.md#chaff-and-sticky-lock")]
    public override void SetLockedByChaff(float Length) => base.SetLockedByChaff(StickyNeutralizationTime);

    /// <summary>
    /// Native 1328: the first living spy (in the level's pawn order) the camera has a clear line to, whose pitch and
    /// yaw from the camera are both within half the cone of the camera's current rotation and who is closer than
    /// VisibilityMaxDistance. Records him in DetectedPawn and DetectedLocation.
    /// </summary>
    [UcNative("ASCamera::execCheckPresence", Exe = 0x10ab5dd0)]
    [PortOf("nat:SBase.SCamera.CheckPresence", Spec = "spec/devices/security-camera.md#detection")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ab5df0: SPawnAttaque with Health > 0, line check 0x286 hitting nothing or the pawn, |pitch|,|yaw| < VisibilityConeAngle * 91, distance < VisibilityMaxDistance")]
    public bool CheckPresence()
    {
        float cone = VisibilityConeAngle * UnitsPerDegree;
        foreach (SPawnAttaque pawn in XLevel.AllActors<SPawnAttaque>())
        {
            if (pawn.bDeleteMe || pawn.Health <= 0)
            {
                continue;
            }

            if (MovementNatives.Driver(this) is { } driver &&
                driver.Collision.LineCheck(out FCheckResult hit, this, pawn.Location, Location, default, (ETraceFlags)SightTraceFlags) &&
                !ReferenceEquals(hit.Actor, pawn))
            {
                continue;
            }

            FVector d = pawn.Location - Location;
            int yaw = SvmMath.Trunc(SvmMath.Atan2(d.Y, d.X) * RadiansToRotator);
            int pitch = SvmMath.Trunc(SvmMath.Atan2(d.Z, SvmMath.Sqrt((d.X * d.X) + (d.Y * d.Y))) * RadiansToRotator);
            int dPitch = FRotator.NormalizeAxis(pitch - Rotation.Pitch);
            int dYaw = FRotator.NormalizeAxis(yaw - Rotation.Yaw);
            if (!(Math.Abs(dYaw) < cone) || !(Math.Abs(dPitch) < cone))
            {
                continue;
            }

            if (!(d.Size() < VisibilityMaxDistance))
            {
                continue;
            }

            DetectedPawn = pawn;
            DetectedLocation = pawn.Location;
            return true;
        }

        return false;
    }

    /// <summary>
    /// ASCamera::Tick. Every machine: a changed LightState swaps the LED skin (Skins[1]) and the alarm beep, and
    /// a locked camera gets its malfunction sparks. The authority steers: an alert camera follows the spy's yaw
    /// within its sweep and, every DetectionDelay, fires Event while CheckPresence still sees someone, else calms
    /// down and sweeps back towards the side it is on; a patrolling camera turns at each sweep end; an inactive
    /// or locked one droops. Every machine then turns the head towards CameraDesiredRotation (yaw at
    /// RotationVelocity, pitch at 10000 a second, roll at once).
    /// </summary>
    [UcNative("ASCamera::Tick", Exe = 0x10ab5fe0)]
    [PortOf("eng:ASCamera::Tick", Spec = "spec/devices/security-camera.md#native-tick")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ab5fe0 (field offsets from the generated metadata: LightState +0x3b8, LocalLightState +0x3b9, bSomethingIsDetected bit 2 of +0x3bc)")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (bDeleteMe)
        {
            return;
        }

        if (LightState != LocalLightState)
        {
            LocalLightState = LightState;
            ApplyLightState();
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            SteerOnAuthority();
        }

        TurnHead(DeltaSeconds);
    }

    /// <summary>The LED skin and beep of LocalLightState, and the malfunction sparks while locked.</summary>
    private void ApplyLightState()
    {
        switch (LocalLightState)
        {
            case LightPatrol:
                SetSkinSlot(1, LightGreen);
                AmbientSound = AssetRef.None;
                break;
            case LightAlert:
                SetSkinSlot(1, LightRed);
                AmbientSound = CamBipbip;
                break;
            case < LightPatrol:
                SetSkinSlot(1, LightOff);
                AmbientSound = AssetRef.None;
                break;
        }

        if (LocalLightState != LightLocked)
        {
            MalfunctionEmitter?.Destroy();
            MalfunctionEmitter = null;
        }
        else if (MalfunctionEmitter is null)
        {
            MalfunctionEmitter = SpawnMalfunctionEmitter();
        }
    }

    [Provenance(ProvenanceKind.Estimated, "game 0x10ab6093 spawns a class it loads by name at Location and Rotation; SMalfunctionEmitter is the SBase name next to the camera's in the name table")]
    private Emitter? SpawnMalfunctionEmitter() =>
        DeviceClasses.Spawn(this, MalfunctionEmitterClass, null, Name.None, Location, Rotation) as Emitter;

    private void SetSkinSlot(int index, AssetRef skin)
    {
        while (Skins.Count <= index)
        {
            Skins.Add(AssetRef.None);
        }

        Skins[index] = skin;
    }

    private void SteerOnAuthority()
    {
        if (LightState <= LightInactive)
        {
            CameraDesiredRotation = InitialRotation;
            CameraDesiredRotation.Pitch = DroopPitch;
            CameraDesiredRotation = NormalizePitchYaw(CameraDesiredRotation);
            return;
        }

        if (bSomethingIsDetected && DetectedPawn is not null)
        {
            FRotator toSpy = (DetectedLocation - Location).Rotation();
            if (Math.Abs(FRotator.NormalizeAxis(toSpy.Yaw - InitialRotation.Yaw)) < PatrolAngle * UnitsPerDegree)
            {
                CameraDesiredRotation = new FRotator(InitialRotation.Pitch, toSpy.Yaw, toSpy.Roll);
            }

            if (DetectionDelay < Level.TimeSeconds - fDetectionTime)
            {
                if (CheckPresence())
                {
                    TriggerEvent(Event, this, DetectedPawn);
                    fDetectionTime = Level.TimeSeconds;
                }
                else
                {
                    bSomethingIsDetected = false;
                    LightState = LightPatrol;
                    FRotator offset = Rotation - InitialRotation;
                    PatrolDirection = FRotator.NormalizeAxis(offset.Yaw) > 0 ? -1 : 1;
                    CameraDesiredRotation = InitialRotation;
                    CameraDesiredRotation.Yaw += PatrolAngle * PatrolDirection * UnitsPerDegree;
                    CameraDesiredRotation = NormalizePitchYaw(CameraDesiredRotation);
                    fCheckPeriode = IdleCheckPeriod;
                }
            }

            return;
        }

        int fromCentre = FRotator.NormalizeAxis(Rotation.Yaw - InitialRotation.Yaw);
        if (PatrolAngle > 0 && Math.Abs(fromCentre - (PatrolDirection * PatrolAngle * UnitsPerDegree)) < SweepEndTolerance)
        {
            PatrolDirection = -PatrolDirection;
            CameraDesiredRotation = InitialRotation;
            CameraDesiredRotation.Yaw += PatrolDirection * PatrolAngle * UnitsPerDegree;
            CameraDesiredRotation = NormalizePitchYaw(CameraDesiredRotation);
        }
    }

    /// <summary>The native's normalisation: pitch and yaw to -32768..32767, roll left alone.</summary>
    private static FRotator NormalizePitchYaw(FRotator r) =>
        new(FRotator.NormalizeAxis(r.Pitch), FRotator.NormalizeAxis(r.Yaw), r.Roll);

    [Provenance(ProvenanceKind.Estimated, "The native moves the actor by a zero delta with the new rotation (ULevel::MoveActor); the port sets the rotation (cameras do not encroach)")]
    private void TurnHead(float DeltaSeconds)
    {
        int dPitch = FRotator.NormalizeAxis(CameraDesiredRotation.Pitch - Rotation.Pitch);
        int dYaw = FRotator.NormalizeAxis(CameraDesiredRotation.Yaw - Rotation.Yaw);
        int dRoll = CameraDesiredRotation.Roll - Rotation.Roll;
        int stepYaw = Step(dYaw, RotationVelocity, DeltaSeconds);
        int stepPitch = Step(dPitch, PitchRate, DeltaSeconds);
        if (stepPitch != 0 || stepYaw != 0 || dRoll != 0)
        {
            Rotation = new FRotator(Rotation.Pitch + stepPitch, Rotation.Yaw + stepYaw, Rotation.Roll + dRoll);
        }
    }

    /// <summary>sign(delta) x rate x dt, clamped to the remaining |delta| (zero for no delta).</summary>
    private static int Step(int delta, int rate, float dt)
    {
        if (delta == 0)
        {
            return 0;
        }

        int abs = Math.Abs(delta);
        float step = delta / abs * rate * dt;
        return SvmMath.Trunc(Math.Clamp(step, -abs, abs));
    }

    /// <summary>Plays the spotted sound and turns the LED red (the state code's and the alert relay's shared body).</summary>
    private void Alert()
    {
        PlaySound(CamDzzz, ESoundSlot.SLOT_Misc, AlertSoundVolume, false, CamSoundRadius, null, true);
        LightState = LightAlert;
        bSomethingIsDetected = true;
        fDetectionTime = Level.TimeSeconds;
        fCheckPeriode = AlertCheckPeriod;
    }

    /// <summary>The next sweep from the centre on the other side (on the authority), shared by the wake-ups.</summary>
    private void FlipSweep()
    {
        if (Role != ENetRole.ROLE_Authority)
        {
            return;
        }

        PatrolDirection = -PatrolDirection;
        CameraDesiredRotation = InitialRotation;
        CameraDesiredRotation.Yaw += PatrolDirection * PatrolAngle * UnitsPerDegree;
        CameraDesiredRotation = CameraDesiredRotation.Normalize();
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        MalfunctionEmitter = Live(MalfunctionEmitter);
        WallSupport = Live(WallSupport);
        DetectedPawn = Live(DetectedPawn);
        AlertOnEventFrom = Live(AlertOnEventFrom);
    }

    /// <summary>
    /// The auto state: green LED, a presence check every fCheckPeriode; a Trigger from AlertOnEventFrom raises the
    /// alarm at once (and fires Event); any other Trigger switches a non-TriggerControl camera off; an UnTrigger
    /// from AlertOnEventFrom calms it, any other switches it off.
    /// </summary>
    [Uc("SBase.SCamera.s_Patrol")]
    public class s_Patrol : UcActorState
    {
        public static readonly s_Patrol Instance = new(UcMeta.s_Patrol, null);

        protected s_Patrol(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SCamera.s_Patrol.BeginState", Sha1 = "2482969bd34e240d86668ec46947f61f405ec611")]
        [PortOf("uc:SBase.SCamera:s_Patrol.BeginState", Spec = "spec/devices/security-camera.md#states")]
        public override void BeginState(Actor self) => ((SCamera)self).LightState = LightPatrol;

        [Uc("SBase.SCamera.s_Patrol.Trigger", Sha1 = "221fa70b0dcbde5b09ce17b06f6010c2acaf2e3d")]
        [PortOf("uc:SBase.SCamera:s_Patrol.Trigger", Spec = "spec/devices/security-camera.md#states")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var cam = (SCamera)self;
            if (ReferenceEquals(Other, cam.AlertOnEventFrom))
            {
                if (cam.bSomethingIsDetected)
                {
                    return;
                }

                cam.DetectedPawn = null;
                cam.Alert();
                cam.TriggerEvent(cam.Event, cam, null);
                return;
            }

            if (cam.TriggerMethode != TriggerType.TriggerControl)
            {
                cam.GotoState(nameof(s_Inactive));
            }
        }

        [Uc("SBase.SCamera.s_Patrol.UnTrigger", Sha1 = "875a84b0205474743d5b9e9e41391a3a113e26c2")]
        [PortOf("uc:SBase.SCamera:s_Patrol.UnTrigger", Spec = "spec/devices/security-camera.md#states")]
        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var cam = (SCamera)self;
            if (ReferenceEquals(Other, cam.AlertOnEventFrom))
            {
                if (!cam.bSomethingIsDetected)
                {
                    return;
                }

                cam.bSomethingIsDetected = false;
                cam.LightState = LightPatrol;
                return;
            }

            cam.GotoState(nameof(s_Inactive));
        }

        /// <summary>Begin: a first sighting raises the alert; Sleep(fCheckPeriode); loop.</summary>
        [Uc("SBase.SCamera.s_Patrol@code", Sha1 = "3067d51a4f301fa0415d63413d46d6f3443492c2")]
        [PortOf("uc:SBase.SCamera:s_Patrol@code", Spec = "spec/devices/security-camera.md#states")]
        public override void Code(Actor self, ref LatentFrame frame) => ((SCamera)self).PatrolCode(ref frame);
    }

    private void PatrolCode(ref LatentFrame f)
    {
        if (CheckPresence() && !bSomethingIsDetected)
        {
            Alert();
        }

        f.Pc = 0;
        Sleep(fCheckPeriode);
    }

    /// <summary>Switched off: black LED, head drooping; a Trigger (or a non-TriggerControl UnTrigger) wakes it on the other sweep.</summary>
    [Uc("SBase.SCamera.s_Inactive")]
    public class s_Inactive : UcActorState
    {
        public static readonly s_Inactive Instance = new(UcMeta.s_Inactive, null);

        protected s_Inactive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SCamera.s_Inactive.BeginState", Sha1 = "dc2b2a69ba8bbe6245cfc56bbb595207856d1fda")]
        [PortOf("uc:SBase.SCamera:s_Inactive.BeginState", Spec = "spec/devices/security-camera.md#states")]
        public override void BeginState(Actor self) => ((SCamera)self).LightState = LightInactive;

        [Uc("SBase.SCamera.s_Inactive.Trigger", Sha1 = "baad0a3628398913512e16b688edd4b27a3a403f")]
        [PortOf("uc:SBase.SCamera:s_Inactive.Trigger", Spec = "spec/devices/security-camera.md#states")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var cam = (SCamera)self;
            cam.FlipSweep();
            cam.GotoState(nameof(s_Patrol));
        }

        [Uc("SBase.SCamera.s_Inactive.UnTrigger", Sha1 = "b3e21201fb635aa0e5e7fd1645dff0c76a5dc0a0")]
        [PortOf("uc:SBase.SCamera:s_Inactive.UnTrigger", Spec = "spec/devices/security-camera.md#states")]
        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var cam = (SCamera)self;
            if (cam.TriggerMethode != TriggerType.TriggerControl)
            {
                cam.FlipSweep();
                cam.GotoState(nameof(s_Patrol));
            }
        }
    }

    /// <summary>
    /// Locked by chaff or a sticky shot: black LED and sparks, the camera explosion sound, a new lock restarts the
    /// StickyNeutralizationTime timer; the timer wakes it on the other sweep in the remembered state.
    /// </summary>
    [Uc("SBase.SCamera.s_LockedByChaff")]
    public new class s_LockedByChaff : SBasicGamePlayObject.s_LockedByChaff, ISetLockedByChaff
    {
        public static new readonly s_LockedByChaff Instance = new(UcMeta.s_LockedByChaff, SBasicGamePlayObject.s_LockedByChaff.Instance);

        protected s_LockedByChaff(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SCamera.s_LockedByChaff.SetLockedByChaff", Sha1 = "6a63fb16a8f5e9d4b3515aa74dad68b1da0cb263")]
        [PortOf("uc:SBase.SCamera:s_LockedByChaff.SetLockedByChaff", Spec = "spec/devices/security-camera.md#chaff-and-sticky-lock")]
        void ISetLockedByChaff.SetLockedByChaff(SBasicGamePlayObject self, float Length) =>
            self.SetTimer(self.StickyNeutralizationTime, false);

        [Uc("SBase.SCamera.s_LockedByChaff.BeginState", Sha1 = "d8945b2d2045aabac9a34ac7c4531b05d8810779")]
        [PortOf("uc:SBase.SCamera:s_LockedByChaff.BeginState", Spec = "spec/devices/security-camera.md#chaff-and-sticky-lock")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            var cam = (SCamera)self;
            cam.LightState = LightLocked;
            cam.PlaySound(ChaffExplodeSound, ESoundSlot.SLOT_Misc, 1f, false, CamSoundRadius);
        }

        [Uc("SBase.SCamera.s_LockedByChaff.Timer", Sha1 = "76afc04c28de7bf857ce39b46bfcef09e798c434")]
        [PortOf("uc:SBase.SCamera:s_LockedByChaff.Timer", Spec = "spec/devices/security-camera.md#chaff-and-sticky-lock")]
        public override void Timer(Actor self)
        {
            var cam = (SCamera)self;
            cam.FlipSweep();
            cam.GotoState(cam.ChaffLockPreviousState);
        }
    }
}
