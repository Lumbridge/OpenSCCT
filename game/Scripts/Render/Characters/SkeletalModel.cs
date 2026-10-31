using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Gltf;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.Characters;

/// <summary>
/// A skeletal mesh of the shared packs (.content/shared/chars: the spy ATT_01, the merc DEF_01, animated props such
/// as SteeE's crane) with its sequences, drawn with the actor-lit Faithful material and posed by
/// <see cref="Ue2PoseApplier"/>. Placement follows UE2: world = Location + PrePivot, then Rotation, then the mesh's
/// RotOrigin and scale. docs/RENDERING.md#characters.
/// </summary>
public sealed class SkeletalModel
{
    private readonly List<MeshInstance3D> _meshes = [];
    private readonly List<(MeshInstance3D Mesh, int Surface, string Name, Material? Own)> _surfaces = [];

    private SkeletalModel(Node3D root, Node3D pivot, Skeleton3D skeleton, AnimSampler sampler, FRotator rotOrigin, Vector3 meshScale)
    {
        Root = root;
        Pivot = pivot;
        Skeleton = skeleton;
        Sampler = sampler;
        RotOrigin = rotOrigin;
        MeshScale = meshScale;
    }

    /// <summary>Extra culling margin of skinned meshes, in metres.</summary>
    public const float CullMargin = 64f;

    /// <summary>The actor node: placed at Location + PrePivot with the actor's rotation.</summary>
    public Node3D Root { get; }

    /// <summary>The mesh node under <see cref="Root"/>: RotOrigin and scale.</summary>
    public Node3D Pivot { get; }

    public Skeleton3D Skeleton { get; }

    public AnimSampler Sampler { get; }

    public IReadOnlyList<MeshInstance3D> Meshes => _meshes;

    public FRotator RotOrigin { get; }

    public Vector3 MeshScale { get; }

    /// <summary>The shared folder of a skeletal mesh asset path such as <c>SPerso.ATT_01</c>.</summary>
    public static string? FindGlb(string asset, string? sharedRoot = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(asset);
        sharedRoot ??= DefaultSharedRoot();
        int dot = asset.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
        {
            return null;
        }

        string path = Path.Combine(sharedRoot, "chars", asset[..dot], asset[(dot + 1)..] + ".glb");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// A skeletal mesh a map places (an SAnimatedMesh prop, a DT_Mesh fish): the map's own copy
    /// (<c>render/chars/&lt;Package&gt;/&lt;Mesh&gt;.glb</c>, which the converter writes so a downloaded map does not need its
    /// anim package in the player's install), else the shared one.
    /// </summary>
    public static string? FindMapGlb(string asset, string? mapFolder, string? sharedRoot = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(asset);
        if (!string.IsNullOrEmpty(mapFolder) && FindGlb(asset, Path.Combine(mapFolder, "render")) is { } own)
        {
            return own;
        }

        return FindGlb(asset, sharedRoot);
    }

    /// <summary>The shared folder next to the executable for an export, else the project's .content/shared.</summary>
    public static string DefaultSharedRoot()
    {
        // An installed build: the content the launcher built (--svm-content).
        if (OpenScct.Server.ContentPaths.Shared is { } content)
        {
            return content;
        }

        string executableDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
        string beside = Path.Combine(executableDir, "shared");
        if (OS.HasFeature("template") && Directory.Exists(beside))
        {
            return Path.GetFullPath(beside);
        }

        string projectDir = ProjectSettings.GlobalizePath("res://");
        return Path.GetFullPath(Path.Combine(projectDir, "..", ".content", "shared"));
    }

    /// <summary>
    /// A model of <paramref name="glbPath"/>. The file is read, its scene generated and its materials compiled once per
    /// path; later loads duplicate that scene and share its meshes, materials and clips. Reading the glTF again for
    /// every pawn view took half a second each time a pawn respawned (the drawn pawn is made anew).
    /// </summary>
    public static SkeletalModel Load(string glbPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(glbPath);
        string key = Path.GetFullPath(glbPath);
        if (!s_templates.TryGetValue(key, out Template? template) || !GodotObject.IsInstanceValid(template.Scene))
        {
            template = BuildTemplate(glbPath);
            s_templates[key] = template;
            FilesRead++;
        }
        else
        {
            Reused++;
        }

        var scene = (Node3D)template.Scene.Duplicate();
        Skeleton3D skeleton = FindFirst<Skeleton3D>(scene) ?? throw new InvalidDataException($"{glbPath}: no skeleton");
        string name = Path.GetFileNameWithoutExtension(glbPath);
        var root = new Node3D { Name = name };
        var pivot = new Node3D { Name = "Mesh", Transform = new Transform3D(UeSpace.RotationToGodot(template.RotOrigin), Vector3.Zero) };
        root.AddChild(pivot);
        pivot.AddChild(scene);
        pivot.Scale = UeSpace.DirectionToGodot(new FVector(template.MeshScale.X, template.MeshScale.Y, template.MeshScale.Z));
        var model = new SkeletalModel(root, pivot, skeleton, template.Sampler.For(skeleton), template.RotOrigin, template.MeshScale)
        {
            _template = template,
        };
        foreach (MeshInstance3D mesh in FindAll<MeshInstance3D>(scene))
        {
            mesh.ExtraCullMargin = CullMargin;
            if (!template.Warm && (template.CullBounds ??= new AnimatedCullBounds(template.Sampler)).TryGet(mesh, skeleton, out Aabb bounds))
            {
                mesh.CustomAabb = bounds;
                mesh.ExtraCullMargin = 0f;
            }
            model._meshes.Add(mesh);
            if (mesh.Mesh is null)
            {
                continue;
            }

            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                string? matName = mesh.Mesh.SurfaceGetMaterial(s)?.ResourceName;
                model._surfaces.Add((mesh, s, matName ?? "", mesh.GetSurfaceOverrideMaterial(s)));
            }
        }

        // Characters are warm (HeatIntensity 1 for both sides); views set the EMF values from the pawn's state.
        if (template.Warm)
        {
            model.SetVision(new Vector4(RenderK.PawnHeatIntensity, RenderK.DefaultHeatOpacity, 0f, 1f));
        }

        return model;
    }

