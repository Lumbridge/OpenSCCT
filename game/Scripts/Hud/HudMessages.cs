using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScctGame.Hud;

/// <summary>
/// The receiving side of the localized messages on a client (spec/objectives/messages.md rules 4 to 6 and
/// spec/hud/messages.md): the text of a message is built on the receiving machine from the replicated PRIs and
/// object, then SHud routes it to the game message queue or one of the stacks, which the HUD pages show. The HUD
/// layer feeds it from <c>ReceiveLocalizedMessage</c> calls for the local controller (<see cref="HudReceiver"/>) and
/// runs <see cref="Tick"/> once a draw, as the page's message driver does.
/// <para>These are SHud, SGameMessages, SDeathMessage and GameMessage units; they live with the HUD until those
/// classes get their own port in OpenScct.Game.</para>
/// </summary>
public static class HudMessages
{
    public const string GameMessagesClass = "SBase.SGameMessages";
    public const string DeathMessageClass = "SBase.SDeathMessage";
    public const string EngineGameMessageClass = "Engine.GameMessage";

    /// <summary>Switches that go to the game message queue (priority 2); every other switch goes to the stack.</summary>
    [Provenance(ProvenanceKind.Decoded, "SHud.LocalizedMessage: 0, 2, 3, 4, 5, 6, 7, 15, 16, 17, 20, 53, 54, 55 -> SetGameMessage(text, 2)")]
    private static readonly int[] GameQueueSwitches = [0, 2, 3, 4, 5, 6, 7, 15, 16, 17, 20, 53, 54, 55];

    [Provenance(ProvenanceKind.Decoded, "messages.md constants: default stack time 10 s")]
    public const int DefaultStackTime = 10;

    [Provenance(ProvenanceKind.Decoded, "messages.md constants: switch 39 stack time 4 s")]
    public const int HackingStackTime = 4;

    [Provenance(ProvenanceKind.Decoded, "messages.md constants: game message priority 2")]
    public const byte GameMessagePriority = 2;

    [Provenance(ProvenanceKind.Decoded, "SHud.LocalizedMessage: other message classes go to SetGameMessage(text, 1)")]
    public const byte OtherMessagePriority = 1;

    [Provenance(ProvenanceKind.Decoded, "SHud.LocalizedMessage alarm branch: Duration < 500 else 5 s; alarm off 1 s")]
    public const int AlarmLongTime = 5;

    private static readonly SGameMessages Texts = new();
    private static readonly SDeathMessage DeathTexts = new();
    private static readonly GameMessage EngineTexts = new();

    // ---- texts ---------------------------------------------------------------------------------------------

    /// <summary><c>SGameMessages.GetString</c> with only an object (the objective messages).</summary>
    public static string GetString(int Switch, Actor? obj) => GetString(Switch, null, null, obj);

