using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Movement;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>
/// A quick-launched smoke, chaff or flash-bang grenade pops at its first hit (the floor under the spy's hand), long
/// before the 3 s fuse; an aimed one bounces and keeps the fuse (spec/equipment/grenade-throw.md rules 6a and 10a).
/// </summary>
public sealed class QuickLaunchPopTests
{
    private static readonly PlayerInputFrame Still = new();

    /// <summary>Well under a second: the drop from the hand to the floor.</summary>
    private const int PopWithin = (int)(0.5f * Sim.Hz);

    private static PawnRig Kit()
    {
        PawnRig rig = MovementTestKit.Rig(PawnTeam.Spy);
        CombatEquipment.Install(rig.Level);
        Loadouts.UpdateInventory(rig.Pawn, EGameMode.GM_Neutralization);
        rig.Hold(Still, (int)(K.QuickLaunchCooldown * Sim.Hz) + 10);
        return rig;
    }

    private static T? Live<T>(PawnRig rig)
        where T : Actor
    {
        foreach (T a in rig.Level.AllActors<T>())
        {
            if (!a.bDeleteMe)
            {
                return a;
            }
        }

        return null;
    }

    /// <summary>Steps until a <typeparamref name="T"/> exists; the tick count it took.</summary>
    private static T WaitFor<T>(PawnRig rig, int limit = 300)
        where T : Actor
    {
        for (int i = 0; i < limit; i++)
        {
            if (Live<T>(rig) is { } a)
            {
                return a;
            }

            rig.Step(Still);
        }

        throw new Xunit.Sdk.XunitException($"no {typeof(T).Name} after {limit} ticks");
    }

    [Fact]
    public void AQuickSmokeDropsDownwardFromUnderTheHandAndPopsOnTheFloor()
    {
        PawnRig rig = Kit();
        rig.Controller.eventGadgetQuickChoice(0);
        SProjSmoke smoke = WaitFor<SProjSmoke>(rig);
        Assert.True(smoke.QuickLaunch);
        Assert.Equal(K.QuickThrowForce, smoke.Velocity.Z, 1f);
        Assert.Equal(0f, smoke.Velocity.X, 1f);
        Assert.True(smoke.Location.Z < rig.Pawn.Location.Z, $"start {smoke.Location} pawn {rig.Pawn.Location}");
        Assert.False(rig.Pawn.bQuickGadgetHasBegun);

        int ticks = 0;
        while (Live<SSmokeEmitter>(rig) is null && ticks < 3 * Sim.Hz)
        {
            rig.Step(Still);
            ticks++;
        }

        Assert.True(ticks <= PopWithin, $"the cloud came {ticks} ticks after the throw");
        Assert.True(smoke.bDeleteMe);
        SSmokeEmitter cloud = Live<SSmokeEmitter>(rig)!;
        float floor = rig.Pawn.Location.Z - rig.Pawn.CollisionHeight;
        Assert.True(cloud.Location.Z < floor + 20f, $"cloud at {cloud.Location.Z}, floor {floor}");
    }

    [Fact]
    public void AQuickChaffBurstsOnTheFloor()
    {
        PawnRig rig = Kit();
        rig.Controller.eventGadgetQuickChoice(1);
        SProjChaffe chaff = WaitFor<SProjChaffe>(rig);
        int ticks = 0;
        while (!chaff.Exploded && ticks < 3 * Sim.Hz)
        {
            rig.Step(Still);
            ticks++;
        }

        Assert.True(ticks <= PopWithin, $"the burst came {ticks} ticks after the throw");
        Assert.Equal(GadgetStates.s_Brouillage, chaff.GetStateName());
        Assert.NotNull(Live<SChaffeEmitter>(rig));
    }

    [Fact]
    public void AQuickFlashBangGoesOffOnTheFloor()
    {
        PawnRig rig = Kit();
        SGadSmokeGrenade launcher = (SGadSmokeGrenade)rig.Pawn.SelectedGadget!;
        launcher.ProjectileClass = new ClassRef("SBase.SProjFlashBang");
        launcher.ThrowGren(K.QuickThrowForce, rig.Pawn.Location, new FRotator(K.QuickThrowPitch, 0, 0), true);
        SProjFlashBang flash = WaitFor<SProjFlashBang>(rig, 1);
        int ticks = 0;
        while (!flash.bDeleteMe && ticks < 3 * Sim.Hz)
        {
            rig.Step(Still);
            ticks++;
        }

        Assert.True(ticks <= PopWithin, $"the flash-bang went off {ticks} ticks after the throw");
    }

    [Fact]
    public void AnAimedSmokeBouncesAndKeepsItsThreeSecondFuse()
    {
        PawnRig rig = Kit();
        SGadSmokeGrenade launcher = (SGadSmokeGrenade)rig.Pawn.SelectedGadget!;
        launcher.ThrowGren(450, rig.Pawn.Location, new FRotator(-8000, 0, 0), false);
        SProjSmoke smoke = WaitFor<SProjSmoke>(rig, 1);
        Assert.False(smoke.QuickLaunch);
        bool bounced = false;
        int ticks = 0;
        while (Live<SSmokeEmitter>(rig) is null && ticks < 4 * Sim.Hz)
        {
            rig.Step(Still);
            bounced |= smoke.bAlreadyRebounded;
            ticks++;
        }

        Assert.True(bounced);
        Assert.InRange(ticks, (3 * Sim.Hz) - 2, (3 * Sim.Hz) + 2);
    }

    [Fact]
    public void AClientCopyOfAQuickGrenadeGoesAtItsFirstHitWithoutACloud()
    {
        PawnRig rig = Kit();
        var copy = (SProjSmoke)DeviceClasses.Spawn(rig.Pawn, new ClassRef("SBase.SProjSmoke"), null, default,
            rig.Pawn.Location, default)!;
        copy.Role = ENetRole.ROLE_SimulatedProxy;
        copy.QuickLaunch = true;
        copy.eventHitWall(new FVector(0f, 0f, 1f), null);
        Assert.True(copy.bDeleteMe);
        Assert.False(copy.Exploded);
        Assert.Null(Live<SSmokeEmitter>(rig));
    }

    [Theory]
    [InlineData(SBasePawn.SA_COMPUTER_ENTER)]
    [InlineData(SBasePawn.SA_HACK_ENTER)]
    public void EnteringATerminalOrAHackCancelsARunningQuickThrow(int action)
    {
        // ExecuteAction game 0x10ad6840 cases 0xc4 and 0xc7 end with the quick-gadget cancel.
        PawnRig rig = Kit();
        rig.Pawn.bQuickGadgetHasBegun = true;
        Assert.True(InteractionActions.Execute(rig.Pawn, action));
        Assert.False(rig.Pawn.bQuickGadgetHasBegun);
    }
}
