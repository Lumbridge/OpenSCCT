namespace OpenScct.Game.Equipment;

/// <summary>The server's answer to a gear change (spec/additions/gear-change.md).</summary>
public enum GearChangeResult
{
    /// <summary>The new kit went on now.</summary>
    Now,

    /// <summary>The new kit goes on at the next spawn.</summary>
    NextSpawn,

    /// <summary>Too soon after the last change (hub); the wait is sent with it.</summary>
    Cooldown,
}

/// <summary>The owner's machine is told the server's answer (the HUD shows it).</summary>
public interface IGearChangeFeedback
{
    void GearChangeAnswered(GearChangeResult Result, float WaitSeconds);
}
