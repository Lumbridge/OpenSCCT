using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Net.Moves;

/// <summary>One saved move (SBase.SSavedMove): the stamp it was sent with and the pawn's state at that moment.</summary>
public sealed class SavedMove
{
    public int Slot;
    public uint Sequence, Epoch;
    public float DeltaTime;
    public FVector Acceleration;
    public bool Jump;
    public float TimeStamp;
    public FVector SavedLocation;
    public FVector SavedVelocity;
    public Actor? SavedBase;
    public EPhysics SavedPhysics;
    public byte ZoneConfig;

    /// <summary>The next (newer) move, or null for the newest.</summary>
    public SavedMove? NextMove;
}

/// <summary>
/// The client's saved moves (spec/native/net-moves.md rule 5): one per send. New moves are created until there are
/// 100, then the list closes into a ring and each send overwrites the oldest in place. Moves are never freed
/// (CleanOutSavedSMoves is a no-op, move-protocol.md rule 22). <see cref="FirstSMove"/> is the oldest,
/// <see cref="LastSMove"/> the newest.
/// </summary>
[UcNative("ASPlayerController::GetFreeSMove", Editor = 0x111e7160)]
public sealed class SavedMoveList
{
    private readonly SavedMove[] _moves;

    public SavedMoveList(int capacity = MoveK.SavedMoveRing, bool preallocate = false)
    {
        _moves = new SavedMove[capacity];
        if (preallocate)
            for (int i = 0; i < _moves.Length; i++) _moves[i] = new SavedMove { Slot = i };
    }

    public void Clear() { _count = 0; _oldest = 0; }
    private int _count;
    private int _oldest;

    public int Count => _count;

    public SavedMove? FirstSMove => _count == 0 ? null : _moves[_oldest];

    public SavedMove? LastSMove => _count == 0 ? null : _moves[(_oldest + _count - 1) % _moves.Length];

    /// <summary>Records a move (GetFreeSMove then fill), returning it.</summary>
    public SavedMove Add(float timeStamp, FVector location, EPhysics physics, byte zoneConfig)
    {
        SavedMove move;
        SavedMove? newest = LastSMove;
        if (_count < _moves.Length)
        {
            move = _moves[_count] ??= new SavedMove { Slot = _count };
            _count++;
        }
        else
        {
            // The ring is full: the oldest move becomes the newest.
            move = _moves[_oldest];
            _oldest = (_oldest + 1) % _moves.Length;
        }

        move.TimeStamp = timeStamp;
        move.SavedLocation = location;
        move.SavedPhysics = physics;
        move.ZoneConfig = zoneConfig;
        move.NextMove = null;
        if (newest is not null && !ReferenceEquals(newest, move))
        {
            newest.NextMove = move;
        }

        return move;
    }

    /// <summary>CleanOutSavedSMoves: its loop body never runs in the original (move-protocol.md rule 22).</summary>
    [Uc("SBase.SPlayerController.CleanOutSavedSMoves", Sha1 = "d80f225c8f473b14868a3f5bd88a8d018ca70ea7")]
    [PortOf("uc:SBase.SPlayerController:CleanOutSavedSMoves", Spec = "spec/net/move-protocol.md#rules")]
    public static void CleanOutSavedSMoves()
    {
    }
}
