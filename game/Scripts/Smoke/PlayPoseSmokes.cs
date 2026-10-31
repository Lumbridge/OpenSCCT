using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>
/// What a drawn pawn looks like over a run: how far its drawn bones moved in the skeleton's own frame (so a turn of the
/// whole pawn does not count) in each half second it moved, how its lowest foot bone sits on the floor it stands on, and which walk set clips it showed. Read from the
/// drawn skeleton of its <see cref="SimPawnView"/> and the channels the view posed it from.
/// </summary>
public sealed class DrawnPoseTrack
{
    /// <summary>Half-second windows, as the inspector's animation check.</summary>
    public const double WindowSeconds = 0.5;

    /// <summary>A pawn moving faster than this (uu/s) over a window counts as moving.</summary>
    public const float MovingSpeed = 60f;

    /// <summary>A drawn bone moving more than this (uu, relative to the skeleton) in a window counts as animated.</summary>
    public const float AnimatedBoneMotion = 0.5f;

    private Vector3[] _start = [];
    private Vector3[] _now = [];
    private Vector3[] _clipStart = [];
    private Name _clip;
    private FVector _startLocation;
    private double _time;
    private float _boneExcursion;
    private bool _open;

    public DrawnPoseTrack(SimPawnView view, string label)
    {
        View = view;
        Label = label;
    }

    public SimPawnView View { get; }

    public string Label { get; }

    public int MovingWindows { get; private set; }

    public int AnimatedWindows { get; private set; }

    public List<string> FrozenWindows { get; } = new();

    /// <summary>Largest bone displacement within each base clip, including actions played while stationary.</summary>
    public Dictionary<Name, float> ClipBoneMotion { get; } = new();

    /// <summary>The lowest foot bone over the pawn's floor (cylinder bottom while walking), lowest and highest seen, uu.</summary>
    public float MinFoot { get; private set; } = float.NaN;

    public float MaxFoot { get; private set; } = float.NaN;

    /// <summary>What the pawn was doing when its feet were lowest (stance, channel 0 and the strongest walk channel).</summary>
    public string MinFootAt { get; private set; } = "";

    /// <summary>The lowest foot while crouched outside the steady stance (turning in place, tweening, part blends; reported, not judged).</summary>
    public float MinTransitionCrouchedFoot { get; private set; } = float.NaN;

    /// <summary>Foot samples taken while crouched on the floor.</summary>
    public int CrouchedFootSamples { get; private set; }

