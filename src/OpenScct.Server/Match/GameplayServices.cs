using System;
using OpenScct.Core;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Game.GameFlow;
using OpenScct.Game.Movement;
using OpenScct.Physics;

namespace OpenScct.Server;

/// <summary>
/// The gameplay services a networked level runs with, on the server and on every client: the device natives over the
/// map's physics, the movement layer with the pawn animation sets, the movement and equipment parts of the game-flow
/// ports, and the combat and equipment hooks. The network bridges are installed separately by the host
/// (<see cref="ServerHost"/>, <see cref="ClientSession"/>). Both sides must use the same animation source, or the
/// pawns' timed actions (and so their moves) differ between machines.
/// </summary>
public static class GameplayServices
{
    /// <summary>Installs the services on <paramref name="level"/> (after the map loaded and its physics attached).</summary>
    public static void Install(Level level, PhysicsWorld? physics, IPawnAnimationSource? animations)
    {
        ArgumentNullException.ThrowIfNull(level);
        DeviceNatives.Install(level, physics?.Driver);
        level.SetService(new MovementService(animations ?? PawnAnimationSource.Default));
        level.SetService<IGameFlowPorts>(new MovementGameFlowPorts(level.GetService<IGameFlowPorts>()));
        CombatEquipment.Install(level);
    }
}
