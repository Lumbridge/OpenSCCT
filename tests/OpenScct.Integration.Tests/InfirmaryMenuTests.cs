using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class InfirmaryMenuTests
{
    [Theory]
    [InlineData("ClubE", false, false)]
    [InlineData("ClubE", false, true)]
    [InlineData("ClubE", true, false)]
    [InlineData("ClubE", true, true)]
    public void InfirmariesHealBothTeamsThroughTheUseMenu(string map, bool spy, bool latency)
        => CheckInfirmaries(map, spy, latency);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AquaPracticeInfirmariesHealBothTeams(bool spy) => CheckInfirmaries("AquaE", spy, false);

    private static void CheckInfirmaries(string map, bool spy, bool latency)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder(map) is null, $"{map} content is required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = map, Side = spy ? PlaySide.Spy : PlaySide.Merc, Rules = PlayRules.MapSettings,
            OfflineRules = map == "AquaE", UnlimitedGear = map == "AquaE",
            HostMode = PlayHostMode.Inline, MatchSeed = 3, SimLink = latency ? SimLinkProfile.B : null,
        }, clock);
        void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }
        for (int i = 0; i < 1800 && (session.LocalPawn is null || session.ServerController?.Pawn is null ||
            session.LocalController!.IsInState("WaitingForPawn")); i++) Tick();
        Assert.NotNull(session.LocalPawn);
        var pawn = (SBasePawn)session.ServerController!.Pawn!;
        var spawn = pawn.Location;
        var stations = new List<SInfirmary>();
        foreach (var crate in session.Host!.World.Level.AllActors<SInfirmary>()) stations.Add(crate);
        Assert.NotEmpty(stations);
        foreach (var crate in stations)
        {
            Assert.NotNull(crate.InteractionObj);
            var entry = crate.InteractionObj;
            var at = crate.Location + crate.Rotation.Vector() * 55f;
            bool placed = false;
            foreach (float distance in new[] { 55f, 80f, 110f, 140f })
            {
                foreach (float height in new[] { 0f, 50f, -40f, 100f })
                {
                    at = crate.Location + crate.Rotation.Vector() * distance + new FVector(0, 0, height);
                    if (pawn.SetLocation(at)) { placed = true; break; }
                }
                if (placed) break;
            }
            Assert.True(placed, $"Cannot place player in front of station at {crate.Location}");
            Assert.True(session.LocalPawn!.SetLocation(at));
            pawn.Rotation = session.LocalPawn.Rotation = (crate.Location - at).Rotation();
            session.LocalController!.Rotation = pawn.Rotation;
            session.LocalController.MyDesiredRotation = session.LocalController.LocalMyDesiredRotation = pawn.Rotation;
            session.ServerController.Rotation = session.ServerController.MyDesiredRotation =
                session.ServerController.LocalMyDesiredRotation = pawn.Rotation;
            Tick(90);
            // Teleport is fixture setup; restore walking and aim after the fall to the floor.
            pawn.SetPhysics(EPhysics.PHYS_Walking);
            session.LocalPawn.SetPhysics(EPhysics.PHYS_Walking);
            session.ServerController.GotoWaitingState();
            session.LocalController.GotoWaitingState();
            pawn.Rotation = session.LocalPawn.Rotation = (crate.Location - at).Rotation();
            session.LocalController.Rotation = session.LocalController.MyDesiredRotation =
                session.LocalController.LocalMyDesiredRotation = pawn.Rotation;
            session.ServerController.Rotation = session.ServerController.MyDesiredRotation =
                session.ServerController.LocalMyDesiredRotation = pawn.Rotation;
            Assert.Contains(session.ServerController.IManager!.Interactions, x => ReferenceEquals(x, entry));
            Assert.Contains(session.LocalController.IManager!.Interactions, x => x is SInfirmaryInteraction);
            Assert.True(session.LocalController.IManager.HasUsableInteractions(), $"Infirmary at {crate.Location} must be usable from the front.");
            Assert.Contains(session.LocalController.IManager.Interactions, x => x is SLeaveInteraction && x.CanUse(session.LocalController));
            pawn.Health = 40;
            Tick(30);
            session.Input.Set("bUsing", true);
            Tick(10);
            Assert.True(session.LocalController.IsInState("s_MenuInteraction"), $"station {crate.Location}; state {session.LocalController.GetStateName()} usable {session.LocalController.IManager.HasUsableInteractions()} facing {session.LocalPawn.Rotation.Yaw} desired {session.LocalController.LocalMyDesiredRotation.Yaw} using {session.LocalController.bUsing} check {session.LocalController.bCanCheckUsingButton}");
            Assert.IsType<SInfirmaryInteraction>(session.LocalController.IManager.GetCurrentInteraction());
            session.Input.Set("bUsing", false);
            Tick(90);
            Assert.Equal(100, pawn.Health);
            Assert.Equal(100, session.LocalPawn.Health);
            var away = map == "AquaE" ? spawn : at + crate.Rotation.Vector() * 150f;
            Assert.True(pawn.SetLocation(away));
            Assert.True(session.LocalPawn.SetLocation(away));
            Tick(90);
            Assert.DoesNotContain(session.ServerController.IManager.Interactions, x => ReferenceEquals(x, entry));
            Assert.DoesNotContain(session.LocalController.IManager.Interactions, x => x is SInfirmaryInteraction);

        }
    }
}
