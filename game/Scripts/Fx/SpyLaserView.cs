using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
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
/// hit; the simulation's own placement (the measured pistol frame) is used only when the pawn is not drawn. The dot is RET_Laser (SWeaponTex.Reticules, 32 x 32, additive) projected
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

    /// <summary>The shared content root (the converted effect textures in fx/textures).</summary>
    public string? SharedRoot { get; set; }

    public int BeamsDrawn { get; private set; }

    public int DotsDrawn { get; private set; }

    /// <summary>The last beam drawn (the local spy's own), from <see cref="BeamStart"/> to <see cref="BeamEnd"/> (UE).</summary>
    public SBasePawn? BeamPawn { get; private set; }

    public FVector BeamStart { get; private set; }

    public FVector BeamEnd { get; private set; }

    /// <summary>The last dot drawn: where it sits (UE, just off the surface) and whose pistol it is.</summary>
    public FVector DotAt { get; private set; }

    public void Draw(Level? level)
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
            if (pistol.LaserBeam is { bDeleteMe: false } b)
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
                PlaceDot(dot, hit, dir, p.DrawScale);
                DotsDrawn++;
                DotAt = UeSpace.ToUe(dot.GlobalPosition);
            }
        }
    }

    /// <summary>
    /// The laser's line as this machine draws it (rule 14.4, run per machine from the drawn bones): from the drawn hand
    /// bone (B R Hand, B L Hand in the other hand) at 30 X - 4 Y + 8 Z of its frame (- 8 Z in the other hand), a few uu
    /// behind the drawn pistol's FX bone, along the hand's X axis for ShootingRange, to the first hit bullets do not
    /// pass. Without a drawn hand, the simulation's line (from the measured pistol frame the drawn hand follows within
    /// a few uu in a still pose).
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
            (FVector hit, _) = pistol.LaserHit(pawn, start, start + (dir * pistol.ShootingRange));
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

    private static readonly Name BoneRightHand = new("B R Hand");
    private static readonly Name BoneLeftHand = new("B L Hand");

    /// <summary>
    /// The dot: RET_Laser added on the surface the line hit, facing back along the line, 32 uu times the projector's
    /// DrawScale wide (the projector sits MaxTraceDistance / 2 before the hit, projecting along the line).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "A quad at the hit facing back along the line stands in for the projected texture (the same on a surface square to the line)")]
    private static void PlaceDot(MeshInstance3D dot, FVector hit, FVector dir, float drawScale)
    {
        FVector at = hit - (dir * DotSurfaceGap);
        float width = UeSpace.LengthToGodot(DotTexels * drawScale);
        Vector3 along = UeSpace.DirectionToGodot(dir).Normalized();
        Vector3 side = MathF.Abs(along.Y) > 0.99f ? Vector3.Right : along.Cross(Vector3.Up).Normalized();
        Vector3 up = side.Cross(along).Normalized();
        // A quad faces its local +Z: +Z points back along the line, toward the shooter.
        dot.GlobalTransform = new Transform3D(new Basis(side * width, up * width, -along), UeSpace.ToGodot(at));
    }

    /// <summary>How far in front of the surface the dot is drawn, so the wall does not hide it.</summary>
    private const float DotSurfaceGap = 0.5f;

    private (MeshInstance3D Beam, MeshInstance3D Dot) NodesFor(SWeaponAttaque pistol)
    {
        if (_nodes.TryGetValue(pistol, out var nodes))
        {
            return nodes;
        }

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
                _beamLook = AdditiveBeamMaterial(Map);
                _finLook = AdditiveBeamMaterial(Map, FinTexture);
            }

            ApplyBeamLook(beam, _beamLook, _finLook, showFin: true);
        }
        else
        {
            beam.MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0.08f, 0.05f, 0.7f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            };
        }

        var dot = new MeshInstance3D
        {
            Name = "SpyLaserDot",
            Mesh = new QuadMesh { Size = Vector2.One },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoTexture = DotTextureOrStandIn(),
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            Visible = false,
        };
        AddChild(beam);
        AddChild(dot);
        beam.AddToGroup(GroupBeam);
        dot.AddToGroup(GroupDot);
        _nodes[pistol] = (beam, dot);
        return (beam, dot);
    }

    private Mesh BeamMeshOrStandIn(out bool packed)
    {
        if (!_beamTried)
        {
            _beamTried = true;
            if (Map is not null && GameplayObjects.MeshFor(Map, BeamMesh, unlit: true) is { } mesh)
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

    /// <summary>
    /// The beam's look: the SLaserBeam mesh's LaserBeamShader adds its texture (a dark red band with a bright core) to
    /// the picture. The render pack keeps the texture but not the shader's blending (its material is opaque), which drew
    /// a solid red bar; the laser views draw the packed mesh with this material instead.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/EFFECTS_COVERAGE.md: SGameplayObjects_LaserBeam with the additive LaserBeamShader")]
    internal static StandardMaterial3D? AdditiveBeamMaterial(RenderMap? map, string texture = BeamTexture)
    {
        if (map?.Pack?.GameplayGlb is not { } glb)
        {
            return null;
        }

        string path = Path.Combine(Path.GetDirectoryName(glb)!, "textures", texture + ".png");
        if (!File.Exists(path) || Image.LoadFromFile(path) is not { } image || image.IsEmpty())
        {
            return null;
        }

        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoTexture = ImageTexture.CreateFromImage(image),
        };
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
    internal static void ApplyBeamLook(MeshInstance3D beam, StandardMaterial3D? body, StandardMaterial3D? fin, bool showFin)
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

    private static readonly StandardMaterial3D HiddenSection = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = new Color(0f, 0f, 0f, 0f),
    };

    private StandardMaterial3D? _beamLook;
    private StandardMaterial3D? _finLook;
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
