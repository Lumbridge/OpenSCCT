using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Animation;

public sealed class IndependentOverlayClockTests
{
    [Fact]
    public void SplitOverlayClockPreservesAimOtherChannelsBlendAndOneShotEnd()
    {
        const float dt = 1f / 60f;
        var whole = new PawnAnimation();
        var split = new PawnAnimation();
        var clip = new PawnClip(new Name("Overlay"), 16, 15f);
        foreach (PawnAnimation animation in new[] { whole, split })
        {
            animation.PlayAnim(0, clip, 1f, 0f, loop: true);
            animation.PlayAnim(SBasePawn.GOGGLECHANNEL, clip, 1f, 0f, loop: false);
            animation.SetBlendParams(SBasePawn.GOGGLECHANNEL, 0f);
            animation.BlendToAlpha(SBasePawn.GOGGLECHANNEL, 1f, .3f);
            animation.Aim.FullYaw = 1f;
        }
        var wholeEnds = new List<int>();
        var splitEnds = new List<int>();
        for (int i = 0; i < 80; i++)
        {
            whole.Advance(dt, wholeEnds.Add);
            float overlayFrame = split[SBasePawn.GOGGLECHANNEL].Frame;
            split.Advance(dt, splitEnds.Add, skipChannel: SBasePawn.GOGGLECHANNEL);
            Assert.Equal(overlayFrame, split[SBasePawn.GOGGLECHANNEL].Frame);
            split.AdvanceChannel(SBasePawn.GOGGLECHANNEL, dt, splitEnds.Add);
            Assert.Equal(whole.Aim.FullYaw, split.Aim.FullYaw);
            Assert.Equal(whole[0].Frame, split[0].Frame);
            Assert.Equal(whole[SBasePawn.GOGGLECHANNEL].Frame, split[SBasePawn.GOGGLECHANNEL].Frame);
            Assert.Equal(whole[SBasePawn.GOGGLECHANNEL].BlendAlpha, split[SBasePawn.GOGGLECHANNEL].BlendAlpha);
        }
        Assert.Contains(SBasePawn.GOGGLECHANNEL, wholeEnds);
        Assert.Equal(wholeEnds, splitEnds);
    }
}
