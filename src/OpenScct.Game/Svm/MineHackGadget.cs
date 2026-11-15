using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Svm;

public sealed class MineHackGadget : SpyDeployableGadget
{
    public const string ClassPath = "Svm.MineHackGadget";
    public const int HacksPerLife = 2;
    public const float HoldSeconds = 3f;
    private const float Reach = 120f;
    private const float AimCos = 0.92f;

    public static readonly UcClassInfo ClassInfo = GadgetClass<MineHackGadget>(ClassPath);
    private SMines? _target;
    private float _finishesAt;
    private SMines? _heldMine;

    public byte HackPercent;
    public byte HeldHackPercent => _heldMine is { CanBeHacked: true } ? HackPercent : (byte)0;

    public override UcClassInfo GetUcClass() => ClassInfo;

    protected override void SetDefaults()
    {
        base.SetDefaults();
        ItemName = ItemLongName = "Mine Hack";
        ItemDescription = "Aim at a Merc mine and hold Use Gadget for three seconds to turn it against Mercs. Two hacks per life.";
        MagmaIconName = "D_Mine";
        IconU = 64;
        IconV = 128;
        Charge = HacksPerLife;
    }

    public override void GiveTo(Pawn other)
    {
        if (other is SPawnAttaque spy)
            Charge = HacksPerLife - spy.MineHacksUsed;
        base.GiveTo(other);
    }

    public override void Use(float value)
    {
        if (_heldMine is not null || OwnerPawn is not SPawnAttaque spy || spy.Controller is not SPlayerController pc) return;
        if (!HasCharge)
        {
            ShowMessage(pc, "No mine hacks left.");
            return;
        }

        SMines? mine = FindTarget(spy, pc.Rotation.Vector());
        if (mine is null)
        {
            ShowMessage(pc, "Aim at a Merc mine within reach.");
            return;
        }

        _heldMine = mine;
        if (!GadgetNet.ToServer(this, pc, new CombatCall(new Name("StartMineHack")) { Target = this, Actor1 = mine }))
            BeginHack(pc, mine);
    }

    public override void Unload() => Release();

    protected override void Tick(float deltaTime)
    {
        base.Tick(deltaTime);
        if (_heldMine is { } held && OwnerPawn is SPawnAttaque holder &&
            holder.Controller is SPlayerController { IsRemotePlayer: false } local &&
            (local.bAltFire == 0 || !ReferenceEquals(holder.SelectedGadget, this) ||
             AimAt(holder, held, local.Rotation.Vector()) <= AimCos))
            Release();

        if (Role != ENetRole.ROLE_Authority || _target is not { } mine) return;
        if (OwnerPawn is not SPawnAttaque spy || spy.Controller is not SPlayerController pc ||
            !CanDeploy(pc) || !CanReach(spy, mine) || AimAt(spy, mine, pc.Rotation.Vector()) <= AimCos)
        {
            CancelHack();
            return;
        }

        float remaining = _finishesAt - Level.TimeSeconds;
        if (remaining > 0f)
        {
            HackPercent = (byte)Math.Clamp((int)(100f * (1f - remaining / HoldSeconds)), 1, 99);
            return;
        }

        CancelHack();
        bool practice = PracticeRules.ResourcesInfinite(this);
        if ((!practice && spy.MineHacksUsed >= HacksPerLife) || !mine.TryHack(pc)) return;

        if (!practice) Charge = HacksPerLife - ++spy.MineHacksUsed;
        LastUsed = Level.TimeSeconds;
        ShowMessage(pc, "Mine hacked.");
    }

    internal void BeginHack(SPlayerController pc, SMines mine)
    {
        if (!CanDeploy(pc) || OwnerPawn is not SPawnAttaque spy ||
            (spy.MineHacksUsed >= HacksPerLife && !PracticeRules.ResourcesInfinite(this)) ||
            _target is not null || !CanReach(spy, mine)) return;

        _target = mine;
        _finishesAt = Level.TimeSeconds + HoldSeconds;
        HackPercent = 1;
    }

    internal void CancelHack()
    {
        _target = null;
        HackPercent = 0;
    }

    private void Release()
    {
        if (_heldMine is null) return;
        _heldMine = null;
        if (!GadgetNet.ToServer(this, OwnerPawn?.Controller as SPlayerController,
                new CombatCall(new Name("CancelMineHack")) { Target = this }))
            CancelHack();
    }

    private static SMines? FindTarget(SPawnAttaque spy, FVector aim)
    {
        SMines? target = null;
        float best = AimCos;
        foreach (SMines mine in spy.XLevel.AllActors<SMines>())
        {
            float alignment = AimAt(spy, mine, aim);
            if (alignment <= best || !CanReach(spy, mine)) continue;
            best = alignment;
            target = mine;
        }
        return target;
    }

    private static float AimAt(SPawnAttaque spy, SMines mine, FVector aim) =>
        (mine.Location - Eye(spy)).SafeNormal() | aim;

    private static bool CanReach(SPawnAttaque spy, SMines mine) =>
        mine.CanBeHacked && FVector.DistSquared(spy.Location, mine.Location) <= Reach * Reach &&
        GadgetWorld.Sees(spy, Eye(spy), mine);

    private static FVector Eye(SPawnAttaque spy) => spy.Location + new FVector(0, 0, spy.EyeHeight) + spy.WalkBob;
}
