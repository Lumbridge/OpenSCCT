using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Server;

/// <summary>
/// Collision pills follow the installed spy roll animations on drawing and dedicated machines alike.
/// Other actions, or installations without the character clips, retain the reference-pose fallback.
/// </summary>
public sealed class RollPillPose(RollPoseContent content) : IPillPose
{
    private readonly RollPoseContent _content = content ?? throw new ArgumentNullException(nameof(content));

    public static IPillPose FromContent(string? characters) =>
        characters is not null && RollPoseContent.Load(Path.Combine(characters, "SPerso", "ATT_01.glb")) is { } content
            ? new RollPillPose(content) : EstimatedPillPose.Instance;

    public void GetPills(SBasePawn Pawn, List<PillShape> Shapes)
    {
        PawnAnimChannel channel = Pawn.PawnAnim[SBasePawn.DBLJUMPCHANNEL];
        if (!Pawn.bIsRolling || Pawn.SPawnType != PawnType.SPAWNOFFENSE || channel.Clip is not { Fps: > 0 } ||
            !RollPoseContent.IsRollClip(channel.Sequence.ToString()) || channel.bMirror || channel.bGlobalPose || channel.Frame < 0f)
        {
            EstimatedPillPose.Instance.GetPills(Pawn, Shapes);
            return;
        }

        var bones = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
        if (!TrySample(Pawn, channel, bones))
        {
            EstimatedPillPose.Instance.GetPills(Pawn, Shapes);
            return;
        }

        // Keep the entire result on the fallback if a partial/corrupt skeleton lacks a collision bone.
        foreach (CollisionPoint point in Points)
        {
            if (!bones.ContainsKey(point.Bone))
            {
                EstimatedPillPose.Instance.GetPills(Pawn, Shapes);
                return;
            }
        }

        _content.RotOrigin.GetAxes(out FVector mx, out FVector my, out FVector mz);
        new FRotator(0, Pawn.Rotation.Yaw, 0).GetAxes(out FVector px, out FVector py, out FVector pz);
        Span<FVector> points = stackalloc FVector[Points.Length];
        for (int i = 0; i < Points.Length; i++)
        {
            CollisionPoint point = Points[i];
            FVector offset = point.Offset;
            Vector3 local = Vector3.Transform(new Vector3(offset.X, offset.Z, offset.Y) / 100f, bones[point.Bone]);
            FVector mesh = mx * (local.X * 100f) + my * (local.Z * 100f) + mz * (local.Y * 100f);
            mesh = new FVector(mesh.X * _content.MeshScale.X, mesh.Y * _content.MeshScale.Y, mesh.Z * _content.MeshScale.Z);
            // Walking rolls keep their authored root displacement; only traversal root-motion clips lock it.
            points[i] = Pawn.Location + Pawn.PrePivot + px * mesh.X + py * mesh.Y + pz * mesh.Z;
        }

        for (int i = 0; i < Endpoints.Length; i++)
        {
            (int a, int b) = Endpoints[i];
            PillTable.Row row = PillTable.Rows[i];
            Shapes.Add(new PillShape(row.Pill, points[a], points[b], row.Radius, PillTable.Priority, row.IsSphere));
        }
    }

    private bool TrySample(SBasePawn pawn, PawnAnimChannel roll, Dictionary<string, Matrix4x4> bones)
    {
        if (roll.BlendAlpha >= 1f)
            return _content.TrySampleSeconds(roll.Sequence.ToString(), roll.ClipFrame * roll.Frames / roll.Clip!.Fps, bones);

        // During the 0.1 s entrance the run still contributes: immediately using the airborne roll pose would
        // let the collision shape leave the ground before the visible body. Blend the same local bone channels.
        var layers = new RollPoseContent.Layer[SBasePawn.DBLJUMPCHANNEL + 1];
        int count = 0;
        for (int i = 0; i <= SBasePawn.DBLJUMPCHANNEL; i++)
        {
            PawnAnimChannel channel = pawn.PawnAnim[i];
            float alpha = i == 0 ? 1f : channel.BlendAlpha;
            if (alpha <= 0f || channel.Sequence.IsNone || channel.Clip is not { Fps: > 0 } clip) continue;
            // History-dependent tweens and mirrored traversal overlays are outside this roll sampler.
            if (channel.Frame < 0f || channel.bMirror || channel.bGlobalPose) return false;
            layers[count++] = new RollPoseContent.Layer(channel.Sequence.ToString(), channel.ClipFrame * channel.Frames / clip.Fps,
                alpha, i == 0 || channel.StartBone.IsNone ? null : channel.StartBone.ToString(), channel.bLoop, clip.Fps);
        }
        return _content.TrySampleLayers(layers.AsSpan(0, count), bones);
    }

    private readonly record struct CollisionPoint(string Bone, FVector Offset);

    // Bone-space collision offsets, independent of animation tracks. spec/native/hit-zones.md; decoded by
    // re/ghidra/scripts/actions/pills.py (local/re-out/c/actions/pills-SPerso.json, ATT_01).
    [Provenance(ProvenanceKind.Decoded, "ATT_01's 26 collision points; spec/native/hit-zones.md")]
    private static readonly CollisionPoint[] Points =
    [
        new("B Head", new(4.172f, -2.376f, 0f)), new("B Head", new(11.519f, -0.428f, 0f)),
        new("B Spine2", new(15.82f, -2.171f, 6.21f)), new("B Pelvis", new(2.591f, -2.42f, 4.759f)),
        new("B L UpperArm", new(0.503f, 1.035f, 0.881f)), new("B L ForeArm", new(0.345f, 1.431f, -1.673f)),
        new("B L ForeArm", new(17.537f, 1.051f, -1.409f)), new("B L Hand", new(4.393f, -0.614f, -1.436f)),
        new("B L Thigh", new(3.65f, -1.73f, 0.08f)), new("B L Calf", new(-0.205f, -2.376f, -1.444f)),
        new("B L Calf", new(5.693f, -0.653f, -1.457f)), new("B L Foot", new(-4.25f, -1.127f, -0.066f)),
        new("B L Foot", new(5.318f, 0.159f, -0.153f)), new("B L Toe0", new(5.208f, -3.884f, -0.137f)),
        new("B Spine2", new(15.82f, -2.171f, -6.21f)), new("B Pelvis", new(2.591f, -2.42f, -4.759f)),
        new("B R UpperArm", new(0.503f, 1.035f, -0.881f)), new("B R ForeArm", new(0.345f, 1.431f, 1.673f)),
        new("B R ForeArm", new(17.537f, 1.051f, 1.409f)), new("B R Hand", new(4.393f, -0.614f, 1.436f)),
        new("B R Thigh", new(3.65f, -1.73f, -0.08f)), new("B R Calf", new(-0.205f, -2.376f, 1.444f)),
        new("B R Calf", new(5.693f, -0.653f, 1.457f)), new("B R Foot", new(-4.25f, -1.127f, 0.066f)),
        new("B R Foot", new(5.318f, 0.159f, 0.153f)), new("B R Toe0", new(5.208f, -3.884f, 0.137f)),
    ];

    private static readonly (int A, int B)[] Endpoints =
        [(0, 1), (2, 3), (4, 5), (5, 6), (7, 7), (8, 9), (10, 11), (12, 13),
         (14, 15), (16, 17), (17, 18), (19, 19), (20, 21), (22, 23), (24, 25)];
}
