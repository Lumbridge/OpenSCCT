namespace OpenScct.Game.SBase;

public partial class SObjectiveTrigger
{
    private bool _hideWhileInactive;

    /// <summary>OpenSCCT map override. Authored maps set false by default; maps without the property keep their
    /// original visibility rules. Visibility never activates a device or enables its interaction.</summary>
    public bool HideWhileInactive
    {
        get => _hideWhileInactive;
        set { _hideWhileInactive = value; HasInactiveVisibility = true; }
    }

    public bool HasInactiveVisibility { get; private set; }

    public void UpdateInactiveVisibility()
    {
        if (HasInactiveVisibility && !bActive && !bDeleteMe)
            bHidden = HideWhileInactive;
    }

    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        if (HasInactiveVisibility && !bActive)
        {
            // An unlinked device never receives a mission reset; it still needs to be visible but unusable.
            Desactivate();
            UpdateInactiveVisibility();
        }
    }

    protected override void NativeTick(float deltaSeconds)
    {
        base.NativeTick(deltaSeconds);
        UpdateInactiveVisibility();
    }
}
