using System;
using System.Collections.Generic;
using CsCheck;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net.Voice;
using OpenScct.Net.Voice.Proximity;
using Xunit;

namespace OpenScct.Net.Tests.Voice;

/// <summary>Proximity voice (an OpenSCCT addition for the hub): falloff, occlusion, volume and mute, and the relay filter.</summary>
public sealed class ProximityVoiceTests
{
    private static readonly ProximityVoiceSettings S = new();

    private sealed class Positions : IVoicePositions
    {
        public readonly Dictionary<ushort, FVector> At = new();

        public bool TryGetPosition(ushort talker, out FVector position) => At.TryGetValue(talker, out position);
    }

    [Fact]
    public void FalloffIsFullNearSilentFarAndSmoothBetween()
    {
        Assert.Equal(1f, ProximityVoice.DistanceGain(0f, S));
        Assert.Equal(1f, ProximityVoice.DistanceGain(S.FullVolumeDistance, S));
        Assert.Equal(0f, ProximityVoice.DistanceGain(S.HearingDistance, S));
        Assert.Equal(0f, ProximityVoice.DistanceGain(S.HearingDistance * 2f, S));
        float mid = ProximityVoice.DistanceGain((S.FullVolumeDistance + S.HearingDistance) / 2f, S);
        Assert.InRange(mid, 0.49f, 0.51f);
        // No step at either end of the ramp.
        Assert.InRange(ProximityVoice.DistanceGain(S.FullVolumeDistance + 1f, S), 0.999f, 1f);
        Assert.InRange(ProximityVoice.DistanceGain(S.HearingDistance - 1f, S), 0f, 0.001f);
    }

    [Fact]
    public void FalloffNeverRisesWithDistance()
    {
        Gen.Select(Gen.Float[0f, 5000f], Gen.Float[0f, 5000f]).Sample((a, b) =>
        {
            float near = System.MathF.Min(a, b);
            float far = System.MathF.Max(a, b);
            return ProximityVoice.DistanceGain(far, S) <= ProximityVoice.DistanceGain(near, S);
        });
    }

    [Fact]
    public void WallsMuffleVolumeScalesAndMuteSilences()
    {
        VoiceMix open = ProximityVoice.Mix(200f, occluded: false, playerVolume: 1f, muted: false, S);
        VoiceMix wall = ProximityVoice.Mix(200f, occluded: true, playerVolume: 1f, muted: false, S);
        Assert.Equal(1f, open.Gain);
        Assert.Equal(S.OpenLowPassHz, open.LowPassHz);
        Assert.Equal(S.OcclusionGain, wall.Gain);
        Assert.Equal(S.OccludedLowPassHz, wall.LowPassHz);
        Assert.Equal(2f, ProximityVoice.Mix(200f, false, 5f, false, S).Gain);
        Assert.Equal(0f, ProximityVoice.Mix(200f, false, -1f, false, S).Gain);
        Assert.False(ProximityVoice.Mix(200f, false, 1f, muted: true, S).Audible);
        Assert.False(ProximityVoice.Mix(S.HearingDistance + 1f, false, 2f, false, S).Audible);
    }

    [Fact]
    public void ListenerSmoothsTowardTheTargetAndKeepsPlayerSettings()
    {
        var listener = new ProximityListener();
        var me = new FVector(0f, 0f, 0f);
        var him = new FVector(100f, 0f, 0f);
        Assert.Equal(1f, listener.Update(7, me, him, null, 1f / 60f).Gain);

        // Behind a wall: the gain eases down over a few frames, never below the occluded target.
        VoiceMix mix = default;
        for (int i = 0; i < 3; i++)
        {
            mix = listener.Update(7, me, him, static (_, _) => true, 1f / 60f);
        }

        Assert.InRange(mix.Gain, S.OcclusionGain, 0.99f);
        for (int i = 0; i < 120; i++)
        {
            mix = listener.Update(7, me, him, static (_, _) => true, 1f / 60f);
        }

        Assert.InRange(mix.Gain, S.OcclusionGain - 0.001f, S.OcclusionGain + 0.001f);

        listener.SetVolume(7, 0.5f);
        listener.SetMuted(8, true);
        Assert.Equal(0.5f, listener.VolumeOf(7));
        Assert.False(listener.Update(8, me, him, null, 1f / 60f).Audible);
        listener.Forget(7);
        Assert.Equal(1f, listener.VolumeOf(7));
        Assert.False(listener.MixOf(7).Audible);
    }

