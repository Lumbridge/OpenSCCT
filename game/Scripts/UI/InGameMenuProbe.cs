using Godot;

namespace OpenScctGame.UI;

/// <summary>
/// Counts a marker key reaching the scene's input pass, so the play-menu smoke can tell whether the events it parses
/// are dispatched (they are not without a display server). It never handles an event.
/// </summary>
public partial class InGameMenuProbe : Node
{
    public InGameMenuProbe()
    {
        Name = "InGameMenuProbe";
    }

    public Key MarkerKey { get; set; } = Key.F24;

    public int Marker { get; private set; }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.PhysicalKeycode == MarkerKey)
        {
            Marker++;
        }
    }
}
