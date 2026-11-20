using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Svm;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class M4ReloadAudioTests
{
    [Fact]
    public void EmptyAndPartialReloadsChooseTheirOwnSoundOnTheSameWeapon()
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(false, new FVector(0, 0, 100));
        var rifle = scene.Give(new M4A1S(), pawn);
        rifle.GiveTo(pawn); pawn.WeaponLoaded = 2;
        scene.Run(.1f);
        foreach (int rounds in new[] { 0, 7, 0, 1 })
        {
            rifle.ClipAmmo = rounds; rifle.Ammo = 100;
            string sound = rounds == 0 ? "OpenScctRifles.m4a1s_reload" : "OpenScctRifles.m4a1s_reload_tactical";
            Assert.True(rifle.eventReload());
            Assert.Contains(scene.World.Events.Late.ToArray(), x => x.Kind == SimEventKind.PlaySound && x.What == new Name(sound));
            Assert.Equal("OpenScctRifles.m4a1s_reload", rifle.ReloadSound.Path);
            float seconds = rifle.Definition.ReloadDuration(rounds);
            Assert.Equal(rounds == 0 ? 2.536f : 2.090f, seconds, 3);
            scene.World.Step((int)(seconds * Sim.Hz));
            Assert.Equal(rounds, rifle.ClipAmmo);
            scene.World.Step(1);
            Assert.Equal(rifle.ClipMaxAmmo, rifle.ClipAmmo);
        }
    }
}
