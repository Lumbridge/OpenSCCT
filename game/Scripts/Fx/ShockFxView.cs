using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Views;

namespace OpenScctGame.Fx;

/// <summary>
/// The sticky shock on a pawn as this machine sees it (spec/equipment/sticky-shock-effect.md rule 3.6): each
/// SEffectShockedPawnEmitter the pawn's SA_DMG_ELECTRIC action spawned in the client world is drawn at its bone of
/// the drawn skeleton (forearms, calves, pelvis, head, spine), and the first one's blinking blue light (hue 145,
/// saturation 174, brightness 150, radius 10) lights the pawn. They go when SA_DMG_ELECTRIC_STOP destroys them.
/// Drawn sparks join the group <c>svm_shock_sparks</c>.
/// </summary>
[PortOf("uc:SBase.SBasePawn:StickyShockSfx", Spec = "spec/equipment/sticky-shock-effect.md#rules")]
[Provenance(ProvenanceKind.Decoded, "The SEffectShockedPawnEmitter sub-emitters are the converted class's own (EffectViews draws them at the attachment bone)")]
public sealed partial class ShockFxView : Node3D
{
    public const string GroupSparks = "svm_shock_sparks";

    /// <summary>UE2 WorldLightRadius: 25 uu per step of LightRadius, plus one step.</summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 AActor::WorldLightRadius = 25 * (LightRadius + 1)")]
    public const float LightRadiusUnit = 25f;

    /// <summary>The blink period of the LT_Blink light.</summary>
    [Provenance(ProvenanceKind.Estimated, "LT_Blink on and off; the period is not decoded")]
    public const float BlinkPeriod = 0.2f;

    private readonly Dictionary<Actor, Node3D> _sparks = new(ReferenceEqualityComparer.Instance);
    private readonly List<Actor> _gone = new();
    private float _time;

    public PawnViews? Pawns { get; set; }

    public int SparksDrawn { get; private set; }

    public void Draw(Level? level, float dt)
    {
        _time += dt;
        SparksDrawn = 0;
        foreach (Actor a in _sparks.Keys)
        {
            if (level is null || a.bDeleteMe)
            {
                _gone.Add(a);
            }
        }

        foreach (Actor a in _gone)
        {
            _sparks[a].QueueFree();
            _sparks.Remove(a);
        }

        _gone.Clear();
        if (level is null)
        {
            return;
        }

        foreach (SEffectShockedPawnEmitter e in level.AllActors<SEffectShockedPawnEmitter>())
        {
            if (e.bDeleteMe)
            {
                continue;
            }

            Node3D node = NodeFor(e);
            Vector3 at = UeSpace.ToGodot(e.Location);
            if (e.Owner is SBasePawn pawn)
            {
                at = Pawns is not null && Pawns.TryGetBone(pawn, e.AttachmentBone, out Transform3D bone)
                    ? bone.Origin
                    : UeSpace.ToGodot(pawn.Location);
                node.Visible = !pawn.bHidden;
            }

            node.GlobalPosition = at;
            if (node.GetNodeOrNull<OmniLight3D>("Light") is { } light)
            {
                light.Visible = (int)(_time / BlinkPeriod) % 2 == 0;
            }

            SparksDrawn++;
        }
    }

    private Node3D NodeFor(SEffectShockedPawnEmitter e)
    {
        if (_sparks.TryGetValue(e, out Node3D? node))
        {
            return node;
        }

        // The sparks themselves are the class's converted sub-emitters (sfx.Impacts.sticky and aura_sticky), drawn by
        // EffectViews at the same bone; this node marks the place and carries the blinking light.
        node = new Node3D { Name = "ShockSpark" };
        if (e.bDynamicLight)
        {
            // HSB 145 / 174 / 150 (of 255): a light blue.
            var colour = Color.FromHsv(e.LightHue / 255f, e.LightSaturation / 255f, Math.Clamp(e.LightBrightness / 255f, 0f, 1f));
            node.AddChild(new OmniLight3D
            {
                Name = "Light",
                LightColor = colour,
                LightEnergy = 2f,
                OmniRange = UeSpace.LengthToGodot(LightRadiusUnit * (e.LightRadius + 1f)),
                ShadowEnabled = false,
            });
        }

        AddChild(node);
        node.AddToGroup(GroupSparks);
        _sparks[e] = node;
        return node;
    }
}
