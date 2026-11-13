using OpenScctGame.Render.Materials;
using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Render;
using OpenScctGame.Views;

namespace OpenScctGame.Fx;

/// <summary>
/// The spy pistol's laser on screen (spec/equipment/spy-shocker.md, "Laser drawing"): the red dot of every spy pistol
/// (its SLaserProjector, on every machine) and the beam of the local spy's own pistol (its SLaserBeam). As the native
/// places them on each machine from that machine's bones, both are drawn from the drawn hand of the pistol's pawn
/// (30 X - 4 Y + 8 Z of B R Hand, a few uu behind the drawn pistol's FX bone) along the hand's X axis to the first
/// hit during draw/holster. The ready sight converges on the shot target at the rendered aim. In the local eye view,
/// the visible beam starts at the first-person socket and uses the same hit and dot as the third-person view.
/// The simulation's placement is used when the pawn is not drawn. The dot is RET_Laser (SWeaponTex.Reticules, 32 x 32, additive) projected
/// along the line, 32 uu times its DrawScale wide, on whatever it hit; the beam is the SLaserBeam class mesh
/// (SGameplayObjects.Objects.LaserBeam, 1 uu along X) at DrawScale3D (length + 1, 0.5, 0.5), in every vision mode.
/// Drawn nodes join the groups <c>svm_spy_laser_beam</c> and <c>svm_spy_laser_dot</c> so checks can see them.
/// </summary>
[PortOf("nat:SBase.SWeaponAttaque.TickSWeaponAttaque", Spec = "spec/equipment/spy-shocker.md#rules")]
public sealed partial class SpyLaserView : Node3D
{
    public const string GroupBeam = "svm_spy_laser_beam";
    public const string GroupDot = "svm_spy_laser_dot";

    [Provenance(ProvenanceKind.Decoded, "SLaserBeam defaults StaticMesh SGameplayObjects.Objects.LaserBeam")]
    public const string BeamMesh = "SGameplayObjects.Objects.LaserBeam";

    [Provenance(ProvenanceKind.Decoded, "SLaserProjector defaults ProjTexture SWeaponTex.Reticules.RET_Laser (32 x 32)")]
    public const string DotTexture = "SWeaponTex_RET_Laser.png";

    /// <summary>The dot's width per unit of DrawScale (the texture's 32 texels, near orthographic at FOV 1).</summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 projector frustum at FOV 1: about the texture's width times DrawScale (bridge laser SPEC 3)")]
    public const float DotTexels = 32f;

    private readonly Dictionary<SWeaponAttaque, (MeshInstance3D Beam, MeshInstance3D Dot)> _nodes = new(ReferenceEqualityComparer.Instance);
    private readonly List<SWeaponAttaque> _gone = new();
    private Mesh? _beamMesh;
    private bool _beamTried;
    private Texture2D? _dotTexture;
    private bool _dotTried;

    public RenderMap? Map { get; set; }

    public PawnViews? Pawns { get; set; }

    public SpyFirstPersonView? SpyFirstPerson { get; set; }

    /// <summary>The shared content root (the converted effect textures in fx/textures).</summary>
    public string? SharedRoot { get; set; }

    public int BeamsDrawn { get; private set; }

    public int DotsDrawn { get; private set; }

    /// <summary>The last beam drawn (the local spy's own), from <see cref="BeamStart"/> to <see cref="BeamEnd"/> (UE).</summary>
    public SBasePawn? BeamPawn { get; private set; }

    public FVector BeamStart { get; private set; }

    public FVector BeamEnd { get; private set; }

    /// <summary>The last dot's projection centre (UE, at the drawn beam's hit).</summary>
    public FVector DotAt { get; private set; }

