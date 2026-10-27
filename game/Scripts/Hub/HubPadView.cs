using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Server;
using OpenScct.Server.Hub;
using OpenScctGame.Play;

namespace OpenScctGame.Hub;

/// <summary>
/// The match pads as the client shows them (an OpenSCCT addition, docs/HUB.md "Match pads"): for each pad the hub
/// described (<c>hub pad</c>), a translucent overlay on its floor with a bright border, coloured by its state (white
/// idle, yellow waiting, green counting down, cyan starting, grey off; the countdown pulses), and the map's own
/// picture from this install's map catalog (the host page's map list picture) floating over the pad's sign. When a
/// pad's match changes (the first line, or a rotation later) <see cref="OnPadConfigChanged"/> runs: the hook for the
/// pads' map themes.
/// </summary>
public partial class HubPadView : Node3D
{
    public const string NodeName = "HubPads";

    /// <summary>The overlay sits this far over the pad's floor (m), above the floor's own marking.</summary>
    private const float Lift = 0.03f;

    private const float BorderWidth = 0.18f;

    /// <summary>Width of the map picture (m).</summary>
    private const float PictureWidth = 3.4f;

    private readonly Dictionary<string, Pad> _pads = new();
    private HubClient? _client;
    private double _time;

    private sealed class Pad
    {
        public required Node3D Root;
        public required StandardMaterial3D Fill;
        public required StandardMaterial3D Border;
        public required Sprite3D Picture;
        public HubPadInfo Info = null!;
    }

    /// <summary>Reads the map pictures (the hub service's catalog of this install), or null.</summary>
    public MapCatalog? Maps { get; set; }

    /// <summary>The sign view, to put each picture over its pad's sign.</summary>
    public HubSignsView? Signs { get; set; }

    /// <summary>Pads shown (diagnostics).</summary>
    public int Shown => _pads.Count;

    public HubClient? Client
    {
        get => _client;
        set
        {
            if (_client is not null)
            {
                _client.PadChanged -= OnPad;
            }

            _client = value;
            if (_client is not null)
            {
                _client.PadChanged += OnPad;
                foreach (HubPadInfo pad in _client.Pads.Values)
                {
                    OnPad(pad, true);
                }
            }
        }
    }

    private bool _themesShown;

    public override void _Ready() => Name = NodeName;

    public override void _ExitTree() => Client = null;

