using System;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.Views;

/// <summary>Which character mesh a pawn view draws.</summary>
public enum PawnMesh
{
    /// <summary>The spy, SPerso.ATT_01.</summary>
    Spy,

    /// <summary>The merc, SPerso.DEF_01.</summary>
    Merc,
}

/// <summary>
/// Draws one pawn with the Faithful renderer: the character mesh posed by <see cref="Ue2PoseApplier"/> from an
/// <see cref="AnimSampler"/> that plays one sequence by name at a rate, placed at Location + PrePivot with the
/// pawn's yaw, and lit by the map's actor lighting where it stands. Views read the simulation; they never write it.
/// Until the core animation channels exist, callers pick the sequence (<see cref="Play"/>).
/// </summary>
public partial class PawnView : Node3D
{
    /// <summary>Bounding sphere radius used to rank lights for a pawn (cylinder 35 x 89 half height).</summary>
    public const float LightRadius = 95f;

    private SkeletalModel? _model;
    private FVector _location;

    public SkeletalModel? Model => _model;

    public RenderMap? Map { get; set; }

    /// <summary>Loads the character from the shared packs; false when they are missing.</summary>
    public bool Load(PawnMesh mesh, string? sharedRoot = null)
    {
        string asset = mesh == PawnMesh.Spy ? "SPerso.ATT_01" : "SPerso.DEF_01";
        string? glb = SkeletalModel.FindGlb(asset, sharedRoot ?? Map?.SharedRoot);
        if (glb is null)
        {
            return false;
        }

        _model?.Root.QueueFree();
        _model = SkeletalModel.Load(glb);
        AddChild(_model.Root);
        return true;
    }

    /// <summary>Plays a sequence by name at <paramref name="rate"/>; false when the mesh has no such sequence.</summary>
    public bool Play(string sequence, float rate = 1f, bool loop = true, float frame = 0f) =>
        _model?.Sampler.Play(sequence, rate, loop, frame) ?? false;

    /// <summary>Places the pawn (UE location, rotation and PrePivot) and relights it.</summary>
    public void Place(FVector location, FRotator rotation, FVector prePivot)
    {
        if (_model is null)
        {
            return;
        }

        _location = location;
        _model.Place(location, new FRotator(0, rotation.Yaw, 0), prePivot);
        Relight();
    }

    public void Relight()
    {
        if (_model is not null && Map is not null)
        {
            _model.Light(Map.LightActor(_location, LightRadius, 0));
        }
    }

    public override void _Process(double delta)
    {
        if (_model is null)
        {
            return;
        }

        _model.Sampler.Advance(delta);
        _model.Pose();
    }

    /// <summary>Advances and poses without waiting for a frame (tests, offline renders).</summary>
    public void Step(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        _model?.Sampler.Advance(seconds);
        _model?.Pose();
    }
}
