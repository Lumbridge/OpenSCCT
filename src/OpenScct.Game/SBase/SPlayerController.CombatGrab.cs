using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController spy grab (spec/combat/spy-grab.md): the owner's grab test and the server's validation, the
// release location search, the server's release resolution with the KO, neck break and choke outcomes, the
// counter-grab result and the drop attack on a holding spy. The grab states are in SPlayerController.CombatGrabStates.cs.
// The ledge grab (rules 30 to 36) is in SPlayerController.CombatGrabLedge.cs, the grab voice link (rule 15) in
// SPlayerController.Voice.cs.
public partial class SPlayerController
{
    private static readonly Name GNAME_AskServerToGrabPawn = new("AskServerToGrabPawn");
    private static readonly Name GNAME_AskServerToUnGrabPawn = new("AskServerToUnGrabPawn");
    private static readonly Name GNAME_AskServerToUnGrabPawnAndFreeGuy = new("AskServerToUnGrabPawnAndFreeGuy");
    private static readonly Name GNAME_GrabServerReleaseRequest = new("GrabServerReleaseRequest");
    private static readonly Name GNAME_GrabAborted = new("GrabAborted");
    private static readonly Name GNAME_s_GrabbedByPlayer_Begin = new("s_GrabbedByPlayer_Begin");
    private static readonly Name GNAME_s_GrabbedByPlayer = new("s_GrabbedByPlayer");
    private static readonly Name GNAME_s_GrabbedByPlayer_AvoidGrab = new("s_GrabbedByPlayer_AvoidGrab");
    private static readonly Name GNAME_s_GrabbedByPlayer_Released_KO = new("s_GrabbedByPlayer_Released_KO");
    private static readonly Name GNAME_s_GrabbedByPlayer_Released_STRANGLE = new("s_GrabbedByPlayer_Released_STRANGLE");
    private static readonly Name GNAME_s_GrabbedByPlayer_Released_DMG = new("s_GrabbedByPlayer_Released_DMG");
    private static readonly Name GNAME_s_GrabbedByPlayer_Released_Grabbing_LD = new("s_GrabbedByPlayer_Released_Grabbing_LD");
    private static readonly Name GNAME_s_GrabbedByPlayer_Released_GrabbingDMG = new("s_GrabbedByPlayer_Released_GrabbingDMG");
    private static readonly Name GNAME_s_GrabbedByPlayer_CounterGrab = new("s_GrabbedByPlayer_CounterGrab");
    private static readonly Name GNAME_s_Grabbing_UncrouchForGrab = new("s_Grabbing_UncrouchForGrab");
    private static readonly Name GNAME_s_Grabbing_GotoPosition = new("s_Grabbing_GotoPosition");
    private static readonly Name GNAME_s_Grabbing_GoToPosToFreeGuy = new("s_Grabbing_GoToPosToFreeGuy");
    private static readonly Name GNAME_s_GrabbingPlayer_Begin = new("s_GrabbingPlayer_Begin");
    private static readonly Name GNAME_s_GrabbingPlayer_RealBegin = new("s_GrabbingPlayer_RealBegin");
    private static readonly Name GNAME_s_GrabbingPlayer = new("s_GrabbingPlayer");
    private static readonly Name GNAME_s_Grab_Avoided = new("s_Grab_Avoided");
    private static readonly Name GNAME_s_AbortGrab = new("s_AbortGrab");
    private static readonly Name GNAME_s_ReleaseGrabbedPlayer = new("s_ReleaseGrabbedPlayer");
    private static readonly Name GNAME_s_ReleaseGrabbedPlayer_LD = new("s_ReleaseGrabbedPlayer_LD");
    private static readonly Name GNAME_s_ReleaseGrabbedPlayer_KO = new("s_ReleaseGrabbedPlayer_KO");
    private static readonly Name GNAME_s_ReleaseGrabbedPlayer_Strangle = new("s_ReleaseGrabbedPlayer_Strangle");
    private static readonly Name GNAME_s_ReleaseGrabbedPlayer_DMG = new("s_ReleaseGrabbedPlayer_DMG");
    private static readonly Name GNAME_s_ReleaseGrabbedPlayer_BothDMG = new("s_ReleaseGrabbedPlayer_BothDMG");
    private static readonly Name GNAME_s_ReleaseGrabbedPlayer_Grabbed_DMGED = new("s_ReleaseGrabbedPlayer_Grabbed_DMGED");
    private static readonly Name GNAME_s_GrabRelease_Counter = new("s_GrabRelease_Counter");
    private static readonly Name GNAME_s_FallDownDying = new("s_FallDownDying");
    private static readonly ClassRef GREF_SGameMessages = new("SBase.SGameMessages");

