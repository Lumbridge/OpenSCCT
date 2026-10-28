using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SFootPrint: a footprint left by dirty feet (spec/native/audio.md, footsteps). Its PostBeginPlay builds the
/// material it projects at run time: a Combiner that takes its colour from a ConstantColor (grey 120, alpha 170 until
/// the pawn sets the step's colour) and multiplies the footprint texture's alpha by the colour's, and a TexScaler of it
/// turned upside down for the right foot. The port keeps what that material draws: the texture (sand, water or
/// phosphor, all step01 in this install), the tint and the flip.
/// </summary>
public partial class SFootPrint
{
    [Provenance(ProvenanceKind.Decoded, "SFootPrint.PostBeginPlay: FootStepColor.Color = (120, 120, 120, 170)")]
    public static readonly FColor DefaultTint = new() { R = 120, G = 120, B = 120, A = 170 };

    /// <summary>The colour and opacity the footprint is drawn in (FootStepColor.Color).</summary>
    public FColor StepTint
    {
        get => ProjectionTint ?? DefaultTint;
        set => ProjectionTint = value;
    }

    /// <summary>
    /// The footprint material: the sand texture under the default tint, projected as is (the right foot's flipped copy is
    /// chosen by the pawn).
    /// </summary>
    [Uc("SBase.SFootPrint.PostBeginPlay", Sha1 = "20401438f2da0f387f6ac5f525d57244ab9b2e02")]
    [PortOf("uc:SBase.SFootPrint:PostBeginPlay", Spec = "spec/native/audio.md#footsteps-sbasepawn")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        ProjectionTint = DefaultTint;
        if (!FootStepSand.IsNone)
        {
            ProjTexture = FootStepSand;
        }

        ProjectionFlipV = false;
    }

    /// <summary>The right foot: the combiner turned upside down (FootStepRevert, VScale -1).</summary>
    public void UseRightFoot() => ProjectionFlipV = true;

    /// <summary>The texture the combiner takes its alpha from (FootStepCombiner.Material1).</summary>
    public void SetStepTexture(AssetRef Texture)
    {
        if (!Texture.IsNone)
        {
            ProjTexture = Texture;
        }
    }
}
