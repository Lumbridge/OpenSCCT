using System.Collections.Generic;
using OpenScctGame.Voice;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class VoiceMicrophoneStateTests
{
    [Fact]
    public void FailedCaptureRetriesWithBackoffAndAChangedDeviceTriesImmediately()
    {
        var state = new VoiceMicrophoneState();
        var tried = new List<string>();
        bool available = false;
        bool Start(string device) { tried.Add(device); return available; }
        state.Update(true, "Missing", 0, Start, () => { });
        for (int i = 0; i < 240; i++) state.Update(true, "Missing", 1.0 / 60, Start, () => { });
        Assert.Single(tried);
        state.Update(true, "Present", 0, Start, () => { });
        Assert.Equal(["Missing", "Present"], tried);
        available = true;
        state.Update(true, "Present", 5, Start, () => { });
        Assert.Equal(3, tried.Count);
        state.Update(true, "Present", 20, Start, () => { });
        Assert.Equal(3, tried.Count);
        state.Update(true, "Another", 0, Start, () => { });
        Assert.Equal(4, tried.Count);
        Assert.Equal("Another", tried[3]);
    }

    [Fact]
    public void DisableStopsOnceAndReenableOrNewVisitStartsImmediately()
    {
        var state = new VoiceMicrophoneState();
        int starts = 0, stops = 0;
        bool Start(string _) { starts++; return true; }
        void Stop() => stops++;
        state.Update(true, null, 0, Start, Stop);
        state.Update(false, null, 0, Start, Stop);
        state.Update(false, null, 0, Start, Stop);
        Assert.Equal(1, stops);
        state.Update(true, "", 0, Start, Stop);
        Assert.Equal(2, starts);
        state.Reset();
        state.Update(true, "", 0, Start, Stop);
        Assert.Equal(3, starts);
    }
}
