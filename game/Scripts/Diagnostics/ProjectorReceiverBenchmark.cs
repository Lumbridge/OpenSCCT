using System;
using Godot;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Projectors;

namespace OpenScctGame.Diagnostics;

/// <summary>Opt-in real skinned player and moving rigid geometry, repeated at identical poses in each benchmark mode.</summary>
internal sealed class ProjectorReceiverBenchmark
{
    private readonly Node3D _player = new() { Name = "ProjectorBenchmarkPlayer" };
    private readonly Node3D _objects = new() { Name = "ProjectorBenchmarkObjects" };
    private readonly SkeletalModel _spy;
    private readonly Vector3 _origin, _forward, _side;
    private readonly string _scenario;

    public ProjectorReceiverBenchmark(Node parent, string scenario)
    {
        if (scenario is not ("player" or "objects" or "cloak" or "mixed")) throw new ArgumentException("Unknown projector receiver scenario " + scenario);
        _scenario = scenario;
        ProjectorEntry source = SurfaceProjector.Source ?? throw new InvalidOperationException("Receiver benchmark requires an eligible G16 projector");
        _origin = UeSpace.ToGodot(source.Location);
        source.Rotation.GetAxes(out var x, out var y, out _);
        _forward = UeSpace.DirectionToGodot(x); _side = UeSpace.DirectionToGodot(y);
        parent.AddChild(_player); parent.AddChild(_objects);
        string path = SkeletalModel.FindGlb("SPerso.ATT_01") ?? throw new InvalidOperationException("Converted spy model unavailable");
        _spy = SkeletalModel.Load(path); _player.AddChild(_spy.Root);
        _spy.Place(default, default, default); _spy.Sampler.Play("WaitStAlFd0"); _spy.Pose();
        Aabb? box = null;
        foreach (var mesh in _spy.Meshes) box = box is { } all ? all.Merge(mesh.GlobalTransform * mesh.GetAabb()) : mesh.GlobalTransform * mesh.GetAabb();
        Aabb bounds = box ?? throw new InvalidOperationException("Spy model has no geometry");
        float scale = .95f / bounds.Size.Y;
        _spy.Root.Scale = Vector3.One * scale;
        _spy.Root.Position = -bounds.GetCenter() * scale;
        _spy.Light(new ActorLightSet { Ambient = new Vector3(.65f,.65f,.65f) });
        _spy.SetGlass(scenario == "cloak" ? .5f : 0);
        var compiler = new MaterialCompiler(new MaterialsDoc(), new TextureCache(""));
        for (int i = 0; i < 3; i++)
        {
            var material = compiler.Build(new MaterialEntry { Name = "benchmark-moving-object-" + i, Unlit = true }, LightSource.None);
            using var color = Image.CreateEmpty(1,1,false,Image.Format.Rgba8);
            color.Fill(new Color(.6f, .55f + i * .05f, .5f));
            ((ShaderMaterial)material).SetShaderParameter("tex_diffuse", ImageTexture.CreateFromImage(color));
            _objects.AddChild(new MeshInstance3D { Mesh = i == 1 ? new SphereMesh { Radius = .14f, Height = .28f } :
                new BoxMesh { Size = new Vector3(.26f,.32f,.20f) }, MaterialOverride = material,
                Position = _side * ((i - 1) * .32f) + Vector3.Up * ((i - 1) * .19f), Layers = RenderLayers.World });
        }
        _player.Visible = scenario != "objects"; _objects.Visible = scenario is "objects" or "mixed";
        Update(0);
    }

    public void Update(double phase)
    {
        float t = (float)phase, wave = Mathf.Sin(t * Mathf.Tau / 6);
        if (_player.Visible)
        {
            _player.Position = _origin + _forward * .55f + _side * (.20f * wave - (_scenario == "mixed" ? .35f : 0));
            _player.Rotation = new Vector3(0, .25f * wave, 0);
            _spy.Sampler.Play("WaitStAlFd0", frame: ((t % 6 + 6) % 6) / 6);
            _spy.Pose();
        }
        _objects.Position = _origin + _forward * .50f + _side * (.16f * wave + (_scenario == "mixed" ? .35f : 0));
        foreach (Node child in _objects.GetChildren()) ((Node3D)child).Rotation = new Vector3(.15f * wave, .4f * wave, .1f * wave);
    }
}
