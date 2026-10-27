using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SPresDetector: the merc's presence detector, the second item of the spy trap launcher
/// (spec/equipment/presence-detector.md). On the server it scans every DetectionPeriod for moving spies within
/// fDetectionRadius in sight; a new one puts the detector on every player's HUD (the mercs' radar alarm), tells the
/// mercs "Intruder" and the spy he is detected; the warning lasts fWarningDuration after the last detection.
/// </summary>
public partial class SPresDetector
{
    public static new readonly StateTable ClassStates = new(SMines.ClassStates, s_Activated.Instance, s_LockedByChaff.Instance);

    public override StateTable GetStateTable() => ClassStates;

    private static readonly ClassRef CREF_SGameMessages = new("SBase.SGameMessages");

    /// <summary>In rain the heat is halved (not ported, as for the other mines).</summary>
    [Uc("SBase.SPresDetector.PreBeginPlay", Sha1 = "7cf035cdf3cf022e65ff4fbb199f16863f9d4da9")]
    [PortOf("uc:SBase.SPresDetector:PreBeginPlay", Spec = "spec/equipment/presence-detector.md#rules")]
    protected override void PreBeginPlay() => base.PreBeginPlay();

    [Uc("SBase.SPresDetector.AcceptStickableGPO", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPresDetector:AcceptStickableGPO", Spec = "spec/equipment/presence-detector.md#rules")]
    public override bool AcceptStickableGPO() => false;

    /// <summary>The placement time, then the mine's start.</summary>
    [Uc("SBase.SPresDetector.PostBeginPlay", Sha1 = "235fef6d9ba55faf95e6f1e2e475172c1ae43568")]
    [PortOf("uc:SBase.SPresDetector:PostBeginPlay", Spec = "spec/equipment/presence-detector.md#rules")]
    protected override void PostBeginPlay()
    {
        fSpawnedTime = Level.TimeSeconds;
        base.PostBeginPlay();
    }

    /// <summary>The gameplay object's damage (one hit point; refused while the detector is warning).</summary>
    [Uc("SBase.SPresDetector.TakeDamage", Sha1 = "d26d13f625f19569212e9764ca9a16a249935f49")]
    [PortOf("uc:SBase.SPresDetector:TakeDamage", Spec = "spec/equipment/presence-detector.md#rules")]
    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0) =>
        base.TakeDamage(Damage, EventInstigator, HitLocation, Momentum, DamageType, PillTag);

    /// <summary>The detected spies are forgotten and the HUD markers removed; then the mine's clean-up.</summary>
    [Uc("SBase.SPresDetector.Destroyed", Sha1 = "3f39561e91729b4f6a1527fa49efa397e9ca0fe3")]
    [PortOf("uc:SBase.SPresDetector:Destroyed", Spec = "spec/equipment/presence-detector.md#rules")]
    protected override void Destroyed()
    {
        if (Role == ENetRole.ROLE_Authority)
        {
            DetectedPawn[0] = null;
            DetectedPawn[1] = null;
            RemoveFromHud();
        }

        base.Destroyed();
    }

    /// <summary>
    /// The native tick (decoded 0x10abd500), after the actor tick on every machine: damageable only while not warning;
    /// the interaction's bPresenceDetected follows the warning; on the server the scan when due, and the warning's
    /// start (new spies) or end (fWarningDuration after the last detection).
    /// </summary>
    [UcNative("ASPresDetector::Tick", Exe = 0x10abd500, Editor = 0x111cb050)]
    [PortOf("eng:ASPresDetector::Tick", Spec = "spec/equipment/presence-detector.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "presence-detector.md rules 2 to 5: bDamageable = !bDetected, ChangePresenceDetected, scan when DetectionPeriod is 0 or past, AddOnHud / RemoveFromHud, AddMessageOnHud(1), message 254")]
    [Provenance(ProvenanceKind.Estimated, "ChangePresenceDetected runs on the server only: off it the original's call is a client-to-server event of an actor the client does not own")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (bDeleteMe)
        {
            return;
        }

        bDamageable = !bDetected;
        float now = Level.TimeSeconds;
        bool authority = Role == ENetRole.ROLE_Authority;
        if (authority && InteractionObj is { } interaction)
        {
            float end = fDetectionTime + fWarningDuration;
            if (!bDetected)
            {
                if (interaction.bPresenceDetected && end < now)
                {
                    interaction.ChangePresenceDetected(false);
                }
            }
            else if (!interaction.bPresenceDetected && 0f < fDetectionTime && now <= end)
            {
                interaction.ChangePresenceDetected(true);
            }
        }

        if (!authority)
        {
            return;
        }

        SBasePawn? old0 = DetectedPawn[0];
        SBasePawn? old1 = DetectedPawn[1];
        bool due = DetectionPeriod == 0f || !(now <= fLastScanTime + DetectionPeriod);
        if (!due || !ScanPresence())
        {
            if ((DetectedPawn[0] is null && DetectedPawn[1] is null) || now - fDetectionTime <= fWarningDuration)
            {
                return;
            }

            DetectedPawn[0] = null;
            DetectedPawn[1] = null;
            RemoveFromHud();
            bDetected = false;
            return;
        }

        bool news = false;
        if (DetectedPawn[0] is { } p0 && !ReferenceEquals(p0, old0))
        {
            news = true;
            (p0.Controller as SPlayerController)?.AddMessageOnHud(K.PresDetectedHudMessage);
        }

        if (DetectedPawn[1] is { } p1 && !ReferenceEquals(p1, old1))
        {
            news = true;
            (p1.Controller as SPlayerController)?.AddMessageOnHud(K.PresDetectedHudMessage);
        }

        if (news)
        {
            AddOnHud();
            (Level.Game as SGameInfo)?.BroadcastTeamLocalizedMessage(K.PresWarnTeam, CREF_SGameMessages, K.PresWarnMessage);
        }

        fDetectionTime = now;
        bDetected = true;
    }

    /// <summary>
    /// The scan (ASPresDetector virtual 0x138, decoded 0x10abd710): nothing while chaffed; the scan time and sound
    /// (every player, radius twice the detection radius); the first two spies within fDetectionRadius, faster than
    /// VitesseMin (3D when going up, else horizontal) and in sight of the detector go into DetectedPawn.
    /// </summary>
    [PortOf("eng:ASPresDetector::Tick", Spec = "spec/equipment/presence-detector.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "Game 0x10abd710 (unnamed vtable slot 0x138):bChaffed refuses, fLastScanTime, the scan sound, SPawnAttaque within the radius (squared, strict), speed squared above VitesseMin squared, the shot line (TraceBone) from the detector to the spy hits him or nothing, at most 2")]
    [Provenance(ProvenanceKind.Estimated, "The scan sound is one PlaySound at the detector (the native calls each player controller's sound hook)")]
    public bool ScanPresence()
    {
        if (bChaffed)
        {
            return false;
        }

        fLastScanTime = Level.TimeSeconds;
        PlaySound(Snd_ScanPresDec, ESoundSlot.SLOT_None, 1f, false, fDetectionRadius * K.PresScanSoundRadiusScale);
        float speed2 = VitesseMin * VitesseMin;
        float range2 = fDetectionRadius * fDetectionRadius;
        IHitZones zones = CombatServices.HitZones(this);
        int found = 0;
        foreach (SBasePawn p in GadgetWorld.Pawns(XLevel))
        {
            if (p is not SPawnAttaque || p.bDeleteMe || !(FVector.DistSquared(p.Location, Location) < range2))
            {
                continue;
            }

            FVector v = p.Velocity;
            float s2 = v.Z > 0f ? v.SizeSquared() : v.SizeSquared2D();
            if (!(speed2 < s2))
            {
                continue;
            }

            BoneTraceResult hit = zones.TraceBone(this, p.Location, Location, false);
            if (hit.bHit && !ReferenceEquals(hit.Actor, p))
            {
                continue;
            }

            DetectedPawn[found++] = p;
            if (found > 1)
            {
                return true;
            }
        }

        return found > 0;
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        DetectedPawn[0] = Live(DetectedPawn[0]);
        DetectedPawn[1] = Live(DetectedPawn[1]);
    }

    /// <summary>Armed (auto): nothing of its own; the native tick scans in any state but the chaff lock.</summary>
    [Uc("SBase.SPresDetector.s_Activated")]
    public new class s_Activated : SMines.s_Activated
    {
        public static new readonly s_Activated Instance = new(UcMeta.s_Activated, SMines.s_Activated.Instance);

        protected s_Activated(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }
    }

    /// <summary>Chaffed: bChaffed stops the scans while locked.</summary>
    [Uc("SBase.SPresDetector.s_LockedByChaff")]
    public new class s_LockedByChaff : SBasicGamePlayObject.s_LockedByChaff
    {
        public static new readonly s_LockedByChaff Instance = new(UcMeta.s_LockedByChaff, SBasicGamePlayObject.s_LockedByChaff.Instance);

        protected s_LockedByChaff(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPresDetector.s_LockedByChaff.BeginState", Sha1 = "a3e5212553058cd9fb4a7eb182d7f6905b40df05")]
        [PortOf("uc:SBase.SPresDetector:s_LockedByChaff.BeginState", Spec = "spec/equipment/presence-detector.md#rules")]
        public override void BeginState(Actor self)
        {
            ((SPresDetector)self).bChaffed = true;
            base.BeginState(self);
        }

        [Uc("SBase.SPresDetector.s_LockedByChaff.EndState", Sha1 = "4348d05e71747a6191becd093459ddae21fbd8e3")]
        [PortOf("uc:SBase.SPresDetector:s_LockedByChaff.EndState", Spec = "spec/equipment/presence-detector.md#rules")]
        public override void EndState(Actor self)
        {
            ((SPresDetector)self).bChaffed = false;
            base.EndState(self);
        }
    }
}
