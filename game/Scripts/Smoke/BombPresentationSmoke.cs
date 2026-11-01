using System;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Fx;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Bomb meshes follow client state through planting, removal, explosion and a new round.</summary>
public sealed class BombPresentationSmoke : SmokeTest
{
    public const string TestName = "bomb-presentation";
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        string shared = ContentPaths.Shared ?? SmokePaths.Tracked(".content/shared");
        RenderPack? pack = RenderPack.Load(shared);
        if (pack is null || GameplaySkins.For(pack, GameplaySkins.BombGhost, 0) is null)
            return SmokeResult.Fail("bomb ghost textures are required");
        Mesh? mesh = FxLibrary.Load(shared).Mesh("SGameplayObjects.SGPO_SM.Mode_bombing_spy");
        if (mesh is null) return SmokeResult.Fail("bomb mesh is required");

        var world = new World(1, ENetMode.NM_Client);
        world.Level.BeginLoad();
        var bomb = world.Level.SpawnActor(new SBombTargetObjectiveTrigger
        {
            Role = ENetRole.ROLE_SimulatedProxy,
        }, default, default)!;
        world.Level.EndLoad();
        var node = new MeshInstance3D { Mesh = mesh };
        context.Tree.Root.AddChild(node);
        try
        {
            var follower = new MeshFollower(bomb, new GameplayMesh
            {
                ActorId = "Bomb", Class = nameof(SBombTargetObjectiveTrigger), Node = node,
                Location = default, Light = LightSource.Actor,
            }, mapHidden: false);

            void Draw()
            {
                world.Step();
                follower.OnTick();
                follower.DrawSkin(pack, world.TimeSeconds);
                follower.Draw(1f);
            }

            bomb.bHidden = false;
            Draw();
            Require(node.Visible && node.MaterialOverride is not null, "unarmed spot has no ghost");

            for (int cycle = 0; cycle < 2; cycle++)
            {
                bomb.bSkinState = true;
                Draw();
                Require(node.Visible && node.MaterialOverride is null, "planted bomb retained its ghost");
                for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                    Require(node.GetSurfaceOverrideMaterial(surface) is null, "planted bomb retained a ghost surface");

                bomb.bSkinState = false;
                Draw();
                Require(node.Visible && node.MaterialOverride is not null, "removed bomb did not restore its ghost");

                bomb.bSkinState = true;
                Draw();
                bomb.bSkinState = false;
                bomb.bHidden = true;
                Draw();
                Require(!node.Visible, "exploded bomb retained a visible ghost");
                Draw();
                Require(!node.Visible, "exploded bomb reappeared on the following frame");

                bomb.bHidden = false;
                Draw();
                Require(node.Visible && node.MaterialOverride is not null, "new round did not restore its ghost");
            }

            return SmokeResult.Pass("client bomb mesh: ghost, solid planted charge, restored ghost on removal, hidden after explosion, reset for two rounds");
        }
        catch (Exception error) { return SmokeResult.Fail(error.ToString()); }
        finally { node.Free(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