    /// <summary>The strongest walk channel's clip while moving, by stance ("Cr" or "St"), with the frames it showed.</summary>
    public Dictionary<string, int> MovingClips { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The lowest foot over the floor (lowest and highest seen, uu) while channel 0 plays a crouch or stand-up clip
    /// (WaitCrAlBg0, WaitCrAlEd0 and the armed WaitCrSpBg2, WaitCrSpEd2), standing still.
    /// </summary>
    public Dictionary<string, (float Min, float Max)> StanceChangeFeet { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True for the crouch and stand-up clips of channel 0.</summary>
    public static bool IsStanceChange(string sequence) =>
        sequence.StartsWith("WaitCrAlBg", StringComparison.OrdinalIgnoreCase) || sequence.StartsWith("WaitCrAlEd", StringComparison.OrdinalIgnoreCase) ||
        sequence.StartsWith("WaitCrSpBg", StringComparison.OrdinalIgnoreCase) || sequence.StartsWith("WaitCrSpEd", StringComparison.OrdinalIgnoreCase);

    /// <summary>Channel 0's clip while standing still, by stance, with the frames it showed.</summary>
    public Dictionary<string, int> RestClips { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One frame, after the play scene drew. Feet are measured in the steady stance only: with
    /// <paramref name="crouchedOnly"/> crouched (still or walking), else standing or crouched at rest (another
    /// player's pawn walking over steps or with its smoothing under way is left out). The floor is a trace down on
    /// <paramref name="collision"/> (the cylinder bottom without it).
    /// </summary>
    public void Frame(double dt, bool crouchedOnly, CollisionWorld? collision)
    {
        if (View.Pawn is not { } pawn || View.Model is not { } model || pawn.bDeleteMe || !GodotObject.IsInstanceValid(View))
        {
            _open = false;
            return;
        }

        Skeleton3D skeleton = model.Skeleton;
        int count = skeleton.GetBoneCount();
        if (_now.Length != count)
        {
            _now = new Vector3[count];
            _start = new Vector3[count];
            _clipStart = new Vector3[count];
            _open = false;
        }

        Transform3D world = skeleton.GlobalTransform;
        float lowest = float.MaxValue;
        for (int b = 0; b < count; b++)
        {
            Vector3 local = skeleton.GetBoneGlobalPose(b).Origin;
            // In the skeleton's own frame (scaled to uu): the pawn turning does not count as its bones moving.
            _now[b] = local * world.Basis.Scale.X * 100f;
            string name = skeleton.GetBoneName(b);
            if (name.Contains("Foot", StringComparison.OrdinalIgnoreCase) || name.Contains("Toe", StringComparison.OrdinalIgnoreCase))
            {
                lowest = Math.Min(lowest, (world * local).Y * 100f);
            }
        }

        Name clip = pawn.PawnAnim[0].Sequence;
        if (_clip != clip)
        {
            _clip = clip;
            Array.Copy(_now, _clipStart, count);
        }
        else
        {
            ClipBoneMotion.TryGetValue(clip, out float excursion);
            for (int b = 0; b < count; b++) excursion = Math.Max(excursion, _now[b].DistanceTo(_clipStart[b]));
            ClipBoneMotion[clip] = excursion;
        }

        string stance = pawn.bIsCrouched ? "Cr" : "St";
        FVector v = pawn.Velocity;
        bool moving = pawn.Physics == EPhysics.PHYS_Walking && MathF.Sqrt((v.X * v.X) + (v.Y * v.Y)) > MovingSpeed;
        // The steady stance is measured here: not turning in place (channels 2 and 3), channel 0 not tweening and the
        // walk set not part way blended in or out (a part blend of two poses' local rotations bends the legs through
        // lower foot heights; those are reported apart).
        PawnAnimation pa0 = pawn.PawnAnim;
        bool turning = pa0[2].BlendAlpha > 0f || pa0[3].BlendAlpha > 0f;
        float walkAlpha = Math.Max(pa0[4].BlendAlpha, pa0[5].BlendAlpha);
        bool blending = walkAlpha > 0.05f && walkAlpha < 0.95f;
        bool steady = !turning && !blending && pa0[0].Frame >= 0f;
        if (!steady && pawn.bIsCrouched && pawn.Physics == EPhysics.PHYS_Walking && lowest < float.MaxValue)
        {
            float turnOffset = lowest - (pawn.Location.Z - pawn.CollisionHeight);
            MinTransitionCrouchedFoot = float.IsNaN(MinTransitionCrouchedFoot) ? turnOffset : Math.Min(MinTransitionCrouchedFoot, turnOffset);
        }

        bool measured = steady && (crouchedOnly ? pawn.bIsCrouched : !moving && v.SizeSquared() < 1f);
        if (pawn.Physics == EPhysics.PHYS_Walking && lowest < float.MaxValue && measured)
        {
            float floor = Floor(pawn, collision);
            float offset = lowest - floor;
            if (float.IsNaN(MinFoot) || offset < MinFoot)
            {
                PawnAnimation pa = pawn.PawnAnim;
                MinFootAt = string.Create(CultureInfo.InvariantCulture,
                    $"{stance} c0 {pa[0].Sequence}@{pa[0].Frame:F2} c1 {pa[1].Sequence}@{pa[1].BlendAlpha:F2} c2 {pa[2].Sequence}@{pa[2].BlendAlpha:F2} c3 {pa[3].Sequence}@{pa[3].BlendAlpha:F2} c14 {pa[14].Sequence}@{pa[14].BlendAlpha:F2} c4 {pa[4].Sequence}@{pa[4].BlendAlpha:F2} c5 {pa[5].Sequence}@{pa[5].BlendAlpha:F2} c15 {pa[15].Sequence}@{pa[15].BlendAlpha:F2} speed {MathF.Sqrt((v.X * v.X) + (v.Y * v.Y)):F0}");
            }

            MinFoot = float.IsNaN(MinFoot) ? offset : Math.Min(MinFoot, offset);
            MaxFoot = float.IsNaN(MaxFoot) ? offset : Math.Max(MaxFoot, offset);
            if (pawn.bIsCrouched)
            {
                CrouchedFootSamples++;
            }
        }

        PawnAnimation a = pawn.PawnAnim;
        string c0 = a[0].Sequence.ToString();
        if (pawn.Physics == EPhysics.PHYS_Walking && lowest < float.MaxValue && !moving && IsStanceChange(c0))
        {
            float offset = lowest - Floor(pawn, collision);
            (float lo, float hi) = StanceChangeFeet.TryGetValue(c0, out var r) ? r : (float.MaxValue, float.MinValue);
            StanceChangeFeet[c0] = (Math.Min(lo, offset), Math.Max(hi, offset));
        }

        if (moving)
        {
            int walk = a[4].BlendAlpha >= a[5].BlendAlpha ? 4 : 5;
            if (a[walk].BlendAlpha > 0.3f && !a[walk].Sequence.IsNone)
            {
                Count(MovingClips, stance + " " + a[walk].Sequence);
            }
        }
        else if (pawn.Physics == EPhysics.PHYS_Walking && !a[0].Sequence.IsNone)
        {
            Count(RestClips, stance + " " + a[0].Sequence);
        }

        if (!_open)
        {
            Array.Copy(_now, _start, count);
            _startLocation = pawn.Location;
            _time = 0;
            _boneExcursion = 0f;
            _open = true;
            return;
        }

        // A complete walk cycle can end on its starting pose. Measure the excursion throughout
        // the window, rather than mistaking matching end points for a frozen skeleton.
        for (int b = 0; b < count; b++)
        {
            _boneExcursion = Math.Max(_boneExcursion, (_now[b] - _start[b]).Length());
        }

        _time += dt;
        if (_time < WindowSeconds)
        {
            return;
        }

        _open = false;
        FVector moved = pawn.Location - _startLocation;
        float speed = MathF.Sqrt((moved.X * moved.X) + (moved.Y * moved.Y)) / (float)_time;
        if (speed < MovingSpeed)
        {
            return;
        }

        MovingWindows++;
        if (_boneExcursion >= AnimatedBoneMotion)
        {
            AnimatedWindows++;
        }
        else if (FrozenWindows.Count < 8)
        {
            FrozenWindows.Add($"{pawn.XLevel.TickCount}: speed={speed:F0} bones={_boneExcursion:F3} " +
                string.Join(", ", Enumerable.Range(0, PawnAnimation.ChannelCount).Where(i => i == 0 || a[i].BlendAlpha > 0.1f)
                    .Select(i => $"c{i}:{a[i].Sequence}@{a[i].Frame:F3}/{a[i].BlendAlpha:F2}")));
        }
    }

    /// <summary>The floor under the pawn (a trace down past the cylinder bottom), or the cylinder bottom.</summary>
    private static float Floor(SBasePawn pawn, CollisionWorld? collision)
    {
        float bottom = pawn.Location.Z - pawn.CollisionHeight;
        if (collision is null)
        {
            return bottom;
        }

        FVector start = pawn.Location;
        FVector end = start - new FVector(0f, 0f, pawn.CollisionHeight + PlayInspector.InspectFloorSearch);
        return collision.LineCheck(out FCheckResult hit, pawn, end, start, default, PlayInspector.WorldTrace) ? hit.Location.Z : bottom;
    }

    private static void Count(Dictionary<string, int> d, string key) => d[key] = (d.TryGetValue(key, out int n) ? n : 0) + 1;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Label}: animated {AnimatedWindows}/{MovingWindows} moving windows, feet {MinFoot:F1}..{MaxFoot:F1} uu over the floor (lowest at {MinFootAt}; crouched samples {CrouchedFootSamples}; crouched in transitions {MinTransitionCrouchedFoot:F1}; crouch and stand-up clips {string.Join(", ", StanceChangeFeet.Select(kv => $"{kv.Key} {kv.Value.Min:F1}..{kv.Value.Max:F1}"))}), moving clips [{Join(MovingClips)}], rest clips [{Join(RestClips)}]");

    private static string Join(Dictionary<string, int> d) => string.Join(", ", d.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} x{kv.Value}"));
}

/// <summary>
/// The shared frame of the pose smokes: SteeE as the spy set up as the launcher does (host thread, wall clock, HUD;
/// <see cref="PlayInspectSmoke.StartPlay"/>), a <see cref="KeyPilot"/> timeline once the spy is drawn walking, a
/// <see cref="DrawnPoseTrack"/> for every drawn pawn, screenshots with SVM_PLAY_SHOTS, and the verdict.
/// </summary>
public abstract class PlayPoseSmoke : SmokeTest
{
    private const int TeardownFrames = 6;

    private readonly Dictionary<SimPawnView, DrawnPoseTrack> _tracks = new(ReferenceEqualityComparer.Instance);
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private SmokeResult _final;
    private int _teardown = -1;
    private ulong _lastUsec;
    private double _seconds;
    private double _keysAt = -1;
    private double _doneAt = -1;
    private double _nextShot;
    private string _lastStep = "";

    protected abstract int Bots { get; }

    protected virtual OpenScct.Net.SimLinkProfile? NetworkLink => null;

    protected virtual double ShotInterval => 2.0;

    protected virtual PlayRoot StartPlay(SmokeContext context) => PlayInspectSmoke.StartPlay(context, Bots, link: NetworkLink);

    /// <summary>The play scene under test (null before it starts).</summary>
    protected PlayRoot? Root => _root;

    /// <summary>The key timeline, given the spy as drawn first.</summary>
    protected abstract List<KeyStep> Timeline(SBasePawn local);

    /// <summary>Only crouched foot samples of the local pawn count (the crouch smoke).</summary>
    protected virtual bool CrouchedFeetOnly => false;

    /// <summary>The verdict from the tracks once the timeline ran.</summary>
    protected abstract SmokeResult Verdict(DrawnPoseTrack? local, IReadOnlyList<DrawnPoseTrack> others);

    protected virtual bool ReadyForVerdict => true;

    /// <summary>Called each frame while the keys play (extra per-step checks).</summary>
    protected virtual void OnFrame(PlaySession session, string step)
    {
    }

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_teardown >= 0)
        {
            return ++_teardown < TeardownFrames ? SmokeResult.Running : _final;
        }

        if (_root is null)
        {
            _root = StartPlay(context);
            _lastUsec = Time.GetTicksUsec();
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error)
        {
            return Finish(SmokeResult.Fail("play scene did not load: " + error));
        }

        if (!_root.IsLoaded || _root.Session is not { } session)
        {
            return SmokeResult.Running;
        }

        if (session.ServerFailure is { } failure)
        {
            return Finish(SmokeResult.Fail("server failed: " + failure.Message));
        }

        ulong now = Time.GetTicksUsec();
        double dt = (now - _lastUsec) / 1_000_000.0;
        _lastUsec = now;
        _seconds += dt;
        SBasePawn? local = session.LocalPawn;
        if (_keys is null)
        {
            SPlayerController? pc = session.LocalController;
            bool walking = pc is not null && (pc.IsInState("s_PlayerWalkingCr0H") || pc.IsInState("s_PlayerWalkingSt0H") ||
                pc.IsInState("s_PlayerWalkingCr2H") || pc.IsInState("s_PlayerWalkingSt2H"));
            if (local is not null && _root.Pawns?.ViewOf(local) is not null && walking)
            {
                _keys = KeyPilot.Attach(_root);
                _keys.Play(Timeline(local));
                _keysAt = _seconds;
                GD.Print(string.Create(CultureInfo.InvariantCulture, $"SVM_PLAY {Name} spy drawn at {local.Location} (crouched {local.bIsCrouched}) after {context.ElapsedSeconds:F1} s"));
            }
            else if (context.ElapsedSeconds > PlayInspectSmoke.SpawnTimeoutSeconds)
            {
                return Finish(SmokeResult.Fail($"the pawn was not drawn walking after {PlayInspectSmoke.SpawnTimeoutSeconds:F0} s ({pc?.GetStateName()})"));
            }

            return SmokeResult.Running;
        }

        _keys.Update();
        if (_root.Pawns is { } pawns)
        {
            foreach (SimPawnView view in pawns.Views)
            {
                if (!_tracks.TryGetValue(view, out DrawnPoseTrack? track))
                {
                    string label = view.Pawn is { } p ? $"{p.PlayerReplicationInfo?.PlayerName ?? p.ClassName.ToString()} ({p.ClassName}{(ReferenceEquals(p, local) ? ", you" : "")})" : "pawn";
                    track = new DrawnPoseTrack(view, label);
                    _tracks[view] = track;
                }

                bool isLocal = ReferenceEquals(view.Pawn, local);
                track.Frame(dt, isLocal && CrouchedFeetOnly, session.Client.Map?.Physics?.Collision);
            }
        }

        OnFrame(session, _keys.Step);
        if (_keys.Step != _lastStep || _seconds >= _nextShot)
        {
            _lastStep = _keys.Step;
            _nextShot = _seconds + ShotInterval;
            SaveShot(_keys.Step.Length == 0 ? "end" : _keys.Step.Replace(' ', '-'));
        }

        if (!_keys.Done || !ReadyForVerdict)
        {
            return SmokeResult.Running;
        }

        if (_doneAt < 0)
        {
            _doneAt = _seconds;
        }

        if (_seconds - _doneAt < 1.0)
        {
            return SmokeResult.Running;
        }

        DrawnPoseTrack? mine = null;
        var others = new List<DrawnPoseTrack>();
        foreach (DrawnPoseTrack track in _tracks.Values)
        {
            GD.Print("SVM_POSE " + Name + " " + track);
            if (ReferenceEquals(track.View.Pawn, local))
            {
                mine = track;
            }
            else
            {
                others.Add(track);
            }
        }

        if (_keys.Missing.Count > 0)
        {
            return Finish(SmokeResult.Fail("no key bound to " + string.Join(", ", _keys.Missing)));
        }

        return Finish(Verdict(mine, others));
    }

