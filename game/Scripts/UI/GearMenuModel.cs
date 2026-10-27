using System;
using System.Collections.Generic;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Options;

namespace OpenScctGame.UI;

/// <summary>
/// The gear picker's rules (an OpenSCCT addition, spec/additions/gear-change.md, laid out as the original's
/// <c>Edit_Profile</c> and <c>Edit_Gadgets</c> pages): the player's side's gadget slots, each set from the team's
/// options the host allows (a gadget another slot holds swaps over), and the merc's weapon. Godot-free:
/// <c>GearMenu</c> draws it. Saving writes the choice into <see cref="LoadoutSettings"/>; the server decides when it
/// goes on.
/// </summary>
public sealed class GearMenuModel
{
    [Provenance(ProvenanceKind.Decoded, "GUI.Edit_Profile defaults TitlePage, ButtonA, ButtonB, ButtonY")]
    public const string Title = "Equipment", Ok = "OK", Cancel = "Cancel", ResetCaption = "Reset";

    [Provenance(ProvenanceKind.Decoded, "GUI.Edit_Gadgets defaults TitlePage")]
    public const string GadgetsTitle = "Gadgets";

    [Provenance(ProvenanceKind.Decoded, "GUI.Edit_Gadgets defaults Button1..8 Caption, QuickUse Caption")]
    public const string Equipped = "Equipped", QuickUse = "Quick use";

    [Provenance(ProvenanceKind.Decoded, "GUI.Edit_Profile defaults QuickUseR, QuickUseL Caption")]
    public const string QuickUse1 = "Quick Use 1", QuickUse2 = "Quick Use 2";

    public const string Empty = "Empty";

    public static readonly string MatchRule =
        $"New gear goes on now if you spawned less than {GearK.SwapWindowSeconds:0} s ago and have not used anything; " +
        "otherwise it goes on at your next spawn.";

    public const string HubRule = "New gear goes on now; you can change it every 10 s.";

    private readonly IReadOnlyList<string> _options;
    private readonly byte _allowed;

