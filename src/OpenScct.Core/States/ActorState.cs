using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;

namespace OpenScct.Core;

/// <summary>
/// One UnrealScript state (UE2 UState). Each state is a C# class with a single shared instance; all per-actor data
/// lives in the actor (fields plus its <see cref="LatentFrame"/>).
/// <para>The pattern, which a generator can emit mechanically:</para>
/// <list type="bullet">
/// <item><c>state B extends A</c>: class B derives from class A, and passes A's instance as <c>super</c>.</item>
/// <item>A subclass redefining a parent class's state derives from the parent's state class, with the parent's
/// instance as <c>super</c> (UnrealScript does the same implicitly).</item>
/// <item>A state function overriding an Actor event overrides the matching virtual here (<c>self</c> first).
/// Functions declared by Actor subclasses use a nested interface on the declaring class, see the README.</item>
/// <item><c>ignores F</c> is an override of F with an empty body.</item>
/// <item><c>Global.F()</c> calls the actor's own virtual F (the class-level version).</item>
/// <item>BeginState and EndState are ordinary overrides.</item>
/// <item>Latent state code is <see cref="Code"/>: a switch on <see cref="LatentFrame.Pc"/>, see the README.</item>
/// </list>
/// </summary>
public abstract class ActorState
{
    /// <summary>The "no state" node: every function goes straight to the class-level version.</summary>
    public static readonly ActorState None = new NoState();

    private readonly StateLabel[] _labels;

    /// <param name="className">UnrealScript class that declares this state.</param>
    /// <param name="stateName">UnrealScript state name.</param>
    /// <param name="super">The state this one extends (or redefines), or null.</param>
    /// <param name="isAuto">True for an <c>auto state</c>.</param>
    /// <param name="labels">Labels of this state's own latent code with their entry Pc values (1 or more).</param>
    protected ActorState(string className, string stateName, ActorState? super = null, bool isAuto = false,
        params ReadOnlySpan<(string Name, int Pc)> labels)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentException.ThrowIfNullOrEmpty(stateName);
        ClassName = className;
        Name = new Name(stateName);
        QualifiedName = className + "." + stateName;
        Id = StableHash.OfNameCaseless(QualifiedName);
        Super = super;
        IsAuto = isAuto;

        _labels = new StateLabel[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            (string labelName, int pc) = labels[i];
            ArgumentException.ThrowIfNullOrEmpty(labelName);
            if (pc <= LatentFrame.NoCode)
            {
                throw new ArgumentException($"Label {labelName} in {QualifiedName} needs a Pc of 1 or more.", nameof(labels));
            }

            var label = new StateLabel(new Name(labelName), StableHash.OfNameCaseless(labelName), pc);
            for (int j = 0; j < i; j++)
            {
                if (_labels[j].Name == label.Name)
                {
                    throw new ArgumentException($"Label {labelName} appears twice in {QualifiedName}.", nameof(labels));
                }
            }

            _labels[i] = label;
        }

        CheckLabelIds();
    }

    private ActorState()
    {
        ClassName = "None";
        Name = Name.None;
        QualifiedName = "None";
        Id = 0;
        _labels = [];
    }

    /// <summary>State name, for example Dying.</summary>
    public Name Name { get; }

    /// <summary>Declaring class name.</summary>
    public string ClassName { get; }

    /// <summary>Class.State, for example Pawn.Dying.</summary>
    public string QualifiedName { get; }

    /// <summary>Stable id (hash of <see cref="QualifiedName"/>); 0 only for <see cref="None"/>.</summary>
    public int Id { get; }

    /// <summary>The state this one extends, or null.</summary>
    public ActorState? Super { get; }

    public bool IsAuto { get; }

    /// <summary>This state's own labels (labels of super states are found through <see cref="Super"/>).</summary>
    public ReadOnlySpan<StateLabel> Labels => _labels;

    internal int FindLabel(Name label)
    {
        StateLabel[] labels = _labels;
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i].Name == label)
            {
                return i;
            }
        }

        return -1;
    }

    internal int FindLabelById(int labelId)
    {
        StateLabel[] labels = _labels;
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i].Id == labelId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>True when this state is <paramref name="name"/> or extends a state with that name.</summary>
    public bool IsOrExtends(Name name)
    {
        for (ActorState? s = this; s is not null; s = s.Super)
        {
            if (s.Name == name)
            {
                return true;
            }
        }

        return false;
    }

    // Label ids are resolved by searching up the super chain, so two different label names in one chain must not
    // share an id.
    private void CheckLabelIds()
    {
        var seen = new Dictionary<int, Name>();
        for (ActorState? s = this; s is not null; s = s.Super)
        {
            foreach (StateLabel label in s._labels)
            {
                if (seen.TryGetValue(label.Id, out Name existing) && existing != label.Name)
                {
                    throw new InvalidOperationException(
                        $"Labels {existing} and {label.Name} in the chain of {QualifiedName} hash to the same id.");
                }

                seen[label.Id] = label.Name;
            }
        }
    }

    /// <summary>
    /// Runs this state's latent code from <paramref name="frame"/>.Pc. Run until a latent call or a jump, then
    /// return: set Pc to the resume point and call the latent function (Sleep...), or call GotoLabel/GotoState, or
    /// set Pc to <see cref="LatentFrame.NoCode"/> to stop. The runner calls again while nothing is waiting.
    /// </summary>
    public virtual void Code(Actor self, ref LatentFrame frame) => frame.Pc = LatentFrame.NoCode;

    // ---- Actor-level functions a state may override. Defaults run the class-level (global) version. ----------

    public virtual void BeginState(Actor self) => self.BeginState();

    public virtual void EndState(Actor self) => self.EndState();

    public virtual void PreBeginPlay(Actor self) => self.PreBeginPlay();

    public virtual void BeginPlay(Actor self) => self.BeginPlay();

    public virtual void PostBeginPlay(Actor self) => self.PostBeginPlay();

    public virtual void SetInitialState(Actor self) => self.SetInitialState();

    public virtual void PostNetBeginPlay(Actor self) => self.PostNetBeginPlay();

    public virtual void Tick(Actor self, float DeltaTime) => self.Tick(DeltaTime);

    public virtual void Timer(Actor self) => self.Timer();

    public virtual void Touch(Actor self, Actor Other) => self.Touch(Other);

    public virtual void UnTouch(Actor self, Actor Other) => self.UnTouch(Other);

    public virtual void Bump(Actor self, Actor Other) => self.Bump(Other);

    public virtual void HitWall(Actor self, FVector HitNormal, Actor? Wall) => self.HitWall(HitNormal, Wall);

    public virtual void Landed(Actor self, FVector HitNormal) => self.Landed(HitNormal);

    public virtual void Falling(Actor self) => self.Falling();

    public virtual void Trigger(Actor self, Actor? Other, Pawn? EventInstigator) => self.Trigger(Other, EventInstigator);

    public virtual void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator) => self.UnTrigger(Other, EventInstigator);

    public virtual void Destroyed(Actor self) => self.Destroyed();

    public virtual void BaseChange(Actor self) => self.BaseChange();

    public virtual void Attach(Actor self, Actor Other) => self.Attach(Other);

    public virtual void Detach(Actor self, Actor Other) => self.Detach(Other);

    public virtual void GainedChild(Actor self, Actor Other) => self.GainedChild(Other);

    public virtual void LostChild(Actor self, Actor Other) => self.LostChild(Other);

    public virtual void AnimEnd(Actor self, int Channel) => self.AnimEnd(Channel);

    public override string ToString() => QualifiedName;

    private sealed class NoState : ActorState
    {
    }
}
