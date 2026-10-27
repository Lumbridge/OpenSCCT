using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests.Views;

/// <summary>
/// The upper-body aim (spec/native/aim-ik.md): the mesh instance's AimAt request and the IK step of each animation
/// update, and the TurnHead case that asks for it on an armed pawn, also on the bots' proxies a client draws.
/// </summary>
public sealed class AimIKTests
{
    private const float Dt = 1f / 60f;

    [Fact]
    public void FullAimStepsTowardTheShareOfTheAimAtTheIKSpeed()
    {
        var ik = new MeshAimIK();
        // Aim 30 degrees up and 40 degrees to the right of the facing (UE yaw +16384 is to the right).
        FVector facing = new FRotator(0, 0, 0).Vector();
        FVector view = new FRotator((int)(30f * 65536f / 360f), (int)(40f * 65536f / 360f), 0).Vector();
        float pitchTarget = 0.85f * MathF.Asin(view.Z);
        float yawTarget = -0.85f * (40f * MathF.PI / 180f);
        ik.AimAt(MeshAimIK.AimType.AAFULL, view, facing, -90f, 90f, -90f, 90f);
        ik.Update(Dt);
        // The larger change (yaw) moves at the full 8 rad/s, the pitch slowed so both arrive together.
        Assert.Equal(-8f * Dt, ik.FullYaw, 4);
        Assert.Equal(8f * Dt * pitchTarget / -yawTarget, ik.FullPitch, 4);
        Assert.Equal(0, ik.Request);
        for (int i = 0; i < 60; i++)
        {
            ik.AimAt(MeshAimIK.AimType.AAFULL, view, facing, -90f, 90f, -90f, 90f);
            ik.Update(Dt);
        }

        Assert.Equal(yawTarget, ik.FullYaw, 3);
        Assert.Equal(pitchTarget, ik.FullPitch, 3);

        // Without a request the pair returns to rest at the return speed (8 rad/s).
        ik.Update(Dt);
        Assert.True(MathF.Abs(ik.FullYaw) < MathF.Abs(yawTarget));
        for (int i = 0; i < 60; i++)
        {
            ik.Update(Dt);
        }

        Assert.Equal(0f, ik.FullYaw);
        Assert.Equal(0f, ik.FullPitch);
    }

    [Fact]
    public void FullAimKeepsWithinTheShareOfTheLimits()
    {
        var ik = new MeshAimIK();
        FVector facing = new FVector(1f, 0f, 0f);
        FVector behindLeft = new FVector(-1f, -0.2f, 0f).SafeNormal();
        for (int i = 0; i < 120; i++)
        {
            ik.AimAt(MeshAimIK.AimType.AAFULL, behindLeft, facing, -90f, 90f, -90f, 90f);
            ik.Update(Dt);
        }

        Assert.Equal(0.85f * 90f * 0.017453f, ik.FullYaw, 3);
    }

    /// <summary>
    /// SteeE with three bots as the launchers play it: on the client, every armed merc bot's proxy asks for the aim
    /// (TurnHead's IKT_WEAPONLOADED case) toward the pitch and yaw its owner aims at.
    /// </summary>
    [Fact]
    public void ArmedBotProxiesAimTheirUpperBody()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings { Map = "SteeE", HostMode = PlayHostMode.Inline, Bots = 3 }, clock);
        var aimed = new Dictionary<int, int>();
        var armed = new Dictionary<int, int>();
        for (int t = 1; t <= 20 * Sim.Hz; t++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(t, Sim.Hz));
            session.Step(clock.NowMicroseconds);
            if (t < 5 * Sim.Hz)
            {
                continue;
            }

            foreach (SPAWNDEFENSE p in session.Client.World.Level.AllActors<SPAWNDEFENSE>())
            {
                if (p.Role != ENetRole.ROLE_SimulatedProxy || p.bDeleteMe || p.WeaponLoaded == 0 || p.IK_Type != IK_Type_.IKT_WEAPONLOADED)
                {
                    continue;
                }

                armed[p.ActorId] = armed.GetValueOrDefault(p.ActorId) + 1;
                MeshAimIK aim = p.PawnAnim.Aim;
                var want = new FRotator(p.iDesiredYawPitch >> 16, p.iDesiredYawPitch & 0xffff, p.Rotation.Roll).Vector();
                if (!aim.ViewDir.IsZero() && FVector.Dist(aim.ViewDir, want) < 0.05f)
                {
                    aimed[p.ActorId] = aimed.GetValueOrDefault(p.ActorId) + 1;
                }
            }
        }

        Assert.NotEmpty(armed);
        foreach ((int id, int frames) in armed)
        {
            Assert.True(aimed.GetValueOrDefault(id) >= frames * 9 / 10, $"merc proxy #{id}: aimed toward its desired rotation in {aimed.GetValueOrDefault(id)} of {frames} armed samples");
        }
    }
}
