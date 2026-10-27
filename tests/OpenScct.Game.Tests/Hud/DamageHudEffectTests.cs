using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using OpenScct.Game.Tests.Equipment;
using Xunit;

namespace OpenScct.Game.Tests.Hud;

/// <summary>
/// The game-state half of ClientDamageHudEffect (0x10ab0990) and SHud.SetHudPerturbationLength (0x10a92880)
/// (spec/hud/screen-effects.md rules 7 and 10).
/// </summary>
public sealed class DamageHudEffectTests
{
    private static readonly FVector At = new(0f, 0f, 100f);

    private static (CombatScene Scene, SPlayerController PC) Player(bool spy)
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, _) = scene.Player(spy, At);
        pc.myHUD = scene.Level.SpawnActor(new SHud(), At, default);
        return (scene, pc);
    }

    private static SGoggle GiveGoggle(CombatScene scene, SPlayerController pc)
    {
        SGoggle goggle = scene.Level.SpawnActor(new SGoggle(), At, default)!;
        goggle.SetOwner(pc);
        goggle.SPC = pc;
        pc.Goggle = goggle;
        return goggle;
    }

    [Fact]
    public void ASpyIsNeverPerturbed()
    {
        (_, SPlayerController spy) = Player(true);
        spy.SetHudPerturbationLength(17f);
        Assert.False(spy.IsHudPerturbed);
    }

    [Fact]
    public void ExtendingAPerturbationKeepsItsStartAndEndsAtNowPlusTheNewLength()
    {
        (CombatScene scene, SPlayerController merc) = Player(false);
        var hud = (SHud)merc.myHUD!;
        merc.SetHudPerturbationLength(5f);
        float start = hud.TimetobeginPerturb;
        scene.Run(1f);
        float now = merc.Level.TimeSeconds;
        merc.SetHudPerturbationLength(1f);
        Assert.Equal(start, hud.TimetobeginPerturb);
        Assert.Equal(now + 1f, hud.TimetobeginPerturb + hud.HudPerturbationLength, 3);
    }

    [Fact]
    public void AnOrdinaryHitPerturbsAMercForOneAndAHalfSeconds()
    {
        (_, SPlayerController merc) = Player(false);
        merc.ClientDamageHudEffect(SDamageType.SDamage_Explosion);
        Assert.True(merc.IsHudPerturbed);
        Assert.Equal(1.5f, ((SHud)merc.myHUD!).HudPerturbationLength);
    }

    [Fact]
    public void FlashFoamAndStickyShockDoNotPerturb()
    {
        (_, SPlayerController merc) = Player(false);
        merc.ClientDamageHudEffect(SDamageType.SDTFlash);
        merc.ClientDamageHudEffect(SDamageType.SDTExtincteurProjection);
        merc.ClientDamageHudEffect(SDamageType.SDTStickyShocker);
        Assert.False(merc.IsHudPerturbed);
    }

    [Fact]
    public void AFlashPlaysTheWhistleOnTheFlashedPlayersMachine()
    {
        (CombatScene scene, SPlayerController merc) = Player(false);
        merc.ClientDamageHudEffect(SDamageType.SDTFlash);
        SimEvent whistle = Assert.Single(scene.World.Events.Current.ToArray(), e => e.Kind == SimEventKind.PlaySound
            && e.What == new Name(merc.Snd_FlashBangSiffle.Path));
        Assert.Equal(merc.ActorId, whistle.ActorId);
    }

    [Fact]
    public void NoWhistleInMotionVision()
    {
        (CombatScene scene, SPlayerController merc) = Player(false);
        SGoggle goggle = GiveGoggle(scene, merc);
        goggle.SwitchMode(1);
        goggle.eventDown();
        Assert.Equal(SGoggle.REN_MotionTrackingVision, goggle.CurrentMode);
        merc.ClientDamageHudEffect(SDamageType.SDTFlash);
        Assert.DoesNotContain(scene.World.Events.Current.ToArray(), e => e.Kind == SimEventKind.PlaySound
            && e.What == new Name(merc.Snd_FlashBangSiffle.Path));
    }

    [Fact]
    public void FoamSwitchesMotionVisionOff()
    {
        (CombatScene scene, SPlayerController merc) = Player(false);
        SGoggle goggle = GiveGoggle(scene, merc);
        goggle.SwitchMode(1);
        goggle.eventDown();
        Assert.Equal(SGoggle.REN_MotionTrackingVision, goggle.CurrentMode);
        merc.ClientDamageHudEffect(SDamageType.SDTExtincteurProjection);
        Assert.NotEqual(SGoggle.REN_MotionTrackingVision, goggle.CurrentMode);
    }
}
