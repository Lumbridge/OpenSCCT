namespace OpenScct.Server.Hub;

/// <summary>
/// The numbers of the hub's activities (an OpenSCCT addition, spec/additions/hub.md): our own choices, not values of
/// the original, so they carry no provenance. Distances in uu, times in seconds unless named otherwise.
/// </summary>
public static class HubPlayK
{
    // ---- gates -------------------------------------------------------------------------------------------------

    public const float StartRadius = 192f;

    public const float FinishRadius = 256f;

    /// <summary>A checkpoint spaced on the line between start and finish (a course without pieces): wide enough for any route.</summary>
    public const float CheckpointRadius = 640f;

    /// <summary>A checkpoint over a traversal piece.</summary>
    public const float PieceCheckpointRadius = 256f;

    /// <summary>Checkpoints spaced on the line when the course lists none.</summary>
    public const int LineCheckpoints = 3;

    /// <summary>A pawn's centre counts over a gate from this far below the gate's height...</summary>
    public const float GateDepth = 192f;

    /// <summary>...to this far above it.</summary>
    public const float GateHeight = 320f;

    // ---- course ------------------------------------------------------------------------------------------------

    /// <summary>A run longer than this is cancelled.</summary>
    public const float MaxRunSeconds = 300f;

    /// <summary>A run that strays this far outside the course's area is cancelled.</summary>
    public const float CourseLeaveMargin = 512f;

    // ---- aim range ---------------------------------------------------------------------------------------------

    public const float RoundSeconds = 30f;

    /// <summary>Rounds that may run at once (each has its own targets).</summary>
    public const int MaxRounds = 4;

    /// <summary>Targets up at once in one round.</summary>
    public const int TargetsUp = 2;

    /// <summary>A target stays up this long (a random time between the two) unless it is hit.</summary>
    public const float TargetUpMin = 1.6f;

    public const float TargetUpMax = 3.2f;

    /// <summary>Wait before the next target rises once one went down.</summary>
    public const float TargetGap = 0.35f;

    /// <summary>Target body: half width (across the lane), half depth (along the line of fire), height.</summary>
    public const float TargetHalfWidth = 22f;

    public const float TargetHalfDepth = 6f;

    public const float TargetBodyHeight = 128f;

    /// <summary>Target head: half width, from the body's top plus a gap, its height.</summary>
    public const float TargetHeadHalfWidth = 13f;

    public const float TargetHeadGap = 4f;

    public const float TargetHeadHeight = 30f;

    public const int BodyPoints = 10;

    public const int HeadPoints = 25;

    /// <summary>The accuracy bonus at the end of a round: this times the share of shots that hit.</summary>
    public const int AccuracyBonus = 50;

    /// <summary>Score updates to the shooter at most this often (and on every hit).</summary>
    public const float RangeScoreInterval = 1f;

    /// <summary>A round is cancelled when its shooter goes this far outside the range's area.</summary>
    public const float RangeLeaveMargin = 256f;

    /// <summary>A shooter may start a round from this far in front of the firing line (towards the targets).</summary>
    public const float FiringLineSlack = 96f;

    // ---- social ------------------------------------------------------------------------------------------------

    /// <summary>An emote reaches the players this near.</summary>
    public const float EmoteRange = 4500f;

    public const float EmoteCooldown = 1.5f;

    /// <summary>"Use" reaches the jukebox from this near.</summary>
    public const float JukeboxUseRange = 384f;

    /// <summary>The jukebox is heard (fading out) up to this far.</summary>
    public const float JukeboxRange = 1600f;

    public const float JukeboxCooldown = 4f;

    /// <summary>A new record's fanfare reaches the players this near the record holder or the activity.</summary>
    public const float FanfareRange = 3500f;

    // ---- plaza ball --------------------------------------------------------------------------------------------

    public const float BallRadius = 24f;

    /// <summary>Kick: at least this fast, else the kicker's speed times <see cref="BallKickScale"/>, at most <see cref="BallMaxSpeed"/>.</summary>
    public const float BallMinKick = 320f;

    public const float BallKickScale = 1.6f;

    public const float BallMaxSpeed = 1400f;

    /// <summary>Speed lost per second (a share of the speed).</summary>
    public const float BallFriction = 0.9f;

    public const float BallStopSpeed = 12f;

    public const float BallBounce = 0.7f;

    /// <summary>The goals on the plaza's west and east edges: the mouth's half width.</summary>
    public const float GoalHalfWidth = 288f;

    /// <summary>Seconds after a goal before the ball is put back.</summary>
    public const float GoalResetSeconds = 2f;

    /// <summary>Ball positions go out this often while it moves.</summary>
    public const float BallSendInterval = 0.1f;

    /// <summary>The updates while it rolls go to the players this near the ball (a kick and the stop go to all).</summary>
    public const float BallSendRange = 6000f;

    // ---- boards ------------------------------------------------------------------------------------------------

    public const int BoardRows = 10;

    /// <summary>Height of a board's middle over the floor (a default anchor).</summary>
    public const float BoardHeight = 160f;

    /// <summary>The HUD shows a board within this distance of it.</summary>
    public const float BoardShowRange = 2200f;

    /// <summary>Personal bests kept by name in the scores file.</summary>
    public const int MaxBests = 1000;
}
