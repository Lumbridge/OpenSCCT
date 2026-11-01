using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>Controller commands reach the selected gadget and preserve their tick and role restrictions.</summary>
public sealed class ControllerActionTests
{
    private sealed class ProbeController : SPlayerController
    {
        private static readonly StateTable ProbeStates = new(SPlayerController.ClassStates,
            new ObserveAltFire("Observe"), new ObserveAltFire("s_UseObject_StickyCam"), new ObserveAltFire("s_UseObject_CamNetwork"));

        public override StateTable GetStateTable() => ProbeStates;
        public int AltFireCalls { get; private set; }
        public SGadget? FiredGadget { get; private set; }
        public byte AltFireHeldAtCall { get; private set; }

        private sealed class ObserveAltFire(string stateName) : ActorState(nameof(ProbeController), stateName), IAltFire
        {
            public void AltFire(SPlayerController self, float value)
            {
                var pc = (ProbeController)self;
                pc.AltFireCalls++;
                pc.FiredGadget = self.SBPawn?.SelectedGadget;
                pc.AltFireHeldAtCall = self.bAltFire;
            }
        }
    }

    private sealed class Rig
    {
        public Rig(bool merc = false, byte weaponLoaded = 2)
        {
            World = new World(1, ENetMode.NM_ListenServer);
            Pawn = World.Level.SpawnActor<SBasePawn>(merc ? new SPawnDefense_1Mesh() : new SPawnAttaque_1Mesh(), default, default)!;
            Pc = World.Level.SpawnActor(new ProbeController(), default, default)!;
            Pc.Possess(Pawn);
            Pc.GotoState("Observe");
            Pawn.WeaponLoaded = weaponLoaded;
            Gadgets = Enumerable.Range(0, 4).Select(_ => Give(new SGadget { bActivatable = true })).ToArray();
            Pawn.SelectedGadget = Gadgets[3];
        }

        public World World { get; }
        public ProbeController Pc { get; }
        public SBasePawn Pawn { get; }
        public SGadget[] Gadgets { get; }
        public int GasCount
        {
            get
            {
                int count = 0;
                foreach (SStickyCamEmitter emitter in World.Level.AllActors<SStickyCamEmitter>())
                    if (!emitter.bDeleteMe) count++;
                return count;
            }
        }

        public T Give<T>(T gadget) where T : SGadget
        {
            T spawned = World.Level.SpawnActor(gadget, default, default)!;
            spawned.GiveTo(Pawn);
            return spawned;
        }
    }

