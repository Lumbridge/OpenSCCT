namespace OpenScct.Core.Uc;

/// <summary>OpenSCCT's neutral default copy for generic players, independent of converted stock wording.</summary>
public static class NeutralPlayerText
{
    public static string Apply(string classPath, string property, int index, string fallback) => (classPath, property, index) switch
    {
        ("SBase.SDeathMessage", "SuicideString", _) => "eliminated themselves",
        ("SBase.SGameMessages", "TeammateNoLivesLeft", _) => "used their last life",
        ("SBase.SGameMessages", "DropAttack2", _) => "with a drop attack",
        ("SBase.SGadMultiPresDetectorLauncher", "ItemDescription", _) =>
            "Mark an enemy with a spy trap or detect nearby movement with a presence detector. Press \\HACKCOMM to listen to enemy communications.",
        ("SBase.SGadTazerLauncher", "ItemDescription", _) => "A short-range electrical shock that immobilizes a spy on impact.",
        ("GUI.Tuto_OneGadget", "Merc_TutorialText", 0) => "The backpack carries extra gadget supplies for the carrier and each teammate.",
        ("GUI.Tuto_OneGadget", "Merc_TutorialText", 2) =>
            "The spy trap marks a spy who crosses its laser beam, making them visible on the radar. The presence detector detects nearby moving spies. Place either device on a wall, like a mine. You can listen to enemy communications through a marked spy.",
        ("GUI.Tutorials_Tuto", "SENTRY_TutoExpli", 3) =>
            "Protect extraction objectives and their triggers. If a hard disk is stolen, stop the intruder and retrieve it before it reaches the drop zone.",
        ("GUI.Tutorials_Tuto", "SENTRY_TutoExpli", 7) =>
            "Aim with the mouse and press \\\\USE at close range to knock an enemy down. Move near the enemy's head and press \\\\USE to finish the attack. You can also stand still and press \\\\USE to counter an attacker.",
        ("GUI.Tutorials_Tuto", "SPY_TutoExpli", 7) =>
            "Press \\\\USE near a mercenary to push them away, knock them out, or grab them from behind. While holding a mercenary, press \\\\PRIMARYFIRE for a knockout, press \\\\USE for a lethal attack, or keep holding to choke them.",
        ("SBase.STeamAttaquant", "aTipStrings", 5) => "Stay in the shadows and plan your approach before attacking an enemy.",
        ("SBase.STeamAttaquant", "aTipStrings", 14) => "You normally speak only to your teammates. While holding a mercenary, or whenever the talk icon appears, you can speak to that player too.",
        ("SBase.STeamDefenseur", "aTipStrings", 9) => "Spies are dangerous at close range. Aim with the mouse and press \\\\USE to knock an enemy down with a charge, then follow up.",
        ("SBase.STeamDefenseur", "aTipStrings", 11) => "To finish a downed spy, move near the spy's head and press \\\\USE. Press \\\\USE again for a lethal attack, or \\\\HACKCOMM to speak to the spy.",
        _ => fallback,
    };
}
