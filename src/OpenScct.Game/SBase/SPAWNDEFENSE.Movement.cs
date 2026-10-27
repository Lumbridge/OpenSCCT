using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPAWNDEFENSE (the merc) movement: the merc's own crouched eye height. Written from spec/movement/walking.md and
// spec/movement/pawn.md.
public partial class SPAWNDEFENSE
{
    /// <summary>Crouching: the eye stays where it was this frame and the merc's crouched eye height (20) is the new base.</summary>
    [Uc("SBase.SPAWNDEFENSE.StartCrouch", Sha1 = "0bba8919f9128edeec3b5d6448baef051687a9ce")]
    [PortOf("uc:SBase.SPAWNDEFENSE:StartCrouch", Spec = "spec/movement/walking.md#crouch-requests-processcrouch")]
    public override void StartCrouch(float HeightAdjust)
    {
        EyeHeight += HeightAdjust;
        OldZ -= HeightAdjust;
        BaseEyeHeight = BaseCrouchEyeHeight;
    }
}
