using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class AmmoStockMenuTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClubhouseCratesAppearInTheMercUseMenu(bool latency)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("ClubE") is null, "Clubhouse content is required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "ClubE", Side = PlaySide.Merc, Rules = PlayRules.MapSettings,
            HostMode = PlayHostMode.Inline, MatchSeed = 3, SimLink = latency ? SimLinkProfile.B : null,
        }, clock);
        void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }
        for (int i = 0; i < 1800 && (session.LocalPawn is null || session.ServerController?.Pawn is null ||
            session.LocalController!.IsInState("WaitingForPawn") ||
            session.ServerController.Pawn is not SBasePawn { MyWeapon: SWeaponDefense } ||
            session.LocalPawn.MyWeapon is not SWeaponDefense); i++) Tick();
        Assert.NotNull(session.LocalPawn);
        var pawn = (SBasePawn)session.ServerController!.Pawn!;
        // Possession and the profile's inventory grant use separate reliable actor channels. The crate
        // test begins after the bounded initial loadout exchange, not merely after the pawn appears.
        Assert.IsAssignableFrom<SWeaponDefense>(pawn.MyWeapon);
        Assert.IsAssignableFrom<SWeaponDefense>(session.LocalPawn!.MyWeapon);
        var crates = new List<SAmmoStock>();
        foreach (var crate in session.Host!.World.Level.AllActors<SAmmoStock>()) crates.Add(crate);
        Assert.NotEmpty(crates);
        foreach (var crate in crates)
        {
            Assert.NotNull(crate.InteractionObj);
            var entry = crate.InteractionObj;
            var at = crate.Location + crate.Rotation.Vector() * 100f + new FVector(0, 0, 50f);
            Assert.True(pawn.SetLocation(at), $"Cannot place merc at {at}");
            Assert.True(session.LocalPawn!.SetLocation(at));
            pawn.Rotation = session.LocalPawn.Rotation = (crate.Location - at).Rotation();
            session.LocalController!.Rotation = pawn.Rotation;
            Tick(90);
            Assert.Contains(session.ServerController.IManager!.Interactions, x => ReferenceEquals(x, entry));
            Assert.Contains(session.LocalController.IManager!.Interactions, x => x is SAmmoStockInteraction);
            Assert.True(session.LocalController.IManager.HasUsableInteractions());
            Assert.Contains(session.LocalController.IManager.Interactions, x => x is SLeaveInteraction && x.CanUse(session.LocalController));
            Assert.True(pawn.MyWeapon is SWeaponDefense,
                $"Authority weapon missing: health={pawn.Health}, deleted={pawn.bDeleteMe}, state={session.ServerController.GetStateName()}, " +
                $"samePawn={ReferenceEquals(pawn, session.ServerController.Pawn)}, position={pawn.Location}, client={session.LocalController.GetStateName()}, " +
                $"clientHealth={session.LocalPawn.Health}, crate={crate.ActorId}");
            var gun = (SWeaponDefense)pawn.MyWeapon!;
            gun.Ammo = gun.ClipAmmo = 1;
            gun.PublishCarriedAmmo();
            Tick(30);
            session.Input.Set("bUsing", true);
            Tick(10);
            Assert.True(session.LocalController.IsInState("s_MenuInteraction"));
            Assert.IsType<SAmmoStockInteraction>(session.LocalController.IManager.GetCurrentInteraction());
            session.Input.Set("bUsing", false);
            Tick(240);
            Assert.Equal(gun.MaxAmmo, gun.Ammo);
            Assert.Equal(gun.MaxAmmo, ((SWeaponDefense)session.LocalPawn.MyWeapon!).Ammo);
        }
    }
}
