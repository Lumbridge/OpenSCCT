using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

/// <summary>Difficulty presets, the light rule, sight, hearing and memory of the bots (docs/AI.md).</summary>
public sealed class AiPerceptionTests
{
    // ---- difficulty ----------------------------------------------------------------------------------------------

    [Fact]
    public void PresetsGetBetterStepByStep()
    {
        AiDifficulty[] p = [AiDifficulty.For(AiSkill.Easy), AiDifficulty.For(AiSkill.Normal), AiDifficulty.For(AiSkill.Hard), AiDifficulty.For(AiSkill.Expert)];
        for (int i = 1; i < p.Length; i++)
        {
            Assert.True(p[i].ReactionTime < p[i - 1].ReactionTime);
            Assert.True(p[i].AimErrorDegrees < p[i - 1].AimErrorDegrees);
            Assert.True(p[i].AimSettledDegrees < p[i - 1].AimSettledDegrees);
            Assert.True(p[i].TurnRateDegrees > p[i - 1].TurnRateDegrees);
            Assert.True(p[i].SightRange > p[i - 1].SightRange);
            Assert.True(p[i].LightThreshold < p[i - 1].LightThreshold);
            Assert.True(p[i].SpotSeconds < p[i - 1].SpotSeconds);
            Assert.True(p[i].HearingScale > p[i - 1].HearingScale);
            Assert.True(p[i].DecisionNoise < p[i - 1].DecisionNoise);
            Assert.True(p[i].GadgetUse > p[i - 1].GadgetUse);
            Assert.True(p[i].Teamwork > p[i - 1].Teamwork);
        }
    }

    [Theory]
    [InlineData("easy", AiSkill.Easy)]
    [InlineData("Normal", AiSkill.Normal)]
    [InlineData("HARD", AiSkill.Hard)]
    [InlineData("expert", AiSkill.Expert)]
    public void PresetNamesParse(string text, AiSkill skill)
    {
        Assert.Equal(skill, AiDifficulty.Parse(text).Skill);
        Assert.False(AiDifficulty.TryParse("godlike", out _));
    }

    // ---- light rule ----------------------------------------------------------------------------------------------

    private static AiLight Point(FVector at, float brightness = 255f, float radius = 20f, byte hue = 0, byte saturation = 255, int effect = 0) =>
        new(at, default, brightness, hue, saturation, radius, 128, effect, -1, false);

    [Fact]
    public void WhiteLightColourIsTheHsvScaleTimesBrightness()
    {
        // Saturation 255 pulls any hue to white; V 255 scales by 0.8213, brightness 255 by 1.
        AiRgb c = AiLightField.Colour(0, 255, 255f);
        Assert.Equal(0.8213f, c.R, 4);
        Assert.Equal(0.8213f, c.G, 4);
        Assert.Equal(0.8213f, c.B, 4);
        AiRgb red = AiLightField.Colour(0, 0, 255f);
        Assert.Equal(0.8213f, red.R, 4);
        Assert.Equal(0f, red.G, 4);
    }

    [Fact]
    public void PointLightFallsOffWithTheSmoothstepToItsRadius()
    {
        // R = 25 (20 + 1) = 525 uu. At the light (facing it) the term is 2 C = 1.64, at R/2 S = 0.5, at R nothing.
        var field = new AiLightField([Point(V(0f, 0f, 0f))]);
        // The viewer is behind the light, so the side it sees faces the light (N.L = 1).
        float near = field.LevelAt(V(1f, 0f, 0f), 0f, V(-1000f, 0f, 0f));
        float mid = field.LevelAt(V(262.5f, 0f, 0f), 0f, V(-1000f, 0f, 0f));
        float far = field.LevelAt(V(530f, 0f, 0f), 0f, V(-1000f, 0f, 0f));
        Assert.Equal(1.6426f, near, 2);
        Assert.Equal(0.8213f, mid, 2);
        Assert.Equal(0f, far, 3);
    }

    [Fact]
    public void TheSideAwayFromTheLightStaysDark()
    {
        var field = new AiLightField([Point(V(0f, 0f, 0f))]);
        // Target at x 200, light behind it (x 0), viewer in front (x 1000): the side the viewer sees faces away.
        float lit = field.LevelAt(V(200f, 0f, 0f), 0f, V(-1000f, 0f, 0f));
        float dark = field.LevelAt(V(200f, 0f, 0f), 0f, V(1000f, 0f, 0f));
        Assert.True(lit > 0.5f, $"lit {lit}");
        Assert.Equal(0f, dark, 4);
    }

    [Fact]
    public void OnlyTheFourStrongestLightsCount()
    {
        var lights = new List<AiLight>();
        for (int i = 0; i < 6; i++)
        {
            lights.Add(Point(V(0f, 0f, 100f + i), brightness: 50f + i * 10f, radius: 40f));
        }

        var six = new AiLightField(lights);
        var four = new AiLightField(lights.GetRange(2, 4));
        FVector target = default;
        Assert.Equal(four.LevelAround(target, 0f), six.LevelAround(target, 0f), 4);
    }

