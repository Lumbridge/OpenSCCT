using System;

namespace OpenScct.Game.AI;

/// <summary>The difficulty presets.</summary>
public enum AiSkill : byte
{
    Easy = 0,
    Normal = 1,
    Hard = 2,
    Expert = 3,
}

/// <summary>
/// The knobs one bot plays with. A preset (<see cref="For"/>) fills them; each can be changed on its own. Every knob
/// limits what a player could also do: the bots see, hear and aim through the same rules at every level, the
/// difficulty only sets how quickly and how well they act on it (docs/AI.md, "Difficulty").
/// </summary>
public sealed record AiDifficulty
{
    public AiSkill Skill { get; init; } = AiSkill.Normal;

    // ---- reaction and aim -----------------------------------------------------------------------------------

    /// <summary>Seconds from first seeing a target until acting on it (aiming, shouting, fleeing).</summary>
    public float ReactionTime { get; init; } = 0.45f;

    /// <summary>Aim error (degrees, one standard spread) when the target was just acquired.</summary>
    public float AimErrorDegrees { get; init; } = 6f;

    /// <summary>Aim error after tracking a target for <see cref="AimSettleSeconds"/>.</summary>
    public float AimSettledDegrees { get; init; } = 1.5f;

    public float AimSettleSeconds { get; init; } = 1.2f;

    /// <summary>Fastest view turn (degrees per second), as a mouse hand would.</summary>
    public float TurnRateDegrees { get; init; } = 360f;

    /// <summary>How much of a moving target's velocity the aim leads (0 none, 1 exact).</summary>
    public float LeadFactor { get; init; } = 0.5f;

    // ---- perception ----------------------------------------------------------------------------------------

    /// <summary>Furthest sight (uu) in good light.</summary>
    public float SightRange { get; init; } = 3000f;

    /// <summary>Light level (the actor lighting term, luminance, 0 to 2) a target needs to be seen at close range.</summary>
    public float LightThreshold { get; init; } = 0.18f;

    /// <summary>Half angle (degrees) of the view in which targets are noticed; FOV beyond it is peripheral.</summary>
    public float FocusHalfAngle { get; init; } = 35f;

    /// <summary>Peripheral targets need this much more light and must be this much closer (0.5: half the range).</summary>
    public float PeripheralFactor { get; init; } = 0.5f;

    /// <summary>Seconds a target must stay in sight before it counts as seen (with the light making it faster).</summary>
    public float SpotSeconds { get; init; } = 0.6f;

    /// <summary>Scales the hearing radius of noises.</summary>
    public float HearingScale { get; init; } = 1f;

    /// <summary>Lowest heard sound gain (volume after distance) that alerts the bot.</summary>
    public float SoundThreshold { get; init; } = 0.12f;

    /// <summary>Seconds a remembered contact keeps half its confidence.</summary>
    public float MemoryHalfLife { get; init; } = 6f;

    // ---- decisions and tools -------------------------------------------------------------------------------

    /// <summary>Seconds between full decisions.</summary>
    public float DecisionPeriod { get; init; } = AiK.DecisionSeconds;

    /// <summary>Random spread added to every option's score (0: always the best option).</summary>
    public float DecisionNoise { get; init; } = 0.15f;

    /// <summary>How readily the bot uses gadgets and vision modes (0 never, 1 whenever they help).</summary>
    public float GadgetUse { get; init; } = 0.6f;

    /// <summary>How much the bot shares and uses team information (0 alone, 1 full callouts).</summary>
    public float Teamwork { get; init; } = 0.5f;

    /// <summary>How strongly a spy's route prefers darkness and cover (0 shortest path).</summary>
    public float Stealth { get; init; } = 0.6f;

    /// <summary>
    /// How readily a merc takes a close-quarters chance with the Use key (the charge, the berserk spin, the elbow) when
    /// the controller's rules allow one: the chance taken per look at it (0 never, 1 always).
    /// </summary>
    public float MeleeUse { get; init; } = 0.45f;

    /// <summary>The preset of <paramref name="skill"/>.</summary>
    public static AiDifficulty For(AiSkill skill) => skill switch
    {
        AiSkill.Easy => new AiDifficulty
        {
            Skill = AiSkill.Easy, ReactionTime = 0.9f, AimErrorDegrees = 10f, AimSettledDegrees = 4f, AimSettleSeconds = 2f,
            TurnRateDegrees = 200f, LeadFactor = 0f, SightRange = 2000f, LightThreshold = 0.3f, FocusHalfAngle = 25f,
            PeripheralFactor = 0.3f, SpotSeconds = 1.2f, HearingScale = 0.6f, SoundThreshold = 0.25f, MemoryHalfLife = 3f,
            DecisionPeriod = 0.5f, DecisionNoise = 0.35f, GadgetUse = 0.2f, Teamwork = 0.1f, Stealth = 0.2f, MeleeUse = 0.15f,
        },
        AiSkill.Normal => new AiDifficulty(),
        AiSkill.Hard => new AiDifficulty
        {
            Skill = AiSkill.Hard, ReactionTime = 0.28f, AimErrorDegrees = 4f, AimSettledDegrees = 0.8f, AimSettleSeconds = 0.8f,
            TurnRateDegrees = 540f, LeadFactor = 0.8f, SightRange = 3500f, LightThreshold = 0.12f, FocusHalfAngle = 40f,
            PeripheralFactor = 0.65f, SpotSeconds = 0.4f, HearingScale = 1.2f, SoundThreshold = 0.08f, MemoryHalfLife = 10f,
            DecisionPeriod = 0.2f, DecisionNoise = 0.08f, GadgetUse = 0.85f, Teamwork = 0.8f, Stealth = 0.85f, MeleeUse = 0.7f,
        },
        AiSkill.Expert => new AiDifficulty
        {
            Skill = AiSkill.Expert, ReactionTime = 0.18f, AimErrorDegrees = 2.5f, AimSettledDegrees = 0.4f, AimSettleSeconds = 0.5f,
            TurnRateDegrees = 720f, LeadFactor = 1f, SightRange = 4000f, LightThreshold = 0.08f, FocusHalfAngle = 45f,
            PeripheralFactor = 0.8f, SpotSeconds = 0.25f, HearingScale = 1.4f, SoundThreshold = 0.05f, MemoryHalfLife = 15f,
            DecisionPeriod = 0.15f, DecisionNoise = 0.03f, GadgetUse = 1f, Teamwork = 1f, Stealth = 1f, MeleeUse = 0.9f,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(skill), skill, "Unknown skill."),
    };

    /// <summary>Parses a preset name (easy, normal, hard, expert; case ignored).</summary>
    public static bool TryParse(string? text, out AiSkill skill)
    {
        switch (text?.Trim().ToUpperInvariant())
        {
            case "EASY":
                skill = AiSkill.Easy;
                return true;
            case "NORMAL" or "MEDIUM":
                skill = AiSkill.Normal;
                return true;
            case "HARD":
                skill = AiSkill.Hard;
                return true;
            case "EXPERT":
                skill = AiSkill.Expert;
                return true;
            default:
                skill = AiSkill.Normal;
                return false;
        }
    }

    /// <summary>The preset named <paramref name="text"/>; throws on an unknown name.</summary>
    public static AiDifficulty Parse(string text) =>
        TryParse(text, out AiSkill skill) ? For(skill) : throw new ArgumentException($"Unknown difficulty '{text}' (easy, normal, hard, expert).", nameof(text));
}
