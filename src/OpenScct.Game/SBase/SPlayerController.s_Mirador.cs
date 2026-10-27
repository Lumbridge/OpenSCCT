using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController manning a fixed searchlight (SProjoFixe through SProjoInteraction), written from
// spec/devices/projector.md. The view is PlayerCalcView's Mirador source; damage is CombatDispatch's
// UseObjectMirador handler (s_UseObject_Mirador.PawnTakeDamage).
public partial class SPlayerController
{
    private static readonly Name NAME_s_UseObject_Mirador = new("s_UseObject_Mirador");

    /// <summary>Radius around the pawn searched for the projector's interaction.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerController.UseMirador: CollidingActors(class'SProjoInteraction', 500)")]
    public const float MiradorSearchRadius = 500f;

    /// <summary>
    /// The first projector interaction within 500 uu of the pawn becomes the used object's (its projector); a merc's
    /// pad-up gadget is put away; the controller mirrors into s_UseObject_Mirador. Nothing happens without one.
    /// </summary>
    [Uc("SBase.SPlayerController.UseMirador", Sha1 = "7933345e54c30ff639a6c4b7a67e781f931db3ac")]
    [PortOf("uc:SBase.SPlayerController:UseMirador", Spec = "spec/devices/projector.md#manning-it")]
    [Provenance(ProvenanceKind.Estimated, "CollidingActors: colliding actors whose location is within the radius of the pawn, in level order")]
    public void UseMirador()
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        pawn.UsedObject = null;
        SProjoInteraction? proj = null;
        float r2 = MiradorSearchRadius * MiradorSearchRadius;
        foreach (SProjoInteraction candidate in XLevel.AllActors<SProjoInteraction>())
        {
            if (!candidate.bDeleteMe && candidate.bCollideActors && FVector.DistSquared(candidate.Location, pawn.Location) <= r2)
            {
                proj = candidate;
                break;
            }
        }

        if (proj is null)
        {
            return;
        }

        if (pawn is SPAWNDEFENSE && pawn.PadUpGadget is { } gadget)
        {
            gadget.Unload();
            pawn.PadUpGadget = null;
        }

        pawn.UsedObject = proj.Owner;
        UsedInterraction = proj;
        eventMirrorState(NAME_s_UseObject_Mirador);
    }

    /// <summary>
    /// s_UseObject_Mirador: behind the searchlight (third person, no reticule, laser off, the merc's torch put away),
    /// steering it with the s_UseObject turn; the torch and laser keys do nothing; the merc can be grabbed, drop
    /// attacked and elbow shocked; smoke drops him out to his waiting state.
    /// </summary>
    [Uc("SBase.SPlayerController.s_UseObject_Mirador")]
    public class s_UseObject_Mirador : s_UseObject, IGadTorcheSwitch, IGadLaserSwitch, ICanBeDropAttacked, ICanBeElbowShocked,
        ICanBeGrabbed, IGoInSmoke
    {
        public static new readonly s_UseObject_Mirador Instance = new(UcMeta.s_UseObject_Mirador, s_UseObject.Instance);

        protected s_UseObject_Mirador(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_UseObject_Mirador.GadTorcheSwitch", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_Mirador.GadTorcheSwitch", Spec = "spec/devices/projector.md#manning-it")]
        public void GadTorcheSwitch(SPlayerController self)
        {
        }

        [Uc("SBase.SPlayerController.s_UseObject_Mirador.GadLaserSwitch", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_Mirador.GadLaserSwitch", Spec = "spec/devices/projector.md#manning-it")]
        public void GadLaserSwitch(SPlayerController self)
        {
        }

        [Uc("SBase.SPlayerController.s_UseObject_Mirador.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_Mirador.CanBeDropAttacked", Spec = "spec/devices/projector.md#manning-it")]
        public bool CanBeDropAttacked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_UseObject_Mirador.CanBeElbowShocked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_Mirador.CanBeElbowShocked", Spec = "spec/devices/projector.md#manning-it")]
        public bool CanBeElbowShocked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_UseObject_Mirador.CanBeGrabbed", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_Mirador.CanBeGrabbed", Spec = "spec/devices/projector.md#manning-it")]
        public bool CanBeGrabbed(SPlayerController self) => true;

        /// <summary>The usual smoke reaction, then back to the waiting state.</summary>
        [Uc("SBase.SPlayerController.s_UseObject_Mirador.GoInSmoke", Sha1 = "515254ea6f8cea011620dec716b7a04f07e7afbd")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_Mirador.GoInSmoke", Spec = "spec/devices/projector.md#manning-it")]
        public void GoInSmoke(SPlayerController self, float tCough, float tFaint, float tHud, float blur)
        {
            self.GoInSmoke(tCough, tFaint, tHud, blur);
            self.GotoWaitingState();
        }

        /// <summary>Laser off, grabbable, the merc's torch put away, no reticule, behind view; then the s_UseObject entry.</summary>
        [Uc("SBase.SPlayerController.s_UseObject_Mirador.BeginState", Sha1 = "78021b9f6d7a51fd4bc528141debf1f46bee4db5")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_Mirador.BeginState", Spec = "spec/devices/projector.md#manning-it")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.EteintLaser();
            if (pc.SBPawn is { } pawn)
            {
                pawn.bCanBeGrabbed = true;
                if ((pawn as SPAWNDEFENSE)?.Torche is { bInUse: true } torch)
                {
                    torch.Unload();
                }
            }

            pc.bNoReticule = true;
            pc.bBehindView = true;
            base.BeginState(self);
        }

        /// <summary>Not grabbable, the controller levelled to the pawn's yaw, the reticule back unless the laser was on, first person; then the s_UseObject exit.</summary>
        [Uc("SBase.SPlayerController.s_UseObject_Mirador.EndState", Sha1 = "c25d83dd1acc14f6202018fdc4b4a8d7318c1d5c")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_Mirador.EndState", Spec = "spec/devices/projector.md#manning-it")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } pawn)
            {
                pawn.bCanBeGrabbed = false;
            }

            if (pc.Pawn is { } p)
            {
                FRotator rot = p.Rotation;
                rot.Pitch = 0;
                pc.Rotation = rot;
            }

            if (!pc.bLaserEtaitAllume)
            {
                pc.bNoReticule = false;
            }

            pc.bBehindView = false;
            base.EndState(self);
        }
    }
}
