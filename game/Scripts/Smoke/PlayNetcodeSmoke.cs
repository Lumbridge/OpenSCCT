using System.Collections.Generic;
using OpenScct.Bots;
using OpenScct.Content;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>The real rendered scene over a lossy, jittery link. Captures a 10 fps image sequence when shots are enabled.</summary>
public class PlayNetcodeSmoke : PlayPoseSmoke
{
    public const string Ping100 = "play-net100", Ping200 = "play-net200";
    public const string Dedicated100 = "play-net-dedicated100", Dedicated200 = "play-net-dedicated200";
    public const string Stock100 = "play-net100-stock", Stock200 = "play-net200-stock";
    public const string StockDedicated100 = "play-net-dedicated100-stock", StockDedicated200 = "play-net-dedicated200-stock";
    private readonly string _name;
    private readonly SimLinkProfile _link;
    private readonly bool _dedicated;
    private readonly NetProfile _profile;
    private BotMatch? _networkMatch;
    private ClientSession? _renderClient;
    private long _snapshots;
    private long _nextLog;
    private SBasePawn? _follow;
    private bool _cameraSet;
    private FVector _cameraOffset = new(180, 100, 75);
    private int _renderedMotionSamples;
    private float _renderedMotionError;
    private readonly Dictionary<SBasePawn, (long Time, FVector Position, FVector Velocity)> _renderedMotion = new();
    private bool _backwardMotion;
    private ulong _motionSampleDeadline;
    private bool _motionTimelineStarted;
    private int _motionCandidates;

    public PlayNetcodeSmoke(bool highPing, bool dedicated = false, NetProfile profile = NetProfile.Enhanced)
    {
        _dedicated = dedicated; _profile = profile;
        _name = (dedicated ? highPing ? Dedicated200 : Dedicated100 : highPing ? Ping200 : Ping100) + PresetSuffix;
        _link = highPing ? SimLinkProfile.Ping200 : SimLinkProfile.Ping100;
    }

    public override string Name => _name;
    protected string PresetSuffix => _profile == NetProfile.Stock ? "-stock" : "";
    protected override int Bots => 3;
    protected override SimLinkProfile NetworkLink => _link;
    protected override bool ReadyForVerdict => _renderedMotionSamples >= 100 ||
        (_motionSampleDeadline != 0 && Time.GetTicksMsec() >= _motionSampleDeadline);
    protected override double ShotInterval => 0.1;
    protected BotMatch? NetworkMatch => _networkMatch;
    protected virtual string NetworkMap => "SteeE";
    protected virtual PlaySide RenderedSide => PlaySide.Spy;
    protected virtual IReadOnlyList<BotSpec> NetworkBots
    {
        get { var scripts = StoryScripts.Bots("spies"); return [scripts[1], scripts[2], scripts[3]]; }
    }

    protected override PlayRoot StartPlay(SmokeContext context)
    {
        if (!_dedicated) return PlayInspectSmoke.StartPlay(context, Bots, link: NetworkLink, profile: _profile);
        PlayConfig launcher = PlayConfig.FromOptions(context.App.Options);
        PlaySettings settings = launcher.Settings with
        {
            Map = NetworkMap, Side = RenderedSide, Mode = "Story", Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline, Bots = 0, NetworkProfile = _profile,
        };
        PlayRoot root = PlayRoot.Create(launcher with
        {
            Settings = settings, Render = true, Hud = true, TicksPerFrame = 0, Pilot = null,
            CreateSession = clock =>
            {
                MapCatalog serverMaps = MapCatalog.CreateDefault();
                var nav = new NavCache();
                nav.Prepare(serverMaps, settings.Map, terminalsBlockPlayers: false);
                var network = new SimLinkNetwork(clock, 1, _link);
                _networkMatch = new BotMatch(new BotMatchSettings
                {
                    Map = settings.Map, Seed = 1, GameOptions = "?GameMode=5?Editeur=true",
                    Bots = NetworkBots, Maps = serverMaps, Nav = nav,
                    Profile = _profile,
                    TerminalsBlockPlayers = false,
                }, network.Bind, "server", clock.NowMicroseconds);
                MapCatalog clientMaps = MapCatalog.CreateDefault();
                string folder = clientMaps.FindFolder(settings.Map) ?? throw new System.IO.FileNotFoundException(settings.Map);
                var loader = new PlayMapLoader(SvMap.Load(folder), "?GameMode=5?Editeur=true", PlayMapLoader.FindCharsDir(folder));
                loader.Register(clientMaps);
                _renderClient = new ClientSession(network.Bind("rendered-player"), "server", clock.NowMicroseconds,
                    new ClientSettings { PlayerName = "Player", LoginOptions = settings.Side == PlaySide.Merc ? "?Team=1" : "?Team=0", Maps = clientMaps,
                        Gameplay = true, InstallGameplayServices = false }, ownsTransport: true);
                return new PlaySession(settings, clock, _renderClient, clientMaps, folder, loader,
                    now => _networkMatch?.Step(now));
            },
        });
        root.TreeExiting += () =>
        {
            _networkMatch?.Dispose(); _networkMatch = null;
            _renderClient?.Dispose(); _renderClient = null;
        };
        context.Tree.Root.AddChild(root);
        return root;
    }
    protected override List<KeyStep> Timeline(SBasePawn local) =>
    [
        new("watch", 5),
        new("look around", 5) { LookPerSecond = new Vector2(120, 0) },
        new("walk", 2) { Hold = ["BT_Up"] },
        new("watch movement", 10),
        new("look back", 5) { LookPerSecond = new Vector2(-120, 0) },
    ];

