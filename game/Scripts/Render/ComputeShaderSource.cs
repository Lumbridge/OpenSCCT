using System;
using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Render;

/// <summary>
/// Compiles one of the project's compute shaders (<c>res://Shaders/*.glsl</c>, a <c>#[compute]</c> section) on the
/// rendering device at run time. The files are imported with the "keep" importer (exported as they are) and not as
/// RDShaderFile: the editor's glsl importer compiled them with glslang during <c>--import</c>, and Godot 4.7.2 headless
/// crashed there now and then (a segmentation fault in about 1 import of 15 to 30, seen in fresh worktrees and
/// exports; a project holding only these three files did it too, an empty one never). docs/TESTING-PLAY.md,
/// "Godot --import crash".
/// </summary>
/// <remarks>
/// glslang runs once per source for the whole process: the SPIR-V is kept, as a source cannot change while the game
/// runs. <see cref="AcquireShared"/> also shares one shader and compute pipeline per source between all effects on a
/// device, freed when the last of them calls <see cref="ReleaseShared"/>.
/// </remarks>
public static class ComputeShaderSource
{
    private const string ComputeTag = "#[compute]";

    private static readonly System.Threading.Lock s_lock = new();

    // Compute-stage SPIR-V by path (or by inline source text).
    private static readonly Dictionary<string, byte[]> s_spirv = new(StringComparer.Ordinal);

    // Shared shaders by device and source, and the reverse lookup for release.
    private static readonly Dictionary<(ulong Device, string Key), SharedShader> s_shared = [];
    private static readonly Dictionary<(ulong Device, Rid Shader), string> s_sharedKeys = [];

    private sealed class SharedShader(Rid shader, Rid pipeline)
    {
        public Rid Shader { get; } = shader;
        public Rid Pipeline { get; } = pipeline;
        public int Users;
    }

    /// <summary>
    /// The shader of <paramref name="path"/>'s compute stage made on <paramref name="rd"/> (named
    /// <paramref name="name"/>), or an invalid RID with <paramref name="error"/> set. The caller owns and frees it.
    /// </summary>
    public static Rid CreateShader(RenderingDevice rd, string path, string name, out string? error)
    {
        ArgumentNullException.ThrowIfNull(rd);
        byte[]? spirv;
        lock (s_lock)
        {
            spirv = SpirVOf(rd, path, () => ReadFile(path), out error);
        }

        return spirv is null ? default : CreateFromSpirV(rd, spirv, name);
    }

    /// <summary>
    /// The shared shader and compute pipeline of <paramref name="path"/>'s compute stage on <paramref name="rd"/>,
    /// or false with <paramref name="error"/> set. Pair each success with one <see cref="ReleaseShared"/> and never
    /// free the RIDs directly. Render thread.
    /// </summary>
    public static bool AcquireShared(RenderingDevice rd, string path, string name, out Rid shader, out Rid pipeline, out string? error) =>
        Acquire(rd, path, name, () => ReadFile(path), out shader, out pipeline, out error);

    /// <summary>As <see cref="AcquireShared"/>, for GLSL compute source text held in code.</summary>
    public static bool AcquireSharedSource(RenderingDevice rd, string source, string name, out Rid shader, out Rid pipeline, out string? error) =>
        Acquire(rd, source, name, () => (source, null), out shader, out pipeline, out error);

    /// <summary>
    /// Drops one user of a shader from <see cref="AcquireShared"/> (an invalid or unknown RID is ignored). The last
    /// user frees the pipeline and the shader. Render thread.
    /// </summary>
    public static void ReleaseShared(RenderingDevice rd, Rid shader)
    {
        ArgumentNullException.ThrowIfNull(rd);
        if (!shader.IsValid)
        {
            return;
        }

        ulong device = rd.GetInstanceId();
        SharedShader? freed = null;
        lock (s_lock)
        {
            if (!s_sharedKeys.TryGetValue((device, shader), out string? key) || !s_shared.TryGetValue((device, key), out SharedShader? shared))
            {
                return;
            }

            if (--shared.Users == 0)
            {
                s_shared.Remove((device, key));
                s_sharedKeys.Remove((device, shader));
                freed = shared;
            }
        }

        if (freed is not null)
        {
            rd.FreeRid(freed.Pipeline);
            rd.FreeRid(freed.Shader);
        }
    }

