using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests.Views;

/// <summary>
/// The merc's first-person arms (spec/equipment/merc-rifle.md rule 24): s_Selected's tick runs AnimateWaiting every
/// tick, so the arms play the idle loop while the merc stands still and the walk loop while he moves.
/// </summary>
public sealed class MercArmsTests
{
    [Fact]
    public void ArmsPlayTheIdleLoopWhileTheMercStandsStill()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings { Map = "SteeE", HostMode = PlayHostMode.Inline, Bots = 0, Side = PlaySide.Merc }, clock);
        int still = 0;
        int idle = 0;
        for (int t = 1; t <= 10 * Sim.Hz; t++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(t, Sim.Hz));
            session.Step(clock.NowMicroseconds);
            if (t < 5 * Sim.Hz || session.LocalPawn is not SPAWNDEFENSE { MyWeapon: SWeaponDefense rifle } merc ||
                !rifle.IsInState("s_Selected") || merc.Velocity.Size() > 1f)
            {
                continue;
            }

            still++;
            if (rifle.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL) == ViewModelClips.FirstWaitStFd2)
            {
                idle++;
            }
        }

        Assert.True(still > Sim.Hz, $"the merc stood still with the rifle selected for {still} ticks");
        Assert.True(idle >= still * 9 / 10, $"the arms played FirstWaitStFd2 in {idle} of {still} still ticks");
    }
}
