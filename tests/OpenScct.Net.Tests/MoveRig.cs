using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net.Moves;

namespace OpenScct.Net.Tests;

/// <summary>A walker on a flat floor for move protocol tests: accelerate, cap at GroundSpeed, brake, move.</summary>
internal sealed class TestMovePawn : IMovePawn
{
    public FVector Location { get; set; }

    public FVector Velocity { get; set; }

    public FRotator Rotation { get; set; }

    public EPhysics Physics { get; set; } = EPhysics.PHYS_Walking;

    public void SetPhysics(EPhysics physics) => Physics = physics;

    public Actor? Base { get; set; }

    public bool IsAlive { get; set; } = true;

    public byte ZoneConfig { get; set; }

    public float ZCDecal(int from, int to) => (from - to) * 10f;

    public bool IsMerc { get; set; }

    public float RunningSpeed { get; set; } = 400f;

    public float GroundSpeed { get; set; } = 400f;

    public bool bIsRolling { get; set; }

    public bool bIsCharging { get; set; }

    public bool AdrenalineActive { get; set; }

    public float AdrenalineFactor { get; set; } = 1.5f;

    public bool UsesPCRot { get; set; }

    public bool bTestBackFeetGrab { get; set; }

    public readonly List<int> QueuedActions = new();
    public readonly List<int> ExecutedActions = new();

    public bool ActionsWaitForBigMove => QueuedActions.Count > 0;

    public int TakeActionsForBigMove(Span<int> actions)
    {
        int n = Math.Min(6, QueuedActions.Count);
        for (int i = 0; i < n; i++)
        {
            actions[i] = QueuedActions[i];
        }

        QueuedActions.Clear();
        return n;
    }

    public void ExecuteReceivedActions(int val0, int val1)
    {
        Span<int> a = stackalloc int[6];
        int n = MovePacking.UnCompressActions(val0, val1, a);
        for (int i = 0; i < n; i++)
        {
            ExecutedActions.Add(a[i]);
        }
    }

    public int iYawPitchFromView { get; set; }

    public FVector ZipP1 { get; set; }

    public FVector ZipP2 { get; set; }

    public FVector NewZipP1 { get; set; }

    public FVector NewZipP2 { get; set; }

    public FVector vVandammePos { get; set; }

    public FVector vWallJumpDir { get; set; }

    public FVector m_geoNormal { get; set; }

    public bool bKeepLocalVandammePos { get; set; }

    /// <summary>A wall at this X that only the server has (a door the client does not see), or NaN.</summary>
    public float WallX = float.NaN;

    public int MovesRun;

    public void MoveAutonomous(float deltaTime, FVector acceleration, bool jump)
    {
        MovesRun++;
        var a = new FVector(SvmMath.Trunc(acceleration.X), SvmMath.Trunc(acceleration.Y), SvmMath.Trunc(acceleration.Z));
        if (Physics != EPhysics.PHYS_Walking)
        {
            return;
        }

        FVector v = Velocity;
        if (a.IsZero())
        {
            v *= SvmMath.Max(0f, 1f - 8f * deltaTime);
        }
        else
        {
            v += a * deltaTime;
            if (v.SizeSquared() > GroundSpeed * GroundSpeed)
            {
                v = v.SafeNormal() * GroundSpeed;
            }
        }

        FVector next = Location + v * deltaTime;
        if (!float.IsNaN(WallX) && next.X > WallX)
        {
            next.X = WallX;
            v.X = 0f;
        }

        Velocity = v;
        Location = next;
    }

    public void MovePawn(FVector delta) => Location += delta;

    public void SnapTo(FVector location) => Location = location;

    public int Reinits;

    public void ReinitBecauseOfDivergences()
    {
        Reinits++;
        Physics = EPhysics.PHYS_Walking;
    }

    public bool BaseMoverMovedVertically(float seconds) => false;

    public bool BaseMoverMovingVertically => false;

    public void Place(FVector location, Actor? newBase, FRotator rotation)
    {
        Location = location;
        Base = newBase;
        Rotation = rotation;
    }
}

internal sealed class TestMoveController : IMoveController
{
    public Func<float> Clock = () => 0f;

    public FRotator Rotation { get; set; }

    public bool bPressedJump { get; set; }

    public float TimeSeconds => Clock();

    public bool IsPaused { get; set; }

    public float MirrorStateAcknowledgementTime { get; set; }

    public bool bWaitingForReinit { get; set; }

    public bool bUseRootMotionAdditionnalPhysics { get; set; }

    public Action? OnReinit;
    public Action<byte>? OnClientReinit;
    public int ReinitRequests;
    public int ReadyCount;

    public void ReinitPawnBecauseOfDivergences()
    {
        ReinitRequests++;
        OnReinit?.Invoke();
    }

    public void ClientReinitDone(byte reinitType) => OnClientReinit?.Invoke(reinitType);

    public void ServerReinitReady()
    {
        ReadyCount++;
        bWaitingForReinit = false;
    }
}

/// <summary>
/// One client and the server joined by an in-memory link with a delay in frames and seeded loss. Vectors are rounded
/// on the way as the generated parameter encoding does; floats are exact.
/// </summary>
internal sealed class MoveRig
{
    private sealed class Link : IMoveTransport
    {
        private readonly MoveRig _rig;
        private readonly bool _toServer;

        public Link(MoveRig rig, bool toServer)
        {
            _rig = rig;
            _toServer = toServer;
        }

        public int OutstandingReliable { get; set; }

        private void Post(bool reliable, Action deliver) => _rig.Post(_toServer, reliable, deliver);

        private static FVector R(FVector v) => new(NetWire.RoundComponent(v.X), NetWire.RoundComponent(v.Y), NetWire.RoundComponent(v.Z));

