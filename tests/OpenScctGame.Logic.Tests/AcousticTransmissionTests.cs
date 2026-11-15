using OpenScctGame.Audio;
using OpenScctGame.Options;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class AcousticTransmissionTests
{
    [Fact]
    public void OldSettingsKeepSpeakersAndSystemDevice()
    {
        GameSettings settings = GameSettings.FromJson("{}");
        Assert.Equal("Speakers", settings.AudioOutputMode);
        Assert.Equal("", settings.AudioOutputDevice);
        settings = GameSettings.FromJson("{\"audio_output_mode\":\"headphones\",\"audio_output_device\":\"USB DAC\"}");
        Assert.Equal("Headphones", settings.AudioOutputMode);
        Assert.Equal("USB DAC", GameSettings.FromJson(settings.ToJson()).AudioOutputDevice);
        settings.AudioOutputMode = "invalid";
        settings.AudioOutputDevice = null!;
        settings.Clamp();
        Assert.Equal("Speakers", settings.AudioOutputMode);
        Assert.Equal("", settings.AudioOutputDevice);
    }

    [Fact]
    public void MaterialsFilterHighFrequenciesMoreThanBroadbandAndKeepIndependentRoomLevels()
    {
        Assert.Equal(TransmissionLevels.Clear, AcousticTransmission.Through(100, 1, 1));
        TransmissionLevels glass = AcousticTransmission.Through(8, 3, 3);
        TransmissionLevels stone = AcousticTransmission.Through(80, 9, 9);
        Assert.True(stone.DirectHF < glass.DirectHF);
        Assert.True(stone.DirectGain < glass.DirectGain);
        Assert.True(stone.RoomGain > stone.DirectGain);
        Assert.True(stone.RoomHF > stone.DirectHF);
        Assert.True(stone.DirectHF < stone.DirectGain);
        Assert.Equal(AcousticTransmission.Through(30, 3, 9), AcousticTransmission.Through(30, 9, 3));
        Assert.Equal(AcousticTransmission.Through(30, 0, 0), AcousticTransmission.Through(30, -1, 999));
    }

    [Fact]
    public void TransitionIsContinuousAndIndependentOfUpdatePartition()
    {
        TransmissionLevels target = AcousticTransmission.Through(100, 8, 8);
        TransmissionLevels once = TransmissionLevels.Clear.Approach(target, 0.1f);
        TransmissionLevels partitioned = TransmissionLevels.Clear;
        for (int i = 0; i < 10; i++) partitioned = partitioned.Approach(target, 0.01f);
        Assert.Equal(once.DirectGain, partitioned.DirectGain, 5);
        Assert.Equal(once.DirectHF, partitioned.DirectHF, 5);
        Assert.True(once.DirectGain > target.DirectGain && once.DirectGain < 1);
        Assert.Equal(TransmissionLevels.Clear, TransmissionLevels.Clear.Approach(target, 0));
    }
}