    public override void _Process(double delta)
    {
        _time += delta;
        if (!_themesShown && GetParent() is PlayRoot { Map: { } map })
        {
            // The pads' themes chosen before the map was built go on once it is.
            HubPadThemes.ApplyChosen(map);
            _themesShown = true;
        }

        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_time * 6f);
        foreach (Pad pad in _pads.Values)
        {
            Color c = StateColour(pad.Info.State);
            float fill = pad.Info.State switch
            {
                HubPadState.Countdown => 0.22f + 0.2f * pulse,
                HubPadState.Starting => 0.4f,
                HubPadState.Waiting => 0.2f,
                HubPadState.Off => 0.08f,
                _ => 0.1f,
            };
            pad.Fill.AlbedoColor = new Color(c, fill);
            pad.Border.AlbedoColor = new Color(c, pad.Info.State == HubPadState.Countdown ? 0.6f + 0.4f * pulse : 0.85f);
            if (Signs?.Sign("Pad." + pad.Info.Id) is { Face.Mounted: true } mounted)
            {
                // On a board: the map's picture stands on the board's top edge, facing the same way, clear of the text.
                Basis basis = HubSignsView.BoardBasis(mounted.Face.Yaw);
                float top = UeSpace.LengthToGodot(mounted.Face.Height) / 2f;
                float half = pad.Picture.Texture is { } t ? t.GetHeight() * pad.Picture.PixelSize / 2f : 0f;
                pad.Picture.Billboard = BaseMaterial3D.BillboardModeEnum.Disabled;
                pad.Picture.Transform = new Transform3D(basis,
                    UeSpace.ToGodot(mounted.Position) + Vector3.Up * (top + half + 0.1f) + basis.Z * 0.02f);
            }
            else if (Signs?.Label("Pad." + pad.Info.Id) is { } label)
            {
                // Over a billboard sign: its lines are about 0.36 m each, centred on the sign's anchor.
                int lines = label.Text.Split('\n').Length;
                pad.Picture.Billboard = BaseMaterial3D.BillboardModeEnum.FixedY;
                pad.Picture.Position = label.Position + new Vector3(0f, lines * 0.19f + 1.2f, 0f);
            }
        }
    }

    private void OnPad(HubPadInfo info, bool configChanged)
    {
        if (!_pads.TryGetValue(info.Id, out Pad? pad))
        {
            pad = Build(info);
            _pads[info.Id] = pad;
        }

        pad.Info = info;
        if (configChanged)
        {
            OnPadConfigChanged(info.Id, info.Map, info.Mode);
        }
    }

    /// <summary>
    /// A pad's match is new or changed (map code, mode, team size): shows the map's picture over the pad.
    /// <para>The hook for the pads' map themes: <c>HubPadThemes.Apply(padId, theme of mapCode)</c> belongs here.</para>
    /// </summary>
    public void OnPadConfigChanged(string padId, string mapCode, EGameMode mode)
    {
        if (!_pads.TryGetValue(padId, out Pad? pad))
        {
            return;
        }

        pad.Picture.Texture = Picture(mapCode);
        HubPadThemes.Apply(padId, HubPadThemes.ThemeForMap(mapCode));
        if (pad.Picture.Texture is { } texture)
        {
            pad.Picture.PixelSize = PictureWidth / Mathf.Max(1, texture.GetWidth());
        }

        GD.Print($"SVM_HUB pad {padId}: {mapCode} {MapCatalog.ModeName(mode)}");
    }

    private Texture2D? Picture(string mapCode)
    {
        string? path = Maps?.FindMap(mapCode) is null ? null : Maps.GetCatalogEntry(mapCode)?.PreviewImage;
        if (path is null || !System.IO.File.Exists(path) || Image.LoadFromFile(path) is not { } image || image.IsEmpty())
        {
            return null;
        }

        return ImageTexture.CreateFromImage(image);
    }

    private Pad Build(HubPadInfo info)
    {
        var root = new Node3D { Name = "Pad_" + info.Id };
        AddChild(root);
        float w = UeSpace.LengthToGodot(info.HiX - info.LoX);
        float d = UeSpace.LengthToGodot(info.HiY - info.LoY);
        Vector3 centre = UeSpace.ToGodot(new FVector((info.LoX + info.HiX) / 2f, (info.LoY + info.HiY) / 2f, info.FloorZ)) + new Vector3(0f, Lift, 0f);
        root.Position = centre;
        StandardMaterial3D fill = Material();
        StandardMaterial3D border = Material();
        root.AddChild(new MeshInstance3D { Name = "Fill", Mesh = new PlaneMesh { Size = new Vector2(w, d) }, MaterialOverride = fill, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        AddBorder(root, border, new Vector3(0f, 0.005f, -d / 2f), new Vector2(w, BorderWidth));
        AddBorder(root, border, new Vector3(0f, 0.005f, d / 2f), new Vector2(w, BorderWidth));
        AddBorder(root, border, new Vector3(-w / 2f, 0.005f, 0f), new Vector2(BorderWidth, d));
        AddBorder(root, border, new Vector3(w / 2f, 0.005f, 0f), new Vector2(BorderWidth, d));
        var picture = new Sprite3D
        {
            Name = "MapPicture_" + info.Id,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            DoubleSided = true,
            Shaded = false,
            Position = UeSpace.ToGodot(new FVector((info.LoX + info.HiX) / 2f, (info.LoY + info.HiY) / 2f, info.FloorZ + 560f)),
            VisibilityRangeEnd = 70f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(picture);
        return new Pad { Root = root, Fill = fill, Border = border, Picture = picture, Info = info };
    }

    private static void AddBorder(Node3D root, StandardMaterial3D material, Vector3 at, Vector2 size) =>
        root.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = size }, MaterialOverride = material, Position = at,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

    private static StandardMaterial3D Material() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        AlbedoColor = new Color(1f, 1f, 1f, 0.1f),
    };

    /// <summary>The state's colour (the same as the pad's sign, HubPads' colours).</summary>
    public static Color StateColour(HubPadState state) => HubSignsView.Colour(state switch
    {
        HubPadState.Waiting => HubPads.WaitingColour,
        HubPadState.Countdown => HubPads.CountdownColour,
        HubPadState.Starting => HubPads.StartingColour,
        HubPadState.Off => HubPads.OffColour,
        _ => HubPads.IdleColour,
    });
}
