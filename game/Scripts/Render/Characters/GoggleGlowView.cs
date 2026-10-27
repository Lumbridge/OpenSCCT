using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.Characters;

/// <summary>
/// The goggle glow of one drawn pawn (spec/equipment/goggle-glow.md rule 8): the pawn's SGoggleBeam mesh
/// (SGoggle.ATT01_beams for a spy, DEF01_beams for a merc) at its bone with its relative rotation, wearing the beam's
/// skin (Skins[0], or the mesh's own material). The skins are fullbright and two-sided: the spy's green is alpha
/// blended through its opacity texture, the merc's orange, motion and EMF are screen-added. HeatOpacity and EMFOpacity
/// 0 keep it out of thermal and EMF vision. Reads the simulation and never writes it.
/// </summary>
public sealed class GoggleGlowView
{
    private static readonly Dictionary<string, Material?> s_materials = new(StringComparer.OrdinalIgnoreCase);

    private readonly Node3D _parent;
    private readonly string? _sharedRoot;
    private readonly Func<Name, (bool Ok, Transform3D World)> _bone;
    private SkeletalModel? _model;
    private string? _asset;
    private string? _skinOn;

    public GoggleGlowView(Node3D parent, string? sharedRoot, Func<Name, (bool Ok, Transform3D World)> bone)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _sharedRoot = sharedRoot;
        _bone = bone ?? throw new ArgumentNullException(nameof(bone));
    }

    /// <summary>True when the glow was drawn this frame (its pawn view shown too: not the own pawn in first person).</summary>
    public bool Shown => _model is { } m && m.Root.IsVisibleInTree();

    /// <summary>The skin drawn (the material's package and name, without groups), or the mesh's own material's.</summary>
    public string? DrawnSkin => _model is null ? null : _skinOn;

    /// <summary>The mesh drawn (SGoggle.ATT01_beams or SGoggle.DEF01_beams), or null.</summary>
    public string? DrawnMesh => _asset;

    /// <summary>The model drawn (for checks), or null.</summary>
    public SkeletalModel? Model => _model;

    /// <summary>Places the glow of <paramref name="pawn"/> for this frame, or hides it when the pawn has none.</summary>
    public void Draw(SBasePawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        SGoggleBeam? beam = pawn.GoggleBeam;
        if (beam is null || beam.bDeleteMe || beam.bHidden || beam.Mesh.IsNone)
        {
            Hide();
            return;
        }

        if (!Load(beam.Mesh.Path!))
        {
            return;
        }

        (bool ok, Transform3D bone) = _bone(beam.AttachmentBone);
        if (!ok)
        {
            Hide();
            return;
        }

        // UE2 bone attachment: the actor's axes are the bone's turned by RelativeRotation in the bone's own frame.
        Basis basis = bone.Basis.Orthonormalized() * UeSpace.RotationToGodot(beam.RelativeRotation);
        Vector3 origin = bone.Origin + (bone.Basis.Orthonormalized() * UeSpace.ToGodot(beam.RelativeLocation));
        _model!.Root.GlobalTransform = new Transform3D(basis, origin);
        string skin = SkinKey(beam.Skin.IsNone ? null : beam.Skin.Path) ?? _model.Slots[0];
        if (skin != _skinOn)
        {
            Material? material = MaterialFor(skin);
            _model.SetSkins(_ => material);
            _skinOn = skin;
        }

        _model.SetVision(new Vector4(beam.HeatIntensity, beam.HeatOpacity, 0f, beam.EMFOpacity));
        // OpenSCCT's own: the player's light colour turns the beam's hue (its brightness stays retail). Only the
        // normal glow: the merc's motion (red) and EMF (blue) beams keep their colours, which tell the spies his mode.
        bool normal = skin.EndsWith("goggle_green", StringComparison.OrdinalIgnoreCase) ||
            skin.EndsWith("goggle_orange", StringComparison.OrdinalIgnoreCase);
        _model.SetGlowHue(normal ? PawnCosmetics.HueTurn(PawnCosmetics.LookOf(pawn)[OpenScct.Net.Cosmetics.CosmeticSlot.Light],
            PawnCosmetics.SideOf(pawn)) : null, diffuse: true);
        _model.Root.Visible = true;
    }

    /// <summary>
    /// The glow as the cosmetic previews show it (OpenSCCT's own), with no simulated pawn: the side's beam on the head
    /// bone at the side's relative rotation, in its normal skin (the spy's green, the merc's orange) turned by
    /// <paramref name="hueTurn"/> as the game turns it for the player's light colour.
    /// </summary>
    public void DrawStill(bool merc, float? hueTurn)
    {
        if (!Load(merc ? "SGoggle.DEF01_beams" : "SGoggle.ATT01_beams"))
        {
            return;
        }

        (bool ok, Transform3D bone) = _bone(OpenScct.Game.Equipment.K.GoggleBeamBone);
        if (!ok)
        {
            Hide();
            return;
        }

        FRotator relative = merc ? OpenScct.Game.Equipment.K.MercGoggleBeamRotation : OpenScct.Game.Equipment.K.SpyGoggleBeamRotation;
        _model!.Root.GlobalTransform = new Transform3D(bone.Basis.Orthonormalized() * UeSpace.RotationToGodot(relative), bone.Origin);
        string skin = SkinKey((merc ? OpenScct.Game.Equipment.K.GoggleSkinOrange : OpenScct.Game.Equipment.K.GoggleSkinGreen).Path) ?? _model.Slots[0];
        if (skin != _skinOn)
        {
            Material? material = MaterialFor(skin);
            _model.SetSkins(_ => material);
            _skinOn = skin;
        }

        _model.SetGlowHue(hueTurn, diffuse: true);
        _model.Root.Visible = true;
    }

    /// <summary>Hides the glow (the previews' gun views).</summary>
    public void HideGlow() => Hide();

    private void Hide()
    {
        if (_model is not null)
        {
            _model.Root.Visible = false;
        }
    }

    private bool Load(string asset)
    {
        if (_model is not null && string.Equals(asset, _asset, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_model is not null)
        {
            _model.Root.QueueFree();
            _model = null;
            _skinOn = null;
        }

        _asset = asset;
        if (SkeletalModel.FindGlb(asset, _sharedRoot) is not { } glb)
        {
            return false;
        }

        _model = SkeletalModel.Load(glb);
        _model.Root.Name = "GoggleGlow";
        _parent.AddChild(_model.Root);
        foreach (MeshInstance3D mesh in _model.Meshes)
        {
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        return true;
    }

    /// <summary>A material path with groups (SGoggleTextures.beam_goggle.TRD_goggle_EMF) as package and name.</summary>
    public static string? SkinKey(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        int first = path.IndexOf('.', StringComparison.Ordinal), last = path.LastIndexOf('.');
        return first > 0 ? path[..first] + path[last..] : path;
    }

    /// <summary>
    /// The skin's material: textures/&lt;Package&gt;_&lt;Name&gt;.png next to the beam meshes (the chars step exports the four),
    /// fullbright, two-sided, alpha blended for the TRA (opacity) skin and added for the TRD ones.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SGoggleTextures shaders: OutputBlending 3, TwoSided, SelfIllumination = Diffuse; TRA_goggle_green has Opacity = A_goggle_green")]
    [Provenance(ProvenanceKind.Estimated, "Blends as the material resolver maps them: OB_Translucent with Opacity alpha blended, without it screen-added (drawn additive)")]
    private Material? MaterialFor(string key)
    {
        string folder = Path.Combine(_sharedRoot ?? SkeletalModel.DefaultSharedRoot(), "chars", "SGoggle");
        string cacheKey = folder + "|" + key;
        if (s_materials.TryGetValue(cacheKey, out Material? cached))
        {
            return cached;
        }

        string uri = "textures/" + key.Replace('.', '_') + ".png";
        Material? material = null;
        if (File.Exists(Path.Combine(folder, uri)))
        {
            bool opacity = key.EndsWith(".TRA_goggle_green", StringComparison.OrdinalIgnoreCase);
            var entry = new MaterialEntry
            {
                Name = key,
                Texture = uri,
                Unlit = true,
                TwoSided = true,
                Translucent = opacity,
                ListBlend = opacity ? MaterialBlend.Translucent : MaterialBlend.Additive,
            };
            material = new MaterialCompiler(new MaterialsDoc(), new TextureCache(folder)).Build(entry, LightSource.None);
        }

        s_materials[cacheKey] = material;
        return material;
    }
}
