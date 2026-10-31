using System;
using System.Collections.Generic;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Physics.Collision;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>Independent owners boost and hoist at SteeE GE63 while a fourth client renders both halves.</summary>
public sealed class PlayCoopNetcodeSmoke : PlayNetcodeSmoke
{
    public const string Coop100 = "play-net-coop100", Coop200 = "play-net-coop200";
    public const string StockCoop100 = "play-net-coop100-stock", StockCoop200 = "play-net-coop200-stock";
    private readonly bool _highPing;
    public PlayCoopNetcodeSmoke(bool highPing, NetProfile profile = NetProfile.Enhanced) : base(highPing, true, profile) => _highPing = highPing;
    public override string Name => (_highPing ? Coop200 : Coop100) + PresetSuffix;
    protected override PlaySide RenderedSide => PlaySide.Merc;
    protected override IReadOnlyList<BotSpec> NetworkBots =>
    [new("Spy1", BotTeam.Spy, () => new IdleBrain()), new("Spy2", BotTeam.Spy, () => new IdleBrain()),
        new("Merc2", BotTeam.Merc, () => new IdleBrain())];
    private sealed class IdleBrain : BotBrain
    {
        private bool _stood;
        public override BotInput Think(BotContext context)
        {
            if (!_stood && context.Controller.Pawn is SPawnAttaque && context.Controller.IsInState("s_PlayerWalkingCr0H"))
            { _stood = true; return new BotInput { bCrouch = 1 }; }
            return BotInput.Idle;
        }
    }

    private int _stage;
    private long _arranged;
    private long _nextCoopLog;
    private bool _inspectionVision;
    private SBasePawn? _climber, _helper, _shownClimber, _shownHelper;
    private readonly HashSet<Name> _clips = new();
    protected override bool ReadyForVerdict => _stage == 5;
    protected override List<KeyStep> Timeline(SBasePawn local) => [new("boost-and-hoist", 5)];

