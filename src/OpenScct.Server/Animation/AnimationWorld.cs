using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenScct.Animation;
using OpenScct.Content;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Game.Movement;

namespace OpenScct.Server;

/// <summary>One level's explicit animation bindings. No process-wide playback state is shared between rounds.</summary>
public sealed class AnimationWorld
{
    public AnimationProject Project { get; }
    public IReadOnlyDictionary<string, Actor> Actors { get; }
    public AnimationWorld(AnimationProject project, IReadOnlyDictionary<string, Actor>? actors = null)
    { Project = project; Actors = actors ?? new Dictionary<string, Actor>(StringComparer.Ordinal); }
    public static AnimationWorld? Install(Level level, SvMap? map, MapLoadResult? loaded, bool client, string? sharedFolder)
    {
        AnimationBundle? shared = sharedFolder is null ? null : AnimationBundle.Load(sharedFolder);
        if (shared is null && map?.Animations is null) return null;
        AnimationProject? SharedProject(AnimationBundle? bundle, string? folder)
        {
            if (bundle is null || folder is null) return null;
            AnimationProject result = AnimationJson.Clone(bundle.Project);
            foreach (SkeletonDefinition skeleton in result.Skeletons)
                if (skeleton.Model is { } model) skeleton.Model = AnimationBundle.AssetPath(folder, model);
            return result;
        }
        AnimationProject project = AnimationBundle.Resolve(SharedProject(shared, sharedFolder), SharedProject(map?.Animations, map?.Root));
        var world = new AnimationWorld(project, loaded?.PlacedActors().ToDictionary(p => "actor:" + p.Id, p => p.Actor, StringComparer.Ordinal));
        level.SetService(world);
        if (!client)
            for (int i = 0; i < project.Bindings.Count; i++)
            {
                AnimationBinding binding = project.Bindings[i];
                if (binding.Graph is null && binding.Sequence is null) continue;
                AnimationController controller = level.Spawn<AnimationController>(tag: new Name(binding.Slot)) ?? throw new InvalidDataException("Cannot create animation controller.");
                controller.BindingIndex = i;
                controller.Initialize();
            }
        return world;
    }
    public Actor? Actor(string target) => Actors.GetValueOrDefault(target);
    public SkeletonDefinition? Model(string asset) => asset.StartsWith("osanim.", StringComparison.Ordinal)
        ? Project.Skeletons.FirstOrDefault(s => s.Id == asset[7..]) : null;
    public AnimationController? Controller(Level level, string target)
    {
        foreach (AnimationController controller in level.AllActors<AnimationController>())
            if (controller.BindingIndex >= 0 && controller.BindingIndex < Project.Bindings.Count && Project.Bindings[controller.BindingIndex].Target == target) return controller;
        return null;
    }
    public AnimationBinding? Binding(string target, string slot) => Project.Bindings.FirstOrDefault(b => b.Clip is not null && b.Target.Equals(target, StringComparison.OrdinalIgnoreCase) && b.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase));
    public IPawnAnimationSource PawnSource(IPawnAnimationSource fallback) => new StudioPawnAnimationSource(this, fallback);
    public IMeshAnimations MeshSource(IMeshAnimations fallback) => new StudioMeshAnimations(this, fallback);
}
