using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using Xunit;

namespace OpenScct.Net.Tests;

/// <summary>
/// Truth tables of the replication condition interpreter against reference formulas written from
/// spec/net/model.md (the Engine.Actor replication block), for every combination of the net inputs and the
/// properties a condition reads; the tree interpreter, the bound form and the parser must agree.
/// </summary>
public sealed class RepConditionTests
{
    private static readonly ENetRole[] Roles =
        [ENetRole.ROLE_None, ENetRole.ROLE_DumbProxy, ENetRole.ROLE_SimulatedProxy, ENetRole.ROLE_AutonomousProxy, ENetRole.ROLE_Authority];

    private static readonly PropertyReaders Readers = new();

    private static UcRepBlock ActorBlockOf(string member) =>
        Actor.UcMeta.Class.Replication.FindBlock(member) ?? throw new InvalidOperationException(member);

    private sealed class World3
    {
        public readonly World World = new(1, ENetMode.NM_DedicatedServer);
        public readonly TestMover Self;
        public readonly TestMover WorldBase;
        public readonly TestMover Platform;

        public World3()
        {
            Self = World.Level.Spawn<TestMover>()!;
            WorldBase = World.Level.Spawn<TestMover>()!;
            WorldBase.bWorldGeometry = true;
            Platform = World.Level.Spawn<TestMover>()!;
        }
    }

    /// <summary>Evaluates <paramref name="expr"/> three ways and checks they agree.</summary>
    private static bool Eval(RepExpr expr, Actor self, in RepFlags flags)
    {
        bool tree = RepExprEvaluator.Evaluate(expr, self, flags, Readers);
        bool bound = RepCondition.Bind(expr, self.GetUcClass(), Readers).Evaluate(self, flags);
        bool parsed = RepExprEvaluator.Evaluate(RepConditionParser.Parse(expr.ToString()), self, flags, Readers);
        Assert.Equal(tree, bound);
        Assert.Equal(tree, parsed);
        return tree;
    }

    [Fact]
    public void LocationBlockMatchesTheScriptRuleForEveryInput()
    {
        var w = new World3();
        RepExpr location = ActorBlockOf("Location").Condition;
        int rows = 0;
        int trueRows = 0;
        foreach (bool skip in new[] { false, true })
        {
            foreach (bool initial in new[] { false, true })
            {
                foreach (bool movement in new[] { false, true })
                {
                    foreach (bool updateSimulated in new[] { false, true })
                    {
                        foreach (int baseKind in new[] { 0, 1, 2 })
                        {
                            foreach (ENetRole remote in Roles)
                            {
                                TestMover a = w.Self;
                                a.bSkipActorPropertyReplication = skip;
                                a.bReplicateMovement = movement;
                                a.bUpdateSimulatedPosition = updateSimulated;
                                a.SetBase(baseKind switch { 0 => null, 1 => w.WorldBase, _ => w.Platform });
                                var flags = new RepFlags(initial, false, false, ENetRole.ROLE_Authority, remote);

                                // spec/net/model.md rule 7, Location.
                                bool worldBase = baseKind != 2;
                                bool expected = (!skip || initial) && movement &&
                                    ((remote == ENetRole.ROLE_AutonomousProxy && initial) ||
                                     (remote == ENetRole.ROLE_SimulatedProxy && (initial || updateSimulated) && worldBase) ||
                                     (remote == ENetRole.ROLE_DumbProxy && worldBase));
                                Assert.Equal(expected, Eval(location, a, flags));
                                rows++;
                                trueRows += expected ? 1 : 0;
                            }
                        }
                    }
                }
            }
        }

        Assert.Equal(240, rows);
        Assert.InRange(trueRows, 20, 120);
    }

