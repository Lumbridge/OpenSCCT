using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Game.Lighting;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Smoke;

/// <summary>Real world-light shader, pulse modulation, pane outlines and break/reset on the GPU.</summary>
public sealed class WindowDynamicLightSmoke : SmokeTest
{
    public const string TestName = "window-dynamic-light";
    public override string Name => TestName;
    private SubViewport? viewport;
    private WorldLights lights = null!;
    private WindowLightFilter filter = null!;
    private WindowLightFilter.Pane pane = null!;
    private int phase, frames;
    private Color clear, red;
    private float high, low;

    public override SmokeResult Step(SmokeContext context)
    {
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires a renderer");
        if (viewport is null)
        {
            var doc = new LightingDoc();
            doc.Lights.Add(new LightEntry("pulse", default, default, 32, 0, 255, 32, 0, 0, 2, true, false, true, true, null, 32, 0, false));
            lights = new WorldLights(doc);
            var compiler = new MaterialCompiler(new MaterialsDoc(), new TextureCache(""));
            var material = (ShaderMaterial)compiler.Build(new MaterialEntry { Name = "receiver", Lightmap = "" }, LightSource.Lightmap);
            using var black = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8); black.Fill(Colors.Black);
            material.SetShaderParameter("tex_lightmap", ImageTexture.CreateFromImage(black));
            viewport = new SubViewport { Size = new(256, 256), OwnWorld3D = true, UseDebanding = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            context.Tree.Root.AddChild(viewport);
            viewport.AddChild(new Camera3D { Current = true, CullMask = RenderLayers.MainView, Projection = Camera3D.ProjectionType.Orthogonal, Size = 4,
                Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes() });
            using var arrays = new QuadMesh { Size = new(4, 4) }.GetMeshArrays();
            arrays[(int)Mesh.ArrayType.Color] = new Color[] { new(1,1,1,0),new(1,1,1,0),new(1,1,1,0),new(1,1,1,0) };
            var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            viewport.AddChild(new MeshInstance3D { Position = new(0, 0, -3), Mesh = mesh, MaterialOverride = material });
            filter = new WindowLightFilter();
            pane = new("pane", new(.72f, .036f, .036f),
                [new(-.5f,-.5f,-1.5f),new(.5f,-.5f,-1.5f),new(.5f,.5f,-1.5f),
                 new(-.5f,-.5f,-1.5f),new(.5f,.5f,-1.5f),new(-.5f,.5f,-1.5f)]);
            high = low = 0;
            for (int i = 1; i < 1000; i++)
            {
                float time = i / 1000f;
                if (Pulse(time) > Pulse(high)) high = time;
                if (Pulse(time) < Pulse(low)) low = time;
            }
            Configure();
        }
        if (++frames < 8) return SmokeResult.Running;
        frames = 0;
        using var image = viewport.GetTexture().GetImage();
        Color center = image.GetPixel(128, 128), outside = image.GetPixel(16, 128);
        string folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") ?? Path.Combine("local", TestName);
        Directory.CreateDirectory(folder); image.SavePng(Path.Combine(folder, $"phase-{phase}.png"));
        GD.Print($"SVM_WINDOW_DYNAMIC phase={phase} center={center} outside={outside}");
        string? error = phase switch
        {
            0 when center.R < .05f => "reference world light is dark",
            1 when center.R <= center.G * 4 || outside.R > outside.G * 1.1f || center.R >= clear.R => "live light did not follow the pane outline/tint",
            2 when center.R >= red.R * .8f || center.R <= center.G * 4 => "transmitted light did not pulse with its source",
            3 when center.R > .01f || center.G > .01f => "switched-off light still casts through glass",
            4 when MathF.Abs(center.R - center.G) > .02f || center.R < red.R => "broken glass kept filtering the live light",
            5 when center.R <= center.G * 4 || MathF.Abs(center.R - red.R) > .02f => "reset glass did not restore live tint",
            6 when center.G >= red.G || center.R >= red.R => "stacked optical sheets did not multiply their transmission",
            7 when center.R > center.G * 1.1f => "a pane beyond the source tinted its light",
            _ => null,
        };
        if (error is not null) return Finish(SmokeResult.Fail(error));
        if (phase == 0) clear = center;
        if (phase == 1) red = center;
        if (++phase == 8) return Finish(SmokeResult.Pass("pulsing/switching live light, outlined/stacked panes and break/reset"));
        Configure();
        return SmokeResult.Running;
    }

    private static float Pulse(float time) => ActorLightRules.Modulation(2, 32, 0, time);
    private void Configure()
    {
        lights.SetOn("pulse", phase != 3);
        lights.Update(phase == 2 ? low : high);
        filter.SetPanes(phase == 0 ? Array.Empty<WindowLightFilter.Pane>() : phase == 6 ? [pane, pane with { Id = "second" }] :
            phase == 7 ? [pane with { Triangles = Array.ConvertAll(pane.Triangles, p => p + new Vector3(0, 0, 2)) }] : [pane]);
        if (phase == 4) filter.SetBroken(new HashSet<string> { "pane" });
    }
    private SmokeResult Finish(SmokeResult result) { viewport?.QueueFree(); return result; }
}
