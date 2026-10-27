using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests.Views;

/// <summary>
/// What another player sees of a merc's melee (spec/combat/melee-elbow-charge-berserk.md rule 33): the server and the
/// connected client (two worlds, SteeE offline with server bots); a merc on the server spins and then whiffs an
/// elbow, and the client's proxy of him must play AtakStAlTu on channel 0 and AtakStAlFd2 on channel 19.
/// </summary>
public sealed class MercMeleeProxyTests
{
    private static readonly Name AtakStAlTu = new("AtakStAlTu");
    private static readonly Name AtakStAlFd2 = new("AtakStAlFd2");
    private static readonly Name s_Berserk360 = new("s_Berserk360");

    [Fact]
    public void AProxyDrawsTheMercBerserkAndElbow()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings { Map = "SteeE", HostMode = PlayHostMode.Inline, Bots = 3 }, clock);
        long t = 0;

        void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                t++;
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(t, Sim.Hz));
                session.Step(clock.NowMicroseconds);
            }
        }

        Run(5 * Sim.Hz);
        SBasePawn? found = null;
        foreach (SBasePawn p in session.Host.World.Level.AllActors<SBasePawn>())
        {
            if (found is null && p is SPAWNDEFENSE { bDeleteMe: false, Health: > 0, bIsCrouched: false } && p.Controller is SPlayerController)
            {
                found = p;
            }
        }

        SBasePawn merc = found ?? throw new Xunit.Sdk.XunitException("no merc on the server");
        var pc = (SPlayerController)merc.Controller!;
        string name = merc.PlayerReplicationInfo!.PlayerName;
        // The berserk start of the weapon walking Use key (UseEdgeWeapon).
        pc.fLastChargingTime = -100f;
        merc.ChangeEnumAction(SBasePawn.SA_BERSERK_ATTACK);
        pc.fLastBerserkTime = merc.Level.TimeSeconds;
        pc.eventChangeState(s_Berserk360);
        Assert.True(pc.IsInState(s_Berserk360), pc.GetStateName().ToString());

        SBasePawn? proxy = null;
        bool spun = false;
        for (int i = 0; i < Sim.Hz && !spun; i++)
        {
            Run(1);
            proxy = null;
            foreach (SBasePawn p in session.Client.World.Level.AllActors<SBasePawn>())
            {
                if (!p.bDeleteMe && p.Role == ENetRole.ROLE_SimulatedProxy && p.PlayerReplicationInfo?.PlayerName == name)
                {
                    proxy = p;
                }
            }

            spun = proxy is not null && proxy.PawnAnim[0].Sequence == AtakStAlTu;
        }

        Assert.True(spun, $"proxy channel 0 {proxy?.PawnAnim[0].Sequence}");

        Run(2 * Sim.Hz);
        // Within 3 s of the spin: no second berserk, the elbow whiff.
        Assert.True(pc.FindPawnToElbowShotAndGoForIt(true));
        bool struck = false;
        for (int i = 0; i < Sim.Hz && !struck; i++)
        {
            Run(1);
            struck = proxy!.PawnAnim[19].Sequence == AtakStAlFd2 && proxy.PawnAnim[19].BlendAlpha > 0.99f;
        }

        Assert.True(struck, $"proxy channel 19 {proxy!.PawnAnim[19].Sequence} alpha {proxy.PawnAnim[19].BlendAlpha}");
    }
}