    [Fact]
    public void RelayFilterSendsOnlyInRangeWithHysteresis()
    {
        var at = new Positions();
        var filter = new ProximityRelayFilter(at);
        at.At[1] = new FVector(0f, 0f, 0f);
        at.At[2] = new FVector(S.HearingDistance - 10f, 0f, 0f);
        at.At[3] = new FVector(S.HearingDistance + 10f, 0f, 0f);
        Assert.True(filter.CanHear(1, 2));
        Assert.False(filter.CanHear(1, 3));
        Assert.False(filter.CanHear(1, 1));
        Assert.False(filter.CanHear(1, 9));

        // Walking a little out of range keeps the stream; far beyond the margin stops it, and coming back needs the range again.
        at.At[2] = new FVector(S.HearingDistance + S.RelayHysteresis - 10f, 0f, 0f);
        Assert.True(filter.CanHear(1, 2));
        at.At[2] = new FVector(S.HearingDistance + S.RelayHysteresis + 10f, 0f, 0f);
        Assert.False(filter.CanHear(1, 2));
        at.At[2] = new FVector(S.HearingDistance + 10f, 0f, 0f);
        Assert.False(filter.CanHear(1, 2));

        // Host mutes are one way and per pair.
        at.At[2] = new FVector(10f, 0f, 0f);
        filter.ServerMute(1, 2, true);
        Assert.False(filter.CanHear(1, 2));
        Assert.True(filter.CanHear(2, 1));
        filter.ServerMute(1, 2, false);
        Assert.True(filter.CanHear(1, 2));
        filter.Forget(2);
        Assert.Equal(0, filter.PairsInRange);
    }

    [Fact]
    public void FiftyPlayersEachHearOnlyTheirNeighbours()
    {
        var at = new Positions();
        var filter = new ProximityRelayFilter(at);
        for (ushort i = 1; i <= 50; i++)
        {
            // A line of players 500 uu apart: each hears those within 3000 uu (six on each side).
            at.At[i] = new FVector(500f * i, 0f, 0f);
        }

        int pairs = 0;
        for (ushort l = 1; l <= 50; l++)
        {
            for (ushort t = 1; t <= 50; t++)
            {
                pairs += filter.CanHear(l, t) ? 1 : 0;
            }
        }

        int expected = 0;
        for (int l = 1; l <= 50; l++)
        {
            for (int t = 1; t <= 50; t++)
            {
                expected += l != t && System.Math.Abs(l - t) * 500 <= S.HearingDistance ? 1 : 0;
            }
        }

        Assert.Equal(expected, pairs);
        Assert.True(pairs < 50 * 49 / 3);
    }

    [Fact]
    public void RelayWithTheFilterSendsAcrossSidesOnlyToListenersInRange()
    {
        using var rig = new VoiceRig(SimLinkProfile.A, mode: EVoiceChatMode.VCM_Open);
        var at = new Positions();
        rig.Relay.Proximity = new ProximityRelayFilter(at);
        rig.Add(1, 0);
        rig.Add(2, 1);
        rig.Add(3, 0);
        at.At[1] = new FVector(0f, 0f, 0f);
        at.At[2] = new FVector(1500f, 0f, 0f);
        at.At[3] = new FVector(0f, 5000f, 0f);
        rig.Step(5);
        rig.Talk(1);
        rig.Step(100);

        // The merc next to the spy hears him (Open mode); the spy far away gets nothing, not even a talker entry.
        Assert.True(rig[2].FramesFrom(1) > 50, $"near heard {rig[2].FramesFrom(1)} frames");
        Assert.Equal(0, rig[3].FramesFrom(1));
        Assert.Empty(rig[3].Client.Talkers);
        Assert.True(rig.Relay.Stats.OutOfRange > 50);

        // Walking up to the talker brings him in.
        at.At[3] = new FVector(0f, 2000f, 0f);
        rig.Step(50);
        Assert.True(rig[3].FramesFrom(1) > 20, $"far heard {rig[3].FramesFrom(1)} frames after walking up");
    }

