using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SHud.LocalizedMessage: the sounds of a message (spec/native/audio.md rule 31). The text side (queue, stack,
// alarms) is the HUD's (spec/hud/messages.md); this is the sound side, run with it on the player's machine.
public partial class SHud
{
    [Provenance(ProvenanceKind.Decoded, "SHud.LocalizedMessage: the alarm jingles by side")]
    private static readonly AssetRef AlarmJingleMerc = new("Interface.Objectifs.Jingl_alarm_Def");

    private static readonly AssetRef AlarmJingleSpy = new("Interface.Objectifs.Jingl_alarm_Attac");

    private const string GameMessagesClass = "SBase.SGameMessages";

    /// <summary>The SGameMessages switches that go to the game message queue rather than the stack (no alarm jingle).</summary>
    [Provenance(ProvenanceKind.Decoded, "SHud.LocalizedMessage: SetGameMessage cases")]
    private static readonly int[] GameQueueSwitches = [0, 2, 3, 4, 5, 6, 7, 15, 16, 17, 20, 53, 54, 55];
    private const string DeathMessageClass = "SBase.SDeathMessage";

    /// <summary>Volume of the alarm jingles and the score sound.</summary>
    [Provenance(ProvenanceKind.Decoded, "SHud.LocalizedMessage: PlaySound(..., SLOT_Misc, 1.5, , 150.0, , true)")]
    public const float MessageSoundVolume = 1.5f;

    /// <summary>Radius of the alarm jingles and the score sound.</summary>
    [Provenance(ProvenanceKind.Decoded, "SHud.LocalizedMessage: PlaySound(..., SLOT_Misc, 1.5, , 150.0, , true)")]
    public const float MessageSoundRadius = 150f;

    /// <summary>
    /// The sounds of a localized message. SGameMessages: an alarm (<paramref name="OptionalObject"/> an SAlarm) of
    /// priority above 0 plays the side's alarm jingle; then by switch, 2, 6 and 7 play SndScoreLives, 5 the losing
    /// jingle, 10, 27, 33 and 36 the attack jingle, 12 and 34 the defence jingle, 23 the stop jingle (the jingles
    /// through ClientSetMusic with no transition). SDeathMessage of a teammate (not the player) killed: a spy
    /// killed by a merc plays the defence jingle.
    /// </summary>
    [PortOf("uc:SBase.SHud:LocalizedMessage", Spec = "spec/native/audio.md#controller-sound-calls")]
    public void LocalizedMessageSounds(string MessageClass, int Switch, PlayerReplicationInfo? RelatedPRI_1,
        PlayerReplicationInfo? RelatedPRI_2, UObject? OptionalObject)
    {
        if (IsMessageClass(MessageClass, GameMessagesClass))
        {
            if (System.Array.IndexOf(GameQueueSwitches, Switch) < 0 && OptionalObject is SAlarm { MessagePriority: > 0 } &&
                Owner is { } owner)
            {
                bool merc = P?.SPawnType == PawnType.SPAWNDEFENSE;
                owner.PlaySound(merc ? AlarmJingleMerc : AlarmJingleSpy, ESoundSlot.SLOT_Misc, MessageSoundVolume, false,
                    MessageSoundRadius, null, true);
            }

            switch (Switch)
            {
                case 2:
                case 6:
                case 7:
                    Owner?.PlaySound(SndScoreLives, ESoundSlot.SLOT_Misc, MessageSoundVolume, false, MessageSoundRadius, null, true);
                    break;
                case 5:
                    PlayerOwner?.ClientSetMusic(SndYouLose, EMusicTransition.MTRAN_None);
                    break;
                case 10:
                case 27:
                case 33:
                case 36:
                    PlayerOwner?.ClientSetMusic(Snd_ObjectiveAttack, EMusicTransition.MTRAN_None);
                    break;
                case 12:
                case 34:
                    PlayerOwner?.ClientSetMusic(Snd_ObjectiveDef, EMusicTransition.MTRAN_None);
                    break;
                case 23:
                    PlayerOwner?.ClientSetMusic(Snd_ObjectiveStop, EMusicTransition.MTRAN_None);
                    break;
            }

            return;
        }

        if (IsMessageClass(MessageClass, DeathMessageClass) && RelatedPRI_1?.Team is { } killerTeam &&
            RelatedPRI_2?.Team is { } victimTeam && (P?.Controller ?? PlayerOwner)?.PlayerReplicationInfo is { } own &&
            victimTeam.TeamIndex == own.Team?.TeamIndex && !ReferenceEquals(RelatedPRI_2, own) &&
            killerTeam.TeamIndex == 1 && victimTeam.TeamIndex == 0)
        {
            PlayerOwner?.ClientSetMusic(Snd_ObjectiveDef, EMusicTransition.MTRAN_None);
        }
    }

    private static bool IsMessageClass(string path, string expected) =>
        string.Equals(path, expected, System.StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(expected[expected.IndexOf('.', System.StringComparison.Ordinal)..], System.StringComparison.OrdinalIgnoreCase);
}
