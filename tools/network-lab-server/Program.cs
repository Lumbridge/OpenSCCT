using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScct.Server.Cli;
using OpenScct.Server.Online;
using static OpenScctGame.Smoke.NetworkLabState;

// Development-only host for tools/network-lab.ps1. Always loopback, never advertises or contacts a master.
if (args.Length != 2 || !int.TryParse(args[1], out int seconds) || seconds is < 30 or > 7500)
    throw new ArgumentException("Expected an existing lab directory and a 30..7500 second lifetime.");
string directory = Path.GetFullPath(args[0]);
using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "config.json")));
JsonElement cfg = config.RootElement;
string map = cfg.GetProperty("map").GetString()!;
int players = cfg.GetProperty("players").GetInt32(), capacity = players == 2 ? 2 : 4;
int interval = cfg.GetProperty("sampleIntervalMs").GetInt32();
ContentPaths.Configure(cfg.GetProperty("contentRoot").GetString(), AppContext.BaseDirectory);
var maps = MapCatalog.CreateDefault();
LobbySetup setup = LobbySetup.ForMap(map, EGameMode.GM_Multi, maps.GetMapSettings(map), capacity)
    with { TimeLimit = 0, Revenge = false, AutoBalance = false, Bots = BotFill.Off };
int lives = cfg.GetProperty("lives").GetInt32();
if (lives >= 0) setup = setup with { SpyLives = lives, MercLives = lives };
if (cfg.TryGetProperty("respawnSeconds", out var respawn) && respawn.GetInt32() >= 1)
    setup = setup with { RespawnSeconds = respawn.GetInt32() };
