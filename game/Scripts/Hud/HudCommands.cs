using System;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Controls;

namespace OpenScctGame.Hud;

/// <summary>
/// The HUD's own exec commands on the keys of the player's layout: ShowConnectedPlayers (F10), DisplaySessionStatus
/// (F8) and SwitchEnhancedReality (F9). These execs only touch the local HUD, so the HUD runs them; the key press does
/// not reach the player's input.
/// </summary>
public partial class HudCommands : Node
{
    private static readonly Name NAME_s_Dead = new("s_Dead");

    public HudCommands()
    {
        Name = "HudCommands";
    }

    public HudLayer? Hud { get; set; }

    /// <summary>True while something else owns the keys (the chat line is open).</summary>
    public Func<bool>? Blocked { get; set; }

    public GameStatusPage? Status { get; set; }

    /// <summary>The play input's layout, and the input to stop while a menu is open.</summary>
    public PlayInput? PlayInput { get; set; }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } || PlayInput?.Bindings is not { } bindings ||
            GodotKeys.ToIk(@event) is not { } ik || (Status?.IsOpen ?? false) || (Blocked?.Invoke() ?? false) || !PlayInput.Enabled)
        {
            return;
        }

        foreach (KeyAction a in bindings.Actions(ik))
        {
            if (a.Kind == KeyActionKind.Command && Run(a.Name, ik))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
        }
    }

    /// <summary>The first key (IK name) whose binding runs <paramref name="action"/> (a command or a button byte), or null.</summary>
    public static string? KeyOf(InputBindings? bindings, string action)
    {
        if (bindings is null)
        {
            return null;
        }

        foreach (string key in bindings.Layout.Bindings.Keys)
        {
            foreach (KeyAction a in bindings.Actions(key))
            {
                if (a.Kind != KeyActionKind.Axis && string.Equals(a.Name, action, StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }
        }

        return null;
    }

    /// <summary>Runs one of the HUD's commands; false for any other command.</summary>
    public bool Run(string command, string? key = null)
    {
        switch (command.ToUpperInvariant())
        {
            case "SHOWCONNECTEDPLAYERS":
                Hud?.ShowConnectedPlayers();
                return true;
            case "DISPLAYSESSIONSTATUS":
                if (Status is not null)
                {
                    Status.ExitKey = key;
                    Status.Open();
                }

                return true;
            case "SWITCHENHANCEDREALITY":
                if (Hud?.Controller?.Invoke() is { } pc)
                {
                    SwitchEnhancedReality(pc, Hud.Options);
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// <c>SPlayerController.SwitchEnhancedReality</c>: the HUD's Enhanced Reality level cycles 0, 1; a spy gets
    /// "Display off" / "Display all objective positions" on the stack, a merc "Display intrusions" / "Display all
    /// objectives and intrusions" on the state area. Dead and spectating controllers ignore it.
    /// </summary>
    [Uc("SBase.SPlayerController.SwitchEnhancedReality", Sha1 = "1d5e8c32a254f0fc4396bd6eb0d6973af2a12b58")]
    [PortOf("uc:SBase.SPlayerController:SwitchEnhancedReality", Spec = "spec/hud/counters-and-prompts.md#rules")]
    public static void SwitchEnhancedReality(SPlayerController pc, HudOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pc);
        if (pc.myHUD is not SHud hud || pc.IsInState(NAME_s_Dead) || HudK.IsSpectating(pc))
        {
            return;
        }

        // The player now decides; the slice's forced markers give way to the HUD level.
        if (options is not null)
        {
            options.EnhancedReality = false;
        }

        hud.EnhancedRealityLevel++;
        if (hud.EnhancedRealityLevel > 1)
        {
            hud.EnhancedRealityLevel = 0;
        }

        bool spy = (pc.SBPawn?.SPawnType ?? pc.SPawnType) != PawnType.SPAWNDEFENSE;
        if (spy)
        {
            HudMessages.AddMessageToStack(hud, hud.EnhancedRealityLevel == 0 ? pc.EROff : pc.ERAllObjectives, 3, 0);
        }
        else
        {
            HudMessages.AddMessageToAuxiliaryStack(hud, hud.EnhancedRealityLevel == 0 ? pc.ERAlarmsOnly : pc.ERAlarmsAndObjectives, 3, 0);
        }
    }
}