    [Fact]
    public void TheTermIsCappedAtTwo()
    {
        var lights = new List<AiLight>();
        for (int i = 0; i < 4; i++)
        {
            lights.Add(Point(V(0f, 0f, 10f), brightness: 255f, radius: 100f));
        }

        Assert.Equal(2f, new AiLightField(lights).LevelAround(default, 0f), 3);
    }

    [Fact]
    public void ASpotLightOnlyLightsInsideItsCone()
    {
        // Pointing down (pitch -16384), cone 64: phi = 1.15 acos(0.5625) ~ 1.12 rad, so 32 degrees off the axis.
        var spot = new AiLight(V(0f, 0f, 300f), new FRotator(-16384, 0, 0), 255f, 0, 255, 30f, 64, AiK.LE_Spotlight, -1, false);
        var field = new AiLightField([spot]);
        float under = field.LevelAround(V(0f, 0f, 0f), 0f);
        float outside = field.LevelAround(V(400f, 0f, 0f), 0f);
        Assert.True(under > 0.3f, $"under {under}");
        Assert.Equal(0f, outside, 4);
    }

    [Fact]
    public void ZoneAmbientAddsEverywhereInTheZone()
    {
        var field = new AiLightField([], new Dictionary<int, AiRgb> { [3] = new AiRgb(0.2f, 0.2f, 0.2f) }, _ => 3);
        Assert.Equal(0.2f, field.LevelAround(V(500f, 0f, 0f), 0f), 4);
        Assert.True(field.HasData);
        Assert.Equal(AiLightField.UnknownLevel, AiLightField.Unknown.LevelAround(default, 0f));
    }

    [Fact]
    public void TheTorchLightsAlongItsBeamOnlyWhenTheLineIsOpen()
    {
        FRotator aim = default; // +X
        float inBeam = AiLightField.TorchLevel(V(0f, 0f, 0f), aim, V(500f, 20f, 0f), clear: true);
        float blocked = AiLightField.TorchLevel(V(0f, 0f, 0f), aim, V(500f, 20f, 0f), clear: false);
        float aside = AiLightField.TorchLevel(V(0f, 0f, 0f), aim, V(500f, 200f, 0f), clear: true);
        float beyond = AiLightField.TorchLevel(V(0f, 0f, 0f), aim, V(1600f, 0f, 0f), clear: true);
        Assert.True(inBeam > 0.5f, $"in beam {inBeam}");
        Assert.Equal(0f, blocked);
        Assert.Equal(0f, aside);
        Assert.Equal(0f, beyond);
    }

    // ---- sight ---------------------------------------------------------------------------------------------------

    private static (PawnRig Rig, AiWorld World) SightLevel(AiLightField? light = null, bool wall = false)
    {
        CollisionDataBuilder level = Floor();
        if (wall)
        {
            level.AddBox(V(-200f, 400f, 0f), V(200f, 440f, 400f));
        }

        PawnRig rig = Rig(level, null, V(0f, 1000f, 0f));
        AiWorld world = World(rig, V(-300f, -300f, -100f), V(300f, 300f, 300f), light: light);
        world.BeginTick();
        return (rig, world);
    }

    private static readonly AiDifficulty Normal = AiDifficulty.For(AiSkill.Normal);

    [Fact]
    public void ALitTargetInViewIsSeenAndTheSameTargetBehindTheViewIsNot()
    {
        var light = new AiLightField([Point(V(0f, 800f, 150f), radius: 30f)]);
        (PawnRig rig, AiWorld world) = SightLevel(light);
        FVector eye = V(0f, 0f, 60f);
        AiSighting front = AiSight.Look(world, eye, new FRotator(0, 16384, 0), 85f, AiVisionMode.Normal, rig.Pawn, Normal);
        AiSighting behind = AiSight.Look(world, eye, new FRotator(0, 49152, 0), 85f, AiVisionMode.Normal, rig.Pawn, Normal);
        Assert.True(front.Visible, $"strength {front.Strength} light {front.Light}");
        Assert.False(behind.Visible);
    }

    [Fact]
    public void AWallBlocksSight()
    {
        var light = new AiLightField([Point(V(0f, 800f, 150f), radius: 30f)]);
        (PawnRig rig, AiWorld world) = SightLevel(light, wall: true);
        AiSighting s = AiSight.Look(world, V(0f, 0f, 60f), new FRotator(0, 16384, 0), 85f, AiVisionMode.Normal, rig.Pawn, Normal);
        Assert.False(s.Visible);
    }

