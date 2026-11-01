using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Combat;

public sealed class CoughAnimationCleanupTests
{
    [Theory]
    [InlineData("s_Dead")]
    [InlineData("GameEnded")]
    public void EndingPlayDuringACoughClearsTheBodyOverlay(string state)
    {
        PawnRig rig = CoughingMerc();

        if (state == "s_Dead") rig.Pawn.Health = 0;
        rig.Controller.GotoState(new Name(state));
        rig.Hold(default, 30);

        Assert.False(rig.Controller.btoCought);
        Assert.Equal(0f, rig.Pawn.PawnAnim[SBasePawn.COUGHTCHANNEL].BlendAlpha);
    }

    [Fact]
    public void FinishingAWakeClearsACoughThatOutlastsTheKnockout()
    {
        PawnRig rig = CoughingMerc();
        rig.Pawn.ExecuteAction(SBasePawn.SA_DIE_INPLACE);
        rig.Controller.GotoState(new Name("s_Reanimating"));
        for (int i = 0; i < 600 && rig.Controller.IsInState(new Name("s_Reanimating")); i++)
        {
            rig.Step(default);
        }

        Assert.False(rig.Controller.IsInState(new Name("s_Reanimating")));
        rig.Hold(default, 30);
        Assert.False(rig.Controller.btoCought);
        Assert.Equal(0f, rig.Pawn.PawnAnim[SBasePawn.COUGHTCHANNEL].BlendAlpha);
    }

    [Fact]
    public void AnUninterruptedCoughKeepsItsOverlayUntilItsDeadline()
    {
        PawnRig rig = CoughingMerc(duration: 1f);
        rig.Hold(default, 10);
        Assert.True(rig.Controller.btoCought);
        Assert.Equal(1f, rig.Pawn.PawnAnim[SBasePawn.COUGHTCHANNEL].BlendAlpha);

        rig.Hold(default, 90);
        Assert.False(rig.Controller.btoCought);
        Assert.Equal(0f, rig.Pawn.PawnAnim[SBasePawn.COUGHTCHANNEL].BlendAlpha);
    }

    private static PawnRig CoughingMerc(float duration = 30f)
    {
        PawnRig rig = Rig(PawnTeam.Merc);
        rig.Controller.fLastTimeWakeUp = -100f;
        rig.Controller.eventGoInSmoke(duration, 30f, 0f, 0f);
        rig.Step(default);
        Assert.True(rig.Controller.btoCought);
        Assert.Equal(new Name("CaughStSpFd2"), rig.Pawn.PawnAnim[SBasePawn.COUGHTCHANNEL].Sequence);
        Assert.Equal(1f, rig.Pawn.PawnAnim[SBasePawn.COUGHTCHANNEL].BlendAlpha);
        return rig;
    }
}
