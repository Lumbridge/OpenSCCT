using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.Objectives;

/// <summary>The original surface placement checks (PS2 natives 1315/1316, 0x43FAA0/0x43FE90).</summary>
public static class Ps2ModemRules
{
    public readonly record struct Placement(SBombTargetObjectiveTrigger Target, FVector Location, FRotator Rotation);

    public static bool Probe(SGadBomb gadget, out Placement placement)
    {
        placement = default;
        if (gadget.OwnerPawn is not { Health: > 0 } pawn || pawn.Physics != EPhysics.PHYS_Walking ||
            pawn.sAction is >= SBasePawn.SA_ROLLING and <= SBasePawn.SA_ROLL_LAND)
            return Fail(gadget, 6);
        if (gadget.Charge < 1 || gadget.Level.TimeSeconds - gadget.LastUsed < gadget.RefireRate)
            return Fail(gadget, 0);

        // The original uses the owner's view axes. OpenSCCT keeps the camera
        // pitch on the controller, whereas the upright pawn keeps its body yaw.
        FRotator view = pawn.Controller?.Rotation ?? pawn.Rotation;
        view.GetAxes(out FVector x, out _, out FVector z);
        FVector start = pawn.Location + z * (pawn.EyeHeight * .5f);
        if (!GadgetWorld.Line(pawn, start + x * 100f, start, ETraceFlags.TRACE_AllColliding, out FCheckResult hit) ||
            !GadgetWorld.IsWorldGeometry(hit.Actor)) return Fail(gadget, 1);

        foreach (SBombTargetObjectiveTrigger target in gadget.XLevel.AllActors<SBombTargetObjectiveTrigger>())
        {
            if (!target.bPS2Objective || !target.Ps2Contains(hit.Location) ||
                (!target.bActive && target.Objective is not { ObjectiveTaken: true })) continue;
            gadget.BombTrigger = target;
            if (target.bLocked) return Fail(gadget, 4);
            if (target.Objective is { ObjectiveTaken: true }) return Fail(gadget, 5);
            foreach (Ps2Modem modem in gadget.XLevel.AllActors<Ps2Modem>())
                if (!modem.bDeleteMe && ReferenceEquals(modem.Target, target)) return Fail(gadget, 4);
            if (target.Ps2Computer is not { bDeleteMe: false } console ||
                (GadgetWorld.Line(pawn, console.Location, hit.Location, (ETraceFlags)0x496, out FCheckResult sight) &&
                 !ReferenceEquals(sight.Actor, console))) return Fail(gadget, 3);
            placement = new Placement(target, hit.Location, hit.Normal.Rotation());
            return true;
        }
        return Fail(gadget, 2);
    }

    public static bool TryPlace(SGadBomb gadget)
    {
        if (gadget.Role != ENetRole.ROLE_Authority || !Probe(gadget, out Placement p)) return false;
        var modem = new Ps2Modem
        {
            Target = p.Target, StaticMesh = p.Target.Ps2ModemMesh,
            ActiveSound = p.Target.Ps2ModemSound,
            DestroySound = p.Target.Ps2ModemDestroyedSound,
            DestroyedEmitter = p.Target.Ps2ModemDestroyedEmitter,
            SmokeTexture = p.Target.Ps2ModemSmokeTexture, FireTexture = p.Target.Ps2ModemFireTexture,
        };
        if (gadget.XLevel.SpawnActor(modem, p.Location, p.Rotation, gadget.OwnerPawn, gadget.OwnerPawn) is null) return false;
        gadget.LastUsed = gadget.Level.TimeSeconds;
        gadget.Charge--;
        return true;
    }

    private static bool Fail(SGadBomb gadget, int reason) { gadget.WhyCantUse = reason; return false; }
}
