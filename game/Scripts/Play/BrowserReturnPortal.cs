using Godot;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Play;

/// <summary>Visible marker for the local lesson's walk-in return volume. The simulation owns teleporting.</summary>
public partial class BrowserReturnPortal : Node3D
{
    public BrowserReturnPortal() => Name = "ReturnPortal";

    public override void _Ready()
    {
        Position = UeSpace.ToGodot(BrowserLesson.ReturnPortal);
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.18f, 0.9f, 0.4f),
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.83f, OuterRadius = 1f, Rings = 40, RingSegments = 12 },
            MaterialOverride = material,
            Rotation = new Vector3(0, 0, Mathf.Pi / 2),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.025f },
            MaterialOverride = material, Position = new Vector3(0, -0.86f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        AddChild(new Label3D
        {
            Text = "RETURN TO LEDGE\nWalk through", Position = new Vector3(0, 1.3f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 50, PixelSize = 0.005f,
            Modulate = new Color(0.65f, 1f, 0.75f), OutlineSize = 10,
        });
    }
}
