using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SHud: the binocular states (spec/hud/binoculars.md rules 18 to 21). The controller's scope and binocular
/// states put the HUD in them; their sounds run here, on the player's machine. The pages they draw are the client's
/// (game/Scripts/Hud/HudBinoculars.cs).
/// </summary>
public partial class SHud
{
    /// <summary>How often the spy's binoculars blip while he looks through them (seconds).</summary>
    [Provenance(ProvenanceKind.Decoded, "SHud s_H_Binoculars_Att.PostRender: BinocularSndRegular > 5.0")]
    public const float BinocularDetectPeriod = 5f;

    /// <summary>The scope or the binoculars are up.</summary>
    [Uc("SBase.SHud.IsInBinocular", Sha1 = "e876e1fd74ee6258957971e81bada8c67406043a")]
    [PortOf("uc:SBase.SHud:IsInBinocular", Spec = "spec/hud/binoculars.md#rules")]
    public bool IsInBinocular() => IsInState(GadgetStates.s_H_Binoculars_Def) || IsInState(GadgetStates.s_H_Binoculars_Att);

    /// <summary>
    /// The HUD's sounds play at its player controller. The HUD actor stays where it was spawned; the controller sits
    /// at the camera on this machine, so the sound is heard as the original's HUD sound is, at full volume.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "SHud.PlaySound: the HUD actor's own location is not kept in the port; its owner stands in")]
    private void HudPlaySound(AssetRef Sound, ESoundSlot Slot, bool Attenuate = true) =>
        (PlayerOwner ?? Owner)?.PlaySound(Sound, Slot, 1f, Attenuate: Attenuate);

    private void HudStopSound(AssetRef Sound) => (PlayerOwner ?? Owner)?.StopSound(Sound);

    /// <summary>
    /// The spy's analyse blip (rule 21, the sound part of <c>s_H_Binoculars_Att.PostRender</c>): once the time the
    /// controller adds up in the binoculars passes 5 s, 5 s come off it and the detect sound plays. Run each tick the
    /// controller adds to <see cref="BinocularSndRegular"/>.
    /// </summary>
    [Uc("SBase.SHud.s_H_Binoculars_Att.PostRender", Sha1 = "ec4f239b9d4ed6ce15b4e2d91daaddcc4c4eb1b2")]
    [PortOf("uc:SBase.SHud:s_H_Binoculars_Att.PostRender", Spec = "spec/hud/binoculars.md#rules")]
    public void BinocularDetectBlip()
    {
        if (IsInState(GadgetStates.s_H_Binoculars_Att) &&BinocularSndRegular > BinocularDetectPeriod)
        {
            BinocularSndRegular -= BinocularDetectPeriod;
            HudPlaySound(Snd_BinocularAnalyzeDetect, ESoundSlot.SLOT_Misc);
        }
    }

    /// <summary>The merc's rifle scope (rules 18 and 19): the switch sound, the noise and analyse loops.</summary>
    [Uc("SBase.SHud.s_H_Binoculars_Def")]
    public class s_H_Binoculars_Def : UcActorState
    {
        public static readonly s_H_Binoculars_Def Instance = new(UcMeta.s_H_Binoculars_Def, null);

        protected s_H_Binoculars_Def(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SHud.s_H_Binoculars_Def.BeginState", Sha1 = "fc38e9c1be844cc537f926de0a1c22aa498b715e")]
        [PortOf("uc:SBase.SHud:s_H_Binoculars_Def.BeginState", Spec = "spec/hud/binoculars.md#rules")]
        public override void BeginState(Actor self)
        {
            var hud = (SHud)self;
            hud.HudPlaySound(hud.Snd_SwitchBinoculars, ESoundSlot.SLOT_Misc, Attenuate: false);
            hud.HudPlaySound(hud.Snd_BinocularNoise, ESoundSlot.SLOT_Ambient, Attenuate: false);
            hud.HudPlaySound(hud.Snd_BinocularAnalyzeLoop, ESoundSlot.SLOT_Interface, Attenuate: false);
        }

        [Uc("SBase.SHud.s_H_Binoculars_Def.EndState", Sha1 = "be05865b4ebb6c1ff1412ec23e247c6ee5206338")]
        [PortOf("uc:SBase.SHud:s_H_Binoculars_Def.EndState", Spec = "spec/hud/binoculars.md#rules")]
        public override void EndState(Actor self)
        {
            var hud = (SHud)self;
            hud.PreviousState = hud.GetStateName();
            hud.HudStopSound(hud.Snd_BinocularNoise);
            hud.HudStopSound(hud.Snd_BinocularAnalyzeLoop);
        }
    }

    /// <summary>The spy's binoculars (rules 20 and 21): the switch sound, the attack loop and the 5 s blip.</summary>
    [Uc("SBase.SHud.s_H_Binoculars_Att")]
    public class s_H_Binoculars_Att : UcActorState
    {
        public static readonly s_H_Binoculars_Att Instance = new(UcMeta.s_H_Binoculars_Att, null);

        protected s_H_Binoculars_Att(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SHud.s_H_Binoculars_Att.BeginState", Sha1 = "71216b4d8af94563c3f4a6e5c2cca103ef6cd539")]
        [PortOf("uc:SBase.SHud:s_H_Binoculars_Att.BeginState", Spec = "spec/hud/binoculars.md#rules")]
        public override void BeginState(Actor self)
        {
            var hud = (SHud)self;
            hud.HudPlaySound(hud.Snd_SwitchBinoculars, ESoundSlot.SLOT_Misc);
            hud.HudPlaySound(hud.Snd_BinocularAttLoop, ESoundSlot.SLOT_Interface, Attenuate: false);
            hud.BinocularSndRegular = 0f;
        }

        [Uc("SBase.SHud.s_H_Binoculars_Att.EndState", Sha1 = "9430991cc7142f0332b4135bc8fe882d3676c529")]
        [PortOf("uc:SBase.SHud:s_H_Binoculars_Att.EndState", Spec = "spec/hud/binoculars.md#rules")]
        public override void EndState(Actor self)
        {
            var hud = (SHud)self;
            hud.PreviousState = hud.GetStateName();
            hud.HudStopSound(hud.Snd_BinocularAttLoop);
        }
    }
}
