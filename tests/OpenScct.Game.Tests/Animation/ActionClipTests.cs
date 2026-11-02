using System.IO;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Animation;

/// <summary>
/// The clips of the ExecuteAction cases a player sees in a match beyond moving (spec/native/execute-action.md): the
/// overlays are drawn (their channel blended in from the case's bone) and blended out at the clip's end
/// (SBasePawn.AnimEnd), deaths, hits, the weapon overlay, wake-ups and the clips AnimEnd chains on channel 0.
/// </summary>
public sealed class ActionClipTests
{
    private const float Dt = 1f / 60f;

    private static readonly Name Spine1 = new("B Spine1");
    private static readonly Name Head = new("B Head");

    private static PawnRig MercRig() => Rig(PawnTeam.Merc);

    /// <summary>Runs the pawn's animation for <paramref name="seconds"/> with its AnimEnd, as the pawn's tick does.</summary>
    private static void Run(PawnRig rig, float seconds)
    {
        int frames = (int)(seconds / Dt);
        for (int i = 0; i < frames; i++)
        {
            rig.Step(default);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FeetLedgeCompletionRestoresTheStandingPoseAndMovement(bool armed, bool leftFoot)
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn pawn = rig.Pawn;
        PawnAnimation anim = pawn.PawnAnim;
        pawn.WeaponLoaded = armed ? (byte)2 : (byte)0;
        pawn.bIsLeftPart = leftFoot;
        ActionAnimation.WalkSet(pawn, SBasePawn.SA_WALK_CR_0H);
        pawn.ExecuteAction(SBasePawn.SA_FEETLEDGE);
        rig.Controller.GotoState(MovementStateNames.s_FeetLedge);
        pawn.NetworkRootMotion.LockRootMotion(pawn.iLockRootMotionType);
        pawn.OldPhysics = EPhysics.PHYS_RootMotion;
        FVector incomingVelocity = new(14f, 28f, 0f);
        pawn.Velocity = incomingVelocity;

        // A completed mantle must replace its displaced base pose even if movement stops immediately.
        // Draw and roll overlays also have to release their contribution to the restored standing pose.
        PawnActions.Once(pawn, SBasePawn.DRAWCHANNEL, pawn.ADrawStand2H, 0f);
        anim.SetBlendParams(SBasePawn.DRAWCHANNEL, 1f, bone: Spine1);
        anim.SetBlendParams(SBasePawn.DBLJUMPCHANNEL, 1f);
        for (int tick = 0; tick < 120 && rig.Controller.IsInState(MovementStateNames.s_FeetLedge); tick++)
        {
            pawn.AdvancePredictionAnimation(Dt);
        }

        Assert.Equal(armed ? MovementStateNames.s_PlayerWalkingSt2H : MovementStateNames.s_PlayerWalkingSt0H,
            rig.Controller.GetStateName());
        Assert.Equal(EPhysics.PHYS_Walking, pawn.Physics);
        Assert.Equal(0, pawn.NetworkRootMotion.RootLockType);
        Assert.Equal(armed ? pawn.AWaitSt2 : pawn.AWaitSt0, anim[0].Sequence);
        Assert.True(anim[0].bLoop);
        Assert.True(anim[0].bPlaying);
        Assert.Equal(armed ? pawn.AWalkStFd2 : pawn.AWalkStFd0, pawn.ForwardAnim);
        Assert.Equal(armed ? pawn.ARunStFd2 : pawn.ARunStFd0, pawn.RunAnim);
        Assert.Equal(0f, anim[SBasePawn.DRAWCHANNEL].BlendAlpha);
        Assert.Equal(0f, anim[SBasePawn.DRAWCHANNEL].Rate);
        Assert.Equal(0f, anim[SBasePawn.DBLJUMPCHANNEL].BlendAlpha);
        Assert.Equal(armed ? incomingVelocity : pawn.Rotation.Vector() * pawn.RunningSpeed, pawn.Velocity);
        if (!armed)
        {
            Assert.Equal(pawn.RunAnim, anim[4].Sequence);
            Assert.Equal(1f, anim[4].BlendAlpha);
            Assert.Equal(pawn.ForwardAnim, anim[5].Sequence);
        }
    }

    [Fact]
    public void TheMercsFireOverlayIsDrawnFromSpine1AndBlendsOutAtItsEnd()
    {
        PawnRig rig = MercRig();
        SBasePawn pawn = rig.Pawn;
        pawn.ExecuteAction(SBasePawn.SA_FIRE);
        PawnAnimChannel c = pawn.PawnAnim[SBasePawn.WEAPONCHANNEL];
        Assert.Equal(new Name("ReloStSpFd3"), c.Sequence);
        Assert.Equal(1f, c.BlendAlpha);
        Assert.Equal(Spine1, c.StartBone);

        Run(rig, 3f);
        Assert.Equal(0f, pawn.PawnAnim[SBasePawn.WEAPONCHANNEL].BlendAlpha, 3);
        Assert.Equal(IK_Type_.IKT_WEAPONLOADED, pawn.IK_Type);
    }

    [Fact]
    public void AReloadPlaysTheRifleClipAndCountsTheRounds()
    {
        PawnRig rig = MercRig();
        SBasePawn pawn = rig.Pawn;
        pawn.ExecuteAction(SBasePawn.SA_RELOAD_3);
        PawnAnimChannel c = pawn.PawnAnim[SBasePawn.WEAPONCHANNEL];
        Assert.Equal(new Name("ReloStSpFd2"), c.Sequence);
        Assert.Equal(1f, c.BlendAlpha);
        Assert.Equal(3, pawn.iNbAmmoToReload);
        Assert.Equal(IK_Type_.IKT_NONE, pawn.IK_Type);
    }

    [Fact]
    public void AMercDeathPlaysTheDeathClipOfItsDirectionAndStance()
    {
        PawnRig rig = MercRig();
        SBasePawn pawn = rig.Pawn;
        pawn.ExecuteAction(SBasePawn.SA_DIE_LEFT);
        Assert.Equal(pawn.ADeathStLeft2H, pawn.PawnAnim[0].Sequence);
        Assert.True(pawn.bLyingOnGround);
        Assert.True(pawn.bSleeping);
        Assert.Equal(EPhysics.PHYS_Walking, pawn.Physics);
    }

    [Fact]
    public void ASpyDeathMovesByRootMotionThenLiesDown()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn pawn = rig.Pawn;
        pawn.ExecuteAction(SBasePawn.SA_DIE_FORWARD);
        Assert.Equal(pawn.ADeathStForward, pawn.PawnAnim[0].Sequence);
        Assert.Equal(EPhysics.PHYS_RootMotion, pawn.Physics);
        Assert.Equal(3, pawn.iLockRootMotionType);

        Run(rig, 6f);
        Assert.Equal(new Name("XxxxDnAlBk0"), pawn.PawnAnim[0].Sequence);
        Assert.Equal(EPhysics.PHYS_Walking, pawn.Physics);
    }

