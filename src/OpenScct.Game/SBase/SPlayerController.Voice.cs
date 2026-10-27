using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Movement;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController voice eavesdropping (spec/game/voice-chat.md rules 12 to 14): the Hack Comm key and grab
// talk sent to the server, and the voice trace of the merc's scope and the spy's binoculars. The original changes
// mutes at each event (HearVoiceChatter, TalkToMate, MuteVoiceChattersByPlayer, the HackCommUnmuted list); the
// rewrite's relay routes from the state these calls leave on the server's copy of the controller (bSpecialsChat,
// bVoiceComWhenGrabbing, VoiceChatTargetPC, read by OpenScct.Server.Online.SessionVoiceLinks), so only that state is
// kept here.
public partial class SPlayerController
{
    private static readonly Name VNAME_ReplicateSpecialsChatOnServer = new("ReplicateSpecialsChatOnServer");
    private static readonly Name VNAME_ServerSetVoiceChatTarget = new("ServerSetVoiceChatTarget");
    private static readonly Name VNAME_ServerClearVoiceChatTarget = new("ServerClearVoiceChatTarget");
    private static readonly Name VNAME_ServerSetVoiceComWhenGrabbing = new("ServerSetVoiceComWhenGrabbing");

    /// <summary>
    /// Server: a spy holding a merc pressed the Hack Comm key and talks with him (ServerSetVoiceComWhenGrabbing).
    /// The original keeps no flag (it mutes and unmutes at once); the voice links read this one.
    /// </summary>
    public bool bVoiceComWhenGrabbing;

    /// <summary>A state's VoiceChatButtonToggle (s_GrabbingPlayer: grab talk).</summary>
    public interface IVoiceChatButtonToggle
    {
        void VoiceChatButtonToggle(SPlayerController self, bool bActivate);
    }

    /// <summary>VoiceChatButtonToggle through the state.</summary>
    public void eventVoiceChatButtonToggle(bool bActivate)
    {
        if (StateNode is IVoiceChatButtonToggle s)
        {
            s.VoiceChatButtonToggle(this, bActivate);
        }
        else
        {
            VoiceChatButtonToggle(bActivate);
        }
    }

    /// <summary>
    /// The Hack Comm key changed (or a forced update): the state's toggle, then the key to the server. Runs in the
    /// owning machine's PlayerTick after the move.
    /// </summary>
    [Uc("SBase.SPlayerController.UpdateSpecialsChat", Sha1 = "5a7d50aae5bd6bfd238b66c4196d241e8b76c327")]
    [PortOf("uc:SBase.SPlayerController:UpdateSpecialsChat", Spec = "spec/game/voice-chat.md#rules")]
    public void UpdateSpecialsChat(bool bForce = false)
    {
        if (bForce || bSpecialsChat != OldbSpecialsChat)
        {
            eventVoiceChatButtonToggle(bSpecialsChat >= 1);
            ReplicateSpecialsChatOnServer(bSpecialsChat);
        }

        OldbSpecialsChat = bSpecialsChat;
    }

    /// <summary>
    /// The global toggle asks the server to hear (or stop hearing) the tagged enemies (ServerHearSpybullets). The
    /// server's routing hears them for as long as its copy of the key is down (rule 13), so the key itself, sent by
    /// <see cref="ReplicateSpecialsChatOnServer"/>, is all it needs.
    /// </summary>
    [Uc("SBase.SPlayerController.VoiceChatButtonToggle", Sha1 = "dfe87d15a2ce346d4ff7d5e3a7ab34f29af8209a")]
    [PortOf("uc:SBase.SPlayerController:VoiceChatButtonToggle", Spec = "spec/game/voice-chat.md#rules")]
    [PortOf("uc:SBase.SPlayerController:ServerHearSpybullets", Spec = "spec/game/voice-chat.md#rules")]
    public void VoiceChatButtonToggle(bool bActivate)
    {
    }

