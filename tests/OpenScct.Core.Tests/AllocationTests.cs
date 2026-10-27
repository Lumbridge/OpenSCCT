using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Core.Tests;

public sealed class AllocationTests
{
    /// <summary>Actor with a looping timer, a physics mode, tag events and presentation events every few ticks.</summary>
    private sealed class BusyActor : SleeperActor
    {
        public Name FireTag;
        public int Fired;

        protected internal override void Tick(float DeltaTime)
        {
            if (XLevel.TickCount % 7 == ActorId % 7)
            {
                TriggerEvent(FireTag, this, null);
                XLevel.World.Events.PlaySound(this, FireTag, slot: 1);
            }

            Location.X += DeltaTime;
            Rotation.Yaw += 100;
            Velocity = Rotation.Vector() * SvmMath.Sin(Rotation.Yaw);
        }

        protected internal override void Timer() => Fired++;

        protected internal override void Trigger(Actor? Other, Pawn? EventInstigator) => Fired++;
    }

    private sealed class NoAllocPhysics : IActorPhysics
    {
        public int Calls;

        public void performPhysics(Actor Actor, float DeltaSeconds)
        {
            Calls++;
            Actor.Location += Actor.Velocity * DeltaSeconds;
        }
    }

    [Fact]
    public void SteadyStateTickDoesNotAllocate()
    {
        var world = new World(7);
        var physics = new NoAllocPhysics();
        world.Level.PhysicsDriver = physics;
        Name[] tags = [new("GroupA"), new("GroupB"), new("GroupC")];
        var actors = new BusyActor[1000];
        for (int i = 0; i < actors.Length; i++)
        {
            BusyActor a = world.Level.Spawn<BusyActor>()!;
            a.Tag = tags[i % 3];
            a.FireTag = tags[(i + 1) % 3];
            a.SetTimer(0.05f + (i % 10) * 0.01f, true);
            a.Physics = i % 2 == 0 ? EPhysics.PHYS_Walking : EPhysics.PHYS_None;
            if (i % 3 == 0)
            {
                a.GotoState("Waiting"); // WaitForLanding then Sleep loops, then back to Idle
            }

            actors[i] = a;
        }

        // Base some actors on a later one so the deferred (base first) pass runs every tick.
        for (int i = 0; i + 1 < actors.Length; i += 5)
        {
            actors[i].SetBase(actors[i + 1], bNotifyActor: false);
        }

        // Warm up: grow every internal buffer and JIT every path.
        world.Step(120);

        long before = GC.GetAllocatedBytesForCurrentThread();
        world.Step(240);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.True(physics.Calls > 0);
        Assert.True(world.Events.Count > 0);
    }
}
