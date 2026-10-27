using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SFlareCourseEmitter: the burning trail of a flare in flight (spec/equipment/flare.md rule 2). Each drawing
/// machine spawns its own on its flare and it rides the flare (a small steady light, radius 6) until the flare
/// catches or its 5 s life ends.
/// </summary>
public partial class SFlareCourseEmitter
{
    /// <summary>The trail rides its flare.</summary>
    [Uc("SBase.SFlareCourseEmitter.PostNetBeginPlay", Sha1 = "65349c069005ae49d42f6be161df40810db1748f")]
    [PortOf("uc:SBase.SFlareCourseEmitter:PostNetBeginPlay", Spec = "spec/equipment/flare.md#rules")]
    protected override void PostNetBeginPlay()
    {
        SetBase(Owner);
        base.PostNetBeginPlay();
    }

    /// <summary>A flare whose trail this is forgets it.</summary>
    [Uc("SBase.SFlareCourseEmitter.Destroyed", Sha1 = "009af9cf184c61f8c36506c06705eb88b5aa8b29")]
    [PortOf("uc:SBase.SFlareCourseEmitter:Destroyed", Spec = "spec/equipment/flare.md#rules")]
    protected override void Destroyed()
    {
        if (Owner is SProjFlare flare && ReferenceEquals(flare.CourseEmitter, this))
        {
            flare.CourseEmitter = null;
        }

        base.Destroyed();
    }
}