    public void Draw(Level? level, float? drawnTime = null)
    {
        BeamsDrawn = 0;
        DotsDrawn = 0;
        foreach (SWeaponAttaque pistol in _nodes.Keys)
        {
            if (level is null || pistol.bDeleteMe)
            {
                _gone.Add(pistol);
            }
        }

        foreach (SWeaponAttaque pistol in _gone)
        {
            _nodes[pistol].Beam.QueueFree();
            _nodes[pistol].Dot.QueueFree();
            _nodes.Remove(pistol);
        }

        _gone.Clear();
        if (level is null)
        {
            return;
        }

        foreach (SWeaponAttaque pistol in level.AllActors<SWeaponAttaque>())
        {
            if (pistol.bDeleteMe)
            {
                continue;
            }

            (MeshInstance3D beam, MeshInstance3D dot) = NodesFor(pistol);
            beam.Visible = pistol.LaserBeam is { bDeleteMe: false };
            dot.Visible = pistol.LaserProjector is { bDeleteMe: false };
            if (!beam.Visible && !dot.Visible)
            {
                continue;
            }

            (FVector start, FVector hit, FVector dir) = LineFromHand(pistol);
            if (beam.Visible && SpyFirstPerson is { } first && first.OwnsWeapon(pistol))
            {
                if (first.TryGetSocket(pistol, BoneLaser, out Transform3D socket))
                {
                    start = UeSpace.ToUe(socket.Origin);
                }
                else
                {
                    beam.Visible = false;
                }
            }

            if (beam.Visible && pistol.LaserBeam is { } b)
            {
                FVector d = hit - start;
                FRotator rotation = d.Size() > 0f ? d.Rotation() : dir.Rotation();
                beam.Transform = UeSpace.TransformToGodot(start, rotation, new FVector(d.Size() + 1f, b.DrawScale3D.Y, b.DrawScale3D.Z), default);
                BeamsDrawn++;
                BeamPawn = pistol.Instigator as SBasePawn;
                BeamStart = start;
                BeamEnd = hit;
            }

            if (pistol.LaserProjector is { bDeleteMe: false } p)
            {
                PlaceDot(dot, hit, dir, p.DrawScale, p.MaxTraceDistance, p.bProjectOnBackfaces);
                DotsDrawn++;
                DotAt = UeSpace.ToUe(dot.GlobalPosition);
            }
        }

        AnimateBeam(_beamLook, drawnTime ?? level.TimeSeconds);
    }

    /// <summary>
    /// The laser's line as this machine draws it (rule 14.4, run per machine from the drawn bones): from the drawn hand
    /// bone (B R Hand, B L Hand in the other hand) at 30 X - 4 Y + 8 Z of its frame (- 8 Z in the other hand), a few uu
    /// behind the drawn pistol's FX bone. While ready it converges on the zero-spread shot hit, including the
    /// animated hand's offset; draw and holster use the hand's X axis. Without a drawn hand, use the simulation line.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "laser placement helper (game 0x10ab7c80): start = hand bone origin + 30 X - 4 Y +/- 8 Z, a line of ShootingRange along X, the first hit not bBulletGoTru")]
    private (FVector Start, FVector Hit, FVector Dir) LineFromHand(SWeaponAttaque pistol)
    {
        bool otherHand = pistol.Instigator is SBasePawn { WeaponLoaded: 7 };
        if (Pawns is not null && pistol.Instigator is SBasePawn pawn && Pawns.TryGetBone(pawn, otherHand ? BoneLeftHand : BoneRightHand, out Transform3D world))
        {
            FCoords hand = PawnAttachments.ToCoords(world);
            FVector start = SWeaponAttaque.LaserStartOn(hand, otherHand);
            FVector dir = hand.XAxis.SafeNormal();
            FVector end = start + dir * pistol.ShootingRange;
            if (pawn.WeaponLoaded is 2 or 7)
            {
                FCoords? shot = null;
                if (Pawns.ViewOf(pawn) is { DrawnView: { } aim } view)
                {
                    // Match the rendered hand during pending mouse movement and body interpolation.
                    FCoords drawn = CombatNatives.SpyPistolCoords(pawn, aim);
                    drawn.Origin += view.DrawnLocation - pawn.Location;
                    shot = drawn;
                }
                return TraceReadyBeam(pistol, start, shot);
            }
            (FVector hit, _) = pistol.LaserHit(pawn, start, end);
            return (start, hit, dir);
        }

        if (pistol.LaserBeam is { bDeleteMe: false } b)
        {
            FVector d = b.Rotation.Vector();
            return (b.Location, b.Location + (d * (b.DrawScale3D.X - 1f)), d);
        }

        if (pistol.LaserProjector is { } p)
        {
            FVector d = p.Rotation.Vector();
            FVector at = p.Location + (d * (0.5f * p.MaxTraceDistance));
            return (at, at, d);
        }

        return default;
    }

