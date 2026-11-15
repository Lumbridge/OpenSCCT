using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Physics.Movement;

namespace OpenScct.Game.SBase;

public partial class SFlag
{
    /// <summary>Source SFlag property 853799: home pickup offset along the console's forward axis.</summary>
    public float Ps2InteractionDistance = 80f;
    public static readonly FVector Ps2CarryOffset = new(-10f, 0f, 10f);
    public static readonly FRotator Ps2CarryRotation = new(16000, 0, 0);
    // Source COM_ONL: SFlag.PostBeginPlay 0x1384C8, Reset 0x138322,
    // Tick 0x137AD5; the PS2 scripts keep the authored vial mesh throughout.
    private void Ps2PlaceAtConsole()
    {
        if (Ps2Computer is not { bDeleteMe: false } console) return;
        SetLocation(console.Location + new FVector(0f, 0f, 25f));
        ObjectiveNatives.SetRotation(this, console.Rotation);
    }

    private void Ps2ResetFlag()
    {
        bPickable = true;
        if (PickupInteraction is { } pickup) pickup.bCanPickup = true;
        SetLocation(InitialLocation);
        ObjectiveNatives.SetRotation(this, InitialRotation);
        DesiredRotation = InitialRotation;
        bHidden = false;
        Ps2PlaceAtConsole();
        Ps2Computer?.Ps2UseAsProxy();
        Ps2Computer?.Ps2SetVisualState(5, 4f);
        SetCollision(true, false, false);
        if (PickupInteraction is { } interaction)
        {
            interaction.SetBase(null);
            Rotation.GetAxes(out FVector x, out _, out _);
            FVector at = Location + x * Ps2InteractionDistance;
            at.Z -= 25f;
            interaction.SetLocation(at);
            ObjectiveNatives.SetRotation(interaction, Rotation);
            interaction.SetBase(this);
        }

        if (bShouldLock)
        {
            Ps2Computer?.Ps2SetVisualState(6, 4f);
            bPickable = false;
            if (PickupInteraction is { } locked) locked.bCanPickup = false;
            GotoState(K.s_TemporaryInactive);
        }
        else GotoState(K.s_Initial);
    }

    protected override void Tick(float deltaSeconds)
    {
        base.Tick(deltaSeconds);
        if (!bPS2Objective) return;
        float target = FlagCarrier is { } carrier ? MathF.Min(255f, carrier.Velocity.Size()) : bActive ? 100f : 0f;
        LightBrightness = FlagCarrier is null ? target : LightBrightness + Math.Clamp(target - LightBrightness, -255f * deltaSeconds, 255f * deltaSeconds);
        if (Role == ENetRole.ROLE_Authority && Ps2Computer is { bDeleteMe: false } console &&
            PickupInteraction is { bCanPickup: true } && Location.Z < console.Location.Z + 25f)
            Ps2MoveVertically(15f * deltaSeconds);
    }

    private void Ps2MoveVertically(float delta)
    {
        FVector move = new(0f, 0f, delta);
        if (XLevel.PhysicsDriver is PhysicsDriver physics) physics.MoveActor(this, move);
        else SetLocation(Location + move);
    }
}
