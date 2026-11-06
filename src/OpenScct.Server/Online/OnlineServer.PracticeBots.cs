using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenScct.Core.Engine;
using OpenScct.Net.Discovery;

namespace OpenScct.Server.Online;

public sealed partial class OnlineServer
{
    private bool _practiceBotsStopped;
    private PendingPracticeBotCommand? _pendingPracticeBotCommand;

    private sealed record PendingPracticeBotCommand(SessionPlayer Player, string Verb, string Arg, byte ActivationMask, Task Preparation);

    private bool OnPracticeBotCommand(SessionPlayer player, string verb, string arg, bool navigationReady = false)
    {
        if (!PracticeBotCommands.IsCommand(verb)) return false;
        string reply;
        if (!Setup.Practice) reply = "Bot commands work in practice / Visit Map games only.";
        else if (!CanControl(player)) reply = "Only the host or an admin may control bots.";
        else if (Phase != SessionPhase.Match || _travel is not null || player.Slot?.Controller is null) reply = "Bot commands work during a round.";
        else if (Settings.Bots is null) reply = "Bots are unavailable on this server.";
        else if (_pendingPracticeBotCommand is not null && verb is "addbot" or "placebot")
            reply = "Bots are still being prepared. The pending bot command will run when they are ready.";
        else
        {
            var control = new PracticeBotControl(this, navigationReady);
            reply = PracticeBotCommands.Run(Host, control, player.Slot.Controller, Setup.MaxPlayers, verb, arg);
            if (control.NeedsPreparation && Settings.PrepareBots is { } prepare)
            {
                byte mask = Host.World.Level.Info.GEActivationMask;
                _pendingPracticeBotCommand = new(player, verb, arg, mask, StartBotPreparation(prepare, Host.Map.Name, mask));
                reply = "Preparing bots for " + Host.Map.Name + ". The command will run when they are ready.";
            }
        }
        SendMessage(player, reply);
        return true;
    }

    private void TickPracticeBotCommand()
    {
        if (_pendingPracticeBotCommand is not { } pending) return;
        if (!Setup.Practice || _travel is not null || !_players.Contains(pending.Player) ||
            !CanControl(pending.Player) || pending.Player.Slot?.Controller is null)
        {
            _pendingPracticeBotCommand = null;
            if (_players.Contains(pending.Player)) SendMessage(pending.Player, "Pending bot command cancelled.");
            return;
        }
        if (!pending.Preparation.IsCompleted) return;
        if (pending.Preparation.IsCompletedSuccessfully &&
            pending.ActivationMask != Host.World.Level.Info.GEActivationMask && Settings.PrepareBots is { } prepare)
        {
            // Traversal can change while the worker runs; warm the new cache key before creating the runner.
            byte mask = Host.World.Level.Info.GEActivationMask;
            _pendingPracticeBotCommand = pending with
            {
                ActivationMask = mask, Preparation = StartBotPreparation(prepare, Host.Map.Name, mask),
            };
            return;
        }
        _pendingPracticeBotCommand = null;
        if (pending.Preparation.IsCompletedSuccessfully)
            OnPracticeBotCommand(pending.Player, pending.Verb, pending.Arg, navigationReady: true);
        else
            SendMessage(pending.Player, BotPreparationError(pending.Preparation));
    }

    private sealed class PracticeBotControl(OnlineServer server, bool navigationReady) : IPracticeBotControl
    {
        public bool NeedsPreparation { get; private set; }

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
            // Validate the command and available slots before starting cold navigation off the match thread.
            if (server._bots is null && !navigationReady && server.Settings.PrepareBots is not null)
            {
                NeedsPreparation = true;
                return null;
            }
            server._bots ??= server.StartBots(server.Host, addingBot: true);
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
