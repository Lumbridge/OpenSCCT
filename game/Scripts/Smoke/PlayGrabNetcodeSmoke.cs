using System.Collections.Generic;
using Godot;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Physics.Collision;
using OpenScctGame.Play.Session;
using System.Linq;

namespace OpenScctGame.Smoke;

/// <summary>Two network owners perform a grab, knockout and recovery, rendered by an observer or the initiating spy.</summary>
public sealed class PlayGrabNetcodeSmoke : PlayNetcodeSmoke
{
    public const string Grab100 = "play-net-grab100", Grab200 = "play-net-grab200";
    public const string Attacker100 = "play-net-grab-attacker100", Attacker200 = "play-net-grab-attacker200";
    public const string StockGrab100 = "play-net-grab100-stock", StockGrab200 = "play-net-grab200-stock";
    private readonly bool _highPing;
    private readonly bool _attacker;
    public PlayGrabNetcodeSmoke(bool highPing, NetProfile profile = NetProfile.Enhanced, bool attacker = false) : base(highPing, true, profile)
    { _highPing = highPing; _attacker = attacker; }
    public override string Name => (_attacker ? _highPing ? Attacker200 : Attacker100 : _highPing ? Grab200 : Grab100) + PresetSuffix;
    protected override string NetworkMap => "SteeE";
    protected override IReadOnlyList<BotSpec> NetworkBots =>
    [
        new("Spy2", BotTeam.Spy, () => new IdleBrain { Stand = true }),
        new("Merc1", BotTeam.Merc, () => new IdleBrain()),
        new("Merc2", BotTeam.Merc, () => new IdleBrain()),
    ];

    private sealed class IdleBrain : BotBrain
    {
        public bool Stand;
        private bool _pressed;
        public override BotInput Think(BotContext context)
        {
            if (Stand && !_pressed && context.Controller.IsInState("s_PlayerWalkingCr0H"))
            { _pressed = true; return new BotInput { bCrouch = 1 }; }
            return BotInput.Idle;
        }
    }

    private SBasePawn? _merc;
    private SBasePawn? _shownMerc;
    private bool _arranged, _requested, _released, _down, _recovered;
    private int _health;
    private readonly HashSet<Name> _clips = new();
    private readonly record struct PoseFrame(long Tick, float Alpha, float Phase, Transform3D Head);
    private readonly List<PoseFrame> _knockoutFrames = new();
    private readonly List<PoseFrame> _visionFrames = new();
    private readonly List<PoseFrame> _ownVisionFrames = new();
    private string _visionStep = "";
    protected override bool ReadyForVerdict => _recovered && _clips.Contains(new Name("GrbdStAlWkA"));

    protected override List<KeyStep> Timeline(SBasePawn local)
    {
        List<KeyStep> steps = [new("settle", 2) { Tap = _attacker && local.bIsCrouched ? ["bCrouch"] : [] }, new("arrange", 2)];
        if (_attacker)
            steps.AddRange([new("vision motion on", 1.5), new("vision motion off", 1.5),
                new("vision EMF on", 1.5), new("vision EMF off", 1.5),
                new("own vision off", 1.5), new("own vision on", 1.5)]);
        steps.AddRange([new("grab", 6), new("knockout", 4), new("recovery", 12)]);
        return steps;
    }