    [Fact]
    public void AHitAtRestPlaysTheBodyHitOnTheDamageChannelThenBlendsOut()
    {
        PawnRig rig = MercRig();
        SBasePawn pawn = rig.Pawn;
        pawn.WeaponLoaded = 2;
        pawn.ExecuteAction(SBasePawn.SA_DMG_CHEST_FRONT);
        PawnAnimChannel c = pawn.PawnAnim[SBasePawn.DAMAGECHANNEL];
        Assert.Equal(pawn.ADamageStChestForward2, c.Sequence);
        Assert.Equal(1f, c.BlendAlpha);
        Assert.Equal(IK_Type_.IKT_NONE, pawn.IK_Type);

        Run(rig, 4f);
        Assert.Equal(0f, pawn.PawnAnim[SBasePawn.DAMAGECHANNEL].BlendAlpha, 3);
    }

    [Fact]
    public void AHitWhileMovingOnlyFlinchesTheHead()
    {
        PawnRig rig = MercRig();
        SBasePawn pawn = rig.Pawn;
        pawn.Velocity = new FVector(0f, 200f, 0f);
        pawn.ExecuteAction(SBasePawn.SA_DMG_ARM_LEFT);
        PawnAnimChannel c = pawn.PawnAnim[SBasePawn.DAMAGECHANNEL];
        Assert.Equal(new Name("GoglStAlDn2"), c.Sequence);
        Assert.Equal(Head, c.StartBone);
        Assert.Equal(1f, c.BlendAlpha);
    }

    [Fact]
    public void TheElbowAttackIsDrawnFromSpine1()
    {
        PawnRig rig = MercRig();
        SBasePawn pawn = rig.Pawn;
        pawn.ExecuteAction(SBasePawn.SA_ELBOW_ATTACK);
        PawnAnimChannel c = pawn.PawnAnim[SBasePawn.ATTACKCHANNEL];
        Assert.Equal(1f, c.BlendAlpha);
        Assert.Equal(Spine1, c.StartBone);

        Run(rig, 3f);
        Assert.Equal(0f, pawn.PawnAnim[SBasePawn.ATTACKCHANNEL].BlendAlpha, 3);
    }

