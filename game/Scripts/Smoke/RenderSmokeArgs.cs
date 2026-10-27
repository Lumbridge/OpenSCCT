using System;
using System.Globalization;
using System.IO;
using Godot;
using OpenScctGame.Render;

namespace OpenScctGame.Smoke;

/// <summary>
/// Extra command line options of the render smoke tests, read from the user arguments next to the ones
/// <see cref="LaunchOptions"/> knows: <c>--svm-render-map=SteeE</c>, <c>--svm-golden-in=dir</c>,
/// <c>--svm-golden-out=dir</c>, <c>--svm-golden-only=a,b</c>, <c>--svm-lightmap-scale=2.5</c>,
/// <c>--svm-glow-gate=0|1</c>, <c>--svm-golden-frames=n</c>.
/// </summary>
public sealed class RenderSmokeArgs
{
    public string Map { get; private init; } = "SteeE";

    public string GoldenIn { get; private init; } = Path.Combine(RepoRoot, "local", "golden", "game");

    public string GoldenOut { get; private init; } = Path.Combine(RepoRoot, "local", "golden", "svm");

    public string[] Only { get; private init; } = [];

    public float? LightmapScale { get; private init; }

    public bool GlowGate { get; private init; } = true;

    /// <summary>Dynamic world light calibration (gain, radius scale, falloff), from <c>--svm-dyn-tune=g,r,f</c>.</summary>
    public Vector4? DynTune { get; private init; }

    /// <summary>PlayerController.RendMap of the views, from <c>--svm-rendmap=34</c> (night vision); 5 normal.</summary>
    public int RendMap { get; private init; } = RenderK.RendMapNormal;

    /// <summary>Mip levels dropped from colour textures, from <c>--svm-mip-drop=n</c>.</summary>
    public int? MipDrop { get; private init; }

    /// <summary>The game mode the captures were taken in (objective props shown for it), from <c>--svm-golden-mode=Story</c>.</summary>
    public string GameMode { get; private init; } = "Story";

    /// <summary>The pawn's HeatIntensity in thermal, from <c>--svm-pawn-heat=0.8</c> (calibration); null keeps the class default.</summary>
    public float? PawnHeat { get; private init; }

    /// <summary>Scale of the night vision scene boost, from <c>--svm-night-boost=1.5</c> (calibration).</summary>
    public float NightBoost { get; private init; } = 1f;

    /// <summary>LOD bias of colour textures, from <c>--svm-mip-bias=-0.5</c> (calibration).</summary>
    public float MipBias { get; private init; }

    /// <summary>Calibration logs, from <c>--svm-golden-debug=coronas</c>.</summary>
    public string Debug { get; private init; } = "";

    /// <summary>Frames drawn after a camera move before the picture is read.</summary>
    public int SettleFrames { get; private init; } = 4;

    public static string RepoRoot => SmokePaths.RepoRoot ?? Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));

    public static RenderSmokeArgs Parse()
    {
        string map = "SteeE";
        string? gin = null;
        string? gout = null;
        string[] only = [];
        float? scale = null;
        bool gate = true;
        int frames = 4;
        Vector4? tune = null;
        int? mipDrop = null;
        int rendMap = RenderK.RendMapNormal;
        string gameMode = "Story";
        string debug = "";
        float mipBias = 0f;
        float? pawnHeat = null;
        float nightBoost = 1f;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string? Value(string prefix) => arg.StartsWith(prefix, StringComparison.Ordinal) ? arg[prefix.Length..] : null;
            if (Value("--svm-render-map=") is { Length: > 0 } m)
            {
                map = m;
            }
            else if (Value("--svm-golden-in=") is { Length: > 0 } i)
            {
                gin = i;
            }
            else if (Value("--svm-golden-out=") is { Length: > 0 } o)
            {
                gout = o;
            }
            else if (Value("--svm-golden-only=") is { Length: > 0 } list)
            {
                only = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else if (Value("--svm-lightmap-scale=") is { Length: > 0 } s
                && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v > 0f)
            {
                scale = v;
            }
            else if (Value("--svm-glow-gate=") is { Length: > 0 } g)
            {
                gate = g != "0";
            }
            else if (Value("--svm-dyn-tune=") is { Length: > 0 } t)
            {
                string[] parts = t.Split(',');
                float P(int i) => i < parts.Length && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ? x : 0f;
                tune = new Vector4(P(0), P(1), P(2), parts.Length > 3 ? P(3) : 1f);
            }
            else if (Value("--svm-rendmap=") is { Length: > 0 } rm
                && int.TryParse(rm, NumberStyles.None, CultureInfo.InvariantCulture, out int mode))
            {
                rendMap = mode;
            }
            else if (Value("--svm-mip-drop=") is { Length: > 0 } md
                && int.TryParse(md, NumberStyles.None, CultureInfo.InvariantCulture, out int drop))
            {
                mipDrop = drop;
            }
            else if (Value("--svm-golden-mode=") is { Length: > 0 } gm)
            {
                gameMode = gm;
            }
            else if (Value("--svm-mip-bias=") is { Length: > 0 } mb
                && float.TryParse(mb, NumberStyles.Float, CultureInfo.InvariantCulture, out float bias))
            {
                mipBias = bias;
            }
            else if (Value("--svm-night-boost=") is { Length: > 0 } nb
                && float.TryParse(nb, NumberStyles.Float, CultureInfo.InvariantCulture, out float boost))
            {
                nightBoost = boost;
            }
            else if (Value("--svm-pawn-heat=") is { Length: > 0 } ph
                && float.TryParse(ph, NumberStyles.Float, CultureInfo.InvariantCulture, out float heat))
            {
                pawnHeat = heat;
            }
            else if (Value("--svm-golden-debug=") is { Length: > 0 } dbg)
            {
                debug = dbg;
            }
            else if (Value("--svm-golden-frames=") is { Length: > 0 } f
                && int.TryParse(f, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0)
            {
                frames = n;
            }
        }

        var defaults = new RenderSmokeArgs();
        return new RenderSmokeArgs
        {
            Map = map,
            Only = only,
            LightmapScale = scale,
            GlowGate = gate,
            SettleFrames = frames,
            DynTune = tune,
            MipDrop = mipDrop,
            RendMap = rendMap,
            GameMode = gameMode,
            Debug = debug,
            MipBias = mipBias,
            PawnHeat = pawnHeat,
            NightBoost = nightBoost,
            GoldenIn = gin ?? defaults.GoldenIn,
            GoldenOut = gout ?? defaults.GoldenOut,
        };
    }
}