    /// <summary>Model files read (main thread; for the frame-time smoke's report).</summary>
    public static int FilesRead { get; private set; }

    /// <summary>Models made from a scene already read.</summary>
    public static int Reused { get; private set; }

    /// <summary>Models read so far, by full path: the generated scene (never in the tree) with its materials, and its clips.</summary>
    private static readonly Dictionary<string, Template> s_templates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Frees the kept scenes (at exit; a later load reads the file again).</summary>
    public static void ReleaseTemplates()
    {
        foreach (Template t in s_templates.Values)
        {
            if (GodotObject.IsInstanceValid(t.Scene))
            {
                t.Scene.Free();
            }
        }

        s_templates.Clear();
    }

    private sealed record Template(Node3D Scene, AnimSampler Sampler, FRotator RotOrigin, Vector3 MeshScale, bool Warm,
        string Folder, MaterialCompiler Compiler, Dictionary<string, MaterialEntry> Materials)
    {
        /// <summary>Materials made for skins (UE2 Skins[]) over a slot, by the slot's own material and the skin.</summary>
        public Dictionary<(string Own, string Skin), Material?> Swaps { get; } = new();
        public AnimatedCullBounds? CullBounds { get; set; }

        /// <summary>
        /// <paramref name="own"/>'s material drawing the texture of the material <paramref name="skin"/> (package and
        /// name, as the mesh export names materials) instead: textures/Package_Name.png next to the model. Null when
        /// the chars step did not export that texture.
        /// </summary>
        public Material? Swap(string own, string skin)
        {
            if (Swaps.TryGetValue((own, skin), out Material? made))
            {
                return made;
            }

            string uri = "textures/" + skin.Replace('.', '_') + ".png";
            made = Materials.TryGetValue(own, out MaterialEntry? entry) && File.Exists(Path.Combine(Folder, uri))
                ? Compiler.Build(new MaterialEntry
                {
                    Name = skin,
                    Texture = uri,
                    NormalTexture = entry.NormalTexture,
                    Masked = entry.Masked,
                    Translucent = entry.Translucent,
                    TwoSided = entry.TwoSided,
                }, LightSource.Actor)
                : null;
            Swaps[(own, skin)] = made;
            return made;
        }

        /// <summary>
        /// <paramref name="own"/>'s material with its diffuse replaced by the content builder's camo version
        /// (textures/cosmetic_&lt;pattern&gt;_&lt;own texture file&gt;), keeping everything else (self-illumination,
        /// blending). Null when that file was not built.
        /// </summary>
        public Material? Cosmetic(string own, string pattern)
        {
            string key = "cosmetic:" + pattern;
            if (Swaps.TryGetValue((own, key), out Material? made))
            {
                return made;
            }

            made = null;
            if (Materials.TryGetValue(own, out MaterialEntry? e) && e.Texture is { } texture)
            {
                string uri = "textures/cosmetic_" + pattern + "_" + Path.GetFileName(texture);
                if (File.Exists(Path.Combine(Folder, uri)))
                {
                    made = Compiler.Build(new MaterialEntry
                    {
                        Name = e.Name + "+" + pattern, Source = e.Source, Texture = uri, NormalTexture = e.NormalTexture, Masked = e.Masked, Translucent = e.Translucent,
                        TwoSided = e.TwoSided, Unlit = e.Unlit, SelfIllum = e.SelfIllum, SelfIllumMask = e.SelfIllumMask, SpecMask = e.SpecMask,
                        EnvColor = e.EnvColor, UvAnim = e.UvAnim, ListBlend = e.ListBlend,
                    }, LightSource.Actor);
                    if (made is ShaderMaterial shiny)
                    {
                        // Painted in screen colours: the world shader brings its texels into the retail range
                        // (svm_cosmetic_level) so pale camos light up without clipping to white.
                        shiny.SetShaderParameter(CamoParameter, true);

                        // The world shader's sheen (svm_sheen): on the golden and mastercraft camos a full gold mirror that stays bright
                        // in any light (a special finish), on the pyre camo glossy black lacquer with near-white
                        // highlights that darkens in shadow like everything else.
                        if (pattern is "golden" or "mastercraft")
                        {
                            shiny.SetShaderParameter(SheenParameter, 1f);
                            shiny.SetShaderParameter(SheenTintParameter, new Vector3(1f, 0.8f, 0.36f));
                            shiny.SetShaderParameter(SheenMirrorParameter, 1f);
                        }
                        else if (pattern == "pyre")
                        {
                            shiny.SetShaderParameter(SheenParameter, 0.7f);
                            shiny.SetShaderParameter(SheenTintParameter, new Vector3(1f, 0.93f, 0.85f));
                        }
                    }
                }
            }

            Swaps[(own, key)] = made;
            return made;
        }

        /// <summary>The meshes made by <see cref="Split"/>, by source mesh and material.</summary>
        private Dictionary<(Mesh, string), MeshSplit?> Splits { get; } = new();

        /// <summary>
        /// <paramref name="mesh"/> cut in two: the surfaces drawing <paramref name="material"/> and the rest, each a mesh
        /// of its own (made once and shared by every model of this file). Null when no surface draws it or every
        /// surface does.
        /// </summary>
        public MeshSplit? Split(Mesh? mesh, string material)
        {
            if (mesh is not ArrayMesh source)
            {
                return null;
            }

            if (Splits.TryGetValue((source, material), out MeshSplit? made))
            {
                return made;
            }

            var rest = new List<int>();
            var part = new List<int>();
            for (int s = 0; s < source.GetSurfaceCount(); s++)
            {
                (source.SurfaceGetMaterial(s)?.ResourceName == material ? part : rest).Add(s);
            }

            made = part.Count == 0 || rest.Count == 0 ? null : new MeshSplit(Pick(source, rest), [.. rest], Pick(source, part), [.. part]);
            Splits[(source, material)] = made;
            return made;
        }

        // The flags a surface keeps when copied: 8 bone weights and the custom channel formats.
        private static readonly Mesh.ArrayFormat KeptFlags = Mesh.ArrayFormat.FlagUse8BoneWeights | (Mesh.ArrayFormat)(((1L << 12) - 1) << 13);

        private static ArrayMesh Pick(ArrayMesh source, List<int> surfaces)
        {
            var made = new ArrayMesh { ResourceName = source.ResourceName, BlendShapeMode = source.BlendShapeMode };
            for (int b = 0; b < source.GetBlendShapeCount(); b++)
            {
                made.AddBlendShape(source.GetBlendShapeName(b));
            }

            foreach (int s in surfaces)
            {
                made.AddSurfaceFromArrays(source.SurfaceGetPrimitiveType(s), source.SurfaceGetArrays(s),
                    source.SurfaceGetBlendShapeArrays(s), null, source.SurfaceGetFormat(s) & KeptFlags);
                int i = made.GetSurfaceCount() - 1;
                made.SurfaceSetMaterial(i, source.SurfaceGetMaterial(s));
                made.SurfaceSetName(i, source.SurfaceGetName(s));
            }

            return made;
        }
    }