    private void SaveShot(string label)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(folder);
        Image image = _root.GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{Name}-{_seconds - _keysAt:000.0}-{label}.png")));
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _final = result;
        _teardown = 0;
        _keys?.ReleaseAll();
        _root?.QueueFree();
        return SmokeResult.Running;
    }
}

/// <summary>
/// <c>--svm-smoke=play-crouch</c>: the spy at the keys crouches, walks crouched, stands, walks, crouches again and walks
/// crouched turning. Passes when the drawn spy's lowest foot bone stays on the floor while steadily crouched (not turning
/// in place, channel 0 not tweening, the walk set fully in or out; no deeper than 8 uu under it, no higher than 30 uu over it, the inspector's
/// bounds), the crouched walk shows the original's crouched
/// walk set (SBasePawn's AWalkCrFd0 WalkCrAlFd0 and AJoggCrFd0 JoggCrAlFd0) and the crouched rest its wait loop
/// (AWaitCr0 waitCrAlFd0), and the drawn bones move while it walks.
/// </summary>
public sealed class PlayCrouchSmoke : PlayPoseSmoke
{
    public const string TestName = "play-crouch";

    /// <summary>The inspector's floor bounds (PlayInspector.InspectFeetBelow and InspectFeetAbove).</summary>
    public const float FeetBelow = PlayInspector.InspectFeetBelow;

