using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests.Views;

/// <summary>
/// How fast the common clips play on the play client, against the original. The reference rates were read from the
/// running original in the project sandbox (a Play Level game, the animation channels of the local pawn and of its
/// weapon each frame, 60 frames a second): the merc's c0 WaitStSpFd2 advances 1.000 of the clip a second (a 1.0 s loop),
/// the crouched spy's waitCrAlFd0 0.889, the merc's first-person FirstWaitStFd2 1.000 standing and FirstWalkStFd2
/// 2.835 at 346.5 uu/s (play rate 2.079 = 0.006 x the speed, 11 frames at 15 fps). The movement goldens agree for c0
/// (WaitStSpFd2 moves 0.01665 of the clip per 1/60 s sample).
/// </summary>
public sealed class AnimTimingTests
{
    /// <summary>The original's c0 idle rates (clip fraction a second), read live.</summary>
    private static readonly Dictionary<string, float> OriginalIdleRates = new(StringComparer.Ordinal)
    {
        ["WaitStSpFd2"] = 1.000f,
        ["waitCrAlFd0"] = 0.8889f,
    };

    /// <summary>The merc's first-person walk loop at 346.5 uu/s in the original: play rate 2.079.</summary>
    private const float OriginalArmsWalkRateAt346 = 2.079f;

    private const float OriginalArmsWalkSpeed = 346.5f;

    private readonly ITestOutputHelper _output;