    [Fact]
    public void ADarkTargetIsOnlySeenThroughNightOrThermalVision()
    {
        var dark = new AiLightField([], new Dictionary<int, AiRgb> { [0] = default }, _ => 0);
        (PawnRig rig, AiWorld world) = SightLevel(dark);
        FVector eye = V(0f, 0f, 60f);
        var view = new FRotator(0, 16384, 0);
        Assert.False(AiSight.Look(world, eye, view, 85f, AiVisionMode.Normal, rig.Pawn, Normal).Visible);
        Assert.True(AiSight.Look(world, eye, view, 85f, AiVisionMode.Night, rig.Pawn, Normal).Visible);
        Assert.True(AiSight.Look(world, eye, view, 85f, AiVisionMode.Thermal, rig.Pawn, Normal).Visible);

        // EMF shows a spy only with its electronics on; motion vision shows nothing by sight, moving or not (only the
        // tracker's locks, PerceptionMotionTests).
        Assert.False(AiSight.Look(world, eye, view, 85f, AiVisionMode.Emf, rig.Pawn, Normal).Visible);
        Assert.False(AiSight.Look(world, eye, view, 85f, AiVisionMode.Motion, rig.Pawn, Normal).Visible);
        rig.Pawn.Velocity = V(0f, 300f, 0f);
        Assert.False(AiSight.Look(world, eye, view, 85f, AiVisionMode.Motion, rig.Pawn, Normal).Visible);
    }

    [Fact]
    public void FartherTargetsNeedMoreLight()
    {
        float close = AiSight.LitStrength(0.2f, 300f, Normal);
        float far = AiSight.LitStrength(0.2f, 2000f, Normal);
        Assert.True(close > far, $"close {close} far {far}");
    }

    // ---- hearing -------------------------------------------------------------------------------------------------

    [Fact]
    public void MakeNoiseReachesTheBotsDampenedAsDecoded()
    {
        PawnRig rig = Rig(Floor(), null, default);
        var board = new AiNoiseBoard();
        rig.Level.SetService<INoiseHearing>(board);
        rig.Pawn.Instigator = rig.Pawn;
        rig.Pawn.MakeNoise(1f);
        board.Gather(rig.Level);
        Assert.Single(board.Noises);
        Assert.Equal(AiK.HearingThreshold, board.Noises[0].Radius, 1);

        // Visibility under 2 scales the loudness by 0.3: radius 2800 sqrt(0.3). (200 uu away: not a repeat.)
        rig.Pawn.Visibility = 1;
        rig.Pawn.Location += V(200f, 0f, 0f);
        rig.Pawn.MakeNoise(1f);
        board.Gather(rig.Level);
        Assert.Contains(board.Noises, n => n.FromMakeNoise && System.Math.Abs(n.Radius - AiK.HearingThreshold * SvmMath.Sqrt(0.3f)) < 1f);
    }

    [Fact]
    public void ARepeatedNoiseWithinTheWindowIsDroppedUnlessLouder()
    {
        PawnRig rig = Rig(Floor(), null, default);
        var board = new AiNoiseBoard();
        rig.Pawn.Instigator = rig.Pawn;
        board.CheckNoiseHearing(rig.Pawn, rig.Pawn, 1f);
        board.CheckNoiseHearing(rig.Pawn, rig.Pawn, 1f);
        board.CheckNoiseHearing(rig.Pawn, rig.Pawn, 1.05f);
        board.CheckNoiseHearing(rig.Pawn, rig.Pawn, 1.2f);
        board.Gather(rig.Level);
        // The second (same) and the third (0.9 x 1.05 <= 1) are dropped; the fourth (0.9 x 1.2 > 1.05) is heard.
        Assert.Equal(2, board.Noises.Count);
        Assert.Equal(2, board.MakeNoiseCount);
    }

    [Fact]
    public void WithoutAListenerMakeNoiseDoesNothing()
    {
        PawnRig rig = Rig(Floor(), null, default);
        rig.Pawn.Instigator = rig.Pawn;
        rig.Pawn.MakeNoise(1f);
        Assert.Null(rig.Level.GetService<INoiseHearing>());
    }

    [Fact]
    public void SoundsFadeByTheListenerRollOff()
    {
        PawnRig rig = Rig(Floor(), null, default);
        AiWorld world = World(rig, V(-300f, -300f, -100f), V(300f, 300f, 300f));
        var noise = new AiNoise(V(0f, 0f, 50f), 400f, 1f, null, null, false, 0f);
        AiDifficulty d = AiDifficulty.For(AiSkill.Normal);
        Assert.Equal(1f, AiNoiseBoard.Heard(world, in noise, V(300f, 0f, 50f), d), 3);

        // Beyond the radius: R / (R + 0.5 (d - R)) = 400 / (400 + 800) at 2000 uu.
        Assert.Equal(400f / 1200f, AiNoiseBoard.Heard(world, in noise, V(2000f, 0f, 50f), d), 3);
        Assert.Equal(0f, AiNoiseBoard.Heard(world, in noise, V(20000f, 0f, 50f), d));
    }

    // ---- memory --------------------------------------------------------------------------------------------------

    [Fact]
    public void MemoryHalvesEveryHalfLife()
    {
        PawnRig rig = Rig(Floor(), null, default);
        var memory = new AiMemory();
        memory.Report(rig.Pawn, V(1f, 2f, 3f), 0f, 0.8f, AiContactSource.Heard);
        AiDifficulty d = AiDifficulty.For(AiSkill.Normal);
        memory.Decay(d.MemoryHalfLife, d);
        Assert.Equal(0.4f, memory.Contacts[0].Confidence, 2);
        for (int i = 0; i < 10; i++)
        {
            memory.Decay(d.MemoryHalfLife, d);
        }

        Assert.Empty(memory.Contacts);
    }
}
