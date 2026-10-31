using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Render;

namespace OpenScctGame.Views;

/// <summary>
/// One <see cref="SimPawnView"/> per pawn of the client world: views are made when a pawn appears (replicated or
/// spawned) and freed when it goes. The local player's own pawn is hidden while its view is first person.
/// </summary>
public partial class PawnViews : Node3D
{
    private readonly Dictionary<SBasePawn, SimPawnView> _views = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SBasePawn> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SBasePawn> _unbound = new(ReferenceEqualityComparer.Instance);
    private readonly List<SBasePawn> _gone = [];

    public PawnViews()
    {
        Name = "Pawns";
    }

    public RenderMap? Map { get; set; }

    public string? SharedRoot { get; set; }

    /// <summary>Pawns whose mesh the shared packs do not have (drawn as nothing).</summary>
    public int Unbound => _unbound.Count;

    public IReadOnlyCollection<SimPawnView> Views => _views.Values;

    /// <summary>The view of <paramref name="pawn"/>, or null.</summary>
    public SimPawnView? ViewOf(SBasePawn? pawn) => pawn is not null && _views.TryGetValue(pawn, out SimPawnView? v) ? v : null;

    /// <summary>The world transform of <paramref name="bone"/> of <paramref name="pawn"/> as last drawn, or false.</summary>
    public bool TryGetBone(SBasePawn? pawn, Name bone, out Transform3D world)
    {
        world = default;
        return ViewOf(pawn) is { } view && view.TryGetBone(bone, out world);
    }

    /// <summary>After a client tick: adds and removes views, then each takes its pawn's tick state.</summary>
    public void OnTick(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);

        // The drawn poses answer the camera's bone lookups (the first-person view on the head, ICameraBones).
        if (level.GetService<ICameraBones>() is null)
        {
            level.SetService<ICameraBones>(new PoseCameraBones(this));
        }

        _seen.Clear();
        foreach (SBasePawn pawn in level.AllActors<SBasePawn>())
        {
            if (pawn.bDeleteMe)
            {
                continue;
            }

            _seen.Add(pawn);
            if (!_views.ContainsKey(pawn) && !_unbound.Contains(pawn))
            {
                var view = new SimPawnView { Map = Map };
                if (!view.Bind(pawn, SharedRoot))
                {
                    _unbound.Add(pawn);
                    view.Free();
                    continue;
                }

                AddChild(view);
                _views[pawn] = view;
            }
        }

        _gone.Clear();
        foreach (KeyValuePair<SBasePawn, SimPawnView> kv in _views)
        {
            if (!_seen.Contains(kv.Key))
            {
                _gone.Add(kv.Key);
            }
        }

        foreach (SBasePawn pawn in _gone)
        {
            if (_views.Remove(pawn, out SimPawnView? view))
            {
                view.QueueFree();
            }
        }

        _unbound.IntersectWith(_seen);
        foreach (SimPawnView view in _views.Values)
        {
            view.OnTick();
        }
    }

    /// <summary>
    /// Draws every pawn; <paramref name="firstPerson"/> (the local pawn seen from its eyes) is hidden from the main view
    /// but still casts the flares' shadows (<see cref="SimPawnView.SetShadowOnly"/>).
    /// </summary>
    public void Draw(float alpha, float dt, SBasePawn? firstPerson, SBasePawn? localAimer = null, FRotator localView = default)
    {
        if (!Visible) return;
        foreach (KeyValuePair<SBasePawn, SimPawnView> kv in _views)
        {
            SimPawnView view = kv.Value;
            view.DrawnView = ReferenceEquals(kv.Key, localAimer) ? localView : null;
            view.Draw(alpha, dt);
            view.SetShadowOnly(ReferenceEquals(kv.Key, firstPerson));
        }
    }
}

/// <summary>
/// <see cref="ICameraBones"/> from the drawn pawn poses: a bone's world placement converted back to UE space (the
/// rotation from the bone's axes, as FCoords::OrthoRotation does). The centred camera start keeps the pawn's centre.
/// </summary>
public sealed class PoseCameraBones : ICameraBones
{
    private readonly PawnViews _views;

    public PoseCameraBones(PawnViews views)
    {
        _views = views ?? throw new ArgumentNullException(nameof(views));
    }

    public bool TryGetBone(Pawn pawn, Name bone, out FVector location, out FRotator rotation)
    {
        location = default;
        rotation = default;
        if (pawn is not SBasePawn sb || !_views.TryGetBone(sb, bone, out Transform3D world))
        {
            return false;
        }

        location = UeSpace.ToUe(world.Origin);
        Basis b = world.Basis.Orthonormalized();
        rotation = OrthoRotation(UeSpace.DirectionToUe(b.Column0), UeSpace.DirectionToUe(b.Column2), UeSpace.DirectionToUe(b.Column1));
        return true;
    }

    public bool TryGetCentre(Pawn pawn, out FVector location)
    {
        location = default;
        return false;
    }

    /// <summary>The rotator of an orthonormal frame (UE2 FCoords::OrthoRotation).</summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 FCoords::OrthoRotation: pitch and yaw from the X axis, roll from the Z axis against the unrolled Y axis")]
    public static FRotator OrthoRotation(FVector x, FVector y, FVector z)
    {
        const double ToUnits = 32768.0 / Math.PI;
        int pitch = (int)Math.Round(Math.Atan2(x.Z, Math.Sqrt((x.X * x.X) + (x.Y * x.Y))) * ToUnits);
        int yaw = (int)Math.Round(Math.Atan2(x.Y, x.X) * ToUnits);
        new FRotator(pitch, yaw, 0).GetAxes(out _, out FVector sy, out _);
        int roll = (int)Math.Round(Math.Atan2((z.X * sy.X) + (z.Y * sy.Y) + (z.Z * sy.Z), (y.X * sy.X) + (y.Y * sy.Y) + (y.Z * sy.Z)) * ToUnits);
        return new FRotator(pitch, yaw, roll);
    }
}
