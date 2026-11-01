using System;
using static OpenScctGame.Smoke.NetworkLabState;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Hub;
using OpenScctGame.Online;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Opt-in, file-controlled real game processes. No listener or control surface exists in normal play.</summary>
public sealed class NetworkLabSmoke(bool host, bool observer = false, bool secondObserver = false) : SmokeTest
{
    public const string HostName = "network-lab-host", GuestName = "network-lab-guest", ObserverName = "network-lab-observer";
    public const string Observer2Name = "network-lab-observer2";
    private readonly ConcurrentQueue<object> _serverSamples = new();
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);
    private string _directory = "", _role = "", _map = "TestBox";
    private bool _hub, _dedicated, _started, _launched, _stopping;
    private bool _loadingStalled;
    private int _loadDelay;
    private int _sampleIntervalMs = 100;
    private int _players = 2, _hostTeam, _observerTeam;
    private double _nextSample;
    private long _nextServerSample;
    private KeyPilot? _pilot;
    private PlayRoot? _root;
    private PlayInspector? _inspector;
    private object? _server;
    private float _capturedFoot = -PlayInspector.InspectFeetBelow;
    private int _poseCaptures;
    private string? _pendingInput;
    public override string Name => host ? HostName : secondObserver ? Observer2Name : observer ? ObserverName : GuestName;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public override SmokeResult Step(SmokeContext context)
    {
        if (_directory.Length == 0) Initialize();
        OnlineService online = OnlineService.Get(context.Tree);
        HubService hub = HubService.Get(context.Tree);
        if (!_started)
        {
            if (context.Tree.CurrentScene is not Boot) return SmokeResult.Running;
            using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "config.json")));
            int port = config.RootElement.GetProperty("port").GetInt32();
            if (host && !_dedicated)
            {
                int capacity = _players == 2 ? 2 : 4;
                LobbySetup setup = LobbySetup.ForMap(_map, EGameMode.GM_Multi, online.Catalog.Maps.GetMapSettings(_map), capacity)
                    with { TimeLimit = 0, Revenge = false, AutoBalance = false, Bots = BotFill.Off };
                if (config.RootElement.TryGetProperty("lives", out var lives) && lives.GetInt32() >= 0)
                    setup = setup with { SpyLives = lives.GetInt32(), MercLives = lives.GetInt32() };
                if (config.RootElement.TryGetProperty("respawnSeconds", out var respawn) && respawn.GetInt32() >= 1)
                    setup = setup with { RespawnSeconds = respawn.GetInt32() };
                bool ok = _hub ? hub.HostAndJoin(port, "LabHost", _hostTeam, loopbackOnly: true) : online.Host(new HostRequest
                {
                    Name = "Network lab", Port = port, MaxPlayers = capacity, Upnp = false, LoopbackOnly = true, LanPort = 0,
                    Setup = setup,
                });
                if (!ok) return SmokeResult.Fail(online.LastError ?? hub.LastError ?? "Host failed");
                if (_hub) hub.Host!.Ticked += s => RecordServer(s.Host, "hub");
                else online.Hosting!.Ticked += s => RecordServer(s.Host, s.Phase.ToString());
                Atomic(Path.Combine(_directory, "listening.json"), new { port, hub = _hub });
            }
            else
            {
                if (!File.Exists(Path.Combine(_directory, "listening.json"))) return SmokeResult.Running;
                int joinPort = !host && !observer && config.RootElement.TryGetProperty("joinPort", out var redirected) ? redirected.GetInt32() : port;
                string address = "127.0.0.1:" + joinPort;
                if (_hub) hub.Join(address, SettingsStore.Current.PlayerName, observer ? _observerTeam : 1 - _hostTeam);
                else online.Join(JoinTarget.FromAddress(address));
            }
            _started = true;
        }
        if (!_hub && online.Client is { State: OnlineClientState.Lobby } client)
        {
            int desiredTeam = host ? _hostTeam : observer ? _observerTeam : 1 - _hostTeam;
            if (client.Lobby.Local is { } local)
            {
                if (local.Team != desiredTeam) client.SetTeam(desiredTeam);
                else if ((!host || _dedicated) && !local.Ready) client.SetReady(true);
            }
            if (host && !_dedicated && !_launched && client.Lobby.Players.Count == _players &&
                client.Lobby.Local?.Team == _hostTeam && client.Lobby.Players.Where(p => p.Name != "LabHost").All(p => p.Ready))
            {
                client.Launch();
                _launched = true;
            }
        }
        if (!host && !observer && !_loadingStalled && (_hub ? hub.Client?.Session.Map is not null : online.Client?.State == OnlineClientState.Loading))
        {
            _loadingStalled = true;
            Atomic(Path.Combine(_directory, "guest-loading.json"), new { started = DateTimeOffset.UtcNow, milliseconds = _loadDelay });
            System.Threading.Thread.Sleep(_loadDelay);
        }
        if (_root is not null && !ReferenceEquals(context.Tree.CurrentScene, _root))
        {
            _root = null; _pilot = null; _inspector = null;
            if (_pendingInput is { } interrupted)
            {
                Ack(interrupted, "Input timeline interrupted by scene travel.");
                _pendingInput = null;
            }
        }
        if (context.Tree.CurrentScene is PlayRoot { IsLoaded: true } root && !ReferenceEquals(root, _root))
        {
            _root = root;
            _pilot = KeyPilot.Attach(root);
            if (DisplayServer.GetName() != "headless")
            {
                _inspector = new PlayInspector(root, InspectChecks.Animation | InspectChecks.Feet);
                _inspector.Attach();
            }
        }
        _pilot?.Update();
        if (_inspector is { } inspector)
        {
            inspector.Phase = _pilot?.Step ?? "idle";
            inspector.Frame();
            CaptureWorstPose(context, inspector);
        }
        if (_pendingInput is { } pending && _pilot is { Done: true })
        {
            Ack(pending, _pilot.Missing.Count > 0 ? "Unbound input: " + string.Join(", ", _pilot.Missing) : null);
            _pendingInput = null;
        }
        while (_serverSamples.TryDequeue(out object? sample)) _server = sample;
        if (context.ElapsedSeconds >= _nextSample)
        {
            _nextSample = context.ElapsedSeconds + _sampleIntervalMs / 1000.0;
            ClientSession? session = _hub ? hub.Client?.Session : online.Client?.Session;
            object data = new
            {
                role = _role, utc = DateTimeOffset.UtcNow, elapsed = context.ElapsedSeconds,
                phase = _hub ? hub.Status : online.Client?.State.ToString(), ready = session?.ReportedWaiting,
                results = online.Client?.Results,
                onlineError = online.LastError,
                lobby = online.Client?.State == OnlineClientState.Lobby ? online.Client.Lobby.Players.Select(p => new { p.Name, p.Team, p.Ready, p.Map }).ToArray() : null,
                loaded = _root?.IsLoaded ?? false, pilotReady = _pilot?.Ready ?? false,
                hudSleeping = _root?.Hud?.Effects.Sfx.Sleeping,
                hud = _root?.Hud is { } hud ? new
                {
                    page = hud.ActivePage?.GetType().Name, hud.Snapshot.Spectator.Active,
                    hud.Snapshot.Spectator.Watched, hud.Snapshot.Spectator.RespawnIn, hud.Snapshot.Spectator.NoRespawn,
                    hud.Snapshot.Spectator.Lives, hud.Snapshot.Spectator.TeamLives,
                    panel = hud.Spectator?.Panel?.Visible, hud.Snapshot.Valid, hud.Snapshot.HideHud,
                } : null,
                inputSent = _pilot?.Sent, inputDelivered = _pilot?.Delivered, inputPath = _pilot?.Notes,
                memoryBytes = GC.GetTotalMemory(false), allocatedBytes = GC.GetTotalAllocatedBytes(false),
                movement = Movement(session),
                localController = Controller(session?.LocalPlayer as SPlayerController),
                tick = session?.World.TickCount, renderTick = session?.RenderTimeFixed / 256.0,
                snapshotTick = session?.PawnStates?.LatestSnapshotTick, poseGaps = session?.PawnStates?.MissingDescriptors,
                stateGaps = session?.PawnStates?.StateGaps, underruns = session?.PawnStates?.Underflows,
                pawns = session is null ? [] : Pawns(session.World, session.Connection), server = _server,
                grenades = session is null ? [] : Grenades(session.World, session.Connection),
                inspectedPoses = _inspector?.PoseChecks.Select(p => new
                {
                    p.Label, p.FootSamples, p.MissingFloorSamples, p.CylinderSupportSamples, p.UnsupportedSamples, p.NonWalkingSamples, p.MissingFootSamples,
                    minFoot = float.IsFinite(p.MinFoot) ? (float?)p.MinFoot : null,
                    maxFoot = float.IsFinite(p.MaxFoot) ? (float?)p.MaxFoot : null, p.MovingWindows, p.AnimatedWindows,
                    p.MinimumFootPose, p.MaximumFootPose,
                    p.LandingTransitionSamples, p.MaximumLandingTransitionPose,
                    maxLandingTransitionFoot = float.IsFinite(p.MaxLandingTransitionFoot) ? (float?)p.MaxLandingTransitionFoot : null,
                }).ToArray(),
                poseIssues = _inspector?.Issues.Select(i => new { area = i.Area.ToString(), i.Subject, i.FirstStep, i.Count, i.Message, i.WorstMessage }).ToArray(),
                error = online.Client?.Error ?? hub.LastError ?? _root?.LoadError,
                controls = session?.LocalPlayer is SPlayerController pc ? new
                {
                    grabAllowed = pc.GameReplicationInfo?.bAvlb_SPY_Grab, use = pc.bUsing, oldUse = pc.oldbUsing,
                    jump = pc.bPressedJump, hooks = MovementService.HooksOf(pc).GetType().Name,
                    candidates = session.World.Level.Actors.OfType<SBasePawn>().Where(p => p is SPAWNDEFENSE).Select(p => new
                    { name = p.PlayerReplicationInfo?.PlayerName, reachable = pc.CanGrabPawn(p), grabbable = p.bCanBeGrabbed }).ToArray(),
                } : null,
            };
            Atomic(Path.Combine(_directory, _role + "-status.json"), data);
            File.AppendAllText(Path.Combine(_directory, _role + "-trace.jsonl"), JsonSerializer.Serialize(data, Json) + "\n");
            if (host && !_dedicated)
            {
                if (_hub) hub.Host?.Inspect(s => _serverSamples.Enqueue(Server(s.Host, "hub")));
                else online.Hosting?.Inspect(s => _serverSamples.Enqueue(Server(s.Host, s.Phase.ToString())));
            }
        }
        foreach (string file in Directory.EnumerateFiles(Path.Combine(_directory, _role), "*.json").Order(StringComparer.Ordinal))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            if (_done.Contains(id)) continue;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                JsonElement command = doc.RootElement;
                string op = command.GetProperty("op").GetString()!;
                if (_dedicated && host && op is "place" or "damage")
                {
                    // The separate server consumes these host-addressed fixture commands on its own tick.
                    if (File.Exists(Path.Combine(_directory, _role + "-acks", id + ".json"))) _done.Add(id);
                    continue;
                }
                if (op == "input" && (_pendingInput is not null || _pilot is not { Ready: true })) continue;
                _done.Add(id);
                switch (op)
                {
                    case "input":
                        var steps = JsonSerializer.Deserialize<LabKeyStep[]>(command.GetProperty("steps").GetRawText(), Json)!;
                        if (steps.Length == 0 || steps.Any(s => !double.IsFinite(s.Seconds) || s.Seconds <= 0 || s.Seconds > 60))
                            throw new ArgumentException("Input steps must last 0 to 60 seconds.");
                        _pilot!.Play(steps.Select(s => new KeyStep(s.Label, s.Seconds)
                        { Hold = s.Hold, Tap = s.Tap, LookPerSecond = new Vector2(s.LookX, s.LookY) }));
                        _pendingInput = id;
                        break;
                    case "stall":
                        int ms = command.GetProperty("milliseconds").GetInt32();
                        if (ms is < 0 or > 15000) throw new ArgumentException("Stalls must be between 0 and 15000 ms.");
                        System.Threading.Thread.Sleep(ms); // intentional main-thread loading/frame stall; host keeps ticking
                        Ack(id, null);
                        break;
                    case "capture":
                        string? shot = MenuPage.Shot(context.Tree.Root, _role + "-" + id);
                        Ack(id, shot is null ? "Capture requires a rendered window and SVM_PLAY_SHOTS." : null);
                        break;
                    case "place":
                        if (!host) throw new ArgumentException("Fixture placement belongs to the host.");
                        string player = command.GetProperty("player").GetString()!;
                        float[][] positions = command.TryGetProperty("positions", out var candidates)
                            ? JsonSerializer.Deserialize<float[][]>(candidates.GetRawText())!
                            : [JsonSerializer.Deserialize<float[]>(command.GetProperty("position").GetRawText())!];
                        if (positions.Length is < 1 or > 32 || positions.Any(xyz => xyz.Length != 3 || xyz.Any(v => !float.IsFinite(v))))
                            throw new ArgumentException("Expected 1 to 32 candidate positions with three finite coordinates each.");
                        int yaw = command.GetProperty("yaw").GetInt32();
                        bool requireSupport = command.TryGetProperty("requireSupport", out var supportValue) && supportValue.GetBoolean();
                        void Place(ServerHost server)
                        {
                            try
                            {
                                var pc = server.Players.Single(p => p.Name == player).Controller as SPlayerController
                                    ?? throw new InvalidOperationException("No player controller.");
                                var pawn = pc.Pawn as SBasePawn ?? throw new InvalidOperationException("No pawn.");
                                bool TryPlace(float[] xyz)
                                {
                                    var candidate = new FVector(xyz[0], xyz[1], xyz[2]);
                                    if (requireSupport)
                                    {
                                        var collision = server.Map.Physics?.Collision;
                                        var extent = new FVector(pawn.CollisionRadius, pawn.CollisionRadius, pawn.CollisionHeight);
                                        if (collision is null || !collision.LineCheck(out var floor, pawn,
                                            candidate - new FVector(0, 0, 8), candidate + new FVector(0, 0, 2),
                                            extent, PlayInspector.WorldTrace) || floor.Normal.Z < 0.7f) return false;
                                    }
                                    return pawn.SetLocation(candidate);
                                }
                                if (!positions.Any(TryPlace))
                                    throw new InvalidOperationException("Fixture position is blocked.");
                                pawn.Rotation = new FRotator(0, yaw, 0);
                                pc.SetViewRotation(pawn.Rotation);
                                pawn.Velocity = pawn.Acceleration = default;
                                pawn.ResetNetworkHistory();
                                server.Movement.PeekEndpoint(pc)?.Sequenced?.ResetLifetime();
                                Ack(id, null);
                            }
                            catch (Exception e) { Ack(id, e.Message); }
                        }
                        if (_hub) hub.Host!.Inspect(s => Place(s.Host));
                        else online.Hosting!.Inspect(s => Place(s.Host));
                        break;
                    case "self-damage":
                        // Controlled owner-to-server RPC fixture, used to retain an old request across respawn.
                        if (_hub || _root?.Session?.LocalController is not { Pawn: SBasePawn } self)
                            throw new InvalidOperationException("Self-damage requires a possessed regular-session player.");
                        int selfAmount = command.GetProperty("amount").GetInt32();
                        if (selfAmount is < 1 or > 1000) throw new ArgumentException("Damage must be 1 to 1000.");
                        self.PawnTakeDamage(selfAmount, null, self.Pawn.Location, default, SDamageType.Suicided);
                        Ack(id, null);
                        break;
                    case "damage":
                        if (!host || _hub) throw new ArgumentException("Damage fixtures require the regular-session host.");
                        string victimName = command.GetProperty("player").GetString()!;
                        int amount = command.GetProperty("amount").GetInt32();
                        bool requireAirborne = command.TryGetProperty("requireAirborne", out var airborne) && airborne.GetBoolean();
                        if (amount is < 1 or > 1000) throw new ArgumentException("Damage must be 1 to 1000.");
                        online.Hosting!.Inspect(s =>
                        {
                            try
                            {
                                var victim = s.Host.Players.Single(p => p.Name == victimName).Controller as SPlayerController;
                                if (victim?.Pawn is not { } body) throw new InvalidOperationException("No possessed damage target.");
                                if (requireAirborne && body.Physics != EPhysics.PHYS_Falling) throw new InvalidOperationException("Victim landed before airborne damage fixture.");
                                victim.PawnTakeDamage(amount, null, body.Location, default, SDamageType.SDTBase);
                                Ack(id, null);
                            }
                            catch (Exception e) { Ack(id, e.Message); }
                        });
                        break;
                    case "face":
                        // Explicit fixture setup, separate from input commands used to exercise gameplay.
                        if (_root?.Session?.LocalController is not { Pawn: SBasePawn body } owner)
                            throw new InvalidOperationException("No local pawn.");
                        var facing = new FRotator(command.TryGetProperty("pitch", out var pitch) ? pitch.GetInt32() : 0, command.GetProperty("yaw").GetInt32(), 0);
                        owner.SetViewRotation(facing);
                        body.Rotation = new FRotator(0, facing.Yaw, 0);
                        Ack(id, null);
                        break;
                    case "inspect-reset":
                        _inspector?.ResetPoseChecks();
                        _capturedFoot = -PlayInspector.InspectFeetBelow;
                        _poseCaptures = 0;
                        Ack(id, null);
                        break;
                    case "ready-next-round":
                        if (_hub || online.Client is not { State: OnlineClientState.Results } resultsClient)
                            throw new InvalidOperationException("The client is not at regular-session results.");
                        _launched = false;
                        _loadingStalled = false;
                        resultsClient.SetReady(true);
                        Ack(id, null);
                        break;
                    case "leave":
                        if (host) throw new InvalidOperationException("Use a guest for controlled leave/rejoin.");
                        if (_hub) hub.Leave(); else online.Leave();
                        Ack(id, null);
                        break;
                    case "rejoin":
                        if (host) throw new InvalidOperationException("Use a guest for controlled leave/rejoin.");
                        using (JsonDocument cfg = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "config.json"))))
                        {
                            int port = !observer && cfg.RootElement.TryGetProperty("joinPort", out var redirected)
                                ? redirected.GetInt32() : cfg.RootElement.GetProperty("port").GetInt32();
                            string address = "127.0.0.1:" + port;
                            _loadingStalled = false;
                            if (_hub) hub.Join(address, SettingsStore.Current.PlayerName, observer ? _observerTeam : 1 - _hostTeam);
                            else online.Join(JoinTarget.FromAddress(address));
                        }
                        Ack(id, null);
                        break;
                    case "stop":
                        _stopping = true;
                        Ack(id, null);
                        break;
                    default: throw new ArgumentException("Unknown lab operation: " + op);
                }
            }
            catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
            { _done.Add(id); Ack(id, e.Message); }
        }
        if (_stopping)
        {
            _pilot?.ReleaseAll();
            if (_hub) hub.Leave(false); else online.Leave();
            return SmokeResult.Pass("Controlled session closed; scenario assertions are in the lab runner report.");
        }
        return SmokeResult.Running;
    }

    private sealed record LabKeyStep(string Label, double Seconds)
    {
        public string[] Hold { get; init; } = [];
        public string[] Tap { get; init; } = [];
        public float LookX { get; init; }
        public float LookY { get; init; }
    }

    private void Initialize()
    {
        _directory = Path.GetFullPath(System.Environment.GetEnvironmentVariable("SVM_NETLAB_DIR")
            ?? throw new InvalidOperationException("Run tools/network-lab.ps1 first."));
        _role = host ? "host" : secondObserver ? "observer2" : observer ? "observer" : "guest";
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "config.json")));
        _map = config.RootElement.GetProperty("map").GetString()!;
        _hub = config.RootElement.GetProperty("hub").GetBoolean();
        _dedicated = config.RootElement.TryGetProperty("dedicated", out var dedicated) && dedicated.GetBoolean();
        _loadDelay = config.RootElement.TryGetProperty("loadDelayMs", out var delay) ? Math.Clamp(delay.GetInt32(), 0, 75000) : 0;
        _sampleIntervalMs = config.RootElement.TryGetProperty("sampleIntervalMs", out var interval) ? Math.Clamp(interval.GetInt32(), 100, 1000) : 100;
        _players = config.RootElement.TryGetProperty("players", out var players) ? players.GetInt32() : 2;
        _hostTeam = config.RootElement.TryGetProperty("hostTeam", out var side) ? side.GetInt32() : 0;
        _observerTeam = config.RootElement.TryGetProperty("observerTeam", out var observerSide) ? observerSide.GetInt32() : 0;
        if (secondObserver && config.RootElement.TryGetProperty("observer2Team", out var secondSide))
            _observerTeam = secondSide.GetInt32();
        SettingsStore.Current.PlayerName = host ? "LabHost" : secondObserver ? "LabObserver2" : observer ? "LabObserver" : "LabGuest";
        OnlineSettings.Current.MasterUrl = "";
        OnlineSettings.Current.HubListed = OnlineSettings.Current.HostListed = false;
        Directory.CreateDirectory(Path.Combine(_directory, _role));
        Directory.CreateDirectory(Path.Combine(_directory, _role + "-acks"));
    }

    private void Ack(string id, string? error) => Atomic(Path.Combine(_directory, _role + "-acks", id + ".json"),
        new { id, ok = error is null, error, utc = DateTimeOffset.UtcNow });

    private void RecordServer(ServerHost server, string phase)
    {
        long now = System.Environment.TickCount64;
        if (now < _nextServerSample) return;
        _nextServerSample = now + _sampleIntervalMs;
        var sample = new { utc = DateTimeOffset.UtcNow, server = Server(server, phase) };
        Atomic(Path.Combine(_directory, "server-status.json"), sample);
        File.AppendAllText(Path.Combine(_directory, "server-trace.jsonl"), JsonSerializer.Serialize(sample, Json) + "\n");
    }

    private static object? Movement(ClientSession? session)
    {
        if (session?.LocalPlayer is not { } pc || session.Movement.PeekEndpoint(pc) is not { } moves) return null;
        return new
        {
            corrections = moves.Client?.CorrectionsApplied, received = moves.Client?.AuthoritativeCorrectionsReceived,
            obsolete = moves.Client?.CorrectionsObsoletedByState, replayed = moves.Client?.MovesReplayed,
            lastAdjustment = moves.Client?.LastCorrectionDistance, epoch = moves.Sequenced?.CommandEpoch,
            nextSequence = moves.Sequenced?.NextSequence, acknowledged = moves.Sequenced?.LastAcknowledged,
            resets = moves.Sequenced?.HistoryResets, resyncs = moves.Sequenced?.ResynchronizationRequests,
            pending = moves.Sequenced?.DescribePending(), firstResynchronization = moves.Sequenced?.FirstResynchronization,
            inOld = session.Connection?.Stats.InPacketsOld, inLost = session.Connection?.Stats.InPacketsLost,
            outLost = session.Connection?.Stats.OutPacketsLost,
            rttMs = session.Connection?.Stats.RttMicros / 1000.0,
            jitterMs = session.Connection?.Stats.JitterMicros / 1000.0,
            sendRate = session.Connection?.CurrentNetSpeed, queuedBytes = session.Driver.QueuedBytes,
            bytesSent = session.Connection?.Stats.OutBytes, bytesReceived = session.Connection?.Stats.InBytes,
        };
    }

    private void CaptureWorstPose(SmokeContext context, PlayInspector inspector)
    {
        InspectPose? worst = inspector.PoseChecks.Where(p => p.MinFoot < _capturedFoot - 1f).MinBy(p => p.MinFoot);
        if (worst?.View.Model is not { } model || worst.View.Pawn is not { } pawn || _poseCaptures >= 8) return;
        _capturedFoot = worst.MinFoot;
        _poseCaptures++;
        string? shot = MenuPage.Shot(context.Tree.Root, $"{_role}-pose-{_poseCaptures}-{DateTime.UtcNow:HHmmssffff}");
        Skeleton3D skeleton = model.Skeleton;
        var feet = Enumerable.Range(0, skeleton.GetBoneCount()).Where(b =>
            skeleton.GetBoneName(b).Contains("Foot", StringComparison.OrdinalIgnoreCase) ||
            skeleton.GetBoneName(b).Contains("Toe", StringComparison.OrdinalIgnoreCase)).Select(b =>
            {
                Vector3 position = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(b).Origin * 100f;
                return new { bone = skeleton.GetBoneName(b), position = new[] { position.X, -position.Z, position.Y } };
            }).ToArray();
        File.AppendAllText(Path.Combine(_directory, _role + "-pose-evidence.jsonl"), JsonSerializer.Serialize(new
        {
            utc = DateTimeOffset.UtcNow, shot, minimum = worst.MinFoot, phase = inspector.Phase,
            pawn = Pawn(pawn, _root?.Session?.Client.Connection), feet,
        }, Json) + "\n");
    }


}
