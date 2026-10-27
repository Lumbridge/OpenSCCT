using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

// spec/objectives/messages.md
public sealed class MessagesAndHudTests
{
    private sealed class RecordingGameHost : IGameHost
    {
        public readonly List<(PlayerController Target, GameCall Call)> Calls = new();

        public void ClientCall(PlayerController target, in GameCall call) => Calls.Add((target, call));

        public void ServerCall(PlayerController sender, in GameCall call)
        {
        }

        public bool IsGUIInGame(PlayerController? player) => false;

        public int NbPendingClientOnServer() => 0;

        public string GetURLMap() => string.Empty;

        public bool IsPsx2() => false;

        public bool IsPBClientEnabled() => false;

        public void OnScriptEvent(Actor source, int id)
        {
        }

        public int GetPing(PlayerController player) => 0;

        public NetworkType NetType() => NetworkType.NET_NONE;

        public bool IsDedicatedServerMode() => false;
    }

    [Fact]
    public void Broadcast_reaches_every_player_and_team_broadcast_one_side()
    {
        var w = new ObjWorld();
        TestPC spy = w.AddPlayer(spy: true, default);
        TestPC merc1 = w.AddPlayer(spy: false, default);
        TestPC merc2 = w.AddPlayer(spy: false, default);

        ObjectiveMessages.Broadcast(spy, K.MsgHackingInProgress);
        ObjectiveMessages.BroadcastTeam(spy, K.TeamMercs, K.MsgRetrieveHardDrive);
        ObjectiveMessages.Send(spy, K.MsgGoToDropZone);

        Assert.Equal(3, w.Net.Count(K.MsgHackingInProgress));
        Assert.Equal(new PlayerController[] { merc2, merc1 }, w.Net.To(K.MsgRetrieveHardDrive)); // controller list order
        Assert.Same(spy, Assert.Single(w.Net.To(K.MsgGoToDropZone)));
    }

    [Fact]
    public void Without_a_net_layer_messages_go_out_as_game_calls()
    {
        var w = new ObjWorld();
        w.Level.SetService<IObjectiveNet>(null);
        TestPC spy = w.AddPlayer(spy: true, default);
        var host = new RecordingGameHost();
        w.Level.SetService<IGameHost>(host);
        var marker = w.Level.Spawn<EventCounter>()!;

        ObjectiveMessages.Send(spy, K.MsgNeutralized, marker);

        (PlayerController target, GameCall call) = Assert.Single(host.Calls);
        Assert.Same(spy, target);
        Assert.Equal(ObjectiveNetLocal.NAME_ReceiveLocalizedMessage, call.Function);
        Assert.Equal(ObjectiveNetLocal.SGameMessagesClass, call.Class);
        Assert.Equal(K.MsgNeutralized, call.Int);
        Assert.Same(marker, call.Object);
    }

    [Fact]
    public void Without_a_net_layer_the_menu_mirror_runs_on_this_machine()
    {
        var w = new ObjWorld();
        w.Level.SetService<IObjectiveNet>(null);
        TestPC spy = w.AddPlayer(spy: true, default);
        var owner = w.Level.Spawn<SBasicGamePlayObject>()!;
        var entry = ObjectiveNatives.Spawn<SInteractObject>(owner, owner, default, default)!;

        spy.IManager!.AddInteractionObj(entry);
        spy.IManager.RemoveInteractionObj(entry);

        Assert.Equal(K.FixedEntries, spy.IManager.NbObjects);
    }

    [Fact]
    public void Draw_list_adds_at_the_front_once_and_removes_every_copy()
    {
        var w = new ObjWorld();
        var hud = w.Level.Spawn<SHud>()!;
        var a = w.Level.Spawn<EventCounter>()!;
        var b = w.Level.Spawn<EventCounter>()!;

        ObjectiveHud.AddRemoveActorOnHud(hud, a, true);
        ObjectiveHud.AddRemoveActorOnHud(hud, b, true);
        ObjectiveHud.AddRemoveActorOnHud(hud, a, true);
        Assert.Equal(new Actor?[] { b, a }, hud.ActorToDraw);

        hud.ActorToDraw.Add(a);
        ObjectiveHud.AddRemoveActorOnHud(hud, a, false);
        Assert.Equal(new Actor?[] { b }, hud.ActorToDraw);
    }

