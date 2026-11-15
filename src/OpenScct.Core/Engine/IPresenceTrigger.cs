namespace OpenScct.Core.Engine;

/// <summary>
/// A map event source whose qualifying occupants can keep an automatic door open. Event relays retain the
/// original source, so occupancy does not require repeating their events or changing their authored delays.
/// </summary>
public interface IPresenceTrigger
{
    bool IsPresenceActive { get; }
}