    public const float FeetAbove = PlayInspector.InspectFeetAbove;

    /// <summary>
    /// The crouch and stand-up clips may put the feet this far under the floor (uu): WaitCrAlEd0's own pose reaches
    /// 9 uu under at the drawn mesh height (not compared with the game); the fault it guards against, the mesh drawn
    /// at the old or new stance height for a frame, put them 25 to 37 uu off.
    /// </summary>
    public const float StanceChangeFeetBelow = 12f;

    public override string Name => TestName;

    protected override int Bots => 0;

    protected override bool CrouchedFeetOnly => true;

    protected override List<KeyStep> Timeline(SBasePawn local)
    {
        var steps = new List<KeyStep>();
        if (!local.bIsCrouched)
        {
            steps.Add(new KeyStep("crouch", 1.2) { Tap = ["bCrouch"] });
        }

        steps.AddRange(
        [
            new KeyStep("crouch still", 1.5),
            new KeyStep("crouch walk", 3.0) { Hold = ["BT_Up"] },
            new KeyStep("stand", 1.2) { Tap = ["bCrouch"] },
            new KeyStep("walk", 2.0) { Hold = ["BT_Down"] },
            new KeyStep("crouch again", 1.2) { Tap = ["bCrouch"] },
            new KeyStep("crouch walk turning", 3.0) { Hold = ["BT_Up"], LookPerSecond = new Vector2(200f, 0f) },
            new KeyStep("crouch rest", 1.5),
        ]);
        return steps;
    }

