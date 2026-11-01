using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Hud;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Real scope handover with a carried pistol, through simulation, view model and HUD.</summary>
public sealed class ScopeLifecycleSmoke : SmokeTest
{
    public const string TestName = "scope-lifecycle";
    public override string Name => TestName;
    public override SmokeResult Step(SmokeContext context)
    {
        var world = new World(1, ENetMode.NM_Standalone);
        var level = world.Level;
        var pawn = level.SpawnActor(new SPawnDefense_1Mesh(), new FVector(0, 0, 100), default)!;
        var pc = level.SpawnActor(new SPlayerController(), pawn.Location, default)!;
        pc.Possess(pawn);
        var rifle = level.SpawnActor(new SWeaponDefense(), pawn.Location, default)!;
        rifle.Instigator = pawn;
        rifle.SetOwner(pawn);
        rifle.GiveTo(pawn);
        SidearmEquipment.Give(pawn, SidearmKind.Suppressed);
        pawn.WeaponLoaded = 2;
        pc.GotoState(new Name("s_PlayerWalkingSt2H"));
        pc.bBehindView = false;
        pc.ViewTarget = pawn;
        var view = new FirstPersonView();
        context.Tree.Root.AddChild(view);
        string? Check(bool gun, HudBinocularMode scope, string phase)
        {
            view.OnTick(pc);
            view.Draw(pc, pawn.Location + new FVector(0, 0, 60), pc.Rotation, Sim.DeltaTime);
            var hud = new HudSnapshot();
            HudSimReader.Read(pc, hud);
            return view.Shown != gun || hud.Binoculars.Mode != scope
                ? $"{phase}: gun={view.Shown}, scope={hud.Binoculars.Mode}; expected {gun}/{scope}" : null;
        }
        try
        {
            world.Step(6);
            if (Check(true, HudBinocularMode.None, "before raise") is { } before) return SmokeResult.Fail(before);
            pc.SwitchBinocularVision();
            if (Check(true, HudBinocularMode.None, "raise started") is { } start) return SmokeResult.Fail(start);
            for (int tick = 0; tick < 15; tick++)
            {
                world.Step();
                if (Check(true, HudBinocularMode.None, $"raise tick {tick}") is { } raise) return SmokeResult.Fail(raise);
            }
            world.Step(6);
            for (int tick = 0; tick < 30; tick++)
            {
                world.Step();
                if (Check(false, HudBinocularMode.Merc, $"scoped tick {tick}") is { } scoped) return SmokeResult.Fail(scoped);
                if (pawn.SidearmWeapon!.bDrawWeapon) return SmokeResult.Fail("carried pistol became visible while scoped");
            }
            pc.SwitchBinocularVision();
            pc.eventPlayerMove(Sim.DeltaTime);
            if (Check(true, HudBinocularMode.None, "exit") is { } exit) return SmokeResult.Fail(exit);
            return SmokeResult.Pass("carried pistol: gun visible during raise; scope replaces gun for 30 ticks; exit restores gun");
        }
        finally { view.Free(); }
    }
}