    /// <summary>Owner to server: the Hack Comm key's state.</summary>
    [Uc("SBase.SPlayerController.ReplicateSpecialsChatOnServer", Sha1 = "32ea1fbfb1ff2fcda704587b677941eda60e433e")]
    [PortOf("uc:SBase.SPlayerController:ReplicateSpecialsChatOnServer", Spec = "spec/game/voice-chat.md#rules")]
    public void ReplicateSpecialsChatOnServer(byte val)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(VNAME_ReplicateSpecialsChatOnServer) { Int1 = val });
            return;
        }

        bSpecialsChat = val;
    }

    /// <summary>Voice routing happens on a dedicated or listen server with a game.</summary>
    [Uc("SBase.SPlayerController.CanRouteVoice", Sha1 = "bd282dd30c892c01c36cef346dd273f8aca93dc8")]
    [PortOf("uc:SBase.SPlayerController:CanRouteVoice", Spec = "spec/game/voice-chat.md#network")]
    public bool CanRouteVoice() =>
        Level.Game is not null && Level.NetMode is ENetMode.NM_DedicatedServer or ENetMode.NM_ListenServer;

    /// <summary>
    /// Owner to server: the trace found an enemy, who is now heard one way (rule 12). The server also checks that
    /// the pawn is on the other side and within the trace's reach, so a client cannot pick any enemy to listen to.
    /// </summary>
    [Uc("SBase.SPlayerController.ServerSetVoiceChatTarget", Sha1 = "f4b887dd70a54577811fd25cb5976a9685f045f3")]
    [PortOf("uc:SBase.SPlayerController:ServerSetVoiceChatTarget", Spec = "spec/game/voice-chat.md#rules")]
    public void ServerSetVoiceChatTarget(Pawn? P)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(VNAME_ServerSetVoiceChatTarget) { Actor1 = P });
            return;
        }

        ServerClearVoiceChatTarget();
        if (P?.Controller is not SPlayerController pc || !CanRouteVoice() || P is not SBasePawn target ||
            SBPawn is not { } own || target.SPawnType == own.SPawnType ||
            (target.Location - own.Location).Size() > K.SpyBinoVoiceRange + K.VoiceTargetServerSlack)
        {
            return;
        }

        VoiceChatTargetPC = pc;
    }

    /// <summary>Owner to server: the voice target is lost.</summary>
    [Uc("SBase.SPlayerController.ServerClearVoiceChatTarget", Sha1 = "b7edded6b58fe6aba6d84098a58ec335871a3964")]
    [PortOf("uc:SBase.SPlayerController:ServerClearVoiceChatTarget", Spec = "spec/game/voice-chat.md#rules")]
    public void ServerClearVoiceChatTarget()
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(VNAME_ServerClearVoiceChatTarget));
            return;
        }

        VoiceChatTargetPC = null;
    }

    /// <summary>
    /// Owner to server: the holding spy's Hack Comm key went down or up (rule 14): down, he and the merc he holds
    /// talk both ways and his own team stops hearing him; up, both go back.
    /// </summary>
    [Uc("SBase.SPlayerController.ServerSetVoiceComWhenGrabbing", Sha1 = "59ace50b3d48842f45d3ae29ee835f69a9f30742")]
    [PortOf("uc:SBase.SPlayerController:ServerSetVoiceComWhenGrabbing", Spec = "spec/game/voice-chat.md#rules")]
    public void ServerSetVoiceComWhenGrabbing(bool bActivateCom)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(VNAME_ServerSetVoiceComWhenGrabbing) { Bool1 = bActivateCom });
            return;
        }

        bVoiceComWhenGrabbing = bActivateCom && SPawnType == PawnType.SPAWNOFFENSE && SBPawn?.m_AttackTarget is not null;
    }

    /// <summary>
    /// The voice trace of the merc's scope (s_Binoculars_Def) and the spy's binoculars (s_Binoculars_Att), rule 12:
    /// once per whole second of level time a line along the view from 50 uu ahead of the eyes to AudioZoomFactor x
    /// 6000. An enemy hit (a spy for the scope, a merc for the binoculars) with enemy comms open becomes the voice
    /// target, with the hacking sound; nothing, or anything else, clears it. The original also sets the audio zoom
    /// here; the game's sound reads AudioZoomFactor itself (SetAudioZoom, docs/VOICE.md).
    /// </summary>
    internal void BinocularVoiceTrace(SBasePawn pawn, FVector X, bool bSpyTarget)
    {
        int second = SvmMath.Trunc(Level.TimeSeconds);
        if (iDateCheckBinoculars == second)
        {
            return;
        }

        iDateCheckBinoculars = second;
        FVector eye = pawn.Location + CombatServices.Ports(this).EyePosition(pawn);
        Actor? hit = MovementNatives.Trace(this, out _, out _, eye + X * (AudioZoomFactor * K.SpyBinoVoiceRange),
            eye + X * K.SpyBinoVoiceStart, true);
        if (hit is null)
        {
            if (VoiceChatTarget is not null)
            {
                ClearBinocularVoiceTarget();
            }

            return;
        }

        if (ReferenceEquals(hit, VoiceChatTarget))
        {
            return;
        }

        if (VoiceChatTarget is not null)
        {
            ClearBinocularVoiceTarget();
        }

        bool enemy = bSpyTarget ? hit is SPawnAttaque : hit is SPAWNDEFENSE;
        if (enemy && CanHackEnemyComs())
        {
            ClientPlaySound(Snd_VoiceHacking);
            VoiceChatTarget = hit;
            ServerSetVoiceChatTarget(hit as Pawn);
            bVoiceChatTargetSent = true;
        }
    }

    /// <summary>The trace's voice target dropped: the server told when it had been sent.</summary>
    internal void ClearBinocularVoiceTarget()
    {
        if (bVoiceChatTargetSent)
        {
            ServerClearVoiceChatTarget();
            bVoiceChatTargetSent = false;
        }

        VoiceChatTarget = null;
    }
}
