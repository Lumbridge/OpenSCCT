using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>
/// The two draws of the Shocker on the fire-mode key against a capture of the original (spec/equipment/spy-shocker.md
/// "Tap and hold draws in the original"): SteeE, a standing spy, the key byte held 6 frames (tap) or 60 frames (hold)
/// at 60 fps. Both draws run the same transition (0.3 s) and the same pistol clip (DrawP190, WeaponLoaded 3 for
/// 0.267 s); the tap starts it the frame after the release with the laser coming on when the armed walk starts, the hold
/// starts it 0.5 s after the press with no laser. The spy's velocity is the same in both, so a merc's motion tracker
/// (which only reads velocity) cannot tell them apart.
/// </summary>
public sealed class ShockerDrawTests
{
    private readonly ITestOutputHelper _out;

    public ShockerDrawTests(ITestOutputHelper output) => _out = output;

    private sealed record Frame(int F, string State, byte Loaded, bool Laser, float Speed);

    private List<Frame> Draw(int holdFrames, int walkFrom = -1, int walkTo = -1, int frames = 150)
    {
        PawnRig rig = Rig(PawnTeam.Spy, crouched: false);
        rig.SetFastSpeed(false);
        OpenScct.Game.Equipment.CombatEquipment.Install(rig.Level);
        SStickyShocker gun = rig.Level.SpawnActor(new SStickyShocker(), rig.Pawn.Location, rig.Pawn.Rotation)!;
        gun.Instigator = rig.Pawn;
        gun.SetOwner(rig.Pawn);
        gun.GiveTo(rig.Pawn);
        var trace = new List<Frame>();
        var log = new StringBuilder();
        for (int f = 0; f < frames; f++)
        {
            var input = new PlayerInputFrame
            {
                bFireMode = (byte)(f < holdFrames ? 1 : 0),
                BT_Up = (byte)(f >= walkFrom && f < walkTo ? 1 : 0),
            };
            rig.Step(input);
            var pistol = rig.Pawn.MyWeapon as SWeaponAttaque;
            var frame = new Frame(f, rig.Controller.GetStateName().ToString(), rig.Pawn.WeaponLoaded, pistol?.bLaserOn ?? false,
                rig.Pawn.Velocity.Size());
            trace.Add(frame);
            log.Append(CultureInfo.InvariantCulture, $"{f} {frame.State} wl={frame.Loaded} laser={frame.Laser} v={frame.Speed:F1}\n");
        }

        _out.WriteLine(log.ToString());
        return trace;
    }

    private static int First(List<Frame> t, System.Func<Frame, bool> p) => t.FindIndex(x => p(x));

    [Fact]
    public void ATapStartsTheDrawAtTheReleaseAndTheLaserComesOnWithTheArmedWalk()
    {
        List<Frame> t = Draw(holdFrames: 6);
        int transition = First(t, x => x.State == "s_PlayerWalkingMario_T_0HTo2H");
        int drawing = First(t, x => x.Loaded == 3);
        int drawn = First(t, x => x.Loaded == 2);
        int armed = First(t, x => x.State == "s_PlayerWalkingSt2H");
        int laser = First(t, x => x.Laser);

        // Capture (frame k is the k-th tick from the press, the key up from frame 6): transition at 7, WeaponLoaded 3
        // at 10 and 2 at 23 (the pistol clip), the armed walk and the laser at 25.
        Assert.Equal(7, transition);
        Assert.InRange(armed - transition, 16, 19);
        Assert.InRange(laser, armed, armed + 1);
        Assert.True(drawing < 0 || drawing > transition);
        Assert.True(drawn >= transition);
    }

    [Fact]
    public void AHoldStartsTheDrawAfterHalfASecondWithoutTheLaser()
    {
        List<Frame> t = Draw(holdFrames: 60);
        int transition = First(t, x => x.State == "s_PlayerWalkingMario_T_0HTo2H");
        int armed = First(t, x => x.State == "s_PlayerWalkingSt2H");

        // Capture: transition at frame 32 (the first tick past press + 0.5 s), armed walk at 50, no laser.
        Assert.InRange(transition, 30, 32);
        Assert.InRange(armed - transition, 16, 20);
        Assert.DoesNotContain(t, x => x.Laser);
    }

    [Fact]
    public void BothDrawsLeaveTheSpyWithTheSameVelocityForTheMotionTracker()
    {
        List<Frame> tap = Draw(holdFrames: 6, walkFrom: 90, walkTo: 140);
        List<Frame> hold = Draw(holdFrames: 60, walkFrom: 90, walkTo: 140);
        for (int f = 0; f < 90; f++)
        {
            Assert.Equal(0f, tap[f].Speed, 1);
            Assert.Equal(0f, hold[f].Speed, 1);
        }

        for (int f = 90; f < 140; f++)
        {
            Assert.Equal(tap[f].Speed, hold[f].Speed, 2);
        }
    }
}
