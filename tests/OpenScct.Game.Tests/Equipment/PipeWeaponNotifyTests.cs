using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class PipeWeaponNotifyTests
{
    private static PawnClip Clip(string name) => new(new Name(name), 30, 30f)
    {
        Notifies = [new PawnClipNotify(0.25f, new UAnimNotify_Script { NotifyName = new Name("NotifyDrawUndrawWeapon") })],
    };

    [Theory]
    [InlineData("PipHStAlBg1", EPhysics.PHYS_OnPipeH, 0, 1)]
    [InlineData("PipHStSpBg2", EPhysics.PHYS_OnPipeH, 0, 1)]
    [InlineData("PipHStAlEd1", EPhysics.PHYS_OnPipeH, 1, 0)]
    [InlineData("PipHStSpEd2", EPhysics.PHYS_OnPipeH, 2, 0)]
    [InlineData("PipHStSpFl2", EPhysics.PHYS_Falling, 2, 0)]
    public void AuthoredPipeBodyNotifyDrawsOrHolstersAtItsFrame(string clip, EPhysics physics, byte initial, byte expected)
    {
        var scene = new CombatScene();
        var (_, body) = scene.Player(true, new FVector(0f, 0f, 300f));
        var spy = Assert.IsAssignableFrom<SPawnAttaque>(body);
        SStickyShocker pistol = scene.Give(new SStickyShocker(), spy);
        pistol.GiveTo(spy);
        MovementNotifies.Install(scene.Level);
        spy.Physics = physics;
        spy.WeaponLoaded = initial;
        if (initial != 0) pistol.ServerSwitchLaser(true);
        spy.PawnAnim.Owner = spy;
        spy.PawnAnim.Play(0, Clip(clip), 1f, 0f, loop: false);

        for (int tick = 0; tick < 5; tick++) spy.PawnAnim.Advance(Sim.DeltaTime, _ => { });
        Assert.Equal(initial, spy.WeaponLoaded);
        for (int tick = 0; tick < 20; tick++) spy.PawnAnim.Advance(Sim.DeltaTime, _ => { });

        Assert.Equal(expected, spy.WeaponLoaded);
        if (expected == 0) Assert.False(pistol.bLaserShouldBeOn);
    }

    [Theory]
    [InlineData("PipHStSpBg2", SBasePawn.DRAWCHANNEL)]
    [InlineData("UnrelatedOverlay", 0)]
    public void OtherChannelsOrClipsCannotToggleThePipePistol(string clip, int channel)
    {
        var scene = new CombatScene();
        var (_, body) = scene.Player(true, new FVector(0f, 0f, 300f));
        var spy = Assert.IsAssignableFrom<SPawnAttaque>(body);
        SStickyShocker pistol = scene.Give(new SStickyShocker(), spy);
        pistol.GiveTo(spy);
        MovementNotifies.Install(scene.Level);
        spy.Physics = EPhysics.PHYS_OnPipeH;
        spy.WeaponLoaded = 0;
        spy.PawnAnim.Owner = spy;
        spy.PawnAnim.Play(channel, Clip(clip), 1f, 0f, loop: false);

        for (int tick = 0; tick < 25; tick++) spy.PawnAnim.Advance(Sim.DeltaTime, _ => { });

        Assert.Equal(0, spy.WeaponLoaded);
    }
}