    /// <summary>The ready sight uses the zero-spread shot trace, with the rendered pistol frame for local aim.</summary>
    internal static (FVector Start, FVector Hit, FVector Dir) TraceReadyBeam(SWeaponAttaque pistol, FVector start, FCoords? drawnShot = null)
    {
        var zones = CombatServices.HitZones(pistol);
        FCoords shot = drawnShot ?? CombatServices.Ports(pistol).BoneCoords(pistol, SWeaponDefense.FxBone);
        FVector shotStart = SWeaponAttaque.GetAxesAndLocation(shot.Origin, shot.XAxis.Rotation(),
            out FVector shotDir, out _, out _);
        FVector far = shotStart + shotDir * pistol.ShootingRange;
        var target = zones.TraceBone(pistol, far, shotStart, false);
        FVector at = target.bHit ? target.HitLocation : far;
        var obstruction = zones.TraceBone(pistol, at, start, false);
        FVector hit = obstruction.bHit ? obstruction.HitLocation : at;
        return (start, hit, (hit - start).SafeNormal());
    }

    private static readonly Name BoneLaser = new("Laser");
    private static readonly Name BoneRightHand = new("B R Hand");
    private static readonly Name BoneLeftHand = new("B L Hand");

    /// <summary>
    /// Project RET_Laser onto visible surfaces inside the original's short volume around the hit. A flat quad
    /// perpendicular to the beam intersects an oblique wall, clipping half the dot even with a small surface gap.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "SLaserProjector's near-orthographic texture projected onto the visible depth within MaxTraceDistance, centred on the drawn beam hit")]
    internal static void PlaceDot(MeshInstance3D dot, FVector hit, FVector dir, float drawScale,
        float maxTraceDistance, bool projectOnBackfaces = true)
    {
        float width = UeSpace.LengthToGodot(MathF.Max(DotTexels * drawScale, 0.001f));
        float depth = UeSpace.LengthToGodot(MathF.Max(maxTraceDistance, 0.001f));
        Vector3 along = UeSpace.DirectionToGodot(dir).Normalized();
        if (along.IsZeroApprox()) along = Vector3.Forward;
        Vector3 side = MathF.Abs(along.Y) > 0.99f ? Vector3.Right : along.Cross(Vector3.Up).Normalized();
        Vector3 up = side.Cross(along).Normalized();
        Vector3 centre = UeSpace.ToGodot(hit);
        dot.GlobalTransform = new Transform3D(new Basis(side * width, up * width, -along * depth), centre);
        var material = (ShaderMaterial)dot.MaterialOverride;
        Vector3 origin = centre - along * (depth * 0.5f);
        material.SetShaderParameter("proj_origin", new Vector4(origin.X, origin.Y, origin.Z, depth));
        material.SetShaderParameter("proj_x", along);
        material.SetShaderParameter("proj_y", side);
        material.SetShaderParameter("proj_z", -up);
        material.SetShaderParameter("proj_shape", new Vector4(width * 0.5f, width * 0.5f, 0f, projectOnBackfaces ? 1f : 0f));
    }

    private const string DotShaderCode = "shader_type spatial;\n"
        + "render_mode unshaded, fog_disabled, blend_add, cull_disabled, depth_test_disabled, depth_draw_never;\n"
        + "#define PROJ_LASER\n#include \"res://Shaders/svm_projector.gdshaderinc\"\n";

    internal static MeshInstance3D CreateDot(Texture2D texture)
    {
        Render.Materials.MaterialCompiler.EnsureGlobals();
        var material = new ShaderMaterial { Shader = ResponsiveSmokeShaders.Shared(DotShaderCode, "spy laser projector") };
        material.SetShaderParameter("proj_tex", texture);
        return new MeshInstance3D
        {
            Name = "SpyLaserDot",
            Mesh = new BoxMesh { Size = Vector3.One },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = material,
            Visible = false,
        };
    }

    private (MeshInstance3D Beam, MeshInstance3D Dot) NodesFor(SWeaponAttaque pistol)
    {
        if (_nodes.TryGetValue(pistol, out var nodes))
        {
            return nodes;
        }

        nodes = CreateNodes(this);
        nodes.Beam.AddToGroup(GroupBeam);
        nodes.Dot.AddToGroup(GroupDot);
        _nodes[pistol] = nodes;
        return nodes;
    }

