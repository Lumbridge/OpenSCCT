using OpenScct.Net;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScctGame.Hud;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>Checks the merc's actual replicated crate entries reach the rendered HUD list.</summary>
public sealed class AmmoMenuHudSmoke : SmokeTest
{
    private sealed class StepClock : IClock { public long NowMicroseconds { get; set; } }
    public const string TestName = "ammo-menu-hud";
    public override string Name => TestName;
    public override SmokeResult Step(SmokeContext context)
    {
        var assets = HudAssets.TryLoad(null, out string? error);
        if (assets is null) return SmokeResult.Fail(error ?? "no HUD assets");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "ClubE", Side = PlaySide.Merc, Rules = PlayRules.MapSettings,
            HostMode = PlayHostMode.Inline, MatchSeed = 3,
        }, clock);
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }
        for (int i = 0; i < 1800 && (session.LocalPawn is null || session.ServerController?.Pawn is null ||
            session.LocalController!.IsInState("WaitingForPawn")); i++) Tick(1);
        if (session.LocalPawn is not { } local || session.ServerController?.Pawn is not SBasePawn pawn)
            return SmokeResult.Fail("merc did not spawn");
        int checkedCrates = 0;
        foreach (var crate in session.Host!.World.Level.AllActors<SAmmoStock>())
        {
            var at = crate.Location + crate.Rotation.Vector() * 100f + new FVector(0, 0, 50);
            pawn.SetLocation(at);
            local.SetLocation(at);
            pawn.Rotation = local.Rotation = (crate.Location - at).Rotation();
            session.LocalController!.Rotation = pawn.Rotation;
            Tick(90);
            var pc = session.LocalController;
            pc.GotoWaitingState();
            var snapshot = new HudSnapshot();
            HudSimReader.Read(pc, snapshot);
            if (snapshot.Side != HudSide.Merc || !snapshot.ShowInteractions)
                return SmokeResult.Fail($"crate menu hidden: state={pc.GetStateName()} rows={snapshot.Interactions.Count}");
            var page = new InteractionPage(assets);
            for (int i = 0; i < 30; i++) page.Update(snapshot, 1f / 60f);
            page.Draw(new Vector2(1280, 720));
            for (int i = 0; i < snapshot.Interactions.Count; i++)
                if (page.DrawList.Find($"ListChoix#{i}/Choice")?.Text != snapshot.Interactions[i])
                    return SmokeResult.Fail("replicated interaction missing from drawn menu");
            snapshot.HideHud = true;
            page.Update(snapshot, 1f / 60f);
            if (page.IsOpen) return SmokeResult.Fail("menu ignores hidden HUD");
            checkedCrates++;
        }
        return checkedCrates == 0 ? SmokeResult.Fail("no ammo boxes")
            : SmokeResult.Pass($"merc menu drawn at all {checkedCrates} Clubhouse ammo boxes");
    }
}


