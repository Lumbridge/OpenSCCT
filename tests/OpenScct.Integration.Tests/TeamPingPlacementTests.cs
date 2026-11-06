using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class TeamPingPlacementTests
{
    [Theory]
    [InlineData("TestRange", PlaySide.Spy)]
    [InlineData("MissE", PlaySide.Merc)]
    public void ListenPlayerCanPingFromAReplicatedRemoteView(string map, PlaySide side)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder(map) is null, $"{map} content is required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = map, Side = side, Rules = PlayRules.MapSettings,
            HostMode = PlayHostMode.Inline, MatchSeed = 3,
        }, clock);

        void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }

        Step(360);
        Assert.True(session.Client.SupportsTeamPings);
        var authority = Assert.IsType<SPlayerController>(session.ServerController);
        SBasePawn pawn = Assert.IsAssignableFrom<SBasePawn>(authority.Pawn);
        Actor camera;
        if (side == PlaySide.Spy)
        {
            camera = Assert.IsType<SStickyCam>(pawn.XLevel.SpawnActor(new SStickyCam(),
                pawn.Location + new FVector(0, 0, 100), default, pawn));
        }
        else
        {
            SProjoInteraction? nearest = null;
            foreach (SProjoInteraction interaction in pawn.XLevel.AllActors<SProjoInteraction>())
            {
                if (!interaction.bDeleteMe && interaction.bCollideActors && interaction.Owner is SProjoFixe)
                {
                    nearest = interaction;
                    break;
                }
            }

            Assert.NotNull(nearest);
            MovementNatives.FarMoveActor(pawn, nearest.Location + new FVector(0, 0, 64));
            Step(90); // Landing after the reposition would otherwise close the mounted view.
            nearest.eventInitInteract(authority, 0);
            camera = Assert.IsType<SProjoFixe>(pawn.UsedObject);
        }

        Step(120);
        var client = Assert.IsType<SPlayerController>(session.LocalController);
        Assert.True(TeamPingPlacement.TryGetSource(authority, out Actor serverSource, out _), authority.GetStateName().ToString());
        Assert.Same(camera, serverSource);
        Assert.True(TeamPingPlacement.TryGetSource(client, out Actor clientSource, out FVector origin), client.GetStateName().ToString());
        Assert.Equal(camera.GetType(), clientSource.GetType());
        ViewSample view = session.View;
        Assert.True(view.Valid);
        Assert.Equal(origin, view.Location);
        Assert.True(TeamPingPlacement.TryProjectTarget(client, clientSource, view.Location, view.Rotation, out FVector point));
        Assert.True(TeamPingPlacement.TryValidateTarget(authority, serverSource, point));
        var game = Assert.IsType<SGameInfo>(authority.Level.Game);
        Assert.True(game.GameReplicationInfo!.bMatchHasBegun);
        Assert.False(game.bGameEnded);

        var marks = new List<TeamPingMark>();
        session.Client.TeamPingReceived += marks.Add;
        session.Client.SendTeamPing(TeamPingKind.Watch, clientSource, point);
        Step(30);
        TeamPingMark mark = Assert.Single(marks);
        Assert.Equal(TeamPingKind.Watch, mark.Kind);
        Assert.Equal(point, mark.Location);
    }

    [Fact]
    public void BodyViewRequiresALivingAssignedPlayerAndRejectsFreeCamera()
    {
        var (_, player, pawn) = CreatePlayer();

        Assert.True(TeamPingPlacement.TryGetTeam(player, out byte team));
        Assert.Equal(0, team);
        Assert.True(TeamPingPlacement.TryGetSource(player, out Actor source, out FVector origin));
        Assert.Same(pawn, source);
        Assert.Equal(new FVector(0, 0, 80), origin);

        player.FreeCameraFlying = 1;
        Assert.False(TeamPingPlacement.TryGetSource(player, out _, out _));
        player.FreeCameraFlying = 0;

        TeamInfo assignedTeam = player.PlayerReplicationInfo!.Team!;
        player.PlayerReplicationInfo.Team = null;
        Assert.False(TeamPingPlacement.TryGetTeam(player, out _));
        player.PlayerReplicationInfo.Team = assignedTeam;
        player.PlayerReplicationInfo.bOnlySpectator = true;
        Assert.False(TeamPingPlacement.TryGetSource(player, out _, out _));
        player.PlayerReplicationInfo.bOnlySpectator = false;
        pawn.Health = 0;
        Assert.False(TeamPingPlacement.TryGetTeam(player, out _));
    }

    [Fact]
    public void SettledSpyStickyCameraUsesItsMountAndRejectsPredictionAndStaleSources()
    {
        var (world, player, pawn) = CreatePlayer();
        var position = new FVector(5000, 0, 100);
        var cam = world.Level.SpawnActor(new SStickyCam(), position, default, pawn)!;
        pawn.UsedObject = cam;
        player.GotoState(GadgetStates.s_UseObject_StickyCam_SmoothToPosition);
        Assert.False(TeamPingPlacement.TryGetSource(player, out _, out _));

        player.GotoState(GadgetStates.s_UseObject_StickyCam);
        Assert.True(TeamPingPlacement.TryGetSource(player, out Actor source, out FVector origin));
        Assert.Same(cam, source);
        Assert.Equal(position + new FVector(3, 0, 0), origin);
        Assert.True(TeamPingPlacement.TryValidateTarget(player, source, origin + new FVector(4000, 0, 0)));
        Assert.False(TeamPingPlacement.TryValidateTarget(player, source, origin + new FVector(4100, 0, 0)));
        Assert.False(TeamPingPlacement.TryValidateTarget(player, pawn, origin));

        player.PredictedStickyCam = cam;
        Assert.False(TeamPingPlacement.TryGetSource(player, out _, out _));
        player.PredictedStickyCam = null;
        cam.SetOwner(null);
        Assert.False(TeamPingPlacement.TryGetSource(player, out _, out _));
    }

    [Fact]
    public void MercCanPingFromAnOccupiedNetworkFeedButNotFromADisabledOne()
    {
        var (world, player, pawn) = CreatePlayer(merc: true);
        var position = new FVector(1000, 0, 100);
        var cam = world.Level.SpawnActor(new SCamNetwork { bNoDelete = false }, position, default)!;
        cam.aUsedByPawn = world.Level.Spawn<SPAWNDEFENSE>();
        player.GotoState(GadgetStates.s_UseObject_CamNetwork);
        pawn.UsedObject = cam;

        Assert.True(TeamPingPlacement.TryGetSource(player, out Actor source, out FVector origin));
        Assert.Same(cam, source);
        Assert.Equal(position + new FVector(8, 0, 0), origin);

        cam.DisabledSeconds = 5;
        Assert.False(TeamPingPlacement.TryGetSource(player, out _, out _));
        cam.DisabledSeconds = 0;
        player.GotoState(GadgetStates.s_UseObject_CamNetworkStWaitForServer);
        Assert.False(TeamPingPlacement.TryGetSource(player, out _, out _));
    }

    [Fact]
    public void MercMiradorUsesItsRotatedCameraOffset()
    {
        var (world, player, pawn) = CreatePlayer(merc: true);
        var position = new FVector(100, 200, 300);
        var camera = world.Level.SpawnActor(new SProjoFixe { bNoDelete = false, CameraOffset = new FVector(40, 0, 20) }, position,
            new FRotator(0, 16384, 0))!;
        pawn.UsedObject = camera;
        player.GotoState(new Name("s_UseObject_Mirador"));

        Assert.True(TeamPingPlacement.TryGetSource(player, out Actor source, out FVector origin));
        Assert.Same(camera, source);
        Assert.InRange(FVector.DistSquared(position + new FVector(0, 40, 20), origin), 0, 0.01f);
    }

    [Fact]
    public void PlacementStopsAtWorldSurfacesAndUsesTheFallbackWhenTheRayMisses()
    {
        var geometry = new CollisionDataBuilder();
        geometry.AddBox(new FVector(500, -100, 0), new FVector(530, 100, 200));
        var (_, player, pawn) = CreatePlayer(geometry: geometry);
        Assert.True(TeamPingPlacement.TryGetSource(player, out _, out FVector eye));

        Assert.True(TeamPingPlacement.TryProjectTarget(player, pawn, eye, default, out FVector hit));
        Assert.InRange(hit.X, 498f, 502f);
        Assert.True(TeamPingPlacement.TryValidateTarget(player, pawn, hit));
        Assert.False(TeamPingPlacement.TryValidateTarget(player, pawn, new FVector(600, 0, 80)));

        var away = new FRotator(0, 32768, 0);
        Assert.True(TeamPingPlacement.TryProjectTarget(player, pawn, eye, away, out FVector fallback));
        Assert.InRange(fallback.X, -1201f, -1199f);
        Assert.True(TeamPingPlacement.TryValidateTarget(player, pawn, fallback));
        Assert.True(TeamPingPlacement.TryValidateTarget(player, pawn, eye + new FVector(-4200, 0, 0)));
        Assert.False(TeamPingPlacement.TryValidateTarget(player, pawn, eye + new FVector(-4500, 0, 0)));
        Assert.False(TeamPingPlacement.TryValidateTarget(player, pawn, new FVector(float.NaN, 0, 0)));
    }

    private static (World World, SPlayerController Player, SBasePawn Pawn) CreatePlayer(bool merc = false,
        CollisionDataBuilder? geometry = null)
    {
        var world = new World(1, ENetMode.NM_DedicatedServer);
        world.Level.SetService<ICollisionWorld>(CollisionWorld.Build((geometry ?? new CollisionDataBuilder()).Build()));
        var player = world.Level.Spawn<SPlayerController>()!;
        SBasePawn pawn = merc ? world.Level.Spawn<SPAWNDEFENSE>()! : world.Level.Spawn<SPawnAttaque>()!;
        pawn.Controller = player;
        pawn.EyeHeight = 80;
        pawn.Health = 100;
        player.Pawn = pawn;
        player.SBPawn = pawn;
        player.PlayerReplicationInfo = new PlayerReplicationInfo { Team = new TeamInfo { TeamIndex = merc ? 1 : 0 } };
        return (world, player, pawn);
    }
}
