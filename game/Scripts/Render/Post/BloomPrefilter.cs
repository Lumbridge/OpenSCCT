using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

namespace OpenScctGame.Render.Post;

/// <summary>Opt-in scene prefilter before the faithful bloom threshold. Owns no scene textures.</summary>
internal sealed class BloomPrefilter
{
    // Measurement switch: 0 = original sampling, 13tap = filtered, karis = filtered with anti-firefly weighting.
    private static readonly string? Override = System.Environment.GetEnvironmentVariable("SVM_BLOOM_FILTER");
    public static bool Filtering { get; set; }
    private static string Mode => Override ?? (Filtering ? "13tap" : "0");
    public static bool Enabled => Mode is "13tap" or "karis";
    private Rid _shader, _pipeline, _sampler, _source;
    private Vector2I _size, _sourceSize;
    private readonly List<(Rid Texture, Rid Set, Vector2I Size)> _levels = [];
    private readonly byte[] _push = new byte[32];
    private bool _failed;
    private bool _reported;

    public Rid Filter(RenderingDevice rd, Rid source, Vector2I target)
    {
        if (!Enabled || _failed) return source;
        if (!_shader.IsValid)
        {
            if (!ComputeShaderSource.AcquireShared(rd, "res://Shaders/svm_bloom_prefilter.glsl", "svm_bloom_prefilter", out _shader, out _pipeline, out string? error))
            {
                _failed = true;
                GD.PushError("Bloom prefilter: " + error);
                return source;
            }
            _sampler = rd.SamplerCreate(new RDSamplerState
            {
                MinFilter = RenderingDevice.SamplerFilter.Linear, MagFilter = RenderingDevice.SamplerFilter.Linear,
                RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            });
        }
        using RDTextureFormat format = rd.TextureGetFormat(source);
        Vector2I sourceSize = new((int)format.Width, (int)format.Height);
        if (_source != source || _size != target || _sourceSize != sourceSize)
        {
            FreeLevels(rd);
            _source = source; _size = target; _sourceSize = sourceSize;
            Vector2I size = sourceSize;
            Rid input = source;
            // Avoid a large one-step reduction at high resolutions. The last pass reaches the existing bright target.
            do
            {
                size = new Vector2I(Math.Max(target.X, (size.X + 1) / 2), Math.Max(target.Y, (size.Y + 1) / 2));
                Rid texture = rd.TextureCreate(new RDTextureFormat
                {
                    Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat, Width = (uint)size.X, Height = (uint)size.Y,
                    UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit,
                }, new RDTextureView());
                var read = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 };
                read.AddId(_sampler); read.AddId(input);
                var write = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
                write.AddId(texture);
                Rid set = rd.UniformSetCreate([read, write], _shader, 0);
                _levels.Add((texture, set, size));
                input = texture;
            } while (size != target);
            if (!_reported)
            {
                GD.Print($"SVM_BLOOM_FILTER mode={Mode} source={sourceSize} target={target} stages={_levels.Count}");
                _reported = true;
            }
        }
        rd.CaptureTimestamp("Bloom Prefilter");
        long list = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(list, _pipeline);
        Vector2I previous = sourceSize;
        for (int i = 0; i < _levels.Count; i++)
        {
            var level = _levels[i];
            Span<float> values = [1f / previous.X, 1f / previous.Y, level.Size.X, level.Size.Y,
                i == 0 && Mode == "karis" ? 1f : 0f, 0f, 0f, 0f];
            MemoryMarshal.AsBytes(values).CopyTo(_push);
            rd.ComputeListBindUniformSet(list, level.Set, 0);
            rd.ComputeListSetPushConstant(list, _push, (uint)_push.Length);
            rd.ComputeListDispatch(list, (uint)((level.Size.X + 7) / 8), (uint)((level.Size.Y + 7) / 8), 1);
            if (i + 1 < _levels.Count) rd.ComputeListAddBarrier(list);
            previous = level.Size;
        }
        rd.ComputeListEnd();
        rd.CaptureTimestamp("Bloom Prefilter End");
        return _levels[^1].Texture;
    }

    private void FreeLevels(RenderingDevice rd)
    {
        foreach (var level in _levels)
        {
            if (rd.UniformSetIsValid(level.Set)) rd.FreeRid(level.Set);
            rd.FreeRid(level.Texture);
        }
        _levels.Clear();
    }

    public void Free(RenderingDevice rd)
    {
        FreeLevels(rd);
        if (_sampler.IsValid) rd.FreeRid(_sampler);
        ComputeShaderSource.ReleaseShared(rd, _shader);
        _sampler = _shader = _pipeline = _source = default;
    }
}
