using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>
/// Engine.Volume: a brush region. Maps use it for location names (PlayerReplicationInfo.UpdatePlayerLocation), as the
/// detection region of SVolumetricSensor, and with AssociatedActorTag to forward touches to another actor (state
/// AssociatedTouch). See spec/devices/volume.md.
/// </summary>
public partial class Volume
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates, AssociatedTouchState.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>Finds the first actor tagged AssociatedActorTag; with one, enters AssociatedTouch and keeps it as the initial state.</summary>
    [Uc("Engine.Volume.PostBeginPlay", Sha1 = "b15aaaa0ce0e636b48eb382c0ef2c26dd1431047")]
    [PortOf("uc:Engine.Volume:PostBeginPlay", Spec = "spec/devices/volume.md#rules")]
    protected internal override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (!AssociatedActorTag.IsNone)
        {
            foreach (Actor a in XLevel.AllActors<Actor>(AssociatedActorTag))
            {
                AssociatedActor = a;
                break;
            }
        }

        if (AssociatedActor is not null)
        {
            GotoState("AssociatedTouch");
            InitialState = GetStateName();
        }
    }

    /// <summary>Touches and untouches go to the associated actor; entering it replays a Touch for every actor already touching.</summary>
    [Uc("Engine.Volume.AssociatedTouch")]
    public class AssociatedTouchState : UcActorState
    {
        public static readonly AssociatedTouchState Instance = new(UcMeta.AssociatedTouch, null);

        protected AssociatedTouchState(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Volume.AssociatedTouch.Touch", Sha1 = "885936de419b45d4d5c916765b86116c342216f4")]
        [PortOf("uc:Engine.Volume:AssociatedTouch.Touch", Spec = "spec/devices/volume.md#rules")]
        public override void Touch(Actor self, Actor Other) => ((Volume)self).AssociatedActor?.eventTouch(Other);

        [Uc("Engine.Volume.AssociatedTouch.UnTouch", Sha1 = "dccddbe67870e40831c21a4786f6b262498abfdb")]
        [PortOf("uc:Engine.Volume:AssociatedTouch.UnTouch", Spec = "spec/devices/volume.md#rules")]
        public override void UnTouch(Actor self, Actor Other) => ((Volume)self).AssociatedActor?.eventUnTouch(Other);

        [Uc("Engine.Volume.AssociatedTouch.BeginState", Sha1 = "c9f0c9b6a688496aa46c5961eb2fe240258fb46c")]
        [PortOf("uc:Engine.Volume:AssociatedTouch.BeginState", Spec = "spec/devices/volume.md#rules")]
        public override void BeginState(Actor self)
        {
            Actor[] touching = [.. self.Touching];
            foreach (Actor a in touching)
            {
                self.eventTouch(a);
            }
        }
    }
}
