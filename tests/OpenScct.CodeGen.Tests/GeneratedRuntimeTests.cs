using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.CodeGen.Tests;

/// <summary>
/// Checks the generated code that is in the repository (Core, Engine, SBase): the metadata is consistent with the
/// C# types, every class can be created with its defaults, states and replication data work as documented.
/// </summary>
public sealed class GeneratedRuntimeTests
{
    private static readonly UcClassIndex Index = new(CorePackage.Info, EnginePackage.Info, SBasePackage.Info);

    [Fact]
    public void PackagesListEveryGeneratedClassOnce()
    {
        Assert.Equal("Core", CorePackage.Info.Name);
        Assert.Contains(Index.Classes, c => c.Path == "Core.Object");
        Assert.Contains(Index.Classes, c => c.Path == "Engine.Actor");
        Assert.Contains(Index.Classes, c => c.Path == "SBase.SInterrupteur");
        Assert.Equal(Index.Classes.Count, Index.Classes.Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(SBasePackage.Info.Classes, c => Assert.Equal("SBase", c.Package));
        Assert.Equal(SBasePackage.Info.Classes.Select(c => c.Name).Order(StringComparer.Ordinal), SBasePackage.Info.Classes.Select(c => c.Name));
    }

    [Fact]
    public void HierarchyMatchesTheCSharpTypes()
    {
        foreach (UcClassInfo c in Index.Classes)
        {
            if (c.Super is null)
            {
                Assert.Equal("Core.Object", c.Path);
                Assert.Equal(typeof(UObject), c.Type);
                continue;
            }

            Assert.Equal(c.Super.Type, c.Type.BaseType);
            Assert.Equal(typeof(Actor).IsAssignableFrom(c.Type), c.IsActor);
            if (!c.IsActor && c.Path != "Core.Object")
            {
                Assert.StartsWith("U", c.Type.Name, StringComparison.Ordinal);
            }
            else if (c.IsActor)
            {
                Assert.Equal(c.Name, c.Type.Name);
            }
        }
    }

    [Fact]
    public void EveryClassWithAFactoryCreatesItselfWithItsMetadata()
    {
        int created = 0;
        foreach (UcClassInfo c in Index.Classes.Where(c => c.Factory is not null))
        {
            UObject o = c.Create()!;
            Assert.IsType(c.Type, o);
            Assert.Same(c, o.GetUcClass());
            created++;
        }

        Assert.True(created > 300, $"only {created} classes have factories");
        Assert.All(Index.Classes.Where(c => c.IsAbstract), c => Assert.Null(c.Factory));
    }

    [Fact]
    public void EveryPropertyWithAMemberExistsOnTheType()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (UcClassInfo c in Index.Classes)
        {
            foreach (UcPropertyInfo p in c.Properties)
            {
                Assert.Same(c, p.Class);
                if (p.CSharpName.Length == 0)
                {
                    continue;
                }

                MemberInfo? m = (MemberInfo?)c.Type.GetField(p.CSharpName, flags) ?? c.Type.GetProperty(p.CSharpName, flags);
                Assert.True(m is not null, $"{p} has no member {p.CSharpName} on {c.Type}");
                Type memberType = m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType;
                if (p.IsStaticArray)
                {
                    Assert.True(memberType.IsArray, $"{p} should be an array");
                }
                else if (p.Kind == UcPropertyKind.Asset)
                {
                    Assert.Equal(typeof(AssetRef), memberType);
                }
                else if (p.Kind == UcPropertyKind.Class)
                {
                    Assert.Equal(typeof(ClassRef), memberType);
                }
            }
        }
    }

    [Fact]
    public void DefaultsComeFromTheDecodedClassDefaults()
    {
        var s = new SInterrupteur();
        Assert.Equal(new FVector(55f, 0f, 0f), s.UseRelativeLocation);
        Assert.Equal(new FRotator(0, 32768, 0), s.UseRelativeRotation);
        Assert.Equal(EDrawType.DT_StaticMesh, s.DrawType);
        Assert.Equal(ENetRole.ROLE_SimulatedProxy, s.RemoteRole);
        Assert.Equal(ENetRole.ROLE_Authority, s.Role);
        Assert.True(s.bNoDelete);
        Assert.Equal(new ClassRef("SBase.STriggerInteraction"), s.InteractionClass);
        Assert.Equal("trigger_standard", s.StaticMesh.Name);
        Assert.Equal(22f, s.CollisionRadius);

        // A property that shadows an ancestor's takes the default; the shadowed one keeps its own.
        var frag = new SProjFrag();
        Assert.Equal(334, frag.Damage);
        Assert.Equal(0f, ((Projectile)frag).Damage);
    }

