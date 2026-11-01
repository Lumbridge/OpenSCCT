using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// The merc's Use edge in weapon walking (charge, berserk) with the post-charge slow-down, and the drop attack
/// trigger of the walking and falling states (spec/combat/melee-elbow-charge-berserk.md rules 1 and 3,
/// spec/combat/drop-attack.md rule 1).
/// </summary>
public sealed class MercAndDropTests
{
    private static readonly Name s_PlayerCharging = new("s_PlayerCharging");
    private static readonly Name s_Berserk360 = new("s_Berserk360");

    private sealed class SpinAnimations : IPawnAnimationSource
    {
        private static readonly PawnClip Spin = new(new Name("AtakStAlTu"), 21, 15f);

        public bool TryGetClip(SBasePawn pawn, Name sequence, out PawnClip clip)
        {
            if (sequence == Spin.Name)
            {
                clip = Spin;
                return true;
            }

            return PawnAnimationSource.Default.TryGetClip(pawn, sequence, out clip);
        }
    }

    private static PawnRig MercRig(PawnRigOptions? options = null)
    {
        PawnRig rig = Rig(PawnTeam.Merc, options: options);
        // A match long under way: no charge or berserk in the last seconds.
        rig.Controller.fLastChargingTime = -100f;
        rig.Controller.fLastBerserkTime = -100f;
        return rig;
    }

    [Fact]
    public void UseWithTheStickForwardStartsTheMercCharge()
    {
        PawnRig rig = MercRig();
        rig.Hold(Up, 10);
        rig.Step(Up with { bUsing = 1 });
        Assert.True(rig.Controller.IsInState(s_PlayerCharging), rig.Controller.GetStateName().ToString());
    }

