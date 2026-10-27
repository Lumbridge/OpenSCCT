using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SAnimatedMesh
{
    /// <summary>Playing forwards: loops and chains sequences by LoopStatus_0forInfinite and bChainedAnims.</summary>
    [Uc("SBase.SAnimatedMesh.s_AnimateForward")]
    public class s_AnimateForward : UcActorState
    {
        public static readonly s_AnimateForward Instance = new(UcMeta.s_AnimateForward, null);

        protected s_AnimateForward(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SAnimatedMesh.s_AnimateForward.BeginState", Sha1 = "68fc0f2a5c989d1b587e344300664ac80b6ea0a1")]
        [PortOf("uc:SBase.SAnimatedMesh:s_AnimateForward.BeginState", Spec = "spec/devices/animated-mesh.md#rules")]
        public override void BeginState(Actor self)
        {
            var mesh = (SAnimatedMesh)self;
            mesh.iLoopLeft = mesh.LoopCount(mesh.iCurrentAnim) - 1;
            mesh.PlayCurrentForward();
        }

        /// <summary>
        /// Stop when asked; else replay while loops are left (-1 loops forever); else advance (wrapping) and, when
        /// chained or asked to continue, play the next sequence (back to s_Wait after the last); else s_Wait.
        /// </summary>
        [Uc("SBase.SAnimatedMesh.s_AnimateForward.AnimEnd", Sha1 = "f97234f30041e9dfc0b2a3dad0ef799f41ecc46b")]
        [PortOf("uc:SBase.SAnimatedMesh:s_AnimateForward.AnimEnd", Spec = "spec/devices/animated-mesh.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var mesh = (SAnimatedMesh)self;
            if (mesh.bShouldStop)
            {
                mesh.bShouldStop = false;
                mesh.GotoState(nameof(s_Wait));
                return;
            }

            if (mesh.iLoopLeft != 0 && !mesh.bShouldContinue)
            {
                if (mesh.iLoopLeft > 0)
                {
                    mesh.iLoopLeft--;
                }

                mesh.PlayCurrentForward();
                return;
            }

            if (++mesh.iCurrentAnim >= mesh.Animations.Count)
            {
                mesh.iCurrentAnim = 0;
            }

            if (mesh.bChainedAnims || mesh.bShouldContinue)
            {
                mesh.bShouldContinue = false;
                if (mesh.iCurrentAnim == 0)
                {
                    mesh.GotoState(nameof(s_Wait));
                }
                else
                {
                    mesh.iLoopLeft = mesh.LoopCount(mesh.iCurrentAnim) - 1;
                    mesh.PlayCurrentForward();
                }
            }
            else
            {
                mesh.GotoState(nameof(s_Wait));
            }
        }

        /// <summary>Skip the remaining loops at the next end.</summary>
        [Uc("SBase.SAnimatedMesh.s_AnimateForward.Trigger", Sha1 = "b0926301c872500b02102b3e7025a5ea99443b1b")]
        [PortOf("uc:SBase.SAnimatedMesh:s_AnimateForward.Trigger", Spec = "spec/devices/animated-mesh.md#rules")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator) => ((SAnimatedMesh)self).bShouldContinue = true;

        /// <summary>Stop at the next end.</summary>
        [Uc("SBase.SAnimatedMesh.s_AnimateForward.UnTrigger", Sha1 = "345d08bfbba730ea4440ea37f1e54b517684bc2d")]
        [PortOf("uc:SBase.SAnimatedMesh:s_AnimateForward.UnTrigger", Spec = "spec/devices/animated-mesh.md#rules")]
        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator) => ((SAnimatedMesh)self).bShouldStop = true;
    }

    /// <summary>LoopStatus_0forInfinite for sequence <paramref name="index"/> (an entry the list lacks reads as 0).</summary>
    private int LoopCount(int index) =>
        index >= 0 && index < LoopStatus_0forInfinite.Count ? LoopStatus_0forInfinite[index] : 0;
}