    /// <summary>A grab state's version of <see cref="GrabAborted"/>.</summary>
    public interface IGrabAborted
    {
        void GrabAborted(SPlayerController self);
    }

    /// <summary>The release type of the spy's pending release (owner copy) and the server's last one.</summary>
    public GrabReleaseMethode_ PendingGrabRelease => GrabReleaseMethode;

    // ---- starting -------------------------------------------------------------------------------------------

    /// <summary>
    /// The owner's grab test (rules 2 and 3): the first merc in reach that passes the ground and height probes and
    /// a clear path is asked for on the server. The search stops at the first merc whose probe fails.
    /// </summary>
    [Uc("SBase.SPlayerController.AskToGrabPawn", Sha1 = "02dc9820b0710ea5f3dc3f3e60fbc024be5b1cb5")]
    [PortOf("uc:SBase.SPlayerController:AskToGrabPawn", Spec = "spec/combat/spy-grab.md#starting-a-grab-spy-owner")]
    public bool AskToGrabPawn()
    {
        if (SBPawn is not SPawnAttaque spy || spy.bStickyShocking || GameReplicationInfo is { bAvlb_SPY_Grab: false })
        {
            return false;
        }

        if (!GrabStance(spy, out FVector myLoc, out float h))
        {
            return false;
        }

        foreach (Pawn p in XLevel.DynamicActors<Pawn>())
        {
            if (p is not SPAWNDEFENSE target || ReferenceEquals(target, spy) || !CanGrabPawn(target) || !target.bCanBeGrabbed ||
                target.bStickyShocking)
            {
                continue;
            }

            GrabProbe probe = GrabGeometry(spy, target, myLoc, h);
            if (probe == GrabProbe.Stop)
            {
                return false;
            }

            if (probe == GrabProbe.Clear)
            {
                AskServerToGrabPawn(target);
                return true;
            }
        }

        return false;
    }

    /// <summary>The outcome of the grab geometry for one merc.</summary>
    private enum GrabProbe
    {
        /// <summary>The ground or height probe failed: the search ends.</summary>
        Stop,

        /// <summary>The path to the grab spot is blocked: try the next merc.</summary>
        Blocked,

        /// <summary>The grab spot is reachable.</summary>
        Clear,
    }

    /// <summary>
    /// The spy's test point and height tolerance (rule 2): a crouched spy must be able to stand; the point rises by
    /// the crouch difference and the tolerance grows by it.
    /// </summary>
    private bool GrabStance(SBasePawn spy, out FVector myLoc, out float h)
    {
        myLoc = spy.Location;
        h = K.GrabHeightTolerance;
        if (!spy.bIsCrouched)
        {
            return true;
        }

        if (!spy.CanUnCrouch())
        {
            return false;
        }

        float standing = spy.ClassDefaults.CollisionHeight;
        myLoc.Z += standing - spy.CollisionHeight;
        h += standing - spy.CrouchHeight;
        return true;
    }