    protected override void OnFrame(PlaySession session, string step)
    {
        base.OnFrame(session, step);
        if (NetworkMatch is not { } match ||
            match.Bots[1].Controller is not { Pawn: SBasePawn ownMerc } mercOwner ||
            match.ServerController(match.Bots[1]) is not { Pawn: SBasePawn sourceMerc } mercAuthority) return;
        SPlayerController? owner = _attacker ? session.LocalController : match.Bots[0].Controller;
        SPlayerController? authority = _attacker
            ? match.Server.Players.FirstOrDefault(slot => slot.Name == owner?.PlayerReplicationInfo?.PlayerName)?.Controller as SPlayerController
            : match.ServerController(match.Bots[0]);
        if (owner is not { Pawn: SBasePawn ownSpy } spyOwner || authority is not { Pawn: SBasePawn sourceSpy } spyAuthority) return;
        if (!_arranged && step == "arrange" && !ownSpy.bIsCrouched)
        {
            _arranged = true; _merc = sourceMerc; _health = sourceMerc.Health;
            string realMap = OpenScct.Server.MapCatalog.CreateDefault().FindFolder("SteeE") ?? throw new System.IO.FileNotFoundException("SteeE");
            string chars = PlayMapLoader.FindCharsDir(realMap) ?? throw new System.IO.FileNotFoundException("Character clips");
            PawnAnimationSource animations = PawnAnimationSource.FromContent(chars);
            void UseClips(Level level) => level.SetService(new MovementService(animations, level.GetService<MovementService>()?.Hooks));
            UseClips(match.Server.World.Level); UseClips(session.Client.World.Level);
            foreach (BotClient bot in match.Bots) UseClips(bot.Session.World.Level);
            FRotator facing = sourceMerc.Rotation; facing.Pitch = facing.Roll = 0;
            FVector mercAt = sourceMerc.Location, spyAt = mercAt - facing.Vector() * 85;
            spyAt.Z += ownSpy.CollisionHeight - ownMerc.CollisionHeight;
            void Place(SBasePawn pawn, FVector at)
            {
                MovementNatives.Driver(pawn)!.FarMoveActor(pawn, at);
                pawn.Rotation = facing; pawn.Velocity = pawn.Acceleration = default;
                ((SPlayerController)pawn.Controller!).SetViewRotation(facing);
            }
            Place(sourceSpy, spyAt); Place(ownSpy, spyAt); Place(sourceMerc, mercAt); Place(ownMerc, mercAt);
            sourceSpy.ResetNetworkHistory(); sourceMerc.ResetNetworkHistory();
            match.Server.Movement.PeekEndpoint(spyAuthority)!.Sequenced!.ResetLifetime();
            match.Server.Movement.PeekEndpoint(mercAuthority)!.Sequenced!.ResetLifetime();
            sourceMerc.iLastWakeTime = -100; mercAuthority.TimeShockForGrab = -100;
            if (spyAuthority.GameReplicationInfo is { } gri) gri.bAvlb_SPY_Grab = true;
            session.ViewOverride = _attacker ? null : view =>
            {
                if (session.Client.Driver.FindActorByNetGuid((uint)sourceMerc.ActorId) is not SBasePawn shown) return view;
                FVector focus = shown.Location + new FVector(0, 0, 15), camera = focus + new FVector(150, 200, 95);
                if (session.Client.Map?.Physics?.Collision is { } collision &&
                    collision.LineCheck(out var hit, shown, camera, focus, new FVector(4, 4, 4), ETraceFlags.TRACE_World))
                    camera = hit.Location + hit.Normal * 4;
                return view with { Location = camera, Rotation = (focus - camera).Rotation(), Fov = 80, Valid = true };
            };
        }
        if (step.StartsWith("vision ", System.StringComparison.Ordinal) && _visionStep != step)
        {
            _visionStep = step;
            mercOwner.SwitchHeadset(step.Contains("motion", System.StringComparison.Ordinal) ? 1 : 2);
        }
        if (step.StartsWith("own vision ", System.StringComparison.Ordinal) && _visionStep != step)
        {
            _visionStep = step;
            spyOwner.SwitchHeadset(1);
        }
        if (_visionStep.StartsWith("own vision ", System.StringComparison.Ordinal) &&
            Root?.Pawns?.ViewOf(ownSpy) is { Model: { } spyModel } spyView &&
            spyView.DrawnChannels[SBasePawn.GOGGLECHANNEL] is { Clip: not null, Alpha: > 0f, ClipFrame: > 0.05f and < 0.9f } ownVision)
            _ownVisionFrames.Add(new(session.Client.World.TickCount, spyView.DrawAlpha, ownVision.ClipFrame,
                spyModel.Skeleton.GetBoneGlobalPose(spyModel.Skeleton.FindBone("B Head"))));
        if (_arranged && step == "grab" && !_requested) _requested = spyOwner.AskToGrabPawn();
        if (_requested && step == "knockout" && !_released && spyOwner.IsInState("s_GrabbingPlayer"))
            _released = spyOwner.CheckForRoomToFreeGrabbedGuy(GrabReleaseMethode_.KO);
        _down |= _released && sourceMerc.bSleeping;
        _recovered |= _down && !sourceMerc.bSleeping && !sourceMerc.bLyingOnGround;
        if (session.Client.Driver.FindActorByNetGuid((uint)sourceMerc.ActorId) is SBasePawn presented)
        {
            _shownMerc = presented;
            _clips.Add(presented.PawnAnim[0].Sequence);
            if (Root?.Pawns?.ViewOf(presented) is { Model: { } model } view)
            {
                int head = model.Skeleton.FindBone("B Head");
                Transform3D pose = model.Skeleton.GetBoneGlobalPose(head);
                if (view.DrawnChannels[0] is { Sequence: "GrbdStAlKo0", ClipFrame: > 0.05f and < 0.9f } knockout)
                    _knockoutFrames.Add(new(session.Client.World.TickCount, view.DrawAlpha, knockout.ClipFrame, pose));
                if (view.DrawnChannels[SBasePawn.GOGGLECHANNEL] is { Clip: not null, Alpha: > 0f, ClipFrame: > 0.05f and < 0.9f } vision)
                    _visionFrames.Add(new(session.Client.World.TickCount, view.DrawAlpha, vision.ClipFrame, pose));
            }
        }
    }

