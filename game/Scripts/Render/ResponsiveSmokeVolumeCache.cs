using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Godot;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Render;

/// <summary>Integrates one cumulative view volume before surface shading; bullet silhouettes retain the exact path.</summary>
internal static class ResponsiveSmokeVolumeCache
{
    private static RenderingDevice? s_rd;
    private static Rid s_shader, s_pipeline, s_nearest, s_linear, s_repeat, s_output, s_history;
    private static Texture2Drd? s_texture;
    private static UniformSlots? s_uniforms;
    private static Vector2I s_size;
    private static bool s_initialized;
    private static string? s_source;
    private static bool s_hasHistory;
    private static int s_frameNumber;
    public static string? Error { get; private set; }
    public static double LastGpuMilliseconds { get; private set; }
    /// <summary>Cumulative output/history texture capacity allocations, excluding ordinary draws.</summary>
    public static long AllocationCount { get; private set; }
    public static long AllocatedBytes { get; private set; }
    public static long Dispatches { get; private set; }
    internal static bool HasResources => s_initialized || s_output.IsValid || s_history.IsValid ||
        s_pipeline.IsValid || s_shader.IsValid || s_nearest.IsValid || s_linear.IsValid || s_repeat.IsValid;

    public static void Prewarm()
    {
        PrepareSource();
        RenderingServer.CallOnRenderThread(Callable.From(Init));
    }

    private static void PrepareSource()
    {
        if (s_source is not null) return;
        static string Expand(string path) => Regex.Replace(ResourceLoader.Load<ShaderInclude>(path).Code,
            "#include\\s+\"([^\"]+)\"", match => Expand(match.Groups[1].Value));
        string source = Expand("res://Shaders/svm_responsive_smoke.gdshaderinc");
        source = Regex.Replace(source, @"global uniform [^;]+;", "");
        s_source = Header + source + Main;
    }

    public static void Queue(ImageTexture fields, ImageTexture frame, ImageTexture growth, ImageTexture3D shape, ImageTexture probes, int[] slots, Vector2I size, int[] sceneSlots)
    {
        if (slots.Length == 0 && sceneSlots.Length == 0) { Idle(); return; }
        if (Error is not null) return;
        PrepareSource();
        // Values are rendering resources, not scene objects; their RD identities are resolved only on the render thread.
        var (spots,depth,cookies,spotCount) = SpotShadowLight.VolumeInputs();
        Rid f = fields.GetRid(), p = frame.GetRid(), g = growth.GetRid(), s = spots.GetRid(), d = depth.GetRid(), c = cookies.GetRid();
        Rid n = shape.GetRid();
        Rid lighting = probes.GetRid();
        RenderingServer.CallOnRenderThread(Callable.From(() => Draw(f,p,g,n,s,d,c,slots,size,spotCount,sceneSlots,lighting)));
    }

