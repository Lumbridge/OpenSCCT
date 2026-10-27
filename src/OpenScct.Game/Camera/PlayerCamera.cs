using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Camera;

/// <summary>
/// The controller's camera actor (SPlayerController.PostBeginPlay and SpawnCamCtrl, camera.md rule 8.1): one
/// SPlayerCam owned by the controller on each machine that draws it. Spawned the first time the local player needs
/// it, not in PostBeginPlay, because a client's controller arrives by replication before it is the local player.
/// </summary>
public static class PlayerCamera
{
    /// <summary>
    /// The camera of <paramref name="PC"/>, spawned for a player this machine draws (a local viewport, never on a
    /// dedicated server); null for other controllers that have none.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:SpawnCamCtrl", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    [Provenance(ProvenanceKind.Estimated, "Spawned on first use for the local viewport player instead of in PostBeginPlay on every machine")]
    public static SPlayerCam? Of(SPlayerController PC)
    {
        if (PC.m_camera is { bDeleteMe: false } camera)
        {
            return camera;
        }

        if (PC.bDeleteMe || PC.Player is not UViewport || PC.Level.NetMode == ENetMode.NM_DedicatedServer)
        {
            return null;
        }

        SPlayerCam? spawned = PC.XLevel.SpawnActor(new SPlayerCam(), PC.Location, PC.Rotation, PC);
        if (spawned is not null)
        {
            spawned.m_EPC = PC;
            PC.m_camera = spawned;
        }

        return spawned;
    }
}
