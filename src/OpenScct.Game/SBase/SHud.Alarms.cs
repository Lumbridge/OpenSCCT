using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The HUD side of alarms and map messages that runs in the simulation on the player's machine (spec/devices/alarm.md
// rules 12 and 13, spec/devices/message-trigger.md). The HUD pages draw from AlarmsList and the message stacks.
public partial class SHud
{
    /// <summary>Volume, radius and pitch of the high-priority alarm loop.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASHud::execAddAlarm game 0x10a91a60: Audio->PlaySound(PlayerOwner, 0, Snd_HiPrioAlarm, Location, 1.0, 100.0, 1.0, 0x12)")]
    public const float HiPrioAlarmVolume = 1f;

    [Provenance(ProvenanceKind.Decoded, "ASHud::execAddAlarm game 0x10a91a60: radius 100.0")]
    public const float HiPrioAlarmRadius = 100f;

    /// <summary>Alarms above this priority count as high priority (the loop, the HUD alarm box).</summary>
    [Provenance(ProvenanceKind.Decoded, "ASHud::execAddAlarm / execRemoveAlarm: MessagePriority > 4")]
    public const byte HiPrioAlarmAbove = 4;

    /// <summary>
    /// Adds <paramref name="Al"/> to <see cref="AlarmsList"/> once (into the first empty slot, else at the front).
    /// When it is the first alarm of priority above 4 on the list, the high-priority alarm sound starts on the
    /// HUD's player (2D, looping).
    /// </summary>
    [UcNative("ASHud::execAddAlarm", Exe = 0x10a91a60)]
    [PortOf("nat:SBase.SHud.AddAlarm", Spec = "spec/devices/alarm.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a91a60: dedupe, first None slot or Insert(0,1), sound when no other alarm above 4 was listed")]
    [Provenance(ProvenanceKind.Estimated, "The play flags 0x12 (loop, 2D) go as a flat sound; the loop comes from the Sound's own flags")]
    public void AddAlarm(SAlarm? Al)
    {
        List<SAlarm?> list = AlarmsList;
        if (list.Contains(Al))
        {
            return;
        }

        bool firstHigh = true;
        foreach (SAlarm? a in list)
        {
            if (a is not null && a.MessagePriority > HiPrioAlarmAbove)
            {
                firstHigh = false;
                break;
            }
        }

        int empty = list.IndexOf(null);
        if (empty >= 0)
        {
            list[empty] = Al;
        }
        else
        {
            list.Insert(0, Al);
        }

        if (firstHigh && Al is { MessagePriority: > HiPrioAlarmAbove } && PlayerOwner is { XLevel: not null } owner)
        {
            owner.XLevel.World.Events.PlaySound(owner, new Name(Snd_HiPrioAlarm.Path), 0, HiPrioAlarmVolume,
                HiPrioAlarmRadius, 1f, flat: true);
        }
    }

    /// <summary>
    /// Removes <paramref name="Al"/> from <see cref="AlarmsList"/>; when no alarm of priority above 4 is left, the
    /// high-priority alarm sound stops.
    /// </summary>
    [UcNative("ASHud::execRemoveAlarm", Exe = 0x10a91b90)]
    [PortOf("nat:SBase.SHud.RemoveAlarm", Spec = "spec/devices/alarm.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a91b90: Remove(index), then StopSound(PlayerOwner, Snd_HiPrioAlarm) when none above 4 is left")]
    public void RemoveAlarm(SAlarm? Al)
    {
        List<SAlarm?> list = AlarmsList;
        int i = list.IndexOf(Al);
        if (i >= 0)
        {
            list.RemoveAt(i);
        }

        foreach (SAlarm? a in list)
        {
            if (a is not null && a.MessagePriority > HiPrioAlarmAbove)
            {
                return;
            }
        }

        PlayerOwner?.StopSound(Snd_HiPrioAlarm);
    }

    /// <summary>
    /// SHud.AddInfoMessageToStack (the zone message area, one entry): an empty text is ignored; a text already there
    /// stays max(time, MessageMustStayTime) from now; otherwise it is appended and, past one entry, the first entry
    /// of the lowest priority goes. The client HUD's copy is HudMessages.AddInfoMessageToStack.
    /// </summary>
    [PortOf("uc:SBase.SHud:AddInfoMessageToStack", Spec = "spec/hud/messages.md#rules")]
    public void AddInfoMessageToStack(string? Message, int Time, byte prio = 0)
    {
        if (string.IsNullOrEmpty(Message))
        {
            return;
        }

        float now = Level.TimeSeconds;
        int stay = SvmMath.Max(Time, SvmMath.Trunc(MessageMustStayTime));
        List<FStackedMessage> stack = InfoMessageStack;
        for (int i = 0; i < stack.Count; i++)
        {
            if (stack[i].Message == Message)
            {
                stack[i] = stack[i] with { StayInStackTime = SvmMath.Trunc(now + stay - stack[i].StartedTime) };
                return;
            }
        }

        stack.Add(new FStackedMessage { Message = Message, StartedTime = now, StayInStackTime = stay, prio = prio });
        if (stack.Count > 1)
        {
            int lowest = 0;
            for (int i = 0; i < stack.Count; i++)
            {
                if (stack[i].prio < stack[lowest].prio)
                {
                    lowest = i;
                }
            }

            stack.RemoveAt(lowest);
        }
    }

    /// <summary>
    /// SHud.AddHudNumberMessageToStack: the numbered HUD texts on the main stack (1 detected; 2 detected, or area
    /// isolated in a versus match; 3 to 8 the gadget texts). The client HUD's copy is
    /// HudMessages.AddHudNumberMessageToStack.
    /// </summary>
    [PortOf("uc:SBase.SHud:AddHudNumberMessageToStack", Spec = "spec/hud/messages.md#rules")]
    public void AddHudNumberMessageToStack(int Message, int Time, byte prio = 0)
    {
        string text = Message switch
        {
            1 => DetectedMessage,
            2 => PlayerOwner?.GameReplicationInfo is { } gri && gri.GameMode != EGameMode.GM_Multi ? DetectedMessage : AreaIsolatedMessage,
            3 => MineOppositeWall,
            4 => TextMineLaunched,
            5 => TextNoiseEmitterOn,
            6 => TextNoiseEmitterOff,
            7 => TextMalfunction,
            8 => TextRebooting,
            _ => "",
        };
        Equipment.GadgetHud.AddMessageToStack(PlayerOwner as SPlayerController, text, Time, prio);
    }
}