    [Fact]
    public void UseStandingStillStartsTheBerserkSpin()
    {
        PawnRig rig = MercRig();
        rig.Pawn.MyWeapon = rig.Level.SpawnActor(new SWeapon(), rig.Pawn.Location, default);
        rig.Step(NoInput with { bUsing = 1 });
        Assert.True(rig.Controller.IsInState(s_Berserk360), rig.Controller.GetStateName().ToString());
        Assert.Equal(rig.Level.TimeSeconds, rig.Controller.fLastBerserkTime, 3);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AChargeWaitsThreeSecondsAfterTheSpinEnds(bool hasTarget, bool interrupted)
    {
        PawnRig rig = MercRig(new PawnRigOptions { Animations = new SpinAnimations() });
        rig.Pawn.MyWeapon = rig.Level.SpawnActor(new SWeapon(), rig.Pawn.Location, default);
        rig.Step(NoInput with { bUsing = 1 });
        Assert.True(rig.Controller.IsInState(s_Berserk360));
        if (interrupted)
        {
            rig.Hold(NoInput, 15);
            rig.Pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_2H);
            rig.Controller.eventChangeState(MovementStateNames.s_PlayerWalkingSt2H);
        }

        for (int i = 0; i < 300 && rig.Controller.IsInState(s_Berserk360); i++)
        {
            rig.Step(NoInput);
        }

        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingSt2H));
        float spinEnded = rig.Level.TimeSeconds;
        float spinStarted = rig.Controller.fLastBerserkTime;
        if (hasTarget)
        {
            // Exercise the shorter charge cooldown when a homing target is available.
            RigMate spy = rig.AddMate(PawnTeam.Spy, V(0f, 1000f, 0f), 0);
            rig.Controller.TargetPawn = spy.Pawn;
            rig.Controller.fLastChargingTime = spinEnded;
        }

        rig.Hold(Up, 170);
        Assert.True(rig.Level.TimeSeconds - spinStarted > 3f);
        rig.Step(Up with { bUsing = 1 });
        Assert.False(rig.Controller.IsInState(s_PlayerCharging));
        Assert.True(rig.Level.TimeSeconds - spinEnded < 3f);
        Assert.Equal(spinStarted, rig.Controller.fLastBerserkTime);

        // A fresh press after the recovery window can charge again.
        rig.Hold(Up, 12);
        rig.Step(Up with { bUsing = 1 });
        Assert.True(rig.Controller.IsInState(s_PlayerCharging), rig.Controller.GetStateName().ToString());
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void ReplayingASpinExitDoesNotChangeTheLiveChargeCooldown(float replayOffset)
    {
        PawnRig rig = MercRig();
        rig.Controller.GotoState(s_Berserk360);
        rig.Controller.GotoState(MovementStateNames.s_PlayerWalkingSt2H);
        float spinEnded = rig.Level.TimeSeconds;
        rig.Hold(Up, 120);

        rig.World.IsReplaying = true;
        try
        {
            using (rig.Level.ReplayTime(0, spinEnded + replayOffset, Sim.DeltaTime))
            {
                rig.Controller.GotoState(s_Berserk360);
                rig.Controller.GotoState(MovementStateNames.s_PlayerWalkingSt2H);
            }
        }
        finally
        {
            rig.World.IsReplaying = false;
        }

        rig.Hold(Up, 50);
        rig.Step(Up with { bUsing = 1 });
        Assert.False(rig.Controller.IsInState(s_PlayerCharging));
        rig.Hold(Up, 12);
        rig.Step(Up with { bUsing = 1 });
        Assert.True(rig.Controller.IsInState(s_PlayerCharging), rig.Controller.GetStateName().ToString());
    }

    [Fact]
    public void WithoutARifleTheMercsUseIsNoBerserk()
    {
        PawnRig rig = MercRig();
        rig.Step(NoInput with { bUsing = 1 });
        Assert.False(rig.Controller.IsInState(s_Berserk360));
    }

    [Fact]
    public void HoldingUseDoesNotStartASecondCharge()
    {
        PawnRig rig = MercRig();
        rig.Hold(Up, 10);
        rig.Controller.fLastChargingTime = rig.Level.TimeSeconds;
        // Within the cooldown the press gives no charge, and the held key gives no new edge later.
        rig.Hold(Up with { bUsing = 1 }, 20);
        Assert.False(rig.Controller.IsInState(s_PlayerCharging));
    }

    [Fact]
    public void AfterAChargeTheMercWalksAtHalfSpeedForTwoSeconds()
    {
        PawnRig fresh = MercRig();
        fresh.Hold(Up, 60);
        float full = fresh.Pawn.GroundSpeed;

        PawnRig tired = MercRig();
        tired.Controller.fLastChargingTime = tired.Level.TimeSeconds;
        tired.Hold(Up, 60);
        float slow = tired.Pawn.GroundSpeed;
        Assert.True(slow < full * 0.75f, $"slow {slow} full {full}");

        // Back to full speed half a second after the two seconds.
        tired.Hold(Up, 120);
        Assert.Equal(full, tired.Pawn.GroundSpeed, 0);
    }

    // ---- drop attack ----------------------------------------------------------------------------------------

    /// <summary>Records the drop attacks the states ask for.</summary>
    private sealed class DropHooks : IMovementHooks
    {
        public readonly List<SBasePawn> Drops = [];

        public void SetCameraMode(SPlayerController PC, ECamMode Mode)
        {
        }

        public void CameraSmooth(SPlayerController PC, float Smooth)
        {
        }

        public void PlayWeaponClip(SPlayerController PC, MovementWeaponClip Clip)
        {
        }

        public void SetWeaponLoaded(SBasePawn Pawn, int Num) => MovementHooksLocal.Instance.SetWeaponLoaded(Pawn, Num);

        public void SwitchLaser(SPlayerController PC, bool On)
        {
        }

        public bool UseSearch(SPlayerController PC, int Step, bool bCantRollCuzColl) => false;

        public void DiscardPickup(SPlayerController PC)
        {
        }

        public void PlayAnimDeath(SPlayerController PC)
        {
        }

        public void FootPrint(SBasePawn Pawn, bool bTrueIfLeftFoot)
        {
        }

        public void LaunchQuickGadget(SPlayerController PC)
        {
        }

        public void PerformDropAttack(SPlayerController PC, SBasePawn PawnToAttack) => Drops.Add(PawnToAttack);

        public void TellServerForDropAttack(SPlayerController PC, SBasePawn PawnToAttack) => Drops.Add(PawnToAttack);
    }

    [Fact]
    public void ASpyComingDownOnAMercDropAttacksHim()
    {
        var hooks = new DropHooks();
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, Floor().Build(), null, new PawnRigOptions { Hooks = hooks });
        RigMate merc = rig.AddMate(PawnTeam.Merc, V(0f, 0f, 0f), 16384);
        // The spy 120 above the merc's head.
        rig.Place(V(0f, 0f, 175f + 120f + 87.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        for (int i = 0; i < 120 && hooks.Drops.Count == 0; i++)
        {
            rig.Step(NoInput);
        }

        Assert.NotEmpty(hooks.Drops);
        Assert.Same(merc.Pawn, hooks.Drops[0]);
    }

    [Fact]
    public void ASpyStandingOnAMercDropAttacksHimFromTheWalkingState()
    {
        var hooks = new DropHooks();
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, Floor().Build(), null, new PawnRigOptions { Hooks = hooks });
        RigMate merc = rig.AddMate(PawnTeam.Merc, V(0f, 0f, 0f), 16384);
        rig.Place(V(0f, 0f, 175f + 87.5f + 0.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        rig.Pawn.SetBase(merc.Pawn);
        rig.Step(NoInput);
        Assert.Contains(merc.Pawn, hooks.Drops);
    }
}
