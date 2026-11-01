using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScctGame.Render.Gltf;

namespace OpenScctGame.Render.Characters;

/// <summary>Preserves the exact linear keys of converted UE skeletal clips instead of baking them at 30 Hz.</summary>
internal static class AuthoredAnimationTracks
{
    public static bool IsConverted(GlbDocument glb) =>
        glb.Root.TryGetProperty("extras", out JsonElement extras) &&
        extras.TryGetProperty("svmap", out JsonElement metadata) &&
        metadata.TryGetProperty("animation", out JsonElement animation) &&
        animation.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(animation.GetString());

    public static List<(string, Animation)> Read(GlbDocument glb, Skeleton3D skeleton)
    {
        var result = new List<(string, Animation)>();
        if (!glb.Root.TryGetProperty("animations", out JsonElement clips))
            throw new InvalidDataException("Converted skeletal content declares animation metadata but has no glTF clips.");
        JsonElement nodes = glb.Root.GetProperty("nodes");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement clip in clips.EnumerateArray())
        {
            string name = clip.GetProperty("name").GetString() ?? "";
            if (name.Length == 0 || !names.Add(name)) throw new InvalidDataException("Missing or duplicate converted animation name: " + name);
            var animation = new Animation();
            JsonElement samplers = clip.GetProperty("samplers");
            var seen = new HashSet<(int, string)>();
            double length = 0;
            foreach (JsonElement channel in clip.GetProperty("channels").EnumerateArray())
            {
                JsonElement target = channel.GetProperty("target");
                int node = target.GetProperty("node").GetInt32();
                string bone = nodes[node].GetProperty("name").GetString() ?? "";
                string path = target.GetProperty("path").GetString() ?? "";
                if (skeleton.FindBone(bone) < 0 || !seen.Add((node, path)))
                    throw new InvalidDataException($"{name}: missing bone or duplicate track {bone}/{path}.");
                Animation.TrackType type = path switch
                {
                    "translation" => Animation.TrackType.Position3D,
                    "rotation" => Animation.TrackType.Rotation3D,
                    "scale" => Animation.TrackType.Scale3D,
                    _ => throw new InvalidDataException($"{name}: unsupported converted track {path}."),
                };
                JsonElement sampler = samplers[channel.GetProperty("sampler").GetInt32()];
                if (sampler.TryGetProperty("interpolation", out JsonElement interpolation) && interpolation.GetString() != "LINEAR")
                    throw new InvalidDataException($"{name}: converted clips require LINEAR keys, got {interpolation}.");
                float[] times = glb.ReadFloats(sampler.GetProperty("input").GetInt32());
                float[] values = glb.ReadFloats(sampler.GetProperty("output").GetInt32());
                int width = type == Animation.TrackType.Rotation3D ? 4 : 3;
                if (times.Length == 0 || values.Length != times.Length * width)
                    throw new InvalidDataException($"{name}/{bone}: animation time/value counts do not match.");
                int track = animation.AddTrack(type);
                animation.TrackSetPath(track, new NodePath("Skeleton3D:" + bone));
                animation.TrackSetInterpolationType(track, Animation.InterpolationType.Linear);
                animation.TrackSetInterpolationLoopWrap(track, false);
                for (int key = 0; key < times.Length; key++)
                {
                    float time = times[key];
                    if (!float.IsFinite(time) || time < 0 || (key > 0 && time <= times[key - 1]))
                        throw new InvalidDataException($"{name}/{bone}: invalid animation key time {time}.");
                    int offset = key * width;
                    for (int component = 0; component < width; component++)
                        if (!float.IsFinite(values[offset + component])) throw new InvalidDataException($"{name}/{bone}: non-finite animation key.");
                    if (type == Animation.TrackType.Rotation3D)
                    {
                        var rotation = new Quaternion(values[offset], values[offset + 1], values[offset + 2], values[offset + 3]);
                        if (rotation.LengthSquared() < 0.000001f) throw new InvalidDataException($"{name}/{bone}: zero animation quaternion.");
                        animation.RotationTrackInsertKey(track, time, rotation.Normalized());
                    }
                    else
                    {
                        var value = new Vector3(values[offset], values[offset + 1], values[offset + 2]);
                        if (type == Animation.TrackType.Position3D) animation.PositionTrackInsertKey(track, time, value);
                        else animation.ScaleTrackInsertKey(track, time, value);
                    }
                    length = Math.Max(length, time);
                }
            }
            if (animation.GetTrackCount() == 0) throw new InvalidDataException(name + ": converted animation has no bone tracks.");
            animation.Length = Math.Max(length, 0.001);
            result.Add((name, animation));
        }
        return result;
    }
}
