using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SProjoFixe: a fixed searchlight a merc mans (SProjoInteraction, "Use Mirador") and turns within its pitch
/// and yaw limits; while it is in use (or always, bPersistentLight) it carries an SProjoLight spot (an ESpotShadow
/// light) at CameraOffset that follows its rotation, and swaps Skins[LightOnSkinIndex] to LightOnSkin. Written from
/// spec/devices/projector.md. Runs on every machine; bInUse replicates from the interaction's bInUse.
/// </summary>
public partial class SProjoFixe
{
    private static readonly ClassRef ProjoLightClass = new("SBase.SProjoLight");

    /// <summary>
    /// Every machine: the authority copies the interaction's use; a persistent light is always in use. Going in use
    /// spawns the spot with the projector's light values and lights the lens skin; going out of use removes it and
    /// puts the lens back. In use, a viewer not steering it adopts the replicated angles, and the projector and its
    /// spot follow the angles (the spot at CameraOffset in the projector's axes).
    /// </summary>
    [Uc("SBase.SProjoFixe.Tick", Sha1 = "ff0a393b779f26a34978bed7eafb1e45433b0cfc")]
    [PortOf("uc:SBase.SProjoFixe:Tick", Spec = "spec/devices/projector.md#rules")]
    protected override void Tick(float DeltaTime)
    {
        if (Role == ENetRole.ROLE_Authority)
        {
            bInUse = InteractionObj?.bInUse ?? false;
        }

        if (bPersistentLight)
        {
            bInUse = true;
        }

        if (bInUse && SLight is null)
        {
            SpawnSpot();
            bInUse = true;
        }

        if (!bInUse && SLight is not null)
        {
            bInUse = false;
            SLight.Destroy();
            SLight = null;
            if (!LightOnSkin.IsNone)
            {
                SetSkinSlot(LightOnSkinIndex, LightOffSkin);
            }
        }

        if (!bInUse)
        {
            return;
        }

        if ((iPitch != iReplicatedPitch || iYaw != iReplicatedYaw) &&
            Level.GetLocalPlayerController() is SPlayerController local && !ReferenceEquals(local.SBPawn?.UsedObject, this))
        {
            iPitch = iReplicatedPitch;
            iYaw = iReplicatedYaw;
        }

        Rotation = new FRotator(iPitch, iYaw, Rotation.Roll);
        Rotation.GetAxes(out FVector X, out FVector Y, out FVector Z);
        if (SLight is not null)
        {
            SLight.SetLocation(Location + (X * CameraOffset.X) + (Y * CameraOffset.Y) + (Z * CameraOffset.Z));
            SLight.Rotation = Rotation;
        }
    }

    private void SpawnSpot()
    {
        SLight = DeviceClasses.Spawn(this, ProjoLightClass, this, Name.None, Location, Rotation) as SProjoLight;
        if (SLight is not null)
        {
            IgnoredEchelonLights.Add(SLight);
            SLight.LightBrightness = LightBrightness;
            SLight.LightHue = LightHue;
            SLight.LightSaturation = LightSaturation;
            SLight.SpotHeight = SpotHeight;
            SLight.SpotWidth = SpotWidth;
            SLight.LightRadius = LightRadius;
            SLight.LightPeriod = LightPeriod;
            SLight.LightCone = LightCone;
            SLight.EchelonRange = EchelonRange;
        }

        if (!LightOnSkin.IsNone)
        {
            if (LightOnSkinIndex < Skins.Count && Skins[LightOnSkinIndex] != LightOnSkin)
            {
                LightOffSkin = Skins[LightOnSkinIndex];
            }

            SetSkinSlot(LightOnSkinIndex, LightOnSkin);
        }
    }

    /// <summary>UnrealScript dynamic-array assignment past the end grows the array.</summary>
    private void SetSkinSlot(int index, AssetRef skin)
    {
        if (index < 0)
        {
            return;
        }

        while (Skins.Count <= index)
        {
            Skins.Add(AssetRef.None);
        }

        Skins[index] = skin;
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        SLight = Live(SLight);
    }
}
