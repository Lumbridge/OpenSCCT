using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Server.Hub;
using OpenScctGame.Play;

namespace OpenScctGame.Hub;

/// <summary>
/// The activities' things in the world (an OpenSCCT addition), drawn from what the server said
/// (<see cref="HubPlayState"/>): the course's gates (start green, checkpoints amber, the next one bright, finish
/// white), the aim range's pop-up targets (the player's own red, other rounds' grey) and firing line, the jukebox,
/// the plaza ball and its two goals. Plain shapes of our own; no game art. Never touches the simulation.
/// </summary>
public partial class HubProps : Node3D
{
    private readonly Dictionary<string, Node3D> _marks = new();
    private readonly Dictionary<int, Node3D> _targets = new();
    private MeshInstance3D? _ball;
    private int _markCount = -1;

    public HubClient? Client { get; set; }

    public PlayRoot? Round { get; set; }

    public override void _Ready() => Name = "HubProps";

    public override void _Process(double delta)
    {
        if (Client?.Play is not { } play)
        {
            return;
        }

        if (play.Marks.Count != _markCount)
        {
            _markCount = play.Marks.Count;
            foreach (HubMark m in play.Marks)
            {
                if (!_marks.ContainsKey(m.Id))
                {
                    _marks[m.Id] = MakeMark(m);
                }
            }
        }

        HighlightNextCheckpoint(play);
        DrawTargets(play);
        DrawBall(play);
    }

    private static float M(float uu) => UeSpace.LengthToGodot(uu);

    private static StandardMaterial3D Material(Color colour, bool glow = false, bool unshaded = false) => new()
    {
        AlbedoColor = colour,
        Transparency = colour.A < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        ShadingMode = unshaded ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
        EmissionEnabled = glow,
        Emission = colour,
        EmissionEnergyMultiplier = glow ? 0.6f : 0f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>A box of UE extents (x, y, z in uu) with its middle at <paramref name="centre"/> (UE).</summary>
    private MeshInstance3D Box(Node parent, FVector centre, FVector size, StandardMaterial3D material)
    {
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(M(size.X), M(size.Z), M(size.Y)) },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(mesh);
        mesh.GlobalPosition = UeSpace.ToGodot(centre);
        return mesh;
    }

