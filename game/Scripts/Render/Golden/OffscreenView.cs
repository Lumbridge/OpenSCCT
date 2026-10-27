using System;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.Render.Golden;

/// <summary>
/// A <see cref="RenderMap"/> in its own world inside a fixed-size SubViewport, seen through a
/// <see cref="FaithfulView"/>: pictures at an exact size whatever the window does, for golden renders and render
/// statistics. Optionally holds a spy pawn for third-person views.
/// </summary>
public sealed class OffscreenView : IDisposable
{
    public OffscreenView(Node parent, RenderMap map, Vector2I size)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(map);
        Map = map;
        Viewport = new SubViewport
        {
            Name = "Offscreen",
            Size = size,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Godot.Viewport.Msaa.Disabled,
            ScreenSpaceAA = Godot.Viewport.ScreenSpaceAAEnum.Disabled,
            UseTaa = false,
            UseDebanding = false,
            Scaling3DScale = 1f,
        };
        View = new FaithfulView();
        Viewport.AddChild(View);
        Viewport.AddChild(map.Root);
        parent.AddChild(Viewport);
        View.Attach(map);
    }

    public SubViewport Viewport { get; }

    public FaithfulView View { get; }

    public RenderMap Map { get; }

    public SkeletalModel? Pawn { get; private set; }

    /// <summary>Adds the spy (SPerso.ATT_01) when the shared packs have it; false otherwise.</summary>
    public bool AddSpy(string? sharedRoot = null) => AddPawn(spy: true, sharedRoot);

    /// <summary>
    /// Adds the spy (SPerso.ATT_01) or the merc (SPerso.DEF_01) when the shared packs have it, with the vision values a
    /// pawn of that side has at spawn: HeatIntensity 1; in EMF the merc is drawn and the spy, weapon holstered, is not
    /// (SBasePawn.TestAndSetEMFVisible, spec/equipment/vision-goggles.md rule 12).
    /// </summary>
    public bool AddPawn(bool spy, string? sharedRoot = null)
    {
        string? glb = SkeletalModel.FindGlb(spy ? "SPerso.ATT_01" : "SPerso.DEF_01", sharedRoot ?? Map.SharedRoot);
        if (glb is null)
        {
            return false;
        }

        Pawn = SkeletalModel.Load(glb);
        Pawn.Root.Name = "Pawn";
        Pawn.SetVision(new Vector4(RenderK.PawnHeatIntensity, RenderK.DefaultHeatOpacity, 0f, spy ? 0f : 1f));
        Viewport.AddChild(Pawn.Root);
        return true;
    }

    /// <summary>Places the pawn, picks its sequence and frame, and lights it from the map.</summary>
    public void PlacePawn(FVector location, FRotator rotation, FVector prePivot, string sequence, float frame)
    {
        if (Pawn is null)
        {
            return;
        }

        Pawn.Place(location, new FRotator(0, rotation.Yaw, 0), prePivot);
        if (!Pawn.Sampler.Play(sequence, 1f, true, frame))
        {
            GD.PushWarning($"pawn has no sequence {sequence}");
        }

        Pawn.Pose();
        Pawn.Light(Map.LightActor(location, PawnLightRadius, 0));
    }

    /// <summary>Bounding sphere radius of a standing spy for light ranking (cylinder 35 x 89 half height).</summary>
    public const float PawnLightRadius = 95f;

    public Image Capture() => Viewport.GetTexture().GetImage();

    public void Dispose()
    {
        Viewport.QueueFree();
    }
}
