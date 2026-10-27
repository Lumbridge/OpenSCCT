using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SAnimatedMesh
{
    /// <summary>Playing the previous sequence backwards; its end (the global AnimEnd) returns to s_Wait.</summary>
    [Uc("SBase.SAnimatedMesh.s_AnimateBackward")]
    public class s_AnimateBackward : UcActorState
    {
        public static readonly s_AnimateBackward Instance = new(UcMeta.s_AnimateBackward, null);

        protected s_AnimateBackward(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SAnimatedMesh.s_AnimateBackward.BeginState", Sha1 = "d9809c5857b4f51a9439e37620e14b7d96c80889")]
        [PortOf("uc:SBase.SAnimatedMesh:s_AnimateBackward.BeginState", Spec = "spec/devices/animated-mesh.md#rules")]
        public override void BeginState(Actor self)
        {
            var mesh = (SAnimatedMesh)self;
            if (--mesh.iCurrentAnim < 0)
            {
                mesh.iCurrentAnim = mesh.Animations.Count - 1;
            }

            if (mesh.iCurrentAnim >= 0)
            {
                mesh.PlayAnim(mesh.Animations[mesh.iCurrentAnim], 1f, true);
            }

            mesh.bHidden = false;
            mesh.SetPhysics(EPhysics.PHYS_None);
            mesh.SetPhysics(EPhysics.PHYS_RootMotion);
        }
    }
}
