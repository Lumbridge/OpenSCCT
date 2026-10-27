using System;
using System.Collections.Generic;

namespace OpenScct.Core;

/// <summary>
/// The states visible to one actor class: its own declarations plus everything inherited from the parent class
/// (a redefinition replaces the inherited state of the same name). Immutable after construction.
/// <para>
/// A class that declares states exposes its table as
/// <c>public static new readonly StateTable ClassStates = new(Parent.ClassStates, StateA.Instance, ...)</c>
/// and returns it from <c>GetStateTable()</c>.
/// </para>
/// </summary>
public sealed class StateTable
{
    /// <summary>Table with no states.</summary>
    public static readonly StateTable Empty = new(null);

    private readonly Dictionary<Name, ActorState> _byName = new();
    private readonly Dictionary<int, ActorState> _byId = new();

    public StateTable(StateTable? parent, params ReadOnlySpan<ActorState> declared)
    {
        if (parent is not null)
        {
            foreach (KeyValuePair<Name, ActorState> pair in parent._byName)
            {
                _byName[pair.Key] = pair.Value;
            }
        }

        ActorState? declaredAuto = null;
        for (int i = 0; i < declared.Length; i++)
        {
            ActorState state = declared[i] ?? throw new ArgumentException("Null state in table.", nameof(declared));
            for (int j = 0; j < i; j++)
            {
                if (declared[j].Name == state.Name)
                {
                    throw new ArgumentException($"State {state.Name} is declared twice.", nameof(declared));
                }
            }

            _byName[state.Name] = state;
            if (state.IsAuto && declaredAuto is null)
            {
                declaredAuto = state;
            }
        }

        foreach (ActorState state in _byName.Values)
        {
            AddIdChain(state);
        }

        if (declaredAuto is not null)
        {
            AutoState = declaredAuto;
        }
        else if (parent?.AutoState is { } inheritedAuto)
        {
            // A redefinition of the inherited auto state stands in for it.
            AutoState = _byName.TryGetValue(inheritedAuto.Name, out ActorState? redefined) ? redefined : inheritedAuto;
        }
    }

    /// <summary>The auto state, or null.</summary>
    public ActorState? AutoState { get; }

    /// <summary>Number of named states.</summary>
    public int Count => _byName.Count;

    public ActorState? Find(Name name) => name.IsNone ? null : _byName.GetValueOrDefault(name);

    /// <summary>Finds a state (or any super state of a visible state) by its stable id.</summary>
    public ActorState? FindById(int id) => id == 0 ? ActorState.None : _byId.GetValueOrDefault(id);

    private void AddIdChain(ActorState state)
    {
        for (ActorState? s = state; s is not null; s = s.Super)
        {
            if (_byId.TryGetValue(s.Id, out ActorState? existing))
            {
                if (!ReferenceEquals(existing, s))
                {
                    throw new InvalidOperationException(
                        $"States {existing.QualifiedName} and {s.QualifiedName} hash to the same id.");
                }

                continue;
            }

            _byId[s.Id] = s;
        }
    }
}
