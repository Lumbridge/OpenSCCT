using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Voice;
using OpenScct.Server;
using OpenScct.Server.Hub;
using OpenScctGame.Chat;
using OpenScctGame.Match;
using OpenScctGame.Play;
using OpenScctGame.Smoke;
using OpenScctGame.UI;

namespace OpenScctGame.Hub;

/// <summary>
/// The hub in the game client (<c>--svm-smoke=hub</c>): a hub server on loopback UDP on its own thread and a headless
/// guest (a merc), then the player's path: the main menu's Hub button, the join page's Join, the play scene on the hub
/// map with the hub HUD, the guest's chat line in the chat box, the guest's name among the name tags, the guest's
/// voice (a tone) heard by the player at the proximity gain of the distance between them, a side change to merc (the
/// local pawn becomes a merc), and O back to the main menu.
/// </summary>
public sealed partial class HubSmoke : SmokeTest
{
    public const string TestName = "hub";

    private const double TimeoutSeconds = 120;

    private readonly StopwatchClock _clock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<HubNameTag> _tags = new();
    private readonly ConcurrentQueue<string> _adminCommands = new();
    private Thread? _serverThread;
    private Thread? _visitorThread;
    private HubServer? _server;
    private HubClient? _guest;
    private HubVoice? _guestVoice;
    private bool _guestTalking;
    private long _toneFed;
    private double _tonePhase;
    private long _guestStart;
    private long _guestTicks;
    private int _phase;
    private string _notes = string.Empty;
    private Exception? _serverFailure;
    private int _district;
    private double _travelAt;
    private bool _groundcoverChecked;
    private static readonly string[] TravelTour = ["range", "garden", "matches", "lounge", "easy", "medium", "hard", "plaza"];

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        if (RenderReport is null) TickGuest();
        if (_serverFailure is { } failure)
        {
            return Fail("hub server stopped: " + failure.Message);
        }

        // Fifty complete simulation worlds load serially on the benchmark worker;
        // keep its setup budget separate from the ordinary two-client smoke.
        if (context.ElapsedSeconds > (RenderReport is null ? TimeoutSeconds : 600))
        {
            return Fail($"timed out in phase {_phase}: scene={context.Tree.CurrentScene?.Name} status={HubService.Get(context.Tree).Status} error={HubService.Get(context.Tree).LastError}");
        }