    /// <summary>
    /// <c>SGameMessages.GetString</c>: the text the receiving machine builds. A reference that did not arrive (not
    /// relevant to this client) leaves its part empty.
    /// </summary>
    [Uc("SBase.SGameMessages.GetString", Sha1 = "74a64c523b2e73dafb8399d514deef41dd06e864")]
    [PortOf("uc:SBase.SGameMessages:GetString", Spec = "spec/hud/messages.md#rules")]
    public static string GetString(int Switch, PlayerReplicationInfo? pri1, PlayerReplicationInfo? pri2, UObject? obj)
    {
        string trigger = (obj as SObjectiveTrigger)?.TriggerName ?? "";
        string objective = (obj as SObjectiveTrigger)?.Objective?.ObjectiveName ?? "";
        string name1 = pri1?.PlayerName ?? "";
        string name2 = pri2?.PlayerName ?? "";
        return Switch switch
        {
            1 or 40 => AlarmText(obj as SAlarm, longIntruder: Switch == 1),
            2 => Texts.MatchRestart,
            K.MsgMissionSpy => (obj as SObjective)?.Description ?? "",
            4 => Texts.Win,
            5 => Texts.Lose,
            7 => obj is LevelInfo li
                ? Texts.Score + Texts.Spies + li.NbPointsAttack.ToString(CultureInfo.InvariantCulture) + Texts.Sentries +
                    li.NbPointsDefense.ToString(CultureInfo.InvariantCulture)
                : "",
            K.MsgDataStolen => Join(trigger, Texts.FlagTaken),
            K.MsgDataDropped => Join(trigger, Texts.FlagDropped),
            K.MsgDataSecured => Join(trigger, Texts.FlagRetaken),
            13 => Texts.EnemyLocked,
            14 => Texts.SpyOff,
            K.MsgMissionMerc => (obj as SObjective)?.DescriptionDEF ?? "",
            16 => Join(Join(name1, Texts.DropAttack1), name2),
            17 => Join(Join(name2, Texts.ElbowShock1), name1) + Texts.ElbowShock2,
            18 => Join(Join(name1, Texts.Grabbed1), name2) + Texts.Grabbed2,
            K.MsgWarnHacking => (obj as SInterrupteur)?.sWarnHackingText ?? "",
            20 => Join(name2, Texts.GrabbedAssom),
            21 => Texts.GPOChaffed,
            K.MsgDataCaptured => Join(trigger, Texts.FlagCaptured),
            K.MsgInterrupted => Texts.ObjectiveInterrupted,
            24 => obj is SAlarm alarm ? alarm.AlarmDescriptionSpy : Texts.AlarmActive,
            25 => Texts.UnlockingAccess,
            26 => Texts.AccessUnlocked,
            27 => Join(objective, Texts.BombUnderAttack),
            28 => Join(objective, Texts.BombSecured),
            29 => Join(objective, Texts.BombNeutralized),
            K.MsgNeutralizationInProgress => Join(trigger, Texts.NeutralizeUnderAttack),
            K.MsgNeutralized => Join(objective, Texts.NeutralizeTaken),
            K.MsgHackingInProgress => Texts.DoorTriggered,
            41 => Texts.NoStickyCamAvailable,
            50 => Join(name2, Texts.GrabbedTimeElapsed),
            51 => Texts.PresDetectorActivated,
            52 => Texts.SpyTrapActivated,
            K.MsgGoToDropZone => Texts.ExtractionAtt,
            K.MsgRetrieveHardDrive => Texts.ExtractionDef,
            55 => Texts.SabotageNoModem,
            56 => Join(name1, Texts.TeammateNoLivesLeft),
            57 => Texts.CounterGrabWin,
            58 => Texts.CounterGrabLose,
            60 => Texts.SpyLockedByDef,
            61 => Texts.SpyUnLockedByDef,
            253 => Join(Join(Texts.Intruder, "-"), Texts.IntruderArea),
            254 => Texts.Intruder,
            255 => " ",
            _ => "",
        };
    }

    /// <summary>The alarm texts of switches 1 and 40 by the alarm's message priority.</summary>
    private static string AlarmText(SAlarm? alarm, bool longIntruder)
    {
        if (alarm is null)
        {
            return Texts.AlarmActive;
        }

        return alarm.MessagePriority switch
        {
            0 or 1 => alarm.AlarmDescription,
            2 or 3 => Join(Join(alarm.AlarmDescription, ":"), Texts.Intruder),
            4 or 5 => Join(Join(alarm.AlarmDescription, ":"), longIntruder ? Texts.IntruderArea : Texts.Intruder),
            _ => "",
        };
    }

    /// <summary>OpenSCCT's own: a kill line's name with the player's level tag before it ("(P3 42) Name"); guests as they are.</summary>
    private static string Ranked(PlayerReplicationInfo pri) =>
        OpenScct.Net.Cosmetics.PlayerRank.Unpack(pri.SvmRank) is { IsNone: false } rank ? "(" + rank.Tag + ") " + pri.PlayerName : pri.PlayerName;

