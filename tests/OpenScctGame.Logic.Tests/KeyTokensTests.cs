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
}
