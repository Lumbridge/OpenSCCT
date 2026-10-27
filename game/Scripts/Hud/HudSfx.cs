using System;
using System.Globalization;
using Godot;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// The HUD's eight screen-effect slots (spec/hud/screen-effects.md rules 1 to 8): colour washes over the view (black,
/// red, grey, the knock-out screen) and the two render factors the slots drive (cross-eye blur, flash after-image).
/// This is the HUD side of SHud.AddVisualSfx, SfxForceSlot and ProcessSFX; the simulation's notifications feed it
/// (<see cref="HudEffects"/>). It never writes simulation state.
/// </summary>
public sealed class HudSfx
{
    public const int Slots = 8;

    [Provenance(ProvenanceKind.Decoded, "AddVisualSfx helper game 0x10a92690: Duration 0 and EndTime 0 -> 5 s")]
    public const float DefaultDuration = 5f;

    [Provenance(ProvenanceKind.Decoded, "AddVisualSfx helper game 0x10a92690: StartValue -10000 keeps the current value")]
    public const float KeepCurrent = -10000f;

    /// <summary>Type 4's colour (90, 0, 0) through the canvas (<see cref="HudK.CanvasColour"/>).</summary>
    [Provenance(ProvenanceKind.Decoded, "ProcessSFX game 0x10a91e90: type 4 draws 0x5a0000 (90, 0, 0)")]
    public static readonly Color Red = HudK.CanvasColour(90, 0, 0);

    /// <summary>Type 5's colour (128, 128, 128) through the canvas: white.</summary>
    [Provenance(ProvenanceKind.Decoded, "ProcessSFX game 0x10a91e90: type 5 draws 0x808080")]
    public static readonly Color Grey = HudK.CanvasColour(128, 128, 128);

    private readonly SFXType[] _type = new SFXType[Slots];
    private readonly SFXAttenuation[] _evolution = new SFXAttenuation[Slots];
    private readonly float[] _current = new float[Slots];
    private readonly float[] _startTime = new float[Slots];
    private readonly float[] _startValue = new float[Slots];
    private readonly float[] _endTime = new float[Slots];
    private readonly float[] _endValue = new float[Slots];
    private readonly bool[] _kill = new bool[Slots];
    private int _forcedSlot = -1;

    /// <summary>PlayerController.CrossEyedFactor as the slots set it (the render's double vision, 0..1).</summary>
    public float CrossEyedFactor { get; private set; }

    /// <summary>PlayerController.RemanenceBlendFactor as the slots set it (the flash-bang after-image, 0..1).</summary>
    public float RemanenceBlendFactor { get; private set; }

    /// <summary>A knock-out slot is running (the HUD hides while it is dark).</summary>
    public bool Sleeping => Find(SFXType.SFXT_TextureColorSleep) >= 0;

    public SFXType TypeOf(int slot) => _type[slot];

    public float ValueOf(int slot) => _current[slot];

    /// <summary>The first slot of <paramref name="type"/>, or -1.</summary>
    public int Find(SFXType type) => Array.IndexOf(_type, type);

    /// <summary>SHud.SfxForceSlot: the next effect goes to slot <paramref name="slot"/>.</summary>
    [UcNative("ASHud::execSfxForceSlot", Exe = 0x10a92640, Editor = 0x111be6f0)]
    [PortOf("nat:SBase.SHud.SfxForceSlot", Spec = "spec/hud/screen-effects.md#rules")]
    public void SfxForceSlot(int slot) => _forcedSlot = slot;

