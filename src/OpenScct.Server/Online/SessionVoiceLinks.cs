using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net.Voice;

namespace OpenScct.Server.Online;

/// <summary>
/// The stock special cases of voice routing read from the SBase port (spec/game/voice-chat.md rules 8 to 14): the lobby
/// team-only switch (<c>bTeamVoiceOnly</c>, the lobby's "Restrict Mic to Team"), the merc's binocular voice target
/// (<c>VoiceChatTargetPC</c>), the Hack Comm key (<c>bSpecialsChat</c> held while not grabbing) and spy bullet tags
/// (<c>bSpyBulletOn</c>, <c>iTeamSpying</c>), grab talk, and downed chat partners (<c>PCChattingWith</c>). Every online
/// session's voice relay reads its players through it; install it on <see cref="NetDriverVoiceDirectory.Links"/>.
/// <para>Grab talk: the original mutes and unmutes directly when the voice key changes during a grab
/// (ServerSetVoiceComWhenGrabbing) and keeps no flag. Until the grab port records one, a spy in s_GrabbingPlayer with
/// the Hack Comm key held counts as talking to the merc he holds, which is what that key does in that state.</para>
/// </summary>
public sealed class SessionVoiceLinks : IVoiceLinkSource
{
    private static readonly Name NAME_s_GrabbingPlayer = new("s_GrabbingPlayer");

    public void ReadLinks(PlayerController controller, ref VoiceMember member)
    {
        if (controller is not SPlayerController pc)
        {
            return;
        }

        member.TeamVoiceOnly = pc.bTeamVoiceOnly;
        member.VoiceTarget = TalkerOf(pc.VoiceChatTargetPC);
        bool grabbing = pc.IsInState(NAME_s_GrabbingPlayer);
        bool key = pc.bSpecialsChat > 0;
        member.HackCommKey = key && !grabbing;
        if (grabbing && key && pc.SPawnType == PawnType.SPAWNOFFENSE && pc.SBPawn?.m_AttackTarget?.Controller is SPlayerController held)
        {
            member.GrabTalkWith = TalkerOf(held);
        }

        if (pc.SBPawn is { bSpyBulletOn: true } pawn && pawn.iTeamSpying >= 0f)
        {
            member.SpiedByTeam = (int)pawn.iTeamSpying;
        }

        member.DownedChat0 = TalkerOf(pc.PCChattingWith[0]);
        member.DownedChat1 = TalkerOf(pc.PCChattingWith[1]);
        member.DownedChat2 = TalkerOf(pc.PCChattingWith[2]);
    }

    private static ushort TalkerOf(PlayerController? pc) => pc?.PlayerReplicationInfo is { } pri ? VoiceK.TalkerOf(pri.PlayerID) : (ushort)0;
}
