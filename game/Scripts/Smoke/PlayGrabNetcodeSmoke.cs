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

/// <summary>Two network owners perform a grab, knockout and recovery while a third client renders both pawns.</summary>
public sealed class PlayGrabNetcodeSmoke : PlayNetcodeSmoke
{
    public const string Grab100 = "play-net-grab100", Grab200 = "play-net-grab200";
    public const string StockGrab100 = "play-net-grab100-stock", StockGrab200 = "play-net-grab200-stock";
    private readonly bool _highPing;
    public PlayGrabNetcodeSmoke(bool highPing, NetProfile profile = NetProfile.Enhanced) : base(highPing, true, profile) => _highPing = highPing;
    public override string Name => (_highPing ? Grab200 : Grab100) + PresetSuffix;
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
    protected override bool ReadyForVerdict => _recovered && _clips.Contains(new Name("GrbdStAlWkA"));

    protected override List<KeyStep> Timeline(SBasePawn local) =>
    [new("settle", 2), new("arrange", 2), new("grab", 6), new("knockout", 4), new("recovery", 12)];

    protected override void OnFrame(PlaySession session, string step)
    {
        base.OnFrame(session, step);
        if (NetworkMatch is not { } match || match.Bots[0].Controller is not { Pawn: SBasePawn ownSpy } spyOwner ||
            match.Bots[1].Controller is not { Pawn: SBasePawn ownMerc } mercOwner ||
            match.ServerController(match.Bots[0]) is not { Pawn: SBasePawn sourceSpy } spyAuthority ||
            match.ServerController(match.Bots[1]) is not { Pawn: SBasePawn sourceMerc } mercAuthority) return;
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
            session.ViewOverride = view =>
            {
                if (session.Client.Driver.FindActorByNetGuid((uint)sourceMerc.ActorId) is not SBasePawn shown) return view;
                FVector focus = shown.Location + new FVector(0, 0, 15), camera = focus + new FVector(150, 200, 95);
                if (session.Client.Map?.Physics?.Collision is { } collision &&
                    collision.LineCheck(out var hit, shown, camera, focus, new FVector(4, 4, 4), ETraceFlags.TRACE_World))
                    camera = hit.Location + hit.Normal * 4;
                return view with { Location = camera, Rotation = (focus - camera).Rotation(), Fov = 80, Valid = true };
            };
        }
        if (_arranged && step == "grab" && !_requested) _requested = spyOwner.AskToGrabPawn();
        if (_requested && step == "knockout" && !_released && spyOwner.IsInState("s_GrabbingPlayer"))
            _released = spyOwner.CheckForRoomToFreeGrabbedGuy(GrabReleaseMethode_.KO);
        _down |= _released && sourceMerc.bSleeping;
        _recovered |= _down && !sourceMerc.bSleeping && !sourceMerc.bLyingOnGround;
        if (session.Client.Driver.FindActorByNetGuid((uint)sourceMerc.ActorId) is SBasePawn presented)
        {
            _shownMerc = presented;
            _clips.Add(presented.PawnAnim[0].Sequence);
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
        return SmokeResult.Pass($"two owners: catch, KO and wake rendered; one health reduction; {string.Join(',', _clips)}");
    }
}
