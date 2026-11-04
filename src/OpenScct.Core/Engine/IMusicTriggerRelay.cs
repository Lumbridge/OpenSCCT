namespace OpenScct.Core.Engine;

/// <summary>Sends the original music-trigger call so each listener evaluates its own filter and track state.</summary>
public interface IMusicTriggerRelay
{
    void Relay(MusicTrigger trigger, bool start, Pawn? instigator);
}