    /// <summary>Prepare both the spy beam and the depth-projecting dot before their first activation.</summary>
    public void Preload(Node3D samples)
    {
        var (beam, dot) = CreateNodes(samples);
        beam.Scale = new Vector3(100f, 1f, 1f);
        beam.Visible = true;
        dot.Visible = true;
        PlaceDot(dot, default, new FVector(0f, -1f, 0f), 1f, 100f);
    }

    private (MeshInstance3D Beam, MeshInstance3D Dot) CreateNodes(Node3D parent)
    {
        var beam = new MeshInstance3D
        {
            Name = "SpyLaserBeam",
            Mesh = BeamMeshOrStandIn(out bool packed),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        if (packed)
        {
            if (!_beamLookTried)
            {
                _beamLookTried = true;
                _beamLook = WeaponBeamMaterial(Map);
                _finLook = WeaponBeamMaterial(Map, FinTexture);
            }

            ApplyBeamLook(beam, _beamLook, _finLook, showFin: true);
        }
        else
        {
            beam.MaterialOverride = new EffectMaterial
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0.08f, 0.05f, 0.7f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                NextPass = ThermalBeamPass(null),
            };
        }

        MeshInstance3D dot = CreateDot(DotTextureOrStandIn());
        parent.AddChild(beam);
        parent.AddChild(dot);
        return (beam, dot);
    }

    private Mesh BeamMeshOrStandIn(out bool packed)
    {
        if (!_beamTried)
        {
            _beamTried = true;
            if (Map is not null && PackedBeamMesh(Map) is { } mesh)
            {
                _beamMesh = mesh;
                _packedBeam = true;
            }
            else
            {
                // A 1 uu long bar along +X (UE), 2.06 uu thick like the class mesh, so DrawScale3D stretches it the same way.
                float w = UeSpace.LengthToGodot(2.06f);
                var box = new BoxMesh { Size = new Vector3(UeSpace.LengthToGodot(1f), w, w) };
                var array = new ArrayMesh();
                Godot.Collections.Array arrays = box.GetMeshArrays();
                var verts = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
                for (int i = 0; i < verts.Length; i++)
                {
                    verts[i].X += UeSpace.LengthToGodot(0.5f);
                }

                arrays[(int)Mesh.ArrayType.Vertex] = verts;
                array.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                _beamMesh = array;
            }
        }

        packed = _packedBeam;
        return _beamMesh!;
    }

    private bool _packedBeam;

    // The packed beam mesh and beam textures of a map, made once for all its laser views (merc, spy, detectors) and
    // kept only while the map lives. Neither is changed after it is made: the views set their own materials.
    private sealed class BeamAssets
    {
        public bool MeshTried { get; set; }

        public Mesh? Mesh { get; set; }

        public Dictionary<string, ImageTexture?> Textures { get; } = new(StringComparer.Ordinal);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RenderMap, BeamAssets> s_beamAssets = new();

    /// <summary>The render pack's SLaserBeam mesh (unlit), read once per map; null when the pack lacks it.</summary>
    internal static Mesh? PackedBeamMesh(RenderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        BeamAssets assets = s_beamAssets.GetOrCreateValue(map);
        if (!assets.MeshTried)
        {
            assets.MeshTried = true;
            assets.Mesh = GameplayObjects.MeshFor(map, BeamMesh, unlit: true) is { } mesh ? WithBeamColours(mesh) : null;
        }

        return assets.Mesh;
    }

    /// <summary>The beam mesh's vertex colour along its axis (115 of 255); its outer edges are black.</summary>
    [Provenance(ProvenanceKind.Decoded, "SGameplayObjects.LaserBeam (UseVertexColor): colour 115,115,115,115 on the four axis vertices of section 0, 3,3,0,0 on its eight edge vertices, 255 on the fin quad of section 1")]
    internal const float BeamAxisColour = 115f / 255f;

