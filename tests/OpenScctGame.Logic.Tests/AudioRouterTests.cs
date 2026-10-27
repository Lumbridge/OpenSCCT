using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScctGame.Play;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>The play scene's sound router: one call heard twice (this machine's and the server's copy) plays once.</summary>
public sealed class AudioRouterTests
{
    private sealed class Recorder : ISimEventSink
    {
        public readonly List<(long Tick, SimEvent Event)> Events = new();

        public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events)
        {
            foreach (SimEvent e in events)
            {
                Events.Add((tick, e));
            }
        }
    }

    private static SimEvent Sound(int actor, string sound, bool heard) =>
        new() { Kind = SimEventKind.PlaySound, ActorId = actor, What = new Name(sound), Volume = 1f, Heard = heard };

    private static readonly long Window = (long)Math.Ceiling(PlayAudioRouter.DuplicateSeconds * Sim.Hz);

    [Fact]
    public void TheServerCopyOfAnOwnCallIsDropped()
    {
        var sound = new Recorder();
        var router = new PlayAudioRouter(sound);
        router.OnSimEvents(10, [Sound(5, "Sound_Persos.FootSteps.SW_FootRun", heard: false)]);
        router.OnSimEvents(12, [Sound(5, "Sound_Persos.FootSteps.SW_FootRun", heard: true)]);
        Assert.Single(sound.Events);
        Assert.Equal(1, router.Duplicates);
        Assert.Equal(1, router.OwnPlayed);
        Assert.Equal(0, router.HeardPlayed);
    }

    [Fact]
    public void AnOwnCopyAfterTheServerCopyIsDropped()
    {
        var sound = new Recorder();
        var router = new PlayAudioRouter(sound);
        router.OnSimEvents(10, [Sound(7, "Sound_Persos.FootSteps.SW_FootRun", heard: true)]);
        router.OnSimEvents(10 + Window, [Sound(7, "Sound_Persos.FootSteps.SW_FootRun", heard: false)]);
        Assert.Single(sound.Events);
        Assert.True(sound.Events[0].Event.Heard);
    }

    [Fact]
    public void RepeatedCallsOfOneSidePlayEachTime()
    {
        var sound = new Recorder();
        var router = new PlayAudioRouter(sound);
        router.OnSimEvents(10, [Sound(5, "Step", heard: false)]);
        router.OnSimEvents(11, [Sound(5, "Step", heard: false)]);
        router.OnSimEvents(20, [Sound(5, "Shot", heard: true)]);
        router.OnSimEvents(21, [Sound(5, "Shot", heard: true)]);
        Assert.Equal(4, sound.Events.Count);
        Assert.Equal(0, router.Duplicates);
    }

    [Fact]
    public void CopiesFurtherApartThanTheWindowBothPlay()
    {
        var sound = new Recorder();
        var router = new PlayAudioRouter(sound);
        router.OnSimEvents(10, [Sound(5, "Step", heard: false)]);
        router.OnSimEvents(11 + Window, [Sound(5, "Step", heard: true)]);
        Assert.Equal(2, sound.Events.Count);
    }

    [Fact]
    public void APairIsMatchedOnce()
    {
        // Own, heard (dropped), then the next own step of the same sound plays again.
        var sound = new Recorder();
        var router = new PlayAudioRouter(sound);
        router.OnSimEvents(10, [Sound(5, "Step", heard: false)]);
        router.OnSimEvents(11, [Sound(5, "Step", heard: true)]);
        router.OnSimEvents(12, [Sound(5, "Step", heard: false)]);
        Assert.Equal(2, sound.Events.Count);
    }

    [Fact]
    public void DifferentActorsAndSoundsAreNotMatched()
    {
        var sound = new Recorder();
        var router = new PlayAudioRouter(sound);
        router.OnSimEvents(10, [Sound(5, "Step", heard: false), Sound(6, "Step", heard: true), Sound(5, "Gear", heard: true)]);
        Assert.Equal(3, sound.Events.Count);
    }

    [Fact]
    public void SoundsWithoutAnActorAlwaysPlay()
    {
        var sound = new Recorder();
        var router = new PlayAudioRouter(sound);
        router.OnSimEvents(10, [Sound(0, "Boom", heard: false), Sound(0, "Boom", heard: true)]);
        Assert.Equal(2, sound.Events.Count);
    }

    [Fact]
    public void ThePreviousSinkSeesEveryEvent()
    {
        var sound = new Recorder();
        var hud = new Recorder();
        var router = new PlayAudioRouter(sound, hud);
        var notify = new SimEvent { Kind = SimEventKind.Notify, ActorId = 5, What = new Name("Flash") };
        router.OnSimEvents(10, [Sound(5, "Step", heard: false), notify]);
        router.OnSimEvents(11, [Sound(5, "Step", heard: true)]);
        Assert.Equal(3, hud.Events.Count);
        Assert.Equal(2, sound.Events.Count);
        Assert.Equal(SimEventKind.Notify, sound.Events[1].Event.Kind);
    }
}