    /// <summary>A mesh cut in two by material: each half with the source surface index of each of its surfaces.</summary>
    private sealed record MeshSplit(ArrayMesh Rest, int[] RestSurfaces, ArrayMesh Part, int[] PartSurfaces);

    // The per-mesh shader values a split-off part takes over from its source mesh.
    // (A property: the parameter names are static fields declared further down, not yet set while this one would be.)
    private static StringName[] InstanceParameters => [TintParameter, GlowHueParameter, GameplayObjects.VisionActorParameter, GlassParameter];

    /// <summary>
    /// Moves the surfaces drawing <paramref name="material"/> (as the mesh export names it, such as SWeaponTex.1DEF_01)
    /// into mesh instances of their own, on the same skeleton and skin with the same materials, so a camo or tint can
    /// be put on them apart from the rest (<see cref="SetCosmetic(MeshInstance3D, string?)"/>,
    /// <see cref="SetTint(MeshInstance3D, Vector3, float)"/>). Returns those instances (a mesh drawing nothing but
    /// that material is returned as it is); empty when no surface draws it. Material slots (<see cref="Slots"/>) keep
    /// their order. Call it right after <see cref="Load"/>, before any skin.
    /// </summary>
    public IReadOnlyList<MeshInstance3D> SplitPart(string material)
    {
        var parts = new List<MeshInstance3D>();
        if (_template is null)
        {
            return parts;
        }

        foreach (MeshInstance3D mesh in _meshes.ToArray())
        {
            if (mesh.Mesh is null)
            {
                continue;
            }

            if (_template.Split(mesh.Mesh, material) is not { } split)
            {
                bool all = mesh.Mesh.GetSurfaceCount() > 0;
                for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
                {
                    all &= mesh.Mesh.SurfaceGetMaterial(s)?.ResourceName == material;
                }

                if (all)
                {
                    parts.Add(mesh);
                }

                continue;
            }

            var part = new MeshInstance3D
            {
                Name = mesh.Name + "_" + material.Replace('.', '_'),
                Mesh = split.Part,
                Skin = mesh.Skin,
                Skeleton = mesh.Skeleton,
                Transform = mesh.Transform,
                ExtraCullMargin = mesh.ExtraCullMargin,
                CastShadow = mesh.CastShadow,
                Layers = mesh.Layers,
                Transparency = mesh.Transparency,
                MaterialOverlay = mesh.MaterialOverlay,
            };
            mesh.GetParent().AddChild(part);
            foreach (StringName p in InstanceParameters)
            {
                Variant value = mesh.GetInstanceShaderParameter(p);
                if (value.VariantType != Variant.Type.Nil)
                {
                    part.SetInstanceShaderParameter(p, value);
                }
            }

            // Each surface's override material by its index in the source mesh, then laid on the two halves.
            var overrides = new Material?[mesh.Mesh.GetSurfaceCount()];
            for (int s = 0; s < overrides.Length; s++)
            {
                overrides[s] = mesh.GetSurfaceOverrideMaterial(s);
            }

            mesh.Mesh = split.Rest;
            var moved = new Dictionary<int, (MeshInstance3D, int)>();
            for (int i = 0; i < split.RestSurfaces.Length; i++)
            {
                mesh.SetSurfaceOverrideMaterial(i, overrides[split.RestSurfaces[i]]);
                moved[split.RestSurfaces[i]] = (mesh, i);
            }

            for (int i = 0; i < split.PartSurfaces.Length; i++)
            {
                part.SetSurfaceOverrideMaterial(i, overrides[split.PartSurfaces[i]]);
                moved[split.PartSurfaces[i]] = (part, i);
            }

            // The surface list in place, so the material slots keep their order (UE2 Skins indices).
            for (int e = 0; e < _surfaces.Count; e++)
            {
                (MeshInstance3D m, int s, string name, Material? own) = _surfaces[e];
                if (ReferenceEquals(m, mesh))
                {
                    (MeshInstance3D to, int index) = moved[s];
                    _surfaces[e] = (to, index, name, own);
                }
            }

            _meshes.Add(part);
            parts.Add(part);
        }

        return parts;
    }

