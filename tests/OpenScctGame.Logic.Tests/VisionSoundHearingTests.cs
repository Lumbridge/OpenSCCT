using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>
/// Who hears a vision mode change (spec/equipment/vision-goggles.md rule 8, spec/native/audio.md rule 4): the goggles
/// live on the owner's machine only and play their sounds there, so no other player hears them. A bot's goggles
/// (the rewrite's bots play in the server world) are heard by nobody, as a remote client's goggles are not.
/// </summary>
public sealed class VisionSoundHearingTests
{
    private static readonly string[] VisionSounds =
    [
        "Armes_impacts.Gadgets.VisionRUN_1", "Armes_impacts.Gadgets.VisionSWIT_1", "Armes_impacts.Gadgets.JUMloop",
        "Armes_impacts.Gadgets.VisionNight_loop", "Armes_impacts.Gadgets.Emf_fond",
        "Armes_impacts.Gadgets.Hud_EMF_motion_In_Out", "Armes_impacts.Gadgets.Vision_movt_detect",
    ];

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
    public void ABotsGogglesMakeNoSound()
    {
        var world = new World(1, ENetMode.NM_ListenServer);
        SPlayerController bot = world.Level.Spawn<SPlayerController>(new FVector(10f, 20f, 30f))!;
        bot.Player = new AiViewport();
        world.Step();
        SGoggle goggle = world.Level.Spawn<SGoggle>(default, default, bot)!;
        world.Step();
        foreach (int mode in new[] { SGoggle.REN_MotionTrackingVision, SGoggle.REN_EMFVision, SGoggle.REN_NightVision })
        {
            goggle.CurrentMode = mode;
            goggle.Activate();
        }

        Assert.DoesNotContain(world.Events.Late.ToArray(), e => e.Kind is SimEventKind.PlaySound or SimEventKind.StopSound);
    }

    [Fact]
    public void TheLocalPlayersGogglesStillPlay()
    {
        var world = new World(1, ENetMode.NM_Client);
        SPlayerController pc = world.Level.Spawn<SPlayerController>(new FVector(10f, 20f, 30f))!;
        world.Level.Info.LocalPlayerController = pc;
        world.Step();
        SGoggle goggle = world.Level.Spawn<SGoggle>(default, default, pc)!;
        world.Step();
        goggle.CurrentMode = SGoggle.REN_EMFVision;
        goggle.Activate();
        Assert.Contains(world.Events.Late.ToArray(), e => e.Kind == SimEventKind.PlaySound && e.What.ToString() == "Armes_impacts.Gadgets.Emf_fond");
    }

    /// <summary>
    /// The host and the player's client (TestBox, three bots): the merc bots switch their vision modes over and over
    /// next to the spy player; none of their vision sounds reaches the player's machine.
    /// </summary>
    [Fact]
    public void TheClientNeverHearsTheMercBotsVisionModes()
    {
        var clock = new VirtualClock();
        PlaySession session;
        try
        {
            session = new PlaySession(new PlaySettings
            {
                Map = "TestBox",
                Side = PlaySide.Spy,
                Rules = PlayRules.PlayLevel,
                HostMode = PlayHostMode.Inline,
                Bots = 3,
            }, clock);
        }
        catch (FileNotFoundException e)
        {
            Assert.Skip("TestBox is not in this checkout: " + e.Message);
            return;
        }

        using (session)
        {
            var sound = new Recorder();
            PlayAudioRouter.Install(session.Client, sound);
            int switches = 0;
            bool playerSeen = false;
            for (int i = 0; i < 20 * Sim.Hz; i++)
            {
                clock.Advance(1_000_000 / Sim.Hz);
                session.Step(clock.NowMicroseconds);
                playerSeen |= session.LocalPawn is not null;
                if (session.LocalPawn is not null && i % Sim.Hz == 0)
                {
                    switches += session.ServerSnapshot(h =>
                    {
                        int n = 0;
                        foreach (AiController b in session.Bots?.Bots ?? [])
                        {
                            if (b.Pawn is SPAWNDEFENSE && b.Controller.Goggle is not null)
                            {
                                // Place the bot by the player so its calls are in reach of him.
                                b.Controller.SetLocation(session.LocalPawn.Location);
                                b.Controller.eventSwitchHeadset(3);
                                n++;
                            }
                        }

                        return n;
                    });
                }
            }

            Assert.True(playerSeen, "the player never got a pawn");
            Assert.NotEqual(OpenScct.Server.EClientState.Disconnected, session.Client.State);
            Assert.True(switches > 0, "no merc bot switched its vision mode");
            List<SimEvent> heard = sound.Events.Where(e => VisionSounds.Contains(e.What.ToString())).ToList();
            Assert.True(heard.Count == 0, "vision sounds reached the spy: " + string.Join(", ", heard.Select(e => e.What + " on " + e.ActorId)));
        }
    }
}
