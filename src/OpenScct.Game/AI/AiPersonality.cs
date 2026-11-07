using OpenScct.Core;

namespace OpenScct.Game.AI;

public enum AiStyle : byte
{
    Patient,
    Aggressive,
    Support,
    Scout,
}

/// <summary>Role preferences layered on top of skill. All variation is seeded and stable for the bot's life in the match.</summary>
public readonly record struct AiPersonality(AiStyle Style, float Aggression, float Teamwork, float Utility, float Curiosity,
    float Tempo, float Reaction, float Burst, float ScanDelay, float HeartbeatLikelihood, int AimYaw, float AimHeight)
{
    internal static AiPersonality Create(AiStyle style, SimRandom random)
    {
        (float aggression, float teamwork, float utility, float curiosity, float heartbeat) = style switch
        {
            AiStyle.Patient => (.65f, 1.15f, 1.10f, .7f, .38f),
            AiStyle.Aggressive => (1.35f, .80f, .85f, 1.1f, .20f),
            AiStyle.Support => (.85f, 1.35f, 1.35f, .9f, .60f),
            _ => (1.05f, 1.0f, .95f, 1.35f, .78f),
        };

        float Mutate(float trait) => trait * random.RandRange(.85f, 1.15f);

        return new(style, Mutate(aggression), Mutate(teamwork), Mutate(utility), Mutate(curiosity),
            random.RandRange(.80f, 1.25f), random.RandRange(.85f, 1.30f), random.RandRange(.72f, 1.32f),
            random.RandRange(.65f, 4.5f) / curiosity, Mutate(heartbeat), random.Rand(2901) - 1450, random.RandRange(12f, 27f));
    }
}
