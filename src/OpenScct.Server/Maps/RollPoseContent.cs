using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using OpenScct.Core;

namespace OpenScct.Server;

/// <summary>
/// Renderer-independent character animation sampling. The historical roll-only loader remains available;
/// all-clips loading retains animation tracks for general hit-zone poses. Mesh and texture data are discarded.
/// Matrices use System.Numerics row vectors in glTF model space (metres, Y up), including ancestor transforms.
/// Pawn placement, mesh offsets and conversion to Unreal coordinates belong to the caller.
/// </summary>
public sealed class RollPoseContent
{
    private readonly Node[] _nodes;
    private readonly int[] _order;
    private readonly Dictionary<string, Clip> _clips;
    private readonly Dictionary<string, int> _nodeNames = new(StringComparer.Ordinal);
    private readonly string[] _namedBones;
    private readonly int[] _opposites;
    private string[] _packageBones = [];

    private sealed record Node(string? Name, int Parent, Vector3 Translation, Quaternion Rotation, Vector3 Scale);
    private sealed record Track(int Node, string Path, float[] Times, float[] Values, int Components);
    private sealed record Clip(float Duration, Track[] Tracks);

    private RollPoseContent(Node[] nodes, int[] order, Dictionary<string, Clip> clips, FRotator rotOrigin, FVector meshScale)
    {
        _nodes = nodes;
        _order = order;
        _clips = clips;
        for (int i = 0; i < nodes.Length; i++)
            if (nodes[i].Name is { } name) _nodeNames.Add(name, i);
        _namedBones = order.Where(i => nodes[i].Name is not null).Select(i => nodes[i].Name!).ToArray();
        _opposites = new int[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            string? name = nodes[i].Name;
            string? opposite = name?.Replace(" L ", " R ", StringComparison.Ordinal);
            if (opposite == name) opposite = name?.Replace(" R ", " L ", StringComparison.Ordinal);
            _opposites[i] = opposite is not null && _nodeNames.TryGetValue(opposite, out int mapped) ? mapped : i;
        }
        RotOrigin = rotOrigin;
        MeshScale = meshScale;
    }

    /// <summary>Converted mesh orientation and scale, applied by the renderer after the glTF-to-Unreal conversion.</summary>
    public FRotator RotOrigin { get; }
    public FVector MeshScale { get; }
    internal ReadOnlySpan<string> NamedBones => _namedBones;

    /// <summary>An animation channel's sampled time and weight. Callers pass channel 0 at weight 1.</summary>
    public readonly record struct Layer(string ClipName, float Seconds, float Alpha, string? StartBone = null,
        bool Loop = false, float Fps = 0f, bool Mirror = false, int Channel = -1, float Frame = 0f, int Frames = 1,
        bool GlobalPose = false, IReadOnlyList<int>? BoneMirrors = null);

    /// <summary>One pawn's pre-procedural blended pose and tween clock. Never shared between pawns.</summary>
    public sealed class PoseHistory
    {
        internal Vector3[] Positions = [], Scales = [];
        internal Quaternion[] Rotations = [];
        internal readonly Dictionary<int, (string Sequence, float Frame)> Tweens = new();
        internal Layer[] Layers = [];
        internal int LayerCount = -1;
        internal readonly Dictionary<string, Matrix4x4> Pose = new(StringComparer.Ordinal);
        internal Vector3[] WorkPositions = [], WorkScales = [], SamplePositions = [], SampleScales = [];
        internal Quaternion[] WorkRotations = [], SampleRotations = [];
        internal Matrix4x4[] World = [], MirrorSource = [], Mirrored = [];
        internal byte[] Sampled = [], OriginalSamples = [];
        public void Reset()
        {
            LayerCount = -1;
            Tweens.Clear(); Pose.Clear();
        }

