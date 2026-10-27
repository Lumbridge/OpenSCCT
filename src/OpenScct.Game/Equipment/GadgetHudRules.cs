using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>The full gadget page that replaces the player page (spec/equipment/gadget-hud.md rule 5).</summary>
public enum GadgetView
{
    None,
    StickyCam,
    CamNetwork,
}

/// <summary>How a gadget menu line is drawn (gadget-hud.md rules 15 and 17).</summary>
public enum GadgetMenuLineLook
{
    /// <summary>Not selected: (200,200,200,255).</summary>
    Normal,

    /// <summary>Selected: white, with the arrow beside it.</summary>
    Selected,

    /// <summary>Not available: (128,128,128,128).</summary>
    Unavailable,

    /// <summary>The text is set and the colour and arrow are left as they were (the sticky menu's line 1 without a cam).</summary>
    Untouched,
}

/// <summary>One line of a gadget menu.</summary>
public readonly record struct GadgetMenuLine(string Text, GadgetMenuLineLook Look);

/// <summary>A gadget menu while it is up: its lines and the selected one.</summary>
public sealed record GadgetMenu(IReadOnlyList<GadgetMenuLine> Lines, int Selected);

/// <summary>The gas mask overlay's inputs (gadget-hud.md rules 18 to 21).</summary>
public readonly record struct GasMaskView(bool Shown, int AirSeconds, bool Worn)
{
    /// <summary>The air gauge frame: 2 x (30 - t), 0 full and 60 empty.</summary>
    public int GaugeFrame => SvmMath.Clamp(2 * (K.GasMaskGaugeSeconds - AirSeconds), 0, 2 * K.GasMaskGaugeSeconds);

    /// <summary>Below this many whole seconds of air the fast breathing shows (once, see the rule).</summary>
    public bool LowAir => AirSeconds < K.GasMaskFastBreathSeconds;
}

/// <summary>The heart sensor cone (gadget-hud.md rules 22 to 26).</summary>
public enum HeartCone
{
    /// <summary>Not the selected gadget, or no gadgets for the team: both areas hidden.</summary>
    Hidden,

    /// <summary>Selected and holstered, or not scanning: the idle cone at frame 0, stopped.</summary>
    Idle,

    /// <summary>Scanning with nothing in the cone: the idle cone playing.</summary>
    Scanning,

    /// <summary>Something in the cone: the red cone playing.</summary>
    Detected,
}

/// <summary>
/// What the client's gadget HUD views show, read from the local controller (spec/equipment/gadget-hud.md). Godot-free:
/// the HUD drives the Magma areas from these answers.
/// </summary>
public static class GadgetHudRules
{
    /// <summary>Rule 5: the sticky page for a spy whose HUD is in the sticky state, the camera page for a merc.</summary>
    [PortOf("uc:SBase.SHud:IsInSticky", Spec = "spec/equipment/gadget-hud.md#page-switch")]
    [Provenance(ProvenanceKind.Decoded, "spy page tick 0x10b2ccec tests IsInSticky; merc page tick 0x10b2743f tests IsInCamNetwork")]
    public static GadgetView ViewOf(SPlayerController? PC)
    {
        if (PC?.myHUD is not SHud hud)
        {
            return GadgetView.None;
        }

        bool merc = PC.Pawn is SPAWNDEFENSE || (PC.Pawn is null && PC.PlayerReplicationInfo?.Team?.TeamIndex == 1);
        if (!merc && hud.IsInSticky())
        {
            return GadgetView.StickyCam;
        }

        return merc && hud.IsInCamNetwork() ? GadgetView.CamNetwork : GadgetView.None;
    }

    /// <summary>
    /// Rules 14 and 15: the sticky cam menu while the controller is in s_StickyCam_MenuInteraction with a sticky cam
    /// gadget selected, else null.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "sticky menu show 0x10b2cdef and update 0x10b2d1c0")]
    public static GadgetMenu? StickyCamMenu(SPlayerController? PC)
    {
        if (PC is null || !PC.IsInState(GadgetStates.s_StickyCam_MenuInteraction) ||
            PC.SBPawn is not { SelectedGadget: SGadStickyCam gad } pawn)
        {
            return null;
        }

        var lines = new GadgetMenuLine[3];
        for (int i = 0; i < lines.Length; i++)
        {
            string text = gad.TextMenu[i];
            GadgetMenuLineLook look;
            if (i == 1 && pawn.UsedObject is null)
            {
                look = GadgetMenuLineLook.Untouched;
            }
            else if (i != 2 && gad.iCanUse[SvmMath.Min(i, gad.iCanUse.Length - 1)] == 0)
            {
                look = GadgetMenuLineLook.Unavailable;
            }
            else
            {
                look = i == gad.iCurrentSelectedMenu ? GadgetMenuLineLook.Selected : GadgetMenuLineLook.Normal;
            }

            lines[i] = new GadgetMenuLine(text, look);
        }

        return new GadgetMenu(lines, gad.iCurrentSelectedMenu);
    }

