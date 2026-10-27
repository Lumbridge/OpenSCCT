using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Maps;
using OpenScctGame.Match;
using OpenScctGame.Play;

namespace OpenScctGame.Smoke;

/// <summary>
/// The M0 client check. Loads TestBox through OpenScct.Content into the game scene (render world from
/// <c>render/world.glb</c>, library mesh instances, player starts), hosts a listen server with the local player
/// and three bots over loopback, and runs it for 600 client ticks. Every frame after joining, the local client
/// must see all four walkers and draw each one where its replicated actor is. At the end the server is stopped
/// and every server walker must be on the local client, close to where the server has it.
/// </summary>
public sealed class TestBoxSmoke : SmokeTest
{
    public const string TestName = "testbox";
    public const string MapId = "TestBox";
    public const int Bots = 3;
    public const int Walkers = Bots + 1;
    public const int MatchTicks = 600;

    /// <summary>Client ticks allowed for every player to join and see every walker.</summary>
    private const int JoinTickLimit = 180;

    private const int LoadFrameLimit = 30;

    /// <summary>
    /// Largest distance between a walker on the server when it stops and the newest state the local client got.
    /// The client is up to one flush (3 ticks at 20 Hz) plus scheduling jitter behind, and a walker covers 7.3 uu
    /// a tick at full speed; exact replication is checked by the integration tests on a virtual clock.
    /// </summary>
    private const float FinalTolerance = 100f;

    /// <summary>Metres; render and collision come from the same solids, so their bounds agree to float rounding.</summary>
    private const float BoundsTolerance = 1e-4f;

    private enum Phase
    {
        Start,
        Loading,
        Joining,
        Running,
    }