    /// <summary><c>SDeathMessage.GetString</c>: the kill line ("Killer killed Victim", "Victim has resigned...").</summary>
    [Uc("SBase.SDeathMessage.GetString", Sha1 = "fb23e354a6bd97aa1c342246da3e732f6d05811e")]
    [PortOf("uc:SBase.SDeathMessage:GetString", Spec = "spec/hud/messages.md#rules")]
    public static string DeathString(int Switch, PlayerReplicationInfo? killer, PlayerReplicationInfo? victim)
    {
        string victimName = victim is not null ? Ranked(victim) : DeathTexts.SomeoneString;
        string killerName = killer is not null ? Ranked(killer) : DeathTexts.SomeoneString;
        List<string> words = DeathTexts.DeathString;
        string Word(int i) => i >= 0 && i < words.Count ? words[i] : "";
        switch (Switch)
        {
            case 18:
                return Join(victimName, Word(17));
            case 19:
                return Join(Join(killerName, DeathTexts.Grabbed1), victimName) + DeathTexts.Grabbed2;
            case 23:
                return Join(Join(killerName, DeathTexts.SnipedString), victimName);
            case 24:
                return Join(victimName, DeathTexts.ResignedString);
        }

        if (killer is not null && ReferenceEquals(killer, victim))
        {
            return Join(victimName, DeathTexts.SuicideString);
        }

        return Join(Join(killerName, Word(Switch < 18 ? Switch : 5)), victimName);
    }

    /// <summary><c>Engine.GameMessage.GetString</c>: entered (1) and left (4) the game.</summary>
    [Uc("Engine.GameMessage.GetString", Sha1 = "3fca6763387171450c130cb2e717900f086a459e")]
    [PortOf("uc:Engine.GameMessage:GetString", Spec = "spec/hud/messages.md#rules")]
    public static string EngineGameString(int Switch, PlayerReplicationInfo? pri1) => Switch switch
    {
        1 => pri1 is null || pri1.PlayerName.Length == 0 ? EngineTexts.NewPlayerMessage : Join(pri1.PlayerName, EngineTexts.EnteredMessage),
        4 => pri1 is null || pri1.PlayerName.Length == 0 ? "" : Join(pri1.PlayerName, EngineTexts.LeftMessage),
        _ => "",
    };

    /// <summary>The text of a localized message of any class the game sends.</summary>
    public static string TextOf(string messageClass, int Switch, PlayerReplicationInfo? pri1, PlayerReplicationInfo? pri2, UObject? obj) =>
        IsClass(messageClass, GameMessagesClass) ? GetString(Switch, pri1, pri2, obj)
        : IsClass(messageClass, DeathMessageClass) ? DeathString(Switch, pri1, pri2)
        : IsClass(messageClass, EngineGameMessageClass) ? EngineGameString(Switch, pri1)
        : "";

    /// <summary>UnrealScript <c>@</c>: the two strings joined by one space.</summary>
    private static string Join(string a, string b) => a + " " + b;

    private static bool IsClass(string path, string expected) =>
        string.Equals(path, expected, StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("." + expected[(expected.IndexOf('.', StringComparison.Ordinal) + 1)..], StringComparison.OrdinalIgnoreCase);

    // ---- routing -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>ReceiveLocalizedMessage(class'SGameMessages', ...)</c> on the receiving controller: builds the text and routes
    /// it through its SHud.
    /// </summary>
    public static void ReceiveLocalizedMessage(PlayerController receiver, int Switch, Actor? obj) =>
        ReceiveLocalizedMessage(receiver, GameMessagesClass, Switch, null, null, obj);

    /// <summary><c>ReceiveLocalizedMessage</c> for any message class: <c>LocalMessage.ClientReceive</c> then SHud.</summary>
    public static void ReceiveLocalizedMessage(PlayerController receiver, string messageClass, int Switch,
        PlayerReplicationInfo? pri1, PlayerReplicationInfo? pri2, UObject? obj)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        if (receiver.myHUD is SHud hud)
        {
            LocalizedMessage(hud, messageClass, Switch, pri1, pri2, obj, null);
        }
    }

    /// <summary><c>SHud.LocalizedMessage</c> for SGameMessages with the text already built.</summary>
    public static void LocalizedMessage(SHud hud, int Switch, string text) =>
        LocalizedMessage(hud, GameMessagesClass, Switch, null, null, null, text);