    protected override SmokeResult Verdict(DrawnPoseTrack? local, IReadOnlyList<DrawnPoseTrack> others)
    {
        if (local is null)
        {
            return SmokeResult.Fail("the spy was never drawn");
        }

        var problems = new List<string>();
        if (local.CrouchedFootSamples == 0)
        {
            problems.Add("the spy was never drawn crouched on the floor");
        }
        else if (local.MinFoot < -FeetBelow || local.MaxFoot > FeetAbove)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture,
                $"crouched feet {local.MinFoot:F1}..{local.MaxFoot:F1} uu over the floor (bounds -{FeetBelow:F0}..{FeetAbove:F0})"));
        }

        if (local.StanceChangeFeet.Count == 0)
        {
            problems.Add("the spy was never drawn in a crouch or stand-up clip");
        }

        foreach ((string clip, (float min, float max)) in local.StanceChangeFeet)
        {
            if (min < -StanceChangeFeetBelow || max > FeetAbove)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{clip}: feet {min:F1}..{max:F1} uu over the floor (bounds -{StanceChangeFeetBelow:F0}..{FeetAbove:F0})"));
            }
        }

        string[] crouchWalk = ["Cr WalkCrAlFd0", "Cr JoggCrAlFd0"];
        int crouchWalkFrames = crouchWalk.Sum(k => local.MovingClips.TryGetValue(k, out int n) ? n : 0);
        int crouchMovingFrames = local.MovingClips.Where(kv => kv.Key.StartsWith("Cr ", StringComparison.Ordinal)).Sum(kv => kv.Value);
        if (crouchMovingFrames == 0 || crouchWalkFrames < crouchMovingFrames * 9 / 10)
        {
            problems.Add($"crouched walk clips {crouchWalkFrames} of {crouchMovingFrames} frames from WalkCrAlFd0 / JoggCrAlFd0");
        }

        int restFrames = local.RestClips.Where(kv => kv.Key.StartsWith("Cr ", StringComparison.Ordinal)).Sum(kv => kv.Value);
        int waitFrames = local.RestClips.TryGetValue("Cr waitCrAlFd0", out int w) ? w : 0;
        if (restFrames == 0 || waitFrames < restFrames / 2)
        {
            problems.Add($"crouched rest on waitCrAlFd0 {waitFrames} of {restFrames} frames");
        }

        if (local.MovingWindows == 0 || local.AnimatedWindows < local.MovingWindows * 3 / 4)
        {
            problems.Add($"drawn bones moved in {local.AnimatedWindows} of {local.MovingWindows} moving windows");
        }

        return problems.Count == 0 ? SmokeResult.Pass(local.ToString()) : SmokeResult.Fail(string.Join("; ", problems) + " | " + local);
    }
}