    private Node3D MakeMark(HubMark m)
    {
        var root = new Node3D { Name = "Mark_" + m.Id };
        AddChild(root);
        switch (m.Kind)
        {
            case "start":
            case "finish":
            case "checkpoint":
            {
                Color colour = m.Kind switch
                {
                    "start" => new Color(0.3f, 1f, 0.45f, 0.35f),
                    "finish" => new Color(1f, 1f, 1f, 0.4f),
                    _ => new Color(1f, 0.75f, 0.2f, 0.22f),
                };
                var ring = new MeshInstance3D
                {
                    Name = "Ring",
                    Mesh = new CylinderMesh
                    {
                        TopRadius = M(m.Radius), BottomRadius = M(m.Radius), Height = M(6f), RadialSegments = 48, Rings = 1,
                    },
                    MaterialOverride = Material(colour, glow: true, unshaded: true),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                root.AddChild(ring);
                ring.GlobalPosition = UeSpace.ToGodot(m.At + new FVector(0f, 0f, 4f));

                // A post at the gate so it shows from afar.
                Box(root, m.At + new FVector(0f, 0f, 160f), new FVector(12f, 12f, 320f), Material(colour with { A = 0.9f }, glow: true));
                break;
            }

            case "firingline":
                Box(root, m.At + new FVector(0f, 0f, 2f), new FVector(16f, m.Radius * 2f, 4f), Material(new Color(1f, 0.85f, 0.1f, 0.9f), glow: true, unshaded: true));
                break;
            case "jukebox":
            {
                Box(root, m.At + new FVector(0f, 0f, 64f), new FVector(72f, 48f, 128f), Material(new Color(0.12f, 0.1f, 0.16f)));
                Box(root, m.At + new FVector(-25f, 0f, 96f), new FVector(4f, 40f, 40f), Material(new Color(0.9f, 0.3f, 1f), glow: true));
                Box(root, m.At + new FVector(-25f, 0f, 40f), new FVector(4f, 40f, 24f), Material(new Color(0.2f, 0.9f, 1f), glow: true));
                break;
            }

            case "goal":
            {
                var post = Material(new Color(0.95f, 0.95f, 0.95f));
                float w = m.Radius;
                Box(root, m.At + new FVector(0f, -w, 60f), new FVector(10f, 10f, 120f), post);
                Box(root, m.At + new FVector(0f, w, 60f), new FVector(10f, 10f, 120f), post);
                Box(root, m.At + new FVector(0f, 0f, 120f), new FVector(10f, w * 2f + 10f, 10f), post);
                Box(root, m.At + new FVector(0f, 0f, 1f), new FVector(24f, w * 2f, 2f), Material(new Color(1f, 1f, 1f, 0.6f), unshaded: true));
                break;
            }
        }

        return root;
    }

    private void HighlightNextCheckpoint(HubPlayState play)
    {
        int index = 0;
        foreach (HubMark m in play.Marks)
        {
            if (m.Kind != "checkpoint" || !_marks.TryGetValue(m.Id, out Node3D? node))
            {
                continue;
            }

            index++;
            bool next = play.Running && index == play.Passed + 1;
            bool done = play.Running && index <= play.Passed;
            node.Visible = !done;
            float s = next ? 1f + 0.08f * Mathf.Sin((float)play.Now * 6f) : 1f;
            node.Scale = new Vector3(s, 1f, s);
        }
    }

    private void DrawTargets(HubPlayState play)
    {
        int me = Client?.LocalPlayer?.PlayerReplicationInfo?.PlayerID ?? -1;
        foreach (HubTargetView t in play.Targets.Values)
        {
            if (!_targets.TryGetValue(t.Id, out Node3D? node))
            {
                node = MakeTarget(t.Owner == me);
                _targets[t.Id] = node;
            }

            // Flip up and down about the foot over a fifth of a second.
            float since = (float)(play.Now - t.ChangedAt);
            float raise = Mathf.Clamp(since / 0.2f, 0f, 1f);
            float angle = t.Up ? (1f - raise) * 90f : raise * 90f;
            node.Visible = t.Up || raise < 1f;
            node.GlobalPosition = UeSpace.ToGodot(t.Foot);
            node.RotationDegrees = new Vector3(0f, 0f, angle);
        }
    }

    private Node3D MakeTarget(bool mine)
    {
        var root = new Node3D { Name = "Target" };
        AddChild(root);
        Color body = mine ? new Color(0.85f, 0.15f, 0.12f) : new Color(0.45f, 0.45f, 0.48f);
        Color head = mine ? new Color(1f, 0.85f, 0.2f) : new Color(0.6f, 0.6f, 0.62f);
        float d = HubPlayK.TargetHalfDepth * 2f;
        AddLocalBox(root, new FVector(0f, 0f, HubPlayK.TargetBodyHeight / 2f), new FVector(d, HubPlayK.TargetHalfWidth * 2f, HubPlayK.TargetBodyHeight), Material(body, glow: mine));
        float hz = HubPlayK.TargetBodyHeight + HubPlayK.TargetHeadGap + HubPlayK.TargetHeadHeight / 2f;
        AddLocalBox(root, new FVector(0f, 0f, hz), new FVector(d, HubPlayK.TargetHeadHalfWidth * 2f, HubPlayK.TargetHeadHeight), Material(head, glow: mine));
        return root;
    }

    private static void AddLocalBox(Node3D parent, FVector centre, FVector size, StandardMaterial3D material)
    {
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(M(size.X), M(size.Z), M(size.Y)) },
            MaterialOverride = material,
            Position = new Vector3(M(centre.X), M(centre.Z), M(centre.Y)),
        };
        parent.AddChild(mesh);
    }

    private void DrawBall(HubPlayState play)
    {
        if (!play.HasBall)
        {
            return;
        }

        if (_ball is null)
        {
            _ball = new MeshInstance3D
            {
                Name = "Ball",
                Mesh = new SphereMesh { Radius = M(HubPlayK.BallRadius), Height = M(HubPlayK.BallRadius * 2f) },
                MaterialOverride = Material(new Color(1f, 0.55f, 0.1f)),
            };
            AddChild(_ball);
        }

        _ball.GlobalPosition = UeSpace.ToGodot(play.BallNow());
    }
}
