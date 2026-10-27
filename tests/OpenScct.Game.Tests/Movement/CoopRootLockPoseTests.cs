using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// The drawn root during a root-motion lock (coop.md rules 24 and 25, anim.md rule 3.4): the root bone moves by the
/// root motion since the lock (bone + R(lock) - R(t)), so a clip with a root track shows the body's own movement
/// around the moving actor, and a clip without one keeps the root bone where the lock found it.
/// </summary>
public sealed class CoopRootLockPoseTests
{
    private static PawnClip? Clip(string name)
    {
        if (ContentOptions() is not { CharacterContentDir: { } dir })
        {
            return null;
        }

        PawnAnimationSource source = PawnAnimationSource.FromContent(dir);
        return source.TryGetClip(new SPawnAttaque_1Mesh(), new Name(name), out PawnClip clip) ? clip : null;
    }

    /// <summary>The drawn root (bone plus the lock offset) at a clip frame number, locked at frame 0.</summary>
    private static FVector Drawn(PawnClip clip, float frame)
    {
        float f = frame / clip.Frames;
        Assert.True(RootLockPose.Offset(clip, f, false, clip.RootAt(0f), out FVector offset));
        return Bone(clip, f) + offset;
    }

    private static FVector Bone(PawnClip clip, float f)
    {
        FVector[] root = clip.Root!;
        float x = f * clip.Frames;
        int i = System.Math.Min((int)x, root.Length - 1);
        int j = System.Math.Min(i + 1, root.Length - 1);
        return root[i] + ((root[j] - root[i]) * (x - i));
    }

    [Fact]
    public void TheBoostedClimbersBodyRunsIntoTheHandsWhileTheActorWaits()
    {
        PawnClip? clip = Clip("CoopBtwAlHpEdS");
        Assert.SkipWhen(clip is null, "the converted characters (root tracks) are not in .content");
        Assert.NotNull(clip!.RootTrack);
        // Rule 25: by frame 14 the body has run 48.7 forward and is 22.3 up while the actor has not moved. Pinned (the
        // old pose), the drawn root stayed at its lock place.
        FVector run = Drawn(clip, 14f) - Drawn(clip, 0f);
        Assert.Equal(48.7f, run.X, 0);
        Assert.Equal(22.3f, run.Z, 0);
        // At the end: 7.8 forward and 21.8 down from the actor's carried root.
        FVector end = Drawn(clip, clip.Frames - 1) - Drawn(clip, 0f);
        Assert.Equal(7.8f, end.X, 0);
        Assert.Equal(-21.8f, end.Z, 0);
    }

    [Fact]
    public void TheHoistClimbersBodyReachesTheLegsPastTheActor()
    {
        PawnClip? clip = Clip("CoopDynHstBSBg");
        Assert.SkipWhen(clip is null, "the converted characters (root tracks) are not in .content");
        // Rule 25: the body reaches 135 forward while the actor stops at 59.
        float most = 0f;
        for (int f = 0; f < clip!.Frames; f++)
        {
            FVector bone = Drawn(clip, f) - Drawn(clip, 0f) + (clip.RootAt((float)f / clip.Frames) - clip.RootAt(0f));
            most = System.Math.Max(most, bone.X);
        }

        Assert.InRange(most, 125f, 145f);
    }

    [Fact]
    public void AClipWithoutATrackKeepsTheRootBoneWhereTheLockFoundIt()
    {
        PawnClip? clip = Clip("LedgStAlBg0");
        Assert.SkipWhen(clip is null, "the converted characters are not in .content");
        if (clip!.RootTrack is not null)
        {
            return;
        }

        FVector at0 = Drawn(clip, 0f);
        for (int f = 1; f < clip.Frames; f++)
        {
            FVector d = Drawn(clip, f) - at0;
            Assert.True(d.Size() < 0.01f, $"frame {f}: {d}");
        }
    }

    [Fact]
    public void AClipWithoutRootDataHasNoOffset()
    {
        var clip = new PawnClip(new Name("NoRoot"), 10, 15f);
        Assert.False(RootLockPose.Offset(clip, 0.5f, false, default, out _));
        Assert.False(RootLockPose.Offset(null, 0.5f, false, default, out _));
    }

    [Fact]
    public void AMirroredClipReversesTheSidewaysOffset()
    {
        var clip = new PawnClip(new Name("Side"), 2, 15f, [new FVector(0f, 0f, 0f), new FVector(10f, 20f, 30f), new FVector(10f, 20f, 30f)]);
        Assert.True(RootLockPose.Offset(clip, 0.5f, true, default, out FVector offset));
        Assert.Equal(new FVector(-10f, 20f, -30f), offset);
    }
}
