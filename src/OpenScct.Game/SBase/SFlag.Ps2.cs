using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Physics.Movement;

namespace OpenScct.Game.SBase;

public partial class SFlag
{
    /// <summary>The ISO-authored vial, carried explicitly across a dynamic spawn's PC class defaults.</summary>
    public AssetRef Ps2SourceStaticMesh;

    private void Ps2BindSourceMesh()
    {
        if (!bPS2Objective || Ps2SourceStaticMesh.IsNone) return;
        InitialStaticMesh = Ps2SourceStaticMesh;
        if (StaticMesh != Ps2SourceStaticMesh) SetStaticMesh(Ps2SourceStaticMesh);
    }

    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        Ps2BindSourceMesh();
    }

    protected override void NativeTick(float deltaSeconds)
    {
        base.NativeTick(deltaSeconds);
        // The PS2 opt-in and asset can arrive after the global PC factory has
        // captured its rack as InitialStaticMesh. Source SFlag never swaps its vial.
        Ps2BindSourceMesh();
    }

    /// <summary>Source SFlag property 853799: home pickup offset along the console's forward axis.</summary>
    public float Ps2InteractionDistance = 80f;
    public static readonly Name Ps2CarryBone = new("B R Thigh");
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
        Ps2BindSourceMesh();
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
