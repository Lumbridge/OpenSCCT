using System;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScctGame.Hud;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>
/// Plays SteeE offline through the play scene with a <see cref="ScriptedPilot"/> at the keys: the spy spawns
/// through the game flow, walks a planned route to a use spot, presses and releases Use, and the test watches the
/// server and the client views for the result. The listen server runs inline, so each frame runs a fixed number of
/// ticks and server state is read between them. Reports the frame costs.
/// </summary>
public abstract class PlaySmoke : SmokeTest
{
    /// <summary>Ticks run per frame (the virtual clock makes the run independent of the frame rate).</summary>
    public const int TicksPerFrame = 8;

    private const int TeardownFrames = 4;

    private PlayRoot? _root;
    private ScriptedPilot? _pilot;
    private SmokeResult _final;
    private int _teardown = -1;

    /// <summary>Simulation ticks after which the test gives up.</summary>
    protected abstract int MaxTicks { get; }

    /// <summary>The game mode the run plays (the map settings key: Story, DiskHunt, Deathmatch).</summary>
    protected virtual string Mode => "Story";

    protected virtual string Map => "SteeE";

    /// <summary>Server bots next to the player (0: none).</summary>
    protected virtual int Bots => 0;

    /// <summary>False when the player stands still and only the checks run (no scripted walk).</summary>
    protected virtual bool Piloted => true;

    /// <summary>The use spot and the point to face, on the client's world.</summary>
    protected abstract (FVector Spot, FVector Face)? Target(SPlayerController pc);

    /// <summary>Passed or failed when the run has its answer, else null (keep running).</summary>
    protected abstract SmokeResult? Check(PlayRoot root, ScriptedPilot pilot);

    /// <summary>The play scene of the run (for subclasses' checks).</summary>
    protected PlayRoot? Root => _root;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_teardown >= 0)
        {
            return ++_teardown < TeardownFrames ? SmokeResult.Running : _final;
        }

        if (_root is null)
        {
            _pilot = new ScriptedPilot(Target);
            bool headless = DisplayServer.GetName() == "headless";
            var config = new PlayConfig
            {
                Settings = new PlaySettings
                {
                    Map = Map,
                    Side = PlaySide.Spy,
                    Mode = Mode,
                    HostMode = PlayHostMode.Inline,
                    MapRoots = [.. context.App.Options.MapRoots],
                    Bots = Bots,
                },
                Render = true,
                Hud = !headless,
                TicksPerFrame = TicksPerFrame,
                CaptureFrameHistory = true,
                Pilot = Piloted ? _pilot : null,
            };
            _root = PlayRoot.Create(config);
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error)
        {
            return Finish(SmokeResult.Fail("play scene did not load: " + error));
        }

        if (!_root.IsLoaded || _pilot is null || _root.Session is not { } session)
        {
            return SmokeResult.Running;
        }

        if (session.ServerFailure is { } failure)
        {
            return Finish(SmokeResult.Fail("server failed: " + failure.Message));
        }

        if (session.LoginError is { } login)
        {
            return Finish(SmokeResult.Fail("login refused: " + login));
        }

        if (_pilot.Phase == PilotPhase.Failed)
        {
            return Finish(SmokeResult.Fail($"pilot failed: {_pilot.Failure} ({State(_root)})"));
        }

        if (Check(_root, _pilot) is { } result)
        {
            string detail = string.Create(CultureInfo.InvariantCulture,
                $"{result.Detail} load={_root.LoadSeconds:F1}s ticksPerFrame={TicksPerFrame} {_root.Stats.Summary()} pawnViews={_root.Pawns?.Views.Count ?? 0} {_root.Devices?.Summary() ?? "devices=unbound"}");
            GD.Print("SVM_PLAY " + Name + " " + detail);
            return Finish(result with { Detail = detail });
        }

        if (_root.Stats.Ticks > MaxTicks)
        {
            return Finish(SmokeResult.Fail($"no result after {MaxTicks} ticks ({State(_root)})"));
        }

        if (context.Frame % 60 == 0)
        {
            GD.Print("SVM_PLAY " + Name + " " + State(_root));
            SaveShot(context);
        }

        return SmokeResult.Running;
    }

    /// <summary>A one-line state of the run for failures and progress.</summary>
    protected string State(PlayRoot root)
    {
        PlaySession? s = root.Session;
        SPlayerController? pc = s?.LocalController;
        return string.Create(CultureInfo.InvariantCulture,
            $"tick={root.Stats.Ticks} pilot={_pilot?.Phase} wp={_pilot?.Waypoint} client={pc?.GetStateName()} server={s?.ServerController?.GetStateName()} pawn={s?.LocalPawn?.Location}");
    }

    /// <summary>
    /// With a renderer and <c>SVM_PLAY_SHOTS</c> naming a folder (keep it under local/), saves the window now and
    /// then for a look at the run.
    /// </summary>
    private void SaveShot(SmokeContext context) => SaveShot("");

    /// <summary>Saves the window now (as <see cref="SaveShot(SmokeContext)"/>), the tag added to the file name.</summary>
    protected void SaveShot(string tag)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(folder);
        Image image = _root.GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{Name}-{_root.Stats.Ticks:D5}{tag}.png")));
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _final = result;
        _teardown = 0;
        _root?.QueueFree();
        return SmokeResult.Running;
    }

    /// <summary>The use spot of a SteeE terminal whose name starts with <paramref name="name"/>, or of the switch whose Event is it.</summary>
    protected static (FVector Spot, FVector Face)? UseSpot(SPlayerController pc, string name)
    {
        ArgumentNullException.ThrowIfNull(pc);
        if (pc.Pawn is not { } pawn)
        {
            return null;
        }

        foreach (SComputerObjectiveTrigger t in pc.XLevel.AllActors<SComputerObjectiveTrigger>())
        {
            if (t.TriggerName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                t.GetUseSpot(pawn, out FVector spot, out _);
                spot.Z = t.Location.Z;
                return (spot, t.Location);
            }
        }

        foreach (SInterrupteur s in pc.XLevel.AllActors<SInterrupteur>())
        {
            if (string.Equals(s.Event.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                s.GetUseSpot(pawn, out FVector spot, out _);
                spot.Z = s.Location.Z;
                return (spot, s.Location);
            }
        }

        return null;
    }
}

