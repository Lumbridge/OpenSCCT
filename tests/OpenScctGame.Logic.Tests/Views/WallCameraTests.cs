using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests.Views;

/// <summary>
/// The third-person camera next to walls against the original game: frames captured with
/// tools/golden/capture_game.py at poses near the SteeE spy spawn (the spy crouched, the controller turned to the pose's
/// yaw) record where the game put the camera (SPlayerController.vCamLocation). The play client puts the spy at the same
/// place and turn and its camera (the calc view of the client tick) must land within a few units of the game's. One
/// pose has a wall right behind the spy (crouch-by-wall-2), where the game pulls the camera in to m_absoluteMinDist
/// (30 uu, inside the spy's cylinder) and draws the spy's back filling the frame; in another (crouch-by-wall) the
/// crouched spy is out of the camera's sight behind a low wall in the game too. The captures are game-derived data under local/; the test is skipped without them.
/// </summary>
public sealed class WallCameraTests
{
    /// <summary>
    /// The camera may differ from the game's by this much (uu), next to walls as in the open: every pose matches to
    /// 0.1 uu since the collision distance is rate limited from the last frame's distance and the second clearance hit
    /// is held at m_absoluteMinDist (editor 0x111f56a0). Before, the camera stood 6 to 11 uu off next to walls.
    /// </summary>
    private const float Tolerance = 0.5f;

    /// <summary>In views where the game's camera is at its full 250 uu or in the open, the match must be this close.</summary>
    private const float OpenTolerance = 0.5f;

    private readonly ITestOutputHelper _output;

    public WallCameraTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static string CapturePath =>
        Path.Combine(RepoRoot(), "local", "golden", "game-wall", "SteeE", "cameras.json");

    [Fact]
    public void CameraNextToWallsMatchesTheGame()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        Assert.SkipWhen(!File.Exists(CapturePath), "no wall camera capture (python tools/golden/capture_game.py --poses tools/golden/poses/SteeE-walls.json --out local/golden/game-wall)");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(CapturePath));
        var failures = new List<string>();
        foreach (JsonProperty shot in doc.RootElement.GetProperty("shots").EnumerateObject())
        {
            JsonElement cam = shot.Value.GetProperty("camera");
            JsonElement pawn = shot.Value.GetProperty("pawn");
            FVector gameCamera = Vec(cam.GetProperty("location"));
            FVector pawnLocation = Vec(pawn.GetProperty("location"));
            JsonElement rot = cam.GetProperty("controllerRotation");
            var view = new FRotator(rot[0].GetInt32(), rot[1].GetInt32(), 0);
            FVector ours = Solve(pawnLocation, view);
            float error = FVector.Dist(ours, gameCamera);
            float gameDistance = (gameCamera - pawnLocation).Size2D();
            float ourDistance = (ours - pawnLocation).Size2D();
            string line = string.Create(CultureInfo.InvariantCulture,
                $"{shot.Name}: game camera {gameCamera} ({gameDistance:F1} uu from the pawn's axis), ours {ours} ({ourDistance:F1}), off by {error:F1} uu");
            _output.WriteLine(line);
            bool open = gameDistance >= 249f || shot.Name.Contains("look", StringComparison.Ordinal);
            if (!(error <= (open ? OpenTolerance : Tolerance)))
            {
                failures.Add(line);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>The client's camera with the local spy held at <paramref name="location"/> and the view at <paramref name="yaw"/>.</summary>
    private static FVector Solve(FVector location, FRotator view)
    {
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings { Map = "SteeE", HostMode = PlayHostMode.Inline, Bots = 0 }, clock);
        FVector camera = default;
        for (int t = 1; t <= 12 * Sim.Hz; t++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(t, Sim.Hz));
            if (t > 3 * Sim.Hz)
            {
                Hold(session, location, view);
            }

            session.Step(clock.NowMicroseconds);
            if (t > 3 * Sim.Hz && session.View.Valid)
            {
                camera = session.View.Location;
            }
        }

        return camera;
    }

    private static void Hold(PlaySession session, FVector location, FRotator view)
    {
        SetPawn(session.ServerController?.Pawn, location, view.Yaw);
        SetPawn(session.LocalPawn, location, view.Yaw);
        if (session.LocalController is { } pc)
        {
            pc.Rotation = view;
        }
    }

    private static void SetPawn(Pawn? pawn, FVector location, int yaw)
    {
        if (pawn is null)
        {
            return;
        }

        // A little over the recorded spot, which sits exactly on the floor (the move to it would be blocked).
        // The captures are taken at the spy spawn, where the pawn already stands; a move is only needed elsewhere.
        if (FVector.Dist(pawn.Location, location) > 2f)
        {
            pawn.SetLocation(location);
        }

        pawn.Velocity = default;
        pawn.Acceleration = default;
        pawn.Rotation = new FRotator(0, yaw, 0);
    }

    private static FVector Vec(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "OpenSCCT.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? Directory.GetCurrentDirectory();
    }
}
