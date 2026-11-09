using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace OpenScctGame.Render.Materials;

/// <summary>
/// The shaders that include svm_fog.gdshaderinc. It compiles responsive smoke only under
/// <see cref="Define"/>; without it, stubs return the values the smoke code gives while inactive
/// (svm_smoke_active false), so a match without responsive smoke draws the same image without that code.
/// A match picks the variant with <see cref="Use"/> before it builds materials. Each shader is one object whose code
/// follows the flag, so rules that arrive later switch the materials already built.
/// </summary>
public static class ResponsiveSmokeShaders
{
    public const string Define = "SVM_RESPONSIVE_SMOKE";
    private static readonly Regex ShaderType = new(@"\bshader_type\s+\w+\s*;", RegexOptions.CultureInvariant);
    private static readonly System.Threading.Lock s_lock = new();
    private static readonly Dictionary<string, Shader> s_byCode = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Shader> s_byPath = new(StringComparer.Ordinal);
    private static readonly Dictionary<Shader, Entry> s_entries = new(ReferenceEqualityComparer.Instance);
    private static bool s_enabled;

    private sealed class Entry(string code)
    {
        public string Code { get; } = code;
        public bool Smoke;
    }

    /// <summary>
    /// The process-wide Shader of <paramref name="code"/> (without the define), made (named <paramref name="name"/>,
    /// then given to <paramref name="init"/>) on first use. Callers must not change it: other materials share it.
    /// </summary>
    public static Shader Shared(string code, string name, Action<Shader>? init = null)
    {
        lock (s_lock)
        {
            if (s_byCode.TryGetValue(code, out Shader? shader)) return Refresh(shader);
            shader = Make(code, name);
            init?.Invoke(shader);
            s_byCode[code] = shader;
            return shader;
        }
    }

    /// <summary>A process-wide copy of the .gdshader file at <paramref name="path"/> that follows the flag.</summary>
    public static Shader Load(string path)
    {
        lock (s_lock)
        {
            if (s_byPath.TryGetValue(path, out Shader? shader)) return Refresh(shader);
            shader = Make(GD.Load<Shader>(path).Code, path);
            s_byPath[path] = shader;
            return shader;
        }
    }

    /// <summary><paramref name="shader"/> with the current variant's code, for callers that keep their own cache.</summary>
    public static Shader Current(Shader shader)
    {
        lock (s_lock) return Refresh(shader);
    }

    /// <summary>The code <paramref name="shader"/> was made from, without the define.</summary>
    public static string BaseCode(Shader shader)
    {
        lock (s_lock) return s_entries.TryGetValue(shader, out Entry? entry) ? entry.Code : shader.Code;
    }

    /// <summary>True when the shaders compile responsive smoke (<see cref="Use"/>).</summary>
    public static bool Enabled
    {
        get
        {
            lock (s_lock) return s_enabled;
        }
    }

    /// <summary>
    /// Compile responsive smoke into the shaders (or leave it out). Shaders that materials use switch now; idle cached
    /// ones switch when next handed out (<see cref="Shared"/>, <see cref="Load"/>, <see cref="Current"/>).
    /// </summary>
    public static void Use(bool enabled)
    {
        lock (s_lock)
        {
            if (s_enabled == enabled) return;
            s_enabled = enabled;
            foreach ((Shader shader, Entry entry) in s_entries)
            {
                // The registry's own wrapper holds one reference; a material holds another.
                if (GodotObject.IsInstanceValid(shader) && shader.GetReferenceCount() > 1) Apply(shader, entry);
            }
        }
    }

    private static Shader Make(string code, string name)
    {
        var shader = new Shader { ResourceName = name };
        // Only an include can reach svm_fog.gdshaderinc; other code never changes.
        if (!code.Contains("#include", StringComparison.Ordinal))
        {
            shader.Code = code;
            return shader;
        }

        var entry = new Entry(code);
        s_entries[shader] = entry;
        Apply(shader, entry);
        return shader;
    }

    private static Shader Refresh(Shader shader)
    {
        if (s_entries.TryGetValue(shader, out Entry? entry) && entry.Smoke != s_enabled) Apply(shader, entry);
        return shader;
    }

    private static void Apply(Shader shader, Entry entry)
    {
        entry.Smoke = s_enabled;
        shader.Code = s_enabled ? WithDefine(entry.Code) : entry.Code;
    }

    // The define goes after the shader_type statement, which must stay the code's first statement.
    private static string WithDefine(string code)
    {
        System.Text.RegularExpressions.Match match = ShaderType.Match(code);
        string line = "\n#define " + Define + "\n";
        return match.Success ? code.Insert(match.Index + match.Length, line) : line + code;
    }
}
