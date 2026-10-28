using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net.Voice;

namespace OpenScct.Server.Online;

/// <summary>
/// The stock special cases of voice routing read from the SBase port (spec/game/voice-chat.md rules 8 to 14): the lobby
/// team-only switch (<c>bTeamVoiceOnly</c>, the lobby's "Restrict Mic to Team"), the merc's binocular voice target
/// (<c>VoiceChatTargetPC</c>, set by the scope's and the binoculars' voice trace), the Hack Comm key (<c>bSpecialsChat</c>,
/// sent by the owner with ReplicateSpecialsChatOnServer, held while not grabbing) and spy bullet tags (<c>bSpyBulletOn</c>,
/// <c>iTeamSpying</c>), grab talk (<c>bVoiceComWhenGrabbing</c>, ServerSetVoiceComWhenGrabbing), and downed chat partners
/// (<c>PCChattingWith</c>). Every online session's voice relay reads its players through it; install it on
/// <see cref="NetDriverVoiceDirectory.Links"/>. All of it is the server's copy of the controller, so a client only ever
/// asks; the server decides.
/// </summary>
public sealed class SessionVoiceLinks : IVoiceLinkSource
{
    private static readonly Name NAME_s_GrabbingPlayer = new("s_GrabbingPlayer");

    // Every state of the grabbing side, from the start of a grab (a ledge grab too) to its release. The original stops
    // Hack Comm as soon as a grab begins (ServerHearSpybullets(true) in AskServerToGrabPawn and
    // AskServerToGrabPawnFromLedge) and gives it back at the release (AskServerToUnGrabPawn).
    private static readonly Name[] GrabbingStates =
    [
        new("s_GrabbingPlayer_Begin"), new("s_GrabbingPlayer_RealBegin"), new("s_Grabbing_GotoPosition"),
        new("s_Grabbing_UncrouchForGrab"), new("s_Grabbing_GoToPosToFreeGuy"), NAME_s_GrabbingPlayer,
        new("s_GrabbingFromLedge_Begin"), new("s_GrabbingFromLedge_Go"),
    ];

    public void ReadLinks(PlayerController controller, ref VoiceMember member)
    {
        if (controller is not SPlayerController pc)
        {
            return;
        }

        member.TeamVoiceOnly = pc.bTeamVoiceOnly;
        member.VoiceTarget = TalkerOf(pc.VoiceChatTargetPC);
        bool grabbing = pc.IsInState(NAME_s_GrabbingPlayer);
        member.HackCommKey = pc.bSpecialsChat > 0 && !IsGrabbing(pc);
        if (grabbing && pc.bVoiceComWhenGrabbing && pc.SPawnType == PawnType.SPAWNOFFENSE && pc.SBPawn?.m_AttackTarget?.Controller is SPlayerController held)
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

    private static bool IsGrabbing(SPlayerController pc)
    {
        foreach (Name state in GrabbingStates)
        {
            if (pc.IsInState(state))
            {
                return true;
            }
        }

        return false;
    }

    private static ushort TalkerOf(PlayerController? pc) => pc?.PlayerReplicationInfo is { } pri ? VoiceK.TalkerOf(pri.PlayerID) : (ushort)0;
}
