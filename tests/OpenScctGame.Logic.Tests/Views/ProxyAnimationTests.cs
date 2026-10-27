using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests.Views;

/// <summary>
/// What the play client animates for the pawns it does not control (spec/net/proxy-pawns.md, spec/native/net-proxy.md
/// rules 5 to 9): SteeE offline with three server bots, as the launchers play it (the listen server and the loopback
/// client). Every bot proxy must get its walk set (InitializeBasicActions), run the clips of the actions the server
/// relays, move its walk channels while it moves and keep its crouched cylinder and mesh offset (zone configuration).
/// The local spy, who spawns crouched, must have its mesh offset too.
/// </summary>
public sealed class ProxyAnimationTests
{
    private const int Seconds = 30;

    private readonly ITestOutputHelper _output;

    public ProxyAnimationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed class ProxyTrack
    {
        public int Samples;
        public int MovingSamples;
        public int MovingWithWalkChannel;
        public int WalkFrameChanges;
        public int BaseFrameChanges;
        public int CrouchedSamples;
        public int CrouchedRight;
        public float LastWalkFrame = float.NaN;
        public float LastBaseFrame = float.NaN;
        public readonly HashSet<string> Clips = new(StringComparer.OrdinalIgnoreCase);
        public string LastBad = string.Empty;
    }

