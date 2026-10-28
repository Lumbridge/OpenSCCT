using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using EquipmentK = OpenScct.Game.Equipment.K;

namespace OpenScct.Server.Online;

/// <summary>An ability the host turns on or off in Alternate Mode (the AlternateModeSettings check boxes).</summary>
[Flags]
public enum AlternateAbility : ushort
{
    None = 0,
    SpyThermalVision = 1 << 0,
    SpyNightVision = 1 << 1,
    SpyGrab = 1 << 2,
    SpyElbow = 1 << 3,
    SpyLethalDrop = 1 << 4,
    MercEmfVision = 1 << 5,
    MercMotionTracking = 1 << 6,
    MercElbow = 1 << 7,
    MercLethalDrop = 1 << 8,
    MercTorch = 1 << 9,
    MercLaser = 1 << 10,
    MercBerserk = 1 << 11,
    MercCharge = 1 << 12,
    CinematicCamera = 1 << 13,
}

/// <summary>
/// Enhanced 4.0's Alternate Mode (Create Session's "Alternate" box and the AlternateModeSettings page,
/// spec/game/lobby-and-match-creation.md "Alternate Mode"): with it on, the host chooses which abilities each side
/// has, which of the eight gadgets of each side may be taken and how many gadget slots each side gets. Off, the match
/// uses the stock values (InitPersistentData), with Deathmatch's own gadget rule. Gadget masks: a set bit allows the
/// side's option of that index (STeamInfo.ClassInventory order), as SPersistentData.GetGadgetRestriction reads it.
/// </summary>
public readonly record struct AlternateRules(bool On, AlternateAbility Abilities, byte SpyGadgets, byte SpyGadgetCount, byte MercGadgets, byte MercGadgetCount)
{
    /// <summary>The page's untouched values: InitPersistentData's (every ability but the lethal drops, every gadget, four slots).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.PersistentData.InitPersistentData; SPersistentData.InitPersistentData (no cinematic camera)")]
    public static AlternateRules Default { get; } = new(false, DefaultAbilities, EquipmentK.AllGadgetsAllowed, (byte)EquipmentK.GadgetSlots,
        EquipmentK.AllGadgetsAllowed, (byte)EquipmentK.GadgetSlots);

    /// <summary>The abilities InitPersistentData gives: all but Lethal Drop Tech on both sides and the cinematic camera.</summary>
    public const AlternateAbility DefaultAbilities = AlternateAbility.SpyThermalVision | AlternateAbility.SpyNightVision |
        AlternateAbility.SpyGrab | AlternateAbility.SpyElbow | AlternateAbility.MercEmfVision | AlternateAbility.MercMotionTracking |
        AlternateAbility.MercElbow | AlternateAbility.MercTorch | AlternateAbility.MercLaser | AlternateAbility.MercBerserk |
        AlternateAbility.MercCharge;

    /// <summary>Every ability bit the page has.</summary>
    public const AlternateAbility AllAbilities = (AlternateAbility)((1 << 14) - 1);

    /// <summary>Most gadget slots the page's "Number of gadgets" offers.</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.AlternateModeSettings NbGadgetSpy / NbGadgetMerc MaxValue 4")]
    public const int MaxGadgetCount = 4;

    /// <summary>Gadget options of each side on the page.</summary>
    public const int GadgetOptions = 8;

    /// <summary>The spy abilities in the page's order, with their captions (GUI.AlternateModeSettings).</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.AlternateModeSettings captions ThermalVision, NightVision, GrabPlayer, ElbowHit, LethalDropTech")]
    public static IReadOnlyList<(AlternateAbility Ability, string Caption)> SpyAbilityRows { get; } =
    [
        (AlternateAbility.SpyThermalVision, "Thermal Vision"),
        (AlternateAbility.SpyNightVision, "Night Vision"),
        (AlternateAbility.SpyGrab, "Grab Player"),
        (AlternateAbility.SpyElbow, "Elbow Hit"),
        (AlternateAbility.SpyLethalDrop, "Lethal Drop Tech"),
    ];

    /// <summary>The merc abilities in the page's order, with their captions (GUI.AlternateModeSettings).</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.AlternateModeSettings captions EMFVision, MotionTracking, ElbowHitMerc, LethalDropTechMErc, TorcheLight, LaserLight, Berserk, Charge")]
    public static IReadOnlyList<(AlternateAbility Ability, string Caption)> MercAbilityRows { get; } =
    [
        (AlternateAbility.MercEmfVision, "EMF Vision"),
        (AlternateAbility.MercMotionTracking, "Motion Tracking Vision"),
        (AlternateAbility.MercElbow, "Elbow Hit"),
        (AlternateAbility.MercLethalDrop, "Lethal Drop Tech"),
        (AlternateAbility.MercTorch, "Torch Light"),
        (AlternateAbility.MercLaser, "Laser Sight"),
        (AlternateAbility.MercBerserk, "Berserk"),
        (AlternateAbility.MercCharge, "Charge"),
    ];

    /// <summary>Deathmatch without Alternate Mode: spies only the flash bang (option 6), mercs only the flare launcher (option 7), one slot each.</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.Create_Match.ApplyMatchRules, Deathmatch branch: b_SPY_GadgetRestriction 64, b_MERC_GadgetRestriction 128, NBGadget 1")]
    private const byte DeathMatchSpyGadgets = 64;

    private const byte DeathMatchMercGadgets = 128;

    public bool Has(AlternateAbility ability) => (Abilities & ability) == ability;

    public AlternateRules With(AlternateAbility ability, bool on) => this with { Abilities = on ? Abilities | ability : Abilities & ~ability };

    /// <summary>Whether a side's gadget option is allowed (SPersistentData.GetGadgetRestriction).</summary>
    public bool GadgetAllowed(int team, int option) => (((team == 0 ? SpyGadgets : MercGadgets) >> option) & 1) != 0;

    /// <summary>A side's gadget option allowed or not (SetGadgetRestriction sets the page's checked boxes).</summary>
    public AlternateRules WithGadget(int team, int option, bool allowed)
    {
        byte bit = (byte)(1 << option);
        byte mask = team == 0 ? SpyGadgets : MercGadgets;
        mask = allowed ? (byte)(mask | bit) : (byte)(mask & ~bit);
        return team == 0 ? this with { SpyGadgets = mask } : this with { MercGadgets = mask };
    }

    /// <summary>
    /// The values within the page's ranges: "Number of gadgets" is 0 to 4 and, when four or fewer gadgets are allowed,
    /// at most that many (AlternateModeSettings.SpyGadgetChange, MercGadgetChange).
    /// </summary>
    public AlternateRules Clamped() => this with
    {
        Abilities = Abilities & AllAbilities,
        SpyGadgetCount = (byte)Math.Clamp((int)SpyGadgetCount, 0, Math.Min(MaxGadgetCount, BitOperations.PopCount(SpyGadgets))),
        MercGadgetCount = (byte)Math.Clamp((int)MercGadgetCount, 0, Math.Min(MaxGadgetCount, BitOperations.PopCount(MercGadgets))),
    };

    /// <summary>
    /// The host settings object as Create Session's Apply leaves it: Alternate Mode copies the page's values;
    /// without it the stock values (InitPersistentData), and in Deathmatch the lethal drop for spies, no merc EMF,
    /// motion tracking, laser or berserk, and one gadget a side (flash bang, flare launcher).
    /// </summary>
    [PortOf("uc:GUI.AlternateModeSettings:AlternateKeyEvent", Spec = "spec/game/lobby-and-match-creation.md#alternate-mode")]
    [PortOf("uc:GUI.Create_Match:ApplyMatchRules", Spec = "spec/game/lobby-and-match-creation.md#alternate-mode")]
    public void ApplyTo(USPersistentData pd, EGameMode mode)
    {
        ArgumentNullException.ThrowIfNull(pd);
        pd.bAlternateMode = On;
        if (!On)
        {
            pd.InitPersistentData();
            if (mode == EGameMode.GM_DeathMatch)
            {
                pd.bAvlb_SPY_LeathalDropTech = true;
                pd.bAvlb_MERC_EMFVision = false;
                pd.bAvlb_MERC_MotiontrackingVision = false;
                pd.bAvlb_MERC_Laser = false;
                pd.bAvlb_MERC_Berserk = false;
                pd.b_SPY_GadgetRestriction = DeathMatchSpyGadgets;
                pd.b_SPY_NBGadget = 1;
                pd.b_MERC_GadgetRestriction = DeathMatchMercGadgets;
                pd.b_MERC_NBGadget = 1;
            }

            return;
        }

        AlternateRules r = Clamped();
        pd.bAvlb_SPY_ThermalVision = r.Has(AlternateAbility.SpyThermalVision);
        pd.bAvlb_SPY_NightVision = r.Has(AlternateAbility.SpyNightVision);
        pd.bAvlb_SPY_Grab = r.Has(AlternateAbility.SpyGrab);
        pd.bAvlb_SPY_Elbow = r.Has(AlternateAbility.SpyElbow);
        pd.bAvlb_SPY_LeathalDropTech = r.Has(AlternateAbility.SpyLethalDrop);
        pd.bAvlb_MERC_EMFVision = r.Has(AlternateAbility.MercEmfVision);
        pd.bAvlb_MERC_MotiontrackingVision = r.Has(AlternateAbility.MercMotionTracking);
        pd.bAvlb_MERC_Elbow = r.Has(AlternateAbility.MercElbow);
        pd.bAvlb_MERC_LeathalDropTech = r.Has(AlternateAbility.MercLethalDrop);
        pd.bAvlb_MERC_Torche = r.Has(AlternateAbility.MercTorch);
        pd.bAvlb_MERC_Laser = r.Has(AlternateAbility.MercLaser);
        pd.bAvlb_MERC_Berserk = r.Has(AlternateAbility.MercBerserk);
        pd.bAvlb_MERC_Charge = r.Has(AlternateAbility.MercCharge);
        pd.bAvlb_CinematicCamera = r.Has(AlternateAbility.CinematicCamera);
        pd.b_SPY_GadgetRestriction = r.SpyGadgets;
        pd.b_SPY_NBGadget = r.SpyGadgetCount;
        pd.b_MERC_GadgetRestriction = r.MercGadgets;
        pd.b_MERC_NBGadget = r.MercGadgetCount;
    }

    /// <summary>
    /// A running map's replication info and teams, as the page's validation writes them on the host (the GRI's
    /// switches and each side's gadget mask and slot count, which replicate to every client).
    /// </summary>
    public static void ApplyTo(GameReplicationInfo gri, USPersistentData pd)
    {
        ArgumentNullException.ThrowIfNull(gri);
        ArgumentNullException.ThrowIfNull(pd);
        gri.bAlternateMode = pd.bAlternateMode;
        gri.bAvlb_SPY_ThermalVision = pd.bAvlb_SPY_ThermalVision;
        gri.bAvlb_SPY_NightVision = pd.bAvlb_SPY_NightVision;
        gri.bAvlb_SPY_Grab = pd.bAvlb_SPY_Grab;
        gri.bAvlb_SPY_Elbow = pd.bAvlb_SPY_Elbow;
        gri.bAvlb_SPY_LeathalDropTech = pd.bAvlb_SPY_LeathalDropTech;
        gri.bAvlb_MERC_EMFVision = pd.bAvlb_MERC_EMFVision;
        gri.bAvlb_MERC_MotiontrackingVision = pd.bAvlb_MERC_MotiontrackingVision;
        gri.bAvlb_MERC_Elbow = pd.bAvlb_MERC_Elbow;
        gri.bAvlb_MERC_LeathalDropTech = pd.bAvlb_MERC_LeathalDropTech;
        gri.bAvlb_MERC_Torche = pd.bAvlb_MERC_Torche;
        gri.bAvlb_MERC_Laser = pd.bAvlb_MERC_Laser;
        gri.bAvlb_MERC_Berserk = pd.bAvlb_MERC_Berserk;
        gri.bAvlb_MERC_Charge = pd.bAvlb_MERC_Charge;
        gri.bAvlb_CinematicCamera = pd.bAlternateMode && pd.bAvlb_CinematicCamera;
        if (gri.Teams.Length > 0 && gri.Teams[0] is STeamInfo spies)
        {
            spies.bDisallowedInv = pd.b_SPY_GadgetRestriction;
            spies.iNBGadgets = pd.b_SPY_NBGadget;
        }

        if (gri.Teams.Length > 1 && gri.Teams[1] is STeamInfo mercs)
        {
            mercs.bDisallowedInv = pd.b_MERC_GadgetRestriction;
            mercs.iNBGadgets = pd.b_MERC_NBGadget;
        }
    }

    /// <summary>
    /// The rules as one session option (part of <see cref="LobbySetup.ToOptions"/>), kept short because the setup
    /// line must stay under the control message's 512 bytes: <c>?SvmAlt=</c> then eleven hex digits, the switch (1),
    /// the abilities (4), the spy gadgets (2) and slots (1), the merc gadgets (2) and slots (1).
    /// </summary>
    public string ToOptions() => string.Create(CultureInfo.InvariantCulture,
        $"?SvmAlt={(On ? 1 : 0):X1}{(int)Abilities:X4}{SpyGadgets:X2}{SpyGadgetCount:X1}{MercGadgets:X2}{MercGadgetCount:X1}");

    /// <summary>Reads the rules from options (as <see cref="ToOptions"/> writes them); a missing or bad value keeps <paramref name="fallback"/>.</summary>
    public static AlternateRules FromOptions(string options, AlternateRules fallback)
    {
        string text = UcOptions.ParseOption(options ?? string.Empty, "SvmAlt");
        if (text.Length != 11 || !ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong v))
        {
            return fallback;
        }

        return new AlternateRules(
            (v >> 40 & 0xF) != 0,
            (AlternateAbility)((int)(v >> 24 & 0xFFFF) & (int)AllAbilities),
            (byte)(v >> 16 & 0xFF),
            (byte)(v >> 12 & 0xF),
            (byte)(v >> 4 & 0xFF),
            (byte)(v & 0xF)).Clamped();
    }

    /// <summary>A short line for the setup's description: what differs from the stock values.</summary>
    public string Describe()
    {
        if (!On)
        {
            return string.Empty;
        }

        var off = new List<string>();
        foreach ((AlternateAbility a, string caption) in SpyAbilityRows)
        {
            if (Has(a) != DefaultAbilities.HasFlag(a))
            {
                off.Add((Has(a) ? "spy " : "no spy ") + caption);
            }
        }

        foreach ((AlternateAbility a, string caption) in MercAbilityRows)
        {
            if (Has(a) != DefaultAbilities.HasFlag(a))
            {
                off.Add((Has(a) ? "merc " : "no merc ") + caption);
            }
        }

        string gadgets = string.Create(CultureInfo.InvariantCulture,
            $"spy gadgets {BitOperations.PopCount(SpyGadgets)}/8 ({SpyGadgetCount} slots), merc gadgets {BitOperations.PopCount(MercGadgets)}/8 ({MercGadgetCount} slots)");
        return ", alternate (" + (off.Count > 0 ? string.Join(", ", off) + ", " : string.Empty) + gadgets + ")";
    }
}
