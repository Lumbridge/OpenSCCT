using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScctGame.Render.Characters;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

public sealed class LandingBlendSmoke : SmokeTest
{
    public const string TestName = "landing-blend";
    public override string Name => TestName;
    public override SmokeResult Step(SmokeContext context)
    {
        string? path = SkeletalModel.FindGlb(Sidearms.All[0].Asset + "_first");
        if (path is null) return SmokeResult.Fail("Missing first-person model");
        var model = SkeletalModel.Load(path);
        context.Tree.Root.AddChild(model.Root);
        try
        {
            foreach (int fps in new[] { 30, 60, 144 })
            foreach (var landing in new[] { ViewModelClips.FirstLandStFd2, ViewModelClips.FirstLandStHi2 })
            {
                var anim = new ViewModelAnim();
                var pose = new ViewModelPose();
                anim.Loop(0, ViewModelClips.FirstFallStFd2);
                pose.Update(anim, model.Sampler, 1f / fps);
                anim.Play(0, landing, ViewModelClips.DataOf(landing));
                if (pose.Update(anim, model.Sampler, 1f / fps)[0].Frame >= 0)
                    return SmokeResult.Fail($"{fps}: landing entry did not blend");
                for (int i = 0; i < fps / 5; i++) pose.Update(anim, model.Sampler, 1f / fps);
                if (pose.Update(anim, model.Sampler, 1f / fps)[0].Frame < 0)
                    return SmokeResult.Fail($"{fps}: entry blend did not finish");
                anim.Loop(0, ViewModelClips.FirstWaitStFd2);
                if (pose.Update(anim, model.Sampler, 1f / fps)[0].Frame >= 0)
                    return SmokeResult.Fail($"{fps}: landing recovery did not blend");
            }
            return SmokeResult.Pass("Landing entry and recovery blend at 30/60/144 FPS");
        }
        finally { model.Root.QueueFree(); }
    }
}

