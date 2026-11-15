using Godot;
using OpenScctGame.Hud;

namespace OpenScctGame.Play;

public partial class PlayRoot
{
    private CanvasLayer? _grabArea;
    public bool GrabAreaShown => _grabArea is not null;

    public bool ShowGrabArea(bool on)
    {
        if (!on)
        {
            _grabArea?.QueueFree();
            _grabArea = null;
            return true;
        }
        if (!GrabAreaOverlay.Allowed(Session)) return false;
        if (_grabArea is null)
        {
            _grabArea = new CanvasLayer { Name = "GrabArea", Layer = HudLayer.DefaultLayer - 1 };
            _grabArea.AddChild(new GrabAreaOverlay(() => Session, () => View?.Camera)
            {
                ShowLegend = !System.OperatingSystem.IsBrowser() || BrowserPlatform.Lesson != "spy-grab",
            });
            AddChild(_grabArea);
        }
        return true;
    }
}
