using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The SPlayerController natives and helpers of the vertical pipe, zip line and back-to-wall states. The back-to-wall
// probes follow spec/native/natives2-traversal.md rules 1 to 3; the pipe neighbour natives are in
// SPlayerController.Movement.PipeNatives.cs.
public partial class SPlayerController
{
    /// <summary>
    /// A wall for the spy's back: probe A along the pawn's facing (Reverse, a wall in front) or against it, then from
    /// the wall it found the two waist probes and the head probe must all hit; vWallNormal is the normalised sum of
    /// their normals.
    /// </summary>
    [UcNative("ASPlayerController::execCheckBTW", Exe = 0x10aa97e0, Editor = 0x111e5640)]
    [PortOf("nat:SBase.SPlayerController.CheckBTW", Spec = "spec/native/natives2-traversal.md#1-checkbtwreverse---bool")]
    [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 1: 1.5 R probes, waist 0.5 R aside and 0.5 H down, head 0.8 H, flags 0x2df, PHYS_None hits only")]
    public bool CheckBTW(bool Reverse = false)
    {
        if (SBPawn is not { } pawn)
        {
            return false;
        }

        FVector fwd = pawn.Rotation.Vector();
        FVector d = Reverse ? fwd : -fwd;
        float r = pawn.CollisionRadius;
        if (!WorldProbe(pawn, pawn.Location, pawn.Location + d * (MovementK.BtwProbeReachR * r), out FVector n1) ||
            !BtwWallProbes(pawn, n1, out FVector sum))
        {
            return false;
        }

        vWallNormal = sum.SafeNormal();
        if (vWallNormal.IsZero())
        {
            vWallNormal.X = 1f;
        }

        return true;
    }

    /// <summary>
    /// Whether the spy sliding along the wall toward <paramref name="ToRight"/> (his right side when false) can go
    /// on: 1 the wall and the floor go on, 0 the wall ends with room to look round it, -1 blocked or a drop.
    /// </summary>
    [UcNative("ASPlayerController::execCheckBTWEyes", Exe = 0x10aaa000, Editor = 0x111e5dc0)]
    [PortOf("nat:SBase.SPlayerController.CheckBTWEyes", Spec = "spec/native/natives2-traversal.md#2-checkbtweyestoright---int")]
    [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 2: (5, 5, 5) boxes with flags 0xdf, side R + 4 at 0.8 H, reach 2 R, floor H + 31, peek line 100")]
    public int CheckBTWEyes(bool ToRight)
    {
        if (SBPawn is not { } pawn || MovementNatives.Driver(pawn) is not { } driver)
        {
            return -1;
        }

        pawn.Rotation.GetAxes(out FVector x, out FVector y, out _);
        FVector t = -x;
        FVector side = ToRight ? -y : y;
        float r = pawn.CollisionRadius;
        float h = pawn.CollisionHeight;
        FVector l = pawn.Location;
        var box = new FVector(MovementK.BtwEyesBox, MovementK.BtwEyesBox, MovementK.BtwEyesBox);
        var flags = (OpenScct.Physics.Collision.ETraceFlags)MovementK.BtwEyesFlags;
        FVector p = l + side * (r + MovementK.BtwEyesSide) + new FVector(0f, 0f, MovementK.BtwHeadH * h);
        if (driver.Collision.LineCheck(out _, pawn, p + t * (MovementK.BtwEyesReachR * r), p, box, flags))
        {
            FVector q = l + side * MovementK.BtwEyesFloorSide;
            bool floor = driver.Collision.LineCheck(out OpenScct.Physics.Collision.FCheckResult f, pawn,
                new FVector(q.X, q.Y, q.Z - (h + MovementK.BtwEyesFloorDrop)), q, box, flags);
            FVector s0 = l + side * (r + MovementK.BtwEyesSide);
            var e = new FVector(s0.X, s0.Y, floor ? f.Location.Z - MovementK.BtwEyesFloorDrop : l.Z - h - MovementK.BtwEyesFloorDrop);
            return driver.Collision.LineCheck(out _, pawn, e, s0, box, flags) ? 1 : -1;
        }

        return driver.Collision.LineCheck(out _, pawn, l + side * MovementK.BtwEyesPeek, l, default, flags) ? -1 : 0;
    }

    /// <summary>
    /// A wall met while sliding along the wall is a corner the spy can turn round when it is a full-height flat wall:
    /// the probe into it must find the same plane, then the waist and head probes from it must hit.
    /// </summary>
    [UcNative("ASPlayerController::execCheckBTWCorner", Exe = 0x10aa91c0, Editor = 0x111e5050)]
    [PortOf("nat:SBase.SPlayerController.CheckBTWCorner", Spec = "spec/native/natives2-traversal.md#3-checkbtwcornerhitnormal---bool")]
    [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md rule 3: probe 1.5 R into the wall, same plane within a cross length of 0.1, then probes B, C and D")]
    public bool CheckBTWCorner(FVector vNormal)
    {
        if (SBPawn is not { } pawn)
        {
            return false;
        }

        FVector l = pawn.Location;
        if (!WorldProbe(pawn, l, l - vNormal * (MovementK.BtwProbeReachR * pawn.CollisionRadius), out FVector n) ||
            (vNormal ^ n).Size() > MovementK.BtwCornerPlane)
        {
            return false;
        }

        return BtwWallProbes(pawn, n, out _);
    }

    /// <summary>Probes B, C and D of CheckBTW against the wall whose probe-facing normal is <paramref name="n1"/>.</summary>
    private static bool BtwWallProbes(SBasePawn pawn, FVector n1, out FVector sum)
    {
        sum = default;
        var face = new FRotator(0, (-n1).Rotation().Yaw, 0);
        face.GetAxes(out FVector w, out FVector s, out _);
        float r = pawn.CollisionRadius;
        float h = pawn.CollisionHeight;
        FVector v = w * (MovementK.BtwProbeReachR * r);
        FVector l = pawn.Location;
        FVector b = l + s * (MovementK.BtwWaistSideR * r) - new FVector(0f, 0f, MovementK.BtwWaistDownH * h);
        FVector c = l - s * (MovementK.BtwWaistSideR * r) - new FVector(0f, 0f, MovementK.BtwWaistDownH * h);
        var d = new FVector(l.X, l.Y, l.Z + MovementK.BtwHeadH * h);
        if (!WorldProbe(pawn, b, b + v, out FVector n2) || !WorldProbe(pawn, c, c + v, out FVector n3) ||
            !WorldProbe(pawn, d, d + v, out FVector n4))
        {
            return false;
        }

        sum = n2 + n3 + n4;
        return true;
    }

    /// <summary>
    /// The wall at the end of a zip line: a box (35, 35, 20) 60 along the slide must hit a surface facing it
    /// (normal . slide direction below -0.7).
    /// </summary>
    [Uc("SBase.SPlayerController.TestZipWall", Sha1 = "d779bfce73f5f1de8e528f79b9909c50217678c6")]
    [PortOf("uc:SBase.SPlayerController:TestZipWall", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
    public virtual bool TestZipWall()
    {
        if (SBPawn is not { } pawn || pawn.ZipP1 == pawn.ZipP2)
        {
            return false;
        }

        FVector d = (pawn.ZipP1 - pawn.ZipP2).SafeNormal();
        var extent = new FVector(35f, 35f, MovementK.ZipWallProbeHeight);
        Actor? hit = MovementNatives.Trace(pawn, out _, out FVector n, pawn.Location + d * MovementK.ZipWallProbe, pawn.Location, true, extent);
        return hit is not null && (n | d) < MovementK.ZipWallFacing;
    }

    /// <summary>The zip slide speed: v = 210 + 0.015 aForward along the line, GroundSpeed = RunningSpeed v / 191.</summary>
    [PortOf("uc:SBase.SPlayerController:s_ZipLine.PlayerMove", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
    internal FVector ZipSlide(SBasePawn pawn)
    {
        float v = MovementK.ZipSlideBase + MovementK.ZipSlideStick * aForward;
        pawn.GroundSpeed = pawn.RunningSpeed * v / MovementK.ZipSpeedDivisor;
        return pawn.m_geoNormal * v;
    }

    /// <summary>The zip line drop (crouch, end of the line): latch off, jump cleared, MyDoJump, SA_ZIPFALL, s_PlayerJump.</summary>
    [PortOf("uc:SBase.SPlayerController:s_ZipLine.NoGE", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
    internal void DropFromZip(SBasePawn pawn)
    {
        bCrouchWithRoll = false;
        bPressedJump = false;
        pawn.MyDoJump();
        pawn.ChangeEnumAction(SBasePawn.SA_ZIPFALL);
        eventChangeState(MovementStateNames.s_PlayerJump);
    }

    /// <summary>
    /// The peek states' move (Left states shown in the spec, Right states mirror with the Left key): the view
    /// within 15000 of looking back along the wall; leaving when the wall is lost or the key is held, back to the
    /// wall when the key is released, and the stance change 0.5 s apart. The spy does not move.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_BTW_StandLeft.PlayerMove", Spec = "spec/movement/back-to-wall.md#peeking-s_btw_standleft--standright--crouchleft--crouchright")]
    internal void PeekMove(float DeltaTime, bool bLeft, bool bCrouched)
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        int v = MovementK.BtwPeekView;
        UpdateLimitedRotation(DeltaTime, -v, v, -v, v, pawn.Rotation.Yaw + 32768, 0);
        if (!pawn.bWeaponTransit)
        {
            int key = bLeft ? cMoveRight : cMoveLeft;
            int peekAction = bLeft ? SBasePawn.SA_BTW_LEFT : SBasePawn.SA_BTW_RIGHT;
            float now = Level.TimeSeconds;
            if (!CheckBTW() || bUseBackToWall == 1)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_BTW_OUT);
                pawn.ChangeEnumAction(bCrouched ? SBasePawn.SA_WALK_CR_0H : SBasePawn.SA_WALK_ST_0H);
                eventChangeState(bCrouched ? MovementStateNames.s_PlayerWalkingCr0H : MovementStateNames.s_PlayerWalkingSt0H);
                MovementCamera(ECamMode.ECM_Walking);
            }
            else if ((iMove & key) == 0)
            {
                pawn.ChangeEnumAction(bCrouched ? SBasePawn.SA_BTW_CR : SBasePawn.SA_BTW_ST);
                eventChangeState(bCrouched ? MovementStateNames.s_BTW_down : MovementStateNames.s_BTW_up);
            }
            else if (!bCrouched && bCrouchWithRoll)
            {
                if (pawn.bCanCrouch && now - m_LastSpecialMoveTime > MovementK.BtwStanceCooldown)
                {
                    m_LastSpecialMoveTime = now;
                    pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
                    pawn.ChangeEnumAction(peekAction);
                    eventChangeState(bLeft ? MovementStateNames.s_BTW_CrouchLeft : MovementStateNames.s_BTW_CrouchRight);
                }
                else
                {
                    bCrouchWithRoll = false;
                }
            }
            else if (bCrouched && !bCrouchWithRoll)
            {
                if (pawn.CanUnCrouch() && now - m_LastSpecialMoveTime > MovementK.BtwStanceCooldown)
                {
                    m_LastSpecialMoveTime = now;
                    pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
                    pawn.ChangeEnumAction(peekAction);
                    eventChangeState(bLeft ? MovementStateNames.s_BTW_StandLeft : MovementStateNames.s_BTW_StandRight);
                }
                else
                {
                    bCrouchWithRoll = true;
                }
            }
        }

        SendMove(DeltaTime, default);
    }

    /// <summary>
    /// A world probe of the back-to-wall natives: a line with flags 0x2df whose hit counts only on an actor in
    /// PHYS_None (level geometry and static meshes, not movers or pawns); the normal is turned to face the probe.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "natives2-traversal.md Data: world probe")]
    private static bool WorldProbe(SBasePawn pawn, FVector start, FVector end, out FVector normal)
    {
        normal = default;
        if (MovementNatives.Driver(pawn) is not { } driver ||
            !driver.Collision.LineCheck(out OpenScct.Physics.Collision.FCheckResult hit, pawn, end, start, default,
                (OpenScct.Physics.Collision.ETraceFlags)MovementK.BtwProbeFlags) ||
            hit.Actor is not { Physics: EPhysics.PHYS_None })
        {
            return false;
        }

        normal = (hit.Normal | (end - start)) > 0f ? -hit.Normal : hit.Normal;
        return true;
    }
}
