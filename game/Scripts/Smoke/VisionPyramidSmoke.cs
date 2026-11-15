using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using OpenScctGame.Render;

namespace OpenScctGame.Smoke;

/// <summary>GPU comparison of hardware-filtered vision input with the existing manual bilinear reference.</summary>
public sealed class VisionPyramidSmoke : SmokeTest
{
    public const string TestName = "vision-pyramid";
    public override string Name => TestName;
    private bool _started;
    private volatile bool _done;
    private bool _passed;
    private string _detail = "";

    public override SmokeResult Step(SmokeContext context)
    {
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("GPU filtering check requires a rendered window");
        if (!_started) { _started = true; RenderingServer.CallOnRenderThread(Callable.From(Check)); }
        return !_done ? SmokeResult.Running : _passed ? SmokeResult.Pass(_detail) : SmokeResult.Fail(_detail);
    }

    private void Check()
    {
        RenderingDevice? rd = RenderingServer.GetRenderingDevice();
        if (rd is null) { _detail = "No rendering device"; _done = true; return; }
        var owned = new List<Rid>(); Rid shader = default;
        try
        {
            if (!ComputeShaderSource.AcquireShared(rd, "res://Shaders/svm_vision_blur.glsl", "vision pyramid reference", out shader, out Rid pipeline, out string? error))
                throw new InvalidOperationException(error);
            const int width = 47, height = 31, outWidth = 95, outHeight = 47;
            var pixels = new Half[width * height * 4];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 4;
                pixels[at] = (Half)((x / 3 + y / 5) % 2 == 0 ? 1f : .1f);
                pixels[at+1] = (Half)((float)x / (width-1));
                pixels[at+2] = (Half)((float)y / (height-1)); pixels[at+3] = (Half)1;
            }
            RDTextureFormat Format(int w, int h) => new() { Width = (uint)w, Height = (uint)h, Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
                UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyFromBit };
            Rid source = rd.TextureCreate(Format(width,height), new RDTextureView(), [MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray()]); owned.Add(source);
            Rid manual = rd.TextureCreate(Format(outWidth,outHeight), new RDTextureView()); owned.Add(manual);
            Rid sampled = rd.TextureCreate(Format(outWidth,outHeight), new RDTextureView()); owned.Add(sampled);
            Rid sampler = rd.SamplerCreate(new RDSamplerState { MagFilter = RenderingDevice.SamplerFilter.Linear, MinFilter = RenderingDevice.SamplerFilter.Linear,
                RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge, MaxLod = 0 }); owned.Add(sampler);
            Rid Set(Rid target)
            {
                var read = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 }; read.AddId(sampler); read.AddId(source);
                var write = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 1 }; write.AddId(target);
                var image = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 2 }; image.AddId(source);
                Rid set = rd.UniformSetCreate([read,write,image],shader,0); owned.Add(set); return set;
            }
            Rid manualSet = Set(manual), sampledSet = Set(sampled);
            long list = rd.ComputeListBegin(); rd.ComputeListBindComputePipeline(list,pipeline);
            for (int mode = 0; mode < 2; mode++)
            {
                float[] values = [0,1,width,height,outWidth,outHeight,RenderK.BlurNearWeight,RenderK.BlurFarWeight,mode,0,0,0];
                byte[] bytes = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
                rd.ComputeListBindUniformSet(list,mode == 0 ? sampledSet : manualSet,0);
                rd.ComputeListSetPushConstant(list,bytes,(uint)bytes.Length);
                rd.ComputeListDispatch(list,(uint)((outWidth+7)/8),(uint)((outHeight+7)/8),1);
            }
            rd.ComputeListEnd();
            byte[] before = rd.TextureGetData(manual,0), after = rd.TextureGetData(sampled,0);
            float peak = 0; double sum = 0;
            for (int i = 0; i < before.Length; i += 2)
            {
                float delta = MathF.Abs((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(before,i)) - (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(after,i)));
                peak = MathF.Max(peak,delta); sum += delta;
            }
            double mean = sum / (before.Length/2);
            _passed = peak < .004f && mean < .001;
            _detail = $"hardware/manual 4-tap bilinear blur: peak={peak:F6}, mean={mean:F6}; fractional upscale coordinates, checker edges, ramps and clamp borders";
        }
        catch (Exception ex) { _detail = ex.Message; }
        finally
        {
            for (int i = owned.Count-1; i >= 0; i--) if (owned[i].IsValid) rd.FreeRid(owned[i]);
            if (shader.IsValid) ComputeShaderSource.ReleaseShared(rd,shader);
            _done = true;
        }
    }
}
