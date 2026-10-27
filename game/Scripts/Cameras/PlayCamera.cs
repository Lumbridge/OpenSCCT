using System;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;

namespace OpenScctGame.Cameras;

/// <summary>
/// Puts the simulation's camera on the screen: the calc view of the last two client ticks (vCamLocation,
/// rCamRotation and FovAngle, from <see cref="PlaySession.PreviousView"/> and <see cref="PlaySession.View"/>)
/// interpolated by how far the clock is into the next tick (<see cref="ViewBlend"/>), plus the mouse turn the
/// simulation has not drawn yet (<see cref="LookAhead"/>), handed to the <see cref="FaithfulView"/>. The first view,
/// and a jump further than <see cref="ViewBlend.CutDistance"/> (a respawn, a change of view source), is a cut.
/// </summary>
public sealed class PlayCamera
{
    /// <summary>A camera move longer than this in one tick is drawn as a cut, not interpolated.</summary>
    public const float CutDistance = ViewBlend.CutDistance;

    private readonly FaithfulView _view;
    private bool _placed;

    public PlayCamera(FaithfulView view)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    /// <summary>The camera of the last frame (uu, rotator, horizontal FOV in degrees).</summary>
    public (FVector Location, FRotator Rotation, float Fov) Current { get; private set; }

    /// <summary>
    /// Places the view between <paramref name="previous"/> and <paramref name="current"/>, turned further by
    /// <paramref name="look"/> (the mouse turn not yet drawn); false before the first view.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Render interpolation between the last two simulated views (the original renders each frame's own view)")]
    public bool Apply(ViewSample previous, ViewSample current, float alpha, FRotator look = default, float orbit = 0f)
    {
        if (!current.Valid)
        {
            return false;
        }

        bool cut = !_placed || ViewBlend.IsCut(previous, current);
        (FVector Location, FRotator Rotation, float Fov) camera = cut ? (current.Location, current.Rotation, current.Fov) : ViewBlend.Blend(previous, current, alpha);
        if (!cut)
        {
            // A cut draws the last tick as it is (the look-ahead's rest of a tick is already in it).
            FRotator turned = new(camera.Rotation.Pitch + look.Pitch, camera.Rotation.Yaw + look.Yaw, camera.Rotation.Roll + look.Roll);

            // A following view turned ahead moves round its target, the orbit distance in front of it (camera.md 3.11).
            if (orbit > 0f && (look.Pitch != 0 || look.Yaw != 0))
            {
                camera.Location += (camera.Rotation.Vector() - turned.Vector()) * orbit;
            }

            camera.Rotation = turned;
        }
        _view.SetUeCamera(camera.Location, camera.Rotation, camera.Fov, cut);
        Current = camera;
        _placed = true;
        return true;
    }

    /// <summary>The camera <paramref name="alpha"/> of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static (FVector Location, FRotator Rotation, float Fov) Blend(ViewSample a, ViewSample b, float alpha) => ViewBlend.Blend(a, b, alpha);
}
