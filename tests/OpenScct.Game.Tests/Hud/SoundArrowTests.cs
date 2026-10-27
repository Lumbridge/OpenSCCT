using System;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Game;
using Xunit;

namespace OpenScct.Game.Tests.Hud;

/// <summary>spec/hud/merc-reticle.md rules 8 to 10: the merc's sound arrows from ClientNotifyNoise.</summary>
public class SoundArrowTests
{
    private sealed record Rig(GameWorld W, SPlayerController Pc, SBasePawn Pawn, SHud Hud);

    private static Rig Make(bool withHud = true)
    {
        var w = new GameWorld();
        SBasePawn pawn = w.SpawnPawn(spy: false, new FVector(0f, 0f, 0f));
        var pc = w.Level.SpawnActor(new SPlayerController(), default, default)!;
        pc.Pawn = pawn;
        SHud hud = w.Level.SpawnActor(new SHud(), default, default)!;
        hud.PlayerOwner = pc;
        if (withHud)
        {
            pc.myHUD = hud;
        }

        w.Run(2f);   // the echo cue needs an arrow that was off for a second
        return new Rig(w, pc, pawn, hud);
    }

    private static Actor NoiseAt(Rig r, FVector at) => r.W.Level.SpawnActor(new SNotifyNoise(), at, default)!;

    [Theory]
    [InlineData(600f, 0f, "Forward")]
    [InlineData(-600f, 0f, "Backward")]
    [InlineData(0f, 600f, "Right")]
    [InlineData(0f, -600f, "Left")]
    public void A_noise_lights_the_arrow_toward_it_for_a_second(float x, float y, string arrow)
    {
        Rig r = Make();
        float now = r.W.Level.TimeSeconds;
        r.Pc.ClientNotifyNoise(NoiseAt(r, new FVector(x, y, 0f)), 1f);
        float[] times = [r.Hud.ForwardArrowTime, r.Hud.BackwardArrowTime, r.Hud.RightArrowTime, r.Hud.LeftArrowTime];
        string[] names = ["Forward", "Backward", "Right", "Left"];
        for (int i = 0; i < names.Length; i++)
        {
            Assert.Equal(names[i] == arrow ? now + 1f : 0f, times[i], 3);
        }

        Assert.Equal(0f, r.Hud.TopArrowTime);
        Assert.Contains(r.W.World.Events.Current.ToArray(), e => e.Kind == SimEventKind.PlaySound);
    }

    [Fact]
    public void Loudness_falls_off_to_nothing_at_1600_uu_and_a_noise_above_also_lights_the_top_arrow()
    {
        Rig r = Make();
        r.Pc.ClientNotifyNoise(NoiseAt(r, new FVector(800f, 0f, 300f)), 1f);
        Assert.Equal((1600f - MathF.Sqrt((800f * 800f) + (300f * 300f))) / 1600f, r.Hud.ForwardArrowInt, 3);
        Assert.True(r.Hud.TopArrowTime > 0f);

        Rig far = Make();
        far.Pc.ClientNotifyNoise(NoiseAt(far, new FVector(1700f, 0f, 0f)), 1f);
        Assert.Equal(0f, far.Hud.ForwardArrowTime);
    }

    [Fact]
    public void The_merc_weapon_dummy_gives_no_arrow()
    {
        Rig r = Make();
        r.Pc.ClientNotifyNoise(r.W.Level.SpawnActor(new SWeaponDefenseDummy(), new FVector(600f, 0f, 0f), default), 1f);
        Assert.Equal(0f, r.Hud.ForwardArrowTime);
    }

    [Fact]
    public void Without_a_HUD_the_server_sends_the_call_to_the_player()
    {
        Rig r = Make(withHud: false);
        Actor noise = NoiseAt(r, new FVector(600f, 0f, 0f));
        r.Pc.ClientNotifyNoise(noise, 0.5f);
        (PlayerController target, GameCall call) = Assert.Single(r.W.Host.Calls, c => c.Call.Function.ToString() == "ClientNotifyNoise");
        Assert.Same(r.Pc, target);
        Assert.Same(noise, call.Actor);
        Assert.Equal(0.5f, call.Float1);
        Assert.Equal(0f, r.Hud.ForwardArrowTime);
    }
}
