using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render.Materials;

public sealed partial class MaterialCompiler
{
    private static readonly List<WeakReference<MaterialCompiler>> s_sequenceCompilers = [];
    private static readonly (string Slot, string Prefix)[] SequenceSlots = [("diffuse", "d"), ("self_illum", "si"), ("self_illum_mask", "sim")];
    private readonly Dictionary<string, MaterialSequenceClock> _sequenceClocks = new(StringComparer.Ordinal);
    private readonly List<SequenceBinding> _sequences = [];
    private readonly List<SequenceStateBinding> _sequenceStates = [];
    private float _sequenceTime;

    private sealed class SequenceBinding(ShaderMaterial material, string prefix, MaterialSequenceEntry sequence, MaterialSequenceClock clock)
    {
        public ShaderMaterial Material { get; } = material;
        public string Prefix { get; } = prefix;
        public MaterialSequenceEntry Sequence { get; } = sequence;
        public MaterialSequenceClock Clock { get; } = clock;
        public MaterialSequenceSample Last = new(-1, -1, -1f);
    }

    private void BindSequences(ShaderMaterial material, MaterialEntry entry)
    {
        foreach ((string slot, string prefix) in SequenceSlots)
        {
            if (!entry.Sequences.TryGetValue(slot, out MaterialSequenceEntry? sequence) || sequence.Items.Length == 0) continue;
            if (!_sequenceClocks.TryGetValue(sequence.Source, out MaterialSequenceClock? clock))
            {
                clock = new(sequence.Items.Select(i => i.Timing).ToArray(), sequence.Loop, sequence.Paused);
                _sequenceClocks[sequence.Source] = clock;
            }
            var binding = new SequenceBinding(material, "seq_" + prefix, sequence, clock);
            _sequences.Add(binding);
            UpdateSequence(binding, _sequenceTime);
        }
    }

    private void UpdateSequences(float seconds)
    {
        _sequenceTime = seconds;
        foreach (SequenceBinding binding in _sequences) UpdateSequence(binding, seconds);
        foreach (SequenceStateBinding binding in _sequenceStates) UpdateSequenceState(binding, seconds);
    }

    private sealed class SequenceStateBinding(ShaderMaterial material, ShaderMaterial[] frames, MaterialSequenceClock clock)
    {
        public ShaderMaterial Material { get; } = material;
        public ShaderMaterial[] Frames { get; } = frames;
        public MaterialSequenceClock Clock { get; } = clock;
        public int Last = -1;
    }

    private ShaderMaterial ComposeSequence(MaterialEntry owner, MaterialSequenceEntry sequence, LightSource light, bool seeThroughPass, bool billboard)
    {
        if (sequence.Items.Length == 0 || sequence.Items.Any(item => item.Material is null || item.Timing.Fade))
            throw new InvalidOperationException($"MaterialSequence {sequence.Source} needs complete Show frame states");
        if (!_sequenceClocks.TryGetValue(sequence.Source, out MaterialSequenceClock? clock))
        {
            clock = new(sequence.Items.Select(item => item.Timing).ToArray(), sequence.Loop, sequence.Paused);
            _sequenceClocks[sequence.Source] = clock;
        }
        // Surface lighting survives a selected frame, but the first frame's own unlit/SI flag must not force
        // every later frame to fullbright. An actor's explicit bUnlit is encoded by the exporter suffix.
        if (owner.Name.EndsWith("__unlit", StringComparison.Ordinal)) light = LightSource.None;
        ShaderMaterial[] frames = sequence.Items.Select(item =>
        {
            MaterialEntry frame = item.Material!.WithLightmap(owner.Lightmap);
            return frame.Skip ? new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; void fragment() { discard; }" } }
                : Compose(frame, light, seeThroughPass, billboard);
        }).ToArray();
        var material = new ShaderMaterial { ResourceName = owner.Name };
        var binding = new SequenceStateBinding(material, frames, clock);
        _sequenceStates.Add(binding);
        UpdateSequenceState(binding, _sequenceTime);
        return material;
    }

    private static void UpdateSequenceState(SequenceStateBinding binding, float seconds)
    {
        int frame = binding.Clock.Update(seconds).Current;
        if (frame == binding.Last) return;
        ShaderMaterial source = binding.Frames[frame], target = binding.Material;
        if (target.Shader is { } previous)
            foreach (var uniform in previous.GetShaderUniformList())
                target.SetShaderParameter(uniform.AsGodotDictionary()["name"].AsStringName(), default);
        target.Shader = source.Shader;
        target.NextPass = source.NextPass;
        foreach (var uniform in source.Shader.GetShaderUniformList())
        {
            StringName name = uniform.AsGodotDictionary()["name"].AsStringName();
            target.SetShaderParameter(name, source.GetShaderParameter(name));
        }
        binding.Last = frame;
    }

    private void UpdateSequence(SequenceBinding binding, float seconds)
    {
        MaterialSequenceSample sample = binding.Clock.Update(seconds);
        if (sample.Previous != binding.Last.Previous) BindSequenceFrame(binding, sample.Previous, 0);
        if (sample.Current != binding.Last.Current) BindSequenceFrame(binding, sample.Current, 1);
        if (sample.Blend != binding.Last.Blend) binding.Material.SetShaderParameter(binding.Prefix + "_mix", sample.Blend);
        binding.Last = sample;
    }

    private void BindSequenceFrame(SequenceBinding binding, int index, int input)
    {
        MaterialSequenceFrame frame = binding.Sequence.Items[index];
        string prefix = binding.Prefix + input;
        ShaderMaterial material = binding.Material;
        SetTexture(material, prefix + "_tex", _textures.Get(frame.Flipbook?.Texture ?? frame.Layer.Texture, true));
        SetLayer(material, prefix, frame.Layer);
        // Unlike immutable layers, a sequence can move from an animated input back to a still texture.
        material.SetShaderParameter(prefix + "_anim_type", frame.Layer.Anim?.Code ?? 0);
        UvAffine post = frame.Layer.Post ?? UvAffine.Identity;
        material.SetShaderParameter(prefix + "_post_u", post.RowU);
        material.SetShaderParameter(prefix + "_post_v", post.RowV);
        FlipbookEntry? fb = frame.Flipbook;
        material.SetShaderParameter(prefix + "_flip", fb is null ? new Vector4(1f, 1f, 1f, 0f) : new Vector4(fb.Cols, fb.Rows, fb.Frames, fb.Fps));
    }

    /// <summary>MaterialTrigger actions use the source material path; shared variants advance on the same clock.</summary>
    public void TriggerSequence(string source, int action)
    {
        if (_sequenceClocks.TryGetValue(source, out MaterialSequenceClock? clock)) clock.Trigger(action);
    }
}
