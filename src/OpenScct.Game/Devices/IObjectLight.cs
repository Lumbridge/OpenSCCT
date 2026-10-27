namespace OpenScct.Game.Devices;

/// <summary>
/// The light switching a gameplay object asks of its ObjectLights (STriggerLight.LightOn and LightOff, ported with
/// the lighting system). A light that does not implement it is left as it is.
/// </summary>
public interface IObjectLight
{
    /// <summary>STriggerLight.LightOn: back to its lit look.</summary>
    void LightOn();

    /// <summary>STriggerLight.LightOff: dark (<paramref name="bMalfunction"/> for a chaff or sticky lock).</summary>
    void LightOff(bool bMalfunction = false);
}
