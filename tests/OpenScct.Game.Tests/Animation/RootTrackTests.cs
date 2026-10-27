using System;
using System.IO;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Animation;

/// <summary>
/// Root motion from the clip loader (ANSWERS-moveset.md findings 1 and 2): a clip with a keyed root track moves the
/// pawn by the track, linear between its keys, not by the root bone; root positions are absolute, so a clip change
/// while locked moves from the last clip's last root position to the new clip's first.
/// </summary>
public sealed class RootTrackTests
{
    private const float Dt = 1f / 60f;

    /// <summary>
    /// Two clips: TestClimb has a root bone that curves and a keyed track that rises 100 then goes 50 forward (mesh
    /// Y is forward); TestAfter has no track and a root bone starting 40 forward and 20 up.
    /// </summary>
    private static PawnAnimationSource Source(string dir)
    {
        Directory.CreateDirectory(Path.Combine(dir, "SPerso"));
        const string json = "{\"format\":\"svm.anim/1\",\"sequences\":{" +
            "\"TestClimb\":{\"frames\":11,\"rate\":10," +
            "\"rootBone\":{\"bone\":\"B\",\"pos\":[[0,0,0],[0,1,30],[0,2,55],[0,4,75],[0,7,88],[0,11,95],[0,16,98],[0,22,99],[0,30,100],[0,40,100],[0,50,100]],\"rot\":[]}," +
            "\"rootTrack\":{\"pos\":[[0,0,-90],[0,0,10],[0,50,10],[0,50,10]],\"rot\":[[0,0,0,-1]],\"time\":[0,4,8,10]}}," +
            "\"TestAfter\":{\"frames\":5,\"rate\":10," +
            "\"rootBone\":{\"bone\":\"B\",\"pos\":[[0,40,20],[0,40,30],[0,40,40],[0,40,50],[0,40,60]],\"rot\":[]}," +
            "\"rootTrack\":{\"pos\":[],\"rot\":[],\"time\":[]}}}}";
        File.WriteAllText(Path.Combine(dir, "SPerso", "PRO.anim.json"), json);
        return PawnAnimationSource.FromContent(dir);
    }

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "svm-roottrack-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void TheKeyedTrackIsReadInPawnSpaceAndSampledLinearlyBetweenKeys()
    {
        string dir = TempDir();
        try
        {
            PawnAnimationSource source = Source(dir);
            PawnRig rig = Rig(PawnTeam.Spy);
            Assert.True(source.TryGetClip(rig.Pawn, new Name("TestClimb"), out PawnClip climb));
            Assert.NotNull(climb.RootTrack);

            // Frame 2 of 11 (normalised 2/11) is half way between keys 0 and 4: Z -40, not the bone's 55.
            FVector at2 = climb.RootAt(2f / 11f);
            Assert.Equal(-40f, at2.Z, 3);
            Assert.Equal(0f, at2.X, 3);

            // Frame 6: half way along the forward leg (mesh Y is pawn X).
            FVector at6 = climb.RootAt(6f / 11f);
            Assert.Equal(25f, at6.X, 3);
            Assert.Equal(10f, at6.Z, 3);

            // Past the last key the track holds.
            Assert.Equal(50f, climb.RootAt(1f).X, 3);

            // No keys: the root bone, absolute (not moved to start at zero).
            Assert.True(source.TryGetClip(rig.Pawn, new Name("TestAfter"), out PawnClip after));
            Assert.Null(after.RootTrack);
            Assert.Equal(40f, after.RootAt(0f).X, 3);
            Assert.Equal(20f, after.RootAt(0f).Z, 3);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ALockedMoveFollowsTheTrackAndAClipChangeMovesFromTheLastRootPosition()
    {
        string dir = TempDir();
        try
        {
            PawnAnimationSource source = Source(dir);
            PawnRig rig = Rig(PawnTeam.Spy);
            SBasePawn pawn = rig.Pawn;
            Assert.True(source.TryGetClip(pawn, new Name("TestClimb"), out PawnClip climb));
            Assert.True(source.TryGetClip(pawn, new Name("TestAfter"), out PawnClip after));
            var rm = new PawnRootMotion(pawn);
            PawnAnimation a = pawn.PawnAnim;
            a.PlayAnim(0, climb, 1f, 0f, loop: false);
            rm.LockRootMotion(1);

            // Facing +Y (yaw 16384): the track's forward is world +Y. Sum the deltas over the whole clip.
            FVector total = default;
            for (int i = 0; i < 70; i++)
            {
                a.Advance(Dt, _ => { }, () => true);
                rm.AdvanceRootMotion(out FVector d, out _);
                total += d;
            }

            Assert.Equal(100f, total.Z, 1);
            Assert.Equal(50f, total.Y, 1);
            Assert.Equal(0f, total.X, 1);

            // The next clip starts 40 forward and 20 up in its own root: from the track's end (50 forward, 10 up
            // over a -90 start, so Z 10) the first delta is 10 back and 10 up.
            a.PlayAnim(0, after, 1f, 0f, loop: false);
            a.Advance(1e-6f, _ => { }, () => true);
            rm.AdvanceRootMotion(out FVector first, out _);
            Assert.Equal(-10f, first.Y, 1);
            Assert.Equal(10f, first.Z, 1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The drawn pose's lock bit 2 turn (anim.md rule 3.5, pose build game 0x109ba4f0): minus the root turn the root
    /// motion gave the pawn since the lock, read at the drawn frame; a turn of the pawn from anything else is not part
    /// of it, and a mirrored clip turns the other way.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheCarriedRootTurnIsTheRootMotionsTurnSinceTheLock(bool mirror)
    {
        string dir = TempDir();
        try
        {
            // TestTurn: the root bone turns 0, 30, 60, 90 degrees about the up axis over its 4 frames.
            Directory.CreateDirectory(Path.Combine(dir, "SPerso"));
            const string json = "{\"format\":\"svm.anim/1\",\"sequences\":{" +
                "\"TestTurn\":{\"frames\":4,\"rate\":4," +
                "\"rootBone\":{\"bone\":\"B\",\"pos\":[[0,0,0],[0,0,0],[0,0,0],[0,0,0]]," +
                "\"rot\":[[0,0,0,1],[0,0,0.258819,0.965926],[0,0,0.5,0.866025],[0,0,0.707107,0.707107]]}}}}";
            File.WriteAllText(Path.Combine(dir, "SPerso", "PRO.anim.json"), json);
            PawnAnimationSource source = PawnAnimationSource.FromContent(dir);
            PawnRig rig = Rig(PawnTeam.Spy);
            SBasePawn pawn = rig.Pawn;
            Assert.True(source.TryGetClip(pawn, new Name("TestTurn"), out PawnClip turn));
            var rm = new PawnRootMotion(pawn);
            PawnAnimation a = pawn.PawnAnim;
            a.PlayAnim(0, turn, 1f, 0f, loop: false, mirror: mirror);
            rm.LockRootMotion(3);
            Assert.Equal(0, rm.CarriedRootTurn(a[0], a[0].ClipFrame));

            int applied = 0;
            for (int i = 0; i < 30; i++)
            {
                a.Advance(Dt, _ => { }, () => true);
                rm.AdvanceRootMotion(out _, out FRotator d);
                applied += d.Yaw;
            }

            Assert.NotEqual(0, applied);
            Assert.Equal(applied, rm.CarriedRootTurn(a[0], a[0].ClipFrame));

            // Another turn of the pawn (a replicated correction, a scripted turn) is not taken out of the root.
            pawn.Rotation = new FRotator(0, pawn.Rotation.Yaw + 5000, 0);
            Assert.Equal(applied, rm.CarriedRootTurn(a[0], a[0].ClipFrame));

            // Read at an earlier drawn frame, the turn still to be measured comes off.
            float earlier = a[0].ClipFrame * 0.5f;
            int back = SvmMath.Round(turn.RootYawAt(earlier)) - SvmMath.Round(turn.RootYawAt(a[0].ClipFrame));
            Assert.Equal(applied + (mirror ? -back : back), rm.CarriedRootTurn(a[0], earlier));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