    /// <summary>
    /// <c>SHud.LocalizedMessage</c> (spec/hud/messages.md rules 2 to 5): SGameMessages to the queue or the stack
    /// (alarms with their own time and priority), SDeathMessage to the queue (priority 2 for a teammate's death,
    /// else 1), anything else to the queue with priority 1. Music and jingles belong to the audio layer.
    /// </summary>
    [Uc("SBase.SHud.LocalizedMessage", Sha1 = "acc2703bfe2d91a6e10e8a790442a541d33896ec")]
    [PortOf("uc:SBase.SHud:LocalizedMessage", Spec = "spec/hud/messages.md#rules")]
    public static void LocalizedMessage(SHud hud, string messageClass, int Switch, PlayerReplicationInfo? pri1,
        PlayerReplicationInfo? pri2, UObject? obj, string? criticalString)
    {
        ArgumentNullException.ThrowIfNull(hud);
        string text = string.IsNullOrEmpty(criticalString) ? TextOf(messageClass, Switch, pri1, pri2, obj) : criticalString;

        // The message's jingles and sounds (spec/native/audio.md rule 31).
        hud.LocalizedMessageSounds(messageClass, Switch, pri1, pri2, obj);
        if (IsClass(messageClass, GameMessagesClass))
        {
            if (Array.IndexOf(GameQueueSwitches, Switch) >= 0)
            {
                SetGameMessage(hud, text, GameMessagePriority);
                return;
            }

            int time;
            byte prio;
            if (obj is SAlarm alarm)
            {
                time = alarm.bAlarmOn ? (alarm.Duration < 500 ? alarm.Duration : AlarmLongTime) : 1;
                prio = alarm.MessagePriority;
                if (hud.Level.TimeSeconds > hud.AlarmHighlightTime)
                {
                    hud.AlarmHighlightTime = hud.Level.TimeSeconds + hud.AlarmListBlinkingTime;
                }
            }
            else if (Switch == K.MsgHackingInProgress)
            {
                time = HackingStackTime;
                prio = 0;
            }
            else
            {
                time = DefaultStackTime;
                prio = 0;
            }

            AddMessageToStack(hud, text, time, prio);
            if (Switch == K.MsgInterrupted)
            {
                AddMessageToStack(hud, " ", 1, 0);
            }

            return;
        }

        if (IsClass(messageClass, DeathMessageClass) && pri2 is not null && IsLocalTeamDeath(hud, pri2))
        {
            SetGameMessage(hud, text, GameMessagePriority);
            return;
        }

        SetGameMessage(hud, text, OtherMessagePriority);
    }

    /// <summary>The victim is on the local player's team and is not the local player.</summary>
    private static bool IsLocalTeamDeath(SHud hud, PlayerReplicationInfo victim)
    {
        PlayerReplicationInfo? own = (hud.P?.Controller ?? hud.PlayerOwner)?.PlayerReplicationInfo;
        int victimTeam = victim.Team?.TeamIndex ?? 0;
        int ownTeam = own?.Team?.TeamIndex ?? 0;
        return victimTeam == ownTeam && !ReferenceEquals(victim, own);
    }

    /// <summary>
    /// <c>SHud.AddMessageToStack</c>: empty texts are ignored; a text already on the stack stays
    /// max(time, MessageMustStayTime) from now; otherwise it is appended, and past MaxStackedMessages the first
    /// entry of the lowest priority goes.
    /// </summary>
    [Uc("SBase.SHud.AddMessageToStack", Sha1 = "4b9023a79b7562f7d1ff3e2fd6e12247c73cde8f")]
    [PortOf("uc:SBase.SHud:AddMessageToStack", Spec = "spec/hud/messages.md#rules")]
    public static void AddMessageToStack(SHud hud, string text, int time, byte prio) =>
        Push(hud, hud.MessageStack, text, time, prio, hud.MaxStackedMessages);

    /// <summary>
    /// <c>SHud.AddInfoMessageToStack</c>: the same rules with at most one entry (the zone message area).
    /// </summary>
    [Uc("SBase.SHud.AddInfoMessageToStack", Sha1 = "2c97f928056e4b01d7b82cb906c1f686d94ad0fa")]
    [PortOf("uc:SBase.SHud:AddInfoMessageToStack", Spec = "spec/hud/messages.md#rules")]
    public static void AddInfoMessageToStack(SHud hud, string text, int time, byte prio) =>
        Push(hud, hud.InfoMessageStack, text, time, prio, 1);

