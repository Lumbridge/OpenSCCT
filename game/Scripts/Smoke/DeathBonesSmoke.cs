using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Gltf;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Opt-in replay of original combat-rig death captures through the actual skeleton pose builder.</summary>
public sealed class DeathBonesSmoke : SmokeTest
{
    public const string TestName = "death-bones";
    private const float Tolerance = 1.5f;
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        string[] paths = (System.Environment.GetEnvironmentVariable("SVM_DEATH_BONES_CAPTURES") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (paths.Length == 0) return SmokeResult.Pass("skipped: set SVM_DEATH_BONES_CAPTURES to original death-presentation.jsonl paths");
        var details = new List<string>();
        bool failed = false;
        foreach (string path in paths)
        {
            if (!File.Exists(path)) return SmokeResult.Fail("Missing requested death capture: " + path);
            (bool ok, string detail) = Compare(path);
            failed |= !ok;
            details.Add(detail);
            GD.Print("SVM_DEATH_BONES " + detail);
        }
        return failed ? SmokeResult.Fail(string.Join(" | ", details)) : SmokeResult.Pass(string.Join(" | ", details));
    }

    private static (bool Ok, string Detail) Compare(string capture)
    {
        using JsonDocument header = JsonDocument.Parse(File.ReadLines(capture).First());
        string side = header.RootElement.GetProperty("victim").GetString()!;
        bool merc = header.RootElement.GetProperty("victimTeam").GetString() == "merc";
        string asset = merc ? "SPerso.DEF_01" : "SPerso.ATT_01";
        string? glb = SkeletalModel.FindGlb(asset);
        string anim = Path.Combine(SkeletalModel.DefaultSharedRoot(), "chars", "SPerso", merc ? "Def.anim.json" : "PRO.anim.json");
        if (glb is null || !File.Exists(anim)) return (false, "Missing converted character for requested capture " + asset);
        SkeletalModel model = SkeletalModel.Load(glb);
        try
        {
            Skeleton3D skeleton = model.Skeleton;
            var applier = new ChannelPoseApplier(skeleton);
            using JsonDocument meta = JsonDocument.Parse(File.ReadAllText(anim));
            var clips = meta.RootElement.GetProperty("sequences").EnumerateObject().ToDictionary(p => p.Name,
                p => (Frames: p.Value.GetProperty("frames").GetInt32(), Fps: p.Value.GetProperty("rate").GetSingle()),
                StringComparer.OrdinalIgnoreCase);
            using GlbDocument document = GlbDocument.Load(glb);
            JsonElement nodes = document.Root.GetProperty("nodes");
            int[] boneOf = document.Root.GetProperty("skins")[0].GetProperty("joints").EnumerateArray()
                .Select(j => skeleton.FindBone(nodes[j.GetInt32()].GetProperty("name").GetString()!)).ToArray();
            var channels = new ChannelPose[25];
            Vector3? lockedRoot = null;
            int compared = 0, matched = 0, lockedFrames = 0;
            float worst = 0f;
            string worstAt = "";
            var seen = new HashSet<string>();
            foreach (string line in File.ReadLines(capture).Skip(1))
            {
                using JsonDocument frame = JsonDocument.Parse(line);
                JsonElement sample = frame.RootElement, body = sample.GetProperty(side), pose = sample.GetProperty(side + "Pose");
                Array.Clear(channels);
                foreach (JsonElement channel in body.GetProperty("anim").EnumerateArray())
                {
                    int index = channel[0].GetInt32();
                    string sequence = channel[1].GetString()!;
                    if (index >= channels.Length || model.Sampler.Find(sequence) is not { } clip || !clips.TryGetValue(sequence, out var info))
                        return (false, $"{asset}: missing captured channel {index}/{sequence}");
                    float phase = channel[2].GetSingle();
                    channels[index] = new ChannelPose { Clip = clip, Sequence = sequence, Frame = phase,
                        ClipFrame = Math.Clamp(phase, 0f, 1f), Frames = info.Frames, Fps = info.Fps,
                        Alpha = channel[3].GetSingle(), Loop = true };
                }
                JsonElement root = pose.GetProperty("rootLock");
                int mode = root.GetProperty("mode").GetInt32();
                Vector3 currentRoot = Vector(root.GetProperty("rootKey"));
                if (mode == 0) lockedRoot = null;
                else lockedRoot ??= root.TryGetProperty("lockKey", out JsonElement key) ? Vector(key) : currentRoot;
                // Current ordinary rifle captures use translation-only locking. Reject an unsupported yaw
                // capture rather than treating the absence of its rotation bases as a successful comparison.
                if ((mode & 2) != 0) return (false, asset + ": captured yaw lock requires rotation-basis capture/replay");
                applier.Apply(channels, (RootLock)mode, rootOffset: lockedRoot is { } start ? start - currentRoot : null);
                if (body.GetProperty("hp").GetInt32() > 0 || channels[0].Frame < 0f ||
                    !sample.GetProperty("stable").GetBoolean() || !pose.GetProperty("poseReadSameTick").GetBoolean()) continue;
                // A damage overlay can be caught between its tick and pose rebuild; the next frame of these
                // captures has faded it out. Require the authored death itself, including its resting clip.
                bool overlay = false;
                for (int c = 1; c < channels.Length; c++) overlay |= channels[c].Alpha > 0.001f;
                if (overlay) continue;
                Transform3D[] global = GlobalPoses(skeleton);
                byte[] original = Convert.FromHexString(pose.GetProperty("arrays").GetProperty("180").GetString()!);
                if (pose.GetProperty("boneCount").GetInt32() != boneOf.Length || boneOf.Any(b => b < 0))
                    return (false, asset + ": package bone order does not match capture");
                float distance = 0f;
                for (int b = 0; b < boneOf.Length; b++)
                {
                    int offset = b * 48;
                    Vector3 expected = new(BitConverter.ToSingle(original, offset), BitConverter.ToSingle(original, offset + 8), BitConverter.ToSingle(original, offset + 4));
                    distance = Math.Max(distance, (global[boneOf[b]].Origin * 100f).DistanceTo(expected));
                }
                compared++;
                if (mode != 0) lockedFrames++;
                if (distance <= Tolerance) matched++;
                seen.Add(channels[0].Sequence!);
                if (distance > worst) { worst = distance; worstAt = $"frame {sample.GetProperty("frame").GetInt32()} {channels[0].Sequence}"; }
            }
            string detail = $"{asset}: {matched}/{compared} death frames, all {boneOf.Length} bones <= {Tolerance} uu; {lockedFrames} root-locked; worst {worst:F3} uu at {worstAt}; {string.Join(',', seen)}";
            return (compared >= 120 && lockedFrames >= 30 && matched >= compared * 0.99f, detail);
        }
        finally { model.Root.Free(); }
    }

    // Original root keys are in mesh axes/uu; the renderer uses X,Z,Y/metres.
    private static Vector3 Vector(JsonElement v) => new(v[0].GetSingle() / 100f, v[2].GetSingle() / 100f, v[1].GetSingle() / 100f);

    private static Transform3D[] GlobalPoses(Skeleton3D skeleton)
    {
        var result = new Transform3D[skeleton.GetBoneCount()];
        var done = new bool[result.Length];
        Transform3D Resolve(int b)
        {
            if (!done[b])
            {
                int parent = skeleton.GetBoneParent(b);
                result[b] = parent >= 0 ? Resolve(parent) * skeleton.GetBonePose(b) : skeleton.GetBonePose(b);
                done[b] = true;
            }
            return result[b];
        }
        for (int b = 0; b < result.Length; b++) Resolve(b);
        return result;
    }
}
