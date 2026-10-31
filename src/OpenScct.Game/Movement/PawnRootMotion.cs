using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>
/// The root-motion side of a pawn's base animation channel for PHYS_RootMotion (IRootMotionMesh): while locked,
/// each call returns how far the clip's root (its keyed root track, else its root bone) moved since the last call,
/// measured on absolute positions so a clip change while locked moves from the last clip's last root position, turned
/// into world space by the yaw the pawn had when the lock started (sideways reversed for a mirrored clip). Locking stores the start of the move
/// (Pawn.vLocationBeforeRM, rRotBeforeRM) that EndRootMotion places the pawn from. The channel itself advances with
/// the pawn's animation (once per tick), so a call reads the movement of the frame already advanced.
/// </summary>
public sealed class PawnRootMotion : IRootMotionMesh
{
    public readonly record struct State(int LockType, FVector LockedRoot, FVector LastRoot, float LastYaw, int LockYaw, int Carried, int Serial);

    public State Capture() => new(RootLockType, LockedRoot, _lastRoot, _lastRootYaw, _lockYaw, _carried, _serial);

    public void Restore(in State state)
    {
        RootLockType = state.LockType; LockedRoot = state.LockedRoot; _lastRoot = state.LastRoot;
        _lastRootYaw = state.LastYaw; _lockYaw = state.LockYaw; _carried = state.Carried; _serial = state.Serial;
    }

    private readonly SBasePawn _pawn;
    private FVector _lastRoot;
    private float _lastRootYaw;
    private int _lockYaw;
    private int _carried;
    private int _serial = -1;

    public PawnRootMotion(SBasePawn pawn)
    {
        _pawn = pawn;
    }

    public int RootLockType { get; private set; }

    /// <summary>
    /// The clip root (track, else bone; pawn space, absolute) when the lock was set: R(lock) of the drawn pose's root
    /// offset (coop.md rule 24, anim.md rule 3.4).
    /// </summary>
    public FVector LockedRoot { get; private set; }

    /// <summary>
    /// The turn the root motion has carried since the lock (the GetRootRotationDelta turns, mirror-signed as physics
    /// applies them) at <paramref name="clipFrame"/> of channel 0: the turns taken so far plus the root's turn from the
    /// last measured frame to that one. The drawn pose with lock bit 2 turns the root back by it (anim.md rule 3.5).
    /// 0 when unlocked; a clip not yet measured (played since the last call) adds nothing.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "pose build game 0x109ba4f0 at 0x109bcbac: the forward bit-2 turn is yaw(0x250) - yaw(0x1f0), negated for a bone clip mirrored or a track clip not mirrored (the GetRootRotationDelta 0x109b7f30 sign rules), so it is minus the root turn the actor took since the lock")]
    public int CarriedRootTurn(PawnAnimChannel c, float clipFrame)
    {
        System.ArgumentNullException.ThrowIfNull(c);
        if (RootLockType == 0 || c.Clip is null || c.Serial != _serial)
        {
            return RootLockType == 0 ? 0 : _carried;
        }

        int turn = SvmMath.Round(c.Clip.RootYawAt(clipFrame)) - SvmMath.Round(_lastRootYaw);
        return _carried + (c.bMirror ? -turn : turn);
    }

    /// <summary>
    /// Locks (or with 0 unlocks) the root: the movement is counted from the clip's current frame, and a lock
    /// records the pawn's location and yaw as the start of the move.
    /// </summary>
    [UcNative("USkeletalMeshInstance::LockRootMotion", Editor = 0x11105e70)]
    [PortOf("eng:USkeletalMeshInstance::LockRootMotion", Spec = "spec/native/anim.md#5-root-motion-lockrootmotion-getrootlocationdelta-getrootrotationdelta")]
    [Provenance(ProvenanceKind.Decoded, "anim.md root motion rule 1: lock frame from Location and Rotation, vLocationBeforeRM and rRotBeforeRM, first delta zero")]
    public void LockRootMotion(int LockType)
    {
        RootLockType = LockType;
        PawnAnimChannel c = _pawn.PawnAnim[0];
        _serial = c.Serial;
        _lastRoot = RootOf(c);
        LockedRoot = _lastRoot;
        _lastRootYaw = c.Clip?.RootYawAt(c.ClipFrame) ?? 0f;
        _lockYaw = _pawn.Rotation.Yaw;
        _carried = 0;
        if (LockType != 0)
        {
            _pawn.vLocationBeforeRM = _pawn.Location;
            _pawn.rRotBeforeRM = _pawn.Rotation.Yaw;
        }
    }

    /// <summary>The root bone's world movement and turn since the last call.</summary>
    [Provenance(ProvenanceKind.Decoded, "anim.md root motion rules 2 and 3: channel 0 root in the lock frame, delta since the last call")]
    [Provenance(ProvenanceKind.Measured, "golden moveset.spy-wall-jump frame 36: a new clip on channel 0 while locked takes its first delta from the previous clip's last root position, both absolute (ANSWERS-moveset.md finding 2)")]
    [Provenance(ProvenanceKind.Measured, "golden traversal.spy-ledge-pull-up: the yaw follows the root's turn during the lock-3 climb (16380 to 17755), and the end placement takes it back")]
    public void AdvanceRootMotion(out FVector DeltaLocation, out FRotator DeltaRotation)
    {
        DeltaRotation = default;
        PawnAnimChannel c = _pawn.PawnAnim[0];
        if (RootLockType == 0 || c.Clip is null)
        {
            DeltaLocation = default;
            return;
        }

        if (c.Serial != _serial)
        {
            _serial = c.Serial;
            _lastRootYaw = c.Clip.RootYawAt(c.bBackward ? c.EndFrame : 0f);
        }

        FVector root = RootOf(c);
        FVector local = root - _lastRoot;
        _lastRoot = root;
        if (c.bMirror)
        {
            local.Y = -local.Y;
        }

        DeltaLocation = MovementMath.ToWorld(default, new FRotator(0, _lockYaw, 0), local);

        // The root's turn (GetRootRotationDelta, anim.md rule 5.4), mirrored with the clip; physics applies it with
        // lock bit 2. Whole rotator units of the running value, so the turn adds up to the clip's.
        float rootYaw = c.Clip.RootYawAt(c.ClipFrame);
        int turn = SvmMath.Round(rootYaw) - SvmMath.Round(_lastRootYaw);
        _lastRootYaw = rootYaw;
        DeltaRotation = new FRotator(0, c.bMirror ? -turn : turn, 0);
        _carried += DeltaRotation.Yaw;
    }

    // The sampled clip position: a backward clip's frame rises from 0 while its key runs from the end (ANSWERS-anim.md A5).
    private static FVector RootOf(PawnAnimChannel c) => c.Clip?.RootAt(c.ClipFrame) ?? default;
}