    /// <summary>
    /// <c>SHud.AddMessageToAuxiliaryStack</c> (the state message area, one entry): a text already there restarts
    /// when it had expired and stays time + 1 s; a new one is appended with its own time, and the lowest priority
    /// entry goes past one entry.
    /// </summary>
    [Uc("SBase.SHud.AddMessageToAuxiliaryStack", Sha1 = "0f62e423cda3c9cfabda5aa4454ed78cf9d0e3fd")]
    [PortOf("uc:SBase.SHud:AddMessageToAuxiliaryStack", Spec = "spec/hud/messages.md#rules")]
    public static void AddMessageToAuxiliaryStack(SHud hud, string text, int time, byte prio)
    {
        ArgumentNullException.ThrowIfNull(hud);
        if (text.Length == 0)
        {
            return;
        }

        float now = hud.Level.TimeSeconds;
        List<FStackedMessage> stack = hud.AuxiliaryMessageStack;
        for (int i = 0; i < stack.Count; i++)
        {
            if (stack[i].Message == text)
            {
                FStackedMessage m = stack[i];
                if (m.StartedTime + m.StayInStackTime <= now)
                {
                    m.StartedTime = now;
                }

                m.StayInStackTime = (int)(time + 1f);
                stack[i] = m;
                return;
            }
        }

        stack.Add(new FStackedMessage { Message = text, StartedTime = now, StayInStackTime = time, prio = prio });
        RemoveLowest(stack, 1);
    }

    /// <summary>
    /// <c>SHud.AddReticuleMessage</c>: an empty text clears it; the same text extends its time; a different one
    /// replaces it unless the current one has a higher priority.
    /// </summary>
    [Uc("SBase.SHud.AddReticuleMessage", Sha1 = "6e12c61e5f4b489cce9f1c23bc896e8b7ba12fbb")]
    [PortOf("uc:SBase.SHud:AddReticuleMessage", Spec = "spec/hud/messages.md#rules")]
    public static void AddReticuleMessage(SHud hud, string text, int time, byte prio)
    {
        ArgumentNullException.ThrowIfNull(hud);
        FStackedMessage m = hud.ReticuleMessage;
        m.Message ??= "";
        if (text.Length == 0)
        {
            hud.ReticuleMessage = m with { Message = "" };
            return;
        }

        if (m.Message == text)
        {
            hud.ReticuleMessage = m with { StayInStackTime = time };
            return;
        }

        if (m.Message.Length > 0 && m.prio > prio)
        {
            return;
        }

        hud.ReticuleMessage = new FStackedMessage { Message = text, StayInStackTime = time, StartedTime = hud.Level.TimeSeconds, prio = prio };
    }

    /// <summary><c>SHud.AddHudNumberMessageToStack</c>: the numbered HUD texts (detected, mine, malfunction...).</summary>
    [Uc("SBase.SHud.AddHudNumberMessageToStack", Sha1 = "7628a0b86b0adb0cd058016fa35a3dcd4bb5b5df")]
    [PortOf("uc:SBase.SHud:AddHudNumberMessageToStack", Spec = "spec/hud/messages.md#rules")]
    public static void AddHudNumberMessageToStack(SHud hud, int message, int time, byte prio)
    {
        ArgumentNullException.ThrowIfNull(hud);
        string text = message switch
        {
            1 => hud.DetectedMessage,
            2 => hud.PlayerOwner?.GameReplicationInfo is { } gri && gri.GameMode != EGameMode.GM_Multi ? hud.DetectedMessage : hud.AreaIsolatedMessage,
            3 => hud.MineOppositeWall,
            4 => hud.TextMineLaunched,
            5 => hud.TextNoiseEmitterOn,
            6 => hud.TextNoiseEmitterOff,
            7 => hud.TextMalfunction,
            8 => hud.TextRebooting,
            _ => "",
        };
        AddMessageToStack(hud, text, time, prio);
    }