    protected override void OnFrame(PlaySession session, string step)
    {
        base.OnFrame(session, step);
        // This observer camera uses night vision to expose the stationary poses in the unlit courtyard.
        if (session.LocalController is { } observer)
        {
            if (!_inspectionVision) observer.Goggle?.ResetVisionMode(true);
            observer.RendMap = SGoggle.REN_NightVision;
            _inspectionVision = true;
        }
        if (NetworkMatch is not { } match ||
            match.Bots[0].Controller is not { Pawn: SBasePawn ownClimber } climber ||
            match.Bots[1].Controller is not { Pawn: SBasePawn ownHelper } helper ||
            match.ServerController(match.Bots[0]) is not { Pawn: SBasePawn sourceClimber } authorityClimber ||
            match.ServerController(match.Bots[1]) is not { Pawn: SBasePawn sourceHelper } authorityHelper) return;
        long tick = match.Server.World.TickCount;
        if (_stage == 0 && tick >= 300 && !ownClimber.bIsCrouched && !ownHelper.bIsCrouched)
        {
            _climber = sourceClimber; _helper = sourceHelper; _arranged = tick;
            string folder = OpenScct.Server.MapCatalog.CreateDefault().FindFolder("SteeE") ?? throw new System.IO.FileNotFoundException("SteeE");
            string chars = PlayMapLoader.FindCharsDir(folder) ?? throw new System.IO.FileNotFoundException("Character clips");
            PawnAnimationSource animations = PawnAnimationSource.FromContent(chars);
            void UseClips(Level level) => level.SetService(new MovementService(animations, level.GetService<MovementService>()?.Hooks));
            UseClips(match.Server.World.Level); UseClips(session.Client.World.Level);
            foreach (BotClient bot in match.Bots) UseClips(bot.Session.World.Level);
            void Place(SPlayerController pc, float y, int yaw)
            {
                var pawn = (SBasePawn)pc.Pawn!;
                if (!MovementNatives.Driver(pawn)!.FarMoveActor(pawn, new FVector(-3340, y, -168 + 89.5f)))
                    throw new InvalidOperationException("Cannot place cooperative smoke pawn");
                pawn.Velocity = pawn.Acceleration = default; pawn.Rotation = new FRotator(0, yaw, 0);
                pc.SetViewRotation(pawn.Rotation);
            }
            Place(climber, 2301, 16384); Place(authorityClimber, 2301, 16384);
            Place(helper, 2451, -16384); Place(authorityHelper, 2451, -16384);
            sourceClimber.ResetNetworkHistory(); sourceHelper.ResetNetworkHistory();
            match.Server.Movement.PeekEndpoint(authorityClimber)!.Sequenced!.ResetLifetime();
            match.Server.Movement.PeekEndpoint(authorityHelper)!.Sequenced!.ResetLifetime();
            session.ViewOverride = view =>
            {
                FVector focus = new(-3340, 2450, 50), camera = focus + new FVector(400, -400, 200);
                if (session.Client.Map?.Physics?.Collision is { } collision &&
                    collision.LineCheck(out var hit, sourceClimber, camera, focus, new FVector(4, 4, 4), ETraceFlags.TRACE_World))
                    camera = hit.Location + hit.Normal * 4;
                return view with { Location = camera, Rotation = (focus - camera).Rotation(), Fov = 80, Valid = true };
            };
            _stage = 1;
        }
        else if (_stage == 1 && tick >= _arranged + 180)
        { helper.eventPerformCoopMvt(); _stage = 2; }
        else if (_stage == 2 && sourceHelper.bReadyForShortLadder && ownHelper.bReadyForShortLadder &&
            match.Bots[0].Session.Driver.FindActorByNetGuid((uint)sourceHelper.ActorId) is SBasePawn { bReadyForShortLadder: true })
        { if (climber.DoCoopInteraction()) _stage = 3; }
        else if (_stage == 3 && climber.IsInState(MovementStateNames.OnLedge) && authorityClimber.IsInState(MovementStateNames.OnLedge) &&
            helper.IsInState(MovementStateNames.s_PlayerWalkingSt0H))
        { climber.eventPerformCoopMvt(); _stage = 4; }
        else if (_stage == 4 && sourceClimber.bReadyForHoistClimb && ownClimber.bReadyForHoistClimb &&
            match.Bots[1].Session.Driver.FindActorByNetGuid((uint)sourceClimber.ActorId) is SBasePawn { bReadyForHoistClimb: true })
        { if (helper.DoCoopInteraction()) _stage = 6; }
        else if (_stage == 6 && helper.IsInState(MovementStateNames.s_PlayerWalkingCr0H) &&
            authorityHelper.IsInState(MovementStateNames.s_PlayerWalkingCr0H) && sourceHelper.Location.Z - sourceHelper.CollisionHeight > 340)
            _stage = 5;
        _shownClimber = session.Client.Driver.FindActorByNetGuid((uint)sourceClimber.ActorId) as SBasePawn;
        _shownHelper = session.Client.Driver.FindActorByNetGuid((uint)sourceHelper.ActorId) as SBasePawn;
        if (_shownClimber is { } c) _clips.Add(c.PawnAnim[0].Sequence);
        if (_shownHelper is { } h) _clips.Add(h.PawnAnim[0].Sequence);
        if (tick >= _nextCoopLog)
        {
            _nextCoopLog = tick + 120;
            Godot.GD.Print($"SVM_NET_COOP stage={_stage} climber={climber.GetStateName()}/{authorityClimber.GetStateName()} " +
                $"at={ownClimber.Location}/{sourceClimber.Location} helper={helper.GetStateName()}/{authorityHelper.GetStateName()} " +
                $"at={ownHelper.Location}/{sourceHelper.Location} ready={ownHelper.bReadyForShortLadder}/{sourceHelper.bReadyForShortLadder}, " +
                $"hoist={ownClimber.bReadyForHoistClimb}/{sourceClimber.bReadyForHoistClimb}");
        }
    }

    protected override SmokeResult Verdict(DrawnPoseTrack? local, IReadOnlyList<DrawnPoseTrack> others)
    {
        if (_stage != 5 || _climber?.Physics != EPhysics.PHYS_Ledge || _helper is null)
            return SmokeResult.Fail($"coop stopped at {_stage}");
        DrawnPoseTrack? climber = null, helper = null;
        foreach (DrawnPoseTrack track in others)
        {
            if (ReferenceEquals(track.View.Pawn, _shownClimber)) climber = track;
            if (ReferenceEquals(track.View.Pawn, _shownHelper)) helper = track;
        }
        foreach ((DrawnPoseTrack? track, string clip) in new[] { (climber, "CoopBtwAlHpEdS"), (helper, "CoopCrAlHpEdP") })
            if (track is null || !track.ClipBoneMotion.TryGetValue(new Name(clip), out float excursion) ||
                excursion < DrawnPoseTrack.AnimatedBoneMotion) return SmokeResult.Fail($"no rendered bone motion for {clip}");
        foreach (BotClient bot in NetworkMatch!.Bots)
            if (bot.Session.State != OpenScct.Server.EClientState.Joined ||
                bot.Session.Movement.PeekEndpoint(bot.Controller!)?.Sequenced is not { ResynchronizationRequests: 0 })
                return SmokeResult.Fail($"{bot.Name} disconnected or reset");
        return SmokeResult.Pass($"two owners completed boost and hoist; rendered paired bones; {string.Join(',', _clips)}");
    }
}
