using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Gltf;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>
/// The drawn pose against the original's bones. A capture of the running original (local/difftest/golden-bones, one
/// JSON line per frame, written by the sandbox capture: Play Level on SteeE as the spy, crouched at the spawn, then
/// standing up, turning, walking, crouching) holds each frame's animation channels (sequence, frame, alpha, tween)
/// and the mesh instance's 43 bone space bases (mesh space, 48 bytes each: origin and three axes). This smoke feeds
/// the same channels to <see cref="ChannelPoseApplier"/> on SPerso.ATT_01 and compares every bone's origin with the
/// original's: the crouched and standing stances, the crouch and stand-up clips, the turn in place (channel 3 plays
/// its clip backward) and the walk blends. Skips when no capture is there (it is game-derived and never tracked).
/// </summary>
public sealed class AnimBonesSmoke : SmokeTest
{
    public const string TestName = "anim-bones";

    /// <summary>Largest bone distance that counts as the same pose (uu): the capture's floats and the sampler's keys.</summary>
    private const float Tolerance = 1.5f;

    /// <summary>Share of frames that must match: a capture read can straddle the next frame's pose build.</summary>
    private const float MatchShare = 0.99f;

    /// <summary>The channels the walk blender's first call plays backward on the spy (anim.md rule 3).</summary>
    private static readonly HashSet<int> SpyBackward = [3, 6, 7, 11, 12];

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        string folder = SmokePaths.Local("difftest", "golden-bones");
        string? glb = SkeletalModel.FindGlb("SPerso.ATT_01");
        string anim = Path.Combine(SkeletalModel.DefaultSharedRoot(), "chars", "SPerso", "PRO.anim.json");
        string[] captures = Directory.Exists(folder) ? Directory.GetFiles(folder, "spy-*.jsonl").Order(StringComparer.Ordinal).ToArray() : [];
        if (captures.Length == 0 || glb is null || !File.Exists(anim))
        {
            return SmokeResult.Pass($"skipped: no spy capture under {folder} or no converted SPerso");
        }

        var details = new List<string>();
        bool failed = false;
        foreach (string capture in captures)
        {
            SkeletalModel model = SkeletalModel.Load(glb);
            try
            {
                (bool ok, string detail) = Compare(model, capture, anim, glb);
                failed |= !ok;
                details.Add(Path.GetFileNameWithoutExtension(capture) + ": " + detail);
            }
            finally
            {
                model.Root.Free();
            }
        }

