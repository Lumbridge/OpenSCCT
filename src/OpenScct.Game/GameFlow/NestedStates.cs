using System;
using System.Collections.Generic;
using System.Reflection;
using OpenScct.Core;

namespace OpenScct.Game.GameFlow;

/// <summary>
/// Collects the states a class declares as nested <see cref="ActorState"/> classes, each with its own
/// <c>public static readonly Instance</c>, sorted by state name. It lets several partial files of one class add
/// states without editing a shared list:
/// <code>
/// public static new readonly StateTable ClassStates = new(Parent.ClassStates, NestedStates.Of(typeof(MyClass)));
/// </code>
/// Runs once per class, when its state table is built.
/// </summary>
public static class NestedStates
{
    public static ActorState[] Of(Type owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var states = new List<ActorState>();
        foreach (Type nested in owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!typeof(ActorState).IsAssignableFrom(nested) || nested.IsGenericTypeDefinition)
            {
                continue;
            }

            FieldInfo? field = nested.GetField("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (field?.GetValue(null) is ActorState state)
            {
                states.Add(state);
            }
        }

        states.Sort(static (a, b) => string.CompareOrdinal(a.Name.ToString(), b.Name.ToString()));
        return states.ToArray();
    }
}
