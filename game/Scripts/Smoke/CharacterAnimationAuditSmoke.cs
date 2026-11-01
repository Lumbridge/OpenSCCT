using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Game.SBase;
using OpenScctGame.Render.Characters;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Checks every converted character clip, gameplay mirror variants, and layered traversal/overlay poses.</summary>
public sealed class CharacterAnimationAuditSmoke : SmokeTest
{
    public const string TestName = "character-animation-audit";
    public override string Name => TestName;

    private static readonly string[] SpyMirrored =
    [
        "LedgStAlTiL", "LedgStAlToL", "PipVStAlNtL", "PipVStAlTrRL", "PipVStAlTrLL",
        "PipVStLgEuR", "PipVStLgBuR", "LaddStAlNtL", "LaddStAlTuR", "WljpStAlUwL", "WljpStAlUmL",
    ];

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            string spy = CheckCharacter("SPerso.ATT_01", "PRO.anim.json", new SPawnAttaque_1Mesh().BoneMirrors,
                SpyMirrored, ["ATT_02_HEAD", "HOLSTER"], "WaitStAlFd0", pipe: true);
            string merc = CheckCharacter("SPerso.DEF_01", "Def.anim.json", new SPAWNDEFENSE().BoneMirrors,
                ["LaddStAlNtL", "LaddStAlTuR"], ["Laser", "DEF_01_Casque", "HOLSTER"], "WaitStSpFd2", pipe: false);
            return SmokeResult.Pass(spy + "; " + merc + "; finite unit-scale bones, fixed attachments, climb layers, overlay fades/repeated tweens");
        }
        catch (Exception error) { return SmokeResult.Fail(error.ToString()); }
    }

    private static string CheckCharacter(string asset, string metadata, IReadOnlyList<int> mirrors,
        string[] mirrored, string[] attachments, string idle, bool pipe)
    {
        using var audit = new Audit(asset, metadata, mirrors, attachments);
        foreach (ClipInfo clip in audit.Clips.Values)
            Sweep(audit, clip, mirror: false);
        foreach (string name in mirrored)
            Sweep(audit, audit.Clips[name], mirror: true);
        CheckClimb(audit, "LaddStAlNtL", "LaddStAlUpLR");
        if (pipe) CheckClimb(audit, "PipVStAlNtL", "PipVStAlUpRL");
        CheckOverlays(audit, idle);
        foreach (var finding in audit.TranslationOutliers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            GD.Print($"SVM_CHARACTER_ANIMATION_OUTLIER {asset} {finding.Key} maxLocalOffset={finding.Value:F6}m");
        return $"{asset}: {audit.Clips.Count} clips + {mirrored.Length} mirrored, {audit.Poses} poses, " +
            $"{audit.KnownOutlierClips.Count} known unused clips with translation outliers" +
            (audit.KnownOutlierClips.Count > 0 ? " (" + string.Join(", ", audit.KnownOutlierClips.OrderBy(name => name, StringComparer.Ordinal)) + "; details in log)" : "");
    }

    private static void Sweep(Audit audit, ClipInfo clip, bool mirror)
    {
        audit.Reset();
        // Original frame count/rate come from the converted package metadata; include every key frame and midpoint.
        for (int halfFrame = 0; halfFrame <= 2 * (clip.Frames - 1); halfFrame++)
        {
            ChannelPose pose = clip.Pose(halfFrame / (2f * clip.Frames), mirror);
            audit.Apply([pose], clip.Clip.Name + (mirror ? " mirrored" : ""), translations: true);
        }
    }

    private static void CheckClimb(Audit audit, string idleName, string climbName)
    {
        ClipInfo idle = audit.Clips[idleName], climb = audit.Clips[climbName];
        foreach (int channel in new[] { 5, 6 })
        {
            audit.Reset();
            var channels = new ChannelPose[7];
            // ClimbStep changes channel 0's mirror at quarter/three-quarter phases while channel 5 or 6
            // overlays the unmirrored climbing body. The static helpers must survive both channel layers.
            for (int step = 0; step <= 160; step++)
            {
                float phase = (step % 80) / 80f;
                channels[0] = idle.Pose(phase, mirror: !(phase > 0.25f && phase < 0.75f));
                channels[0].Loop = true;
                channels[channel] = climb.Pose(channel == 5 ? phase : 1f - phase);
                channels[channel].Loop = true;
                channels[channel].Alpha = Math.Min(1f, Math.Min(step, 160 - step) / 8f);
                audit.Apply(channels, idleName + "/" + climbName + " channel=" + channel);
            }
        }
    }

    private static void CheckOverlays(Audit audit, string idleName)
    {
        SkeletalModel reference = SkeletalModel.Load(audit.Path);
        try
        {
            var referenceApplier = new ChannelPoseApplier(reference.Skeleton);
            ChannelPose basis = audit.Clips[idleName].Pose(0.25f);
            referenceApplier.Apply([basis]);
            foreach ((string name, string bone, int channel) in new[]
            {
                ("GoglStAlDn2", "B Head", 17), ("AtakStAlFd2", "B Spine1", 14),
            })
            {
                audit.Reset();
                ClipInfo overlay = audit.Clips[name];
                var channels = new ChannelPose[18];
                channels[0] = basis;
                audit.Apply(channels, idleName + " before overlay");
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    channels[channel] = overlay.Pose(0f);
                    channels[channel].StartBone = audit.Model.Skeleton.FindBone(bone);
                    if (channels[channel].StartBone < 0) throw new InvalidOperationException("Missing overlay bone " + bone);
                    // Restart the same clip's tween repeatedly, including duplicate rendered frames.
                    foreach (float remaining in new[] { 1f, 0.75f, 0.5f, 0.25f, 0f })
                    {
                        channels[channel].Frame = -remaining / overlay.Frames;
                        audit.Apply(channels, name + " repeated tween");
                        audit.Apply(channels, name + " repeated draw");
                    }
                    for (int step = 0; step <= 8; step++)
                    {
                        channels[channel].Frame = channels[channel].ClipFrame = step * (overlay.Frames - 1f) / (8f * overlay.Frames);
                        channels[channel].Alpha = 1f - step / 8f;
                        audit.Apply(channels, name + " fade");
                    }
                    // Alpha zero must recover an independently drawn base, with no stale tween/attachment pose.
                    for (int b = 0; b < audit.Model.Skeleton.GetBoneCount(); b++)
                        RequireTransform(reference.Skeleton.GetBonePose(b), audit.Model.Skeleton.GetBonePose(b),
                            name + " faded bone=" + audit.Model.Skeleton.GetBoneName(b));
                }
            }
        }
        finally { reference.Root.Free(); }
    }

    private readonly record struct ClipInfo(AnimClip Clip, int Frames, float Fps)
    {
        public ChannelPose Pose(float frame, bool mirror = false) => new()
        {
            Clip = Clip, Sequence = Clip.Name, Frames = Frames, Fps = Fps,
            Frame = frame, ClipFrame = frame, Alpha = 1f, Mirror = mirror,
        };
    }

    private sealed class Audit : IDisposable
    {
        public readonly string Path;
        public readonly SkeletalModel Model;
        public readonly Dictionary<string, ClipInfo> Clips = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, float> TranslationOutliers = new(StringComparer.Ordinal);
        public readonly HashSet<string> KnownOutlierClips = new(StringComparer.Ordinal);
        public int Poses;
        private readonly IReadOnlyList<int> _mirrors;
        private readonly int[] _parents, _order, _attachments;
        private readonly string[] _names;
        private readonly Transform3D[] _rest, _local, _global;
        private ChannelPoseApplier _applier;

        public Audit(string asset, string metadata, IReadOnlyList<int> mirrors, string[] attachments)
        {
            Path = SkeletalModel.FindGlb(asset) ?? throw new InvalidOperationException("Missing " + asset);
            Model = SkeletalModel.Load(Path);
            _mirrors = mirrors;
            int count = Model.Skeleton.GetBoneCount();
            _parents = new int[count];
            _names = new string[count];
            _rest = new Transform3D[count];
            _local = new Transform3D[count];
            _global = new Transform3D[count];
            for (int bone = 0; bone < count; bone++)
            {
                _parents[bone] = Model.Skeleton.GetBoneParent(bone);
                _names[bone] = Model.Skeleton.GetBoneName(bone);
                _rest[bone] = Model.Skeleton.GetBoneRest(bone);
            }
            _order = Enumerable.Range(0, count).OrderBy(Depth).ToArray();
            _attachments = attachments.Select(name => Model.Skeleton.FindBone(name)).ToArray();
            if (_attachments.Any(bone => bone < 0)) throw new InvalidOperationException(asset + ": missing static attachment");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, metadata)));
            JsonElement sequences = document.RootElement.GetProperty("sequences");
            foreach (string name in Model.Sampler.ClipNames)
            {
                JsonElement sequence = sequences.GetProperty(name);
                var clip = new ClipInfo(Model.Sampler.Find(name)!, sequence.GetProperty("frames").GetInt32(), sequence.GetProperty("rate").GetSingle());
                if (clip.Frames < 1 || !(clip.Fps > 0f)) throw new InvalidOperationException(name + ": invalid clip timing");
                if (clip.Clip.Tracks.Any(track => _attachments.Contains(track.Bone)))
                    throw new InvalidOperationException(name + ": expected attachment now has an authored track; review audit assumptions");
                Clips.Add(name, clip);
            }
            _applier = new ChannelPoseApplier(Model.Skeleton, mirrors, Model.PackageBones);
        }

        private int Depth(int bone)
        {
            int depth = 0;
            for (int parent = _parents[bone]; parent >= 0; parent = _parents[parent]) depth++;
            return depth;
        }

        public void Reset() => _applier = new ChannelPoseApplier(Model.Skeleton, _mirrors, Model.PackageBones);

        public void Apply(ChannelPose[] channels, string label, bool translations = false)
        {
            _applier.Apply(channels);
            foreach (int bone in _order)
            {
                Transform3D local = _local[bone] = Model.Skeleton.GetBonePose(bone);
                _global[bone] = _parents[bone] >= 0 ? _global[_parents[bone]] * local : local;
                RequireFinite(local, label + " local " + _names[bone]);
                RequireFinite(_global[bone], label + " global " + _names[bone]);
                Basis basis = local.Basis;
                if (Math.Abs(basis.Determinant() - 1f) > 0.001f ||
                    Math.Abs(basis.X.LengthSquared() - 1f) > 0.001f || Math.Abs(basis.Y.LengthSquared() - 1f) > 0.001f ||
                    Math.Abs(basis.Z.LengthSquared() - 1f) > 0.001f)
                    throw new InvalidOperationException($"{label} {_names[bone]}: non-unit or singular bone basis {basis}");
                // The root is deliberately not mirrored. Its direct child's local translation therefore
                // includes the reflected root motion; compare deeper body offsets, not that compensation.
                bool mirroredRootChild = channels[0].Mirror && _parents[bone] >= 0 && _parents[_parents[bone]] < 0;
                if (translations && _parents[bone] >= 0 && !mirroredRootChild)
                {
                    float offset = local.Origin.DistanceTo(_rest[bone].Origin);
                    if (offset > 0.5f)
                    {
                        // A finite transform may still detach a body part. Two legacy W rail clips have these
                        // offsets in the source data and no gameplay references; keep the exceptions explicit.
                        string sequence = channels[0].Sequence ?? "";
                        if (sequence is not ("GrabLgBgRailW" or "GrabLgEdRailW"))
                            throw new InvalidOperationException($"{label} {_names[bone]}: unexpected local offset {offset:F6}m from rest");
                        KnownOutlierClips.Add(sequence);
                        string key = label + " bone=" + _names[bone];
                        TranslationOutliers[key] = Math.Max(TranslationOutliers.GetValueOrDefault(key), offset);
                    }
                }
            }
            foreach (int bone in _attachments)
                RequireTransform(_rest[bone], _local[bone], label + " static attachment=" + _names[bone]);
            Poses++;
        }

        public void Dispose() => Model.Root.Free();
    }

    private static void RequireFinite(Transform3D transform, string label)
    {
        if (!float.IsFinite(transform.Origin.LengthSquared()) || !float.IsFinite(transform.Basis.X.LengthSquared()) ||
            !float.IsFinite(transform.Basis.Y.LengthSquared()) || !float.IsFinite(transform.Basis.Z.LengthSquared()))
            throw new InvalidOperationException(label + ": non-finite bone transform");
    }

    private static void RequireTransform(Transform3D expected, Transform3D actual, string label)
    {
        RequireFinite(actual, label);
        if (expected.Origin.DistanceTo(actual.Origin) > 0.0001f || expected.Basis.X.DistanceTo(actual.Basis.X) > 0.0001f ||
            expected.Basis.Y.DistanceTo(actual.Basis.Y) > 0.0001f || expected.Basis.Z.DistanceTo(actual.Basis.Z) > 0.0001f)
            throw new InvalidOperationException(label + ": attachment/base pose changed");
    }
}