    private static void Init()
    {
        if (s_initialized) return;
        s_initialized = true;
        s_rd = RenderingServer.GetRenderingDevice();
        if (s_rd is null) { Error = "Responsive smoke requires a RenderingDevice."; return; }
        using var source = new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = s_source! };
        using RDShaderSpirV spirv = s_rd.ShaderCompileSpirVFromSource(source);
        if (!string.IsNullOrEmpty(spirv.CompileErrorCompute)) { Error = spirv.CompileErrorCompute; GD.PushError(Error); return; }
        s_shader = s_rd.ShaderCreateFromSpirV(spirv,"responsive_smoke_prefix");
        s_pipeline = s_rd.ComputePipelineCreate(s_shader);
        if (!s_shader.IsValid || !s_pipeline.IsValid)
        { Error = "Could not create the responsive smoke compute pipeline."; GD.PushError(Error); return; }
        s_nearest = s_rd.SamplerCreate(new RDSamplerState { MinFilter = RenderingDevice.SamplerFilter.Nearest,
            MagFilter = RenderingDevice.SamplerFilter.Nearest, RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge });
        s_linear = s_rd.SamplerCreate(new RDSamplerState { MinFilter = RenderingDevice.SamplerFilter.Linear,
            MagFilter = RenderingDevice.SamplerFilter.Linear, RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge });
        s_repeat = s_rd.SamplerCreate(new RDSamplerState { MinFilter = RenderingDevice.SamplerFilter.Linear,
            MagFilter = RenderingDevice.SamplerFilter.Linear, RepeatU = RenderingDevice.SamplerRepeatMode.Repeat,
            RepeatV = RenderingDevice.SamplerRepeatMode.Repeat, RepeatW = RenderingDevice.SamplerRepeatMode.Repeat });
        s_uniforms = new UniformSlots(RenderingDevice.UniformType.SamplerWithTexture,RenderingDevice.UniformType.SamplerWithTexture,
            RenderingDevice.UniformType.SamplerWithTexture,RenderingDevice.UniformType.SamplerWithTexture,
            RenderingDevice.UniformType.SamplerWithTexture,RenderingDevice.UniformType.SamplerWithTexture,RenderingDevice.UniformType.Image,
            RenderingDevice.UniformType.SamplerWithTexture,RenderingDevice.UniformType.SamplerWithTexture,
            RenderingDevice.UniformType.SamplerWithTexture,RenderingDevice.UniformType.SamplerWithTexture);
    }

    private static void Draw(Rid fields, Rid frame, Rid growth, Rid shape, Rid spots, Rid depth, Rid cookies, int[] slots, Vector2I size, int spotCount, int[] sceneSlots, Rid probes)
    {
        Init();
        if (s_rd is not { } rd || !s_pipeline.IsValid || Error is not null) return;
        ResponsiveSmokeVisualFlow.SampleGpuTiming(rd);
        LastGpuMilliseconds = 0;
        ulong first = 0;
        for (uint i = 0; i < rd.GetCapturedTimestampsCount(); i++)
        {
            string name = rd.GetCapturedTimestampName(i);
            if (name == "ResponsiveSmokeBegin") first = rd.GetCapturedTimestampGpuTime(i);
            else if (name == "ResponsiveSmokeEnd" && first != 0)
            {
                LastGpuMilliseconds += (rd.GetCapturedTimestampGpuTime(i)-first)/1_000_000.0;
                first = 0;
            }
        }
        // Capacity survives an enabled match's quiet periods and smaller view layouts. Every active tile is
        // overwritten, and textureSize supplies the retained atlas dimensions to both sampling paths.
        bool resized = s_size.X < size.X || s_size.Y < size.Y || !s_output.IsValid;
        if (resized)
        {
            if (s_texture is not null) s_texture.TextureRdRid = default;
            if (s_output.IsValid) rd.FreeRid(s_output);
            if (s_history.IsValid) rd.FreeRid(s_history);
            AllocatedBytes = 0;
            using var format = new RDTextureFormat { Width = (uint)size.X, Height = (uint)size.Y,
                Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
                UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit
                    | RenderingDevice.TextureUsageBits.CanCopyFromBit | RenderingDevice.TextureUsageBits.CanCopyToBit };
            using var view = new RDTextureView();
            s_output = rd.TextureCreate(format,view);
            s_history = rd.TextureCreate(format,view);
            if (s_output.IsValid) { AllocationCount++; AllocatedBytes += (long)size.X*size.Y*8; }
            if (s_history.IsValid) { AllocationCount++; AllocatedBytes += (long)size.X*size.Y*8; }
            s_hasHistory = false;
            if (!s_output.IsValid || !s_history.IsValid)
            {
                Error = $"Could not allocate the responsive smoke view volume ({size.X} x {size.Y}).";
                RenderingServer.GlobalShaderParameterSet("svm_smoke_prefix_ready",false);
                GD.PushError(Error);
                return;
            }
            s_size = size;
            s_texture ??= new Texture2Drd();
            s_texture.TextureRdRid = s_output;
            RenderingServer.GlobalShaderParameterSet("svm_smoke_prefix",s_texture);
        }
        UniformSlots uniforms = s_uniforms!;
        uniforms.Set(0,s_nearest,RenderingServer.TextureGetRdTexture(fields));
        uniforms.Set(1,s_nearest,RenderingServer.TextureGetRdTexture(frame));
        uniforms.Set(2,s_linear,RenderingServer.TextureGetRdTexture(growth));
        uniforms.Set(3,s_nearest,RenderingServer.TextureGetRdTexture(spots));
        uniforms.Set(4,s_nearest,RenderingServer.TextureGetRdTexture(depth));
        uniforms.Set(5,s_linear,RenderingServer.TextureGetRdTexture(cookies));
        uniforms.Set(6,s_output);
        uniforms.Set(7,s_linear,s_history);
        uniforms.Set(8,s_linear,ResponsiveSmokeVisualFlow.StateRid);
        uniforms.Set(9,s_repeat,RenderingServer.TextureGetRdTexture(shape));
        uniforms.Set(10,s_linear,RenderingServer.TextureGetRdTexture(probes));
        Rid set = UniformSetCacheRD.GetCache(s_shader,0,uniforms.Array);
        rd.CaptureTimestamp("ResponsiveSmokeBegin");
        long list = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(list,s_pipeline);
        rd.ComputeListBindUniformSet(list,set,0);
        for (int i = 0; i < sceneSlots.Length; i += 3)
        {
            if (!resized && sceneSlots[i+2] == 0) continue;
            int[] parameters = [sceneSlots[i],sceneSlots[i+1],-1,0];
            byte[] bytes = MemoryMarshal.AsBytes(parameters.AsSpan()).ToArray();
            rd.ComputeListSetPushConstant(list,bytes,(uint)bytes.Length);
            rd.ComputeListDispatch(list,ResponsiveSmokeRenderer.SceneShadowGrid,
                (uint)((ResponsiveSmokeRenderer.SceneShadowGrid * ResponsiveSmokeRenderer.SceneShadowRows + 7) / 8),1);
            Dispatches++;
        }
        for (int i = 0; i < slots.Length; i += 3)
        {
            int[] parameters = [slots[i],spotCount,s_hasHistory ? 1 : 0,s_frameNumber];
            byte[] bytes = MemoryMarshal.AsBytes(parameters.AsSpan()).ToArray();
            rd.ComputeListSetPushConstant(list,bytes,(uint)bytes.Length);
            rd.ComputeListDispatch(list,(uint)((slots[i+1]+7)/8),(uint)((slots[i+2]+7)/8),1);
            Dispatches++;
        }
        rd.ComputeListEnd();
        // Keep the exported RenderingServer texture identity stable. Reassigning TextureRdRid on every
        // ping-pong swap calls texture_replace in Godot and invalidates materials throughout the map.
        // A GPU copy retains last frame's history without rebuilding those resource bindings.
        Godot.Error copied = rd.TextureCopy(s_output,s_history,Vector3.Zero,Vector3.Zero,new(s_size.X,s_size.Y,1),0,0,0,0);
        rd.CaptureTimestamp("ResponsiveSmokeEnd");
        if (copied != Godot.Error.Ok)
        {
            Error = $"Could not preserve responsive smoke history: {copied}";
            s_hasHistory = false;
            RenderingServer.GlobalShaderParameterSet("svm_smoke_prefix_ready",false);
            GD.PushError(Error);
            return;
        }
        RenderingServer.GlobalShaderParameterSet("svm_smoke_prefix_ready",true);
        s_hasHistory = true;
        s_frameNumber++;
    }

    /// <summary>No dispatch while idle; keep the current viewport allocation but reject its stale history.</summary>
    public static void Idle()
    {
        LastGpuMilliseconds = 0;
        RenderingServer.GlobalShaderParameterSet("svm_smoke_prefix_ready",false);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            RenderingServer.GlobalShaderParameterSet("svm_smoke_prefix_ready",false);
            s_hasHistory = false;
            LastGpuMilliseconds = 0;
        }));
    }

    public static void Release(bool retainPipeline = false)
    {
        // Initialization is queued too. Always enqueue cleanup after it, even if it has not run yet.
        LastGpuMilliseconds = 0;
        RenderingServer.GlobalShaderParameterSet("svm_smoke_prefix_ready",false);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            RenderingServer.GlobalShaderParameterSet("svm_smoke_prefix_ready",false);
            LastGpuMilliseconds = 0;
            if (s_rd is not { } rd) return;
            if (s_texture is not null) s_texture.TextureRdRid = default;
            if (retainPipeline)
            {
                if (s_output.IsValid) rd.FreeRid(s_output);
                if (s_history.IsValid) rd.FreeRid(s_history);
                s_output = s_history = default; s_size = default; s_hasHistory = false;
                AllocatedBytes = 0;
                return;
            }
            foreach (Rid rid in new[] {s_output,s_history,s_pipeline,s_shader,s_nearest,s_linear,s_repeat}) if (rid.IsValid) rd.FreeRid(rid);
            s_output = s_history = s_pipeline = s_shader = s_nearest = s_linear = s_repeat = default;
            AllocatedBytes = 0;
            s_hasHistory = false;
            s_rd = null; s_initialized = false; s_size = default;
        }));
    }

    private const string Header = """
        #version 450
        #define SVM_SMOKE_COMPUTE
        layout(local_size_x=8,local_size_y=8,local_size_z=1) in;
        layout(set=0,binding=0) uniform sampler2D svm_smoke_fields;
        layout(set=0,binding=1) uniform sampler2D svm_smoke_frame;
        layout(set=0,binding=2) uniform sampler2D svm_smoke_growth;
        layout(set=0,binding=3) uniform sampler2D svm_spots;
        layout(set=0,binding=4) uniform sampler2D svm_spot_depth;
        layout(set=0,binding=5) uniform sampler2DArray svm_spot_cookies;
        layout(rgba16f,set=0,binding=6) uniform writeonly image2D smoke_prefix_image;
        layout(set=0,binding=7) uniform sampler2D smoke_history;
        layout(set=0,binding=8) uniform sampler3D svm_smoke_flow;
        layout(set=0,binding=9) uniform sampler3D svm_smoke_shape;
        layout(set=0,binding=10) uniform sampler2D svm_smoke_probes;
        #define svm_smoke_flow_ready true
        layout(push_constant,std430) uniform Parameters { ivec4 dispatch; } pc;
        #define svm_spot_count pc.dispatch.y

        """;

    private const string Main = """

        vec4 history_slice(vec2 uv, int slice, vec4 grid) {
            vec2 size = vec2(textureSize(smoke_history,0));
            vec2 local = clamp(uv*grid.yz,vec2(.5),grid.yz-vec2(.5));
            vec2 tile = vec2(float(slice%8)*grid.y,grid.x+float(slice/8)*grid.z);
            return textureLod(smoke_history,(tile+local)/size,0.0);
        }

        vec4 resolve_history(int view, vec3 world_position, vec4 current) {
            vec4 old_eye = smoke_frame_row(view+8);
            if (pc.dispatch.z == 0 || old_eye.w < .5 || current.a > .99) return current;
            vec4 right = smoke_frame_row(view+9), up = smoke_frame_row(view+10), forward = smoke_frame_row(view+11);
            vec4 grid = smoke_frame_row(view+12), depth = smoke_frame_row(view+13);
            vec3 relative = world_position-old_eye.xyz;
            float z = dot(relative,forward.xyz);
            if (z <= 0.0 || z < depth.x || z > depth.y) return current;
            vec2 uv = vec2(.5)+vec2(dot(relative,right.xyz)*right.w,-dot(relative,up.xyz)*up.w)/(2.0*z);
            if (any(lessThan(uv,vec2(0.0))) || any(greaterThan(uv,vec2(1.0)))) return current;
            float layer = clamp((z-depth.x)/max(depth.y-depth.x,.001),0.0,1.0)*(grid.w-1.0);
            int low = int(floor(layer)), high = min(low+1,int(grid.w)-1);
            vec4 history = mix(history_slice(uv,low,grid),history_slice(uv,high,grid),fract(layer));
            if (abs(history.a-current.a) > .025 || history.a > .99) return current;
            // Only lighting is reconstructed. Current extinction always wins, including new holes and walls.
            vec3 old_light = history.rgb*((1.0-current.a)/max(1.0-history.a,.001));
            old_light = clamp(old_light,current.rgb-vec3(.035),current.rgb+vec3(.035));
            return vec4(mix(current.rgb,old_light,.8),current.a);
        }

        void main() {
            int view = pc.dispatch.x*14;
            vec4 frame = smoke_frame_row(view), eye = smoke_frame_row(view+1);
            if (pc.dispatch.z < 0) {
                int cloud = int(frame.x)+pc.dispatch.y*14, field = int(smoke_frame_row(cloud).x);
                vec4 cache = smoke_field_row(field+4);
                int grid = int(cache.y);
                ivec2 xy = ivec2(gl_GlobalInvocationID.xy);
                if (xy.x >= grid*8 || xy.y >= grid*((grid+7)/8)) return;
                ivec3 voxel = ivec3(xy.x%grid,xy.y%grid,(xy.y/grid)*8+xy.x/grid);
                if (voxel.z >= grid) return;
                vec3 point = smoke_field_row(field).xyz+vec3(voxel)*(625.0/float(grid-1));
                vec4 transmission = vec4(1.0);
                for (int i = 0; i < int(smoke_frame_row(cloud).y); i++) {
                    vec4 light = smoke_frame_row(cloud+2+i*3);
                    vec3 delta = light.w < 0.0 ? light.xyz : light.xyz-point.xzy*.01;
                    float distance = length(delta);
                    transmission[i] = smoke_scene_ray_transmission(int(frame.x),int(frame.y),point,
                        delta.xzy/max(distance,.00001),light.w < 0.0 ? 100000.0 : distance*100.0,frame.z);
                }
                imageStore(smoke_prefix_image,xy+ivec2(0,int(cache.x)),transmission);
                return;
            }
            vec4 right = smoke_frame_row(view+2), up = smoke_frame_row(view+3), forward = smoke_frame_row(view+4);
            vec4 layout_info = smoke_frame_row(view+5), depth_info = smoke_frame_row(view+6);
            ivec2 grid = ivec2(layout_info.yz), xy = ivec2(gl_GlobalInvocationID.xy);
            if (any(greaterThanEqual(xy,grid))) return;
            vec2 uv = (vec2(xy)+.5)/vec2(grid), ndc = uv*2.0-1.0;
            vec3 unnormalized = forward.xyz + right.xyz*(ndc.x/right.w) - up.xyz*(ndc.y/up.w);
            float ray_scale = length(unnormalized);
            vec3 ray = unnormalized/ray_scale;
            float tau = 0.0, previous = depth_info.x*ray_scale;
            vec3 radiance = vec3(0.0);
            int quality = int(frame.w), mode = int(eye.w), slices = int(layout_info.w);
            float jitter = quality == 0 ? .5 : fract(52.9829189*fract(dot(vec2(xy),vec2(.06711056,.00583715))+float(pc.dispatch.w%8)*.61803399));
            bool shared_lights = depth_info.z > 1.5;
            float stride = quality == 0 ? 12.5 : quality == 1 ? 8.333333 : 6.25;
            // The cache's depth layout is shared across the whole view, but most rays miss a small cloud or
            // traverse it only briefly. Clip integration to this ray's conservative union of field bounds.
            // All depth slices are still written in the same layout, preserving surface/history sampling.
            float occupied_near = depth_info.y*ray_scale, occupied_far = 0.0;
            for (int c = 0; c < int(frame.y); c++) {
                int cloud = int(frame.x)+c*14, field = int(smoke_frame_row(cloud).x);
                vec3 origin = smoke_field_row(field).xyz;
                vec2 occupied;
                if (smoke_box(eye.xyz,ray,origin,origin+vec3(625.0),depth_info.y*ray_scale,occupied)) {
                    occupied_near = min(occupied_near,occupied.x);
                    occupied_far = max(occupied_far,occupied.y);
                }
            }
            for (int slice = 0; slice < slices; slice++) {
                float stop = mix(depth_info.x,depth_info.y,float(slice)/float(slices-1))*ray_scale;
                float start = max(previous,occupied_near), end = min(stop,occupied_far);
                int steps = max(0,int(ceil((end-start)/stride)));
                float step_length = max(0.0,end-start)/float(max(steps,1));
                // Keep writing every depth slice after the opaque tail is bounded by exp(-8).
                for (int s = 0; s < steps && tau < SMOKE_OPAQUE_TAU; s++) {
                    float along = start+(float(s)+.5)*step_length;
                    vec3 p = eye.xyz+ray*along;
                    vec3 light_position = p+ray*((jitter-.5)*step_length);
                    float density = 0.0;
                    vec3 source = vec3(0.0);
                    // Raw omitted radiance is bounded by .9*exp(-6) per channel, before history/vision.
                    // Continue extinction to the opaque-tail threshold; thermal keeps its unmodified source.
                    bool light_sample = mode != 36 && tau < 6.0;
                    bool spots_ready = false;
                    vec3 common_spots = vec3(0.0);
                    for (int c = 0; c < int(frame.y); c++) {
                        int cloud = int(frame.x)+c*14, field = int(smoke_frame_row(cloud).x);
                        float part = smoke_density(field,p,frame.z,false);
                        if (part <= .0001) continue;
                        density += part;
                        if (light_sample && !shared_lights) {
                            if (!spots_ready) {
                                common_spots = smoke_spot_light(light_position,ray,frame.z,quality,int(frame.x),int(frame.y));
                                spots_ready = true;
                            }
                            source += part*smoke_lighting_with_spots(cloud,light_position,ray,frame.z,quality,int(frame.x),int(frame.y),common_spots);
                        }
                        // Only per-cloud color weighting needs contributors after the union saturates.
                        if ((!light_sample || shared_lights) && density >= 1.0) break;
                    }
                    if (density > .0001) {
                        if (light_sample && shared_lights) source = density*smoke_light(int(frame.x),light_position,ray,min(density,1.0),frame.z,quality,int(frame.x),int(frame.y));
                        source = mode == 36 ? vec3(0.0,0.0,1.0-clamp(along/5000.0,0.0,1.0)) : source/density;
                        float optical = min(density,1.0)*step_length*SMOKE_EXTINCTION*(mode == 36 ? .2 : 1.0);
                        if (light_sample || mode == 36) radiance += exp(-tau)*(1.0-exp(-optical))*source;
                        tau += optical;
                    }
                }
                previous = stop;
                ivec2 out_xy = xy+ivec2((slice%8)*grid.x,int(layout_info.x)+(slice/8)*grid.y);
                imageStore(smoke_prefix_image,out_xy,resolve_history(view,eye.xyz+ray*stop,vec4(radiance,exp(-tau))));
            }
        }
        """;
}
