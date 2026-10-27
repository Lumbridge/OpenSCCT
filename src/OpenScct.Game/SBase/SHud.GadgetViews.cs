using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SHud: the gadget view states (spec/equipment/gadget-hud.md rules 1 to 4). The controller's sticky cam and
/// camera network states put the HUD in them; the client HUD swaps the player page while they last. Their
/// PostRender and DrawHUD are drawing, done by the client (game/Scripts/Hud/Gadgets).
/// </summary>
public partial class SHud
{
    public static new readonly StateTable ClassStates =
        new(Actor.ClassStates, s_Hud_UseObject_CamNetwork.Instance, s_Hud_UseObject_StickyCam.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>The camera network view is up.</summary>
    [Uc("SBase.SHud.IsInCamNetwork", Sha1 = "e75d275ae0c754db91da2b471ad05601c31d94a0")]
    [PortOf("uc:SBase.SHud:IsInCamNetwork", Spec = "spec/equipment/gadget-hud.md#shud-states")]
    public bool IsInCamNetwork() => IsInState(GadgetStates.s_Hud_UseObject_CamNetwork);

    /// <summary>The sticky cam view is up.</summary>
    [Uc("SBase.SHud.IsInSticky", Sha1 = "8d15c22f621cf155627d6436ac9786d0af1fc0c8")]
    [PortOf("uc:SBase.SHud:IsInSticky", Spec = "spec/equipment/gadget-hud.md#shud-states")]
    public bool IsInSticky() => IsInState(GadgetStates.s_Hud_UseObject_StickyCam);

    /// <summary>The merc's camera network view (rule 4): the page swap and the motion tracker are client drawing.</summary>
    [Uc("SBase.SHud.s_Hud_UseObject_CamNetwork")]
    public class s_Hud_UseObject_CamNetwork : UcActorState
    {
        public static readonly s_Hud_UseObject_CamNetwork Instance = new(UcMeta.s_Hud_UseObject_CamNetwork, null);

        protected s_Hud_UseObject_CamNetwork(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SHud.s_Hud_UseObject_CamNetwork.EndState", Sha1 = "0f9828abdc4acc5d565b83fd43b4aef85cb9b2aa")]
        [PortOf("uc:SBase.SHud:s_Hud_UseObject_CamNetwork.EndState", Spec = "spec/equipment/gadget-hud.md#shud-states")]
        public override void EndState(Actor self)
        {
            var hud = (SHud)self;
            hud.PreviousState = hud.GetStateName();
        }
    }

    /// <summary>The spy's sticky cam view (rule 3): the page swap is client drawing; DrawHUD is empty.</summary>
    [Uc("SBase.SHud.s_Hud_UseObject_StickyCam")]
    public class s_Hud_UseObject_StickyCam : UcActorState
    {
        public static readonly s_Hud_UseObject_StickyCam Instance = new(UcMeta.s_Hud_UseObject_StickyCam, null);

        protected s_Hud_UseObject_StickyCam(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SHud.s_Hud_UseObject_StickyCam.EndState", Sha1 = "0f9828abdc4acc5d565b83fd43b4aef85cb9b2aa")]
        [PortOf("uc:SBase.SHud:s_Hud_UseObject_StickyCam.EndState", Spec = "spec/equipment/gadget-hud.md#shud-states")]
        public override void EndState(Actor self)
        {
            var hud = (SHud)self;
            hud.PreviousState = hud.GetStateName();
        }
    }
}
