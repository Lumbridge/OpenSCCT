using System;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>A timed bot grenade/mine kill through a local listen session, the real HUD receiver and rendered HUD.</summary>
public sealed class LocalExplosiveHudSmoke : SmokeTest
{
    public const string TestName = "local-explosive-hud";
    public override string Name => TestName;
    private static readonly Vector2I Size = new(1920, 1080);
    private VirtualClock _clock = new();
    private PlaySession? _session;
    private HudLayer? _hud;
    private SubViewport? _viewport;
    private SBasePawn? _victim;
    private NetConnection? _serverConnection;
    private long _frame;
    private int _case;
    private int _settle;
    private int _visibleSamples;
    private int _captures;
    private bool _armed;
    private bool _sawBlast;
    private string CaseName => _case == 0 ? "mine" : "grenade";

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (_session is null) Setup(context);
            if (_frame < 900)
            {
                Tick(60);
                return SmokeResult.Running;
            }
            if (!_armed) Arm();
            if (_frame < 990)
            {
                Tick(3);
                return SmokeResult.Running;
            }
            if (++_settle < 4) return SmokeResult.Running;
            CheckAndCapture();
            Cleanup();
            if (++_case < 2) return SmokeResult.Running;
            return SmokeResult.Pass($"Local timer-driven grenade and mine deaths: {_visibleSamples} visible HUD samples; {_captures} rendered captures; blast state seen={_sawBlast}.");
        }
        catch (Exception error)
        {
            Cleanup();
            return SmokeResult.Fail(error.ToString());
        }
    }

    private void Setup(SmokeContext context)
    {
        Require(MapCatalog.CreateDefault().FindFolder(MapCatalog.TestBox) is not null, "TestBox content is required.");
        _clock = new VirtualClock();
        _session = new PlaySession(new PlaySettings
        {
            Map = MapCatalog.TestBox, Side = PlaySide.Spy, Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline, Bots = 1, MatchSeed = 905,
        }, _clock);
        _session.Host.World.Step(12 * Sim.Hz);
        _session.Host.BeforeWorldTick += _ =>
        {
            foreach (var bot in _session.Bots!.Bots) bot.Held = true;
        };
        _viewport = new SubViewport { Size = Size, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_viewport);
        _viewport.AddChild(new ColorRect { Size = Size, Color = new Color(.045f, .065f, .075f) });
        _hud = HudLayer.Attach(_viewport, () => _session.LocalController, out string? error)
            ?? throw new InvalidOperationException(error ?? "HUD content is required.");
        _hud.ProcessMode = Node.ProcessModeEnum.Disabled;
        _hud.ScreenSize = Size;
        _hud.ShowFps = _hud.ShowNet = true;
        _hud.Fps.Settings.Graph = _hud.Net.Settings.Graph = "Line";
        _hud.Fps.Settings.Opacity = _hud.Net.Settings.Opacity = 50f;
        _hud.Fps.Settings.ShowFrameTime = _hud.Fps.Settings.ShowPercentiles = false;
        _hud.Fps.Settings.ShowAverage = true;
        _hud.Net.Settings.Detailed = _hud.Net.Settings.ShowTraffic = false;
        _hud.Net.Source = ReadNet;
        _hud.Connect(_session.Client);
        _session.Client.BeforeWorldTick += client => HudEventTap.TakeLate(client.World, _hud.Effects);
        HudEventTap.Ensure(_session.Client.World, _hud.Effects);
        _frame = 0;
        _settle = 0;
        _armed = false;
    }

    private NetReadout? ReadNet()
    {
        if (_session?.Client.Connection is not { } connection) return null;
        var stats = connection.Stats;
        var movement = _session.LocalController is { } owner ? _session.Client.Movement.PeekEndpoint(owner) : null;
        return new NetReadout(stats.HasRtt, stats.RttMicros, stats.JitterMicros, stats.InBytes, stats.OutBytes,
            stats.InPackets, stats.InPacketsLost, stats.OutPackets, stats.OutPacketsLost,
            movement?.Client?.CorrectionsApplied ?? 0, _session.Client.ServerNetRate, IsHost: connection.IsLocal,
            BufferMs: _session.Client.InterpolationDelayMs, BufferUnderflows: _session.Client.Presentation.Underflows,
            MovesReplayed: movement?.Client?.MovesReplayed ?? 0, CorrectionDistance: movement?.Client?.LastCorrectionDistance ?? 0,
            AnimationGaps: _session.Client.PawnStates?.MissingDescriptors ?? 0,
            PoseResets: _session.Client.PawnStates?.Resets ?? 0, PredictionResets: movement?.Sequenced?.HistoryResets ?? 0,
            StateGaps: _session.Client.PawnStates?.StateGaps ?? 0);
    }

    private void Tick(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _clock.AdvanceTo(VirtualClock.TickToMicroseconds(++_frame, Sim.Hz));
            _session!.Step(_clock.NowMicroseconds);
            _session.Input.EndTick();
            _hud!.Step(Sim.DeltaTime);
            if (!_armed) continue;
            Require(_session.LocalController is not null, $"Owner lost: server={_serverConnection?.State}:{_serverConnection?.CloseReason}; client={_session.Client.Connection?.CloseReason}; session={string.Join(';', _session.Log)}");
            _sawBlast |= _session.LocalController?.IsInState("s_Blast") == true;
            HudActivityEntry? death = _hud.Activity.Entries.FirstOrDefault(e => e.Kind == HudActivityKind.Kill);
            if (death is null || _hud.Snapshot.TimeSeconds - death.ReceivedAt < .2f) continue;
            Require(_hud.ActivityDrawer.VisibleCount > 0, CaseName + " death exists but HUD draws no activity row.");
            Require(_hud.Items.Any(item => item.Path == $"Activity/{death.Id}/Victim"), CaseName + " victim text was omitted from the composite.");
            _visibleSamples++;
        }
    }

    private void Arm()
    {
        SPlayerController owner = _session!.LocalController ?? throw new InvalidOperationException("No local controller.");
        _serverConnection = _session.Host.Driver.ClientConnections.Single();
        SPlayerController victim = _session.ServerController ?? throw new InvalidOperationException("No server controller.");
        _victim = victim.Pawn as SBasePawn ?? throw new InvalidOperationException("No victim pawn.");
        SBasePawn merc = _session.Bots!.Bots.Single().Controller.Pawn as SBasePawn ?? throw new InvalidOperationException("No merc pawn.");
        Require(owner.myHUD is SHud, "No replicated HUD.");
        Require(victim.Level.TimeSeconds - owner.Level.TimeSeconds > HudActivityFeed.Lifetime, "Server/client clocks must have a substantial offset.");
        var shud = (SHud)owner.myHUD!;
        HudMessages.AddMessageToStack(shud, "Warehouse: Intruder detected", 10, 2);
        HudMessages.AddMessageToStack(shud, "Area isolated", 10, 2);
        HudMessages.AddMessageToAuxiliaryStack(shud, "Thermal vision on", 10, 0);
        HudMessages.AddInfoMessageToStack(shud, "Warehouse", 10, 0);
        _victim.SetLocation(new FVector(700f, 300f, _victim.CollisionHeight + 2f));
        owner.Pawn!.SetLocation(_victim.Location);
        merc.SetLocation(new FVector(200f, 900f, 100f));
        _victim.Velocity = owner.Pawn.Velocity = merc.Velocity = FVector.Zero;
        _session.Host.Movement.PeekEndpoint(victim)?.Sequenced?.ResetLifetime();
        victim.fSpawnProtectionEnd = 0f;
        FVector at = _victim.Location - new FVector(100f, 0f, 0f);
        if (_case == 1)
        {
            SProjFrag explosive = _session.Host.World.Level.SpawnActor(new SProjFrag(), at, default, merc, merc)!;
            explosive.SetPhysics(EPhysics.PHYS_None);
            explosive.SetTimer(.2f, false);
        }
        else
        {
            SMineProx explosive = _session.Host.World.Level.SpawnActor(new SMineProx(), at, default, merc, merc)!;
            explosive.Explode = true;
            explosive.fTRef = victim.Level.TimeSeconds;
            explosive.TempsAvantExplosion = .2f;
        }
        _armed = true;
    }

    private void CheckAndCapture()
    {
        Require(_victim is { Health: <= 0 }, CaseName + " did not kill the victim.");
        HudActivityEntry death = _hud!.Activity.Entries.Single(e => e.Kind == HudActivityKind.Kill);
        Require(death.InvolvesLocalPlayer && death.Actor?.Name == "MercBot1", "Wrong death participants.");
        MagmaDrawItem text = _hud.Items.FirstOrDefault(i => i.Path == $"Activity/{death.Id}/Cause")
            ?? throw new InvalidOperationException("No explosion cause text in HUD composite.");
        Require(text.Text == "EXPLOSION", "Wrong cause text: " + text.Text);
        Require(_hud.Fps.DrawList.Items.Count > 0 && _hud.Net.DrawList.Items.Count > 0, "FPS and network readouts must be visible.");
        if (DisplayServer.GetName() == "headless") return;
        using Image image = _viewport!.GetTexture().GetImage();
        Require(!image.IsEmpty(), "Empty rendered HUD image.");
        int lit = 0;
        Rect2 probe = text.Rect.Grow(2f);
        for (int y = Math.Max(0, (int)probe.Position.Y); y < Math.Min(image.GetHeight(), (int)MathF.Ceiling(probe.End.Y)); y++)
        for (int x = Math.Max(0, (int)probe.Position.X); x < Math.Min(image.GetWidth(), (int)MathF.Ceiling(probe.End.X)); x++)
        {
            Color pixel = image.GetPixel(x, y);
            if (Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) > .5f) lit++;
        }
        Require(lit > 5, CaseName + " cause text has no visible rendered pixels.");
        string folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS")
            ?? Path.Combine(RenderSmokeArgs.RepoRoot, "local", "test-results", TestName);
        Directory.CreateDirectory(folder);
        Require(image.SavePng(Path.Combine(folder, CaseName + ".png")) == Error.Ok, "Could not save HUD capture.");
        _captures++;
    }

    private void Cleanup()
    {
        _hud?.Receiver.Disconnect();
        _session?.Dispose();
        _session = null;
        _viewport?.Free();
        _viewport = null;
        _hud = null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
