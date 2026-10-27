using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>spec/devices/sound-music.md: SoundTrigger, SAmbientSoundTrigger and MusicTrigger as presentation events.</summary>
public sealed class SoundDeviceTests
{
    private static List<SimEvent> Now(DeviceScene s, SimEventKind kind)
    {
        var list = new List<SimEvent>();
        foreach (SimEvent e in s.World.Events.Current)
        {
            if (e.Kind == kind)
            {
                list.Add(e);
            }
        }

        return list;
    }

    [Fact]
    public void ASoundTriggerPlaysItsSoundOnEveryTriggerWithItsVolumeRadiusAndPitch()
    {
        DeviceScene s = DeviceScene.Create();
        SoundTrigger t = s.Add(new SoundTrigger { Sound = new AssetRef("Seq_alarme"), SoundVolume = 255, SoundRadius = 300f, SoundPitch = 32 },
            V.At(1f, 2f, 3f), tag: "blast");
        s.Begin();
        Assert.False(t.bCollideActors);
        s.Level.TriggerEvent("blast", null, null);
        s.Level.TriggerEvent("blast", null, null);
        s.Level.UntriggerEvent("blast", null, null);
        List<SimEvent> plays = Now(s, SimEventKind.PlaySound);
        Assert.Equal(2, plays.Count);
        Assert.Equal(new Name("Seq_alarme"), plays[0].What);
        Assert.Equal(1f, plays[0].Volume);
        Assert.Equal(300f, plays[0].Radius);
        Assert.Equal(0.5f, plays[0].Pitch);
        Assert.Equal(V.At(1f, 2f, 3f), plays[0].Location);
        Assert.Equal((int)ESoundSlot.SLOT_None, plays[0].Slot);
    }

    [Fact]
    public void ASoundTriggerHasTheDecodedDefaults()
    {
        var t = new SoundTrigger();
        Assert.Equal(190, t.SoundVolume);
        Assert.Equal(64f, t.SoundRadius);
        Assert.Equal(64, t.SoundPitch);
    }

    [Fact]
    public void AnAmbientTriggerStartsSilentAndSwitchesItsLoop()
    {
        DeviceScene s = DeviceScene.Create();
        var loop = new AssetRef("Squ_Feu_Molotov");
        SAmbientSoundTrigger plain = s.Add(new SAmbientSoundTrigger { AmbientSound = loop }, tag: "plain");
        SAmbientSoundTrigger toggle = s.Add(new SAmbientSoundTrigger { AmbientSound = loop, bToggle = true }, tag: "toggle");
        SAmbientSoundTrigger on = s.Add(new SAmbientSoundTrigger { AmbientSound = loop, bInitialyOn = true }, tag: "on");
        s.Begin();
        Assert.True(plain.AmbientSound.IsNone);
        Assert.Equal(loop, on.AmbientSound);

        s.Level.TriggerEvent("plain", null, null);
        s.Level.TriggerEvent("plain", null, null);
        Assert.Equal(loop, plain.AmbientSound);

        s.Level.TriggerEvent("toggle", null, null);
        Assert.Equal(loop, toggle.AmbientSound);
        s.Level.TriggerEvent("toggle", null, null);
        Assert.True(toggle.AmbientSound.IsNone);

        s.Level.UntriggerEvent("on", null, null);
        Assert.True(on.AmbientSound.IsNone);
        SimEvent stop = Assert.Single(Now(s, SimEventKind.StopSound));
        Assert.Equal(new Name("Squ_Feu_Molotov"), stop.What);
        s.Level.TriggerEvent("on", null, null);
        Assert.Equal(loop, on.AmbientSound);
    }

    [Fact]
    public void AMusicTriggerPlaysStopsAndTogglesTheTrack()
    {
        DeviceScene s = DeviceScene.Create();
        var song = new AssetRef("Squ_Mus_Stress");
        MusicTrigger toggle = s.Add(new MusicTrigger { Song = song, bToggle = true, FadeInTime = 2f, FadeOutTime = 3f }, tag: "toggle");
        MusicTrigger plain = s.Add(new MusicTrigger { Song = song }, tag: "plain");
        s.Begin();
        Assert.Equal(EDrawType.DT_None, toggle.DrawType);

        s.Level.TriggerEvent("toggle", null, null);
        SimEvent play = Assert.Single(Now(s, SimEventKind.PlayMusic));
        Assert.Equal(new Name("Squ_Mus_Stress"), play.What);
        Assert.Equal(2f, play.Radius);
        Assert.Equal(song, toggle.GetCurrentMusic());

        // Without bToggle a second trigger leaves the track playing; with it, the track stops.
        s.Level.TriggerEvent("plain", null, null);
        Assert.Single(Now(s, SimEventKind.PlayMusic));
        Assert.Empty(Now(s, SimEventKind.StopMusic));
        s.Level.TriggerEvent("toggle", null, null);
        SimEvent stop = Assert.Single(Now(s, SimEventKind.StopMusic));
        Assert.Equal(3f, stop.Radius);
        Assert.True(toggle.GetCurrentMusic().IsNone);

        // An UnTrigger always stops it.
        s.Level.TriggerEvent("plain", null, null);
        s.Level.UntriggerEvent("plain", null, null);
        Assert.Equal(2, Now(s, SimEventKind.StopMusic).Count);
    }

    [Fact]
    public void AnInstigatorOnlyMusicTriggerPlaysOnlyOnTheInstigatorsMachine()
    {
        DeviceScene s = DeviceScene.Create();
        MusicTrigger t = s.Add(new MusicTrigger { Song = new AssetRef("Song"), bInstigatorOnly = true }, tag: "m");
        s.Begin();
        TestSpy local = s.Player<TestSpy>(default);
        TestMerc other = s.Player<TestMerc>(default);
        s.Level.Info.LocalPlayerController = (PlayerController)local.Controller!;
        s.Level.TriggerEvent("m", null, other);
        Assert.Empty(Now(s, SimEventKind.PlayMusic));
        s.Level.TriggerEvent("m", null, local);
        Assert.Single(Now(s, SimEventKind.PlayMusic));
    }
}
