using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>
/// The look of a sticky shock on the victim (spec/equipment/sticky-shock-effect.md rule 3.6, execute-action.md 53 to
/// 55): the electric action sets the Stickess skin and spawns the seven spark emitters at their bones, the first with
/// the blinking blue light; the stop action removes them and the skin; a dedicated server's own copy spawns none.
/// </summary>
public sealed class ShockerSparksTests
{
    private static (CombatScene Scene, SBasePawn Merc) Merc(ENetMode mode)
    {
        var scene = new CombatScene(mode);
        (_, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        merc.Role = ENetRole.ROLE_Authority;
        return (scene, merc);
    }

    private static SEffectShockedPawnEmitter[] Sparks(CombatScene scene)
    {
        var list = new System.Collections.Generic.List<SEffectShockedPawnEmitter>();
        foreach (SEffectShockedPawnEmitter a in scene.Level.AllActors<SEffectShockedPawnEmitter>())
        {
            if (!a.bDeleteMe)
            {
                list.Add(a);
            }
        }

        return list.ToArray();
    }

    [Theory]
    [InlineData(SBasePawn.SA_DMG_ELECTRIC)]
    [InlineData(SBasePawn.SA_DMG_ELECTRIC_NOANIMS)]
    public void TheElectricActionSpawnsSevenSparksAtTheirBonesAndTheStopRemovesThem(int action)
    {
        (CombatScene scene, SBasePawn merc) = Merc(ENetMode.NM_Client);
        CombatActionAnimation.Execute(merc, action);
        Assert.True(merc.bStickyShocking);
        Assert.True(merc.bStickySkinned);
        SEffectShockedPawnEmitter[] sparks = Sparks(scene);
        Assert.Equal(7, sparks.Length);
        Assert.Equal(SBasePawn.ShockSfxBones, merc.Shock_SfxActors.Select(a => a!.AttachmentBone).ToArray());
        Assert.All(sparks, s => Assert.Same(merc, s.Owner));
        Actor first = merc.Shock_SfxActors[0]!;
        Assert.True(first.bDynamicLight);
        Assert.Equal(ELightType.LT_Blink, first.LightType);
        Assert.Equal((byte)145, first.LightHue);
        Assert.Equal((byte)174, first.LightSaturation);
        Assert.Equal(150f, first.LightBrightness);
        Assert.Equal(10f, first.LightRadius);
        Assert.False(merc.Shock_SfxActors[1]!.bDynamicLight);

        // A second electric action spawns only the missing ones.
        CombatActionAnimation.Execute(merc, action);
        Assert.Equal(7, Sparks(scene).Length);

        CombatActionAnimation.Execute(merc, SBasePawn.SA_DMG_ELECTRIC_STOP);
        Assert.False(merc.bStickyShocking);
        Assert.False(merc.bStickySkinned);
        Assert.Empty(Sparks(scene));
        Assert.All(merc.Shock_SfxActors, a => Assert.Null(a));
    }

    [Fact]
    public void TheEndOfTheFreezeKeepsTheSparks()
    {
        (CombatScene scene, SBasePawn merc) = Merc(ENetMode.NM_Client);
        CombatActionAnimation.Execute(merc, SBasePawn.SA_DMG_ELECTRIC);
        CombatActionAnimation.Execute(merc, SBasePawn.SA_DMG_ELECTRIC_STOP_ANIM);
        Assert.Equal(7, Sparks(scene).Length);
        Assert.True(merc.bStickyShocking);
    }

    [Fact]
    public void ADedicatedServerSpawnsNoSparks()
    {
        (CombatScene scene, SBasePawn merc) = Merc(ENetMode.NM_DedicatedServer);
        CombatActionAnimation.Execute(merc, SBasePawn.SA_DMG_ELECTRIC);
        Assert.True(merc.bStickyShocking);
        Assert.Empty(Sparks(scene));
    }
}