    /// <summary>
    /// The packed mesh with the original's vertex colours, which the render pack's file does not carry: each crossed
    /// quad fades from its axis to nothing at its edges, so the beam reads as one soft line instead of two red planes.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SGameplayObjects.LaserBeam vertex colours; the edge colour's 3 of 255 in two channels is drawn as 0")]
    private static ArrayMesh WithBeamColours(ArrayMesh source)
    {
        var mesh = new ArrayMesh { ResourceName = source.ResourceName };
        for (int s = 0; s < source.GetSurfaceCount(); s++)
        {
            Godot.Collections.Array arrays = source.SurfaceGetArrays(s);
            Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Material? material = source.SurfaceGetMaterial(s);
            string name = material?.ResourceName ?? string.Empty;
            bool fin = name.Length > 0 ? name.Contains("fin", StringComparison.OrdinalIgnoreCase) : s == 1;
            var colours = new Color[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                FVector at = UeSpace.ToUe(vertices[i]);
                float v = fin ? 1f : MathF.Abs(at.Y) + MathF.Abs(at.Z) < 0.01f ? BeamAxisColour : 0f;
                colours[i] = new Color(v, v, v, v);
            }

            arrays[(int)Mesh.ArrayType.Color] = colours;
            mesh.AddSurfaceFromArrays(source.SurfaceGetPrimitiveType(s), arrays);
            mesh.SurfaceSetMaterial(s, material);
            mesh.SurfaceSetName(s, source.SurfaceGetName(s));
        }

        return mesh;
    }

    // The PNG at path as a texture (as it is stored: no mipmaps), read once per map; null when missing or empty.
    private static ImageTexture? BeamTextureOf(RenderMap map, string path)
    {
        BeamAssets assets = s_beamAssets.GetOrCreateValue(map);
        if (!assets.Textures.TryGetValue(path, out ImageTexture? tex))
        {
            using Image? image = File.Exists(path) ? Image.LoadFromFile(path) : null;
            tex = image is null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
            assets.Textures[path] = tex;
        }

        return tex;
    }

    /// <summary>
    /// The beam's look: the SLaserBeam mesh's LaserBeamShader adds its texture (a dark red band with a bright core) to
    /// the picture. The render pack keeps the texture but not the shader's blending (its material is opaque), which drew
    /// a solid red bar; the laser views draw the packed mesh with this material instead.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/EFFECTS_COVERAGE.md: SGameplayObjects_LaserBeam with the additive LaserBeamShader")]
    internal static EffectMaterial? AdditiveBeamMaterial(RenderMap? map, string texture = BeamTexture)
    {
        if (map?.Pack?.GameplayGlb is not { } glb)
        {
            return null;
        }

        string path = Path.Combine(Path.GetDirectoryName(glb)!, "textures", texture + ".png");
        if (BeamTextureOf(map, path) is not { } tex)
        {
            return null;
        }

        return new EffectMaterial
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoTexture = tex,
            NextPass = ThermalBeamPass(tex),
        };
    }

    /// <summary>
    /// <see cref="AdditiveBeamMaterial"/> for the SLaserBeam class mesh (<see cref="PackedBeamMesh"/>): the texture's
    /// display values times the mesh's vertex colours, the same sum on both renderers.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SGameplayObjectsTex.LaserBeamShader and lasebeam2fin: Diffuse only, OutputBlending 5 (OB_Brighten), LaserBeamShader TwoSided; the mesh has UseVertexColor")]
    [Provenance(ProvenanceKind.Estimated, "OB_Brighten drawn as the source's colour times its alpha, added: with the vertex colour in both, the axis adds 123 x 0.45 x 0.45 = 25 levels, the level of the original's capture (local/lasers/orig/merc-00-stand_level.png, about 20); colour alone gave 55")]
    internal static EffectMaterial? WeaponBeamMaterial(RenderMap? map, string texture = BeamTexture)
    {
        if (AdditiveBeamMaterial(map, texture) is not { } material) return null;
        material.DisplayTexture = true;
        material.VertexColorUseAsAlbedo = true;
        if (material.NextPass is ShaderMaterial vision) vision.SetShaderParameter("vertex_fade", true);
        return material;
    }

    /// <summary>The original pan_laserbeam moves the beam's diffuse along U; the muzzle fin is stationary.</summary>
    [Provenance(ProvenanceKind.Decoded, "SGameplayObjectsTex.LaserBeamShader Diffuse=pan_laserbeam; TexPanner PanRate=-0.05, PanDirection=(0,0,0)")]
    internal const float BeamPanRate = -0.05f;

