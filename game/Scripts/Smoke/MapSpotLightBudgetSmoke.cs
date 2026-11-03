using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Smoke;

/// <summary>Actual camera/frustum and map projector allocation with more lights than atlas slots.</summary>
public sealed class MapSpotLightBudgetSmoke : SmokeTest
{
    public const string TestName = "map-spot-light-budget";
    public override string Name => TestName;
    private SubViewport? _viewport;
    private Camera3D? _camera;
    private RenderMap? _map;
    private MapSpotLights? _lights;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_viewport is null)
        {
            _viewport = new SubViewport { Size = new Vector2I(640, 360), OwnWorld3D = true };
            context.Tree.Root.AddChild(_viewport);
            _camera = new Camera3D
            {
                Transform = FaithfulView.CameraTransform(default, default), Current = true, Far = 100f,
            };
            _viewport.AddChild(_camera);
            var root = new Node3D();
            _viewport.AddChild(root);
            var lighting = new LightingDoc();
            for (int i = 0; i < RenderK.SpotShadowMapLights; i++)
                lighting.Lights.Add(Light($"Behind{i}", new FVector(-300 - i * 50, 0, 0), new FRotator(0, 32768, 0)));
            lighting.Lights.Add(Light("VisibleWindow", new FVector(1500, 0, 0), default));
            var materials = new MaterialsDoc();
            var textures = new TextureCache("");
            _map = new RenderMap("", "SpotBudgetFixture", root, materials, lighting, textures,
                new MaterialCompiler(materials, textures), new ActorLighting(lighting), null);
            _lights = new MapSpotLights(_map, _viewport);
            return SmokeResult.Running;
        }

        _lights!.Update(default);
        if (_lights.DrawnCount != RenderK.SpotShadowMapLights || SpotShadowLight.SlotOf("VisibleWindow") < 0)
            return Finish(false, "six nearer offscreen projectors displaced the visible window patch");

        _camera!.Transform = FaithfulView.CameraTransform(default, new FRotator(0, 32768, 0));
        _lights.Update(default);
        if (SpotShadowLight.SlotOf("VisibleWindow") >= 0)
            return Finish(false, "offscreen light retained a slot needed by visible lights after turning");
        for (int i = 0; i < RenderK.SpotShadowMapLights; i++)
            if (SpotShadowLight.SlotOf($"Behind{i}") < 0)
                return Finish(false, "a visible projector was not allocated after turning");

        foreach (LightEntry light in _map!.Lighting.Lights) _map.SetLightOn(light.Name, false);
        _lights.Update(default);
        return Finish(_lights.DrawnCount == 0, "visible-first allocation, camera turn and immediate light switches");
    }

    private SmokeResult Finish(bool passed, string detail)
    {
        _lights!.Dispose();
        _viewport!.QueueFree();
        return passed ? SmokeResult.Pass(detail) : SmokeResult.Fail(detail);
    }

    private static LightEntry Light(string name, FVector location, FRotator rotation) =>
        new(name, location, rotation, 255f, 0, 255, 1f, 128, RenderK.LE_ESpotShadow, 1,
            true, false, false, true, null, 32f, 0f, false)
        { Echelon = new EchelonSpot(200f, 50f, 50f, 0.1f, 0f, null, 1) };
}