        internal void CopyTo(PoseHistory copy)
        {
            Resize(ref copy.Positions, Positions.Length); Positions.CopyTo(copy.Positions, 0);
            Resize(ref copy.Rotations, Rotations.Length); Rotations.CopyTo(copy.Rotations, 0);
            Resize(ref copy.Scales, Scales.Length); Scales.CopyTo(copy.Scales, 0);
            if (copy.Layers.Length < Layers.Length) copy.Layers = new Layer[Layers.Length];
            Layers.CopyTo(copy.Layers, 0); copy.LayerCount = LayerCount;
            copy.Tweens.Clear(); copy.Pose.Clear();
            foreach (var pair in Tweens) copy.Tweens.Add(pair.Key, pair.Value);
            foreach (var pair in Pose) copy.Pose.Add(pair.Key, pair.Value);
        }

        internal void EnsureScratch(int count)
        {
            Resize(ref WorkPositions, count); Resize(ref WorkRotations, count); Resize(ref WorkScales, count);
            Resize(ref SamplePositions, count); Resize(ref SampleRotations, count); Resize(ref SampleScales, count);
            Resize(ref World, count); Resize(ref MirrorSource, count); Resize(ref Mirrored, count);
            Resize(ref Sampled, count); Resize(ref OriginalSamples, count);
        }

        internal static void Resize<T>(ref T[] array, int count)
        {
            if (array.Length != count) array = new T[count];
        }
    }

