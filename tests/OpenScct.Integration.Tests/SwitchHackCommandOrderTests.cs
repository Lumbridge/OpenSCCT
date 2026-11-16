using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Game.GameFlow;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class SwitchHackCommandOrderTests
{
    [Fact]
    public void Reliable_interaction_phases_cannot_overtake_their_preceding_queued_state_call()
    {
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestBox", Side = PlaySide.Spy, Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline,
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
        Tick(600);
        var owner = Assert.IsType<SPlayerController>(session.LocalController);
        var calls = new List<string>();
        // Observe protocol dispatch independently of game authorization. Other tests below exercise the real handlers.
        foreach (string name in new[] { "PCChangeServerState", "InteractionInit", "Interaction", "InteractionPost" })
            session.Host.Driver.Functions.Register("SBase.SPlayerController", name, (_, _, _, _) => calls.Add(name));
        var host = GameFlowServices.Host(owner);
        host.ServerCall(owner, new GameCall("PCChangeServerState") { Name1 = new Name("s_MenuInteraction") });
        owner.InteractionInit(null, 0);
        owner.Interaction(null, 2);
        owner.InteractionPost(null, 1);
        Tick(120);
        Assert.Equal(new[] { "PCChangeServerState", "InteractionInit", "Interaction", "InteractionPost" }, calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void A_remote_switch_hack_keeps_its_start_and_completion_in_movement_order(bool delayed, bool batchedMenuUse)
    {
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestBox", Side = PlaySide.Spy, Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline, SimLink = delayed ? SimLinkProfile.B : null,
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
        Tick(600);
        var owner = Assert.IsType<SPlayerController>(session.LocalController);
        var authority = Assert.IsType<SPlayerController>(session.ServerController);
        var pawn = Assert.IsAssignableFrom<SBasePawn>(authority.Pawn);
        var panel = authority.XLevel.SpawnActor(new SInterrupteur
        {
            bStatic = false, bNoDelete = false, bAlwaysRelevant = true, RemoteRole = ENetRole.ROLE_SimulatedProxy,
            bCollideActors = false, bInteractInFront = false, iHackingTime = 1,
            bTriggerOnceOnly = true,
            fInteractionRadius = 100, fInteractionHeight = 100,
            UseRelativeLocation = new FVector(30, 0, 0), InteractionClass = new ClassRef("SBase.STriggerInteraction"),
        }, pawn.Location + new FVector(30, 0, 0), new FRotator(0, 32768, 0))!;
        var entry = Assert.IsType<STriggerInteraction>(panel.InteractionObj);
        entry.bAlwaysRelevant = true;
        entry.bUsable = true;
        entry.UsableBy = Usable.U_BOTH;
        Tick(180);
        var copy = Assert.IsType<STriggerInteraction>(session.Client.Driver.FindActorByNetGuid((uint)entry.ActorId));
        var states = new HashSet<string>();
        bool started = false;
        if (batchedMenuUse)
        {
            // A fast menu selection can send both reliable calls before the next movement fence.
            // It still uses the ordinary controller RPC and all server authorization/reach checks.
            owner.eventChangeState(new Name("s_MenuInteraction"));
            Assert.Same(copy, owner.IManager!.GetCurrentInteraction());
            owner.InteractionInit(copy, 0);
        }
        else
        {
            session.Input.Set("bUsing", true);
            Tick(12);
            Assert.True(owner.IsInState("s_MenuInteraction"), owner.GetStateName().ToString());
            Assert.Same(copy, owner.IManager!.GetCurrentInteraction());
            session.Input.Set("bUsing", false);
        }
        for (int i = 0; i < 720; i++)
        {
            Tick();
            states.Add(authority.GetStateName().ToString());
            started |= entry.IsInState("s_Hacking");
            if (started && !entry.IsInState("s_Hacking") && owner.GetStateName().ToString().StartsWith("s_PlayerWalking")) break;
        }
        Assert.True(started, "Authority never accepted the normal hack start: " + string.Join(",", states));
        Assert.False(entry.bInUse);
        Assert.False(entry.IsInState("s_Hacking"));
        Assert.False(entry.bUsable); // The source once-only effect is applied only on successful completion.
        Assert.Contains("s_Hack_Use", states);
    }
}
