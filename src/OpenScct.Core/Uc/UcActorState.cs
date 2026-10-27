using System;

namespace OpenScct.Core.Uc;

/// <summary>
/// An <see cref="ActorState"/> built from the generated <see cref="UcStateInfo"/> of the state it ports: class and
/// state names, the auto flag and the labels come from the metadata, and the super state is checked against it.
/// <code>
/// public class s_LockedByChaff : UcActorState
/// {
///     public static readonly s_LockedByChaff Instance = new(UcMeta.Class.State(nameof(s_LockedByChaff)), null);
///     protected s_LockedByChaff(UcStateInfo info, ActorState? super) : base(info, super) { }
/// }
/// </code>
/// </summary>
public abstract class UcActorState : ActorState
{
    /// <param name="info">The generated state metadata.</param>
    /// <param name="super">
    /// The super state's instance: the state named by <see cref="UcStateInfo.Extends"/> (a state of the same class,
    /// or the parent class's state when this one redefines it), or null when Extends is null.
    /// </param>
    protected UcActorState(UcStateInfo info, ActorState? super)
        : base(Check(info, super).ClassName, info.Name, super, info.IsAuto, info.LabelPcs)
    {
        Info = info;
    }

    /// <summary>The generated metadata of the ported state.</summary>
    public UcStateInfo Info { get; }

    private static UcStateInfo Check(UcStateInfo info, ActorState? super)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.Extends is null)
        {
            if (super is not null)
            {
                throw new ArgumentException($"State {info} has no super state, but {super.QualifiedName} was given.", nameof(super));
            }
        }
        else if (super is null || !string.Equals(super.Name.ToString(), info.Extends, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"State {info} extends {info.Extends}, but {(super is null ? "no super state" : super.QualifiedName)} was given.",
                nameof(super));
        }

        return info;
    }
}