/// <summary><c>--svm-smoke=play-spy</c>: the spy hacks SteeE's Office terminal; the server's objective must be taken.</summary>
public sealed class PlaySpySmoke : PlaySmoke
{
    public const string TestName = "play-spy";

    public const string Terminal = "Office";

    public override string Name => TestName;

    protected override int MaxTicks => 6000;

    protected override (FVector Spot, FVector Face)? Target(SPlayerController pc) => UseSpot(pc, Terminal);

    // Server hack seconds at which the client HUD percent is sampled (and a frame saved), terminal-hack.md rule 35.
    private static readonly float[] SampleAt = [2f, 4f, 5f, 6f, 8f];

    private readonly System.Text.StringBuilder _samples = new();
    private int _nextSample;

    protected override SmokeResult? Check(PlayRoot root, ScriptedPilot pilot)
    {
        PlaySession session = root.Session!;
        (bool taken, string state, float hacked, float level) = session.ServerSnapshot(host =>
        {
            foreach (SComputerObjectiveTrigger t in host.World.Level.AllActors<SComputerObjectiveTrigger>())
            {
                if (t.TriggerName.StartsWith(Terminal, StringComparison.OrdinalIgnoreCase))
                {
                    // The objective counts as taken before the match resets it, so the hack shows in the terminal's skin.
                    bool hacked = t.Objective is { ObjectiveTaken: true } && t.SkinState == SComputerObjectiveTrigger.SKIN_Hacked;
                    float run = t.bTriggering ? t.DeltaCaptureTime : -1f;
                    return (hacked, $"{t.TriggerName} state={t.GetStateName()} skin={t.SkinState}", run, t.Level.TimeSeconds);
                }
            }

            return (false, "no terminal " + Terminal, -1f, 0f);
        });
        if (!taken && hacked >= 0f && session.LocalController is { } pc)
        {
            // The percent the local player's HUD shows, read from the client world as the HUD reads it.
            var snap = new HudSnapshot();
            HudSimReader.Read(pc, snap);
            int percent = snap.Objectives.Find(o => o.Name.StartsWith(Terminal, StringComparison.OrdinalIgnoreCase))?.Percent ?? -1;
            if (percent >= 99 && hacked < 9f)
            {
                return SmokeResult.Fail(string.Create(CultureInfo.InvariantCulture,
                    $"HUD shows {percent} % after {hacked:F2} s of hacking (level time {level:F1} s)"));
            }

            if (_nextSample < SampleAt.Length && hacked >= SampleAt[_nextSample])
            {
                int expect = (int)(SampleAt[_nextSample] * 10f);
                if (percent < expect - 5 || percent > expect + 5)
                {
                    return SmokeResult.Fail(string.Create(CultureInfo.InvariantCulture,
                        $"HUD shows {percent} % after {hacked:F2} s of hacking, expected about {expect} (level time {level:F1} s)"));
                }

                _samples.Append(CultureInfo.InvariantCulture, $" {hacked:F1}s(level {level:F1}s)={percent}%");
                SaveShot(string.Create(CultureInfo.InvariantCulture, $"-hack{SampleAt[_nextSample]:F0}s-{percent}pct"));
                _nextSample++;
            }
        }

        if (!taken)
        {
            return null;
        }

        if (_nextSample < SampleAt.Length)
        {
            return SmokeResult.Fail($"objective taken before every HUD percent sample:{_samples}");
        }

        string views = root.Pawns is { } pawns && root.Session?.LocalPawn is { } pawn && pawns.ViewOf(pawn) is { } v
            ? $"pawnView=ok anim0={pawn.PawnAnim[0].Sequence} drawn={v.Visible}"
            : "pawnView=none";
        return SmokeResult.Pass($"objective taken: {state} {views} hacked at tick {root.Stats.Ticks} hud percent:{_samples}");
    }
}