    /// <summary>SHud.AddVisualSfx (rules 1 to 4); false when no slot was free.</summary>
    [UcNative("ASHud::execAddVisualSfx", Exe = 0x10a92940, Editor = 0x111bea90)]
    [PortOf("nat:SBase.SHud.AddVisualSfx", Spec = "spec/hud/screen-effects.md#rules")]
    public bool AddVisualSfx(SFXType type, float startValue, float endValue, float duration, SFXAttenuation evolution, float now,
        float startTime = 0f, float endTime = 0f, bool dontKill = false)
    {
        int slot = _forcedSlot;
        if (slot < 0)
        {
            slot = 0;
            while (slot < Slots && _type[slot] != SFXType.SFXT_None && !(_type[slot] == SFXType.SFXT_FlashEffect && type == SFXType.SFXT_FlashEffect))
            {
                slot++;
            }
        }
        else
        {
            _forcedSlot = -1;
            if (slot < Slots)
            {
                switch (_type[slot])
                {
                    case SFXType.SFXT_CrossEye:
                        CrossEyedFactor = 0f;
                        break;
                    case SFXType.SFXT_Smoke when type != SFXType.SFXT_Smoke:
                        CrossEyedFactor = 0f;
                        break;
                    case SFXType.SFXT_FlashEffect:
                        RemanenceBlendFactor = 0f;
                        break;
                }
            }
        }

        if (slot >= Slots)
        {
            return false;
        }

        _kill[slot] = !dontKill;
        _startTime[slot] = startTime == 0f ? now : startTime;
        float d = duration != 0f ? duration : endTime != 0f ? endTime - _startTime[slot] : DefaultDuration;
        _endTime[slot] = _startTime[slot] + d;
        _evolution[slot] = evolution;
        _type[slot] = type;
        _startValue[slot] = startValue == KeepCurrent ? _current[slot] : startValue;
        _endValue[slot] = endValue;
        return true;
    }

    /// <summary>
    /// SHud.ProcessSFX (rules 5 and 6) without drawing: updates every slot's value and the render factors for the
    /// time <paramref name="now"/>, and ends finished slots.
    /// </summary>
    [UcNative("ASHud::execProcessSFX", Exe = 0x10a91e90, Editor = 0x111c48d0)]
    [PortOf("nat:SBase.SHud.ProcessSFX", Spec = "spec/hud/screen-effects.md#rules")]
    public void Process(float now)
    {
        for (int i = 0; i < Slots; i++)
        {
            if (_type[i] == SFXType.SFXT_None)
            {
                continue;
            }

            float start = _startValue[i], end = _endValue[i];
            if (_evolution[i] == SFXAttenuation.SFXA_Constant)
            {
                _current[i] = start;
            }
            else if (_evolution[i] == SFXAttenuation.SFXA_Linear)
            {
                float span = _endTime[i] - _startTime[i];
                _current[i] = span != 0f ? start + (end - start) * (now - _startTime[i]) / span : end;
            }
            else
            {
                _type[i] = SFXType.SFXT_None;
                continue;
            }

            _current[i] = Math.Clamp(_current[i], Math.Min(start, end), Math.Max(start, end));
            switch (_type[i])
            {
                case SFXType.SFXT_CrossEye:
                case SFXType.SFXT_Smoke:
                    CrossEyedFactor = Math.Clamp(_current[i], 0f, 1f);
                    break;
                case SFXType.SFXT_FlashEffect:
                    RemanenceBlendFactor = _current[i];
                    break;
            }

            if (_endTime[i] <= now && _kill[i])
            {
                if (_type[i] is SFXType.SFXT_CrossEye or SFXType.SFXT_Smoke)
                {
                    CrossEyedFactor = 0f;
                }
                else if (_type[i] == SFXType.SFXT_FlashEffect)
                {
                    RemanenceBlendFactor = 0f;
                }

                _type[i] = SFXType.SFXT_None;
                _current[i] = end;
            }
        }
    }

    /// <summary>Ends every knock-out slot at once (the pawn died or the match ended, rule 8).</summary>
    public void EndSleep()
    {
        for (int i = 0; i < Slots; i++)
        {
            if (_type[i] == SFXType.SFXT_TextureColorSleep)
            {
                _type[i] = SFXType.SFXT_None;
                _current[i] = _endValue[i];
            }
        }
    }

