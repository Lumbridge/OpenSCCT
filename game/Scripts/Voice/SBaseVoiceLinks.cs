using OpenScct.Core.Engine;
using OpenScct.Net.Voice;
using OpenScct.Server.Online;

namespace OpenScctGame.Voice;

/// <summary>
/// The stock special cases of voice routing (spec/game/voice-chat.md rules 8 to 14). The rules live with the online
/// session (<see cref="SessionVoiceLinks"/>), which every online server's relay uses; this name is kept for the
/// loopback voice smoke and <see cref="VoiceHosting"/>.
/// </summary>
public sealed class SBaseVoiceLinks : IVoiceLinkSource
{
    private readonly SessionVoiceLinks _links = new();

    public void ReadLinks(PlayerController controller, ref VoiceMember member) => _links.ReadLinks(controller, ref member);
}
