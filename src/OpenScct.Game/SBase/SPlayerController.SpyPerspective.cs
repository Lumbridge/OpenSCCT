using OpenScct.Core.Engine;
using OpenScct.Game.Camera;

namespace OpenScct.Game.SBase;

public partial class SPlayerController
{
    private SPawnAttaque? _spyFirstPersonPawn;

    /// <summary>A local perspective preference, scoped to this possession and never replicated.</summary>
    public bool SpyFirstPersonRequested
    {
        get
        {
            if (_spyFirstPersonPawn is { bDeleteMe: false, Health: > 0 } spy &&
                ReferenceEquals(Pawn, spy) && ReferenceEquals(SBPawn, spy)) return true;
            _spyFirstPersonPawn = null;
            return false;
        }
    }

    /// <summary>
    /// The optional eye view is active only for the owning live spy's ordinary camera. Scripted head cameras,
    /// binoculars, remote cameras, free cameras and spectating keep their own view and resume the preference later.
    /// Movement and combat continue to use the original behind-view flags and camera mode.
    /// </summary>
    public bool SpyFirstPersonActive => SpyFirstPersonRequested && Player is UViewport &&
        FreeCameraFlying == 0 && bBehindView && !bFirstPersonWithPawn && !bBinocularVision &&
        SBPawn is { bBinocularsOn: false } && (ViewTarget is null || ReferenceEquals(ViewTarget, Pawn)) &&
        PlayerCalcView.SourceOf(this) == PlayerCalcView.EViewSource.Default &&
        StateNode is not (s_Inactive or s_Dead or s_BaseSpectating or BaseSpectating) &&
        !IsInState("s_IntroCinematic") &&
        m_camera is not { m_bAttachToBone: true } && m_camera?.IsInState("s_IntroCinematic") != true;

    public void SetSpyFirstPerson(bool enabled)
    {
        _spyFirstPersonPawn = enabled && SBPawn is SPawnAttaque { bDeleteMe: false, Health: > 0 } spy &&
            ReferenceEquals(Pawn, spy) ? spy : null;
    }

    public void SwitchSpyPerspective() => SetSpyFirstPerson(!SpyFirstPersonRequested);
}