    public AnimTimingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(PlaySide.Merc, 0)]
    [InlineData(PlaySide.Spy, 0)]
    [InlineData(PlaySide.Spy, 3)]
    public void IdleLoopsTakeTheirOriginalTimeOnTheClient(PlaySide side, int bots)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings { Map = "SteeE", HostMode = PlayHostMode.Inline, Bots = bots, Side = side }, clock);
        var client = new LoopMeter();
        var proxies = new Dictionary<SBasePawn, LoopMeter>();
        for (int t = 1; t <= 12 * Sim.Hz; t++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(t, Sim.Hz));
            session.Step(clock.NowMicroseconds);
            if (t < 4 * Sim.Hz)
            {
                continue;
            }

            client.Sample(session.LocalPawn as SBasePawn);
            foreach (SBasePawn p in session.Client.World.Level.AllActors<SBasePawn>())
            {
                if (p.Role == OpenScct.Core.Engine.ENetRole.ROLE_SimulatedProxy)
                {
                    if (!proxies.TryGetValue(p, out LoopMeter? m))
                    {
                        proxies[p] = m = new LoopMeter();
                    }

                    m.Sample(p);
                }
            }
        }

        _output.WriteLine("local pawn: " + client.Report());
        foreach ((SBasePawn p, LoopMeter m) in proxies)
        {
            _output.WriteLine($"proxy {p.PlayerReplicationInfo?.PlayerName}: " + m.Report());
        }

        int compared = 0;
        foreach ((string clip, float measured, int ticks) in client.Rates().Concat(proxies.Values.SelectMany(m => m.Rates())))
        {
            if (!OriginalIdleRates.TryGetValue(clip, out float original))
            {
                continue;
            }

            compared++;
            Assert.True(MathF.Abs(measured - original) <= original * 0.02f,
                string.Create(CultureInfo.InvariantCulture, $"{clip}: the client plays {measured:F3} of the clip a second over {ticks} ticks, the original {original:F3}"));
        }

        Assert.True(compared > 0, "no idle loop of the original's table was played long enough to measure");
    }

    /// <summary>
    /// The merc's first-person arms: the idle loop at play rate 1 and the walk loop at 0.006 x the speed, clamped to
    /// 3 (SWeaponDefense.AnimateWaiting), so walking at the merc's 346.5 uu/s plays FirstWalkStFd2 at 2.079 as the
    /// original does (before: at 1, so the arms bobbed at half the original's pace).
    /// </summary>
    [Fact]
    public void MercArmsLoopAtTheOriginalRates()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings { Map = "SteeE", HostMode = PlayHostMode.Inline, Bots = 0, Side = PlaySide.Merc }, clock);
        var idle = new List<float>();
        var walk = new List<(float Speed, float Rate)>();
        for (int t = 1; t <= 12 * Sim.Hz; t++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(t, Sim.Hz));
            session.Input.Set("BT_Up", t > 6 * Sim.Hz && t <= 9 * Sim.Hz);
            session.Step(clock.NowMicroseconds);
            if (t < 4 * Sim.Hz || session.LocalPawn is not SPAWNDEFENSE { MyWeapon: SWeaponDefense rifle } merc || !rifle.IsInState("s_Selected"))
            {
                continue;
            }

            Name clip = rifle.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL);
            float rate = rifle.ViewAnim.Rate(SWeaponDefense.MVTCHANNEL);
            if (clip == ViewModelClips.FirstWaitStFd2)
            {
                idle.Add(rate);
            }
            else if (clip == ViewModelClips.FirstWalkStFd2 && merc.Velocity.Size() > 300f)
            {
                walk.Add((merc.Velocity.Size(), rate));
            }
        }

        Assert.True(idle.Count > Sim.Hz, $"the arms played the idle loop in {idle.Count} ticks");
        Assert.All(idle, r => Assert.Equal(1f, r));
        Assert.True(walk.Count > Sim.Hz / 2, $"the merc walked with the walk loop in {walk.Count} ticks");
        foreach ((float speed, float rate) in walk)
        {
            float original = OriginalArmsWalkRateAt346 * speed / OriginalArmsWalkSpeed;
            Assert.True(MathF.Abs(rate - original) <= 0.01f,
                string.Create(CultureInfo.InvariantCulture, $"walking at {speed:F1} uu/s the arms loop at rate {rate:F3}; the original at {original:F3}"));
        }

        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"idle ticks {idle.Count} at rate 1; walk ticks {walk.Count}, rate {walk.Average(w => w.Rate):F3} at {walk.Average(w => w.Speed):F1} uu/s"));
    }

    /// <summary>
    /// A standing bot seen on a client: its proxy turns as its pawn does on the server, not further, and its
    /// turn-in-place clips (the TurnAnim on channels 2 and 3) play no faster. In the original a Rotation pushed as a
    /// property between samples (net-proxy.md rule 3) does not turn the proxy: ASBasePawn::PostNetReceive (editor
    /// 0x1120be70) puts the old rotation back and makes the received one the proxy's ReceivedRotation, which the proxy
    /// tick turns toward (rule 15). Before, the property turned our proxy at once and the next tick turned it back
    /// toward the last sample's yaw: MercBot1's proxy travelled 1.90 times its pawn's yaw (357468 against 188575 yaw
    /// units standing in 40 s) and the turn clips ran fast, the shuffling idle Ryan saw.
    /// </summary>
    [Fact]
    public void StandingProxiesTurnNoFurtherThanTheirPawns()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings { Map = "SteeE", HostMode = PlayHostMode.Inline, Bots = 3, Side = PlaySide.Spy }, clock);
        var last = new Dictionary<string, int>(StringComparer.Ordinal);
        var travel = new Dictionary<string, double>(StringComparer.Ordinal);
        var turnClip = new Dictionary<string, (double Sum, int Ticks)>(StringComparer.Ordinal);
        for (int t = 1; t <= 40 * Sim.Hz; t++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(t, Sim.Hz));
            session.Step(clock.NowMicroseconds);
            Measure(session.Host.World.Level, "server ", last, travel, turnClip);
            Measure(session.Client.World.Level, "client ", last, travel, turnClip);
        }

        int compared = 0;
        foreach ((string key, double server) in travel.Where(kv => kv.Key.StartsWith("server ", StringComparison.Ordinal)))
        {
            string name = key["server ".Length..];
            double client = travel.GetValueOrDefault("client " + name);
            (double serverClip, int serverTicks) = turnClip.GetValueOrDefault(key);
            (double clientClip, int clientTicks) = turnClip.GetValueOrDefault("client " + name);
            _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{name}: standing yaw travel server {server:F0}, client proxy {client:F0} ({client / Math.Max(1.0, server):F2} x); " +
                $"turn clips played server {serverClip:F1}, client {clientClip:F1} clip lengths ({serverTicks} and {clientTicks} ticks)"));
            if (server < 20000)
            {
                continue;
            }

            compared++;
            Assert.True(client <= server * 1.25,
                string.Create(CultureInfo.InvariantCulture, $"{name}'s proxy turned {client:F0} yaw units while standing, its pawn {server:F0}"));
            Assert.True(client >= server * 0.2,
                string.Create(CultureInfo.InvariantCulture, $"{name}'s proxy hardly turned: {client:F0} yaw units while standing, its pawn {server:F0}"));
            Assert.True(clientClip <= serverClip * 1.25 + 1.0,
                string.Create(CultureInfo.InvariantCulture, $"{name}'s proxy played {clientClip:F1} turn clip lengths standing, its pawn {serverClip:F1}"));
        }

        Assert.True(compared > 0, "no bot turned while standing");
    }

    /// <summary>
    /// Adds each standing bot's yaw change since the last tick to <paramref name="travel"/>, and how far its turn
    /// clips (channels 2 and 3 at a blend alpha above 0) advanced to <paramref name="turnClip"/>.
    /// </summary>
    private static void Measure(Level level, string side, Dictionary<string, int> last, Dictionary<string, double> travel,
        Dictionary<string, (double Sum, int Ticks)> turnClip)
    {
        foreach (SBasePawn p in level.AllActors<SBasePawn>())
        {
            if (p.PlayerReplicationInfo?.PlayerName is not { } name || !name.Contains("Bot", StringComparison.Ordinal) || p.Health <= 0)
            {
                continue;
            }

            string key = side + name;
            bool standing = p.Velocity.Size2D() <= 1f;
            if (last.TryGetValue(key, out int previous) && standing)
            {
                travel[key] = travel.GetValueOrDefault(key) + Math.Abs((short)(p.Rotation.Yaw - previous));
                double played = 0;
                for (int c = 2; c <= 3; c++)
                {
                    PawnAnimChannel ch = p.PawnAnim[c];
                    if (ch.BlendAlpha > 0f && ch.Sequence == p.TurnAnim)
                    {
                        played += Math.Abs(ch.Rate) * Sim.DeltaTime * ch.BlendAlpha;
                    }
                }

                (double sum, int ticks) = turnClip.GetValueOrDefault(key);
                turnClip[key] = (sum + played, ticks + (played > 0 ? 1 : 0));
            }

            last[key] = p.Rotation.Yaw;
        }
    }

    /// <summary>Channel 0's frame advance per second while a looping clip plays.</summary>
    private sealed class LoopMeter
    {
        private readonly Dictionary<string, (double Advance, int Ticks)> _by = new(StringComparer.Ordinal);
        private Name _last;
        private float _lastFrame;

        public void Sample(SBasePawn? pawn)
        {
            if (pawn is null)
            {
                return;
            }

            PawnAnimChannel c0 = pawn.PawnAnim[0];
            if (c0.Sequence.IsNone || c0.Clip is null || !c0.bLoop || c0.Frame < 0f)
            {
                _last = default;
                return;
            }

            if (c0.Sequence == _last)
            {
                float d = c0.Frame - _lastFrame;
                if (d < -0.5f)
                {
                    d += 1f;
                }

                string key = c0.Sequence.ToString();
                _by.TryGetValue(key, out var e);
                _by[key] = (e.Advance + d, e.Ticks + 1);
            }

            _last = c0.Sequence;
            _lastFrame = c0.Frame;
        }

        public IEnumerable<(string Clip, float Measured, int Ticks)> Rates() =>
            _by.Where(kv => kv.Value.Ticks >= Sim.Hz)
                .Select(kv => (kv.Key, (float)(kv.Value.Advance / (kv.Value.Ticks * Sim.DeltaTime)), kv.Value.Ticks));

        public string Report() => string.Join("; ", Rates().Select(r =>
            string.Create(CultureInfo.InvariantCulture, $"{r.Clip} {r.Measured:F3}/s ({r.Ticks} ticks)")));
    }
}
