using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

// Constants of the goggle glow (spec/equipment/goggle-glow.md).
public static partial class K
{
    /// <summary>The bone both beams hang on.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPawnAttaque.PostBeginPlay, SPAWNDEFENSE.PostBeginPlay: AttachToBone(GoggleBeam, 'B Head')")]
    public static readonly Name GoggleBeamBone = new("B Head");

    /// <summary>The spy beam's rotation relative to the head bone.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPawnAttaque.PostBeginPlay: SetRelativeRotation(rot(0,1500,0))")]
    public static readonly FRotator SpyGoggleBeamRotation = new(0, 1500, 0);

    /// <summary>The merc beam's rotation relative to the head bone.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPAWNDEFENSE.PostBeginPlay: SetRelativeRotation(rot(0,1250,0))")]
    public static readonly FRotator MercGoggleBeamRotation = new(0, 1250, 0);

    /// <summary>The spy goggles' bone turned when they come down (no such bone in the Versus meshes).</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimEvent_GoggleSwitchDown: SetBoneRotation('BGoggles', rot(0,0,-5400)) for SPAWNOFFENSE")]
    public static readonly Name SpyGogglesBone = new("BGoggles");

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimEvent_GoggleSwitchDown: rot(0,0,-5400)")]
    public static readonly FRotator SpyGogglesDownRotation = new(0, 0, -5400);

    // Beam modes (SGoggleBeam.CurrentMode), as ExecuteActionForGoggle sets them and ChangeSkin reads them.
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.ExecuteActionForGoggle: 72 and 269 set 0 for a spy")]
    public const int BeamModeNone = 0;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.ExecuteActionForGoggle: 68 (thermal) sets 1")]
    public const int BeamModeThermal = 1;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.ExecuteActionForGoggle: 67 (night) sets 2")]
    public const int BeamModeNight = 2;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.ExecuteActionForGoggle: 69 (motion tracking) sets 3")]
    public const int BeamModeMotion = 3;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.ExecuteActionForGoggle: 66 (EMF) sets 4")]
    public const int BeamModeEmf = 4;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.ExecuteActionForGoggle: 72 and 269 set 5 for a merc")]
    public const int BeamModeNormal = 5;

    // The beam skins (SGoggleBeam.ChangeSkin).
    [Provenance(ProvenanceKind.Decoded, "SGoggleBeam.ChangeSkin modes 1, 2: Shader'SGoggleTextures.beam_goggle.TRA_goggle_green'")]
    public static readonly AssetRef GoggleSkinGreen = new("SGoggleTextures.beam_goggle.TRA_goggle_green");

    [Provenance(ProvenanceKind.Decoded, "SGoggleBeam.ChangeSkin modes 0 (merc), 5: Shader'SGoggleTextures.beam_goggle.TRD_goggle_orange'")]
    public static readonly AssetRef GoggleSkinOrange = new("SGoggleTextures.beam_goggle.TRD_goggle_orange");

    [Provenance(ProvenanceKind.Decoded, "SGoggleBeam.ChangeSkin mode 3: Shader'SGoggleTextures.beam_goggle.TRD_goggle_motion'")]
    public static readonly AssetRef GoggleSkinMotion = new("SGoggleTextures.beam_goggle.TRD_goggle_motion");

    [Provenance(ProvenanceKind.Decoded, "SGoggleBeam.ChangeSkin mode 4: Shader'SGoggleTextures.beam_goggle.TRD_goggle_EMF'")]
    public static readonly AssetRef GoggleSkinEmf = new("SGoggleTextures.beam_goggle.TRD_goggle_EMF");
}