    public GearMenuModel(bool spy, int slots, IReadOnlyList<string> options, byte allowedMask, IReadOnlyList<string> weapons,
        LoadoutSettings saved, bool hub)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(weapons);
        ArgumentNullException.ThrowIfNull(saved);
        Spy = spy;
        Slots = Math.Clamp(slots, 0, LoadoutSettings.Slots);
        _options = options;
        _allowed = allowedMask;
        Weapons = weapons;
        Hub = hub;
        Gadgets = (string[])saved.GadgetsOf(spy).Clone();
        ChosenWeapon = spy ? "" : saved.MercWeapon;
        Normalize();
    }

    public bool Spy { get; }

    public bool Hub { get; }

    /// <summary>Gadget slots the host gives this side.</summary>
    public int Slots { get; }

    /// <summary>The chosen class path of each slot ("" for empty).</summary>
    public string[] Gadgets { get; }

    /// <summary>The side's weapons (empty for spies: they only have the Shocker).</summary>
    public IReadOnlyList<string> Weapons { get; }

    public string ChosenWeapon { get; private set; }

    public string Rule => Hub ? HubRule : MatchRule;

    /// <summary>The side's portrait (<c>STeamInfo.MaterialOfMesh[0]</c>, e.g. GUIContent.New_Menu.MN_ATTperso), or "".</summary>
    public string Portrait { get; init; } = "";

    /// <summary>The picker for the player's current side, or null before the player has a team.</summary>
    public static GearMenuModel? For(SPlayerController? pc, LoadoutSettings saved, bool hub)
    {
        if (pc?.PlayerReplicationInfo?.Team is not STeamInfo team)
        {
            return null;
        }

        bool spy = team is STeamAttaquant;
        var options = new List<string>();
        for (int i = 0; i < team.ClassInventory.Count && i < 8; i++)
        {
            options.Add(team.ClassInventory[i].Path ?? "");
        }

        var weapons = new List<string>();
        if (!spy)
        {
            foreach (ClassRef w in team.ClassWeapon)
            {
                weapons.Add(w.Path ?? "");
            }
        }

        return new GearMenuModel(spy, team.iNBGadgets, options, team.bDisallowedInv, weapons, saved, hub)
        {
            Portrait = team.MaterialOfMesh.Count > 0 ? team.MaterialOfMesh[0].Path ?? "" : "",
        };
    }

    /// <summary>The name a class path shows (its localized ItemName), or "Empty".</summary>
    public static string NameOf(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Empty;
        }

        int dot = path.LastIndexOf('.');
        string fallback = dot >= 0 ? path[(dot + 1)..] : path;
        return UcText.Get(path, "ItemName", fallback) is { Length: > 0 } name ? name : fallback;
    }

    /// <summary>The HUD line for the server's answer.</summary>
    public static string ResultText(GearChangeResult result, float waitSeconds) => result switch
    {
        GearChangeResult.Now => "Gear changed.",
        GearChangeResult.Cooldown => $"Wait {Math.Max(1, (int)Math.Ceiling(waitSeconds))} s before changing gear again.",
        _ => "Gear will change on your next spawn.",
    };

    /// <summary>Whether the host allows the team option holding <paramref name="path"/>.</summary>
    public bool IsAllowed(string path)
    {
        int option = IndexOf(path);
        return option >= 0 && ((_allowed >> option) & 1) != 0;
    }

    /// <summary>The team's options in order (the gadget grid's eight buttons).</summary>
    public IReadOnlyList<string> Options => _options;

    /// <summary>Whether a slot holds <paramref name="option"/> (the grid's "Equipped").</summary>
    public bool IsEquipped(string option)
    {
        for (int i = 0; i < Slots; i++)
        {
            if (option.Length > 0 && Same(Gadgets[i], option))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>Edit_Gadgets.ButtonClick</c>: slot <paramref name="slot"/> takes <paramref name="option"/>; a slot that
    /// held it takes the slot's old gadget (the two swap). Options the host bans cannot be picked.
    /// </summary>
    public void Choose(int slot, string option)
    {
        if (slot < 0 || slot >= Slots || !IsAllowed(option))
        {
            return;
        }

        for (int i = 0; i < Slots; i++)
        {
            if (i != slot && Same(Gadgets[i], option))
            {
                Gadgets[i] = Gadgets[slot];
            }
        }

        Gadgets[slot] = option;
    }

    /// <summary><c>Edit_Profile</c>'s Reset: the side's default kit (DefaultProfile.ini), then the host's restrictions.</summary>
    public void Reset()
    {
        string[] defaults = Spy ? LoadoutSettings.DefaultSpy() : LoadoutSettings.DefaultMerc();
        Array.Copy(defaults, Gadgets, Math.Min(defaults.Length, Gadgets.Length));
        if (!Spy)
        {
            ChosenWeapon = Loadouts.MercWeapon.Path ?? "";
        }

        Normalize();
    }

    /// <summary>Moves the merc's weapon to the next (or previous) of the team's weapons.</summary>
    public void CycleWeapon(int step)
    {
        if (Weapons.Count == 0)
        {
            return;
        }

        int at = 0;
        for (int i = 0; i < Weapons.Count; i++)
        {
            if (Same(Weapons[i], ChosenWeapon))
            {
                at = i;
            }
        }

        ChosenWeapon = Weapons[(((at + Math.Sign(step)) % Weapons.Count) + Weapons.Count) % Weapons.Count];
    }

    /// <summary>Writes the choice into <paramref name="target"/> (slots past the host's count keep what they held).</summary>
    public void SaveTo(LoadoutSettings target)
    {
        ArgumentNullException.ThrowIfNull(target);
        string[] slots = target.GadgetsOf(Spy);
        for (int i = 0; i < Slots && i < slots.Length; i++)
        {
            slots[i] = Gadgets[i];
        }

        if (!Spy && ChosenWeapon.Length > 0)
        {
            target.MercWeapon = ChosenWeapon;
        }
    }

    /// <summary>
    /// What a spawn would give: a slot holding an option the host bans, or one an earlier slot holds, takes the first
    /// allowed option no slot holds (or is emptied), as the spawn's restriction pass does; the weapon falls back to
    /// the team's first.
    /// </summary>
    private void Normalize()
    {
        for (int s = 0; s < Slots; s++)
        {
            string g = Gadgets[s];
            bool duplicate = false;
            for (int e = 0; e < s; e++)
            {
                duplicate |= g.Length > 0 && Same(Gadgets[e], g);
            }

            if (g.Length == 0 || (IsAllowed(g) && !duplicate))
            {
                continue;
            }

            Gadgets[s] = "";
            foreach (string option in _options)
            {
                if (option.Length > 0 && IsAllowed(option) && !HeldElsewhere(option, s))
                {
                    Gadgets[s] = option;
                    break;
                }
            }
        }

        if (!Spy && Weapons.Count > 0 && !ContainsWeapon(ChosenWeapon))
        {
            ChosenWeapon = Weapons[0];
        }
    }

    private bool ContainsWeapon(string path)
    {
        foreach (string w in Weapons)
        {
            if (Same(w, path))
            {
                return true;
            }
        }

        return false;
    }

    private bool HeldElsewhere(string option, int slot)
    {
        for (int i = 0; i < Slots; i++)
        {
            if (i != slot && Same(Gadgets[i], option))
            {
                return true;
            }
        }

        return false;
    }

    private int IndexOf(string path)
    {
        for (int i = 0; i < _options.Count; i++)
        {
            if (Same(_options[i], path))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