        public void MyServerMove(float TimeStamp, FVector InAccel, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel)
        {
            _rig.Log.Add("Full");
            Post(false, () => _rig.Server.MyServerMove(TimeStamp, R(InAccel), R(ClientLoc), View, DiversInfos, OldTimeDelta, OldAccel));
        }

        public void MyShortServerMove(float TimeStamp, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel)
        {
            _rig.Log.Add("Short");
            Post(false, () => _rig.Server.MyShortServerMove(TimeStamp, R(ClientLoc), View, DiversInfos, OldTimeDelta < 0 ? 0 : OldTimeDelta, OldAccel));
        }

        public void MyServerMoveWithPCRot(float TimeStamp, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel)
        {
            _rig.Log.Add("PCRot");
            Post(false, () => _rig.Server.MyServerMoveWithPCRot(TimeStamp, R(ClientLoc), View, DiversInfos, OldTimeDelta, OldAccel));
        }

        public void BigServerMove(float MSATime, float TimeStamp, FVector InAccel, FVector ClientLoc, FVector Vel, int View, byte DiversInfos,
            int Val0, int Val1)
        {
            _rig.Log.Add("Big");
            Post(true, () => _rig.Server.BigServerMove(MSATime, TimeStamp, R(InAccel), R(ClientLoc), R(Vel), View, DiversInfos, Val0, Val1));
        }

        public void UnreliableReplicateBigServerMove(in BigServerMoveData move)
        {
            _rig.Log.Add("BigResend");
            BigServerMoveData m = move;
            m.InAccel = R(m.InAccel);
            m.ClientLoc = R(m.ClientLoc);
            m.Vel = R(m.Vel);
            Post(false, () => _rig.Server.UnreliableReplicateBigServerMove(m));
        }

        public void MyShortClientAdjustPosition(float TimeStamp, float NewLocX, float NewLocY, float NewLocZ, Actor? NewBase)
        {
            _rig.Log.Add("ShortAdjust");
            Post(false, () => _rig.Client.MyShortClientAdjustPosition(TimeStamp, NewLocX, NewLocY, NewLocZ, NewBase));
        }

        public void MyClientAdjustPosition(float TimeStamp, float NewLocX, float NewLocY, float NewLocZ, float NewVelX, float NewVelY,
            float NewVelZ, Actor? NewBase)
        {
            _rig.Log.Add("Adjust");
            Post(false, () => _rig.Client.MyClientAdjustPosition(TimeStamp, NewLocX, NewLocY, NewLocZ, NewVelX, NewVelY, NewVelZ, NewBase));
        }

        public void ClientReinitPawnBecauseOfDivergences(byte RT, float MSATime, FVector vPos, FRotator rRot, Actor? aBase)
        {
            _rig.Log.Add("Reinit");
            Post(true, () => _rig.Client.ClientReinitPawnBecauseOfDivergences(RT, MSATime, R(vPos), rRot, aBase));
        }

        public void TellServerIAMReadyToReinit()
        {
            _rig.Log.Add("Ready");
            Post(true, () => _rig.Server.TellServerIAMReadyToReinit());
        }
    }

    private readonly List<(long Due, bool ToServer, Action Deliver)> _queue = new();
    private readonly SimRandom _random;

    public MoveRig(int delayFrames = 3, float lossPercent = 0f, ulong seed = 1)
    {
        DelayFrames = delayFrames;
        LossPercent = lossPercent;
        _random = new SimRandom(seed, 3, 3);
        ClientController.Clock = () => Time;
        ServerController.Clock = () => Time;
        ToServer = new Link(this, toServer: true);
        ToClient = new Link(this, toServer: false);
        Client = new ClientMoves(ClientPawn, ClientController, ToServer);
        Server = new ServerMoves(ServerPawn, ServerController, ToClient);
        ServerController.OnReinit = Server.ReinitPawn;
        ClientController.OnClientReinit = _ => Client.Transport.TellServerIAMReadyToReinit();
    }

    public int DelayFrames { get; set; }

    public float LossPercent { get; set; }

    public long Frame { get; private set; }

    /// <summary>Level.TimeSeconds of both sides (one clock; the link delay stands for the travel time).</summary>
    public float Time => Frame / 60f;

    public readonly TestMovePawn ClientPawn = new();
    public readonly TestMovePawn ServerPawn = new();
    public readonly TestMoveController ClientController = new();
    public readonly TestMoveController ServerController = new();
    public readonly List<string> Log = new();
    public readonly IMoveTransport ToServer;
    public readonly IMoveTransport ToClient;
    public readonly ClientMoves Client;
    public readonly ServerMoves Server;

    public int OutstandingReliable
    {
        set => ((Link)ToServer).OutstandingReliable = value;
    }

    private void Post(bool toServer, bool reliable, Action deliver)
    {
        if (!reliable && LossPercent > 0f && _random.FRand() * 100f < LossPercent)
        {
            return;
        }

        _queue.Add((Frame + DelayFrames, toServer, deliver));
    }

    /// <summary>One client frame with <paramref name="accel"/>, then delivery of what is due.</summary>
    public void Step(FVector accel, int frames = 1)
    {
        for (int i = 0; i < frames; i++)
        {
            Frame++;
            if (Client.bUpdatePosition)
            {
                Client.ClientUpdatePosition();
            }

            Client.MyReplicateMove(1f / 60f, accel);
            for (int q = 0; q < _queue.Count; q++)
            {
                if (_queue[q].Due <= Frame)
                {
                    Action deliver = _queue[q].Deliver;
                    _queue.RemoveAt(q);
                    q--;
                    deliver();
                }
            }
        }
    }
}