    public static bool IsRollClip(string name) =>
        name.Equals("RollStAlFdL", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("RollStAlFdR", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("RollStJpFdL", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("RollStJpFdR", StringComparison.OrdinalIgnoreCase);

    /// <summary>Missing optional character content returns null; malformed or unsupported roll data is diagnosed.</summary>
    public static RollPoseContent? Load(string path, bool allClips = false)
    {
        try
        {
            return Read(File.ReadAllBytes(path), allClips);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception error) when (error is InvalidDataException or JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new InvalidDataException($"Invalid roll animation content '{path}': {error.Message}", error);
        }
    }

    public bool TryGetDuration(string clipName, out float seconds)
    {
        bool found = _clips.TryGetValue(clipName, out Clip? clip);
        seconds = clip?.Duration ?? 0f;
        return found;
    }

    /// <summary>
    /// Samples [0,1] from the first to the last stored frame. UE2 AnimFrame's last frame is 1 - 1/N, so callers
    /// using that clock must divide by its last-frame value first. On success replaces the named-node transforms.
    /// An unknown clip returns false without changing the destination. Values outside [0,1] clamp to the endpoints.
    /// </summary>
    public bool TrySample(string clipName, float normalizedTime, IDictionary<string, Matrix4x4> transforms)
    {
        if (!float.IsFinite(normalizedTime)) throw new ArgumentOutOfRangeException(nameof(normalizedTime));
        return _clips.TryGetValue(clipName, out Clip? clip) &&
            TrySampleLayers([new Layer(clipName, Math.Clamp(normalizedTime, 0f, 1f) * clip.Duration, 1f)], transforms);
    }

    /// <summary>Samples clip seconds, clamping before its first and after its last key.</summary>
    public bool TrySampleSeconds(string clipName, float seconds, IDictionary<string, Matrix4x4> transforms)
    {
        if (!float.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        return TrySampleLayers([new Layer(clipName, seconds, 1f)], transforms);
    }

    /// <summary>
    /// Blends local translations/scales and shortest-arc rotations in channel order, then accumulates the hierarchy.
    /// Mirrored layers reflect skeleton-space bases, exchange left/right bones, then blend in local space.
    /// Tween history belongs to the caller. Unknown active clips or start bones leave the destination unchanged.
    /// </summary>
    public bool TrySampleLayers(ReadOnlySpan<Layer> layers, IDictionary<string, Matrix4x4> transforms, PoseHistory? history = null)
    {
        ArgumentNullException.ThrowIfNull(transforms);
        bool active = false;
        foreach (Layer layer in layers)
        {
            if (!float.IsFinite(layer.Alpha) || !float.IsFinite(layer.Seconds)) return false;
            if (layer.Alpha <= 0f) continue;
            active = true;
            if (!_clips.ContainsKey(layer.ClipName) ||
                (layer.StartBone is { Length: > 0 } bone && !_nodeNames.ContainsKey(bone)) ||
                (layer.Loop && (!(layer.Fps > 0f) || !float.IsFinite(layer.Fps)))) return false;
        }
        if (!active) return false;
        if (history is { LayerCount: >= 0 } && layers.SequenceEqual(history.Layers.AsSpan(0, history.LayerCount)))
        {
            transforms.Clear();
            foreach (var pair in history.Pose) transforms[pair.Key] = pair.Value;
            return true;
        }

        PoseHistory scratch = history ?? new PoseHistory();
        scratch.EnsureScratch(_nodes.Length);
        Vector3[] translations = scratch.WorkPositions, scales = scratch.WorkScales;
        Quaternion[] rotations = scratch.WorkRotations;
        Matrix4x4[] world = scratch.World;
        for (int i = 0; i < _nodes.Length; i++)
        {
            translations[i] = _nodes[i].Translation;
            rotations[i] = _nodes[i].Rotation;
            scales[i] = _nodes[i].Scale;
        }

        int layerIndex = -1;
        foreach (Layer layer in layers)
        {
            layerIndex++;
            if (layer.Alpha <= 0f) continue;
            Clip clip = _clips[layer.ClipName];
            int start = layer.StartBone is { Length: > 0 } bone ? _nodeNames[bone] : -1;
            float alpha = Math.Clamp(layer.Alpha, 0f, 1f);
            bool wrap = layer.Loop && layer.Seconds > clip.Duration && clip.Duration > 0f;
            float seconds = wrap ? clip.Duration : layer.Seconds;
            float wrapAlpha = wrap ? Math.Clamp((layer.Seconds - clip.Duration) * layer.Fps, 0f, 1f) : 0f;
            Vector3[] samplePos = scratch.SamplePositions, sampleScale = scratch.SampleScales;
            Quaternion[] sampleRot = scratch.SampleRotations;
            byte[] sampled = scratch.Sampled;
            Array.Clear(sampled);
            for (int i = 0; i < _nodes.Length; i++)
            {
                samplePos[i] = _nodes[i].Translation;
                sampleRot[i] = _nodes[i].Rotation;
                sampleScale[i] = _nodes[i].Scale;
            }
            foreach (Track track in clip.Tracks)
            {
                int right = Array.BinarySearch(track.Times, seconds);
                if (right < 0) right = ~right;
                right = Math.Min(right, track.Times.Length - 1);
                int left = Math.Max(0, right - 1);
                float amount = left == right ? 0f : Math.Clamp(
                    (seconds - track.Times[left]) / (track.Times[right] - track.Times[left]), 0f, 1f);
                int a = left * track.Components;
                int b = right * track.Components;
                float[] v = track.Values;
                if (track.Path == "rotation")
                {
                    // glTF LINEAR rotations interpolate on the shortest arc, including antipodal quaternion keys.
                    Quaternion value = Quaternion.Normalize(Quaternion.Slerp(
                        new Quaternion(v[a], v[a + 1], v[a + 2], v[a + 3]),
                        new Quaternion(v[b], v[b + 1], v[b + 2], v[b + 3]), amount));
                    if (wrap) value = Quaternion.Normalize(Quaternion.Slerp(value,
                        new Quaternion(v[0], v[1], v[2], v[3]), wrapAlpha));
                    sampleRot[track.Node] = value;
                    sampled[track.Node] |= 2;
                }
                else
                {
                    Vector3 value = Vector3.Lerp(new Vector3(v[a], v[a + 1], v[a + 2]),
                        new Vector3(v[b], v[b + 1], v[b + 2]), amount);
                    if (track.Path == "translation")
                    {
                        if (wrap) value = Vector3.Lerp(value, new Vector3(v[0], v[1], v[2]), wrapAlpha);
                        samplePos[track.Node] = value;
                        sampled[track.Node] |= 1;
                    }
                    // Ordinary channels hold the last scale; the renderer's prepared mirror path wraps it.
                    else
                    {
                        if (wrap && layer.Mirror) value = Vector3.Lerp(value, new Vector3(v[0], v[1], v[2]), wrapAlpha);
                        sampleScale[track.Node] = value;
                        sampled[track.Node] |= 4;
                    }
                }
            }
            if (layer.Mirror && !Mirror(samplePos, sampleRot, sampleScale, sampled, layer.BoneMirrors, scratch)) return false;
            if (layer.GlobalPose && start >= 0 && _nodes[start].Parent >= 0)
            {
                // Keep the start bone in the clip's mesh space while its lower-priority parent may be turned.
                Matrix4x4 sampledGlobal = Global(start, samplePos, sampleRot, sampleScale);
                Matrix4x4 parentGlobal = Global(_nodes[start].Parent, translations, rotations, scales);
                if (!Matrix4x4.Invert(parentGlobal, out Matrix4x4 inverse) ||
                    !Matrix4x4.Decompose(sampledGlobal * inverse, out sampleScale[start], out sampleRot[start], out samplePos[start])) return false;
                sampled[start] |= 7;
            }
            float tween = 1f;
            if (history is { Pose.Count: > 0 } && layer.Frame < 0f)
            {
                int channel = layer.Channel >= 0 ? layer.Channel : layerIndex;
                tween = history.Tweens.TryGetValue(channel, out var cached) ? 1f - layer.Frame / cached.Frame : -1f;
                if (cached.Sequence != layer.ClipName || !(tween >= 0f && tween <= 1f))
                {
                    history.Tweens[channel] = (layer.ClipName, -1f / Math.Max(1, layer.Frames));
                    tween = 0f;
                }
                else history.Tweens[channel] = (layer.ClipName, layer.Frame);
            }
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (start >= 0 && !DescendsFrom(i, start)) continue;
                if (tween < 1f)
                {
                    samplePos[i] = Vector3.Lerp(history!.Positions[i], samplePos[i], tween);
                    sampleRot[i] = Quaternion.Normalize(Quaternion.Slerp(history.Rotations[i], sampleRot[i], tween));
                    sampleScale[i] = Vector3.Lerp(history.Scales[i], sampleScale[i], tween);
                }
                if ((sampled[i] & 1) != 0) translations[i] = Vector3.Lerp(translations[i], samplePos[i], alpha);
                if ((sampled[i] & 2) != 0) rotations[i] = Quaternion.Normalize(Quaternion.Slerp(rotations[i], sampleRot[i], alpha));
                if ((sampled[i] & 4) != 0) scales[i] = Vector3.Lerp(scales[i], sampleScale[i], alpha);
            }
        }

        if (history is not null)
        {
            // Keep the preceding pose intact until every channel has used it for tweening, then swap buffers.
            (history.Positions, history.WorkPositions) = (translations, history.Positions);
            (history.Rotations, history.WorkRotations) = (rotations, history.Rotations);
            (history.Scales, history.WorkScales) = (scales, history.Scales);
            if (history.Layers.Length < layers.Length) history.Layers = new Layer[layers.Length];
            layers.CopyTo(history.Layers);
            history.LayerCount = layers.Length;
        }

        transforms.Clear();
        foreach (int i in _order)
        {
            Node node = _nodes[i];
            Matrix4x4 local = Matrix4x4.CreateScale(scales[i]) * Matrix4x4.CreateFromQuaternion(rotations[i]) *
                Matrix4x4.CreateTranslation(translations[i]);
            world[i] = node.Parent < 0 ? local : local * world[node.Parent];
            if (node.Name is { } name) transforms[name] = world[i];
        }
        if (history is not null)
        {
            history.Pose.Clear();
            // Enumerating IDictionary boxes a Dictionary enumerator on every sampled pose.
            if (transforms is Dictionary<string, Matrix4x4> dictionary)
                foreach (var pair in dictionary) history.Pose[pair.Key] = pair.Value;
            else
                foreach (var pair in transforms) history.Pose[pair.Key] = pair.Value;
        }
        return true;
    }

    private Matrix4x4 Global(int bone, Vector3[] positions, Quaternion[] rotations, Vector3[] scales)
    {
        Matrix4x4 result = Matrix4x4.Identity;
        for (int i = bone; i >= 0; i = _nodes[i].Parent)
            result *= Matrix4x4.CreateScale(scales[i]) * Matrix4x4.CreateFromQuaternion(rotations[i]) * Matrix4x4.CreateTranslation(positions[i]);
        return result;
    }

    /// <summary>Whether a named bone is within another bone's subtree.</summary>
    public bool IsWithin(string bone, string ancestor) => _nodeNames.TryGetValue(bone, out int child) &&
        _nodeNames.TryGetValue(ancestor, out int parent) && DescendsFrom(child, parent);

    public int BoneDepth(string bone)
    {
        if (!_nodeNames.TryGetValue(bone, out int node)) return -1;
        int depth = 0;
        for (int i = _nodes[node].Parent; i >= 0; i = _nodes[i].Parent) depth++;
        return depth;
    }

    /// <summary>Carry the visible root into a tween when root-motion locking ends.</summary>
    public void CorrectHistoryRoot(PoseHistory history, Matrix4x4 root)
    {
        if (history.Pose.Count == 0 || history.Positions.Length != _nodes.Length || !_nodeNames.TryGetValue("B", out int index)) return;
        if (_nodes[index].Parent >= 0 && Matrix4x4.Invert(Global(_nodes[index].Parent, history.Positions, history.Rotations, history.Scales), out Matrix4x4 inverse)) root *= inverse;
        if (!Matrix4x4.Decompose(root, out Vector3 scale, out Quaternion rotation, out Vector3 position)) return;
        history.Scales[index] = scale; history.Rotations[index] = rotation; history.Positions[index] = position;
        history.LayerCount = -1;
    }

    private bool Mirror(Vector3[] positions, Quaternion[] rotations, Vector3[] scales, byte[] sampled, IReadOnlyList<int>? boneMirrors, PoseHistory scratch)
    {
        Matrix4x4[] source = scratch.MirrorSource, mirrored = scratch.Mirrored;
        byte[] originalSamples = scratch.OriginalSamples;
        sampled.CopyTo(originalSamples, 0);
        foreach (int i in _order)
        {
            Matrix4x4 local = Matrix4x4.CreateScale(scales[i]) * Matrix4x4.CreateFromQuaternion(rotations[i]) * Matrix4x4.CreateTranslation(positions[i]);
            source[i] = _nodes[i].Parent < 0 ? local : local * source[_nodes[i].Parent];
        }
        Matrix4x4 meshReflection = Matrix4x4.CreateScale(-1f, 1f, 1f);
        Matrix4x4 boneReflection = Matrix4x4.CreateScale(1f, -1f, 1f);
        foreach (int i in _order)
        {
            string? name = _nodes[i].Name;
            int from = _opposites[i];
            if (boneMirrors is not null && _packageBones.Length > 0)
            {
                int packageIndex = Array.IndexOf(_packageBones, name);
                int sourceIndex = packageIndex >= 0 && packageIndex < boneMirrors.Count ? boneMirrors[packageIndex] : -1;
                from = sourceIndex >= 0 && sourceIndex < _packageBones.Length && _nodeNames.TryGetValue(_packageBones[sourceIndex], out int sourceNode) ? sourceNode : -1;
            }
            else if (name == "B") from = -1; // Both shipped pawn tables leave the root unmapped.
            int parent = _nodes[i].Parent;
            if (from < 0 || (from == i && originalSamples[i] == 0))
            {
                Matrix4x4 local = Matrix4x4.CreateScale(scales[i]) * Matrix4x4.CreateFromQuaternion(rotations[i]) * Matrix4x4.CreateTranslation(positions[i]);
                mirrored[i] = parent < 0 ? local : local * mirrored[parent];
                continue;
            }
            mirrored[i] = boneReflection * source[from] * meshReflection;
            Matrix4x4 relative = mirrored[i];
            if (parent >= 0)
            {
                if (!Matrix4x4.Invert(mirrored[parent], out Matrix4x4 inverse)) return false;
                relative *= inverse;
            }
            if (!Matrix4x4.Decompose(relative, out scales[i], out rotations[i], out positions[i])) return false;
            rotations[i] = Quaternion.Normalize(rotations[i]);
            sampled[i] = (byte)(originalSamples[from] | 3);
        }
        return true;
    }

    private bool DescendsFrom(int node, int ancestor)
    {
        for (int current = node; current >= 0; current = _nodes[current].Parent)
            if (current == ancestor) return true;
        return false;
    }

    private static bool NeededClip(string? name)
    {
        if (name is null) return false;
        if (IsRollClip(name)) return true;
        foreach (string prefix in new[] { "Wait", "Walk", "Jogg", "Run", "Turn", "Land", "Fall", "Jump" })
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static RollPoseContent Read(byte[] bytes, bool allClips)
    {
        if (bytes.Length < 20 || U32(bytes, 0) != 0x46546c67 || U32(bytes, 4) != 2 || U32(bytes, 8) != bytes.Length)
            throw Bad("Expected a complete glTF 2 binary file.");
        ReadOnlyMemory<byte> json = default;
        ReadOnlyMemory<byte> binary = default;
        for (int offset = 12; offset < bytes.Length;)
        {
            if (bytes.Length - offset < 8) throw Bad("Truncated GLB chunk header.");
            uint size = U32(bytes, offset);
            uint kind = U32(bytes, offset + 4);
            if (size > bytes.Length - offset - 8 || size % 4 != 0) throw Bad("Invalid GLB chunk length.");
            var chunk = bytes.AsMemory(offset + 8, (int)size);
            if (kind == 0x4e4f534a)
            {
                if (!json.IsEmpty || offset != 12) throw Bad("Expected one initial JSON chunk.");
                json = chunk;
            }
            else if (kind == 0x004e4942)
            {
                if (!binary.IsEmpty) throw Bad("Expected one embedded binary buffer.");
                binary = chunk;
            }
            offset += 8 + (int)size;
        }
        if (json.IsEmpty || binary.IsEmpty) throw Bad("Missing JSON or binary chunk.");
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement[] accessors = root.GetProperty("accessors").EnumerateArray().ToArray();
        JsonElement[] bufferViews = root.GetProperty("bufferViews").EnumerateArray().ToArray();
        var accessorCache = new Dictionary<(int Index, int Components), float[]>();
        float[] ReadAccessor(int index, int components)
        {
            if (!accessorCache.TryGetValue((index, components), out float[]? data))
                accessorCache.Add((index, components), data = Accessor(accessors, bufferViews, binary.Span, index, components));
            return data;
        }
        JsonElement buffers = root.GetProperty("buffers");
        if (buffers.GetArrayLength() != 1 || buffers[0].TryGetProperty("uri", out _))
            throw Bad("Roll animations require one embedded buffer.");
        int bufferLength = buffers[0].GetProperty("byteLength").GetInt32();
        if (bufferLength < 0 || bufferLength > binary.Length) throw Bad("Truncated embedded buffer.");
        binary = binary[..bufferLength];

        JsonElement sourceNodes = root.GetProperty("nodes");
        var nodes = new Node[sourceNodes.GetArrayLength()];
        var parents = new int[nodes.Length];
        Array.Fill(parents, -1);
        for (int i = 0; i < nodes.Length; i++)
        {
            if (!sourceNodes[i].TryGetProperty("children", out JsonElement children)) continue;
            foreach (JsonElement child in children.EnumerateArray())
            {
                int index = child.GetInt32();
                if (index < 0 || index >= nodes.Length || parents[index] >= 0) throw Bad("Invalid node hierarchy.");
                parents[index] = i;
            }
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Length; i++)
        {
            JsonElement node = sourceNodes[i];
            string? name = node.TryGetProperty("name", out JsonElement named) ? named.GetString() : null;
            if (name is not null && !names.Add(name)) throw Bad($"Duplicate node name '{name}'.");
            Vector3 translation = Vector(node, "translation", Vector3.Zero);
            Vector3 scale = Vector(node, "scale", Vector3.One);
            Quaternion rotation = Quaternion.Identity;
            if (node.TryGetProperty("rotation", out JsonElement rotated))
            {
                float[] r = Numbers(rotated, 4);
                rotation = UnitQuaternion(r, 0);
            }
            if (node.TryGetProperty("matrix", out JsonElement matrix))
            {
                if (node.TryGetProperty("translation", out _) || node.TryGetProperty("rotation", out _) || node.TryGetProperty("scale", out _))
                    throw Bad("Node mixes matrix and TRS transforms.");
                float[] m = Numbers(matrix, 16);
                // Column-major glTF values become the transpose used by System.Numerics row vectors.
                var rest = new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7],
                    m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
                if (!Matrix4x4.Decompose(rest, out scale, out rotation, out translation)) throw Bad("Unsupported node matrix.");
            }
            nodes[i] = new Node(name, parents[i], translation, rotation, scale);
        }

        var order = new List<int>(nodes.Length);
        var visited = new byte[nodes.Length];
        void Visit(int index)
        {
            if (visited[index] == 2) return;
            if (visited[index] == 1) throw Bad("Cycle in node hierarchy.");
            visited[index] = 1;
            if (parents[index] >= 0) Visit(parents[index]);
            visited[index] = 2;
            order.Add(index);
        }
        for (int i = 0; i < nodes.Length; i++) Visit(i);

        var clips = new Dictionary<string, Clip>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement animation in root.GetProperty("animations").EnumerateArray())
        {
            string? name = animation.TryGetProperty("name", out JsonElement named) ? named.GetString() : null;
            if (name is null || (!allClips && !NeededClip(name))) continue;
            JsonElement samplers = animation.GetProperty("samplers");
            var tracks = new List<Track>();
            var targets = new HashSet<(int, string)>();
            float duration = 0f;
            foreach (JsonElement channel in animation.GetProperty("channels").EnumerateArray())
            {
                JsonElement target = channel.GetProperty("target");
                int node = target.GetProperty("node").GetInt32();
                string path = target.GetProperty("path").GetString() ?? "";
                if (node < 0 || node >= nodes.Length || !targets.Add((node, path))) throw Bad("Invalid animation target.");
                if (path is not ("translation" or "rotation" or "scale")) throw Bad($"Unsupported roll track '{path}'.");
                if (sourceNodes[node].TryGetProperty("matrix", out _)) throw Bad("Animated node must use TRS transforms.");
                JsonElement sampler = At(samplers, channel.GetProperty("sampler").GetInt32());
                if (sampler.TryGetProperty("interpolation", out JsonElement interpolation) && interpolation.GetString() != "LINEAR")
                    throw Bad("Roll tracks require LINEAR interpolation.");
                int components = path == "rotation" ? 4 : 3;
                float[] times = ReadAccessor(sampler.GetProperty("input").GetInt32(), 1);
                float[] values = ReadAccessor(sampler.GetProperty("output").GetInt32(), components);
                if (times.Length == 0 || values.Length / components != times.Length) throw Bad("Mismatched animation samples.");
                for (int i = 0; i < times.Length; i++)
                {
                    if (times[i] < 0f || (i > 0 && times[i] <= times[i - 1])) throw Bad("Animation times must increase.");
                    if (path == "rotation")
                    {
                        Quaternion q = UnitQuaternion(values, i * 4);
                        values[i * 4] = q.X; values[i * 4 + 1] = q.Y; values[i * 4 + 2] = q.Z; values[i * 4 + 3] = q.W;
                    }
                }
                duration = Math.Max(duration, times[^1]);
                tracks.Add(new Track(node, path, times, values, components));
            }
            if (tracks.Count == 0 || !clips.TryAdd(name!, new Clip(duration, tracks.ToArray()))) throw Bad("Empty or duplicate roll clip.");
        }
        FRotator rotOrigin = default;
        FVector meshScale = new(1f, 1f, 1f);
        if (root.TryGetProperty("extras", out JsonElement extras) && extras.TryGetProperty("svmap", out JsonElement svmap))
        {
            if (svmap.TryGetProperty("rotOrigin", out JsonElement rotation))
            {
                if (rotation.GetArrayLength() != 3) throw Bad("Invalid svmap.rotOrigin.");
                rotOrigin = new FRotator(rotation[0].GetInt32(), rotation[1].GetInt32(), rotation[2].GetInt32());
            }
            Vector3 scale = Vector(svmap, "meshScale", Vector3.One);
            meshScale = new FVector(scale.X, scale.Y, scale.Z);
        }
        var content = new RollPoseContent(nodes, order.ToArray(), clips, rotOrigin, meshScale);
        // BoneMirrors indexes the mesh's reference skeleton, retained in the skin joints. Animation packages
        // omit mesh-only helper bones, so their timing JSON's bone order cannot be used for this table.
        if (root.TryGetProperty("skins", out JsonElement skins) && skins.GetArrayLength() > 0)
            content._packageBones = skins[0].GetProperty("joints").EnumerateArray().Select(joint =>
            {
                int index = joint.GetInt32();
                if ((uint)index >= (uint)nodes.Length) throw Bad("Invalid skin joint.");
                return nodes[index].Name ?? "";
            }).ToArray();
        return content;
    }

    private static float[] Accessor(JsonElement[] accessors, JsonElement[] views, ReadOnlySpan<byte> binary, int index, int components)
    {
        if ((uint)index >= (uint)accessors.Length) throw Bad("Invalid accessor index.");
        JsonElement accessor = accessors[index];
        string expected = components == 1 ? "SCALAR" : components == 3 ? "VEC3" : "VEC4";
        if (accessor.GetProperty("componentType").GetInt32() != 5126 || accessor.GetProperty("type").GetString() != expected ||
            accessor.TryGetProperty("sparse", out _) || (accessor.TryGetProperty("normalized", out JsonElement normalized) && normalized.GetBoolean()))
            throw Bad("Roll tracks require ordinary floating-point accessors.");
        int viewIndex = accessor.GetProperty("bufferView").GetInt32();
        if ((uint)viewIndex >= (uint)views.Length) throw Bad("Invalid buffer view index.");
        JsonElement view = views[viewIndex];
        if (view.GetProperty("buffer").GetInt32() != 0) throw Bad("Invalid animation buffer.");
        int count = accessor.GetProperty("count").GetInt32();
        int size = components * 4;
        int stride = Int(view, "byteStride", size);
        int viewOffset = Int(view, "byteOffset", 0);
        int viewLength = view.GetProperty("byteLength").GetInt32();
        int offset = Int(accessor, "byteOffset", 0);
        long end = count > 0 ? (long)offset + (long)(count - 1) * stride + size : offset;
        if (count < 0 || stride < size || stride % 4 != 0 || viewOffset < 0 || viewLength < 0 || offset < 0 ||
            offset % 4 != 0 || (long)viewOffset + viewLength > binary.Length || end > viewLength)
            throw Bad("Animation accessor is outside its buffer view.");
        var result = new float[checked(count * components)];
        for (int i = 0; i < count; i++)
        {
            for (int c = 0; c < components; c++)
            {
                float value = BinaryPrimitives.ReadSingleLittleEndian(binary.Slice(viewOffset + offset + i * stride + c * 4, 4));
                if (!float.IsFinite(value)) throw Bad("Non-finite animation value.");
                result[i * components + c] = value;
            }
        }
        return result;
    }

    private static Quaternion UnitQuaternion(float[] values, int offset)
    {
        var q = new Quaternion(values[offset], values[offset + 1], values[offset + 2], values[offset + 3]);
        if (!(q.LengthSquared() > 1e-12f) || !float.IsFinite(q.LengthSquared())) throw Bad("Invalid rotation quaternion.");
        return Quaternion.Normalize(q);
    }

    private static Vector3 Vector(JsonElement node, string name, Vector3 fallback)
    {
        if (!node.TryGetProperty(name, out JsonElement element)) return fallback;
        float[] v = Numbers(element, 3);
        return new Vector3(v[0], v[1], v[2]);
    }

    private static float[] Numbers(JsonElement element, int count)
    {
        if (element.GetArrayLength() != count) throw Bad("Invalid node transform.");
        var result = new float[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = element[i].GetSingle();
            if (!float.IsFinite(result[i])) throw Bad("Non-finite node transform.");
        }
        return result;
    }

    private static JsonElement At(JsonElement array, int index) => index >= 0 && index < array.GetArrayLength()
        ? array[index] : throw Bad("Invalid animation data index.");
    private static int Int(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetInt32() : fallback;
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static InvalidDataException Bad(string message) => new(message);
}
