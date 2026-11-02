using System;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using CombatK = OpenScct.Game.Combat.K;

namespace OpenScctGame.Hud;

/// <summary>Interprets the existing localized-message payload before its participant structure becomes text.</summary>
public static class HudActivityClassifier
{
    public static bool TryCreate(string messageClass, int message, PlayerReplicationInfo? actor,
        PlayerReplicationInfo? victim, UObject? source, PlayerReplicationInfo? localPlayer, float now,
        string? text, out HudActivityEntry entry)
    {
        HudActivityParticipant? a = Capture(actor);
        HudActivityParticipant? v = Capture(victim);
        bool local = IsLocal(actor, localPlayer) || IsLocal(victim, localPlayer);
        entry = null!;

        if (IsClass(messageClass, HudMessages.DeathMessageClass))
        {
            HudActivityCause cause = DeathCause(message);
            bool self = actor is not null && victim is not null && ReferenceEquals(actor, victim);
            bool environment = message is (int)SDamageType.SFell or (int)SDamageType.Crushed;
            HudActivityKind kind = message == 24 ? HudActivityKind.Resign : environment && (self || actor is null)
                ? HudActivityKind.Death : self || message == (int)SDamageType.Suicided
                ? HudActivityKind.Suicide : actor is null ? HudActivityKind.Death : HudActivityKind.Kill;
            // Resign, suicide and an unknown killer are one-participant outcomes, never invented killers.
            if (kind != HudActivityKind.Kill) a = null;
            entry = new(0, kind, a, v, cause, kind switch
            {
                HudActivityKind.Resign => "RESIGNED",
                HudActivityKind.Suicide => "SUICIDE",
                HudActivityKind.Death => "DIED",
                _ => "KILL",
            }, "", now, local);
            return true;
        }

        if (IsClass(messageClass, HudMessages.EngineGameMessageClass) && message is 1 or 4)
        {
            bool join = message == 1;
            entry = new(0, join ? HudActivityKind.Join : HudActivityKind.Leave, a, null, HudActivityCause.None,
                join ? "JOINED" : "LEFT", (a?.Name ?? "Unknown player") + (join ? " joined the match" : " left the match"), now, local);
            return true;
        }

        if (!IsClass(messageClass, HudMessages.GameMessagesClass)) return false;

        if (message is 16 or 17 or 20 or CombatK.MsgSmokeSleep or CombatK.MsgStickyCameraSleep)
        {
            bool sleep = message is CombatK.MsgSmokeSleep or CombatK.MsgStickyCameraSleep;
            HudActivityCause cause = message switch
            {
                16 => HudActivityCause.DropAttack,
                17 => HudActivityCause.Elbow,
                20 => HudActivityCause.Grab,
                CombatK.MsgSmokeSleep => HudActivityCause.Smoke,
                _ => HudActivityCause.StickyCamera,
            };
            entry = new(0, sleep ? HudActivityKind.Sleep : HudActivityKind.Knockout, a, v, cause,
                sleep ? "SLEEP" : "KO", "", now, local);
            return true;
        }

        if (message is 1 or 24 or 40)
        {
            string place = source is SAlarm alarm ? Clean(alarm.AlarmDescription) : "";
            entry = new(0, HudActivityKind.Alarm, null, null, HudActivityCause.None, "ALARM",
                place.Length > 0 ? "Alarm notice: " + place : "Alarm notice", now, false) { AlarmSource = source };
            return true;
        }

        if (message is 2 or 4 or 5 or 7)
        {
            string notice = message switch
            {
                2 => "Match restarted",
                4 => "Victory",
                5 => "Defeat",
                _ => source is LevelInfo level
                    ? string.Create(CultureInfo.InvariantCulture, $"Score: Spies {level.NbPointsAttack} · Mercs {level.NbPointsDefense}")
                    : "Match score updated",
            };
            entry = new(0, HudActivityKind.Match, null, null, HudActivityCause.None, message == 2 ? "RESTART" : "MATCH",
                notice, now, false);
            return true;
        }

        if (message == 56)
        {
            entry = new(0, HudActivityKind.LastLife, a, null, HudActivityCause.None, "LAST LIFE",
                (a?.Name ?? "Unknown player") + " used their last life", now, local);
            return true;
        }

        string? action = message switch
        {
            10 => "Data taken", 11 => "Data dropped", 12 => "Data secured", 22 => "Data captured",
            23 => "Objective interrupted", 25 => "Unlocking access", 26 => "Access unlocked",
            27 => "Bomb under attack", 28 => "Bomb secured", 29 => "Bomb neutralized",
            36 => "Neutralization in progress", 37 => "Objective neutralized", _ => null,
        };
        if (action is null) return false;
        string objective = source switch
        {
            SObjectiveTrigger trigger => Clean(message is 27 or 28 or 29 or 37
                ? trigger.Objective?.ObjectiveName : trigger.TriggerName),
            SObjective obj => Clean(obj.ObjectiveName),
            _ => "",
        };
        entry = new(0, HudActivityKind.Objective, a, v, HudActivityCause.None, "OBJECTIVE",
            objective.Length > 0 ? objective + ": " + action : action, now, local);
        return true;
    }

    private static HudActivityParticipant? Capture(PlayerReplicationInfo? pri) => pri is null ? null :
        new(pri.PlayerID, string.IsNullOrWhiteSpace(pri.PlayerName) ? "Unknown player" : Clean(pri.PlayerName), pri.Team?.TeamIndex ?? -1);

    private static bool IsLocal(PlayerReplicationInfo? pri, PlayerReplicationInfo? own) =>
        pri is not null && own is not null && ReferenceEquals(pri, own);

    private static string Clean(string? text) => string.IsNullOrWhiteSpace(text) ? "" :
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool IsClass(string path, string expected) => string.Equals(path, expected, StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(expected[expected.IndexOf('.', StringComparison.Ordinal)..], StringComparison.OrdinalIgnoreCase);

    private static HudActivityCause DeathCause(int message) => message switch
    {
        (int)SDamageType.SDamage_Charged => HudActivityCause.Charge,
        (int)SDamageType.SDamage_DropAttacked => HudActivityCause.DropAttack,
        (int)SDamageType.SDamage_ElbowShock => HudActivityCause.Elbow,
        (int)SDamageType.SDamage_Explosion => HudActivityCause.Explosion,
        (int)SDamageType.SDTSnipeShot => HudActivityCause.Firearm,
        (int)SDamageType.SDTStickyShocker => HudActivityCause.StickyShock,
        (int)SDamageType.SFell => HudActivityCause.Fall,
        (int)SDamageType.Crushed => HudActivityCause.Crush,
        (int)SDamageType.DamTypeGrabChoke => HudActivityCause.Choke,
        (int)SDamageType.DamTypeGrabBreak => HudActivityCause.NeckBreak,
        (int)SDamageType.SDamage_Poison => HudActivityCause.Poison,
        (int)SDamageType.SDTStickyBlast => HudActivityCause.StickyBlast,
        23 => HudActivityCause.Headshot,
        _ => HudActivityCause.None,
    };
}
