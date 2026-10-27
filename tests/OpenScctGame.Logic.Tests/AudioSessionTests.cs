using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>
/// What reaches the play scene's sound system in an offline match (SteeE, Play Level, three bots, the listen server
/// and the player over loopback): the player's own steps from the client's simulation, the server's sounds
/// (the bots' steps, the player's own steps once more) as ClientHearSound, the spawn sound as ClientPlaySound, and
/// the server's copy of the player's own steps dropped by the router. Needs the converted SteeE (local only).
/// </summary>
public sealed class AudioSessionTests
{
    private const string SpawnSound = "Interface.Menu.Menu_start_point";

    private sealed class Recorder : ISimEventSink
    {
        public readonly List<SimEvent> Events = new();

        public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events)
        {
            foreach (SimEvent e in events)
            {
                if (e.Kind == SimEventKind.PlaySound)
                {
                    Events.Add(e);
                }
            }
        }
    }

    [Fact]
    public void TheClientHearsItsStepsTheBotsAndItsSpawn()
    {
        var clock = new VirtualClock();
        PlaySession session;
        try
        {
            session = new PlaySession(new PlaySettings
            {
                Map = "SteeE",
                Side = PlaySide.Spy,
                Rules = PlayRules.PlayLevel,
                HostMode = PlayHostMode.Inline,
                Bots = 3,
            }, clock);
        }
        catch (FileNotFoundException e)
        {
            Assert.Skip("SteeE is not converted (local only): " + e.Message);
            return;
        }

        using (session)
        {
            var sound = new Recorder();
            PlayAudioRouter router = PlayAudioRouter.Install(session.Client, sound);
            long spawnedAt = -1;
            session.BeforeClientTick = (s, _) =>
            {
                // Forward from 2 s after the spawn for 5 s.
                bool walk = spawnedAt >= 0 && s.TicksRun > spawnedAt + (2 * Sim.Hz) && s.TicksRun < spawnedAt + (7 * Sim.Hz);
                s.Input.Set("BT_Up", walk);
            };

            for (int i = 0; i < 30 * Sim.Hz && (spawnedAt < 0 || session.TicksRun < spawnedAt + (9 * Sim.Hz)); i++)
            {
                clock.Advance(1_000_000 / Sim.Hz);
                session.Step(clock.NowMicroseconds);
                if (spawnedAt < 0 && session.LocalPawn is not null)
                {
                    spawnedAt = session.TicksRun;
                }
            }

            Assert.True(spawnedAt >= 0, "the player never got a pawn");
            int local = session.LocalPawn!.ActorId;
            Level level = session.Client.World.Level;
            long sent = session.ServerSnapshot(h => h.World.Level.GetService<NetHearSound>()?.Sent ?? 0);
            int ownSteps = sound.Events.Count(e => !e.Heard && e.ActorId == local && e.What.ToString().Contains(".FootSteps.", StringComparison.Ordinal));
            int heardOthers = sound.Events.Count(e => e.Heard && e.ActorId != local && level.FindActor(e.ActorId) is Pawn);
            bool spawn = sound.Events.Any(e => e.What.ToString() == SpawnSound);
            string report = $"sent={sent} own steps={ownSteps} heard from other pawns={heardOthers} spawn sound={spawn} " +
                            $"own={router.OwnPlayed} heard={router.HeardPlayed} duplicates={router.Duplicates}";
            Assert.True(sent > 0, "the server sent no ClientHearSound: " + report);
            Assert.True(ownSteps > 0, "no own footsteps: " + report);
            Assert.True(heardOthers > 0, "no sound of the bots reached the client: " + report);
            Assert.True(spawn, "the spawn sound (ClientPlaySound) did not play: " + report);
            Assert.True(router.Duplicates > 0, "the server's copies of the player's own steps were not dropped: " + report);
        }
    }
}
