using System.IO;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class MercCrouchTests
{
    [Theory]
    [InlineData(NetProfile.Stock)]
    [InlineData(NetProfile.Enhanced)]
    public void LocalHostCrouchLowersTheViewAndStandingRestoresItBeforeAndAfterRespawning(NetProfile profile)
    {
        var clock = new VirtualClock();
        PlaySession session;
        try
        {
            session = new PlaySession(new PlaySettings
            {
                Map = "SteeE",
                Side = PlaySide.Merc,
                Rules = PlayRules.PlayLevel,
                HostMode = PlayHostMode.Inline,
                NetworkProfile = profile,
                Bots = 0,
            }, clock);
        }
        catch (FileNotFoundException e)
        {
            Assert.Skip("SteeE is not converted (local only): " + e.Message);
            return;
        }

        using (session)
        {
            for (int i = 0; i < 10 * Sim.Hz && session.LocalPawn is null; i++) Step(1);
            var pawn = Assert.IsAssignableFrom<SPAWNDEFENSE>(session.LocalPawn);
            Step(2 * Sim.Hz);
            Assert.True(session.View.Valid);
            Assert.False(pawn.bIsCrouched);
            CrouchAndStand();
            CrouchAndStand();

            var authority = session.ServerController!;
            authority.GameReplicationInfo!.TimesRespawn = 1;
            authority.PawnTakeDamage(1000, null, authority.Pawn!.Location, default, SDamageType.SDTBase);
            Assert.True(authority.Pawn.Health <= 0, $"Merc did not die: health={authority.Pawn.Health}, state={authority.GetStateName()}");
            for (int i = 0; i < 20 * Sim.Hz &&
                (session.LocalPawn is not { Health: > 0 } || ReferenceEquals(session.LocalPawn, pawn)); i++) Step(1);
            Assert.False(ReferenceEquals(pawn, session.LocalPawn),
                $"Merc did not respawn: local={session.LocalController?.GetStateName()}, server={authority.GetStateName()}, " +
                $"health={authority.Pawn?.Health}, lives={authority.PlayerReplicationInfo?.NumLives}, delay={authority.GameReplicationInfo?.TimesRespawn}");
            Assert.IsAssignableFrom<SPAWNDEFENSE>(session.LocalPawn);
            Step(2 * Sim.Hz);
            CrouchAndStand();
        }

        void CrouchAndStand()
        {
            var current = Assert.IsAssignableFrom<SPAWNDEFENSE>(session.LocalPawn);
            var authoritative = Assert.IsAssignableFrom<SPAWNDEFENSE>(session.ServerController?.Pawn);
            Assert.False(current.bIsCrouched);
            float standingViewHeight = ViewHeight();

            ToggleCrouch();

            Assert.True(current.bIsCrouched);
            Assert.True(authoritative.bIsCrouched);
            Assert.True(session.LocalController!.IsInState(MovementStateNames.s_PlayerWalkingCr2H));
            Assert.Equal(68f, current.CollisionHeight);
            Assert.Equal(20f, current.BaseEyeHeight);
            Assert.True(ViewHeight() < standingViewHeight - 30f,
                $"Merc camera stayed at standing height: standing={standingViewHeight}, crouched={ViewHeight()}, eye={current.EyeHeight}");
            Assert.True(current.bUpdateEyeheight);
            Assert.Equal(20f, current.EyeHeight, 1);

            ToggleCrouch();

            Assert.False(current.bIsCrouched);
            Assert.False(authoritative.bIsCrouched);
            Assert.True(session.LocalController.IsInState(MovementStateNames.s_PlayerWalkingSt2H));
            Assert.Equal(standingViewHeight, ViewHeight(), 1);
        }

        void ToggleCrouch()
        {
            session.Input.Set("bCrouch", true);
            Step(1);
            session.Input.Set("bCrouch", false);
            Step(2 * Sim.Hz);
        }

        void Step(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                clock.Advance(1_000_000 / Sim.Hz);
                session.Step(clock.NowMicroseconds);
            }
        }

        float ViewHeight() => session.View.Location.Z -
            (session.LocalPawn!.Location.Z - session.LocalPawn.CollisionHeight);
    }
}