    /// <summary>
    /// The grab spot behind the merc (rule 3): the merc point (raised when crouched), the spot vGrabOffset short of
    /// him on the line from the spy, the ground probe under it with both height tests, then a clear world box path.
    /// </summary>
    private GrabProbe GrabGeometry(SBasePawn spy, SBasePawn target, FVector myLoc, float h)
    {
        ICollisionWorld? world = TraceHitZones.CollisionOf(this);
        FVector tl = target.Location;
        if (target.bIsCrouched)
        {
            if (!target.CanUnCrouch())
            {
                return GrabProbe.Blocked;
            }

            tl.Z += target.ClassDefaults.CollisionHeight - target.CollisionHeight;
        }

        (tl - myLoc).Rotation().GetAxes(out FVector x, out FVector y, out FVector z);
        FVector off = spy.vGrabOffset;
        FVector g = tl - (x * off.X + y * off.Y + z * off.Z);
        if (world is null)
        {
            return GrabProbe.Clear;
        }

        float ch = spy.CollisionHeight;
        FVector down = g - new FVector(0f, 0f, ch + h);
        if (!world.LineCheck(out FCheckResult floor, spy, down, g, FVector.Zero, ETraceFlags.TRACE_World))
        {
            return GrabProbe.Stop;
        }

        float diff = floor.Location.Z + ch - tl.Z;
        if (!(SvmMath.Abs(diff) < h) || !(SvmMath.Abs(myLoc.Z - tl.Z) < h))
        {
            return GrabProbe.Stop;
        }

        var extent = new FVector(spy.CollisionRadius, spy.CollisionRadius, ch - (diff / 2f + K.GrabExtentShrink));
        g = floor.Location + new FVector(0f, 0f, K.GrabSpotRaise * ch);
        return world.LineCheck(out _, spy, g, myLoc, extent, ETraceFlags.TRACE_World) ? GrabProbe.Blocked : GrabProbe.Clear;
    }

    /// <summary>
    /// The grab reach (rule 4): the merc woke more than 3.8 s ago, is within fGrabDistance, is in front of the spy
    /// (cos 0.75) and faces away from him with the facing shifted by iGrabAreaShift (cos fGrabHalfAngle).
    /// </summary>
    [Uc("SBase.SPlayerController.CanGrabPawn", Sha1 = "d08845defc5f1dcc5cfabd2e64339b91709cbb93")]
    [PortOf("uc:SBase.SPlayerController:CanGrabPawn", Spec = "spec/combat/spy-grab.md#starting-a-grab-spy-owner")]
    public bool CanGrabPawn(SBasePawn Target)
    {
        if (Pawn is not { } me || !(Level.TimeSeconds - Target.iLastWakeTime > fWakeInvulnerabilityTime) ||
            !(FVector.Dist(Target.Location, me.Location) < fGrabDistance))
        {
            return false;
        }

        FVector toTarget = Target.Location - me.Location;
        toTarget.Z = 0f;
        FVector facing = me.Rotation.Vector().SafeNormal();
        if (!((toTarget.SafeNormal() | facing) > K.GrabFrontCone))
        {
            return false;
        }

        FRotator shifted = Target.Rotation;
        shifted.Yaw += iGrabAreaShift;
        return (shifted.Vector().SafeNormal() | facing) > fGrabHalfAngle;
    }

    /// <summary>
    /// The server's grab validation (rules 5 and 6): the merc's state allows it, the reach at double distance, no
    /// shock on either side and none on the merc in the last 5 s, the same geometry; then the two players are linked
    /// and sent to their grab states.
    /// </summary>
    [Uc("SBase.SPlayerController.AskServerToGrabPawn", Sha1 = "2506e4b74474679125862e36c80dacbf1e90fa31")]
    [PortOf("uc:SBase.SPlayerController:AskServerToGrabPawn", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
    [PortOf("rep:SBase.SPlayerController.AskServerToGrabPawn", Spec = "spec/combat/spy-grab.md#network")]
    public void AskServerToGrabPawn(SBasePawn Target)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(GNAME_AskServerToGrabPawn) { Actor1 = Target });
            return;
        }

        if (SBPawn is not { Health: > 0 } spy || !GrabStance(spy, out FVector myLoc, out float h) ||
            Target.Controller is not SPlayerController merc || !CombatStateEvents.CanBeGrabbed(merc))
        {
            return;
        }

        float restore = fGrabDistance;
        fGrabDistance *= K.GrabServerDistanceFactor;
        bool ok = CanGrabPawn(Target) && Target.bCanBeGrabbed && !spy.bStickyShocking && !Target.bStickyShocking &&
            merc.TimeShockForGrab < Level.TimeSeconds - fGrabInvulneralbilityTimeAfterStickyShock &&
            GrabGeometry(spy, Target, myLoc, h) == GrabProbe.Clear;
        fGrabDistance = restore;
        if (!ok)
        {
            return;
        }

        spy.m_AttackTarget = Target;
        Target.m_AttackedBy = spy;
        if (Target.m_PawnToTerminate is { } finishing)
        {
            Target.m_PawnToTerminate = null;
            (finishing.Controller as SPlayerController)?.eventMirrorState(CNAME_s_TerminatedPawn_Released);
        }