    [Fact]
    public void ManifestHasUnitIdsAndShaValues()
    {
        UcClassInfo s = SInterrupteur.UcMeta.Class;
        UcFunctionInfo trigger = s.FindOwnFunction("Trigger")!;
        Assert.Equal("uc:SBase.SInterrupteur:Trigger", trigger.UnitId);
        Assert.Equal("SBase.SInterrupteur.Trigger", trigger.UcPath);
        Assert.Matches("^[0-9a-f]{40}$", trigger.Sha1);
        Assert.Equal(["Other", "EventInstigator"], trigger.Parameters.Select(p => p.Name));
        Assert.Equal("def:SBase.SInterrupteur", s.DefaultsUnitId);

        UcFunctionInfo main = UCommandlet.UcMeta.Class.FindOwnFunction("Main")!;
        Assert.True(main.IsNative);
        Assert.Equal("nat:Core.Commandlet.Main", main.UnitId);

        UcStateInfo open = Mover.UcMeta.Class.State("OpenTimedMover");
        Assert.Equal("uc:Engine.Mover:OpenTimedMover@code", open.CodeUnitId);
        Assert.Same(open, Index.FindStateCode(open.CodeUnitId!));
        Assert.Same(trigger, Index.FindFunction("SInterrupteur.Trigger"));
        Assert.Same(trigger, Index.FindFunction(trigger.UnitId));
        UcFunctionInfo stateFunc = SBasicGamePlayObject.UcMeta.Class.State("s_LockedByChaff").FindFunction("Timer")!;
        Assert.Equal("uc:SBase.SBasicGamePlayObject:s_LockedByChaff.Timer", stateFunc.UnitId);
    }

    [Fact]
    public void UcAttributesInTheRuntimeMatchTheManifest()
    {
        IReadOnlyList<UcManifestCheck.Problem> problems = UcManifestCheck.Check(Index, typeof(Actor).Assembly, typeof(SBasePackage).Assembly);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        string sha = SInterrupteur.UcMeta.Class.FindOwnFunction("Trigger")!.Sha1;
        Assert.Null(UcManifestCheck.CheckOne(Index, "SBase.SInterrupteur.Trigger", sha));
        Assert.NotNull(UcManifestCheck.CheckOne(Index, "SBase.SInterrupteur.Trigger", new string('0', 40)));
        Assert.NotNull(UcManifestCheck.CheckOne(Index, "SBase.SInterrupteur.NoSuchFunction", null));
        Assert.Null(UcManifestCheck.CheckOne(Index, "SBase.SBasicGamePlayObject.s_LockedByChaff", null));
    }

    [Fact]
    public void StatesBuildActorStatesFromTheirMetadata()
    {
        UcStateInfo info = Mover.UcMeta.OpenTimedMover;
        Assert.Equal(["Open", "Close"], info.Labels.Select(l => l.Name));
        Assert.Equal([1, 87], info.LabelPcs.Select(l => l.Pc));
        var open = new TestState(info, null);
        Assert.Equal("Mover.OpenTimedMover", open.QualifiedName);
        Assert.Equal(2, open.Labels.Length);

        UcStateInfo stand = Mover.UcMeta.Class.State("StandOpenTimed");
        Assert.Equal("OpenTimedMover", stand.Extends);
        var standState = new TestState(stand, open);
        var table = new StateTable(Actor.ClassStates, open, standState);
        Assert.Same(standState, table.Find("StandOpenTimed"));
        Assert.True(standState.IsOrExtends("OpenTimedMover"));
        Assert.Throws<ArgumentException>(() => new TestState(stand, null));
        Assert.Throws<ArgumentException>(() => new TestState(info, standState));

        UcStateInfo redefined = SCamera.UcMeta.Class.State("s_LockedByChaff");
        Assert.Equal("s_LockedByChaff", redefined.Extends);
    }

    [Fact]
    public void ReplicationIsData()
    {
        UcReplication rep = SInterrupteur.UcMeta.Class.Replication;
        Assert.Equal(2, rep.Blocks.Length);
        Assert.Equal("bNetInitial && Role == ROLE_Authority", rep.Blocks[0].Condition.ToString());
        Assert.True(rep.Blocks[0].Condition.Uses(RepNetVar.bNetInitial));
        Assert.Same(rep.Blocks[1], rep.FindBlock("iReusableTime"));

        UcRepMember setCarrier = SFlag.UcMeta.Class.Replication.Blocks.SelectMany(b => b.Members).Single(m => m.Name == "SetFlagCarrier");
        Assert.True(setCarrier.IsFunction);
        Assert.True(setCarrier.IsNetAllClients);
        Assert.True(SFlag.UcMeta.Class.FindOwnFunction("SetFlagCarrier")!.IsNetAllClients);
    }

