using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

public sealed class WeaponOwnerLifetimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReceivedShotCannotFireFromADeadOrPreviousPawn(bool spy, bool replaced)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (pc, body) = scene.Player(spy, new FVector(0, 0, 100));
        SWeapon gun = spy ? scene.Give(new SWeaponAttaque(), body) : scene.Give(new SWeaponDefense(), body);
        gun.Controller = pc;
        body.MyWeapon = gun;
        void Shot(int sequence) => pc.ReceiveCombatCall(spy
            ? new CombatCall(new Name("ServerFireWeaponWithLocation")) { Target = gun, Int1 = sequence, Vector1 = body.Location }
            : new CombatCall(new Name("ServerFireWeapon")) { Target = gun, Int2 = sequence }, true);
        Shot(1);
        Assert.Equal(1, gun.ShotCounter);
        if (replaced)
        {
            var (_, fresh) = scene.Player(spy, new FVector(500, 0, 100));
            pc.Possess(fresh);
            // A retained actor can still be owned by this controller; possession is the lifetime boundary.
            gun.SetOwner(pc);
        }
        else body.Health = 0;
        Shot(2);
        Assert.Equal(1, gun.ShotCounter);
    }
}
