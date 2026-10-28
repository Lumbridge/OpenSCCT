using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using OpenScctGame.Views;

namespace OpenScctGame.Render;

/// <summary>
/// The merc laser sight on screen (SGadTorcheLaser, spec/equipment/merc-laser.md rules 8 and 9): one SLaserBeam per
/// laser that is on while its merc has the rifle drawn (or looks through a network camera), drawn in every vision
/// mode (the class does not hide in normal vision). The beam is the SLaserBeam class mesh
/// (SGameplayObjects.Objects.LaserBeam, 1 uu along X) stretched from its start to the end (DrawScale3D.X = length + 1).
/// The local merc's beam runs from his first-person rifle's FX bone to the point the simulation's placement found
/// (SGadTorcheLaser.HitLocation); he sees no corona of his own (decoded: bCorona cleared on the owner's machine).
/// Other mercs' beams follow NativeReplaceLaserDistant from the FX bone of the rifle drawn in their hands, with the SCoronaLight glint (sfx.Flare.ATT_flare, at least
/// 15 pixels) at the beam's start.
/// </summary>
[PortOf("nat:SBase.SGadTorcheLaser.NativeReplaceLaserLocal", Spec = "spec/equipment/merc-laser.md#rules")]
public sealed partial class MercLaserView : Node3D
{
    [Provenance(ProvenanceKind.Decoded, "SLaserBeam defaults StaticMesh SGameplayObjects.Objects.LaserBeam")]
    public const string BeamMesh = "SGameplayObjects.Objects.LaserBeam";

    private static readonly Name BoneFx = new("FX");
    private static readonly Name BoneRightHand = new("B R Hand");

    private readonly Dictionary<SGadTorcheLaser, (MeshInstance3D Beam, MeshInstance3D Dot)> _beams = new();
    private readonly List<SGadTorcheLaser> _gone = new();
    private Mesh? _mesh;
    private bool _meshTried;
    private StandardMaterial3D? _dotMaterial;
    private StandardMaterial3D? _beamLook;
    private StandardMaterial3D? _finLook;
    private bool _beamLookTried;

    public RenderMap? Map { get; set; }

    public PawnViews? Pawns { get; set; }

    public FirstPersonView? FirstPerson { get; set; }

    /// <summary>Beams drawn last frame.</summary>
    public int BeamsDrawn { get; private set; }

    /// <summary>The local merc's beam was drawn last frame, from <see cref="LocalStart"/> to <see cref="LocalEnd"/> (UE).</summary>
    public bool LocalVisible { get; private set; }

    public FVector LocalStart { get; private set; }

    public FVector LocalEnd { get; private set; }

    /// <summary>The beams drawn last frame: whose, from where to where (UE), and whether it is the local merc's.</summary>
    public IReadOnlyList<(SBasePawn Pawn, FVector Start, FVector End, bool Local)> Drawn => _drawn;

    private readonly List<(SBasePawn Pawn, FVector Start, FVector End, bool Local)> _drawn = new();

    /// <summary>Where the local merc's drawn beam mesh begins (UE): the first-person rifle's muzzle.</summary>
    public FVector DrawnFrom { get; private set; }

    private bool _localFromFx;

    /// <summary>Frames the local merc's beam was drawn.</summary>
    public int LocalFrames { get; private set; }

    /// <summary>True when the beam mesh came from the render pack (else a thin stand-in).</summary>
    public bool PackMesh { get; private set; }

