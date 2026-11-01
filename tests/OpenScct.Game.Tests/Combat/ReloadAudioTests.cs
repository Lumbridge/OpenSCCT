using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Animation;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

public sealed class ReloadAudioTests
{
    [Fact]
    public void ReloadStartsLocallyBeforeTheServerReplyAndRelaysWithoutAnOwnerEcho()
    {
        var scene = new CombatScene(ENetMode.NM_Client);
        var (pc, pawn) = scene.Player(false, default);
        scene.Level.Info.LocalPlayerController = pc;
        var weapon = scene.Give(new SWeaponDefense(), pawn);
        weapon.Role = ENetRole.ROLE_AutonomousProxy;
        weapon.ClipAmmo = 1;
        weapon.Ammo = 30;
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);
        int before = scene.World.Events.Count;
        Assert.True(weapon.Reload());
        var sounds = scene.World.Events.Current.ToArray().Skip(before)
            .Where(e => e.Kind == SimEventKind.PlaySound && e.What == new Name(weapon.ReloadSound.Path));
        Assert.True(Assert.Single(sounds).Owned);
        CombatCall relay = Assert.Single(net.ToServer, c => c.Call.Function == new Name("ServerSound")).Call;
        Assert.True(relay.Bool1);

        var server = new CombatScene();
        var (serverPc, serverPawn) = server.Player(false, default);
        var serverWeapon = server.Give(new SWeaponDefense(), serverPawn);
        before = server.World.Events.Count;
        serverPc.ReceiveCombatCall(relay with { Target = serverWeapon }, true);
        var sent = Assert.Single(server.World.Events.Current.ToArray().Skip(before),
            e => e.Kind == SimEventKind.PlaySound);
        Assert.True(sent.Owned); // NetHearSound excludes the owner, still relays to other listeners.
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    public void BodyReloadHandlingOnlyPlaysForThirdPersonListeners(bool behind, bool local, bool expected)
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(false, default);
        if (local) scene.Level.Info.LocalPlayerController = pc;
        pc.bBehindView = behind;
        pc.ViewTarget = pawn;
        pawn.PawnAnim[0].Sequence = new Name("ReloStSpFd2");
        var notify = new UAnimNotify_Sound { Sound = new AssetRef("Armes_impacts.Armes.Reload_Def_court"), Volume = 10, Radius = 80 };
        int before = scene.World.Events.Count;
        PawnAnimNotifies.Fire(pawn, 0, notify);
        var events = scene.World.Events.Current.ToArray().Skip(before).Where(e => e.Kind == SimEventKind.PlaySound).ToArray();
        if (expected) Assert.True(Assert.Single(events).Owned);
        else Assert.Empty(events);
    }
    [Theory]
    [InlineData("Armes_impacts.Armes.Empty")]
    [InlineData("Armes_impacts.Armes.Switch")]
    [InlineData("Armes_impacts.Armes.RA_Reload_Def")]
    public void WeaponHandlingStartsBeforeItsNetworkRelay(string path)
    {
        var scene = new CombatScene(ENetMode.NM_Client);
        var (_, pawn) = scene.Player(false, default);
        var weapon = scene.Give(new SWeaponDefense(), pawn);
        weapon.Role = ENetRole.ROLE_AutonomousProxy;
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);
        int before = scene.World.Events.Count;
        weapon.ServerSound(new AssetRef(path), ESoundSlot.SLOT_Interact, 200);
        var sound = Assert.Single(scene.World.Events.Current.ToArray().Skip(before), e => e.Kind == SimEventKind.PlaySound);
        Assert.Equal(new Name(path), sound.What);
        Assert.True(sound.Owned);
        Assert.Equal(200f, sound.Radius);
        Assert.True(Assert.Single(net.ToServer).Call.Bool1);
    }

    [Fact]
    public void ShockerStartsItsSoundBeforeTheServerShotReply()
    {
        var scene = new CombatScene(ENetMode.NM_Client);
        var (_, pawn) = scene.Player(true, default);
        var weapon = scene.Give(new SStickyShocker(), pawn);
        weapon.Role = ENetRole.ROLE_AutonomousProxy;
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);
        int before = scene.World.Events.Count;
        weapon.ServerFireWeaponWithLocation(default, 0, 0, 0);
        var sound = Assert.Single(scene.World.Events.Current.ToArray().Skip(before),
            e => e.Kind == SimEventKind.PlaySound && e.What == new Name(weapon.FireSingleShotSound.Path));
        Assert.True(sound.Owned);
        Assert.Single(net.ToServer);
    }

    [Fact]
    public void RemoteLandingHasOneServerSoundAcrossSimulationAndLandingRpc()
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(false, default);
        pc.Player = new SimNetConnection();
        int before = scene.World.Events.Count;
        pawn.PlayLandingSound();
        pawn.ServerPlayLandingSound();
        pawn.ClientPlayLandingSound();
        var sound = Assert.Single(scene.World.Events.Current.ToArray().Skip(before), e => e.Kind == SimEventKind.PlaySound);
        Assert.True(sound.Owned);
        Assert.Equal(new Name("Sound_Persos.FootSteps.SW_Landing"), sound.What);
    }

    [Fact]
    public void NonReloadBodyNotifiesAreOwnerPredictedToo()
    {
        var scene = new CombatScene();
        var (_, pawn) = scene.Player(false, default);
        pawn.PawnAnim[0].Sequence = new Name("WaitStAlFd0");
        int before = scene.World.Events.Count;
        PawnAnimNotifies.Fire(pawn, 0, new UAnimNotify_Sound
            { Sound = new AssetRef("Sound_Persos.vetements_materiel.SMEQUIP_1"), Volume = 0.4f, Radius = 20 });
        var sound = Assert.Single(scene.World.Events.Current.ToArray().Skip(before), e => e.Kind == SimEventKind.PlaySound);
        Assert.True(sound.Owned);
        Assert.Equal(0.4f, sound.Volume);
    }

}