using var transport = LiteNetLibTransport.Listen(cfg.GetProperty("port").GetInt32(), capacity + 4, loopbackOnly: true);
var clock = new StopwatchClock();
using var server = new OnlineServer(transport, new OnlineServerSettings
{
    Name = "Network lab dedicated", Dedicated = true, Setup = setup, Maps = maps, MaxPlayers = capacity,
    MinPlayersToLaunch = players, LaunchCountdownSeconds = 1f, ResultsSeconds = 60f,
    LanPort = 0, LanLoopbackOnly = true, Master = null, RequireAccounts = false,
}, clock.NowMicroseconds, transport);
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
using var resolution = TimerResolution.Request();
foreach (string role in new[] { "server", "host" })
{
    Directory.CreateDirectory(Path.Combine(directory, role));
    Directory.CreateDirectory(Path.Combine(directory, role + "-acks"));
}
server.Logged += line => File.AppendAllText(Path.Combine(directory, "dedicated-events.log"), line + "\n");
Atomic(Path.Combine(directory, "listening.json"), new { port = transport.LocalPort, dedicated = true });
var done = new HashSet<string>(StringComparer.Ordinal);
NetDriver? tracedDriver = null;
long nextSample = 0;
server.RunRealtime(clock, stop.Token, online =>
{
    if (!ReferenceEquals(tracedDriver, online.Host.Driver))
    {
        tracedDriver = online.Host.Driver;
        var previous = tracedDriver.DeferReceivedFunction;
        tracedDriver.DeferReceivedFunction = (actor, function, arguments, connection, handler) =>
        {
            if (actor is not SPlayerController) return previous?.Invoke(actor, function, arguments, connection, handler) == true;
            NetFunctionHandler traced = (self, f, values, from) =>
            {
                var pc = (SPlayerController)self;
                string before = pc.GetStateName().ToString();
                handler(self, f, values, from);
                if (before != pc.GetStateName().ToString() || f.Name == "AntiBugTellServerIAmDead" ||
                    (f.Name == "PCChangeServerState" && values.Values[1].AsName == new Name("s_Dead")))
                    File.AppendAllText(Path.Combine(directory, "received-state-calls.jsonl"), JsonSerializer.Serialize(new
                    {
                        utc = DateTimeOffset.UtcNow, player = pc.PlayerReplicationInfo?.PlayerName,
                        function = f.Name, before, after = pc.GetStateName().ToString(), health = pc.Pawn?.Health, pawn = pc.Pawn?.ActorId,
                        requestedState = f.Name == "PCChangeServerState" ? values.Values[1].AsName.ToString() : null,
                    }) + "\n");
            };
            if (previous?.Invoke(actor, function, arguments, connection, traced) == true) return true;
            traced(actor, function, arguments, connection);
            return true;
        };
    }
    foreach (string role in new[] { "server", "host" })
    foreach (string file in Directory.EnumerateFiles(Path.Combine(directory, role), "*.json").Order(StringComparer.Ordinal))
    {
        if (done.Contains(file)) continue;
        string id = Path.GetFileNameWithoutExtension(file);
        using JsonDocument command = JsonDocument.Parse(File.ReadAllText(file));
        JsonElement cmd = command.RootElement;
        string? op = cmd.GetProperty("op").GetString();
        if (role == "host" && op is not ("place" or "damage")) { done.Add(file); continue; }
        done.Add(file);
        string? error = null;
        try
        {
            if (op == "stop") stop.Cancel();
            else if (op is "place" or "damage")
            {
                string name = cmd.GetProperty("player").GetString()!;
                var pc = online.Host.Players.Single(p => p.Name == name).Controller as SPlayerController
                    ?? throw new InvalidOperationException("No player controller.");
                var pawn = pc.Pawn as SBasePawn ?? throw new InvalidOperationException("No possessed pawn.");
                if (op == "damage")
                {
                    int amount = cmd.GetProperty("amount").GetInt32();
                    if (amount is < 1 or > 1000) throw new ArgumentException("Damage must be 1..1000.");
                    if (cmd.TryGetProperty("requireAirborne", out var airborne) && airborne.GetBoolean() && pawn.Physics != EPhysics.PHYS_Falling)
                        throw new InvalidOperationException("Victim landed before airborne damage fixture.");
                    pc.PawnTakeDamage(amount, null, pawn.Location, default, SDamageType.SDTBase);
                }
                else
                {
                    float[][] positions = cmd.TryGetProperty("positions", out var candidates)
                        ? JsonSerializer.Deserialize<float[][]>(candidates.GetRawText())!
                        : [JsonSerializer.Deserialize<float[]>(cmd.GetProperty("position").GetRawText())!];
                    if (positions.Length is < 1 or > 32 || positions.Any(xyz => xyz.Length != 3 || xyz.Any(v => !float.IsFinite(v))))
                        throw new ArgumentException("Expected 1..32 finite XYZ positions.");
                    if (!positions.Any(xyz => pawn.SetLocation(new FVector(xyz[0], xyz[1], xyz[2]))))
                        throw new InvalidOperationException("Fixture position is blocked.");
                    pawn.Rotation = new FRotator(0, cmd.GetProperty("yaw").GetInt32(), 0);
                    pc.SetViewRotation(pawn.Rotation);
                    pawn.Velocity = pawn.Acceleration = default;
                    pawn.ResetNetworkHistory();
                    online.Host.Movement.PeekEndpoint(pc)?.Sequenced?.ResetLifetime();
                }
            }
            else throw new ArgumentException("Unknown dedicated lab command.");
        }
        catch (Exception e) { error = e.Message; }
        Atomic(Path.Combine(directory, role + "-acks", id + ".json"), new { id, ok = error is null, error, utc = DateTimeOffset.UtcNow });
    }
    long now = Environment.TickCount64;
    if (now < nextSample) return;
    nextSample = now + interval;
    var sample = new
    {
        utc = DateTimeOffset.UtcNow, server = Server(online.Host, online.Phase.ToString()),
        lobby = online.Players.Select(p => new { p.Name, p.Team, p.Ready, p.Map, connected = p.Slot is not null }).ToArray(),
    };
    Atomic(Path.Combine(directory, "server-status.json"), sample);
    File.AppendAllText(Path.Combine(directory, "server-trace.jsonl"), JsonSerializer.Serialize(sample) + "\n");
});
server.Shutdown(clock);
