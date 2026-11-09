using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScctGame.Controls;
using OpenScctGame.Hud;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>The key token pass of drawn texts (spec/hud/counters-and-prompts.md rule 11).</summary>
public sealed class KeyTokensTests
{
    private const string Layout = """
        [Engine.Input]
        IK_Shift=Use
        IK_LeftMouse=Fire | GadgetQuickChoice 0
        IK_RightMouse=AltFire | GadgetQuickChoice 1 | HoldBreath
        IK_H=HackComm
        IK_B=SwitchBinocularVision
        IK_X=SwitchBinocularVision
        """;

    private static string? Name(string binding) => KeyTokens.KeyBoundTo(KeyLayout.Parse(Layout), binding) switch
    {
        "RightMouse" => "Right Mouse",
        "LeftMouse" => "Left Mouse",
        { } k => k,
        null => null,
    };

    [Fact]
    public void Gas_mask_hint_names_the_right_mouse_button()
    {
        Assert.Equal("Press Right Mouse to activate the mask.", KeyTokens.Expand(@"Press \SECONDARYFIRE to activate the mask.", Name));
    }

    [Fact]
    public void Tokens_ignore_case_and_several_are_replaced()
    {
        Assert.Equal("Shift then Left Mouse, H", KeyTokens.Expand(@"\use then \PrimaryFire, \HACKCOMM", Name));
    }

    [Fact]
    public void The_first_key_in_input_key_order_wins()
    {
        // IK_B (0x42) comes before IK_X (0x58).
        Assert.Equal("B", KeyTokens.Expand(@"\BINOCULARS", Name));
    }

    [Fact]
    public void Unbound_and_unknown_tokens_stay()
    {
        Assert.Equal(@"\JUMP and \NOTATOKEN", KeyTokens.Expand(@"\JUMP and \NOTATOKEN", Name));
        Assert.Equal(@"\SECONDARYFIRE", KeyTokens.Expand(@"\SECONDARYFIRE", null));
    }

    [Fact]
    public void A_binding_matches_only_as_a_whole()
    {
        Assert.Null(KeyTokens.KeyBoundTo(KeyLayout.Parse(Layout), "AltFire"));
        Assert.Equal("RightMouse", KeyTokens.KeyBoundTo(KeyLayout.Parse(Layout), "altfire | gadgetquickchoice 1 | holdbreath"));
    }

    [Fact]
    public void Merc_tip_resolves_the_expanded_use_alias()
    {
        var layout = KeyLayout.Parse("""
            [Engine.Input]
            Aliases[12]=(Command="InvButton bUsing",Alias="Use")
            IK_Shift=InvButton bUsing
            """);

        Assert.Equal("Press Shift again.", KeyTokens.Expand(@"Press \\USE again.",
            binding => KeyTokens.KeyBoundTo(layout, binding)));
        layout.Bind("Shift", "InvButton bUsing | HackComm");
        Assert.Null(KeyTokens.KeyBoundTo(layout, "Use"));
    }

    [Fact]
    public void Escaped_tip_tokens_consume_both_backslashes()
    {
        Assert.Equal("Press F2, then Shift.",
            KeyTokens.Expand(@"Press \\VISION2, then \\USE.", binding => binding == "SwitchHeadset 2" ? "F2" : Name(binding)));
        Assert.Equal(@"\\JUMP and \\NOTATOKEN", KeyTokens.Expand(@"\\JUMP and \\NOTATOKEN", Name));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Hints_resolve_every_token_without_original_aliases(int slashes)
    {
        KeyLayout layout = DefaultKeyLayout.ApplyProfile(DefaultKeyLayout.Create(), null, "Gadget");
        string prefix = new('\\', slashes);
        Assert.Equal("Shift LeftMouse RightMouse H MiddleMouse",
            KeyTokens.ExpandHint($"{prefix}USE {prefix}PRIMARYFIRE {prefix}SECONDARYFIRE {prefix}HACKCOMM {prefix}SPEEDMODE", layout, merc: false));
        foreach (bool merc in new[] { false, true })
            foreach (var token in KeyTokens.Tokens)
            {
                string expanded = KeyTokens.ExpandHint(prefix + token.Token, layout, merc);
                Assert.DoesNotContain('\\', expanded);
                Assert.NotEqual("Unbound", expanded);
            }
    }

    [Fact]
    public void Hints_follow_split_actions_custom_aliases_chords_and_preferred_inputs()
    {
        var layout = KeyLayout.Parse("""
            [Engine.Input]
            Aliases[0]=(Command="InvButton bUsing | TeamPing",Alias="CustomUse")
            IK_F=CustomUse
            IK_Ctrl+K=CustomUse
            IK_X=Button bFire | Function Fire
            IK_Y=GadgetQuickChoice 0
            IK_Z=Button bAltFire | Function AltFire
            IK_H=Button bHoldBreath
            IK_Q=Button bRoll
            IK_L=GadLaserSwitch
            IK_E=InvButton bFireMode
            IK_T=GadTorcheSwitch
            """);
        layout.SetInputOrder("use", ["F", "Ctrl+K"]);
        Assert.Equal("F X Z Q E", KeyTokens.ExpandHint(@"\\USE \\PRIMARYFIRE \\SECONDARYFIRE \\ACTION1 \\ACTION2", layout, merc: false));
        Assert.Equal("L T", KeyTokens.ExpandHint(@"\\ACTION1 \\ACTION2", layout, merc: true));
        layout.Bind("F", "TeamPing");
        Assert.Equal("[Ctrl+K]", KeyTokens.ExpandHint(@"\\USE", layout, merc: false, key => "[" + key + "]"));
    }

    [Fact]
    public void Hints_name_unbound_actions_and_preserve_unknown_text()
    {
        var layout = KeyLayout.Parse("[Engine.Input]\nIK_X=SelectGadget 1");
        Assert.Equal(@"Unbound X Unbound \\NOTATOKEN",
            KeyTokens.ExpandHint(@"\\USE \\GADGET2 \\GADGET1 \\NOTATOKEN", layout, merc: false));
    }

    [Fact]
    public void Every_converted_loading_tip_expands_with_default_and_unbound_layouts()
    {
        Assert.SkipUnless(UcText.HasText, "Converted loading tips are not installed.");
        KeyLayout defaults = DefaultKeyLayout.ApplyProfile(DefaultKeyLayout.Create(), null, "Gadget");
        foreach (STeamInfo team in new STeamInfo[] { new STeamAttaquant(), new STeamDefenseur() })
            foreach (string tip in team.aTipStrings)
                foreach (KeyLayout layout in new[] { defaults, KeyLayout.Parse("") })
                    Assert.DoesNotContain('\\', KeyTokens.ExpandHint(tip, layout, team is STeamDefenseur));
    }
}