    protected override void OnFrame(PlaySession session, string step)
    {
        // The number of rendered frames depends on the machine and concurrent smoke load. Keep observing the
        // running match until the required constant-motion sample count is met, with a bounded failure deadline.
        if (step.Length != 0) _motionTimelineStarted = true;
        else if (_motionTimelineStarted && _motionSampleDeadline == 0) _motionSampleDeadline = Time.GetTicksMsec() + 60_000;
        _snapshots = session.Client.PawnStates?.SnapshotsReceived ?? 0;
        foreach (SBasePawn pawn in session.Client.World.Level.DynamicActors<SBasePawn>())
            if (pawn is SPawnAttaque && !ReferenceEquals(pawn, session.LocalPawn) && !pawn.bDeleteMe) _follow = pawn;
        if (!_cameraSet)
        {
            _cameraSet = true;
            session.LocalController?.SwitchHeadset(1);
            session.ViewOverride = view =>
            {
                if (_follow is not { } target) return view;
                FVector focus = target.Location + new FVector(0, 0, 25);
                FVector camera = focus + _cameraOffset;
                if (session.Client.Map?.Physics?.Collision is { } collision)
                {
                    const ETraceFlags flags = ETraceFlags.TRACE_Level | ETraceFlags.TRACE_Movers | ETraceFlags.TRACE_LevelGeometry;
                    if (collision.LineCheck(out var hit, target, camera, focus, new FVector(4, 4, 4), flags))
                        camera = hit.Location + hit.Normal * 4;
                    if ((camera - focus).SizeSquared() < 140f * 140f)
                    {
                        float best = (camera - focus).SizeSquared();
                        for (int i = 0; i < 8; i++)
                        {
                            float angle = i * (System.MathF.PI / 4f);
                            FVector offset = new(200f * System.MathF.Cos(angle), 200f * System.MathF.Sin(angle), 75f);
                            FVector candidate = focus + offset;
                            if (collision.LineCheck(out hit, target, candidate, focus, new FVector(4, 4, 4), flags))
                                candidate = hit.Location + hit.Normal * 4;
                            float distance = (candidate - focus).SizeSquared();
                            if (distance <= best) continue;
                            best = distance; camera = candidate; _cameraOffset = offset;
                        }
                    }
                }
                return view with { Location = camera, Rotation = (focus - camera).Rotation(), Fov = 85f, Valid = true };
            };
        }
        ServerHost host = _networkMatch?.Server ?? session.Host;
        CheckRenderedMotion(session, host);
        if (host.World.TickCount < _nextLog) return;
        _nextLog = host.World.TickCount + 120;
        var source = host.PawnStates;
        var received = session.Client.PawnStates;
        GD.Print($"SVM_NET_POSE tick={host.World.TickCount} render={session.Client.RenderTimeFixed / NetTimeline.Fraction} " +
            $"sent={source.SnapshotsSent}/{source.AnimationRecords} received={_snapshots}/{received?.AnimationRecords} missing={received?.MissingDescriptors} deferrals={source.BudgetDeferrals}/{source.ReliableDeferrals} motion={_renderedMotionSamples}/{_motionCandidates}@{_renderedMotionError:F3}uu revisions={string.Join(',', source.RevisionChanges)}");
    }

