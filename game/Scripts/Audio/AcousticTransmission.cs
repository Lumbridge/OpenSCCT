using System;

namespace OpenScctGame.Audio;

public readonly record struct TransmissionLevels(float DirectGain, float DirectHF, float RoomGain, float RoomHF)
{
    public static TransmissionLevels Clear => new(1, 1, 1, 1);
    public TransmissionLevels Approach(TransmissionLevels target, float seconds)
    {
        float mix = 1 - MathF.Exp(-Math.Max(0, seconds) / 0.06f);
        return new(DirectGain + (target.DirectGain - DirectGain) * mix,
            DirectHF + (target.DirectHF - DirectHF) * mix,
            RoomGain + (target.RoomGain - RoomGain) * mix,
            RoomHF + (target.RoomHF - RoomHF) * mix);
    }
}

/// <summary>Presentation presets derived from existing material categories; never used for gameplay hearing.</summary>
public static class AcousticTransmission
{
    public static TransmissionLevels Through(float thickness, int nearMaterial, int farMaterial)
    {
        if (!float.IsFinite(thickness)) thickness = 0;
        float Material(int index) => AudioK.OcclusionMaterialFactor[
            index >= 0 && index < AudioK.OcclusionMaterialFactor.Length ? index : 0];
        float material = Math.Min(Material(nearMaterial), Material(farMaterial));
        float factor = Math.Clamp(MathF.Pow(material, 1 + Math.Clamp(thickness, 0, 700) / 700), 0.00001f, 1);
        // Keep recognisable low-frequency cues while attenuating high frequencies
        // more strongly. Room energy follows a gentler, independent path.
        return new(Math.Max(0.25f, MathF.Pow(factor, 0.25f)), Math.Max(0.001f, MathF.Pow(factor, 0.75f)),
            Math.Max(0.4f, MathF.Pow(factor, 0.15f)), Math.Max(0.03f, MathF.Pow(factor, 0.35f)));
    }
}
