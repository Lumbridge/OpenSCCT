using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Animation;
using OpenScct.Content;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.AnimationStudio;

/// <summary>The editor's private test scene, using the shipped animation evaluator and pose uploader.</summary>
public partial class AnimationPreview : Node3D
{
    private AnimationBundle _bundle = null!;
    private readonly List<(string Id, Node3D Root, Skeleton3D? Rig, SkeletonDefinition Definition, string? Clip)> _models = [];
    private float _time;
    private bool _playing = true;
    private int _clip;
    private Label _label = null!;
    public string Folder { get; set; } = "";
    public string? FirstPersonId { get; set; }
    private FirstPersonProfile? _firstPerson;
    private readonly MeshInstance3D _skeletonLines = new();
    private readonly StandardMaterial3D _lineMaterial = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Colors.Cyan, NoDepthTest = true };
    private bool _sampled;
    public override void _Ready()
    {
        try
        {
            _bundle = AnimationBundle.Load(Folder) ?? throw new InvalidDataException("Animation test bundle is missing.");
            var camera = new Camera3D { Current = true, Position = new(2.5f, 1.6f, 3.5f), Environment = new Godot.Environment
            { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new(.075f, .078f, .09f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .8f } };
            AddChild(camera); camera.LookAt(new(0, .9f, 0));
            _firstPerson = _bundle.Project.FirstPersonProfiles.FirstOrDefault(p => p.Id == FirstPersonId);
            if (_firstPerson is not null) { camera.Position = Vector3.Zero; camera.LookAt(Vector3.Right); camera.Near = .005f; camera.Fov = FirstPersonFraming.VerticalFov(_firstPerson.FieldOfView); }
            AddChild(new DirectionalLight3D { RotationDegrees = new(-35, -30, 0) });
            AddChild(_skeletonLines);
            var instances = _bundle.Project.Scene.Count > 0 ? _bundle.Project.Scene : _bundle.Project.Skeletons.Select(s => new RigInstance { Skeleton = s.Id, Clip = _bundle.Project.Clips.FirstOrDefault(c => c.Skeleton == s.Id)?.Id }).ToList();
            foreach (RigInstance instance in instances)
            {
                if (_firstPerson is not null && instance.Id != _firstPerson.MainRig && instance.Id != _firstPerson.ArmsRig) continue;
                SkeletonDefinition definition = _bundle.Project.Skeletons.Single(s => s.Id == instance.Skeleton);
                string path = AnimationBundle.AssetPath(Folder, definition.Model!);
                var importer = new GltfDocument(); var state = new GltfState { CreateAnimations = false };
                if (importer.AppendFromFile(path, state) != Error.Ok || importer.GenerateScene(state) is not Node3D scene) throw new InvalidDataException("Cannot load animation test model.");
                scene.Transform = new(new Basis(new Quaternion(instance.Transform.Rotation.X, instance.Transform.Rotation.Y, instance.Transform.Rotation.Z, instance.Transform.Rotation.W)).Scaled(new(instance.Transform.Scale.X, instance.Transform.Scale.Y, instance.Transform.Scale.Z)), new(instance.Transform.Position.X, instance.Transform.Position.Y, instance.Transform.Position.Z));
                scene.Transform *= new Transform3D(new Basis(new Quaternion(definition.DisplayTransform.Rotation.X, definition.DisplayTransform.Rotation.Y, definition.DisplayTransform.Rotation.Z, definition.DisplayTransform.Rotation.W)).Scaled(new(definition.DisplayTransform.Scale.X, definition.DisplayTransform.Scale.Y, definition.DisplayTransform.Scale.Z)), new(definition.DisplayTransform.Position.X, definition.DisplayTransform.Position.Y, definition.DisplayTransform.Position.Z));
                AddChild(scene); _models.Add((instance.Id, scene, Find<Skeleton3D>(scene), definition, instance.Clip));
            }
            var canvas = new CanvasLayer(); AddChild(canvas); _label = new Label { Position = new(24, 20) }; canvas.AddChild(_label);
            GD.Print("ANIMATION_PREVIEW_READY " + _bundle.Project.Clips.Count + " clips");
        }
        catch (Exception ex) { GD.PrintErr("Animation preview: " + ex.Message); GetTree().Quit(1); }
    }
    public override void _Process(double delta)
    {
        if (_bundle is null) return;
        if (_playing) _time += (float)delta;
        var lines = new ImmediateMesh(); int vertices = 0; lines.SurfaceBegin(Mesh.PrimitiveType.Lines, _lineMaterial);
        foreach (var model in _models)
        {
            var clips = _bundle.Project.Clips.Where(c => c.Skeleton == model.Definition.Id).ToArray();
            if (clips.Length == 0) continue;
            FirstPersonAction? action = _firstPerson?.Actions.ElementAtOrDefault(_clip % Math.Max(1, _firstPerson.Actions.Count));
            string? chosen = model.Id == _firstPerson?.ArmsRig ? action?.ArmsClip : action?.Clip;
            OpenScct.Animation.AnimationClip clip = clips.FirstOrDefault(c => c.Id == chosen) ?? clips[_clip % clips.Length];
            BoneTransform[] pose = ClipEvaluator.Evaluate(model.Definition, clip, _time % clip.Duration);
            if (model.Rig is not null) StudioPoseBridge.Apply(model.Rig, model.Definition, pose);
            else
            {
                var world = ClipEvaluator.World(model.Definition, pose);
                for (int bone = 0; bone < pose.Length; bone++)
                    if (model.Definition.Bones[bone].Parent is int parent && parent >= 0)
                    {
                        var a = world[parent].Translation; var b = world[bone].Translation;
                        lines.SurfaceAddVertex(model.Root.Transform * new Vector3(a.X, a.Y, a.Z));
                        lines.SurfaceAddVertex(model.Root.Transform * new Vector3(b.X, b.Y, b.Z)); vertices += 2;
                    }
            }
            _label.Text = $"Animation test · {action?.Name ?? clip.Name} · {_time % clip.Duration:0.000}s\nSpace: pause · Left/Right: actions/clips · Escape: close";
        }
        if (vertices > 0) lines.SurfaceEnd();
        Mesh? previous = _skeletonLines.Mesh; _skeletonLines.Mesh = vertices > 0 ? lines : null; previous?.Dispose();
        if (vertices == 0) lines.Dispose();
        if (!_sampled) { _sampled = true; GD.Print("ANIMATION_PREVIEW_POSE rigs=" + _models.Count + " bones=" + _models.Sum(m => m.Definition.Bones.Count)); }
    }
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key) return;
        if (key.Keycode == Key.Space) _playing = !_playing;
        if (key.Keycode == Key.Right) { _clip++; _time = 0; }
        if (key.Keycode == Key.Left) { _clip = Math.Max(0, _clip - 1); _time = 0; }
        if (key.Keycode == Key.Escape) GetTree().Quit();
    }
    private static T? Find<T>(Node node) where T : Node
    {
        if (node is T result) return result;
        foreach (Node child in node.GetChildren()) if (Find<T>(child) is { } found) return found;
        return null;
    }
}