    private Phase _phase = Phase.Start;
    private GameRoot? _game;
    private int _loadFrames;
    private long _joinedTick = -1;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        switch (_phase)
        {
            case Phase.Start:
                if (UeSpaceChecks.Run() is { } spaceFailure)
                {
                    return SmokeResult.Fail("UeSpace: " + spaceFailure);
                }

                _game = GameRoot.Enter(context.Tree, new GameConfig { Map = MapId, Bots = Bots, Pilot = PilotMode.Scripted });
                _phase = Phase.Loading;
                return SmokeResult.Running;

            case Phase.Loading:
                if (_game!.LoadError is { } loadError)
                {
                    return SmokeResult.Fail("load: " + loadError);
                }

                if (!_game.IsLoaded)
                {
                    return ++_loadFrames < LoadFrameLimit ? SmokeResult.Running : SmokeResult.Fail("game scene did not load");
                }

                if (CheckMap(_game) is { } mapFailure)
                {
                    return SmokeResult.Fail("map: " + mapFailure);
                }

                _phase = Phase.Joining;
                return SmokeResult.Running;

            default:
                return StepMatch(_game!);
        }
    }

    private SmokeResult StepMatch(GameRoot game)
    {
        LocalMatch match = game.Match!;
        WalkerViews views = game.Walkers!;
        if (match.Server.Failure is { } failure)
        {
            return SmokeResult.Fail($"listen server stopped: {failure.GetType().Name}: {failure.Message}");
        }

        ClientSession local = match.LocalPlayer;
        if (local.State is EClientState.Rejected or EClientState.Disconnected)
        {
            return SmokeResult.Fail($"local client {local.State}: {local.RejectReason ?? local.Connection?.CloseReason}");
        }

        int seen = LiveWalkers(local).Count;
        if (_phase == Phase.Joining)
        {
            if (match.AllJoined && seen == Walkers && views.Count == Walkers)
            {
                _phase = Phase.Running;
                _joinedTick = local.LocalTick;
            }
            else if (local.LocalTick > JoinTickLimit)
            {
                return SmokeResult.Fail(string.Create(CultureInfo.InvariantCulture,
                    $"after {JoinTickLimit} ticks: all joined {match.AllJoined}, local client sees {seen} walkers, {views.Count} drawn"));
            }

            return SmokeResult.Running;
        }

        if (seen != Walkers || views.Count != Walkers)
        {
            return SmokeResult.Fail(string.Create(CultureInfo.InvariantCulture,
                $"tick {local.LocalTick}: local client sees {seen} walkers, {views.Count} drawn (expected {Walkers})"));
        }

        if (CheckViews(local, views) is { } viewFailure)
        {
            return SmokeResult.Fail(viewFailure);
        }

        return local.LocalTick < MatchTicks ? SmokeResult.Running : Finish(game, match);
    }

    private SmokeResult Finish(GameRoot game, LocalMatch match)
    {
        match.Stop();
        ServerHost host = match.Server.Host;
        ClientSession local = match.LocalPlayer;
        Dictionary<uint, ActorChannel> replicated = LiveWalkers(local);
        if (host.Players.Count != Walkers)
        {
            return SmokeResult.Fail($"server has {host.Players.Count} players, expected {Walkers}");
        }

        long serverTicks = host.World.TickCount;
        if (serverTicks < MatchTicks * 95 / 100)
        {
            return SmokeResult.Fail(string.Create(CultureInfo.InvariantCulture,
                $"server ran {serverTicks} ticks while the clients ran {local.LocalTick}"));
        }

        float maxError = 0f;
        long maxLag = 0;
        foreach (PlayerSlot slot in host.Players)
        {
            if (slot.Walker is not { } walker)
            {
                return SmokeResult.Fail($"player {slot.PlayerId} has no walker on the server");
            }

            if (!replicated.TryGetValue((uint)walker.ActorId, out ActorChannel? channel))
            {
                return SmokeResult.Fail($"walker of player {slot.PlayerId} ({slot.Name}) is not on the local client");
            }

            if (walker.MovesApplied < MatchTicks / 2)
            {
                return SmokeResult.Fail($"walker of player {slot.PlayerId} applied only {walker.MovesApplied} moves");
            }

            ProxyMotion motion = channel.Motion!;
            maxError = System.Math.Max(maxError, (walker.Location - motion.LatestLocation).Size());
            maxLag = System.Math.Max(maxLag, serverTicks - motion.NewestTick);
        }

        if (replicated.Count != Walkers)
        {
            return SmokeResult.Fail($"local client has {replicated.Count} walkers, the server {Walkers}");
        }

        if (maxError > FinalTolerance)
        {
            return SmokeResult.Fail(string.Create(CultureInfo.InvariantCulture,
                $"replicated walker {maxError:F1} uu from the server (limit {FinalTolerance} uu, lag {maxLag} ticks)"));
        }

        MapView view = game.View!;
        return SmokeResult.Pass(string.Create(CultureInfo.InvariantCulture,
            $"walkers={Walkers} joinedAt={_joinedTick} clientTicks={local.LocalTick} serverTicks={serverTicks} " +
            $"maxError={maxError:F2}uu lag={maxLag} render={view.Source} starts={view.PlayerStarts.Count} " +
            $"instances={view.Instances.Count}"));
    }

    /// <summary>The local client's live walkers with replicated state, by channel NetGUID (the server actor id).</summary>
    private static Dictionary<uint, ActorChannel> LiveWalkers(ClientSession session)
    {
        var walkers = new Dictionary<uint, ActorChannel>();
        foreach (ActorChannel channel in session.ActorChannels)
        {
            if (channel.Actor is CubeWalker { bDeleteMe: false } && channel.Motion is { HasState: true })
            {
                walkers[channel.NetGuid] = channel;
            }
        }

        return walkers;
    }

    private static string? CheckViews(ClientSession local, WalkerViews views)
    {
        foreach (ActorChannel channel in local.ActorChannels)
        {
            if (channel.Actor is not CubeWalker { bDeleteMe: false } walker || channel.Motion is not { HasState: true })
            {
                continue;
            }

            if (views.ViewOf(channel.NetGuid) is not { } view)
            {
                return $"walker {channel.NetGuid} is not drawn";
            }

            if ((view.Position - UeSpace.ToGodot(walker.Location)).Length() > 1e-5f)
            {
                return $"walker {channel.NetGuid} drawn at {view.Position}, its actor is at {UeSpace.ToGodot(walker.Location)}";
            }
        }

        return null;
    }

    private static string? CheckMap(GameRoot game)
    {
        SvMap map = game.Map!;
        MapView view = game.View!;
        if (!AsciiCaseComparer.AreEqual(map.Id, MapId) || map.SimHash != map.Manifest.SimHash)
        {
            return $"loaded '{map.Id}' with sim hash {map.SimHash}";
        }

        if (game.MapLoad!.FailedSpawns != 0)
        {
            return $"{game.MapLoad.FailedSpawns} actors failed to spawn";
        }

        if (view.Warnings.Count > 0)
        {
            return "view warnings: " + string.Join("; ", view.Warnings);
        }

        if (view.Source != MapRenderSource.RenderGlb || view.World?.Mesh is null)
        {
            return $"world not built from {MapBuilder.RenderFile} (source {view.Source})";
        }

        // The render world and the world collider come from the same solids: their bounds must agree, which also
        // proves the pipeline's glTF space and UeSpace are the same mapping.
        Aabb render = view.World.GlobalTransform * view.World.GetAabb();
        Aabb collision = MapBuilder.ColliderBounds(map.Collision, ColliderKind.WorldBsp);
        if ((render.Position - collision.Position).Length() > BoundsTolerance || (render.End - collision.End).Length() > BoundsTolerance)
        {
            return $"render world bounds {render} differ from the world collision bounds {collision}";
        }

        if (view.Root.FindChild(MapBuilder.LibraryNodeName, recursive: true, owned: false) is not null)
        {
            return "the render Library node is still in the scene";
        }

        // Every actor with a library StaticMesh is instanced where the actor is.
        int expectedInstances = 0;
        foreach (ActorRecord record in map.Gameplay.AllActors)
        {
            if (record.Props.GetAsset("StaticMesh") is not { } asset)
            {
                continue;
            }

            expectedInstances++;
            if (!view.Library.ContainsKey(asset.Path))
            {
                return $"library has no mesh {asset.Path}";
            }

            MeshInstance3D? instance = view.Instances.Find(i => i.Name == record.Id);
            if (instance is null || !instance.GlobalPosition.IsEqualApprox(UeSpace.ToGodot(record.Location)))
            {
                return $"{record.Id} is not instanced at {UeSpace.ToGodot(record.Location)}";
            }
        }

        if (expectedInstances == 0 || view.Instances.Count != expectedInstances)
        {
            return $"{view.Instances.Count} mesh instances, expected {expectedInstances}";
        }

        // Player starts: one marker each, at the start and facing its yaw.
        var starts = new List<ActorRecord>();
        foreach (ActorRecord record in map.Gameplay.AllActors)
        {
            if (AsciiCaseComparer.AreEqual(record.Class, "Engine.PlayerStart"))
            {
                starts.Add(record);
            }
        }

        if (starts.Count != Walkers || view.PlayerStarts.Count != starts.Count)
        {
            return $"{view.PlayerStarts.Count} player start markers for {starts.Count} player starts";
        }

        foreach (ActorRecord start in starts)
        {
            Marker3D? marker = view.PlayerStarts.Find(m => m.Name == start.Id);
            Vector3 facing = UeSpace.DirectionToGodot(start.Rotation.Vector());
            if (marker is null || !marker.GlobalPosition.IsEqualApprox(UeSpace.ToGodot(start.Location))
                || (marker.GlobalBasis.X - facing).Length() > 1e-4f)
            {
                return $"{start.Id} marker is not at {UeSpace.ToGodot(start.Location)} facing {facing}";
            }
        }

        return null;
    }
}

internal static class ListExtensions
{
    public static T? Find<T>(this IReadOnlyList<T> list, System.Predicate<T> match)
        where T : class
    {
        foreach (T item in list)
        {
            if (match(item))
            {
                return item;
            }
        }

        return null;
    }
}
