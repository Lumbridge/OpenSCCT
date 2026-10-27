using System;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>Scenes for the gadget tests: walls, a drawn weapon and a selected gadget.</summary>
internal static class GadgetKit
{
    /// <summary>A listen server scene (gadget charges keep their pack sizes; offline games raise them to 999).</summary>
    public static CombatScene Scene() => new(ENetMode.NM_ListenServer);

    /// <summary>A player on this machine (as a login makes the local player: remote role simulated proxy).</summary>
    public static (SPlayerController PC, SBasePawn Pawn) Local(CombatScene Scene, bool Spy, FVector At, int Yaw = 0)
    {
        (SPlayerController pc, SBasePawn pawn) = Scene.Player(Spy, At, Yaw);
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        return (pc, pawn);
    }

    /// <summary>Installs the boxes as the level's collision; a box with flags gets its own material.</summary>
    public static CollisionWorld Walls(CombatScene Scene, params (FVector Min, FVector Max, CollisionMaterialFlags Flags)[] Boxes)
    {
        var b = new CollisionDataBuilder();
        foreach ((FVector min, FVector max, CollisionMaterialFlags flags) in Boxes)
        {
            ushort material = flags == CollisionMaterialFlags.None ? (ushort)0 : b.AddMaterial(0, flags);
            b.AddBox(min, max, material);
        }

        CollisionWorld world = CollisionWorld.Build(b.Build());
        Scene.Level.SetService<ICollisionWorld>(world);
        return world;
    }

    /// <summary>A wall facing -X whose face is at <paramref name="X"/>.</summary>
    public static (FVector, FVector, CollisionMaterialFlags) WallAt(float X, CollisionMaterialFlags Flags = CollisionMaterialFlags.None) =>
        (new FVector(X, -1000f, -1000f), new FVector(X + 40f, 1000f, 1000f), Flags);

    /// <summary>Gives <paramref name="Gadget"/> to <paramref name="Pawn"/> and selects it.</summary>
    public static T Gadget<T>(CombatScene Scene, SBasePawn Pawn, T Gadget, bool Select = true)
        where T : SGadget
    {
        T g = Scene.Give(Gadget, Pawn);
        g.GiveTo(Pawn);
        if (Select)
        {
            Pawn.SelectedGadget = g;
        }

        return g;
    }

    /// <summary>The spy's Shocker, drawn.</summary>
    public static SStickyShocker Shocker(CombatScene Scene, SBasePawn Spy, bool Drawn = true)
    {
        SStickyShocker shocker = Scene.Give(new SStickyShocker(), Spy);
        shocker.GiveTo(Spy);
        Spy.WeaponLoaded = (byte)(Drawn ? 2 : 0);
        return shocker;
    }

    /// <summary>The live actors of class <typeparamref name="T"/> in the scene, in actor order.</summary>
    public static System.Collections.Generic.List<T> All<T>(CombatScene Scene)
        where T : Actor
    {
        var list = new System.Collections.Generic.List<T>();
        foreach (T a in Scene.Level.AllActors<T>())
        {
            if (!a.bDeleteMe)
            {
                list.Add(a);
            }
        }

        return list;
    }

    /// <summary>A pawn moving at <paramref name="Velocity"/> (set every tick by the caller when needed).</summary>
    public static void Move(Pawn P, FVector Velocity) => P.Velocity = Velocity;

    /// <summary>Runs whole ticks until <paramref name="Until"/> holds or <paramref name="Seconds"/> pass; true when it held.</summary>
    public static bool RunUntil(CombatScene Scene, float Seconds, Func<bool> Until)
    {
        int frames = (int)(Seconds * Sim.Hz + 0.5f);
        for (int i = 0; i < frames; i++)
        {
            if (Until())
            {
                return true;
            }

            Scene.World.Step();
        }

        return Until();
    }
}
