using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The view effects the map devices set on a player's camera (shaker, explosions). How the native camera turns
// these values into view motion is camera code (spec/devices/shaker.md rule 4).
public partial class SPlayerCam
{
    /// <summary>Pitch tilt: amplitude, speed and fade-out (rotator units).</summary>
    [Uc("SBase.SPlayerCam.Tilt", Sha1 = "c58268110720d6e35f9630d7ec77e261215cc3e1")]
    [PortOf("uc:SBase.SPlayerCam:Tilt", Spec = "spec/devices/shaker.md#rules")]
    public void Tilt(int strenght, int speed, int FadeOut, bool noRumble = false)
    {
        m_tiltPitch = strenght;
        m_tiltTargetPitch = strenght;
        m_tiltSpeed = speed;
        m_tiltFadeOut = FadeOut;
    }

    /// <summary>Roll shake: amplitude, speed and fade-out.</summary>
    [Uc("SBase.SPlayerCam.Shake", Sha1 = "deb79412bf221689df6b5f184003cd8e764510d0")]
    [PortOf("uc:SBase.SPlayerCam:Shake", Spec = "spec/devices/shaker.md#rules")]
    public void Shake(int strenght, int speed, int FadeOut, bool noRumble = false)
    {
        m_shakeRoll = strenght;
        m_shakeTargetRoll = strenght;
        m_shakeSpeed = speed;
        m_shakeFadeOut = FadeOut;
    }

    /// <summary>A hit jolt: roll amplitude and fade-out.</summary>
    [Uc("SBase.SPlayerCam.Hit", Sha1 = "f3f126b09cbf423cf9565bc27ff90326ea344ed6")]
    [PortOf("uc:SBase.SPlayerCam:Hit", Spec = "spec/devices/shaker.md#rules")]
    public void Hit(int strenght, int FadeOut, bool noRumble = false)
    {
        m_hitRoll = strenght;
        m_hitFadeOut = FadeOut;
    }

    /// <summary>Clears hit, tilt and shake.</summary>
    [Uc("SBase.SPlayerCam.ResetEffects", Sha1 = "614a723a7e793a1ea3bec27b8180b3bade07ad44")]
    [PortOf("uc:SBase.SPlayerCam:ResetEffects", Spec = "spec/devices/shaker.md#rules")]
    public void ResetEffects()
    {
        m_hitRoll = 0;
        m_tiltPitch = 0;
        m_tiltTargetPitch = 0;
        m_shakeRoll = 0;
        m_shakeTargetRoll = 0;
    }
}
