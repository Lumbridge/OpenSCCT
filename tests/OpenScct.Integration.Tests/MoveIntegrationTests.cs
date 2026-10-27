using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Moves;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>A walker on a flat floor behind the move protocol interfaces (stands in for the SBasePawn port).</summary>
internal sealed class FlatPawn : IMovePawn
{
    public FVector Location { get; set; }

    public FVector Velocity { get; set; }

    public FRotator Rotation { get; set; }

    public EPhysics Physics { get; set; } = EPhysics.PHYS_Walking;

    public void SetPhysics(EPhysics physics) => Physics = physics;

    public Actor? Base { get; set; }

    public bool IsAlive => true;

    public byte ZoneConfig => 0;

    public float ZCDecal(int from, int to) => 0f;

    public bool IsMerc => false;

    public float RunningSpeed => 400f;

    public float GroundSpeed { get; set; } = 400f;

    public bool bIsRolling => false;

    public bool bIsCharging => false;

    public bool AdrenalineActive => false;

    public float AdrenalineFactor => 1f;

    public bool UsesPCRot { get; set; }

    public bool bTestBackFeetGrab => false;

    public bool ActionsWaitForBigMove => false;

    public int TakeActionsForBigMove(Span<int> actions) => 0;

    public void ExecuteReceivedActions(int val0, int val1)
    {
    }

    public int iYawPitchFromView { get; set; }

    public FVector ZipP1 { get; set; }

    public FVector ZipP2 { get; set; }

    public FVector NewZipP1 { get; set; }

    public FVector NewZipP2 { get; set; }

    public FVector vVandammePos { get; set; }

    public FVector vWallJumpDir { get; set; }

    public FVector m_geoNormal { get; set; }

    public bool bKeepLocalVandammePos => false;

    /// <summary>A wall only the server has, at this X, or NaN.</summary>
    public float WallX = float.NaN;

