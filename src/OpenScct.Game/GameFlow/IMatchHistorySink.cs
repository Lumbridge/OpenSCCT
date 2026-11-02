namespace OpenScct.Game.GameFlow;

/// <summary>Optional authoritative round-event recorder; never changes game rules.</summary>
public interface IMatchHistorySink
{
    void Record(string kind, string text);
}