    /// <summary>
    /// <c>SHud.SetGameMessage</c>: a text already queued is not added again; the queue is kept in priority order
    /// (lowest first, a new entry after the others of its priority). The page shows the head (rule 8).
    /// </summary>
    [Uc("SBase.SHud.SetGameMessage", Sha1 = "8d6b449a46a8f0f586f42dab56e4b74f88ffc2ef")]
    [PortOf("uc:SBase.SHud:SetGameMessage", Spec = "spec/hud/messages.md#rules")]
    public static void SetGameMessage(SHud hud, string text, byte prio)
    {
        ArgumentNullException.ThrowIfNull(hud);
        List<FStackedMessage> queue = hud.GameMessageQueue;
        foreach (FStackedMessage m in queue)
        {
            if (m.Message == text)
            {
                return;
            }
        }

        var entry = new FStackedMessage { Message = text, prio = prio };
        if (queue.Count == 0 || prio >= queue[^1].prio)
        {
            queue.Add(entry);
        }
        else if (prio < queue[0].prio)
        {
            queue.Insert(0, entry);
        }
        else
        {
            // The script's search loop steps once more after it finds the first higher priority, so the entry lands
            // just after that one.
            int i = 0;
            while (i < queue.Count && queue[i].prio <= prio)
            {
                i++;
            }

            queue.Insert(Math.Min(i + 1, queue.Count), entry);
        }
    }

    private static void Push(SHud hud, List<FStackedMessage> stack, string text, int time, byte prio, int max)
    {
        ArgumentNullException.ThrowIfNull(hud);
        if (text.Length == 0)
        {
            return;
        }

        float now = hud.Level.TimeSeconds;
        int stay = Math.Max(time, (int)hud.MessageMustStayTime);
        for (int i = 0; i < stack.Count; i++)
        {
            if (stack[i].Message == text)
            {
                // The entry keeps its start; its stay reaches max(time, MessageMustStayTime) from now.
                stack[i] = stack[i] with { StayInStackTime = (int)(now + stay - stack[i].StartedTime) };
                return;
            }
        }

        stack.Add(new FStackedMessage { Message = text, StartedTime = now, StayInStackTime = stay, prio = prio });
        RemoveLowest(stack, max);
    }

    private static void RemoveLowest(List<FStackedMessage> stack, int max)
    {
        if (max > 0 && stack.Count > max)
        {
            int lowest = 0;
            for (int i = 1; i < stack.Count; i++)
            {
                if (stack[i].prio < stack[lowest].prio)
                {
                    lowest = i;
                }
            }

            stack.RemoveAt(lowest);
        }
    }

    // ---- the page's message driver --------------------------------------------------------------------------

    /// <summary>
    /// The page's message driver, once a draw (rules 6 to 8): expired entries leave the three stacks; the game
    /// queue shows its head for GameMessageDisplayTime and then moves on, and waits while the tutorial message shows.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "HUD page message driver game 0x10b32c30 (own decode): prune by StartedTime + StayInStackTime < now; game queue head for GameMessageDisplayTime")]
    public static void Tick(SHud hud, bool tutorialShown = false)
    {
        ArgumentNullException.ThrowIfNull(hud);
        float now = hud.Level.TimeSeconds;
        Prune(hud.MessageStack, now);
        Prune(hud.AuxiliaryMessageStack, now);
        Prune(hud.InfoMessageStack, now);
        if (hud.GameMessageQueue.Count == 0 || tutorialShown)
        {
            return;
        }

        if (hud.GameMessageEndTime == 0f)
        {
            hud.GameMessageStartTime = now;
            hud.GameMessageEndTime = now + hud.GameMessageDisplayTime;
        }
        else if (hud.GameMessageEndTime < now)
        {
            hud.GameMessageQueue.RemoveAt(0);
            if (hud.GameMessageQueue.Count > 0)
            {
                hud.GameMessageStartTime = now;
                hud.GameMessageEndTime = now + hud.GameMessageDisplayTime;
            }
            else
            {
                hud.GameMessageEndTime = 0f;
            }
        }
    }

    /// <summary>The text a stack area shows: every entry after a leading line break, one per line (the driver's "%s\n%s").</summary>
    public static string StackText(List<string> lines) => lines.Count == 0 ? "" : "\n" + string.Join('\n', lines);

    private static void Prune(List<FStackedMessage> stack, float now) =>
        stack.RemoveAll(m => m.StartedTime + m.StayInStackTime < now);
}