        // s_Hack_Use frees its panel here; crouched, the original calls it after the state change (a no-op).
        if (!spy.bIsCrouched)
        {
            merc.ServerWillStartGrabOnMe();
        }

        merc.eventMirrorState(GNAME_s_GrabbedByPlayer_Begin, default, false, spy);
        eventMirrorState(spy.bIsCrouched ? GNAME_s_Grabbing_UncrouchForGrab : GNAME_s_Grabbing_GotoPosition);
        IncStat(this, static s => s.Spy_Grabs++);
    }

    // ---- releasing ------------------------------------------------------------------------------------------

    /// <summary>
    /// The owner's release request (rule 16): with the merc's box collision off, a free release spot is searched;
    /// found, the release is latched and sent at once or after a repositioning; not found, only the forced types
    /// release at once (GrabbingDamaged becomes BothDie).
    /// </summary>
    [Uc("SBase.SPlayerController.CheckForRoomToFreeGrabbedGuy", Sha1 = "5fa65b5ed7373a16edebf0cb784ecabbf4e7c500")]
    [PortOf("uc:SBase.SPlayerController:CheckForRoomToFreeGrabbedGuy", Spec = "spec/combat/spy-grab.md#choosing-where-to-release-owner")]
    public bool CheckForRoomToFreeGrabbedGuy(GrabReleaseMethode_ Type, bool bTest = false)
    {
        if (SBPawn is not { m_AttackTarget: { } target } spy || bGrabIsReleasing)
        {
            return false;
        }

        bool saved = target.bBlockNonZeroExtentTraces;
        target.bBlockNonZeroExtentTraces = false;
        bool found = GetFreeLocationToRelease(out FVector l, out FRotator r, spy, spy.vGrabOffset);
        target.bBlockNonZeroExtentTraces = saved;
        if (found)
        {
            if (bTest)
            {
                return true;
            }

            GrabReleaseMethode = Type;
            bGrabIsReleasing = true;
            if (l == spy.Location && r == spy.Rotation)
            {
                AskServerToUnGrabPawn(Type);
            }
            else
            {
                spy.vVandammePos = l;
                CombatPorts.SetInfoForVandamme(spy, l, r);
                spy.SetInfoForVandamme(r.Vector());
                eventChangeState(GNAME_s_Grabbing_GoToPosToFreeGuy);
            }

            return true;
        }

        if (Type is GrabReleaseMethode_.Strangled or GrabReleaseMethode_.Timed or GrabReleaseMethode_.GrabbingDamaged or
            GrabReleaseMethode_.GrabbedDamaged && !bTest)
        {
            GrabReleaseMethode_ way = Type == GrabReleaseMethode_.GrabbingDamaged ? GrabReleaseMethode_.BothDie : Type;
            GrabReleaseMethode = way;
            bGrabIsReleasing = true;
            AskServerToUnGrabPawn(way);
        }

        return false;
    }

    /// <summary>
    /// The release spot (rule 17): the spy's own spot when the merc fits in front of him on solid ground; else the
    /// eight rotations around the snapped facing; else a push back along the facing.
    /// </summary>
    [Uc("SBase.SPlayerController.GetFreeLocationToRelease", Sha1 = "fe4b198fa4062c0a9c0a5e2130c0e636a3925ec6")]
    [PortOf("uc:SBase.SPlayerController:GetFreeLocationToRelease", Spec = "spec/combat/spy-grab.md#choosing-where-to-release-owner")]
    public bool GetFreeLocationToRelease(out FVector L, out FRotator R, SBasePawn Master, FVector Offset)
    {
        L = Master.Location;
        R = new FRotator(0, Master.Rotation.Yaw, 0);
        ICollisionWorld? world = TraceHitZones.CollisionOf(this);
        if (world is null)
        {
            R = Master.Rotation;
            return true;
        }

        var extent = new FVector(Master.CollisionRadius, Master.CollisionRadius, Master.CollisionHeight);
        R.GetAxes(out FVector x, out FVector y, out _);
        FVector spot = Master.Location + x * Offset.X + y * Offset.Y;
        bool firstHit = world.LineCheck(out FCheckResult first, Master, spot, Master.Location, extent, ETraceFlags.TRACE_World);
        if (!firstHit && CheckForGround(spot, Master))
        {
            R = Master.Rotation;
            return true;
        }

        int snapped = SnapYaw(Master.Rotation.Yaw) + K.ReleaseYawEighth;
        foreach (int deg in ReleaseTurns)
        {
            var rot = new FRotator(0, snapped + deg * K.ReleaseYawEighth / 45, 0);
            rot.GetAxes(out FVector rx, out FVector ry, out _);
            FVector s = Master.Location + rx * Offset.X + ry * Offset.Y;
            if (!world.LineCheck(out _, Master, s, Master.Location, extent, ETraceFlags.TRACE_AllColliding) && CheckForGround(s, Master))
            {
                R = rot;
                return true;
            }
        }

        FVector facing = x;
        FVector hitAt = firstHit ? first.Location : spot;
        float t = Offset.X + Master.CollisionRadius - (facing | (hitAt - Master.Location)) + K.ReleasePushMargin;
        if (t <= 0f)
        {
            return false;
        }

        FVector back = Master.Location - facing * t;
        FVector mercSpot = back + x * Offset.X + y * Offset.Y;
        if (world.LineCheck(out _, Master, mercSpot, back, extent, ETraceFlags.TRACE_AllColliding) ||
            world.LineCheck(out _, Master, back, Master.Location, extent, ETraceFlags.TRACE_AllColliding) ||
            !CheckForGround(mercSpot, Master))
        {
            return false;
        }

        L = back;
        R = Master.Rotation;
        return true;
    }

    /// <summary>The eight turns tried around the snapped facing, in degrees, in order.</summary>
    [Provenance(ProvenanceKind.Decoded, "spy-grab.md rule 17: S+45, S-45, S+90, S-90, S+135, S-135, S+180, S+0")]
    private static readonly int[] ReleaseTurns = [45, -45, 90, -90, 135, -135, 180, 0];

    /// <summary>The yaw snapped to the nearest quarter: (-8192, 8192) to 0, [8192, 32768) to 16384, [32768, 49151] to 32768, else -16384.</summary>
    public static int SnapYaw(int yaw)
    {
        int a = yaw & 65535;
        int s = a >= 32768 ? a - 65536 : a;
        if (s > -K.ReleaseYawEighth && s < K.ReleaseYawEighth)
        {
            return 0;
        }

        if (a >= K.ReleaseYawEighth && a < 2 * K.ReleaseYawQuarter)
        {
            return K.ReleaseYawQuarter;
        }

        if (a >= 2 * K.ReleaseYawQuarter && a <= 3 * K.ReleaseYawQuarter - 1)
        {
            return 2 * K.ReleaseYawQuarter;
        }

        return -K.ReleaseYawQuarter;
    }

    /// <summary>
    /// Ground under a release spot (rule 18): a box 2 uu shorter swept 35 uu down, else a line from the box bottom
    /// 35 uu down.
    /// </summary>
    [Uc("SBase.SPlayerController.CheckForGround", Sha1 = "7581c63362be9e711ba1efc4b021b42d5f8fd09d")]
    [PortOf("uc:SBase.SPlayerController:CheckForGround", Spec = "spec/combat/spy-grab.md#choosing-where-to-release-owner")]
    public bool CheckForGround(FVector P, SBasePawn Master)
    {
        if (TraceHitZones.CollisionOf(this) is not { } world)
        {
            return true;
        }

        P.Z += K.GroundProbeLift;
        float h = Master.CollisionHeight - K.GroundProbeLift;
        var extent = new FVector(Master.CollisionRadius, Master.CollisionRadius, h);
        FVector down = P - new FVector(0f, 0f, K.GroundProbeDepth);
        if (world.LineCheck(out _, Master, down, P, extent, ETraceFlags.TRACE_World))
        {
            return true;
        }

        FVector bottom = P - new FVector(0f, 0f, h);
        return world.LineCheck(out _, Master, bottom - new FVector(0f, 0f, K.GroundProbeDepth), bottom, FVector.Zero, ETraceFlags.TRACE_World);
    }

    /// <summary>The server asks the spy's owner to release (reliable server to owner).</summary>
    [Uc("SBase.SPlayerController.GrabServerReleaseRequest", Sha1 = "d0bdc3e602f0e521bbfb02376e1db4e3635fdf9f")]
    [PortOf("uc:SBase.SPlayerController:GrabServerReleaseRequest", Spec = "spec/combat/spy-grab.md#network")]
    public void GrabServerReleaseRequest(GrabReleaseMethode_ Type)
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(GNAME_GrabServerReleaseRequest) { Int1 = (int)Type });
            return;
        }

        CheckForRoomToFreeGrabbedGuy(Type);
    }

    /// <summary>The server aborts a grab on this player's owner (reliable server to owner); the state decides.</summary>
    [Uc("SBase.SPlayerController.GrabAborted", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:GrabAborted", Spec = "spec/combat/spy-grab.md#network")]
    public void GrabAborted()
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(GNAME_GrabAborted));
            return;
        }

        if (StateNode is IGrabAborted s)
        {
            s.GrabAborted(this);
        }
    }

    /// <summary>
    /// The server places the merc in front of the repositioned spy (spy + vGrabOffset turned by the spy's
    /// rotation), then resolves the release (rule 16).
    /// </summary>
    [Uc("SBase.SPlayerController.AskServerToUnGrabPawnAndFreeGuy", Sha1 = "6ceae47abcc9e7e0c4cf953dc23a68e25054a21a")]
    [PortOf("uc:SBase.SPlayerController:AskServerToUnGrabPawnAndFreeGuy", Spec = "spec/combat/spy-grab.md#choosing-where-to-release-owner")]
    [PortOf("rep:SBase.SPlayerController.AskServerToUnGrabPawnAndFreeGuy", Spec = "spec/combat/spy-grab.md#network")]
    public void AskServerToUnGrabPawnAndFreeGuy(GrabReleaseMethode_ Way)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(GNAME_AskServerToUnGrabPawnAndFreeGuy) { Int1 = (int)Way });
            return;
        }

        if (SBPawn is { m_AttackTarget: { } target } spy)
        {
            spy.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
            FVector off = spy.vGrabOffset;
            target.SetLocation(spy.Location + x * off.X + y * off.Y + z * off.Z);
        }

        AskServerToUnGrabPawn(Way);
    }

    /// <summary>
    /// The server's release (rules 19 and 20): the KO and choke cost of 51 hp, the outcome by release type with the
    /// kill credit to the spy, and both players' release states.
    /// </summary>
    [Uc("SBase.SPlayerController.AskServerToUnGrabPawn", Sha1 = "cbf9e56f497a54966e7750ffd645dcde1a8162cd")]
    [PortOf("uc:SBase.SPlayerController:AskServerToUnGrabPawn", Spec = "spec/combat/spy-grab.md#release-resolution-server-askservertoungrabpawnway")]
    [PortOf("rep:SBase.SPlayerController.AskServerToUnGrabPawn", Spec = "spec/combat/spy-grab.md#network")]
    public void AskServerToUnGrabPawn(GrabReleaseMethode_ Way)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(GNAME_AskServerToUnGrabPawn) { Int1 = (int)Way });
            return;
        }

        GrabReleaseMethode = GrabReleaseMethode_.NoRelease;
        if (SBPawn is not { } me)
        {
            return;
        }

        if (Way == GrabReleaseMethode_.GrabbedAvoid && me is SPAWNDEFENSE)
        {
            GrabbedAvoidRelease(me);
            return;
        }

        if (me is not SPawnAttaque spy)
        {
            return;
        }

        SBasePawn? merc = spy.m_AttackTarget;
        var mercPc = merc?.Controller as SPlayerController;
        bool removePoint = true;
        if (Way is not (GrabReleaseMethode_.GrabbingDamaged or GrabReleaseMethode_.BothDie) && spy.Health == 0)
        {
            return;
        }

        if (merc is not null && Way is not (GrabReleaseMethode_.Grabbing_Cancelled or GrabReleaseMethode_.GrabbedAvoid or
            GrabReleaseMethode_.Grabbing_Stopped or GrabReleaseMethode_.GrabbingCountered or
            GrabReleaseMethode_.Grabbed_MovedWhileTryingToBeGrabbed))
        {
            if (Way == GrabReleaseMethode_.GrabbedDamaged && merc.Health <= 0)
            {
                removePoint = false;
            }

            merc.Health -= SvmMath.Trunc(merc.ClassDefaults.Health * K.GrabReleaseHealthFactor);
            if (merc.Health <= 0)
            {
                if (Way is GrabReleaseMethode_.GrabbingDamaged or GrabReleaseMethode_.BothDie)
                {
                    merc.Health = SvmMath.Min(merc.Health, K.GrabBothDieHealthCap);
                }
                else
                {
                    merc.Health = 0;
                    Way = GrabReleaseMethode_.Strangled;
                }
            }
        }

        bCanTalkToGuy = false;
        bool spyAlive = spy.Health > 0;
        bool mercAlive = merc is { Health: > 0 };
        switch (Way)
        {
            case GrabReleaseMethode_.BothDie:
                GrabKill(merc, mercPc, SDamageType.DamTypeGrabBreak);
                mercPc?.eventMirrorState(GNAME_s_GrabbedByPlayer_Released_STRANGLE);
                eventMirrorState(GNAME_s_ReleaseGrabbedPlayer_BothDMG);
                break;
            case GrabReleaseMethode_.Normal:
                GrabKill(merc, mercPc, SDamageType.DamTypeGrabBreak);
                mercPc?.eventMirrorState(GNAME_s_GrabbedByPlayer_Released_STRANGLE);
                if (spyAlive)
                {
                    eventMirrorState(GNAME_s_ReleaseGrabbedPlayer);
                }

                break;
            case GrabReleaseMethode_.KO:
                (Level.Game as GameInfo)?.BroadcastLocalizedMessage(GREF_SGameMessages, K.MsgFallenUnconscious,
                    PlayerReplicationInfo, mercPc?.PlayerReplicationInfo);
                if (mercAlive)
                {
                    mercPc?.eventMirrorState(GNAME_s_GrabbedByPlayer_Released_KO);
                }

                (Level.Game as SGameInfo)?.AddPoint(0, this, K.AP_D_KnockOut);
                if (spyAlive)
                {
                    eventMirrorState(GNAME_s_ReleaseGrabbedPlayer_KO);
                }

                break;
            case GrabReleaseMethode_.Strangled:
                if (merc is not null)
                {
                    merc.Health = 0;
                }

                if (removePoint)
                {
                    GrabKill(merc, mercPc, SDamageType.DamTypeGrabChoke);
                }

                mercPc?.eventMirrorState(GNAME_s_GrabbedByPlayer_Released_STRANGLE);
                if (spyAlive)
                {
                    eventMirrorState(GNAME_s_ReleaseGrabbedPlayer_Strangle);
                }

                break;
            case GrabReleaseMethode_.Timed:
                GrabKill(merc, mercPc, SDamageType.DamTypeGrabChoke);
                mercPc?.eventMirrorState(GNAME_s_GrabbedByPlayer_Released_STRANGLE);
                eventMirrorState(GNAME_s_ReleaseGrabbedPlayer_Strangle);
                break;
            case GrabReleaseMethode_.GrabbingCountered:
                if (mercAlive)
                {
                    mercPc?.eventMirrorState(GNAME_s_GrabbedByPlayer_CounterGrab);
                }

                eventMirrorState(GNAME_s_GrabRelease_Counter);
                break;
            case GrabReleaseMethode_.GrabbingDamaged:
                if (mercAlive)
                {
                    mercPc?.eventMirrorState(GNAME_s_GrabbedByPlayer_Released_GrabbingDMG);
                }

                eventMirrorState(GNAME_s_ReleaseGrabbedPlayer_DMG);
                break;
            case GrabReleaseMethode_.GrabbedDamaged:
                if (merc is not null)
                {
                    merc.Health = 0;
                }

                mercPc?.eventMirrorState(GNAME_s_GrabbedByPlayer_Released_DMG);
                if (spyAlive)
                {
                    eventMirrorState(GNAME_s_ReleaseGrabbedPlayer_Grabbed_DMGED);
                }

                break;
            case GrabReleaseMethode_.Grabbing_Cancelled:
                GrabUnlink(spy, merc);
                if (mercAlive)
                {
                    mercPc?.eventMirrorState(GNAME_s_GrabbedByPlayer_AvoidGrab);
                }

                if (spyAlive)
                {
                    eventMirrorState(GNAME_s_Grab_Avoided);
                }

                break;
            case GrabReleaseMethode_.Grabbing_Stopped:
                mercPc?.GrabAborted();
                if (merc is not null)
                {
                    merc.m_AttackedBy = null;
                }

                break;
            case GrabReleaseMethode_.Grabbed_MovedWhileTryingToBeGrabbed:
                mercPc?.eventMirrorState(GNAME_s_AbortGrab);
                eventMirrorState(GNAME_s_AbortGrab);
                break;
        }
    }

    /// <summary>The merc escaped the catch (rule 20, GrabbedAvoid on the merc's controller).</summary>
    private void GrabbedAvoidRelease(SBasePawn merc)
    {
        SBasePawn? spy = merc.m_AttackedBy;
        if (spy is { Health: > 0, Controller: SPlayerController spyPc })
        {
            spyPc.eventMirrorState(GNAME_s_Grab_Avoided);
        }

        GrabUnlink(spy, merc);
        if (merc.Health > 0)
        {
            eventMirrorState(GNAME_s_GrabbedByPlayer_AvoidGrab);
        }
    }

    private static void GrabUnlink(SBasePawn? spy, SBasePawn? merc)
    {
        if (spy is not null)
        {
            spy.m_AttackTarget = null;
        }

        if (merc is not null)
        {
            merc.m_AttackedBy = null;
        }
    }

    /// <summary>A grab kill: the merc at 0 hp, the kill credited to the spy, the merc's grab death counted.</summary>
    private void GrabKill(SBasePawn? merc, SPlayerController? mercPc, SDamageType Type)
    {
        if (merc is null)
        {
            return;
        }

        merc.Health = 0;
        Level.Game?.Killed(this, mercPc, merc, Type);
        mercPc?.CountMercGrabDeath();
    }

    /// <summary>The merc's grab death statistic.</summary>
    [Uc("SBase.SPlayerController.CountMercGrabDeath", Sha1 = "71c863284c6b7c3eecd7629976ac73886b75cb74")]
    [PortOf("uc:SBase.SPlayerController:CountMercGrabDeath", Spec = "spec/combat/spy-grab.md#release-resolution-server-askservertoungrabpawnway")]
    public void CountMercGrabDeath() => IncStat(this, static s => s.Merc_DeathFromGrab++);

    /// <summary>
    /// A drop attack on the holding spy (rule 26): the merc takes the 51 hp KO cost and is knocked out, or dies by
    /// the neck break credited to the spy; the spy falls (dying or not).
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.PawnTakeDamage", Spec = "spec/combat/spy-grab.md#damage-while-holding")]
    private void DropOnGrabber()
    {
        bCanTalkToGuy = false;
        if (SBPawn is { m_AttackTarget: { } merc } spy)
        {
            var mercPc = merc.Controller as SPlayerController;
            merc.Health -= SvmMath.Trunc(merc.ClassDefaults.Health * K.GrabReleaseHealthFactor);
            if (merc.Health > 0)
            {
                mercPc?.eventMirrorState(GNAME_s_GrabbedByPlayer_Released_KO);
            }
            else
            {
                GrabKill(merc, mercPc, SDamageType.DamTypeGrabBreak);
                mercPc?.eventMirrorState(GNAME_s_GrabbedByPlayer_Released_STRANGLE);
            }

            eventMirrorState(spy.Health <= 0 ? GNAME_s_FallDownDying : CNAME_s_FallDown, CNAME_DIE_InPlaceWhileGrabbing);
        }
    }

    /// <summary>The pawn yaw turned toward <paramref name="TargetYaw"/> by at most Rate x dt.</summary>
    [Uc("SBase.SPlayerController.DampRotation", Sha1 = "9e247e553a03b12242111bdb527ae94d8e8af025")]
    [PortOf("uc:SBase.SPlayerController:DampRotation", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
    public static int DampRotation(int Yaw, int TargetYaw, float Rate, float DeltaTime)
    {
        int diff = FRotator.NormalizeAxis(TargetYaw - Yaw);
        int step = SvmMath.Trunc(Rate * DeltaTime);
        if (diff > step)
        {
            diff = step;
        }
        else if (diff < -step)
        {
            diff = -step;
        }

        return Yaw + diff;
    }
}