/// <summary>
/// <c>--svm-smoke=play-proxies</c>: three bots on SteeE while the spy at the keys looks around. Every bot is another
/// player's pawn on this client (a simulated proxy animated from the relayed actions and its smoothed velocity).
/// Passes when every bot that moved was drawn with moving bones in at least three quarters of its moving windows,
/// with its walk set's clips (none frozen on the first walk channel), standing feet on the floor, and at least two
/// bots moved.
/// </summary>
public sealed class PlayProxiesSmoke : PlayPoseSmoke
{
    public const string TestName = "play-proxies";

    public override string Name => TestName;

    protected override int Bots => 3;

    protected override List<KeyStep> Timeline(SBasePawn local) =>
    [
        new KeyStep("watch", 6.0),
        new KeyStep("look around", 6.0) { LookPerSecond = new Vector2(120f, 0f) },
        new KeyStep("watch more", 12.0),
        new KeyStep("look back", 6.0) { LookPerSecond = new Vector2(-120f, 0f) },
    ];

    protected override SmokeResult Verdict(DrawnPoseTrack? local, IReadOnlyList<DrawnPoseTrack> others)
    {
        var problems = new List<string>();
        int moved = 0;
        foreach (DrawnPoseTrack bot in others)
        {
            if (bot.MovingWindows < 4)
            {
                continue;
            }

            moved++;
            if (bot.AnimatedWindows < bot.MovingWindows * 3 / 4)
            {
                problems.Add($"{bot.Label}: drawn bones moved in {bot.AnimatedWindows} of {bot.MovingWindows} moving windows");
            }

            if (bot.MovingClips.Count == 0)
            {
                problems.Add($"{bot.Label}: moved with no walk channel drawn");
            }

            if (!float.IsNaN(bot.MinFoot) && (bot.MinFoot < -PlayCrouchSmoke.FeetBelow || bot.MaxFoot > PlayCrouchSmoke.FeetAbove))
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{bot.Label}: feet {bot.MinFoot:F1}..{bot.MaxFoot:F1} uu over the floor"));
            }
        }

        if (others.Count < Bots)
        {
            problems.Add($"{others.Count} of {Bots} bots drawn");
        }

        if (moved < 2)
        {
            problems.Add($"{moved} bots moved");
        }

        string all = string.Join(" | ", others.Select(o => o.ToString()));
        return problems.Count == 0 ? SmokeResult.Pass(all) : SmokeResult.Fail(string.Join("; ", problems) + " | " + all);
    }
}
