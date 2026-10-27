using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SMessageTrigger: on an event, a line in every player's zone message area, MessageSpy for spies and
/// MessageMerc for mercs, for DisplayTime seconds at Priority (spec/devices/message-trigger.md). An untrigger shows
/// the same line for the minimum time.
/// </summary>
public partial class SMessageTrigger
{
    private static readonly Name NAME_DisplayMessage = new("DisplayMessage");

    [Uc("SBase.SMessageTrigger.PostBeginPlay", Sha1 = "12c2aa385057837ca84d279648175aa2677b2646")]
    [PortOf("uc:SBase.SMessageTrigger:PostBeginPlay", Spec = "spec/devices/message-trigger.md#rules")]
    protected override void PostBeginPlay() => SetDrawType(EDrawType.DT_None);

    /// <summary>
    /// The local player's HUD gets the line of its side: its time is DisplayTime, or 0 (the HUD minimum) when
    /// <paramref name="bRemove"/>. A broadcast function: the server sends it to every client with a channel for the
    /// trigger and also runs it (a listen server's own player).
    /// </summary>
    [Uc("SBase.SMessageTrigger.DisplayMessage", Sha1 = "428a561edf1e367b14ca9608270535242150ea89")]
    [PortOf("uc:SBase.SMessageTrigger:DisplayMessage", Spec = "spec/devices/message-trigger.md#rules")]
    public void DisplayMessage(bool bRemove)
    {
        if (Level.GetLocalPlayerController() is not SPlayerController { SBPawn: { } pawn } pc || pc.myHUD is not SHud hud)
        {
            return;
        }

        string text = pawn is SPawnAttaque ? MessageSpy : MessageMerc;
        hud.AddInfoMessageToStack(text, bRemove ? 0 : DisplayTime, Priority);
    }

    /// <summary>DisplayMessage on every machine: sent where the function's replication says, run here when it says.</summary>
    private void CallDisplayMessage(bool bRemove)
    {
        IDeviceNet net = XLevel.GetService<IDeviceNet>() ?? DeviceNetLocal.Instance;
        if (Role < ENetRole.ROLE_Authority || net.Call(this, new GameCall(NAME_DisplayMessage) { Bool = bRemove }))
        {
            DisplayMessage(bRemove);
        }
    }

    [Uc("SBase.SMessageTrigger.Trigger", Sha1 = "1e36a696958cc9033b765542b584cb0151ec7711")]
    [PortOf("uc:SBase.SMessageTrigger:Trigger", Spec = "spec/devices/message-trigger.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator) => CallDisplayMessage(false);

    [Uc("SBase.SMessageTrigger.UnTrigger", Sha1 = "75d4cf6c307aec03887e38b80e8e31a922db9ce5")]
    [PortOf("uc:SBase.SMessageTrigger:UnTrigger", Spec = "spec/devices/message-trigger.md#rules")]
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator) => CallDisplayMessage(true);
}