    [Fact]
    public void NoFilterLeavesTheRelayAsItWas()
    {
        using var rig = new VoiceRig(SimLinkProfile.A, mode: EVoiceChatMode.VCM_Open);
        rig.Add(1, 0);
        rig.Add(2, 1);
        rig.Step(5);
        rig.Talk(1);
        rig.Step(60);
        Assert.Null(rig.Relay.Proximity);
        Assert.True(rig[2].FramesFrom(1) > 30);
        Assert.Equal(0, rig.Relay.Stats.OutOfRange);
        Assert.All(rig[2].Client.Talkers, t => Assert.Null(t.Spatial));
    }

    [Fact]
    public void PlaybackAppliesTheFalloffAndMufflesThroughWalls()
    {
        using var rig = new VoiceRig(SimLinkProfile.A, mode: EVoiceChatMode.VCM_Open);
        rig.Add(1, 0);
        rig.Add(2, 1);
        rig.Step(5);
        rig.Talk(1, VoiceSignals.Tone(3000, 10.0));
        rig.Step(40);
        VoiceTalker talker = Assert.Single(rig[2].Client.Talkers);

        double Level(int frames)
        {
            rig[2].Heard.Clear();
            rig.Step(frames);
            float[] heard = rig[2].From(1);
            return VoiceSignals.Rms(heard.AsSpan(heard.Length / 4));
        }

        double flat = Level(40);
        Assert.True(flat > 0.1, $"flat level {flat}");

        // 1500 uu away in the open: the smooth-step falloff, and nothing filtered.
        var at = new Positions();
        at.At[1] = new FVector(1500f, 0f, 0f);
        var playback = new ProximityPlayback();
        playback.Update(rig[2].Client, new FVector(0f, 0f, 0f), at, null, 0.02f);
        VoiceMix open = Assert.IsType<VoiceMix>(talker.Spatial);
        float expected = ProximityVoice.DistanceGain(1500f, S);
        Assert.Equal(expected, open.Gain, 4);
        Assert.InRange(expected, 0.5f, 0.7f);
        Assert.Equal(1f, ProximityVoice.LowPassCoefficient(open.LowPassHz, VoiceK.SampleRate));
        double far = Level(40);
        Assert.InRange(far / flat, expected - 0.08, expected + 0.08);

        // Behind a wall at the same distance: the occlusion gain, and the low-pass takes most of a 3 kHz tone on top.
        var walled = new ProximityPlayback();
        walled.Update(rig[2].Client, new FVector(0f, 0f, 0f), at, (_, _) => true, 0.02f);
        VoiceMix muffled = talker.Spatial!.Value;
        Assert.Equal(expected * S.OcclusionGain, muffled.Gain, 4);
        Assert.Equal(S.OccludedLowPassHz, muffled.LowPassHz);
        double behind = Level(40);
        Assert.True(behind < far * S.OcclusionGain * 0.6, $"behind {behind} far {far}");

        // A talker with no position (no pawn) is silent; releasing goes back to flat playback.
        playback.Update(rig[2].Client, new FVector(0f, 0f, 0f), new Positions(), null, 0.02f);
        Assert.Equal(VoiceMix.Silent, talker.Spatial);
        Assert.Equal(0d, Level(20), 6);
        ProximityPlayback.Release(rig[2].Client);
        Assert.Null(talker.Spatial);
        Assert.InRange(Level(40) / flat, 0.9, 1.1);
    }

    [Fact]
    public void LowPassCoefficientIsOneInTheOpenAndSmallForMuffling()
    {
        Assert.Equal(1f, ProximityVoice.LowPassCoefficient(S.OpenLowPassHz, VoiceK.SampleRate));
        Assert.Equal(1f, ProximityVoice.LowPassCoefficient(0f, VoiceK.SampleRate));
        float muffled = ProximityVoice.LowPassCoefficient(S.OccludedLowPassHz, VoiceK.SampleRate);
        Assert.InRange(muffled, 0.1f, 0.2f);
        Assert.True(ProximityVoice.LowPassCoefficient(600f, VoiceK.SampleRate) < muffled);
    }
}