    protected override SmokeResult Verdict(DrawnPoseTrack? local, IReadOnlyList<DrawnPoseTrack> others)
    {
        if (!_arranged || !_requested || !_released || !_down || !_recovered || _merc?.Health != _health - 51)
            return SmokeResult.Fail($"arranged={_arranged} grab={_requested} release={_released} down={_down} recovered={_recovered} health={_merc?.Health}");
        foreach (string clip in new[] { "GrbdStAlBg0", "GrbdStAlKo0", "GrbdStAlWkA" })
            if (!_clips.Contains(new Name(clip))) return SmokeResult.Fail($"missing rendered {clip}: {string.Join(',', _clips)}");
        DrawnPoseTrack? victim = null;
        foreach (DrawnPoseTrack track in others) if (ReferenceEquals(track.View.Pawn, _shownMerc)) victim = track;
        foreach (string clip in new[] { "GrbdStAlKo0", "GrbdStAlWkA" })
            if (victim is null || !victim.ClipBoneMotion.TryGetValue(new Name(clip), out float motion) ||
                motion < DrawnPoseTrack.AnimatedBoneMotion) return SmokeResult.Fail($"rendered bones did not move during {clip}");
        foreach (BotClient bot in NetworkMatch!.Bots)
            if (bot.Session.State != OpenScct.Server.EClientState.Joined ||
                bot.Session.Movement.PeekEndpoint(bot.Controller!)?.Sequenced is not { ResynchronizationRequests: 0 })
                return SmokeResult.Fail($"{bot.Name} disconnected or reset");
        var knockout = Cadence("KO", _knockoutFrames);
        var vision = _attacker ? Cadence("vision head tilt", _visionFrames) : (true, "");
        var ownVision = _attacker ? Cadence("own vision head tilt", _ownVisionFrames) : (true, "");
        string cadence = knockout.Detail + (_attacker ? "; " + vision.Item2 + "; " + ownVision.Item2 : "");
        if (!knockout.Ok || !vision.Item1 || !ownVision.Item1) return SmokeResult.Fail(cadence);
        return SmokeResult.Pass($"two owners: catch, KO and wake rendered; one health reduction; {cadence}; {string.Join(',', _clips)}");
    }

    private static (bool Ok, string Detail) Cadence(string label, List<PoseFrame> frames)
    {
        int samples = 0, advanced = 0, moving = 0, betweenTicks = 0, betweenTicksMoving = 0;
        float maxPhaseStep = 0f;
        for (int i = 1; i < frames.Count; i++)
        {
            PoseFrame previous = frames[i - 1], current = frames[i];
            // Each vision toggle replays the same head clip from its start.
            if (current.Phase < previous.Phase - 0.5f) continue;
            samples++;
            bool changed = current.Phase > previous.Phase + 0.000001f;
            bool moved = current.Head != previous.Head;
            if (changed) advanced++;
            if (moved) moving++;
            maxPhaseStep = System.MathF.Max(maxPhaseStep, current.Phase - previous.Phase);
            if (current.Tick == previous.Tick && current.Alpha > previous.Alpha)
            {
                betweenTicks++;
                if (changed && moved) betweenTicksMoving++;
            }
        }
        string cadence = $"{label} cadence phase={advanced}/{samples} bones={moving}, " +
            $"between-ticks={betweenTicksMoving}/{betweenTicks}, max-step={maxPhaseStep:F6}";
        GD.Print("SVM_GRAB_CADENCE " + cadence);
        return (samples >= 30 && advanced >= samples * 0.98f && moving >= samples * 0.98f, cadence);
    }
}
