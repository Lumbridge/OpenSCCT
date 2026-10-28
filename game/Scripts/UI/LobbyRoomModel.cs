using System;
using System.Collections.Generic;
using OpenScct.Server.Online;

namespace OpenScctGame.UI;

/// <summary>
/// The rules of the original's Lobby Room page (Lobby_Create, GUIChatBox) without Godot
/// (spec/game/lobby-and-match-creation.md rules 11 to 18): the page button's caption for the host and for a client,
/// which session values changed since the client last saw them and how long they blink, the chat box's one-second
/// spam delay, and the captions of the lists.
/// </summary>
public sealed class LobbyRoomModel
{
    /// <summary>How long changed values blink (Lobby_Create.Tick: three seconds).</summary>
    public const double BlinkSeconds = 3.0;

    /// <summary>Blinks per second of a changed value (Lobby_Create.Tick: TimeSeconds * 3).</summary>
    public const double BlinkRate = 3.0;

    /// <summary>The chat box's spam delay (GUIChatBox: a line within a second of the last is not sent).</summary>
    public const double ChatSpamSeconds = 1.0;

    /// <summary>GUI.int [Lobby_Create] ChatBox.txtAntiSpam.</summary>
    public const string SpamText = "(Spam delay, 1 second)";

    /// <summary>GUI.int [Lobby_Create] PlayerAttList.WaitingPlayers.</summary>
    public const string WaitingText = "Waiting for players";

    /// <summary>GUI.int [Lobby_Create] TitlePage.</summary>
    public const string Title = "Lobby Room";

    /// <summary>GUI.int [Error_TeamHaveNoPlayer].</summary>
    public const string TeamHaveNoPlayer = "At least one player per team is required to launch the game.";

    private readonly Dictionary<SessionRow, double> _changedAt = new();
    private LobbySetup? _shown;
    private double _lastChat = double.NegativeInfinity;

    /// <summary>The caption of the page's main button: the host launches (or cancels a countdown), a client is ready or not.</summary>
    public static string MainButton(bool host, bool ready, bool countingDown) =>
        host ? countingDown ? "Cancel" : "Launch" : ready ? "Not Ready" : "Ready";

    /// <summary>The rows whose values differ between two setups (what a client sees blink after the host changed them).</summary>
    public static IReadOnlyList<SessionRow> Differences(LobbySetup before, LobbySetup after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var rows = new List<SessionRow>();
        void Check(SessionRow row, bool differs)
        {
            if (differs)
            {
                rows.Add(row);
            }
        }

        Check(SessionRow.Map, !string.Equals(before.Map, after.Map, StringComparison.OrdinalIgnoreCase));
        Check(SessionRow.Mode, before.Mode != after.Mode);
        Check(SessionRow.VoiceChat, before.VoiceMode != after.VoiceMode);
        Check(SessionRow.MaxPlayers, before.MaxPlayers != after.MaxPlayers);
        Check(SessionRow.Revenge, before.Revenge != after.Revenge);
        Check(SessionRow.RandomMap, before.RandomMap != after.RandomMap);
        Check(SessionRow.VoiceInLobby, before.VoiceInLobby != after.VoiceInLobby);
        Check(SessionRow.AutoBalance, before.AutoBalance != after.AutoBalance);
        Check(SessionRow.Alternate, before.Alternate != after.Alternate);
        Check(SessionRow.GamesInARow, before.GamesInARow != after.GamesInARow);
        Check(SessionRow.TimeLimit, before.TimeLimit != after.TimeLimit);
        Check(SessionRow.SpyLives, before.SpyLives != after.SpyLives || before.ScoreLimit != after.ScoreLimit);
        Check(SessionRow.MercLives, before.MercLives != after.MercLives);
        Check(SessionRow.Respawn, before.RespawnSeconds != after.RespawnSeconds);
        Check(SessionRow.ActiveDisks, before.Disks != after.Disks);
        Check(SessionRow.DisksToTake, before.DisksToWin != after.DisksToWin);
        Check(SessionRow.Bots, before.Bots != after.Bots);
        Check(SessionRow.BotsPerSide, before.BotsPerSide != after.BotsPerSide);
        Check(SessionRow.BotSkill, before.BotSkill != after.BotSkill);
        Check(SessionRow.Practice, before.Practice != after.Practice);
        return rows;
    }

    /// <summary>
    /// The session's setup as the page shows it now: the first one is taken as is; each later change marks its rows
    /// to blink from <paramref name="now"/>. Returns the rows that changed.
    /// </summary>
    public IReadOnlyList<SessionRow> Show(LobbySetup setup, double now)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (_shown is null)
        {
            _shown = setup;
            return [];
        }

        IReadOnlyList<SessionRow> changed = Differences(_shown, setup);
        _shown = setup;
        foreach (SessionRow row in changed)
        {
            _changedAt[row] = now;
        }

        return changed;
    }

    /// <summary>Whether a row is blinking at <paramref name="now"/>.</summary>
    public bool Blinking(SessionRow row, double now) => _changedAt.TryGetValue(row, out double at) && now - at < BlinkSeconds;

    /// <summary>Whether a row is drawn at <paramref name="now"/>: always, or every other third of a second while blinking.</summary>
    public bool Lit(SessionRow row, double now) => !Blinking(row, now) || ((int)(now * BlinkRate) & 1) == 0;

    /// <summary>
    /// The chat box: true when a line may be sent at <paramref name="now"/> (and counts it), false within the spam delay
    /// of the last one (the box then shows <see cref="SpamText"/>).
    /// </summary>
    public bool TrySendChat(double now)
    {
        if (now - _lastChat < ChatSpamSeconds)
        {
            return false;
        }

        _lastChat = now;
        return true;
    }

    /// <summary>A volume as the voice overlay writes it ("name - volume 40%").</summary>
    public static string VolumeText(string name, float volume) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name} - volume {Math.Round(volume * 100f):0}%");
}
