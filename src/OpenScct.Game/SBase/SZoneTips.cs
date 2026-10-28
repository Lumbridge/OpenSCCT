using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SZoneTips: an invisible cylinder that shows a map hint on the HUD while the player stands in it, in
/// offline play only (spec/devices/zone-tips.md). The original's offline game is standalone; the rewrite's is a listen
/// server with the player as a client, so the player's own machine also shows them there
/// (<see cref="IProfileOptions.OfflineSession"/>).
/// </summary>
public partial class SZoneTips
{
    /// <summary>Offline, a player pawn with a HUD entering shows its side's hint.</summary>
    [Uc("SBase.SZoneTips.Touch", Sha1 = "4c0b9f2930f76c930de50b70891163bb8cc611d5")]
    [PortOf("uc:SBase.SZoneTips:Touch", Spec = "spec/devices/zone-tips.md#rules")]
    protected override void Touch(Actor Other)
    {
        if (Other is Pawn { Controller: SPlayerController pc } && ShowsHintsFor(pc) && pc.myHUD is SHud)
        {
            TargetController = pc;
            AddMessage(TargetController);
        }
    }

    /// <summary>Offline, the hint goes when its viewer leaves (unless it stays permanently).</summary>
    [Uc("SBase.SZoneTips.UnTouch", Sha1 = "f0af2c24757e30e01b873be1b23630757ce19f2e")]
    [PortOf("uc:SBase.SZoneTips:UnTouch", Spec = "spec/devices/zone-tips.md#rules")]
    protected override void UnTouch(Actor Other)
    {
        if (Other is Pawn { Controller: SPlayerController pc } && ShowsHintsFor(pc) && ReferenceEquals(pc, TargetController))
        {
            RemoveMessage(TargetController);
            TargetController = null;
        }
    }

    /// <summary>
    /// A standalone game shows the hints (the original's test); so does the player's own machine in an offline
    /// session of the rewrite.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "OpenSCCT: offline play is a listen server, so the standalone test is widened to the offline session's local player")]
    private bool ShowsHintsFor(SPlayerController pc) =>
        Level.NetMode == ENetMode.NM_Standalone || (pc.LocalProfileOptions()?.OfflineSession ?? false);

    /// <summary>Shows the spy or merc text (two parts joined by a space), when in-game help allows it.</summary>
    [Uc("SBase.SZoneTips.AddMessage", Sha1 = "450ee1800f4dd27659c9e328ef63ae09edefe1c0")]
    [PortOf("uc:SBase.SZoneTips:AddMessage", Spec = "spec/devices/zone-tips.md#rules")]
    public void AddMessage(SPlayerController _Ctrl)
    {
        IDeviceHooks? hooks = XLevel.GetService<IDeviceHooks>();
        if (hooks is null || (bShowOnlyWithIngameHelp && !SHelpPath.DisplayHelpInGame(XLevel)))
        {
            return;
        }

        string[] text = _Ctrl.SPawnType == PawnType.SPAWNOFFENSE ? sMessage_ATT : sMessage_DEF;
        hooks.AddTutoMapMessage(_Ctrl, text[0] + " " + text[1]);
    }

    /// <summary>Clears the hint unless it stays permanently.</summary>
    [Uc("SBase.SZoneTips.RemoveMessage", Sha1 = "169b134852cde8358a53cfdd0c0547f96852586e")]
    [PortOf("uc:SBase.SZoneTips:RemoveMessage", Spec = "spec/devices/zone-tips.md#rules")]
    public void RemoveMessage(SPlayerController _Ctrl)
    {
        if (!bStayPermanently)
        {
            XLevel.GetService<IDeviceHooks>()?.AddTutoMapMessage(_Ctrl, string.Empty);
        }
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        TargetController = Live(TargetController);
    }
}