    [Fact]
    public void TheHandInteractionAndTheMineAreDrawnOnTheInteractChannel()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn pawn = rig.Pawn;
        pawn.ExecuteAction(SBasePawn.SA_HANDINTERACT);
        Assert.Equal(1f, pawn.PawnAnim[SBasePawn.INTERACTCHANNEL].BlendAlpha);
        Assert.Equal(Spine1, pawn.PawnAnim[SBasePawn.INTERACTCHANNEL].StartBone);

        pawn.ExecuteAction(SBasePawn.SA_DROPMINE);
        Assert.Equal(1f, pawn.PawnAnim[SBasePawn.INTERACTCHANNEL].BlendAlpha);
        Run(rig, 3f);
        Assert.Equal(0f, pawn.PawnAnim[SBasePawn.INTERACTCHANNEL].BlendAlpha, 3);
    }

    [Fact]
    public void TheCoughLoopsOnItsChannelFromTheHeadUntilStopped()
    {
        PawnRig rig = MercRig();
        SBasePawn pawn = rig.Pawn;
        pawn.ExecuteAction(SBasePawn.SA_COUGHT_START);
        PawnAnimChannel c = pawn.PawnAnim[SBasePawn.COUGHTCHANNEL];
        Assert.Equal(new Name("CaughStSpFd2"), c.Sequence);
        Assert.True(c.bLoop);
        Assert.Equal(1f, c.BlendAlpha);
        Assert.Equal(Head, c.StartBone);

        pawn.ExecuteAction(SBasePawn.SA_COUGHT_STOP);
        Run(rig, 1f);
        Assert.Equal(0f, pawn.PawnAnim[SBasePawn.COUGHTCHANNEL].BlendAlpha, 3);
    }

    [Fact]
    public void TheMercWakesFromItsFallPoseAndTheSpyFromItsBack()
    {
        PawnRig merc = MercRig();
        merc.Pawn.ExecuteAction(SBasePawn.SA_DIE_FORWARD);
        merc.Pawn.ExecuteAction(SBasePawn.SA_STANDUP);
        Assert.Equal(merc.Pawn.AWakeStForward2H, merc.Pawn.PawnAnim[0].Sequence);

        PawnRig spy = Rig(PawnTeam.Spy);
        spy.Pawn.ExecuteAction(SBasePawn.SA_STANDUP);
        Assert.Equal(spy.Pawn.AWakeStBack, spy.Pawn.PawnAnim[0].Sequence);
        Assert.Equal(EPhysics.PHYS_RootMotion, spy.Pawn.Physics);
    }

    [Fact]
    public void ThrowsAndBlastsPlayOnTheBaseChannel()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn pawn = rig.Pawn;
        pawn.ExecuteAction(SBasePawn.SA_THROW_ENTER);
        Assert.Equal(new Name("ThroStAlBgH"), pawn.PawnAnim[0].Sequence);
        pawn.ExecuteAction(SBasePawn.SA_BLAST_LOOP);
        Assert.Equal(new Name("BlstStAlDn2"), pawn.PawnAnim[0].Sequence);
        Assert.Equal(EPhysics.PHYS_Falling, pawn.Physics);
    }

    [Fact]
    public void EveryClipTheNewCasesNameIsInTheCharacterPackages()
    {
        string dir = Path.Combine(RepoRoot, ".content", "shared", "chars");
        if (!File.Exists(Path.Combine(dir, "SPerso", "PRO.anim.json")))
        {
            Assert.Skip("no converted characters under .content/shared/chars");
        }

        PawnAnimationSource source = PawnAnimationSource.FromContent(dir);
        PawnRig spy = Rig(PawnTeam.Spy);
        PawnRig merc = MercRig();
        foreach ((SBasePawn pawn, string[] names) in new[]
        {
            (spy.Pawn, new[] { "XxxxStAlFd0", "XxxxStAlBk0", "XxxxDnAlBk0", "WakeStSpBk0", "ThroStAlBgH", "ThroStAlMix", "BlstStAlDn2", "GrabStAlXxb", "AtkdDnMmBg0", "BackStSpBgL", "PainStAlCB0" }),
            (merc.Pawn, new[] { "ReloStSpFd2", "ReloStSpFd3", "ReloStSpFd1", "XxxxStAlFd0", "WakeStSpFd2", "GrbdStAlKo0", "AtakDnMmBg2", "PainStAlCB2", "GoglStAlDn2", "CaughStSpFd2" }),
        })
        {
            foreach (string n in names)
            {
                Assert.True(source.TryGetClip(pawn, new Name(n), out _), $"{pawn.GetType().Name} has no {n}");
            }
        }
    }
}