    /// <summary>Clears every slot (a new HUD).</summary>
    public void Reset()
    {
        Array.Clear(_type);
        Array.Clear(_current);
        _forcedSlot = -1;
        CrossEyedFactor = 0f;
        RemanenceBlendFactor = 0f;
    }

    /// <summary>
    /// The washes of the running slots as full-screen quads (texture <paramref name="white"/>, a white region of the
    /// HUD atlas), and the knock-out text at full black. Slot order, as ProcessSFX draws.
    /// </summary>
    public void Draw(MagmaDrawList into, Vector2 screen, WhiteRegion white, string sleepingText, int sleepSeconds)
    {
        ArgumentNullException.ThrowIfNull(into);
        for (int i = 0; i < Slots; i++)
        {
            Color? c = _type[i] switch
            {
                SFXType.SFXT_TextureColor or SFXType.SFXT_TextureColorSleep => new Color(0f, 0f, 0f, 1f),
                SFXType.SFXT_TextureColorRed => Red,
                SFXType.SFXT_TextureColorWhite => Grey,
                _ => null,
            };
            if (c is not { } colour)
            {
                continue;
            }

            float alpha = Math.Clamp((int)_current[i], 0, 255) / 255f;
            if (alpha > 0f)
            {
                into.Items.Add(white.Quad("Sfx/" + i.ToString(CultureInfo.InvariantCulture), Vector2.Zero, screen, colour with { A = alpha }));
            }

            if (_type[i] == SFXType.SFXT_TextureColorSleep && _current[i] >= 255f && sleepingText.Length > 0)
            {
                float s = screen.Y / MagmaRenderer.PageHeight;
                float h = HudK.SleepTextHeight * s;
                into.Items.Add(HudText.Item("Sfx/Sleeping", sleepingText, new Rect2(0f, screen.Y / 2f - h, screen.X, h), h,
                    MagmaAlign.Center, HudK.SpyColour));
                if (sleepSeconds > 0)
                {
                    into.Items.Add(HudText.Item("Sfx/SleepTime", sleepSeconds.ToString(CultureInfo.InvariantCulture),
                        new Rect2(0f, screen.Y / 2f + h * 0.45f, screen.X, h), h, MagmaAlign.Center, HudK.SpyColour));
                }
            }
        }
    }
}

/// <summary>A white region of a HUD atlas, for solid quads (washes, panels).</summary>
public readonly record struct WhiteRegion(string Package, string Texture, Vector2 Uv)
{
    /// <summary>A solid quad from <paramref name="topLeft"/> of <paramref name="size"/> pixels.</summary>
    public MagmaDrawItem Quad(string path, Vector2 topLeft, Vector2 size, Color colour) => new()
    {
        Path = path,
        Kind = MagmaWidgetKind.Image,
        Package = Package,
        Texture = Texture,
        Points = [topLeft, topLeft + new Vector2(size.X, 0f), topLeft + size, topLeft + new Vector2(0f, size.Y)],
        Uvs = [Uv, Uv, Uv, Uv],
        Colors = [colour, colour, colour, colour],
    };

    /// <summary>The centre of the HUD package's <c>Blanc_opak</c> material (solid white), or null when it is missing.</summary>
    public static WhiteRegion? Find(HudAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        MagmaPackage hud = assets.Hud;
        int m = hud.FindMaterial(HudK.WhiteMaterial);
        if (m < 0)
        {
            return null;
        }

        MagmaMaterial mat = hud.Materials[m];
        if (assets.TextureSize(hud.Name, mat.Texture) is not { } size || size.X <= 0 || size.Y <= 0)
        {
            return null;
        }

        Rect4 r = mat.Region;
        return new WhiteRegion(hud.Name, mat.Texture, new Vector2((r.L + r.R) * 0.5f / size.X, (r.T + r.B) * 0.5f / size.Y));
    }
}