    internal static void AnimateBeam(EffectMaterial? material, float time)
    {
        if (material is null) return;
        float phase = time * BeamPanRate;
        var offset = new Vector2(phase - MathF.Floor(phase), 0f);
        material.Uv1Offset = new Vector3(offset.X, offset.Y, 0f);
        // The thermal pass must sample the same moving core as the additive colour pass.
        if (material.NextPass is ShaderMaterial thermal)
            thermal.SetShaderParameter("uv_offset", offset);
    }

    /// <summary>
    /// The beam in thermal vision: an SLaserBeam (the spy pistol's beam, the merc's laser sight) is HeatIntensity 1 by
    /// class default, so it draws hot like the map's detector beams. The additive red look adds only to the red channel,
    /// which the thermal post pass does not read, so without this pass the beam vanished in thermal. The heat follows the
    /// lit texels of <paramref name="mask"/> (the beam's core); null heats the whole mesh (the stand-in bar).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SBase.SLaserBeam defaults HeatIntensity 1.0")]
    internal static ShaderMaterial ThermalBeamPass(Texture2D? mask)
    {
        var pass = new ShaderMaterial { Shader = ResponsiveSmokeShaders.Load(Lasers.ShaderPath) };
        pass.SetShaderParameter("heat", Lasers.Vision.X);
        pass.SetShaderParameter("thermal_only", true);
        if (mask is not null)
        {
            pass.SetShaderParameter("mask", mask);
        }

        return pass;
    }

    /// <summary>The beam's body: two crossed quads along the mesh's X, mapped to the texture's bright core.</summary>
    internal const string BeamTexture = "SGameplayObjectsTex_LaserBeamShader";

    /// <summary>The mesh's second section: one quad across the start of the beam, a soft red glow (lasebeam2fin).</summary>
    internal const string FinTexture = "SGameplayObjectsTex_lasebeam2fin";

    /// <summary>
    /// The beam mesh's two sections with their own additive textures: the body and the glow quad at its start. One
    /// material over the whole mesh put the body's streak on the start quad, which drew as a short dashed red line
    /// across the beam's start (the merc's muzzle). <paramref name="showFin"/> false hides the start quad.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SGameplayObjects.LaserBeam sections: material 0 LaserBeamShader (crossed quads, V 0.425 to 0.575), material 1 lasebeam2fin (one quad at X 0)")]
    internal static void ApplyBeamLook(MeshInstance3D beam, EffectMaterial? body, EffectMaterial? fin, bool showFin)
    {
        ArgumentNullException.ThrowIfNull(beam);
        if (beam.Mesh is not { } mesh || mesh.GetSurfaceCount() < 2)
        {
            beam.MaterialOverride = body;
            return;
        }

        beam.MaterialOverride = null;
        for (int i = 0; i < mesh.GetSurfaceCount(); i++)
        {
            string name = mesh.SurfaceGetMaterial(i)?.ResourceName ?? string.Empty;
            bool isFin = name.Length > 0 ? name.Contains("fin", StringComparison.OrdinalIgnoreCase) : i == 1;
            beam.SetSurfaceOverrideMaterial(i, isFin ? (showFin ? fin : HiddenSection) : body);
        }
    }

    private static readonly EffectMaterial HiddenSection = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = new Color(0f, 0f, 0f, 0f),
    };

    private EffectMaterial? _beamLook;
    private EffectMaterial? _finLook;
    private bool _beamLookTried;

    private Texture2D DotTextureOrStandIn()
    {
        if (!_dotTried)
        {
            _dotTried = true;
            string? path = SharedRoot is null ? null : Path.Combine(SharedRoot, "fx", "textures", DotTexture);
            if (path is not null && File.Exists(path) && Image.LoadFromFile(path) is { } image && !image.IsEmpty())
            {
                _dotTexture = ImageTexture.CreateFromImage(image);
            }
            else
            {
                // A soft orange-red disc with RET_Laser's peak colour (255, 190, 90) until the texture is converted.
                var img = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        float r = MathF.Sqrt(((x - 15.5f) * (x - 15.5f)) + ((y - 15.5f) * (y - 15.5f))) / 16f;
                        float a = Math.Clamp(1f - r, 0f, 1f);
                        img.SetPixel(x, y, new Color(a, a * 190f / 255f, a * 90f / 255f, a));
                    }
                }

                _dotTexture = ImageTexture.CreateFromImage(img);
            }
        }

        return _dotTexture!;
    }
}