    private static Template BuildTemplate(string glbPath)
    {
        var document = new GltfDocument();
        var state = new GltfState();
        Error error = document.AppendFromFile(glbPath, state);
        if (error != Error.Ok || document.GenerateScene(state) is not Node3D scene)
        {
            throw new InvalidDataException($"{glbPath}: glTF load failed ({error})");
        }

        Skeleton3D skeleton = FindFirst<Skeleton3D>(scene) ?? throw new InvalidDataException($"{glbPath}: no skeleton");
        var animations = new List<(string, Animation)>();
        if (FindFirst<AnimationPlayer>(scene) is { } player)
        {
            foreach (StringName animName in player.GetAnimationList())
            {
                animations.Add((animName.ToString(), player.GetAnimation(animName)));
            }

            // The player never drives the pose: the sampler and the pose applier do.
            player.GetParent()?.RemoveChild(player);
            player.Free();
        }

        FRotator rotOrigin = default;
        Vector3 meshScale = Vector3.One;
        Dictionary<string, MaterialEntry> materials;
        using (GlbDocument glb = GlbDocument.Load(glbPath))
        {
            ReadExtras(glb.Root, ref rotOrigin, ref meshScale);
            materials = ReadMaterials(glb.Root);
        }

        var textures = new TextureCache(Path.GetDirectoryName(glbPath) ?? ".");
        // shared/chars/<Package>/<Mesh>.glb: the render pack (heat and EMF skins) is shared/render.
        string? shared = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(glbPath))));
        var compiler = new MaterialCompiler(new MaterialsDoc(), textures)
        {
            Pack = shared is null ? null : RenderPack.Load(shared),
            VisionLookup = VisionLookup.SkinBase,
        };
        foreach (MeshInstance3D mesh in FindAll<MeshInstance3D>(scene))
        {
            // The pose moves skinned vertices far from the rest pose the culling box is made of (SteeE's crane, drawn
            // at DrawScale 10, was culled out of views it fills): cull by a generous box instead.
            mesh.ExtraCullMargin = CullMargin;
            if (mesh.Mesh is null)
            {
                continue;
            }

            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                string? matName = mesh.Mesh.SurfaceGetMaterial(s)?.ResourceName;
                if (matName is not null && materials.TryGetValue(matName, out MaterialEntry? entry))
                {
                    mesh.SetSurfaceOverrideMaterial(s, compiler.Build(entry, LightSource.Actor));
                }
            }
        }

        bool warm = string.Equals(Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(glbPath))), "SPerso", StringComparison.OrdinalIgnoreCase);
        return new Template(scene, new AnimSampler(skeleton, animations), rotOrigin, meshScale, warm,
            Path.GetDirectoryName(Path.GetFullPath(glbPath)) ?? ".", compiler, materials);
    }

    /// <summary>UE2 placement: Location + PrePivot, the actor rotation, and DrawScale on the mesh.</summary>
    /// <summary>
    /// Puts skins over the mesh's own materials (UE2 Skins[]): <paramref name="skin"/> gives, for a surface's material
    /// name (the package and texture name, as the mesh export names it), the material to draw instead, or null for
    /// the mesh's own. A null <paramref name="skin"/> puts every surface back.
    /// </summary>
    public int SetSkins(Func<string, Material?>? skin)
    {
        int replaced = 0;
        foreach ((MeshInstance3D mesh, int surface, string name, Material? own) in _surfaces)
        {
            Material? over = skin?.Invoke(name);
            mesh.SetSurfaceOverrideMaterial(surface, over ?? Base(mesh, surface, own));
            if (over is null)
            {
                _skinned.Remove((mesh, surface));
            }
            else
            {
                _skinned.Add((mesh, surface));
            }

            replaced += over is null ? 0 : 1;
        }

        return replaced;
    }

    // ---- OpenSCCT's own cosmetics (camo textures, tints, light hues) ----------------------------------------------

    /// <summary>Surfaces drawing a cosmetic camo under any skin: what a skin's end puts back instead of the mesh's own.</summary>
    private readonly Dictionary<(MeshInstance3D, int), Material> _cosmetic = [];
    private readonly HashSet<(MeshInstance3D, int)> _skinned = [];
    private readonly Dictionary<MeshInstance3D, string?> _cosmeticPatterns = [];

    private Material? Base(MeshInstance3D mesh, int surface, Material? own) =>
        _cosmetic.TryGetValue((mesh, surface), out Material? camo) ? camo : own;

    /// <summary>
    /// Draws the camo <paramref name="pattern"/> (CosmeticItem.Pattern; null for none) on every surface the content
    /// builder made a camo texture for. Skins (Sticky shock, Skins[]) still draw over it and give it back when they end.
    /// </summary>
    public void SetCosmetic(string? pattern)
    {
        foreach (MeshInstance3D mesh in _meshes)
        {
            SetCosmetic(mesh, pattern);
        }
    }

    /// <summary><see cref="SetCosmetic(string?)"/> on one mesh of the model only (a part made by <see cref="SplitPart"/>, or the rest).</summary>
    public void SetCosmetic(MeshInstance3D only, string? pattern)
    {
        if (_template is null || _cosmeticPatterns.GetValueOrDefault(only) == pattern)
        {
            return;
        }

        _cosmeticPatterns[only] = pattern;
        IReadOnlyList<string> slots = Slots;
        var slotted = new HashSet<string>();
        foreach (int slot in _slotSkins.Keys)
        {
            if (slot < slots.Count)
            {
                slotted.Add(slots[slot]);
            }
        }

        foreach ((MeshInstance3D mesh, int surface, string name, Material? own) in _surfaces)
        {
            if (!ReferenceEquals(mesh, only))
            {
                continue;
            }

            _cosmetic.Remove((mesh, surface));
            if (pattern is not null && _template.Cosmetic(name, pattern) is { } camo)
            {
                _cosmetic[(mesh, surface)] = camo;
            }

            if (!_skinned.Contains((mesh, surface)) && !slotted.Contains(name))
            {
                mesh.SetSurfaceOverrideMaterial(surface, Base(mesh, surface, own));
            }
        }
    }

    /// <summary>
    /// A colour tint over every mesh: a texel of the retail suits' mean brightness takes <paramref name="rgb"/>,
    /// brighter and darker texels in proportion (the world shader's SVM_TINT_REF), brought into the retail range
    /// (svm_cosmetic_level); <paramref name="strength"/> 0 is none.
    /// </summary>
    public void SetTint(Vector3 rgb, float strength)
    {
        foreach (MeshInstance3D mesh in _meshes)
        {
            SetTint(mesh, rgb, strength);
        }
    }

    /// <summary><see cref="SetTint(Vector3, float)"/> on one mesh of the model only.</summary>
    public static void SetTint(MeshInstance3D mesh, Vector3 rgb, float strength)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        mesh.SetInstanceShaderParameter(TintParameter, new Vector4(rgb.X, rgb.Y, rgb.Z, strength));
    }

    /// <summary>
    /// Turns the hue of the self-illumination (goggle lenses, lamps) by <paramref name="radians"/>, keeping its
    /// brightness; with <paramref name="diffuse"/> the textures too (the goggle beams). Null puts the retail colour back.
    /// </summary>
    public void SetGlowHue(float? radians, bool diffuse = false)
    {
        Vector4 value = radians is { } r ? new Vector4(r, 1f, diffuse ? 1f : 0f, 0f) : Vector4.Zero;
        foreach (MeshInstance3D mesh in _meshes)
        {
            mesh.SetInstanceShaderParameter(GlowHueParameter, value);
        }
    }

    public static readonly StringName TintParameter = "svm_tint";
    public static readonly StringName CamoParameter = "svm_camo";
    public static readonly StringName SheenParameter = "svm_sheen";
    public static readonly StringName SheenTintParameter = "svm_sheen_tint";
    public static readonly StringName SheenMirrorParameter = "svm_sheen_mirror";
    public static readonly StringName GlowHueParameter = "svm_glow_hue";

    private Template? _template;
    private readonly Dictionary<int, string> _slotSkins = [];

    /// <summary>The mesh's material names by material slot (the UE2 Skins index): the surfaces' materials in order.</summary>
    public IReadOnlyList<string> Slots
    {
        get
        {
            var names = new List<string>();
            foreach ((_, _, string name, _) in _surfaces)
            {
                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }
    }

    /// <summary>
    /// Puts the material <paramref name="skin"/> (a package path such as SWeaponTex.Armes.SI_rof_auto; null or the
    /// slot's own material: none) over material slot <paramref name="slot"/>, as UE2 Skins[slot]. False when the
    /// skin's texture was not exported (the slot keeps its own material).
    /// </summary>
    public bool SetSlotSkin(int slot, string? skin)
    {
        IReadOnlyList<string> slots = Slots;
        if (slot < 0 || slot >= slots.Count || _template is null)
        {
            return false;
        }

        // The mesh export names materials by package and name, without the groups.
        string? key = null;
        if (!string.IsNullOrEmpty(skin))
        {
            int first = skin.IndexOf('.', StringComparison.Ordinal), last = skin.LastIndexOf('.');
            key = first > 0 ? skin[..first] + skin[last..] : skin;
        }

        string own = slots[slot];
        if (key is not null && string.Equals(key, own, StringComparison.OrdinalIgnoreCase))
        {
            key = null;
        }

        if (_slotSkins.TryGetValue(slot, out string? current) ? current == key : key is null)
        {
            return true;
        }

        Material? over = key is null ? null : _template.Swap(own, key);
        foreach ((MeshInstance3D mesh, int surface, string name, Material? material) in _surfaces)
        {
            if (name == own)
            {
                mesh.SetSurfaceOverrideMaterial(surface, over ?? Base(mesh, surface, material));
            }
        }

        if (key is null)
        {
            _slotSkins.Remove(slot);
        }
        else
        {
            _slotSkins[slot] = key;
        }

        return over is not null || key is null;
    }

    /// <summary>The skin key over material slot <paramref name="slot"/> (package and name), or null for the mesh's own.</summary>
    public string? SlotSkin(int slot) => _slotSkins.TryGetValue(slot, out string? key) ? key : null;

    public void Place(FVector location, FRotator rotation, FVector prePivot, float drawScale = 1f) =>
        Place(location, rotation, prePivot, new FVector(drawScale, drawScale, drawScale));

    /// <summary>
    /// Places the model with the actor's scale per axis, <paramref name="actorScale"/> = DrawScale x DrawScale3D (UE2's
    /// actor LocalToWorld scales in the actor's own axes, after the mesh's RotOrigin; Polar's wind sock has DrawScale3D
    /// 1, 1.5, 1).
    /// </summary>
    public void Place(FVector location, FRotator rotation, FVector prePivot, FVector actorScale)
    {
        Transform3D root = UeSpace.TransformToGodot(location + prePivot, rotation);
        if (Root.Transform != root) Root.Transform = root;
        // Scaled multiplies in the parent's (actor's) axes, so the UE axes of DrawScale3D map to Godot X, Z, Y as any
        // direction does.
        Transform3D pivot = new Transform3D(UeSpace.RotationToGodot(RotOrigin), Vector3.Zero)
            .Scaled(UeSpace.DirectionToGodot(new FVector(MeshScale.X, MeshScale.Y, MeshScale.Z)))
            .Scaled(UeSpace.DirectionToGodot(actorScale));
        if (Pivot.Transform != pivot) Pivot.Transform = pivot;
    }

    /// <summary>Samples the current sequence and poses the skeleton.</summary>
    public void Pose() => Ue2PoseApplier.Apply(Sampler);

    /// <summary>
    /// The actor's vision values on every mesh: HeatIntensity, HeatOpacity, EMF glow (EMFIntensity while bEMFIsOn) and
    /// EMFOpacity (0 hides the pawn in EMF vision). docs/RENDERING.md#vision-modes.
    /// </summary>
    public void SetVision(Vector4 vision)
    {
        foreach (MeshInstance3D mesh in _meshes)
        {
            mesh.SetInstanceShaderParameter(GameplayObjects.VisionActorParameter, vision);
        }
    }

    public const string CloakShaderPath = "res://Shaders/svm_cloak.gdshader";

    public static readonly StringName GlassParameter = "svm_glass";

    // A transparency above 0 moves the meshes to the transparent pass (after the screen copy the cloak samples); this
    // one is the smallest Godot takes (alpha below 0.999), so the shading under the glass stays 99% opaque.
    private const float CloakPass = 0.01f;

    private static ShaderMaterial? s_cloak;
    private float _glass;

    /// <summary>
    /// The camo suit on every mesh (docs/RENDERING.md#cloak): <paramref name="amount"/> is GlassEffect / 255, 0 draws
    /// the model as usual.
    /// </summary>
    public void SetGlass(float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        if (amount == _glass)
        {
            return;
        }

        bool on = amount > 0f;
        if (on != (_glass > 0f))
        {
            s_cloak ??= new ShaderMaterial { Shader = GD.Load<Shader>(CloakShaderPath) };
            foreach (MeshInstance3D mesh in _meshes)
            {
                mesh.Transparency = on ? CloakPass : 0f;
                mesh.MaterialOverlay = on ? s_cloak : null;
            }
        }

        _glass = amount;
        foreach (MeshInstance3D mesh in _meshes)
        {
            mesh.SetInstanceShaderParameter(GlassParameter, amount);
        }
    }

    /// <summary>Lights every mesh of the model with one actor light set.</summary>
    public void Light(ActorLightSet set)
    {
        foreach (MeshInstance3D mesh in _meshes)
        {
            ActorLighting.Apply(mesh, set);
        }
    }

    private static void ReadExtras(JsonElement root, ref FRotator rotOrigin, ref Vector3 meshScale)
    {
        if (!root.TryGetProperty("extras", out JsonElement extras) || !extras.TryGetProperty("svmap", out JsonElement sv))
        {
            return;
        }

        if (sv.TryGetProperty("rotOrigin", out JsonElement r) && r.GetArrayLength() >= 3)
        {
            rotOrigin = new FRotator(r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32());
        }

        if (sv.TryGetProperty("meshScale", out JsonElement s) && s.GetArrayLength() >= 3)
        {
            meshScale = new Vector3(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle());
        }
    }

    private static Dictionary<string, MaterialEntry> ReadMaterials(JsonElement root)
    {
        var result = new Dictionary<string, MaterialEntry>(StringComparer.Ordinal);
        var images = new List<string?>();
        if (root.TryGetProperty("images", out JsonElement imgs))
        {
            foreach (JsonElement i in imgs.EnumerateArray())
            {
                images.Add(i.TryGetProperty("uri", out JsonElement u) ? u.GetString() : null);
            }
        }

        var textures = new List<int>();
        if (root.TryGetProperty("textures", out JsonElement texs))
        {
            foreach (JsonElement t in texs.EnumerateArray())
            {
                textures.Add(t.TryGetProperty("source", out JsonElement src) ? src.GetInt32() : -1);
            }
        }

        if (!root.TryGetProperty("materials", out JsonElement mats))
        {
            return result;
        }

        foreach (JsonElement m in mats.EnumerateArray())
        {
            string name = m.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
            string? texture = null;
            if (m.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) && pbr.TryGetProperty("baseColorTexture", out JsonElement bct))
            {
                int ti = bct.GetProperty("index").GetInt32();
                int ii = ti >= 0 && ti < textures.Count ? textures[ti] : -1;
                texture = ii >= 0 && ii < images.Count ? images[ii] : null;
            }

            JsonElement ex = m.TryGetProperty("extras", out JsonElement e) ? e : default;
            string? normalTexture = null;
            if (m.TryGetProperty("normalTexture", out JsonElement nt))
            {
                int ti = nt.GetProperty("index").GetInt32();
                int ii = ti >= 0 && ti < textures.Count ? textures[ti] : -1;
                normalTexture = ii >= 0 && ii < images.Count ? images[ii] : null;
            }
            bool hasEx = ex.ValueKind == JsonValueKind.Object;
            result[name] = new MaterialEntry
            {
                Name = name,
                Texture = texture,
                NormalTexture = normalTexture,
                SelfIllum = hasEx ? MaterialsDoc.Str(ex, "scct_self_illum") : null,
                SelfIllumMask = hasEx ? MaterialsDoc.Str(ex, "scct_self_illum_mask") : null,
                Masked = m.TryGetProperty("alphaMode", out JsonElement am) && am.GetString() == "MASK",
                Translucent = m.TryGetProperty("alphaMode", out JsonElement ab) && ab.GetString() == "BLEND",
                TwoSided = m.TryGetProperty("doubleSided", out JsonElement ds) && ds.GetBoolean(),
            };
        }

        return result;
    }

    private static T? FindFirst<T>(Node node)
        where T : Node
    {
        if (node is T t)
        {
            return t;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindFirst<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static List<T> FindAll<T>(Node node)
        where T : Node
    {
        var list = new List<T>();
        void Walk(Node n)
        {
            if (n is T t)
            {
                list.Add(t);
            }

            foreach (Node child in n.GetChildren())
            {
                Walk(child);
            }
        }

        Walk(node);
        return list;
    }
}
