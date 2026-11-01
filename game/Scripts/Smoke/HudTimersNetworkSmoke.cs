using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Hud;

namespace OpenScctGame.Smoke;

/// <summary>Opt-in connected HUD clock check, including ordinary lethal damage, respawn and an actual time-limit outcome.</summary>
public sealed class HudTimersNetworkSmoke : SmokeTest
{
    public const string TestName = "hud-timers-network";
    public override string Name => TestName;
    private sealed class IdleBrain : BotBrain { public override BotInput Think(BotContext context) => default; }
    private readonly VirtualClock _clock = new();
    private readonly HudSnapshot _snapshot = new();
    private readonly HashSet<int> _roundSeconds = [];
    private BotMatch? _match;
    private long _frame;
    private SBasePawn? _deadBody;
    private int _respawnSamples;
    private bool _respawned;
    private float _maxRespawnError;
    private int _endedAt = -1;

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (_match is null)
            {
                MapCatalog maps = MapCatalog.CreateDefault();
                if (maps.FindFolder(MapCatalog.TestBox) is null) return SmokeResult.Fail("TestBox content is required.");
                var network = new SimLinkNetwork(_clock, 9517, SimLinkProfile.Get("200"));
                _match = new BotMatch(new BotMatchSettings
                {
                    Map = MapCatalog.TestBox, Maps = maps,
                    GameOptions = "?GameMode=5?TimeLimit=1?LivesATT=2?LivesDEF=2?TimesRespawnsATT=10",
                    Bots = [new("Spy", BotTeam.Spy, () => new IdleBrain()), new("Merc", BotTeam.Merc, () => new IdleBrain())],
                }, network.Bind, "server", 0);
                _match.Server.BeforeWorldTick += _ =>
                {
                    if (_deadBody is not null || _frame < 600) return;
                    if (_match.ServerController(_match.Bots[0]) is not { Pawn: SBasePawn body } pc) return;
                    _deadBody = body;
                    pc.PawnTakeDamage(1000, null, body.Location, default, SDamageType.SDTBase);
                };
            }

            for (int i = 0; i < 120; i++)
            {
                _clock.AdvanceTo(VirtualClock.TickToMicroseconds(++_frame, Sim.Hz));
                _match.Step(_clock.NowMicroseconds);
                if (_endedAt < 0 && _match.Server.Game is { bGameEnded: true, GameReplicationInfo: { } stopped })
                    _endedAt = stopped.ElapsedTime;
                foreach (BotClient bot in _match.Bots)
                {
                    if (bot.Controller is not { GameReplicationInfo: { bMatchHasBegun: true } gri } owner) continue;
                    _snapshot.Clear();
                    HudSimReader.Read(owner, _snapshot);
                    int remaining = Math.Max(0, 60 - gri.ElapsedTime);
                    string expected = string.Create(CultureInfo.InvariantCulture, $"{remaining / 60}:{remaining % 60:00}");
                    if (HudK.ChronoText(_snapshot.TimeLimit, _snapshot.ElapsedTime) != expected)
                        return Finish(SmokeResult.Fail($"Round HUD differs from its authority deadline at elapsed {gri.ElapsedTime}."));
                    _roundSeconds.Add(remaining);
                    if (_snapshot.Spectator.Active && !_snapshot.Spectator.GameEnded && _snapshot.Spectator.RemainingTime != expected)
                        return Finish(SmokeResult.Fail("Spectator round timer differs from the player timer."));
                    if (!ReferenceEquals(bot, _match.Bots[0])) continue;
                    if (_deadBody is not null && owner.Pawn is { Health: > 0 } && _match.ServerController(bot)?.Pawn != _deadBody)
                        _respawned = true;
                    if (_match.ServerController(bot) is not { } source ||
                        owner.StateNode is not SPlayerController.s_MultiSpectating_WhenKilled ||
                        source.StateNode is not SPlayerController.s_MultiSpectating_WhenKilled) continue;
                    float authorityLeft = Math.Max(0, source.fRespawnDate - source.XLevel.WorldTimeSeconds);
                    if (authorityLeft <= 0.1f) continue;
                    _respawnSamples++;
                    float error = Math.Abs(_snapshot.Spectator.RespawnIn - MathF.Ceiling(authorityLeft));
                    _maxRespawnError = Math.Max(_maxRespawnError, error);
                    if (_snapshot.Spectator.RespawnIn <= 0 || error > 1f)
                        return Finish(SmokeResult.Fail($"Respawn HUD {_snapshot.Spectator.RespawnIn}s, authority deadline {authorityLeft:F3}s."));
                }
                if (_match.Server.Game is { bGameEnded: true, GameReplicationInfo: { } ended } && _frame > 3900)
                {
                    if (ended.WinningReason != (byte)EEndReason.END_TIMEOUT || _endedAt != 60)
                        return Finish(SmokeResult.Fail("The round did not end at the actual time limit."));
                    if (!_respawned || _respawnSamples < 300 || !_roundSeconds.Contains(1) || !_roundSeconds.Contains(0))
                        return Finish(SmokeResult.Fail($"Coverage incomplete: respawn {_respawned}, samples {_respawnSamples}, round values {_roundSeconds.Count}."));
                    return Finish(SmokeResult.Pass($"Connected 200 ms link: {_roundSeconds.Count} round values, {_respawnSamples} respawn samples, max respawn display error {_maxRespawnError:F0}s; actual timeout at elapsed {_endedAt}."));
                }
                if (_frame > 4500) return Finish(SmokeResult.Fail("The one-minute match never completed."));
            }
            return SmokeResult.Running;
        }
        catch (Exception error) { return Finish(SmokeResult.Fail(error.ToString())); }
    }

    private SmokeResult Finish(SmokeResult result) { _match?.Dispose(); _match = null; return result; }
}
