using System.IO;
using Godot;
using OpenScctGame.Play;
using OpenScctGame.Tutorials;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Exercises the desktop menu entries, both real lesson scenes, coaching and return navigation.</summary>
public sealed class InteractiveLessonsSmoke : SmokeTest
{
    public const string TestName = "interactive-lessons";
    public override string Name => TestName;
    private int _stage, _frames;
    private int _startReturns;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_stage == 0)
        {
            TutorialsScreen.Show(context.Tree);
            _stage = 1;
            return SmokeResult.Running;
        }
        if (_stage == 1 && context.Tree.CurrentScene is TutorialsScreen screen)
        {
            if (++_frames < 8 || context.ElapsedSeconds < 1.2) return SmokeResult.Running;
            var vision = screen.FindChild("Lesson_vision-cancelling", true, false) as Button;
            var grab = screen.FindChild("Lesson_spy-grab", true, false) as Button;
            if (vision is null || grab is null || vision.Disabled || grab.Disabled)
                return SmokeResult.Fail("interactive lessons missing or unavailable in the existing Tutorials section");
            Capture(context, "tutorials-menu");
            vision.EmitSignal(BaseButton.SignalName.Pressed);
            _stage = 2;
            _frames = 0;
            return SmokeResult.Running;
        }
        if (context.Tree.CurrentScene is not PlayRoot { IsLoaded: true } root ||
            root.GetNodeOrNull<InteractiveLessonCoach>(InteractiveLessonCoach.NodeName)?.Lesson is not { Ready: true } lesson)
            return SmokeResult.Running;
        if (_stage == 2)
        {
            if (++_frames < 10) return SmokeResult.Running;
            root.Controls!.Enabled = false;
            lesson.Action("drop");
            _stage = 3;
        }
        if (_stage == 3 && lesson.RecoveryMs is not null)
        {
            if (lesson.BaselineMs is null || lesson.History.Count == 0 || root.GetNodeOrNull<Node3D>("ReturnPortal") is null)
                return SmokeResult.Fail("desktop vision drill did not measure landing recovery or create the portal");
            Capture(context, "vision-feedback");
            _startReturns = lesson.Returns;
            lesson.Action("reset");
            _stage = 4;
        }
        if (_stage == 4 && lesson.Returns > _startReturns)
        {
            if (root.Session!.LocalPawn!.Location.Z < 640 || lesson.History.Count == 0)
                return SmokeResult.Fail("return to ledge lost placement or results");
            root.Menu!.Open();
            // The coach's explicit return must restore the existing Tutorials page, not a second tutorial hub.
            var back = root.FindChild("Tutorials", true, false) as Button;
            if (back is null) return SmokeResult.Fail("lesson has no return to Tutorials");
            back.EmitSignal(BaseButton.SignalName.Pressed);
            if (context.Tree.CurrentScene is not TutorialsScreen tutorials) return SmokeResult.Fail("lesson did not return to Tutorials");
            (tutorials.FindChild("Lesson_spy-grab", true, false) as Button)!.EmitSignal(BaseButton.SignalName.Pressed);
            _stage = 5;
            _frames = 0;
            return SmokeResult.Running;
        }
        if (_stage == 5)
        {
            root.Controls!.Enabled = false;
            if (++_frames < 20) return SmokeResult.Running;
            if (lesson.Kind != "spy-grab" || lesson.Target is null || !root.GrabAreaShown)
                return SmokeResult.Fail("desktop grabbing drill missing safe target or grab debug overlay");
            lesson.Action("moving");
            _stage = 6;
            _frames = 0;
        }
        if (_stage == 6 && ++_frames > 90)
        {
            if (!lesson.Moving || root.Session!.LocalPawn!.Health != 100)
                return SmokeResult.Fail("moving grab target injured the learner");
            root.Menu!.Open();
            _stage = 7;
            _frames = 0;
        }
        if (_stage == 7 && ++_frames > 8)
        {
            Capture(context, "grab-controls");
            return SmokeResult.Pass("desktop Tutorials launches shared vision/grab drills, measures recovery, returns with history, and supplies a safe moving target");
        }
        return SmokeResult.Running;
    }

    private static void Capture(SmokeContext context, string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        string folder = Path.GetFullPath("../local/browser-evidence/desktop-tutorials");
        Directory.CreateDirectory(folder);
        using Image image = context.Tree.Root.GetTexture().GetImage();
        image.SavePng(Path.Combine(folder, name + ".png"));
    }
}