    private sealed class QuickUseProbe : SGadget
    {
        public int QuickUseCalls { get; private set; }
        public override void QuickUse() => QuickUseCalls++;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ArmedLaunchSelectsRequestedSlotAndFiresOnceWithoutHoldingAltFire(int slot)
    {
        var rig = new Rig();
        rig.Pawn.SelectedGadget = rig.Gadgets[(slot + 1) % rig.Gadgets.Length];
        var input = new LocalInput();
        input.ApplyButtons(rig.Pc);

        Assert.True(PlayerCommands.Execute(rig.Pc, new InputCommand("LaunchGadget", slot)));

        Assert.Same(rig.Gadgets[slot], rig.Pawn.SelectedGadget);
        Assert.Same(rig.Gadgets[slot], rig.Pc.FiredGadget);
        Assert.Equal(1, rig.Pc.AltFireCalls);
        Assert.Equal(0, rig.Pc.AltFireHeldAtCall);
        Assert.Equal(0, rig.Pc.bAltFire);
        input.ApplyButtons(rig.Pc);
        Assert.Equal(0, rig.Pc.bAltFire);
        Assert.Equal(1, rig.Pc.AltFireCalls);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void HolsteredDrawingAndMercenaryLaunchesDoNotSelectOrFire(bool merc, byte weaponLoaded)
    {
        var rig = new Rig(merc, weaponLoaded);
        Assert.True(PlayerCommands.Execute(rig.Pc, new InputCommand("LaunchGadget", 0)));
        Assert.Same(rig.Gadgets[3], rig.Pawn.SelectedGadget);
        Assert.Equal(0, rig.Pc.AltFireCalls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void InvalidLaunchSlotDoesNotFireThePreviouslySelectedGadget(int slot)
    {
        var rig = new Rig();
        Assert.True(PlayerCommands.Execute(rig.Pc, new InputCommand("LaunchGadget", slot)));
        Assert.Same(rig.Gadgets[3], rig.Pawn.SelectedGadget);
        Assert.Equal(0, rig.Pc.AltFireCalls);
    }

    [Theory]
    [InlineData("s_UseObject_StickyCam")]
    [InlineData("s_UseObject_CamNetwork")]
    public void CameraViewsDoNotSelectOrLaunchGadgets(string state)
    {
        var rig = new Rig();
        rig.Pc.GotoState(state);
        Assert.True(rig.Pc.IsInState(state));
        Assert.True(PlayerCommands.Execute(rig.Pc, new InputCommand("LaunchGadget", 0)));
        Assert.Same(rig.Gadgets[3], rig.Pawn.SelectedGadget);
        Assert.Equal(0, rig.Pc.AltFireCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StickyCameraLaunchUsesItsSpecialPathInsteadOfTheStatesAltFire(int charges)
    {
        var rig = new Rig();
        SGadStickyCam sticky = rig.Give(new SGadStickyCam { Charge = charges });
        // Put the camera in slot 1 while leaving the existing selection elsewhere.
        sticky.MyGadget = rig.Gadgets[1].MyGadget;
        rig.Gadgets[0].MyGadget = sticky;
        rig.Gadgets[3].MyGadget = null;

        Assert.True(PlayerCommands.Execute(rig.Pc, new InputCommand("LaunchGadget", 1)));

        Assert.Same(sticky, rig.Pawn.SelectedGadget);
        Assert.Equal(0, rig.Pc.AltFireCalls);
        Assert.Equal(0, rig.Pc.bAltFire);
        SimEvent[] emptySounds = rig.World.Events.Late.ToArray()
            .Where(e => e.Kind == SimEventKind.PlaySound && e.What.ToString() == sticky.SND_EmptyCharge.Path).ToArray();
        if (charges == 0)
        {
            SimEvent empty = Assert.Single(emptySounds);
            Assert.Equal(rig.Pc.ActorId, empty.ActorId);
            Assert.True(empty.Owned);
            Assert.Equal((int)ESoundSlot.SLOT_Misc, empty.Slot);
        }
        else Assert.Empty(emptySounds);
    }

    [Theory]
    [InlineData("Observe", 0)]
    [InlineData("s_UseObject_StickyCam", 1)]
    [InlineData("s_UseObject_CamNetwork", 1)]
    public void CameraExitDispatchesAltFireOnlyInsideACameraView(string state, int expectedCalls)
    {
        var rig = new Rig();
        rig.Pc.GotoState(state);

        Assert.True(PlayerCommands.Execute(rig.Pc, new InputCommand("PadCameraExit")));

        Assert.Equal(expectedCalls, rig.Pc.AltFireCalls);
        Assert.Equal(0, rig.Pc.bAltFire);
        Assert.Same(rig.Gadgets[3], rig.Pawn.SelectedGadget);
    }

    [Theory]
    [InlineData("s_UseObject_StickyCam")]
    [InlineData("s_UseObject_CamNetwork")]
    public void CameraGasUsesTheViewedStickyCameraAndHonorsItsCooldown(string state)
    {
        var rig = new Rig();
        rig.Pc.GotoState(state);
        SStickyCam cam = rig.World.Level.SpawnActor(new SStickyCam(), default, default)!;
        cam.SetOwner(rig.Pawn);
        cam.Smoke_NbDoses = 2;
        rig.Pawn.UsedObject = cam;

        Assert.True(PlayerCommands.Execute(rig.Pc, new InputCommand("PadCameraGas")));

        Assert.Equal(1, cam.Smoke_NbDoses);
        Assert.Equal(1, rig.GasCount);
        Assert.Equal(0, rig.Pc.AltFireCalls);
        Assert.Same(rig.Gadgets[3], rig.Pawn.SelectedGadget);
        Assert.True(PlayerCommands.Execute(rig.Pc, new InputCommand("PadCameraGas")));
        Assert.Equal(1, cam.Smoke_NbDoses);
        Assert.Equal(1, rig.GasCount);
    }

    [Fact]
    public void CameraGasDoesNothingOutsideACameraOrWhenViewingANonStickyObject()
    {
        var rig = new Rig();
        SStickyCam cam = rig.World.Level.SpawnActor(new SStickyCam(), default, default)!;
        cam.Smoke_NbDoses = 2;
        rig.Pawn.UsedObject = cam;

        Assert.True(PlayerCommands.Execute(rig.Pc, new InputCommand("PadCameraGas")));
        Assert.Equal(2, cam.Smoke_NbDoses);
        Assert.Equal(0, rig.GasCount);

        rig.Pc.GotoState("s_UseObject_CamNetwork");
        rig.Pawn.UsedObject = rig.Gadgets[0];
        Assert.True(PlayerCommands.Execute(rig.Pc, new InputCommand("PadCameraGas")));
        Assert.Equal(0, rig.GasCount);
        Assert.Equal(0, rig.Pc.AltFireCalls);
    }

    [Fact]
    public void SelectedCloakQuickUseActivatesTheRealCloak()
    {
        var rig = new Rig(weaponLoaded: 0);
        var spy = (SPawnAttaque)rig.Pawn;
        spy.EnergyLevel = spy.EnergyLevelMax;
        SGadCloak cloak = rig.Give(new SGadCloak { LastUsed = -10f });
        spy.SelectedGadget = cloak;
        Assert.True(cloak.CanTurnOn());

        Assert.True(PlayerCommands.Execute(rig.Pc, new InputCommand("PadCloakQuick")));

        Assert.Equal(255, spy.DesiredGlassEffect);
        Assert.Same(cloak, spy.SelectedGadget);
        Assert.Equal(0, rig.Pc.AltFireCalls);
        Assert.Equal(0, rig.Pc.bAltFire);
    }

    [Fact]
    public void CloakShortcutDoesNotSelectAnUnselectedCloakOrQuickUseAnotherGadget()
    {
        var rig = new Rig(weaponLoaded: 0);
        var spy = (SPawnAttaque)rig.Pawn;
        spy.EnergyLevel = spy.EnergyLevelMax;
        SGadCloak cloak = rig.Give(new SGadCloak { LastUsed = -10f });
        QuickUseProbe selected = rig.Give(new QuickUseProbe { bActivatable = true });
        spy.SelectedGadget = selected;
        Assert.True(cloak.CanTurnOn());

        Assert.True(PlayerCommands.Execute(rig.Pc, new InputCommand("PadCloakQuick")));

        Assert.Same(selected, spy.SelectedGadget);
        Assert.Equal(0, spy.DesiredGlassEffect);
        Assert.Equal(0, selected.QuickUseCalls);
        Assert.Equal(0, rig.Pc.AltFireCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongRollPulsesCrouchOnlyWhenLatchedAndTheNextTickClearsThePulse(bool latched)
    {
        var pc = new SPlayerController { bCrouchWithRoll = latched };
        var input = new LocalInput();
        input.Set("bRoll", true);
        input.ApplyButtons(pc);

        Assert.True(PlayerCommands.Execute(pc, new InputCommand("LongRoll")));

        Assert.Equal(latched ? 1 : 0, pc.bCrouch);
        Assert.Equal(1, pc.bRoll);
        Assert.Equal(0, input.Get("bCrouch"));
        input.ApplyButtons(pc);
        Assert.Equal(0, pc.bCrouch);
        Assert.Equal(1, pc.bRoll);
        input.Set("bRoll", false);
        input.ApplyButtons(pc);
        Assert.Equal(0, pc.bRoll);
    }
}