/// <summary>
/// <c>--svm-smoke=play-crane</c>: the spy activates SteeE's crane switch (Event blast). The crane plays its
/// sequence, whose blast01 notify swaps the courtyard wall (sqt_murkc_avant to sqt_murkc_apres) and shows the
/// collapsing wall; the server and the client's views must both show it.
/// </summary>
public sealed class PlayCraneSmoke : PlaySmoke
{
    public const string TestName = "play-crane";

    public const string SwitchEvent = "blast";

    public const string WallBefore = "SQU13_STM.Objects.sqt_murkc_avant";

    public const string WallAfter = "SQU13_STM.Objects.sqt_murkc_apres";

    public override string Name => TestName;

    protected override int MaxTicks => 7000;

    protected override (FVector Spot, FVector Face)? Target(SPlayerController pc) => UseSpot(pc, SwitchEvent);

    protected override SmokeResult? Check(PlayRoot root, ScriptedPilot pilot)
    {
        PlaySession session = root.Session!;
        (bool serverSwapped, bool serverWallShown, string serverState) = session.ServerSnapshot(host =>
        {
            bool swapped = false;
            bool shown = false;
            string crane = "no crane";
            foreach (SGamePlayObject g in host.World.Level.AllActors<SGamePlayObject>())
            {
                if (string.Equals(g.Tag.ToString(), "blast01", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(g.StaticMesh.Path, WallAfter, StringComparison.OrdinalIgnoreCase))
                {
                    swapped = true;
                }
            }

            foreach (SAnimatedMesh m in host.World.Level.AllActors<SAnimatedMesh>())
            {
                string tag = m.Tag.ToString();
                if (string.Equals(tag, "blast01", StringComparison.OrdinalIgnoreCase))
                {
                    shown = !m.bHidden;
                }
                else if (string.Equals(tag, "blast", StringComparison.OrdinalIgnoreCase))
                {
                    crane = $"crane state={m.GetStateName()} seq={m.AnimSequence?.Name} frame={m.AnimFrame:F3}";
                }
            }

            return (swapped, shown, crane);
        });
        if (!serverSwapped || !serverWallShown)
        {
            return null;
        }

        // The client: the replicated wall mesh, the wall node swapped (or hidden when the render lacks the mesh),
        // the crane and the collapsing wall playing their sequences in the views.
        Actor? clientWall = null;
        foreach (SGamePlayObject g in session.Client.World.Level.AllActors<SGamePlayObject>())
        {
            if (string.Equals(g.Tag.ToString(), "blast01", StringComparison.OrdinalIgnoreCase) && g.StaticMesh.Path is { } p
                && (string.Equals(p, WallAfter, StringComparison.OrdinalIgnoreCase) || string.Equals(p, WallBefore, StringComparison.OrdinalIgnoreCase)))
            {
                clientWall = g;
            }
        }

        if (clientWall is null || !string.Equals(clientWall.StaticMesh.Path, WallAfter, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        DeviceViews? devices = root.Devices;
        if (devices is null)
        {
            return SmokeResult.Fail("the device views never bound to the client map");
        }

        MeshFollower? wallNode = null;
        foreach (MeshFollower m in devices.Meshes)
        {
            if (ReferenceEquals(m.Actor, clientWall))
            {
                wallNode = m;
            }
        }

        AnimatedMeshView? craneView = null;
        AnimatedMeshView? wallView = null;
        foreach (AnimatedMeshView a in devices.Animated)
        {
            string tag = a.Actor.Tag.ToString();
            if (string.Equals(tag, "blast", StringComparison.OrdinalIgnoreCase))
            {
                craneView = a;
            }
            else if (string.Equals(tag, "blast01", StringComparison.OrdinalIgnoreCase))
            {
                wallView = a;
            }
        }

        if (wallNode is null || !string.Equals(wallNode.ShownAsset, WallAfter, StringComparison.OrdinalIgnoreCase))
        {
            return SmokeResult.Fail($"the wall's view did not take the swapped mesh (shown {wallNode?.ShownAsset ?? "no node"})");
        }

        if (craneView is null || craneView.Starts < 1)
        {
            return SmokeResult.Fail($"the crane's view never started its sequence ({craneView?.Sequence ?? "no view"}; {serverState})");
        }

        if (wallView is null || wallView.Starts < 1 || !wallView.Visible)
        {
            return SmokeResult.Fail($"the collapsing wall is not shown playing (view {wallView?.Sequence ?? "none"} starts={wallView?.Starts} visible={wallView?.Visible})");
        }

        if (devices.Attached.RenderOnlyBoneLights != 5 || root.Map is not { } map)
        {
            return SmokeResult.Fail("the five crane warning-light attachments did not bind");
        }

        string[] flares = ["Light386", "Light397", "Light400", "Light403", "Light372"];
        for (int i = 0; i < flares.Length; i++)
        {
            int bone = craneView.Model.Skeleton.FindBone("Dummy0" + (i + 1));
            if (bone < 0 || !map.TryGetLightPlace(flares[i], out FVector actual, out _))
            {
                return SmokeResult.Fail(flares[i] + " never followed its crane bone");
            }

            var transform = craneView.Model.Skeleton.GlobalTransform * craneView.Model.Skeleton.GetBoneGlobalPose(bone);
            if (FVector.Dist(actual, UeSpace.ToUe(transform.Origin)) > .1f)
            {
                return SmokeResult.Fail(flares[i] + " detached from the animated crane boom");
            }

            if (map.LightSprites is not { } sprites || !sprites.Nodes.TryGetValue(flares[i], out MeshInstance3D? sprite) ||
                !sprite.Visible || sprite.Mesh is not QuadMesh ||
                sprite.MaterialOverride is not ShaderMaterial { Shader: { } shader } material ||
                !shader.Code.Contains("#define BILLBOARD", StringComparison.Ordinal) ||
                !shader.Code.Contains("#define BLEND_ADDITIVE", StringComparison.Ordinal) ||
                material.GetShaderParameter("tex_diffuse").AsGodotObject() is not Texture2D ||
                sprite.GlobalPosition.DistanceTo(transform.Origin) > .001f)
            {
                return SmokeResult.Fail(flares[i] + " did not draw its additive flare sprite on the crane boom");
            }
        }

        string missing = devices.MissingMeshes.Count > 0 ? " missingMeshes=" + string.Join(",", devices.MissingMeshes) : "";
        return SmokeResult.Pass(string.Create(CultureInfo.InvariantCulture,
            $"blast01 fired: wall {WallAfter} on server and client, wall node shows it (drawn={wallNode.Entry.Node.Visible}), crane view {craneView.Sequence} frame={craneView.Frame:F2}, wall view {wallView.Sequence} frame={wallView.Frame:F2}; {serverState} at tick {root.Stats.Ticks}{missing}"));
    }
}