    [Fact]
    public void BotProxiesAnimateFromTheRelayedActions()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings { Map = "SteeE", HostMode = PlayHostMode.Inline, Bots = 3 }, clock);
        var tracks = new Dictionary<SBasePawn, ProxyTrack>(ReferenceEqualityComparer.Instance);
        var log = new StringBuilder();
        int localCrouchedSamples = 0;
        int localCrouchedRight = 0;
        for (int t = 1; t <= Seconds * Sim.Hz; t++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(t, Sim.Hz));
            session.Step(clock.NowMicroseconds);
            if (t < 5 * Sim.Hz || t % 6 != 0)
            {
                continue;
            }

            foreach (SBasePawn p in session.Client.World.Level.AllActors<SBasePawn>())
            {
                if (p.bDeleteMe || p.Health <= 0)
                {
                    continue;
                }

                if (p.Role == ENetRole.ROLE_AutonomousProxy)
                {
                    if (p.bIsCrouched && p.Physics == EPhysics.PHYS_Walking)
                    {
                        localCrouchedSamples++;
                        if (CrouchedPlacementRight(p))
                        {
                            localCrouchedRight++;
                        }
                    }

                    continue;
                }

                if (p.Role != ENetRole.ROLE_SimulatedProxy)
                {
                    continue;
                }

                if (!tracks.TryGetValue(p, out ProxyTrack? track))
                {
                    track = new ProxyTrack();
                    tracks[p] = track;
                }

                Sample(p, track);
                if (t % 300 == 0)
                {
                    log.AppendLine(Describe(t, p));
                }
            }
        }

        foreach ((SBasePawn p, ProxyTrack track) in tracks)
        {
            log.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{p.GetType().Name}#{p.ActorId}: samples={track.Samples} moving={track.MovingSamples} walkChannel={track.MovingWithWalkChannel} walkFrameChanges={track.WalkFrameChanges} baseFrameChanges={track.BaseFrameChanges} crouched={track.CrouchedSamples} crouchedRight={track.CrouchedRight} clips={string.Join(",", track.Clips)} {track.LastBad}"));
        }

        log.AppendLine(string.Create(CultureInfo.InvariantCulture, $"local crouched={localCrouchedSamples} right={localCrouchedRight}"));
        _output.WriteLine(log.ToString());

        Assert.Equal(3, tracks.Count);
        foreach ((SBasePawn p, ProxyTrack track) in tracks)
        {
            string who = $"{p.GetType().Name}#{p.ActorId}";
            Assert.False(p.ForwardAnim.IsNone, $"{who} has no walk set");
            Assert.True(track.BaseFrameChanges > track.Samples / 4, $"{who}: the base channel froze ({track.BaseFrameChanges} of {track.Samples})");
            if (track.MovingSamples > 20)
            {
                Assert.True(track.MovingWithWalkChannel > track.MovingSamples * 3 / 4,
                    $"{who}: moving without the walk set's clip ({track.MovingWithWalkChannel} of {track.MovingSamples}) {track.LastBad}");
                Assert.True(track.WalkFrameChanges > track.MovingSamples / 2,
                    $"{who}: the walk channels did not cycle ({track.WalkFrameChanges} of {track.MovingSamples})");
            }

            Assert.Equal(track.CrouchedSamples, track.CrouchedRight);
        }

        Assert.True(localCrouchedSamples > 0, "the local spy was never crouched on the floor");
        Assert.Equal(localCrouchedSamples, localCrouchedRight);
    }

    /// <summary>One look at a proxy: is its walk set the one of its stance, and do its channels move.</summary>
    private static void Sample(SBasePawn p, ProxyTrack track)
    {
        track.Samples++;
        OpenScct.Game.Movement.PawnAnimation a = p.PawnAnim;
        float baseFrame = a[0].Frame;
        if (!float.IsNaN(track.LastBaseFrame) && MathF.Abs(baseFrame - track.LastBaseFrame) > 0.01f)
        {
            track.BaseFrameChanges++;
        }

        track.LastBaseFrame = baseFrame;
        if (p.bIsCrouched && p.Physics == EPhysics.PHYS_Walking)
        {
            track.CrouchedSamples++;
            if (CrouchedPlacementRight(p))
            {
                track.CrouchedRight++;
            }
            else
            {
                track.LastBad = string.Create(CultureInfo.InvariantCulture,
                    $"crouched zc={p.ZoneConfig}/{p.ZoneConfigForClient} h={p.CollisionHeight} prePivot={p.PrePivot}");
            }
        }

        FVector v = p.Velocity;
        v.Z = 0f;
        if (p.Physics != EPhysics.PHYS_Walking || v.Size() < 80f)
        {
            return;
        }

        track.MovingSamples++;

        // Moving on the ground the blender drives the run (4) and forward (5) channels of the walk set.
        int walk = a[4].BlendAlpha >= a[5].BlendAlpha ? 4 : 5;
        Name expected = walk == 4 ? p.RunAnim : p.ForwardAnim;
        bool stanceSet = p.bIsCrouched == IsCrouchClip(p.ForwardAnim);
        if (a[walk].BlendAlpha > 0.3f && a[walk].Sequence == expected && !expected.IsNone && stanceSet)
        {
            track.MovingWithWalkChannel++;
            track.Clips.Add(expected.ToString());
        }
        else
        {
            track.LastBad = string.Create(CultureInfo.InvariantCulture,
                $"moving c4={a[4].Sequence}@{a[4].BlendAlpha:F2} c5={a[5].Sequence}@{a[5].BlendAlpha:F2} fwd={p.ForwardAnim} run={p.RunAnim} crouched={p.bIsCrouched}");
        }

        float frame = a[5].Frame;
        if (!float.IsNaN(track.LastWalkFrame) && MathF.Abs(frame - track.LastWalkFrame) > 0.01f)
        {
            track.WalkFrameChanges++;
        }

        track.LastWalkFrame = frame;
    }

    /// <summary>The crouched cylinder, zone configuration 1 and the mesh raised by the height change (feet on the floor).</summary>
    private static bool CrouchedPlacementRight(SBasePawn p)
    {
        SBasePawn d = p.GetUcClass().Create() as SBasePawn ?? throw new InvalidOperationException("no class default");
        float change = d.CollisionHeight - p.CrouchHeight;
        return p.ZoneConfig == 1 && MathF.Abs(p.CollisionHeight - p.CrouchHeight) < 0.01f &&
               MathF.Abs(p.PrePivot.Z - (d.PrePivot.Z + change)) < 0.01f;
    }

    /// <summary>The crouched clips of the original's walk sets (AWalkCr*, AJoggCr*: "Cr" for crouched).</summary>
    private static bool IsCrouchClip(Name clip) => clip.ToString().Contains("Cr", StringComparison.Ordinal);

    private static string Describe(int t, SBasePawn p)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"t={t} {p.GetType().Name}#{p.ActorId} rep={p.fRepTimeOfLastActionChange:F2}/{p.fOldTimeOfLastActionChange:F2} n={p.iNbActionForDistantClient} acts={p.AllActions[0]},{p.AllActions[1]} h={p.CollisionHeight} phys={p.Physics} v={p.Velocity.Size():F0} cr={p.bIsCrouched} zc={p.ZoneConfig}/{p.ZoneConfigForClient} sAction={p.sAction} fwd={p.ForwardAnim} run={p.RunAnim}");
        for (int c = 0; c < 16; c++)
        {
            OpenScct.Game.Movement.PawnAnimChannel ch = p.PawnAnim[c];
            if (!ch.Sequence.IsNone && (c == 0 || ch.BlendAlpha > 0f))
            {
                sb.Append(CultureInfo.InvariantCulture, $" | c{c} {ch.Sequence} a={ch.BlendAlpha:F2} f={ch.Frame:F2}");
            }
        }

        return sb.ToString();
    }
}