    public void Draw(Level? level, SBasePawn? local)
    {
        BeamsDrawn = 0;
        LocalVisible = false;
        _drawn.Clear();
        foreach (SGadTorcheLaser laser in _beams.Keys)
        {
            if (laser.bDeleteMe || level is null)
            {
                _gone.Add(laser);
            }
        }

        foreach (SGadTorcheLaser laser in _gone)
        {
            _beams[laser].Beam.QueueFree();
            _beams[laser].Dot.QueueFree();
            _beams.Remove(laser);
        }

        _gone.Clear();
        if (level is null)
        {
            return;
        }

        foreach (SGadTorcheLaser laser in level.AllActors<SGadTorcheLaser>())
        {
            bool show = !laser.bDeleteMe && laser.bInUse && laser.Instigator is SBasePawn { bDeleteMe: false, Health: > 0 } owner &&
                (owner.WeaponLoaded == 2 || (owner.bUsingCamNetwork && owner.UsedObject is SCamNetwork));
            if (!show)
            {
                if (_beams.TryGetValue(laser, out var hidden))
                {
                    hidden.Beam.Visible = false;
                    hidden.Dot.Visible = false;
                }

                continue;
            }

            var pawn = (SBasePawn)laser.Instigator!;
            bool isLocal = ReferenceEquals(pawn, local);
            (FVector start, FVector end, FVector corona) = isLocal ? LocalBeam(laser, pawn) : DistantBeam(pawn);
            (MeshInstance3D beam, MeshInstance3D dot) = NodesFor(laser);
            FVector d = end - start;
            float length = d.Size();
            FRotator rotation = length > 0f ? d.Rotation() : default;
            // The original draws the first-person rifle over the world (the weapon overlay), so the 50 uu of the local
            // beam behind the muzzle are hidden by the gun. The first-person model here is drawn in the world, where that
            // stretch passed in front of it as a thick bar; the drawn mesh starts at the muzzle instead.
            float behindMuzzle = isLocal && _localFromFx ? MathF.Min(K.LaserFireStartPullBack, length) : 0f;
            FVector from = length > 0f ? start + (d * (behindMuzzle / length)) : start;
            DrawnFrom = isLocal ? from : DrawnFrom;
            beam.Transform = UeSpace.TransformToGodot(from, rotation, new FVector(MathF.Max(length - behindMuzzle, 0.01f) + 1f, 1f, 1f), default);
            beam.Visible = true;
            if (PackMesh && _beamLook is not null)
            {
                // The original's weapon overlay hides the start of the local merc's beam, glow quad included; the drawn
                // mesh here starts at the muzzle, where that quad sat as a red flare.
                OpenScctGame.Fx.SpyLaserView.ApplyBeamLook(beam, _beamLook, _finLook, showFin: !(isLocal && _localFromFx));
            }

            dot.Visible = !isLocal;
            if (!isLocal)
            {
                dot.GlobalPosition = UeSpace.ToGodot(corona);
            }

            BeamsDrawn++;
            _drawn.Add((pawn, start, end, isLocal));
            if (isLocal)
            {
                LocalVisible = true;
                LocalStart = start;
                LocalEnd = end;
                LocalFrames++;
            }
        }
    }

    /// <summary>
    /// The local merc's beam: from the drawn rifle's FX bone pulled back 50 uu to the simulation's end point (the
    /// original's starts 15.4 forward, 12.0 right and 12.7 under his eye, merc-laser.md rule 9a; the first-person
    /// rifle's FX bone puts it within 1 uu of that).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "The drawn mesh starts at the muzzle: the original's weapon overlay hides the 50 uu behind it")]
    private (FVector Start, FVector End, FVector Corona) LocalBeam(SGadTorcheLaser laser, SBasePawn pawn)
    {
        FVector end = laser.HitLocation;
        FVector start = laser.BeamStart;
        _localFromFx = false;
        if (!(pawn.bUsingCamNetwork && pawn.UsedObject is SCamNetwork) && FirstPerson is { Shown: true, Model: { } model } &&
            model.Skeleton.FindBone(BoneFx.ToString()) is var fx and >= 0)
        {
            FVector muzzle = UeSpace.ToUe((model.Skeleton.GlobalTransform * model.Skeleton.GetBoneGlobalPose(fx)).Origin);
            FVector dir = end - muzzle;
            float size = dir.Size();
            start = size > 0f ? muzzle - (dir * (K.LaserFireStartPullBack / size)) : muzzle;
            _localFromFx = size > K.LaserFireStartPullBack;
        }

        return (start, end, start);
    }

