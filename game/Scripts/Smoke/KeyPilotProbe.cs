using Godot;

namespace OpenScctGame.Smoke;

/// <summary>
/// Counts the input events that reach the scene's input pass, so <see cref="KeyPilot"/> can tell whether the events it
/// parses are dispatched (they are not without a display server). It never handles an event.
/// </summary>
public partial class KeyPilotProbe : Node
{
    public KeyPilotProbe()
    {
        Name = "KeyPilotProbe";
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Key, mouse button and mouse motion events seen.</summary>
    public int Seen { get; private set; }

    /// <summary>Events of the marker key seen (the key KeyPilot sends to test the delivery).</summary>
    public int MarkerSeen { get; private set; }

    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey key:
                Seen++;
                if (key.PhysicalKeycode == KeyPilot.MarkerKey)
                {
                    MarkerSeen++;
                }

                break;
            case InputEventMouseButton or InputEventMouseMotion:
                Seen++;
                break;
        }
    }
}
