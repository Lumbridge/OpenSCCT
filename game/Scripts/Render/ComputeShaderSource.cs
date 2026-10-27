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
public static class ComputeShaderSource
{
    private const string ComputeTag = "#[compute]";

    /// <summary>
    /// The shader of <paramref name="path"/>'s compute stage made on <paramref name="rd"/> (named
    /// <paramref name="name"/>), or an invalid RID with <paramref name="error"/> set. The source and SPIR-V wrappers are
    /// disposed here, not left to the finalizer thread.
    /// </summary>
    public static Rid CreateShader(RenderingDevice rd, string path, string name, out string? error)
    {
        error = null;
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text))
        {
            error = $"{path} not found ({FileAccess.GetOpenError()})";
            return default;
        }

        // The section tag the RDShaderFile format uses; glslang does not know it.
        int tag = text.IndexOf(ComputeTag, System.StringComparison.Ordinal);
        if (tag >= 0)
        {
            text = text.Remove(tag, ComputeTag.Length);
        }

        using var source = new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = text };
        using RDShaderSpirV? spirv = rd.ShaderCompileSpirVFromSource(source);
        if (spirv is null || !string.IsNullOrEmpty(spirv.CompileErrorCompute))
        {
            error = spirv?.CompileErrorCompute ?? "no SPIR-V";
            return default;
        }

        return rd.ShaderCreateFromSpirV(spirv, name);
    }
}