    /// <summary>
    /// Another merc's beam (NativeReplaceLaserDistant, decoded game 0x10a99430): it starts at his third-person gun's FX
    /// bone and points along his packed desired aim (signed pitch and yaw words of iDesiredYawPitch; the FX bone's own
    /// direction while IK_Type is IKT_NONE) toward his location raised by 55.5 uu (48 crouched) plus 15000 uu of that
    /// direction; a line to there stops it at the first hit, else it ends 15000 uu from the start. The corona sits at
    /// the start, turned along the beam.
    /// </summary>
    [PortOf("nat:SBase.SGadTorcheLaser.NativeReplaceLaserDistant", Spec = "spec/equipment/merc-laser.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "NativeReplaceLaserDistant game 0x10a99430: end raise 55.5 / 48.0, corona at the beam start")]
    [Provenance(ProvenanceKind.Decoded, "NativeReplaceLaserDistant game 0x10a99430: the start is the third-person gun's FX bone (the drawn M160 on B R Hand)")]
    [Provenance(ProvenanceKind.Estimated, "Only while the drawn rifle is missing: the right hand bone 24 uu along the aim stands in")]
    private (FVector Start, FVector End, FVector Corona) DistantBeam(SBasePawn pawn)
    {
        int packed = pawn.iDesiredYawPitch;
        int pitch = (packed >> 16) & 0xFFFF;
        int yaw = packed & 0xFFFF;
        FRotator aim = new(pitch > 0x8000 ? pitch - 0x10000 : pitch, yaw > 0x8000 ? yaw - 0x10000 : yaw, pawn.Rotation.Roll);
        FVector dir = aim.Vector();
        FVector start = pawn.Location + new FVector(0f, 0f, pawn.EyeHeight * 0.6f) + (dir * 40f);
        if (Pawns?.ViewOf(pawn)?.Attachments is { } att && att.TryGetWeaponBone(BoneFx, out Transform3D fx))
        {
            // The FX bone of the rifle drawn in his hands (SWeaponDefenseDummy, SWeaponFirst.M160, on B R Hand).
            start = UeSpace.ToUe(fx.Origin);
        }
        else if (Pawns is not null && Pawns.TryGetBone(pawn, BoneRightHand, out Transform3D hand))
        {
            start = UeSpace.ToUe(hand.Origin) + (dir * 24f);
        }

        float raise = pawn.bIsCrouched ? DistantRaiseCrouched : K.LaserEndRaiseStanding;
        FVector far = pawn.Location + (dir * K.TraceLength) + new FVector(0f, 0f, raise);
        FVector end = GadgetWorld.Line(pawn, far, start, ETraceFlags.TRACE_World | ETraceFlags.TRACE_Pawns, out FCheckResult hit)
            ? hit.Location
            : start + (dir * K.TraceLength);
        return (start, end, start);
    }

    [Provenance(ProvenanceKind.Decoded, "NativeReplaceLaserDistant game 0x10a99430: 48.0 added when bIsCrouched")]
    private const float DistantRaiseCrouched = 48f;

    private (MeshInstance3D Beam, MeshInstance3D Dot) NodesFor(SGadTorcheLaser laser)
    {
        if (_beams.TryGetValue(laser, out var nodes))
        {
            return nodes;
        }

        var beam = new MeshInstance3D
        {
            Name = "MercLaser",
            Mesh = BeamMeshOrStandIn(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        if (PackMesh)
        {
            if (!_beamLookTried)
            {
                _beamLookTried = true;
                _beamLook = OpenScctGame.Fx.SpyLaserView.AdditiveBeamMaterial(Map);
                _finLook = OpenScctGame.Fx.SpyLaserView.AdditiveBeamMaterial(Map, OpenScctGame.Fx.SpyLaserView.FinTexture);
            }

            OpenScctGame.Fx.SpyLaserView.ApplyBeamLook(beam, _beamLook, _finLook, showFin: true);
        }
        else
        {
            beam.MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0.1f, 0.1f, 0.6f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            };
        }

        _dotMaterial ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.15f, 0.1f, 1f),
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = false,
            FixedSize = true,
        };
        var dot = new MeshInstance3D
        {
            Name = "MercLaserCorona",
            Mesh = new QuadMesh { Size = Vector2.One * 0.015f },
            MaterialOverride = _dotMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(beam);
        AddChild(dot);
        _beams[laser] = (beam, dot);
        return (beam, dot);
    }

    private Mesh BeamMeshOrStandIn()
    {
        if (!_meshTried)
        {
            _meshTried = true;
            if (Map is not null && GameplayObjects.MeshFor(Map, BeamMesh, unlit: true) is { } packed)
            {
                _mesh = packed;
                PackMesh = true;
            }
        }

        if (_mesh is not null)
        {
            return _mesh;
        }

        // Stand-in until the pack carries the beam mesh: a 1 uu long, 0.4 uu thick red bar along +X (UE), so the
        // stretch is the same as the class mesh's.
        float w = UeSpace.LengthToGodot(0.4f);
        var box = new BoxMesh { Size = new Vector3(UeSpace.LengthToGodot(1f), w, w) };
        var mesh = new ArrayMesh();
        Godot.Collections.Array arrays = box.GetMeshArrays();
        var verts = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i].X += UeSpace.LengthToGodot(0.5f);
        }

        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _mesh = mesh;
        return _mesh;
    }
}