    [Fact]
    public void Radar_marker_goes_only_to_a_local_merc_with_a_pawn()
    {
        var w = new ObjWorld();
        SComputerObjectiveTrigger? terminal = null;
        w.Load(x => terminal = x.Place(new SComputerObjectiveTrigger(), default));
        TestPC merc = w.AddPlayer(spy: false, default);
        merc.myHUD = w.Level.Spawn<SHud>(owner: merc);
        w.Level.Info.LocalPlayerController = merc;

        terminal!.AddRemoveObjectiveForRadar(true);
        terminal.AddRemoveObjectiveForRadar(true);
        Assert.Equal(new SObjectiveTrigger?[] { terminal }, ((SHud)merc.myHUD!).ObjectivesList);
        Assert.Equal(2, w.Net.Multicasts.FindAll(m => m.Call == "Radar").Count);

        terminal.AddRemoveObjectiveForRadar(false);
        Assert.Empty(((SHud)merc.myHUD).ObjectivesList);

        TestPC spy = w.AddPlayer(spy: true, default);
        spy.myHUD = w.Level.Spawn<SHud>(owner: spy);
        w.Level.Info.LocalPlayerController = spy;
        terminal.AddRemoveObjectiveForRadar(true);
        Assert.Empty(((SHud)spy.myHUD!).ObjectivesList);
    }

    [Fact]
    public void A_client_copy_runs_a_multicast_without_sending_it_again()
    {
        var w = new ObjWorld();
        SObjective? objective = null;
        w.Load(x => objective = x.Place(new SObjective(), default));
        TestPC local = w.AddPlayer(spy: true, default);
        local.myHUD = w.Level.Spawn<SHud>(owner: local);
        w.Level.Info.LocalPlayerController = local;
        objective!.Role = ENetRole.ROLE_SimulatedProxy;

        objective.AddRemoveObjectiveOnClientHud(true);

        Assert.Contains(objective, ((SHud)local.myHUD!).ActorToDraw);
        Assert.Empty(w.Net.Multicasts);
    }

    [Fact]
    public void Hacker_hud_lists_the_panel_and_everyone_removes_it_at_the_end()
    {
        var w = new ObjWorld();
        SInterrupteur? sw = null;
        w.Load(x => sw = x.Place(new SInterrupteur { iHackingTime = 4 }, default));
        STriggerInteraction entry = sw!.TriggerInteraction!;
        TestPC hacker = w.AddPlayer(spy: true, default);
        TestPC other = w.AddPlayer(spy: true, default);
        hacker.myHUD = w.Level.Spawn<SHud>(owner: hacker);
        other.myHUD = w.Level.Spawn<SHud>(owner: other);
        w.Level.Info.LocalPlayerController = other;

        entry.AddRemoveActorOnClientHud(hacker, true);
        Assert.Contains(entry, ((SHud)hacker.myHUD!).ActorToDraw);
        Assert.Empty(((SHud)other.myHUD!).ActorToDraw);

        ((SHud)other.myHUD).ActorToDraw.Add(entry);
        entry.AddRemoveActorOnClientHud(null, false);
        Assert.Empty(((SHud)other.myHUD).ActorToDraw);
    }

    [Fact]
    public void Flag_carrier_multicast_sets_the_taken_flag_on_every_copy()
    {
        var w = new ObjWorld();
        SFlag? flag = null;
        w.Load(x => flag = x.Place(new SFlag(), default));

        flag!.SetFlagCarrier(true);
        Assert.True(flag.FlagTaken);
        Assert.Contains(("FlagCarrier", (Actor)flag, true), w.Net.Multicasts);

        flag.Role = ENetRole.ROLE_SimulatedProxy;
        flag.SetFlagCarrier(false);
        Assert.False(flag.FlagTaken);
        Assert.Single(w.Net.Multicasts, m => m.Call == "FlagCarrier");
    }
}