    [Fact]
    public void ActorMovementConditionEvaluates()
    {
        UcRepBlock location = Actor.UcMeta.Class.Replication.FindBlock("Location")!;
        var actor = new SInterrupteur { bReplicateMovement = true, bSkipActorPropertyReplication = false };
        bool Eval(ENetRole remote, bool initial) => RepEval.Bool(location.Condition, actor, new RepEval.Inputs(ENetRole.ROLE_Authority, remote, initial, false, true));
        Assert.True(Eval(ENetRole.ROLE_SimulatedProxy, initial: true));
        Assert.False(Eval(ENetRole.ROLE_SimulatedProxy, initial: false));
        Assert.True(Eval(ENetRole.ROLE_DumbProxy, initial: false));
        Assert.True(Eval(ENetRole.ROLE_AutonomousProxy, initial: true));
        Assert.False(Eval(ENetRole.ROLE_AutonomousProxy, initial: false));
        actor.bReplicateMovement = false;
        Assert.False(Eval(ENetRole.ROLE_DumbProxy, initial: true));
    }

    private sealed class TestState : UcActorState
    {
        public TestState(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }
    }

    /// <summary>A small reference interpreter for RepExpr over generated fields (the real one belongs in OpenScct.Net).</summary>
    private static class RepEval
    {
        public readonly record struct Inputs(ENetRole Role, ENetRole RemoteRole, bool bNetInitial, bool bNetOwner, bool bNetDirty);

        public static bool Bool(RepExpr e, object self, Inputs inputs) => Value(e, self, inputs) switch
        {
            bool b => b,
            null => false,
            var v => throw new InvalidOperationException($"{e} is {v}, not a bool"),
        };

        private static object? Value(RepExpr e, object self, Inputs inputs)
        {
            switch (e.Kind)
            {
                case RepExprKind.Bool:
                    return e.Value != 0;
                case RepExprKind.Int:
                case RepExprKind.Enum:
                    return e.Value;
                case RepExprKind.None:
                    return null;
                case RepExprKind.NetVar:
                    return e.NetVar switch
                    {
                        RepNetVar.Role => (int)inputs.Role,
                        RepNetVar.RemoteRole => (int)inputs.RemoteRole,
                        RepNetVar.bNetInitial => inputs.bNetInitial,
                        RepNetVar.bNetOwner => inputs.bNetOwner,
                        RepNetVar.bNetDirty => inputs.bNetDirty,
                        _ => 0,
                    };
                case RepExprKind.Property:
                    return Read(self, e.Name!);
                case RepExprKind.Member:
                    return Value(e.Left!, self, inputs) is { } target ? Read(target, e.Name!) : null;
                case RepExprKind.Not:
                    return !Bool(e.Left!, self, inputs);
                case RepExprKind.And:
                    return Bool(e.Left!, self, inputs) && Bool(e.Right!, self, inputs);
                case RepExprKind.Or:
                    return Bool(e.Left!, self, inputs) || Bool(e.Right!, self, inputs);
                default:
                {
                    object? a = Value(e.Left!, self, inputs);
                    object? b = Value(e.Right!, self, inputs);
                    if (e.Kind is RepExprKind.Equal or RepExprKind.NotEqual)
                    {
                        bool equal = a is int ia && b is int ib ? ia == ib : Equals(a, b);
                        return e.Kind == RepExprKind.Equal ? equal : !equal;
                    }

                    int x = (int)a!;
                    int y = (int)b!;
                    return e.Kind switch
                    {
                        RepExprKind.Less => x < y,
                        RepExprKind.LessEqual => x <= y,
                        RepExprKind.Greater => x > y,
                        _ => x >= y,
                    };
                }
            }
        }

        private static object? Read(object target, string name)
        {
            UcPropertyInfo p = ((UObject)target).GetUcClass().FindProperty(name) ?? throw new InvalidOperationException($"no property {name}");
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            object? value = (object?)target.GetType().GetField(p.CSharpName, flags)?.GetValue(target)
                ?? target.GetType().GetProperty(p.CSharpName, flags)?.GetValue(target);
            return value is Enum en ? Convert.ToInt32(en, System.Globalization.CultureInfo.InvariantCulture) : value;
        }
    }
}