        HubService service = HubService.Get(context.Tree);
        switch (_phase)
        {
            case 0:
                if (context.Tree.CurrentScene is not (Boot or MainMenu))
                {
                    return SmokeResult.Running;
                }

                StartServer();
                FrontEnd.ShowMainMenu(context.Tree);
                return Next();
            case 1:
                if (context.Tree.CurrentScene is not MainMenu menu || menu.FindChild("Hub", true, false) is not Button hub)
                {
                    return SmokeResult.Running;
                }

                hub.EmitSignal(BaseButton.SignalName.Pressed);
                return Next();
            case 2:
                if (context.Tree.CurrentScene is not HubScreen screen || screen.JoinButton is null)
                {
                    return SmokeResult.Running;
                }

                ((LineEdit)screen.FindChild("Address", true, false)).Text = "127.0.0.1:" + Port;
                ((LineEdit)screen.FindChild("PlayerName", true, false)).Text = "SmokeSpy";
                screen.JoinButton.EmitSignal(BaseButton.SignalName.Pressed);
                return Next();
            case 3:
                if (context.Tree.CurrentScene is not PlayRoot { IsLoaded: true, Session.LocalPawn: { } pawn } root ||
                    root.GetNodeOrNull<HubHud>("HubHud") is null || _guest?.LocalPlayer?.Pawn is null)
                {
                    return SmokeResult.Running;
                }

                if (!_groundcoverChecked && service.Client!.Play.Destinations.Count > 0)
                {
                    if (root.FindChild("OutdoorGroundcover", true, false) is not Node3D cover)
                        return Fail("expanded Hub has no groundcover");
                    var batches = cover.GetChildren().OfType<MultiMeshInstance3D>().ToArray();
                    if (batches.Sum(b => b.Multimesh.InstanceCount) < 240000 ||
                        batches.Any(b => b.Multimesh.CustomAabb.Size.X > 24 || b.VisibilityRangeEnd > 110))
                        return Fail("groundcover density or spatial culling bounds are incorrect");
                    _groundcoverChecked = true;
                    _notes += " foliage=" + batches.Sum(b => b.Multimesh.InstanceCount);
                }
                if (RenderReport is not null) return CaptureCrowd(context, service);

                if (Linger(context, "spy"))
                {
                    return SmokeResult.Running;
                }

                _notes += $" pawn={pawn.GetType().Name} side={(pawn.SPawnType == PawnType.SPAWNOFFENSE ? "spy" : "merc")}";
                _guest.Say("hello from the guest");
                _adminCommands.Enqueue("say Admin hub notice");
                _guestTalking = true;
                return Next();
            case 4:
                {
                    if (service.Client is not { } client || !client.Chat.Any(c => c.Text == "hello from the guest"))
                    {
                        return SmokeResult.Running;
                    }

                    var composer = (context.Tree.CurrentScene as PlayRoot)?.Menu?.Hud?.Chat;
                    if (composer is null || composer.ReceiveMatchMessages || !composer.AllowNearby ||
                        composer.Transcript.Entries.Count(c => c.Text == "hello from the guest") != 1)
                    {
                        return Fail("hub chat must display the guest relay exactly once through its Nearby-capable composer");
                    }

                    if (!client.Chat.Any(c => c.Text == "[admin] Admin hub notice")) return SmokeResult.Running;
                    if (composer.Transcript.Entries.Count(c => c.Text == "[admin] Admin hub notice") != 1 ||
                        composer.Transcript.Entries.Single(c => c.Text == "[admin] Admin hub notice").Channel != ChatChannel.System)
                    {
                        return Fail("admin hub notice must appear exactly once as a System chat entry");
                    }

                    client.NameTags(client.LocalPlayer!.Pawn!.Location, _tags);
                    if (!_tags.Exists(t => t.Name == "Guest" && t.Team == 1))
                    {
                        return SmokeResult.Running;
                    }

                    // The guest's voice: heard by the player for a second, at the gain of the distance between them.
                    if (service.VoiceChat is not { Voice: { } voice } chat || chat.Heard("Guest").Samples < VoiceK.SampleRate)
                    {
                        return SmokeResult.Running;
                    }

                    (double rms, _) = chat.Heard("Guest");
                    _guestTalking = false;
                    _notes += $" chat={client.Chat.Count} tags={_tags.Count}" +
                        System.FormattableString.Invariant($" voice=heard rms={rms:F3} gain={voice.MixOf("Guest").Gain:F2} relayed={_server?.VoiceRelay?.Stats.DownPackets}");
                    client.RequestSide(1);
                    return Next();
                }

            case 5:
                if (context.Tree.CurrentScene is not PlayRoot { Session.LocalPawn: { SPawnType: PawnType.SPAWNDEFENSE } merc } round)
                {
                    return SmokeResult.Running;
                }

                if (Linger(context, "merc"))
                {
                    return SmokeResult.Running;
                }

                _notes += $" switched={merc.GetType().Name} tagsShown={round.GetNodeOrNull<HubHud>("HubHud")?.TagsShown}";
                return Next();
            case 6:
                {
                    if (context.Tree.CurrentScene is not PlayRoot travelRoot || service.Client is not { } traveller) return SmokeResult.Running;
                    if (traveller.Play.Destinations.Count != 8) return Fail("expected eight travel destinations");
                    if (_district == 0 && CheckCheckpointPivots(travelRoot, traveller.Play) is { } pivotError) return Fail(pivotError);
                    if (_district == 0 && CheckBindings(travelRoot) is { } bindingError) return Fail(bindingError);
                    if (travelRoot.FindChild("HubTravelButton", true, false) is not Button travelButton) return Fail("missing Travel HUD button");
                    travelButton.EmitSignal(BaseButton.SignalName.Pressed);
                    return Next();
                }
            case 7:
                {
                    if (context.Tree.CurrentScene?.FindChild("Travel_" + TravelTour[_district], true, false) is not Button destination)
                        return Fail("travel menu did not open");
                    string? shots = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
                    if (_district == 0 && !string.IsNullOrEmpty(shots) && DisplayServer.GetName() != "headless")
                        context.Tree.Root.GetTexture().GetImage().SavePng(System.IO.Path.Combine(shots, "hub-travel-menu.png"));
                    destination.EmitSignal(BaseButton.SignalName.Pressed);
                    _travelAt = context.ElapsedSeconds;
                    return Next();
                }
            case 8:
                {
                    if (context.ElapsedSeconds - _travelAt < 2.2) return SmokeResult.Running;
                    var dest = _server!.Activities!.Layout.TeleportDestinations[TravelTour[_district]];
                    if (service.Client?.LocalPlayer?.Pawn is not { } visitor || (visitor.Location - dest.Station).Size() > 1000)
                        return Fail("travel did not arrive at " + dest.Title);
                    if (_lingerFrames == 0 && Math.Abs(FRotator.NormalizeAxis(service.Client.LocalPlayer.Rotation.Yaw - dest.Yaw)) > 128)
                        return Fail("travel did not apply the arrival facing for " + dest.Title);
                    if (Linger(context, TravelTour[_district])) return SmokeResult.Running;
                    _district++;
                    if (_district < TravelTour.Length) { _phase = 6; return SmokeResult.Running; }
                    _notes += " travel=8/8";
                    service.Leave();
                    return Next();
                }
            case 9:
                if (context.Tree.CurrentScene is not MainMenu || service.Client is not null)
                {
                    return SmokeResult.Running;
                }

                Stop();
                return SmokeResult.Pass(_notes.Trim());
            default:
                return Fail("unknown phase");
        }
    }

    private int _lingerFrames;

    private string? CheckCheckpointPivots(PlayRoot root, HubPlayState play)
    {
        if (root.FindChild("HubProps", true, false) is not HubProps props) return "missing activity props";
        var clock = play.Clock;
        int checkedCourses = 0;
        try
        {
            foreach (string course in new[] { "easy", "medium", "hard" })
            {
                var mark = play.Marks.FirstOrDefault(m => m.Kind == "checkpoint" && m.Id.StartsWith(course + "/", StringComparison.Ordinal));
                if (mark.Id is null) return "missing checkpoint for " + course;
                play.Read(HubPlayProtocol.RunStart(8, course));
                var expected = UeSpace.ToGodot(mark.At + new FVector(0, 0, 4));
                foreach (double phase in new[] { 0.0, Math.PI / 12, Math.PI / 4 })
                {
                    play.Clock = () => phase;
                    props._Process(0);
                    if (props.GetNodeOrNull<Node3D>("Mark_" + mark.Id.Replace('/', '_') + "/Ring") is not { } ring ||
                        ring.GlobalPosition.DistanceTo(expected) > .001f)
                        return course + " checkpoint moved during its highlight pulse";
                    var marker = (Node3D)ring.GetParent();
                    if (Math.Abs(marker.Scale.X - (1 + .08 * Math.Sin(phase * 6))) > .001)
                        return course + " checkpoint did not exercise its highlight animation";
                }
                checkedCourses++;
            }
        }
        finally
        {
            play.Clock = clock;
            play.Read(HubPlayProtocol.RunCancel("Checkpoint marker check complete"));
            props._Process(0);
        }
        _notes += " checkpoint-pivots=" + checkedCourses + "/3";
        return null;
    }

    /// <summary>
    /// With a window and SVM_PLAY_SHOTS, stays a few seconds in the hub and saves the window, turning the view so the
    /// plaza is seen from several sides. Without them it does nothing.
    /// </summary>
    private bool Linger(SmokeContext context, string label)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrEmpty(folder) || DisplayServer.GetName() == "headless")
        {
            return false;
        }

        _lingerFrames++;
        if (context.Tree.CurrentScene is PlayRoot { Session.LocalController: { } pc } && _lingerFrames % 60 == 0)
        {
            pc.SetViewRotation(new FRotator(pc.Rotation.Pitch, pc.Rotation.Yaw + 16384, pc.Rotation.Roll));
        }

        if (_lingerFrames % 60 == 45)
        {
            System.IO.Directory.CreateDirectory(folder);
            Image image = context.Tree.Root.GetTexture().GetImage();
            image.SavePng(System.IO.Path.Combine(folder, $"hub-{label}-{_lingerFrames / 60}.png"));
        }

        if (_lingerFrames < 300)
        {
            return true;
        }

        _lingerFrames = 0;
        return false;
    }

    private int Port { get; set; }

    private void StartServer()
    {
        var transport = LiteNetLibTransport.Listen(0, RenderReport is null ? 16 : 64, loopbackOnly: true);
        Port = transport.LocalPort;
        var maps = MapCatalog.CreateDefault();
        _server = new HubServer(transport, new HubSettings { Maps = maps, Name = "Smoke hub", MaxPlayers = RenderReport is null ? 8 : 50, Motd = "Welcome to the smoke hub." });
        _serverThread = new Thread(() =>
        {
            try
            {
                _server.RunRealtime(new StopwatchClock(), _stop.Token, server =>
                {
                    while (_adminCommands.TryDequeue(out string? command)) server.Execute(command);
                });
            }
            catch (Exception e)
            {
                _serverFailure = e;
            }
            finally
            {
                _server.Dispose();
                transport.Dispose();
            }
        })
        { IsBackground = true, Name = "hub smoke server" };
        _serverThread.Start();
        _guestStart = _clock.NowMicroseconds;
        _guest = new HubClient(LiteNetLibTransport.CreateClient(loopbackOnly: true), "127.0.0.1:" + Port, _guestStart,
            new HubClientSettings { Maps = maps, PlayerName = "Guest", Team = 1 }, ownsTransport: true);
        if (RenderReport is null) _guestVoice = new HubVoice(_guest, new VoiceClient(new VoiceSettings(), _clock));
        _toneFed = 0;
        if (RenderReport is not null)
        {
            _visitorThread = new Thread(() =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        while (_visitorCommands.TryDequeue(out var command)) command();
                        TickGuest();
                        Thread.Sleep(1);
                    }
                }
                catch (Exception e) { _serverFailure = e; }
            }) { IsBackground = true, Name = "hub benchmark visitors" };
            _visitorThread.Start();
        }
    }

    private void TickGuest()
    {
        if (_guest is null)
        {
            return;
        }

        long due = (_clock.NowMicroseconds - _guestStart) * Sim.Hz / 1_000_000;
        if (due - _guestTicks > 5)
        {
            _guestTicks = due - 5;
        }

        while (_guestTicks < due)
        {
            _guest.Tick(_guestStart + (_guestTicks + 1) * 1_000_000 / Sim.Hz);
            foreach (var visitor in _crowd) visitor.Tick(_guestStart + (_guestTicks + 1) * 1_000_000 / Sim.Hz);
            _guestTicks++;
        }

        // The guest's microphone: a 440 Hz tone while he talks, silence otherwise, at the wall clock's rate.
        if (_guestVoice is not { } voice)
        {
            return;
        }

        long samples = (_clock.NowMicroseconds - _guestStart) * VoiceK.SampleRate / 1_000_000;
        int count = (int)Math.Min(samples - _toneFed, VoiceK.SampleRate / 4);
        _toneFed = samples;
        if (count > 0)
        {
            var block = new float[count];
            for (int i = 0; i < count && _guestTalking; i++)
            {
                block[i] = 0.3f * (float)Math.Sin(_tonePhase);
                _tonePhase += 2 * Math.PI * 440 / VoiceK.SampleRate;
            }

            voice.Voice.SubmitCapture(block);
        }

        voice.Update(null, 1f / Sim.Hz);
    }

    private SmokeResult Next()
    {
        _phase++;
        GD.Print($"SVM_HUB phase {_phase}");
        return SmokeResult.Running;
    }

    private void Stop()
    {
        _stop.Cancel();
        _visitorThread?.Join(5000);
        _guestVoice?.Dispose();
        _guestVoice = null;
        _guest?.Dispose();
        _guest = null;
        foreach (var visitor in _crowd) visitor.Dispose();
        _crowd.Clear();
        _serverThread?.Join(5000);
    }

    private SmokeResult Fail(string reason)
    {
        Stop();
        return SmokeResult.Fail(reason);
    }
}