    private void CheckRenderedMotion(PlaySession session, ServerHost host)
    {
        long time = session.Client.ViewTimeFixed, tick = time / NetTimeline.Fraction;
        if (tick < 4 || Root?.Pawns is not { } views) return;
        foreach (var view in views.Views)
        {
            if (view.Pawn is not { Role: ENetRole.ROLE_SimulatedProxy, NetworkPoseReady: true } copy || view.Model is not { } model ||
                !view.Visible || session.Client.Connection?.FindActorChannel(copy) is not { } channel ||
                host.World.Level.FindActor((int)channel.NetGuid) is not SBasePawn source ||
                !session.Client.PawnStates!.TryGetMotionInterval(copy, session.Client.PreviousRenderTimeFixed,
                    session.Client.RenderTimeFixed, out long firstTick, out long lastTick) || lastTick <= firstTick) continue;
            // The 2 uu motion requirement concerns constant-speed segments. Turns, steps and state boundaries
            // have different interpolation error; do not silently count those as straight-line reference samples.
            _motionCandidates++;
            // Fit the actual authoritative placements. Reported end-of-step velocity is used for extrapolation;
            // it need not equal displacement/dt on steps, moving bases, or an authored controller move.
            if (lastTick - firstTick < 2) firstTick = lastTick - 2;
            if (!host.PawnStates.TryGetRecordedMotion(source, firstTick, out ProxyState first, out FVector pivot, out EPhysics mode) ||
                mode != EPhysics.PHYS_Walking ||
                !host.PawnStates.TryGetRecordedMotion(source, lastTick, out ProxyState last, out FVector lastPivot, out mode) ||
                mode != EPhysics.PHYS_Walking || lastPivot != pivot) continue;
            FVector velocity = (last.Location - first.Location) * (1f / ((lastTick - firstTick) * Sim.DeltaTime));
            if (velocity.SizeSquared() < 60 * 60) continue;
            bool constant = true;
            // Classify the actual received interpolation interval, including both model tick endpoints.
            // Turns outside those samples do not invalidate a straight segment; turns inside it do.
            for (long sampleTick = firstTick + 1; sampleTick <= lastTick; sampleTick++)
            {
                if (!host.PawnStates.TryGetRecordedMotion(source, sampleTick, out ProxyState sample, out FVector nextPivot, out mode) ||
                    mode != EPhysics.PHYS_Walking || nextPivot != pivot ||
                    (sample.Location - first.Location - velocity * ((sampleTick - firstTick) * Sim.DeltaTime)).SizeSquared() > 0.01f)
                { constant = false; break; }
            }
            if (!constant) continue;
            float elapsed = (time - firstTick * NetTimeline.Fraction) * (Sim.DeltaTime / NetTimeline.Fraction);
            FVector expected = first.Location + velocity * elapsed + pivot;
            FVector shown = UeSpace.ToUe(model.Root.GlobalPosition);
            _renderedMotionError = System.Math.Max(_renderedMotionError, (shown - expected).Size());
            _renderedMotionSamples++;
            if (_renderedMotion.TryGetValue(copy, out var previous) && time > previous.Time &&
                time - previous.Time <= 2 * NetTimeline.Fraction &&
                (velocity - previous.Velocity).SizeSquared() < 0.01f &&
                ((shown - previous.Position) | velocity.SafeNormal()) < -0.01f) _backwardMotion = true;
            _renderedMotion[copy] = (time, shown, velocity);
        }
    }

    protected override SmokeResult Verdict(DrawnPoseTrack? local, IReadOnlyList<DrawnPoseTrack> others)
    {
        if (_renderedMotionSamples < 100 || _renderedMotionError > 2 || _backwardMotion)
            return SmokeResult.Fail($"rendered motion: {_renderedMotionSamples} samples, {_renderedMotionError:F3} uu error, backward={_backwardMotion}");
        if (local is null || local.MovingWindows == 0) return SmokeResult.Fail("local movement input never reached the pawn");
        if (_networkMatch is { } match)
        {
            foreach (BotClient bot in match.Bots)
                if (bot.Session.State != EClientState.Joined || bot.Controller is not { } controller ||
                    bot.Session.Movement.PeekEndpoint(controller)?.Sequenced is not { ResynchronizationRequests: 0 })
                    return SmokeResult.Fail($"{bot.Name}: disconnected or unexpectedly resynchronized");
        }
        int moving = 0;
        foreach (DrawnPoseTrack track in others)
        {
            if (track.MovingWindows < 4) continue;
            moving++;
            if (track.AnimatedWindows < track.MovingWindows * 3 / 4)
                return SmokeResult.Fail($"{track.Label}: animated {track.AnimatedWindows}/{track.MovingWindows} windows at {_link.LatencyMs} ms; {string.Join("; ", track.FrozenWindows)}");
        }
        return others.Count >= 3 && moving >= 2 && _snapshots > 100
            ? SmokeResult.Pass($"{others.Count} remote pawns, {moving} moving, {_snapshots} coherent snapshots at {_link.LatencyMs} ms; rendered motion {_renderedMotionError:F3} uu / {_renderedMotionSamples} samples")
            : SmokeResult.Fail($"remote={others.Count}, moving={moving}, snapshots={_snapshots}");
    }
}