    private static bool Acquire(RenderingDevice rd, string key, string name, Func<(string? Text, string? Error)> read,
        out Rid shader, out Rid pipeline, out string? error)
    {
        ArgumentNullException.ThrowIfNull(rd);
        shader = pipeline = default;
        ulong device = rd.GetInstanceId();
        lock (s_lock)
        {
            if (s_shared.TryGetValue((device, key), out SharedShader? shared))
            {
                shared.Users++;
                shader = shared.Shader;
                pipeline = shared.Pipeline;
                error = null;
                return true;
            }

            byte[]? spirv = SpirVOf(rd, key, read, out error);
            if (spirv is null)
            {
                return false;
            }

            Rid made = CreateFromSpirV(rd, spirv, name);
            if (!made.IsValid)
            {
                error = "shader creation failed";
                return false;
            }

            Rid madePipeline = rd.ComputePipelineCreate(made);
            if (!madePipeline.IsValid)
            {
                rd.FreeRid(made);
                error = "compute pipeline creation failed";
                return false;
            }

            s_shared[(device, key)] = new SharedShader(made, madePipeline) { Users = 1 };
            s_sharedKeys[(device, made)] = key;
            shader = made;
            pipeline = madePipeline;
            return true;
        }
    }

    // The compute-stage SPIR-V of a source, from glslang on first use. Failures are not kept. Caller holds s_lock.
    private static byte[]? SpirVOf(RenderingDevice rd, string key, Func<(string? Text, string? Error)> read, out string? error)
    {
        error = null;
        if (s_spirv.TryGetValue(key, out byte[]? cached))
        {
            return cached;
        }

        (string? text, string? readError) = read();
        if (text is null)
        {
            error = readError;
            return null;
        }

        // The source and SPIR-V wrappers are disposed here, not left to the finalizer thread.
        using var source = new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = text };
        using RDShaderSpirV? spirv = rd.ShaderCompileSpirVFromSource(source);
        if (spirv is null || !string.IsNullOrEmpty(spirv.CompileErrorCompute))
        {
            error = spirv?.CompileErrorCompute ?? "no SPIR-V";
            return null;
        }

        byte[] bytecode = spirv.GetStageBytecode(RenderingDevice.ShaderStage.Compute);
        s_spirv[key] = bytecode;
        return bytecode;
    }

    private static Rid CreateFromSpirV(RenderingDevice rd, byte[] bytecode, string name)
    {
        using var spirv = new RDShaderSpirV();
        spirv.SetStageBytecode(RenderingDevice.ShaderStage.Compute, bytecode);
        return rd.ShaderCreateFromSpirV(spirv, name);
    }

    private static (string? Text, string? Error) ReadFile(string path)
    {
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text))
        {
            return (null, $"{path} not found ({FileAccess.GetOpenError()})");
        }

        // The section tag the RDShaderFile format uses; glslang does not know it.
        int tag = text.IndexOf(ComputeTag, StringComparison.Ordinal);
        if (tag >= 0)
        {
            text = text.Remove(tag, ComputeTag.Length);
        }

        const string fogInclude = "res://Shaders/svm_fog_math.gdshaderinc";
        const string fogDirective = "#include \"" + fogInclude + "\"";
        if (text.Contains(fogDirective, StringComparison.Ordinal))
        {
            // Shader includes may be remapped to resources in an exported build; load their code through the
            // resource loader instead of requiring the original text file to remain beside the exported pack.
            string? shared = ResourceLoader.Load<ShaderInclude>(fogInclude)?.Code;
            if (string.IsNullOrEmpty(shared))
            {
                return (null, $"{fogInclude} shader include not found");
            }

            // RenderingDevice compiles a source string without Godot's material include resolver.
            text = text.Replace(fogDirective, shared, StringComparison.Ordinal);
        }

        return (text, null);
    }
}
