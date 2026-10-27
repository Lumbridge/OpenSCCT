using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Animation;

/// <summary>
/// Clip notifies (ANSWERS-anim.md A2.6: in time order, splitting the advance, script notifies to the level's handlers)
/// and the converted clip data the animation source reads (notifies, root yaw).
/// </summary>
public sealed class NotifyAndContentTests
{
    private const float Dt = 1f / 60f;

    private static PawnClip WithScriptNotifies(params (float Time, string Name)[] notifies)
    {
        var list = new List<PawnClipNotify>();
        foreach ((float time, string name) in notifies)
        {
            list.Add(new PawnClipNotify(time, new UAnimNotify_Script { NotifyName = new Name(name) }));
        }

        return new PawnClip(new Name("TestNotified"), 10, 15f) { Notifies = list };
    }

    [Fact]
    public void ScriptNotifiesFireInTimeOrderThroughTheLevelsHandlers()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn pawn = rig.Pawn;
        var fired = new List<string>();
        AnimNotifyHandlers.Of(rig.Level)
            .Register(new Name("TestNotifyA"), _ => fired.Add("A"))
            .Register(new Name("TestNotifyB"), _ => fired.Add("B"));
        PawnClip clip = WithScriptNotifies((0.5f, "TestNotifyB"), (0.2f, "TestNotifyA"));
        PawnAnimation a = pawn.PawnAnim;
        a.PlayAnim(19, clip, 1f, 0f, loop: false);
        for (int i = 0; i < 40; i++)
        {
            a.Advance(Dt, _ => { });
        }

        Assert.Equal(["A", "B"], fired);
    }

    [Fact]
    public void ANotifyStopsTheFrameAtItsTimeAndTheRestOfTheTickCarriesOn()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        int count = 0;
        AnimNotifyHandlers.Of(rig.Level).Register(new Name("TestNotifyC"), _ => count++);
        PawnClip clip = WithScriptNotifies((0.3f, "TestNotifyC"));
        PawnAnimation a = rig.Pawn.PawnAnim;
        a.PlayAnim(19, clip, 1f, 0f, loop: false);
        a.Advance(Dt, _ => { });
        a.SetFrame(19, 0.29f);
        a.Advance(Dt, _ => { });
        Assert.Equal(1, count);
        Assert.Equal(0.29f + (Dt * 1.5f), a[19].Frame, 5);
    }

    [Fact]
    public void AnInterruptedOneShotFiresItsRemainingScriptNotifies()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        int count = 0;
        AnimNotifyHandlers.Of(rig.Level).Register(new Name("TestNotifyD"), _ => count++);
        PawnAnimation a = rig.Pawn.PawnAnim;
        a.PlayAnim(19, WithScriptNotifies((0.8f, "TestNotifyD")), 1f, 0f, loop: false);
        a.Advance(Dt, _ => { });
        a.Advance(Dt, _ => { });
        a.PlayAnim(19, new PawnClip(new Name("TestPlain"), 4, 15f), 1f, 0f, loop: false);
        Assert.Equal(1, count);
    }

    [Fact]
    public void TheConvertedSequencesGiveNotifiesAndTheRootTurn()
    {
        string dir = Path.Combine(Path.GetTempPath(), "svm-anim-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "SPerso"));
        try
        {
            // A 3-key clip turning 90 degrees about Z at constant speed, with a sound, a stop and a script notify.
            float s = MathF.Sin(MathF.PI / 8f);
            float c = MathF.Cos(MathF.PI / 8f);
            string json = "{\"format\":\"svm.anim/1\",\"sequences\":{\"TestTurn\":{\"frames\":3,\"rate\":15," +
                "\"rootBone\":{\"bone\":\"B\",\"pos\":[[0,0,0],[0,1,0],[0,2,0]],\"rot\":[[0,0,0,1],[0,0," +
                s.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + c.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "],[0,0,0.70710677,0.70710677]]}," +
                "\"notifies\":[{\"class\":\"Engine.AnimNotify_Sound\",\"time\":0.1,\"props\":{\"Sound\":{\"$asset\":\"Pkg.Grp.Snd\"},\"Volume\":20,\"Radius\":80,\"BoneName\":\"B R Hand\"}}," +
                "{\"class\":\"Engine.AnimNotify_StopSound\",\"time\":0.2,\"props\":{\"Sound\":{\"$asset\":\"Pkg.Grp.Loop\"}}}," +
                "{\"class\":\"Engine.AnimNotify_Script\",\"time\":0.5,\"props\":{\"NotifyName\":\"PlayFootStepLeft\"}}]}}}";
            File.WriteAllText(Path.Combine(dir, "SPerso", "PRO.anim.json"), json);
            PawnAnimationSource source = PawnAnimationSource.FromContent(dir);
            PawnRig rig = Rig(PawnTeam.Spy);
            Assert.True(source.TryGetClip(rig.Pawn, new Name("TestTurn"), out PawnClip clip));
            Assert.Equal(3, clip.Notifies.Count);
            UAnimNotify_Sound sound = Assert.IsType<UAnimNotify_Sound>(clip.Notifies[0].Notify);
            Assert.Equal("Pkg.Grp.Snd", sound.Sound.Path);
            Assert.Equal(80, sound.Radius);
            Assert.Equal(new Name("B R Hand"), sound.BoneName);
            Assert.IsType<UAnimNotify_StopSound>(clip.Notifies[1].Notify);
            Assert.Equal(new Name("PlayFootStepLeft"), Assert.IsType<UAnimNotify_Script>(clip.Notifies[2].Notify).NotifyName);
            Assert.NotNull(clip.RootYaw);
            Assert.Equal(0f, clip.RootYawAt(0f), 1);
            Assert.Equal(8192f, clip.RootYawAt(1f / 3f), 0);
            Assert.Equal(16384f, clip.RootYawAt(2f / 3f), 0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
