using System.IO;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class VisitMapGearTests
{
    [Fact]
    public void PracticeCanSwitchTeamsAndRespawnInBothDirections()
    {
        Assert.SkipWhen(OpenScct.Server.MapCatalog.CreateDefault().FindFolder("TestRange") is null, "TestRange content required.");
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestRange", Side = PlaySide.Merc, UnlimitedGear = true,
            HostMode = PlayHostMode.Inline,
        }, clock);
        void StepUntil(System.Func<bool> ready)
        {
            for (int i = 0; i < 15 * Sim.Hz && !ready(); i++)
            {
                clock.Advance(1_000_000 / Sim.Hz);
                session.Step(clock.NowMicroseconds);
            }
            Assert.True(ready());
        }
        StepUntil(() => session.LocalPawn is { Health: > 0 });
        foreach (int team in new[] { 0, 1 })
        {
            var previous = session.LocalPawn;
            session.RequestPracticeTeamSwap();
            StepUntil(() => session.LocalController is { } pc && SGameInfo.TeamIndexOf(pc) == team &&
                session.LocalPawn is { Health: > 0 } pawn && !ReferenceEquals(previous, pawn) &&
                (team == 0 ? pawn is SPawnAttaque : pawn is SPAWNDEFENSE));
        }
    }

    [Theory]
    [InlineData(PlaySide.Spy, true)]
    [InlineData(PlaySide.Merc, true)]
    [InlineData(PlaySide.Spy, false)]
    [InlineData(PlaySide.Merc, false)]
    public void VisitEquipmentDoesNotConsumeChargesOnEitherMachine(PlaySide side, bool unlimited)
    {
        var clock = new VirtualClock();
        PlaySession session;
        try
        {
            session = new PlaySession(new PlaySettings
            {
                Map = "SteeE",
                Side = side,
                OfflineRules = true,
                UnlimitedGear = unlimited,
                HostMode = PlayHostMode.Inline,
            }, clock);
        }
        catch (FileNotFoundException e)
        {
            Assert.Skip("SteeE is not converted (local only): " + e.Message);
            return;
        }

        using (session)
        {
            for (int i = 0; i < 10 * Sim.Hz && session.LocalPawn is null; i++)
            {
                clock.Advance(1_000_000 / Sim.Hz);
                session.Step(clock.NowMicroseconds);
            }

            Assert.NotNull(session.LocalPawn);
            CheckCharges(session.Client.World.Level, unlimited);
            CheckCharges(session.Host!.World.Level, unlimited);
        }
    }

    private static void CheckCharges(Level level, bool unlimited)
    {
        Assert.Equal(unlimited, level.GetService<PracticeRules>()?.On == true);
        var gadget = level.Spawn<SGadSmokeGrenade>();
        Assert.NotNull(gadget);
        gadget.Charge = 3;
        for (int i = 0; i < 20; i++)
        {
            gadget.DecCharge();
            gadget.ServerDecCharge();
        }

        Assert.Equal(unlimited ? 3 : -37, gadget.Charge);
    }
}
