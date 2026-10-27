using System.Collections.Generic;
using Godot;
using OpenScct.Server.Hub;
using OpenScctGame.UI;

namespace OpenScctGame.Hub;

/// <summary>
/// Draws the hub's world texts (an OpenSCCT addition, docs/HUB.md "Signs"): one <see cref="Label3D"/> per
/// <see cref="HubSign"/> the hub server sent, in the menus' Inter with a dark outline so it reads against any wall,
/// hidden past the distance the server stops sending it from. A sign mounted on a board (<see cref="HubSignFace"/>)
/// lies on the board's face, just in front of it, and its text is sized to fit the board; any other sign is a
/// billboard that faces the camera, sized by its scale. The server decides what each sign says (<see cref="HubSigns"/>).
/// 
/// </summary>
public partial class HubSignsView : Node3D
{
    public const string NodeName = "HubSigns";

    /// <summary>Font size of a scale 1 sign (pixels of the glyph texture).</summary>
    private const int FontSize = 72;

    /// <summary>Metres per glyph pixel at scale 1: a line of a standard sign is about 0.36 m tall.</summary>
    private const float PixelSize = 0.005f;

    /// <summary>Signs fade out from here (m) and are gone at <see cref="FarMetres"/> (the server's 7000 uu).</summary>
    private const float FadeMetres = 60f;

    private const float FarMetres = 70f;

    /// <summary>A mounted sign's text sits this far in front of its board (m), so the board never cuts through it.</summary>
    private const float FrontOffset = 0.04f;

    /// <summary>The share of a board's width the text may use, and of its height under the board's printed header.</summary>
    private const float FitWidth = 0.9f;

    private const float FitHeight = 0.74f;

    /// <summary>The board's printed header band (a share of its height): the text is centred in the rest.</summary>
    private const float HeaderBand = 0.16f;

    private readonly Dictionary<string, HubSign> _signs = new();

    private readonly Dictionary<string, Label3D> _labels = new();
    private HubClient? _client;

    /// <summary>Signs shown (smokes and diagnostics).</summary>
    public int Shown => _labels.Count;

    public HubClient? Client
    {
        get => _client;
        set
        {
            if (_client is not null)
            {
                _client.SignChanged -= OnSign;
                _client.SignRemoved -= OnRemoved;
            }

            _client = value;
            Clear();
            if (_client is not null)
            {
                _client.SignChanged += OnSign;
                _client.SignRemoved += OnRemoved;
                foreach (HubSign sign in _client.Signs.Values)
                {
                    OnSign(sign);
                }
            }
        }
    }

    public override void _Ready() => Name = NodeName;

    public override void _ExitTree() => Client = null;

    /// <summary>The label of a sign, or null (the pad view places the map's picture over the pad's sign).</summary>
    public Label3D? Label(string id) => _labels.GetValueOrDefault(id);

    /// <summary>The sign as the server last sent it, or null.</summary>
    public HubSign? Sign(string id) => _signs.TryGetValue(id, out HubSign sign) ? sign : null;

    /// <summary>The Godot basis of a board facing UE yaw <paramref name="yaw"/> (local +Z out of the board's face).</summary>
    public static Basis BoardBasis(float yaw)
    {
        float a = yaw * Mathf.Tau / 65536f;
        Vector3 facing = UeSpace.DirectionToGodot(new OpenScct.Core.FVector(Mathf.Cos(a), Mathf.Sin(a), 0f)).Normalized();
        return Basis.LookingAt(-facing, Vector3.Up);
    }

    private void OnSign(HubSign sign)
    {
        if (!_labels.TryGetValue(sign.Id, out Label3D? label))
        {
            label = new Label3D
            {
                Name = "Sign_" + sign.Id.Replace('.', '_'),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = FontSize,
                OutlineSize = 16,
                OutlineModulate = new Color(0f, 0f, 0f, 0.9f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                DoubleSided = true,
                VisibilityRangeEnd = FarMetres,
                VisibilityRangeEndMargin = FarMetres - FadeMetres,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            if (UiArt.BodyFont is { } font)
            {
                label.Font = font;
            }

            _labels[sign.Id] = label;
            AddChild(label);
        }

        _signs[sign.Id] = sign;
        label.Text = sign.Text;
        label.Modulate = Colour(sign.Rgba);
        if (sign.Face.Mounted)
        {
            Basis basis = BoardBasis(sign.Face.Yaw);
            float height = UeSpace.LengthToGodot(sign.Face.Height);
            label.Billboard = BaseMaterial3D.BillboardModeEnum.Disabled;
            label.DoubleSided = false;
            label.PixelSize = FitPixelSize(label, sign);
            label.Transform = new Transform3D(basis,
                UeSpace.ToGodot(sign.Position) + basis.Z * FrontOffset + Vector3.Down * (height * HeaderBand / 2f));
        }
        else
        {
            label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
            label.DoubleSided = true;
            label.PixelSize = PixelSize * sign.Scale;
            label.Transform = new Transform3D(Basis.Identity, UeSpace.ToGodot(sign.Position));
        }
    }

    /// <summary>The largest glyph size (m per pixel) at which every line fits the board, no larger than a standard sign's.</summary>
    private static float FitPixelSize(Label3D label, HubSign sign)
    {
        float most = PixelSize * sign.Scale;
        if (label.Font is not { } font)
        {
            return most;
        }

        string[] lines = sign.Text.Split('\n');
        float widest = 1f;
        foreach (string line in lines)
        {
            widest = Mathf.Max(widest, font.GetStringSize(line, HorizontalAlignment.Left, -1, label.FontSize).X + 2f * label.OutlineSize);
        }

        float tall = Mathf.Max(1f, lines.Length * (font.GetHeight(label.FontSize) + label.LineSpacing));
        float byWidth = UeSpace.LengthToGodot(sign.Face.Width) * FitWidth / widest;
        float byHeight = UeSpace.LengthToGodot(sign.Face.Height) * FitHeight / tall;
        return Mathf.Min(most, Mathf.Min(byWidth, byHeight));
    }

    private void OnRemoved(string id)
    {
        _signs.Remove(id);
        if (_labels.Remove(id, out Label3D? label))
        {
            label.QueueFree();
        }
    }

    private void Clear()
    {
        foreach (Label3D label in _labels.Values)
        {
            label.QueueFree();
        }

        _labels.Clear();
        _signs.Clear();
    }

    /// <summary>0xRRGGBBAA as a Godot colour.</summary>
    public static Color Colour(uint rgba) =>
        new(((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, (rgba & 0xFF) / 255f);
}
