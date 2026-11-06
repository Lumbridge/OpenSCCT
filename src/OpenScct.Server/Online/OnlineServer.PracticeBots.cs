using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Net.Discovery;

namespace OpenScct.Server.Online;

public sealed partial class OnlineServer
{
    private bool _practiceBotsStopped;

    private bool OnPracticeBotCommand(SessionPlayer player, string verb, string arg)
    {
        if (!PracticeBotCommands.IsCommand(verb)) return false;
        string reply;
        if (!Setup.Practice) reply = "Bot commands work in practice / Visit Map games only.";
        else if (!CanControl(player)) reply = "Only the host or an admin may control bots.";
        else if (Phase != SessionPhase.Match || player.Slot?.Controller is null) reply = "Bot commands work during a round.";
        else if (Settings.Bots is null) reply = "Bots are unavailable on this server.";
        else reply = PracticeBotCommands.Run(Host, new PracticeBotControl(this), player.Slot.Controller, Setup.MaxPlayers, verb, arg);
        SendMessage(player, reply);
        return true;
    }

    private sealed class PracticeBotControl(OnlineServer server) : IPracticeBotControl
    {
        public IReadOnlyList<PlayerController> Controllers => server._players.Where(p => p.IsBot)
            .Select(p => server._bots?.ControllerOf(p.Id)).OfType<PlayerController>().ToArray();

        public bool Stopped
        {
            get => server._practiceBotsStopped;
            set
            {
                server._practiceBotsStopped = value;
                if (server._bots is { } bots) bots.Stopped = value;
            }
        }

        public PlayerController? Add(byte team)
        {
            server._bots ??= server.StartBots(server.Host);
            if (server._bots is not { } bots) return null;
            int number = 1;
            while (server._players.Exists(p => p.Name.Equals(OnlineBotRoster.NameOf(team, number), StringComparison.OrdinalIgnoreCase))) number++;
            int id = server._nextPlayerId++;
            string name = OnlineBotRoster.NameOf(team, number);
            // The command names the current side, regardless of a revenge round's lobby-side swap.
            var pc = bots.Add(new OnlineBot(id, name, team, server.Setup.BotSkill, "?GamesInARow=2"));
            if (pc is null) return null;
            byte lobbyTeam = server.Host.Game?.MatchInfo is { Revenge: true, GamesInARow: < 2 } ? (byte)(1 - team) : team;
            var row = new SessionPlayer(id, name, 0) { IsBot = true, Team = lobbyTeam, Ready = true, Map = MapState.Ok };
            server._players.Add(row);
            server.BroadcastPlayer(row);
            SaveRoster();
            return pc;
        }

        public void Remove(PlayerController controller)
        {
            var row = server._players.Find(p => p.IsBot && ReferenceEquals(server._bots?.ControllerOf(p.Id), controller));
            if (row is null) return;
            server._bots!.Remove(row.Id);
            server._players.Remove(row);
            server.Send(null, SessionProtocol.Line("gone", row.Id));
            SaveRoster();
        }

        private void SaveRoster()
        {
            server.Setup = server.Setup with
            {
                Bots = BotFill.Custom,
                SpyBots = server._players.Count(p => p.IsBot && p.Team == 0),
                MercBots = server._players.Count(p => p.IsBot && p.Team == 1),
            };
            server.SendSetup(null);
            server.Master?.HeartbeatSoon();
        }

        public bool ChangeTeam(PlayerController controller, byte team)
        {
            var row = server._players.Find(p => p.IsBot && ReferenceEquals(server._bots?.ControllerOf(p.Id), controller));
            if (row is null || !server._bots!.ChangeTeam(row.Id, team)) return false;
            row.Team = server.Host.Game?.MatchInfo is { Revenge: true, GamesInARow: < 2 } ? (byte)(1 - team) : team;
            server.BroadcastPlayer(row);
            SaveRoster();
            return true;
        }
    }
}