    [Fact]
    public void AmbientSoundBlockReadsOwnershipAndClientAnim()
    {
        var w = new World3();
        RepExpr ambient = ActorBlockOf("AmbientSound").Condition;
        RepExpr pitch = ActorBlockOf("SoundPitch").Condition;
        foreach (bool skip in new[] { false, true })
        {
            foreach (bool initial in new[] { false, true })
            {
                foreach (bool owner in new[] { false, true })
                {
                    foreach (bool clientAnim in new[] { false, true })
                    {
                        foreach (bool sound in new[] { false, true })
                        {
                            foreach (ENetRole role in Roles)
                            {
                                TestMover a = w.Self;
                                a.bSkipActorPropertyReplication = skip;
                                a.bClientAnim = clientAnim;
                                a.AmbientSound = sound ? new AssetRef("Sounds.Hum") : AssetRef.None;
                                var flags = new RepFlags(initial, owner, false, role, ENetRole.ROLE_SimulatedProxy);
                                bool baseRule = (!skip || initial) && role == ENetRole.ROLE_Authority && (!owner || !clientAnim);
                                Assert.Equal(baseRule, Eval(ambient, a, flags));
                                Assert.Equal(baseRule && sound, Eval(pitch, a, flags));
                            }
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void CollisionBlockNeedsDirtAndCollision()
    {
        var w = new World3();
        RepExpr block = ActorBlockOf("bBlockPlayers").Condition;
        foreach (bool dirty in new[] { false, true })
        {
            foreach (bool collideActors in new[] { false, true })
            {
                foreach (bool collideWorld in new[] { false, true })
                {
                    foreach (bool initial in new[] { false, true })
                    {
                        TestMover a = w.Self;
                        a.bSkipActorPropertyReplication = true;
                        a.bCollideActors = collideActors;
                        a.bCollideWorld = collideWorld;
                        var flags = new RepFlags(initial, false, dirty, ENetRole.ROLE_Authority, ENetRole.ROLE_DumbProxy);
                        Assert.Equal(initial && dirty && (collideActors || collideWorld), Eval(block, a, flags));
                    }
                }
            }
        }
    }

    [Fact]
    public void PhysicsAndRotatingBlocksCompareEnums()
    {
        var w = new World3();
        RepExpr velocity = ActorBlockOf("Velocity").Condition;
        RepExpr rotating = ActorBlockOf("RotationRate").Condition;
        foreach (EPhysics physics in Enum.GetValues<EPhysics>())
        {
            foreach (ENetRole remote in Roles)
            {
                TestMover a = w.Self;
                a.bSkipActorPropertyReplication = false;
                a.bReplicateMovement = true;
                a.bUpdateSimulatedPosition = false;
                a.Physics = physics;
                var flags = new RepFlags(false, false, false, ENetRole.ROLE_Authority, remote);
                Assert.Equal(remote == ENetRole.ROLE_DumbProxy && physics == EPhysics.PHYS_Falling, Eval(velocity, a, flags));
                Assert.Equal(remote <= ENetRole.ROLE_SimulatedProxy && physics == EPhysics.PHYS_Rotating, Eval(rotating, a, flags));
            }
        }
    }

    [Fact]
    public void FunctionConditionsFollowTheRoleOfEachSide()
    {
        RepExpr toServer = ActorBlockOf("ChangeServerState").Condition;
        RepExpr toClient = ActorBlockOf("ChangeClientState").Condition;
        var w = new World3();
        foreach (ENetRole role in Roles)
        {
            foreach (ENetRole remote in Roles)
            {
                var flags = new RepFlags(false, false, false, role, remote);
                Assert.Equal(role < ENetRole.ROLE_Authority, Eval(toServer, w.Self, flags));
                Assert.Equal(role == ENetRole.ROLE_Authority, Eval(toClient, w.Self, flags));

                // The server checks a received call from the sender's view (roles exchanged).
                Assert.Equal(remote < ENetRole.ROLE_Authority, Eval(toServer, w.Self, flags.Swapped()));
            }
        }
    }

    [Fact]
    public void UnrealScriptSemantics()
    {
        var w = new World3();
        TestMover a = w.Self;
        var flags = new RepFlags(true, false, false, ENetRole.ROLE_Authority, ENetRole.ROLE_SimulatedProxy, ENetMode.NM_ListenServer);

        // A member of None reads as zero: no exception, false.
        a.SetBase(null);
        Assert.False(Eval(RepConditionParser.Parse("Base.bWorldGeometry"), a, flags));
        Assert.True(Eval(RepConditionParser.Parse("!Base.bWorldGeometry"), a, flags));
        Assert.True(Eval(RepConditionParser.Parse("Base == None"), a, flags));

        // Short-circuit: the right side is not needed.
        Assert.True(Eval(RepConditionParser.Parse("bNetInitial || Base.bWorldGeometry"), a, flags));

        // Precedence: && binds tighter than ||, comparisons tighter than both, ! tightest.
        Assert.True(Eval(RepConditionParser.Parse("true || false && false"), a, flags));
        Assert.False(Eval(RepConditionParser.Parse("(true || false) && false"), a, flags));
        Assert.True(Eval(RepConditionParser.Parse("!bNetOwner == true"), a, flags));

        // Net mode and enum literals.
        Assert.True(Eval(RepConditionParser.Parse("Level.NetMode == NM_ListenServer"), a, flags));
        Assert.True(Eval(RepConditionParser.Parse("RemoteRole <= ROLE_SimulatedProxy && Role > RemoteRole"), a, flags));

        // Assets compare with None by path.
        a.AmbientSound = AssetRef.None;
        Assert.False(Eval(RepConditionParser.Parse("AmbientSound != None"), a, flags));
        a.AmbientSound = new AssetRef("Sound.Thing");
        Assert.True(Eval(RepConditionParser.Parse("AmbientSound != None"), a, flags));

        // Hand-written fields (bHidden, DrawType) read like generated ones.
        a.bHidden = true;
        a.DrawType = EDrawType.DT_StaticMesh;
        Assert.True(Eval(RepConditionParser.Parse("bHidden && DrawType == DT_StaticMesh"), a, flags));

        // An unknown property reads as None and is reported.
        RepCondition c = RepCondition.Bind(RepConditionParser.Parse("bNoSuchProperty || bHidden"), a.GetUcClass(), Readers);
        Assert.True(c.Evaluate(a, flags));
        Assert.Equal(["bNoSuchProperty"], c.Unresolved);
    }

    [Fact]
    public void EveryGeneratedConditionBindsAndParses()
    {
        var index = new UcClassIndex(CorePackage.Info, EnginePackage.Info);
        var w = new World3();
        int checkedConditions = 0;
        var unresolved = new List<string>();
        foreach (UcClassInfo cls in index.Classes)
        {
            foreach (UcRepBlock block in cls.Replication.Blocks)
            {
                RepCondition c = RepCondition.Bind(block.Condition, cls, Readers);
                foreach (string name in c.Unresolved)
                {
                    unresolved.Add(cls.Path + ":" + name);
                }

                RepExpr reparsed = RepConditionParser.Parse(block.Condition.ToString());
                Assert.Equal(block.Condition.ToString(), reparsed.ToString());
                if (cls.IsA(Actor.UcMeta.Class) && w.Self.GetUcClass().IsA(cls))
                {
                    var flags = new RepFlags(true, true, true, ENetRole.ROLE_Authority, ENetRole.ROLE_SimulatedProxy);
                    _ = Eval(block.Condition, w.Self, flags);
                }

                checkedConditions++;
            }
        }

        Assert.Empty(unresolved);
        Assert.True(checkedConditions > 60, $"only {checkedConditions} conditions");
    }
}
