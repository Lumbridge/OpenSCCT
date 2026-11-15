using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud.Gadgets;

/// <summary>The original P_HudDEF backpack panels (native driver 0x10b28bc0).</summary>
public sealed class BackpackDriver : IHudAreaDriver
{
    public HudSide? Side => HudSide.Merc;
    public IReadOnlyCollection<string> Areas { get; } = ["Backpack"];
    private SPlayerController? _pc;
    private readonly Dictionary<int, string> _iconNames = [];

    public void Read(SPlayerController? pc, HudSnapshot s) => _pc = pc;

    public void Drive(HudPage page, HudSnapshot s, float dt)
    {
        var area = page.Area("Backpack");
        if (area is null) return;
        var pc = _pc;
        Span<int> charges = stackalloc int[4];
        if (!s.HasPawn || pc?.SBPawn is not { } pawn)
        {
            HudPage.Hide(area);
            return;
        }
        int rows = pc.PlayerReplicationInfo?.Team is STeamInfo team ? Math.Clamp(team.iNBGadgets, 1, 4) : 4;
        var self = area.Find("BackPackSelf");
        var friend = area.Find("BackPackFriend");
        var friend1 = area.Find("BackPackFriend1");
        // The refill interaction shows the share that the teammate carries for this player.
        if (pc.IManager?.Interactions[0] is SBackPackInteraction { bHasFocus: true } interaction &&
            interaction.CanUse(pc) && interaction.Mate is { } mate)
        {
            Show(area); Show(self); HudPage.Hide(friend); HudPage.Hide(friend1);
            bool a = ReferenceEquals(mate.aBackPackOwner_A, pawn);
            Charges(mate, a, charges);
            Fill(self, "self", rows, charges);
            SelfIcons(page, self, pawn);
        }
        else if (pawn.SelectedGadget is SGadBackPack pack)
        {
            Show(area); Show(self);
            Fill(self, "self", rows, pack.aNbChargesGad_Self);
            SelfIcons(page, self, pawn);
            if (pawn.aBackPackOwner_A is not null && pawn.aIDGad_Mate_A0 > 0) Show(friend);
            else HudPage.Hide(friend);
            if (pawn.aBackPackOwner_B is not null && pawn.aIDGad_Mate_B0 > 0) Show(friend1);
            else HudPage.Hide(friend1);
            if (friend?.Visible == true)
            {
                Charges(pawn, true, charges);
                Fill(friend, "friend", rows, charges);
                Icons(page, friend, pc, pawn, true);
            }
            if (friend1?.Visible == true)
            {
                Charges(pawn, false, charges);
                Fill(friend1, "friend", rows, charges);
                Icons(page, friend1, pc, pawn, false);
            }
        }
        else HudPage.Hide(area);
    }

    private static void Show(MagmaNode? node)
    {
        if (node is { Visible: false }) node.Restart();
    }

    private static void Charges(SBasePawn pawn, bool a, Span<int> target)
    {
        ReadOnlySpan<int> values = a
            ? [pawn.aNbChargesGad_Mate_A0, pawn.aNbChargesGad_Mate_A1, pawn.aNbChargesGad_Mate_A2, pawn.aNbChargesGad_Mate_A3]
            : [pawn.aNbChargesGad_Mate_B0, pawn.aNbChargesGad_Mate_B1, pawn.aNbChargesGad_Mate_B2, pawn.aNbChargesGad_Mate_B3];
        values.CopyTo(target);
    }

    private static void Fill(MagmaNode? panel, string prefix, int rows, ReadOnlySpan<int> charges)
    {
        if (panel is null) return;
        for (int i = 0; i < 4; i++)
        {
            string suffix = (i + 1).ToString(CultureInfo.InvariantCulture);
            var text = panel.Widget(prefix + suffix);
            text.Hidden = panel.Widget("Ico" + suffix).Hidden = i >= rows;
            int charge = charges[i];
            text.Text = Math.Max(0, charge).ToString(CultureInfo.InvariantCulture);
            text.Color = charge < 0 ? GadgetMenuDriver.UnavailableColour : charge == 0 ? Colors.Red : Colors.White;
        }
    }

    private static void SelfIcons(HudPage page, MagmaNode? panel, SBasePawn pawn)
    {
        if (panel is null) return;
        int row = 0;
        for (SGadget? gadget = pawn.MyGadget; gadget is not null && row < 4; gadget = gadget.MyGadget)
        {
            if (!SBackPackInteraction.Refillable(gadget)) continue;
            Icon(page, panel, row++, gadget.MagmaIconName);
        }
    }

    private static void Icon(HudPage page, MagmaNode panel, int row, string name)
    {
        if (page.Assets.Package("Options") is not { } options) return;
        int material = options.FindMaterial(name);
        if (material < 0) material = options.FindMaterial("C_Default_2");
        if (material >= 0) panel.Widget("Ico" + (row + 1)).Region = options.Materials[material].Region;
    }

    private void Icons(HudPage page, MagmaNode panel, SPlayerController pc, SBasePawn pawn, bool a)
    {
        if (pc.PlayerReplicationInfo?.Team is not STeamInfo team) return;
        ReadOnlySpan<int> ids = a
            ? [pawn.aIDGad_Mate_A0, pawn.aIDGad_Mate_A1, pawn.aIDGad_Mate_A2, pawn.aIDGad_Mate_A3]
            : [pawn.aIDGad_Mate_B0, pawn.aIDGad_Mate_B1, pawn.aIDGad_Mate_B2, pawn.aIDGad_Mate_B3];
        for (int row = 0; row < 4; row++)
        {
            int id = ids[row];
            if (!_iconNames.TryGetValue(id, out string? name))
                foreach (var cls in team.ClassInventory)
                {
                    if (SGadget.GetUniqueIDByClass(cls) != id || cls.Path is not { } path ||
                        OpenScct.Game.Devices.DeviceClasses.Index.Find(path)?.Create() is not SGadget gadget) continue;
                    _iconNames[id] = name = gadget.MagmaIconName;
                    break;
                }
            Icon(page, panel, row, name ?? "C_Default_2");
        }
    }
}
