using OpenScct.Core.Uc;

namespace OpenScct.Core;

/// <summary>
/// Base of every UnrealScript object (Core.Object, the engine's UObject). <see cref="Engine.Actor"/> derives from
/// it, and so does every generated non-actor class (UInteraction, UMaterial...).
/// <para>
/// The constructor runs <see cref="SetDefaults"/> once. Each generated class overrides it to call the parent's
/// version first and then assign the class defaults that differ from the parent's, so a new object starts with the
/// values of the original class default object. Field initializers of every class run before it and constructor
/// bodies after it, so a hand-written constructor can still adjust values.
/// </para>
/// </summary>
public partial class UObject
{
    protected UObject()
    {
        SetDefaults();
    }

    /// <summary>
    /// Assigns the class defaults. Generated per class: call <c>base.SetDefaults()</c>, then the class's own values.
    /// Runs from the <see cref="UObject"/> constructor, before any subclass constructor body.
    /// </summary>
    protected virtual void SetDefaults()
    {
    }

    /// <summary>Generated metadata of the object's UnrealScript class (fields, functions, states, replication).</summary>
    public virtual UcClassInfo GetUcClass() => UcMeta.Class;
}