    public void MoveAutonomous(float deltaTime, FVector acceleration, bool jump)
    {
        var a = new FVector(SvmMath.Trunc(acceleration.X), SvmMath.Trunc(acceleration.Y), 0f);
        FVector v = a.IsZero() ? Velocity * SvmMath.Max(0f, 1f - 8f * deltaTime) : Velocity + a * deltaTime;
        if (v.SizeSquared() > GroundSpeed * GroundSpeed)
        {
            v = v.SafeNormal() * GroundSpeed;
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

    public void ReinitBecauseOfDivergences() => Physics = EPhysics.PHYS_Walking;

    public bool BaseMoverMovedVertically(float seconds) => false;

    public bool BaseMoverMovingVertically => false;

    public void Place(FVector location, Actor? newBase, FRotator rotation)
    {
        Location = location;
        Base = newBase;
        Rotation = rotation;
    }
}

/// <summary>The move protocol's view of an SPlayerController.</summary>
internal sealed class ControllerAdapter : IMoveController
{
    private readonly SPlayerController _pc;

    public ControllerAdapter(SPlayerController pc)
    {
        _pc = pc;
    }

    public ServerMoves? Server { get; set; }

    public ClientMoves? Client { get; set; }

    public int Reinits;

    public FRotator Rotation
    {
        get => _pc.Rotation;
        set => _pc.Rotation = value;
    }

    public bool bPressedJump => false;

    public float TimeSeconds => _pc.Level.TimeSeconds;

    public bool IsPaused => false;

    public float MirrorStateAcknowledgementTime
    {
        get => _pc.MirrorStateAcknowledgementTime;
        set => _pc.MirrorStateAcknowledgementTime = value;
    }

    public bool bWaitingForReinit
    {
        get => _pc.bWaitingForReinit;
        set => _pc.bWaitingForReinit = value;
    }

    public bool bUseRootMotionAdditionnalPhysics => false;

    public void ReinitPawnBecauseOfDivergences()
    {
        Reinits++;
        Server?.ReinitPawn();
    }

    public void ClientReinitDone(byte reinitType) => Client?.Transport.TellServerIAMReadyToReinit();

    public void ServerReinitReady() => _pc.bWaitingForReinit = false;
}

/// <summary>
/// The move protocol between real drivers: each player is an SPlayerController owned by its connection, the moves and
/// corrections are its replicated functions (generated encoding: vectors rounded, floats exact), under SimLink.
/// </summary>
public sealed class MoveIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public MoveIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed class Player
    {
        public required ClientSession Session;
        public FlatPawn ClientPawn = new();
        public FlatPawn ServerPawn = new();
        public ClientMoves? Client;
        public ServerMoves? Server;
        public SPlayerController? ServerPc;
    }

    private static (WalkerRig Rig, List<Player> Players) Create(SimLinkProfile profile, ulong seed, int count)
    {
        WalkerRig rig = WalkerRig.Create(profile, seed, count, new ServerSettings { SpawnWalkers = false, NetMode = ENetMode.NM_ListenServer });
        var players = new List<Player>();
        var serverEndpoints = new Dictionary<Actor, MoveEndpoint>();
        NetMoveTransport.Register(rig.Server.Driver, a => serverEndpoints.GetValueOrDefault(a));
        for (int i = 0; i < count; i++)
        {
            players.Add(new Player { Session = rig.Clients[i] });
        }

        rig.Server.PlayerJoined += slot =>
        {
            Player p = players[int.Parse(slot.Name.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture)];
            SPlayerController pc = rig.Server.World.Level.Spawn<SPlayerController>()!;
            slot.Connection.Actor = pc;
            p.ServerPc = pc;
            var adapter = new ControllerAdapter(pc);
            p.Server = new ServerMoves(p.ServerPawn, adapter, new NetMoveTransport(rig.Server.Driver, pc))
            {
                ClientNetSpeed = slot.Connection.CurrentNetSpeed,
            };
            adapter.Server = p.Server;
            serverEndpoints[pc] = new MoveEndpoint(p.Server, null);
        };

        foreach (Player p in players)
        {
            Player player = p;
            var clientEndpoints = new Dictionary<Actor, MoveEndpoint>();
            NetMoveTransport.Register(player.Session.Driver, a => clientEndpoints.GetValueOrDefault(a));
            player.Session.ActorChannelOpened += channel =>
            {
                if (channel.Actor is SPlayerController { Role: ENetRole.ROLE_AutonomousProxy } pc)
                {
                    var adapter = new ControllerAdapter(pc);
                    player.Client = new ClientMoves(player.ClientPawn, adapter, new NetMoveTransport(player.Session.Driver, pc));
                    adapter.Client = player.Client;
                    clientEndpoints[pc] = new MoveEndpoint(null, player.Client);
                }
            };
        }

        return (rig, players);
    }

    private static void Run(WalkerRig rig, List<Player> players, int frames, Func<long, FVector> input)
    {
        for (int f = 0; f < frames; f++)
        {
            foreach (Player p in players)
            {
                if (p.Client is { } c)
                {
                    if (c.bUpdatePosition)
                    {
                        c.ClientUpdatePosition();
                    }

                    c.MyReplicateMove(1f / 60f, input(rig.Frame));
                }
            }

            rig.Step();
        }
    }

    [Theory]
    [InlineData("B", 41UL)]
    [InlineData("C", 42UL)]
    public void MovesReplicateAndOnlyTheForcedCorrectionsGoOut(string profile, ulong seed)
    {
        (WalkerRig rig, List<Player> players) = Create(SimLinkProfile.Get(profile), seed, 2);
        using (rig)
        {
            Run(rig, players, 240, _ => default);
            Assert.All(players, p => Assert.NotNull(p.Client));
            Run(rig, players, 900, frame => (frame / 150) % 2 == 0 ? new FVector(3000f, 0f, 0f) : new FVector(0f, 3000f, 0f));
            foreach (Player p in players)
            {
                _output.WriteLine(FormattableString.Invariant(
                    $"{profile}: sent full {p.Client!.Sent.Full} short {p.Client.Sent.Short}; server applied {p.Server!.MovesApplied} ignored {p.Server.MovesIgnored}; corrections {p.Server.Sent.Corrections + p.Server.Sent.ShortCorrections}, applied {p.Client.CorrectionsApplied}; error {(p.ClientPawn.Location - p.ServerPawn.Location).Size():F2} uu"));
                Assert.True(p.Client.Sent.Full > 600);

                // Same input, same pawn: the server's pawn follows within rounding and lost moves; the forced
                // corrections (at least once a second) match the saved moves and move nothing.
                Assert.InRange((p.ClientPawn.Location - p.ServerPawn.Location).Size(), 0f, 40f);
                Assert.InRange(p.Client.CorrectionsApplied, 0, 3);
            }
        }
    }

    [Theory]
    [InlineData("B", 43UL)]
    [InlineData("C", 44UL)]
    public void AServerOnlyWallCorrectsTheClient(string profile, ulong seed)
    {
        (WalkerRig rig, List<Player> players) = Create(SimLinkProfile.Get(profile), seed, 1);
        using (rig)
        {
            Run(rig, players, 240, _ => default);
            Player p = players[0];
            p.ServerPawn.WallX = 400f;
            Run(rig, players, 300, _ => new FVector(3000f, 0f, 0f));
            Run(rig, players, 120, _ => default);
            _output.WriteLine(FormattableString.Invariant(
                $"{profile}: corrections {p.Server!.Sent.Corrections + p.Server.Sent.ShortCorrections}, applied {p.Client!.CorrectionsApplied}; client x {p.ClientPawn.Location.X:F2} server x {p.ServerPawn.Location.X:F2}"));
            Assert.True(p.Client.CorrectionsApplied > 0);
            Assert.Equal(400f, p.ServerPawn.Location.X, 2);
            Assert.InRange(MathF.Abs(p.ClientPawn.Location.X - 400f), 0f, 2.5f);
            Assert.Equal(0, ((ControllerAdapter)p.Server.Controller).Reinits);
        }
    }

    [Fact]
    public void APCRotMoveTurnsTheServersAimEvenWhenThePhysicsDiffer()
    {
        // MyServerMoveWithPCRot writes iYawPitchFromView before its physics check (game 0x10aaec30): while the host's
        // copy of the pawn is in another physics, the connected client's PCRot moves are not simulated but its aim
        // still reaches the host.
        (WalkerRig rig, List<Player> players) = Create(SimLinkProfile.A, 46UL, 1);
        using (rig)
        {
            Run(rig, players, 240, _ => default);
            Player p = players[0];
            Assert.NotNull(p.Client);
            p.ClientPawn.UsesPCRot = true;
            p.ServerPawn.Physics = EPhysics.PHYS_Falling;
            p.ServerPawn.iYawPitchFromView = 0;
            long ignored = p.Server!.MovesIgnored;
            Run(rig, players, 30, _ =>
            {
                p.Client!.Controller.Rotation = new FRotator(400, 20000, 0);
                return default;
            });

            Assert.True(p.Server.MovesIgnored > ignored, "the host simulated moves of a pawn in another physics");
            Assert.Equal(MovePacking.PackW(20000, 400), p.ServerPawn.iYawPitchFromView);
            Assert.Equal(0, ((ControllerAdapter)p.Server.Controller).Reinits);
        }
    }

    [Fact]
    public void AnotherClientCannotMoveSomeoneElsesController()
    {
        (WalkerRig rig, List<Player> players) = Create(SimLinkProfile.A, 45UL, 2);
        using (rig)
        {
            Run(rig, players, 240, _ => default);

            // Client 1 never has client 0's controller (owner-only relevancy), so it cannot even name it.
            Assert.Null(players[1].Session.Driver.FindActorByNetGuid((uint)players[0].ServerPc!.ActorId));
            Assert.NotNull(players[0].Session.Driver.FindActorByNetGuid((uint)players[0].ServerPc!.ActorId));
        }
    }
}
