using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>
/// The sound calls a player's machine makes for the server and the HUD: ClientPlaySound, ClientSetMusic,
/// ClientHearSound, the HUD messages' jingles and the goggles' mode sounds (spec/native/audio.md rules 5, 29 to 31,
/// spec/equipment/vision-goggles.md rule 8).
/// </summary>
public sealed class AudioCallsTests
{
    private sealed class Thing : Actor
    {
    }

    private sealed class Rig
    {
        public Rig()
        {
            World = new World(1, ENetMode.NM_Client);
            Pc = World.Level.Spawn<SPlayerController>(new FVector(10f, 20f, 30f))!;
            World.Level.Info.LocalPlayerController = Pc;
            World.Step();
        }

        public World World { get; }

        public SPlayerController Pc { get; }

        /// <summary>The sound events raised since the last call (between two ticks: the world's late events).</summary>
        public List<SimEvent> Take()
        {
            List<SimEvent> late = World.Events.Late.ToArray().Where(e => e.Kind is SimEventKind.PlaySound or SimEventKind.StopSound or SimEventKind.PlayMusic).ToList();
            World.Step();
            return late;
        }
    }

    [Fact]
    public void ClientPlaySoundPlaysAtTheControllerIn2DWithoutAViewTarget()
    {
        var rig = new Rig();
        rig.Pc.ClientPlaySound(new AssetRef("Interface.Menu.Menu_start_point"));
        SimEvent e = Assert.Single(rig.Take());
        Assert.Equal("Interface.Menu.Menu_start_point", e.What.ToString());
        Assert.Equal(rig.Pc.ActorId, e.ActorId);
        Assert.Equal(0.9f, e.Volume);
        Assert.True(e.NoAttenuate);
        Assert.True(e.Flat);
    }

    [Fact]
    public void ClientSetMusicPlaysAJingleOrTheMusic()
    {
        var rig = new Rig();
        rig.Pc.ClientSetMusic(new AssetRef("Interface.Objectifs.Jingl_AttacB"), EMusicTransition.MTRAN_None);
        SimEvent jingle = Assert.Single(rig.Take());
        Assert.Equal(SimEventKind.PlaySound, jingle.Kind);
        Assert.Equal((int)ESoundSlot.SLOT_Talk, jingle.Slot);
        Assert.Equal(1f, jingle.Volume);

        rig.Pc.ClientSetMusic(new AssetRef("Interface.Objectifs.Jingle_Victoire1"), EMusicTransition.MTRAN_Fade);
        SimEvent music = Assert.Single(rig.Take());
        Assert.Equal(SimEventKind.PlayMusic, music.Kind);
        Assert.Equal(3f, music.Radius);
        Assert.Equal("Interface.Objectifs.Jingle_Victoire1", rig.Pc.Song.Path);
    }

    [Fact]
    public void ClientHearSoundRaisesTheServersCall()
    {
        var rig = new Rig();
        Actor other = rig.World.Level.Spawn<Thing>(new FVector(500f, 0f, 0f))!;
        int id = (other.ActorId * 16) + ((int)ESoundSlot.SLOT_Interact * 2) + 1;
        rig.Pc.ClientHearSound(other, id, new AssetRef("Sound_Persos.FootSteps.SW_FootRun"), new FVector(1f, 2f, 3f),
            new FVector(800f, 0f, 1200f), true);
        rig.Pc.ClientHearSound(null, 0, new AssetRef("Armes_impacts.Armes.RA_GunDef_1shot"), new FVector(7f, 8f, 9f),
            new FVector(1000f, 2000f, 1000f), true);
        List<SimEvent> events = rig.Take();
        Assert.Equal(2, events.Count);
        SimEvent step = events[0];
        Assert.True(step.Heard);
        Assert.Equal(other.ActorId, step.ActorId);
        Assert.Equal(other.Location, step.Location);
        Assert.Equal((int)ESoundSlot.SLOT_Interact, step.Slot);
        Assert.True(step.NoOverride);
        Assert.Equal(0.8f, step.Volume, 4);
        Assert.Equal(80f, step.Radius);
        Assert.Equal(1.2f, step.Pitch, 4);
        Assert.False(step.Flat);
        SimEvent shot = events[1];
        Assert.Equal(0, shot.ActorId);
        Assert.Equal(new FVector(7f, 8f, 9f), shot.Location);
        Assert.Equal(2000f, shot.Radius);
    }

    [Fact]
    public void AnUnattenuatedSoundOfTheViewedActorIsFlat()
    {
        var rig = new Rig();
        Actor viewed = rig.World.Level.Spawn<Thing>()!;
        Actor elsewhere = rig.World.Level.Spawn<Thing>()!;
        rig.Pc.ViewTarget = viewed;
        viewed.PlaySound(new AssetRef("A"), Attenuate: false);
        viewed.PlaySound(new AssetRef("B"));
        elsewhere.PlaySound(new AssetRef("C"), Attenuate: false);
        viewed.PlayOwnedSound(new AssetRef("D"));
        List<SimEvent> events = rig.Take();
        Assert.Equal([true, false, false, true], events.Select(e => e.Flat));
    }

    [Fact]
    public void HudMessagesPlayTheirJingles()
    {
        var rig = new Rig();
        SHud hud = rig.World.Level.Spawn<SHud>(default, default, rig.Pc)!;
        hud.PlayerOwner = rig.Pc;
        rig.Take();

        // An objective under attack (switch 10): the attack jingle through ClientSetMusic, a jingle on SLOT_Talk.
        OpenScctGame.Hud.HudMessages.LocalizedMessage(hud, "SBase.SGameMessages", 10, null, null, null, "Objective under attack");
        SimEvent attack = Assert.Single(rig.Take());
        Assert.Equal("Interface.Objectifs.Jingl_AttacB", attack.What.ToString());
        Assert.Equal((int)ESoundSlot.SLOT_Talk, attack.Slot);

        // The score sound (switch 2) at the HUD's owner.
        OpenScctGame.Hud.HudMessages.LocalizedMessage(hud, "SBase.SGameMessages", 2, null, null, null, "Score");
        SimEvent score = Assert.Single(rig.Take());
        Assert.Equal("Interface.Menu.Menu_start_point", score.What.ToString());
        Assert.Equal(SHud.MessageSoundVolume, score.Volume);
        Assert.Equal(SHud.MessageSoundRadius, score.Radius);

        // A message with no sound.
        OpenScctGame.Hud.HudMessages.LocalizedMessage(hud, "SBase.SGameMessages", 3, null, null, null, "Other");
        Assert.Empty(rig.Take());
    }

    [Fact]
    public void AVisionModeStopsTheLoopsAndPlaysItsSounds()
    {
        var rig = new Rig();
        SGoggle goggle = rig.World.Level.Spawn<SGoggle>(default, default, rig.Pc)!;
        rig.Take();
        goggle.CurrentMode = SGoggle.REN_NightVision;
        goggle.Activate();
        List<SimEvent> events = rig.Take();
        Assert.Equal(
            ["StopSound JUMloop", "StopSound VisionNight_loop", "StopSound Emf_fond", "PlaySound VisionSWIT_1", "PlaySound VisionRUN_1", "PlaySound VisionNight_loop"],
            events.Select(e => e.Kind + " " + e.What.ToString()[(e.What.ToString().LastIndexOf('.') + 1)..]));
        Assert.All(events, e => Assert.Equal(rig.Pc.ActorId, e.ActorId));
        Assert.Equal(SGoggle.VisionSoundRadius, events[3].Radius);
    }
}