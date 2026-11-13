using System;
using Godot;
using OpenScct.Core;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;
using OpenScctGame.Play.Session;
using CK = OpenScct.Game.Combat.K;

namespace OpenScctGame.Play;

/// <summary>
/// The practice console command <c>grabarea</c> (an OpenSCCT debug aid, not in the original): draws on the floor around
/// every living mercenary where a spy can stand to grab him (spec/combat/spy-grab.md rule 4), as this machine's own grab
/// test sees him now. The bright sector is where a spy facing straight at the mercenary can grab; the faint one is
/// where he still can with his body turned by up to the front cone. The colour says what Use would do now: green
/// grabs, yellow is out of position, red is a mercenary who cannot be grabbed (just got up, shocked, or in a state
/// that refuses it). Drawn in 2D from projected points, so it shows through walls and does not depend on the world
/// renderer. It draws nothing outside a practice round. The sector is the angular guide; the colour also checks
/// the actual ground, headroom and path probes used by the local player's grab.
/// </summary>
public sealed partial class GrabAreaOverlay : Control
{
    private const int Segments = 24;
    private const float FloorLift = 2f;
    private static readonly Color GrabNow = new(0.2f, 1f, 0.3f);
    private static readonly Color OutOfPlace = new(1f, 0.85f, 0.2f);
    private static readonly Color Refused = new(1f, 0.25f, 0.2f);
    private const string Legend =
        "grabarea: green = Use grabs now, yellow = not in position, red = cannot be grabbed (just got up, shocked or busy). " +
        "Bright = facing him, faint = with your body turned.";

    private readonly Func<PlaySession?> _session;
    private readonly Func<Camera3D?> _camera;
    private readonly Vector2[] _points = new Vector2[Segments + 2];
    private readonly bool[] _behind = new bool[Segments + 2];
    private readonly Vector2[] _triangle = new Vector2[3];
    private readonly Color[] _triangleColours = new Color[3];
    public bool ShowLegend { get; set; } = true;

    public GrabAreaOverlay(Func<PlaySession?> session, Func<Camera3D?> camera)
    {
        _session = session;
        _camera = camera;
        Name = "GrabAreaOverlay";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Whether <paramref name="session"/> is a practice round on this machine.</summary>
    public static bool Allowed(PlaySession? session) =>
        session?.Client.World.Level.GetService<PracticeRules>()?.On == true;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        PlaySession? session = _session();
        if (!Allowed(session) || _camera() is not { } camera)
        {
            return;
        }

        SPlayerController? pc = session!.LocalController;
        SBasePawn? me = session.LocalPawn;
        float reach = pc?.fGrabDistance ?? 160f;
        int shift = pc?.iGrabAreaShift ?? 7200;
        float half = MathF.Acos(Math.Clamp(pc?.fGrabHalfAngle ?? 0.5f, -1f, 1f));
        float turned = half + MathF.Acos(CK.GrabFrontCone);
        float now = session.Client.World.Level.TimeSeconds;
        Vector2 scale = Size / camera.GetViewport().GetVisibleRect().Size;
        foreach (SPAWNDEFENSE merc in session.Client.World.Level.AllActors<SPAWNDEFENSE>())
        {
            if (merc.Health <= 0 || merc.bDeleteMe || merc.bHidden || ReferenceEquals(merc, me))
            {
                continue;
            }

            bool refused = !merc.bCanBeGrabbed || merc.bStickyShocking || !(now - merc.iLastWakeTime > CK.GrabWakeProtection);
            bool ready = !refused && pc is not null && me is SPawnAttaque { Health: > 0 } && pc.DescribeGrab(merc) == "Grab available.";
            Color colour = refused ? Refused : ready ? GrabNow : OutOfPlace;
            FVector feet = merc.Location;
            feet.Z -= merc.CollisionHeight - FloorLift;
            // The spy stands on the side opposite the shifted facing.
            int back = merc.Rotation.Yaw + shift + 32768;
            Sector(camera, scale, feet, back, MathF.Min(turned, MathF.PI), reach, colour with { A = 0.12f }, false);
            Sector(camera, scale, feet, back, half, reach, colour with { A = 0.3f }, true);
            // Where his body faces.
            Line(camera, scale, feet, feet + new FRotator(0, merc.Rotation.Yaw, 0).Vector() * (reach * 0.5f), Colors.White);
        }

        if (ShowLegend) DrawString(ThemeDB.FallbackFont, new Vector2(16f, Size.Y - 16f), Legend, HorizontalAlignment.Left, -1f, 14, Colors.White);
    }

    private void Sector(Camera3D camera, Vector2 scale, FVector centre, int yaw, float halfAngle, float radius, Color fill, bool outline)
    {
        Project(camera, scale, centre, 0);
        for (int i = 0; i <= Segments; i++)
        {
            float a = -halfAngle + 2f * halfAngle * i / Segments;
            int y = yaw + (int)(a * 32768f / MathF.PI);
            Project(camera, scale, centre + new FRotator(0, y, 0).Vector() * radius, i + 1);
        }

        _triangleColours[0] = _triangleColours[1] = _triangleColours[2] = fill;
        for (int i = 1; i <= Segments; i++)
        {
            if (_behind[0] || _behind[i] || _behind[i + 1])
            {
                continue;
            }

            _triangle[0] = _points[0];
            _triangle[1] = _points[i];
            _triangle[2] = _points[i + 1];
            DrawPrimitive(_triangle, _triangleColours, null);
        }

        if (!outline)
        {
            return;
        }

        Color edge = fill with { A = 0.9f };
        for (int i = 0; i <= Segments + 1; i++)
        {
            int next = i == Segments + 1 ? 0 : i + 1;
            if (!_behind[i] && !_behind[next])
            {
                DrawLine(_points[i], _points[next], edge, 2f);
            }
        }
    }

    private void Line(Camera3D camera, Vector2 scale, FVector from, FVector to, Color colour)
    {
        Vector3 a = UeSpace.ToGodot(from), b = UeSpace.ToGodot(to);
        if (!camera.IsPositionBehind(a) && !camera.IsPositionBehind(b))
        {
            DrawLine(camera.UnprojectPosition(a) * scale, camera.UnprojectPosition(b) * scale, colour, 2f);
        }
    }

    private void Project(Camera3D camera, Vector2 scale, FVector point, int index)
    {
        Vector3 p = UeSpace.ToGodot(point);
        _behind[index] = camera.IsPositionBehind(p);
        _points[index] = _behind[index] ? default : camera.UnprojectPosition(p) * scale;
    }
}