        string all = string.Join(" | ", details);
        return failed ? SmokeResult.Fail(all) : SmokeResult.Pass(all);
    }

    private static (bool Ok, string Detail) Compare(SkeletalModel model, string capture, string animPath, string glb)
    {
        Skeleton3D skeleton = model.Skeleton;
        var applier = new ChannelPoseApplier(skeleton);
        Dictionary<string, (int Frames, float Rate)> meta = ReadMeta(animPath);
        int[] boneOf = PackageBoneOrder(glb).Select(skeleton.FindBone).ToArray();
        var channels = new ChannelPose[25];
        int frames = 0;
        int matched = 0;
        float worst = 0f;
        string worstAt = "";
        foreach (string line in File.ReadLines(capture))
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement s = doc.RootElement;
            if (s.TryGetProperty("header", out _) || !s.TryGetProperty("arrays", out JsonElement arrays) ||
                !arrays.TryGetProperty("180", out JsonElement bonesHex) || !s.TryGetProperty("traversal", out JsonElement tr))
            {
                continue;
            }

            Array.Clear(channels);
            foreach (JsonElement c in tr.GetProperty("channels").EnumerateArray())
            {
                int index = c[0].GetInt32();
                string seq = c[1].GetString() ?? "";
                float frame = c[2].GetSingle();
                float alpha = c[4].GetSingle();
                if (index >= channels.Length || model.Sampler.Find(seq) is not { } clip || !meta.TryGetValue(seq, out var m))
                {
                    continue;
                }

                bool backward = index != 0 && SpyBackward.Contains(index);
                float f = Math.Clamp(frame, 0f, 1f);
                channels[index] = new ChannelPose
                {
                    Clip = clip,
                    Sequence = seq,
                    Frame = frame,
                    ClipFrame = backward ? 1f - f : f,
                    Frames = m.Frames,
                    Fps = m.Rate,
                    Alpha = alpha,
                    // A one-shot holds at AnimLast, so sampling it as a loop changes nothing.
                    Loop = true,
                };
            }

            applier.Apply(channels);
            // Outside the scene tree the skeleton does not rebuild its global poses: chain the local ones.
            Transform3D[] global = GlobalPoses(skeleton);
            byte[] bones = Convert.FromHexString(bonesHex.GetString() ?? "");
            float frameWorst = 0f;
            for (int b = 0; b < boneOf.Length && (b * 48) + 12 <= bones.Length; b++)
            {
                if (boneOf[b] < 0)
                {
                    continue;
                }

                var original = new Vector3(BitConverter.ToSingle(bones, b * 48), BitConverter.ToSingle(bones, (b * 48) + 4), BitConverter.ToSingle(bones, (b * 48) + 8));
                Vector3 g = global[boneOf[b]].Origin * 100f;
                // The converted skeleton's space: UE mesh X, Z, Y as Godot X, Y, Z.
                var ours = new Vector3(g.X, g.Z, g.Y);
                frameWorst = Math.Max(frameWorst, ours.DistanceTo(original));
            }

            frames++;
            if (frameWorst <= Tolerance)
            {
                matched++;
            }

            if (frameWorst > worst)
            {
                worst = frameWorst;
                worstAt = string.Create(CultureInfo.InvariantCulture, $"frame {s.GetProperty("frame").GetInt32()} ({s.GetProperty("phase").GetString()})");
            }
        }

        string detail = string.Create(CultureInfo.InvariantCulture,
            $"{matched}/{frames} frames of the original's spy within {Tolerance} uu on all 43 bones; worst {worst:F1} uu at {worstAt}");
        GD.Print("SVM_BONES " + Path.GetFileName(capture) + " " + detail);
        return (frames > 0 && matched >= frames * MatchShare, frames > 0 ? detail : "the capture holds no frames with bones");
    }

    private static Transform3D[] GlobalPoses(Skeleton3D skeleton)
    {
        int count = skeleton.GetBoneCount();
        var global = new Transform3D[count];
        var done = new bool[count];
        for (int b = 0; b < count; b++)
        {
            Resolve(b);
        }

        return global;

        Transform3D Resolve(int b)
        {
            if (!done[b])
            {
                int parent = skeleton.GetBoneParent(b);
                global[b] = parent >= 0 ? Resolve(parent) * skeleton.GetBonePose(b) : skeleton.GetBonePose(b);
                done[b] = true;
            }

            return global[b];
        }
    }

    private static Dictionary<string, (int Frames, float Rate)> ReadMeta(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        var meta = new Dictionary<string, (int, float)>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty seq in doc.RootElement.GetProperty("sequences").EnumerateObject())
        {
            meta[seq.Name] = (seq.Value.GetProperty("frames").GetInt32(), seq.Value.GetProperty("rate").GetSingle());
        }

        return meta;
    }

    /// <summary>The skin's joint names in the package's bone order (the order of the original's space bases).</summary>
    private static List<string> PackageBoneOrder(string glb)
    {
        using GlbDocument doc = GlbDocument.Load(glb);
        JsonElement root = doc.Root;
        JsonElement nodes = root.GetProperty("nodes");
        return root.GetProperty("skins")[0].GetProperty("joints").EnumerateArray()
            .Select(j => nodes[j.GetInt32()].GetProperty("name").GetString() ?? "").ToList();
    }
}