    /// <summary>
    /// Rules 16 and 17: the mine menu while the controller is in s_PutMineOnWall_InteractionMenu with the mine
    /// launcher selected, else null.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "mine menu 0x10b27575 / 0x10b27770: count NbIT, MineName, CanUse, iCurrentMineType")]
    [Provenance(ProvenanceKind.Estimated, "Which unavailable entries are greyed: every CanUse == 0 entry but the last (gadget-hud.md open question)")]
    public static GadgetMenu? MineMenu(SPlayerController? PC)
    {
        if (PC is null || PC.GetStateName() != GadgetStates.s_PutMineOnWall_InteractionMenu ||
            PC.SBPawn is not { SelectedGadget: SGadMultiMinesLauncher gad })
        {
            return null;
        }

        int count = SvmMath.Min(gad.NbIT, gad.MineName.Length);
        var lines = new GadgetMenuLine[count];
        for (int i = 0; i < count; i++)
        {
            GadgetMenuLineLook look = i == gad.iCurrentMineType ? GadgetMenuLineLook.Selected
                : i < count - 1 && i < gad.CanUse.Length && gad.CanUse[i] == 0 ? GadgetMenuLineLook.Unavailable
                : GadgetMenuLineLook.Normal;
            lines[i] = new GadgetMenuLine(gad.MineName[i], look);
        }

        return new GadgetMenu(lines, gad.iCurrentMineType);
    }

    /// <summary>
    /// Rule 10: the camera name text of the camera page, or null to leave the text as it is.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "camera page frame 0x10b337a0")]
    public static string? CamNetworkName(SPlayerController? PC)
    {
        if (PC?.Pawn is not SBasePawn { UsedObject: { } used } pawn)
        {
            return null;
        }

        if (used is not SCamNetwork cam)
        {
            return "";
        }

        if (!ReferenceEquals(cam.aUsedByPawn, pawn))
        {
            return cam.tCamAlreadyInUse;
        }

        return cam.CamName.Length > 0 ? cam.CamName : null;
    }

    /// <summary>Rule 11: an objective of the HUD's draw list with a running trigger time (a spy is hacking).</summary>
    [Provenance(ProvenanceKind.Decoded, "camera page alert 0x10b335c0: SObjective in ActorToDraw with MinTriggerTime != 0")]
    public static bool CamNetworkAlert(SHud? Hud)
    {
        if (Hud is null)
        {
            return false;
        }

        foreach (Actor? a in Hud.ActorToDraw)
        {
            if (a is SObjective { MinTriggerTime: not 0f })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Rules 18 to 21: the gas mask overlay's inputs for the local merc.</summary>
    [Provenance(ProvenanceKind.Decoded, "gas mask page method 0x10b27ec0")]
    public static GasMaskView GasMask(SPlayerController? PC)
    {
        if (PC?.SBPawn is not { SelectedGadget: SGadGasMask mask, UsedObject: null } pawn)
        {
            return default;
        }

        return new GasMaskView(true, SvmMath.Trunc(mask.MaskDuration), pawn.bGasMask);
    }

    /// <summary>Rules 22 to 26: the heart sensor cone for the local spy.</summary>
    [Provenance(ProvenanceKind.Decoded, "heart cone 0x10b2d7a0: SelectedGadget, iNBGadgets, WeaponLoaded, StateOfDetect")]
    public static HeartCone Heart(SPlayerController? PC)
    {
        if (PC?.SBPawn is not { SelectedGadget: SGadHeartBeatSensor sensor } pawn || PawnInventory.TeamSlots(pawn) <= 0)
        {
            return HeartCone.Hidden;
        }

        if (pawn.WeaponLoaded == 0)
        {
            return HeartCone.Idle;
        }

        return sensor.StateOfDetect switch
        {
            1 => HeartCone.Detected,
            2 => HeartCone.Scanning,
            _ => HeartCone.Idle,
        };
    }

    /// <summary>The heart sensor's detected sound may play: more than 1.5 s since the last one (rule 24).</summary>
    public static bool HeartDetectedSoundDue(float Now, float LastPlayed) => LastPlayed + K.HeartDetectedSoundSpacing < Now;
}
