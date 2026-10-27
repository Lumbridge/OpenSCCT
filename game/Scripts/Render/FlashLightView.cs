using System;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;
using OpenScctGame.Views;

namespace OpenScctGame.Render;

/// <summary>
/// The merc torch on screen (SGadTorche, spec/equipment/merc-flashlight.md rule 7): an LE_ESpotShadow light
/// (<see cref="SpotShadowShape.Torch"/>) wherever a torch's LightType is on. The simulation's NativeTickTorche port
/// decides on and off and the direction; the light sits on the merc's Laser bone as drawn (the eye point when the
/// pawn is not drawn, as for the local merc in first person) and, while IK_Type is IKT_NONE, points along that
/// bone. One torch is drawn: the local merc's own, else the nearest other. Each spot shadow light takes one of the
/// template's slots: the torch, the nearest lit searchlight spot and the map's own LE_ESpotShadow lights
/// (<see cref="MapSpotLights"/>) are drawn together.
/// </summary>
[PortOf("nat:SBase.SGadTorche.NativeTickTorche", Spec = "spec/equipment/merc-flashlight.md#rules")]
public sealed partial class FlashLightView : Node
{
    private static readonly Name LaserBone = new("Laser");

    private SpotShadowLight? _light;
    private PoseCameraBones? _bones;

    public RenderMap? Map { get; set; }

    public PawnViews? Pawns { get; set; }

    /// <summary>The merc whose torch lights the scene, or null when every torch is off.</summary>
    public SBasePawn? LitBy { get; private set; }

    /// <summary>The light is on (its shadow pass runs and the template adds it).</summary>
    public bool On => _light?.On == true;

    /// <summary>Where the light was last placed (UE).</summary>
    public (FVector Location, FRotator Rotation) Placement => _light?.Placement ?? default;

    /// <summary>Frames the light was on (a smoke counts them).</summary>
    public int LitFrames { get; private set; }

    /// <summary>True when the last placement came from the drawn Laser bone.</summary>
    public bool FromBone { get; private set; }

    /// <summary>The map's own LE_ESpotShadow lights, once a map is set.</summary>
    public MapSpotLights? MapLights => _mapLights;

    private MapSpotLights? _mapLights;

    public void Draw(Level? level, SBasePawn? local, FVector cameraLocation)
    {
        if (Map is not null)
        {
            _mapLights ??= new MapSpotLights(Map, this);
            _mapLights.Update(cameraLocation);
        }

        DrawProjector(level, cameraLocation);
        SGadTorche? best = null;
        float bestD = float.MaxValue;
        if (level is not null)
        {
            foreach (SGadTorche torch in level.AllActors<SGadTorche>())
            {
                if (torch.bDeleteMe || torch.LightType == ELightType.LT_None || torch.Instigator is not SBasePawn { bDeleteMe: false, Health: > 0 } owner)
                {
                    continue;
                }

                float d = ReferenceEquals(owner, local) ? -1f : FVector.Dist(owner.Location, cameraLocation);
                if (d < bestD && d <= SpotShadowShape.Torch.Range * 2f)
                {
                    bestD = d;
                    best = torch;
                }
            }
        }

        if (best?.Instigator is not SBasePawn pawn || Map is null)
        {
            LitBy = null;
            if (_light is { On: true })
            {
                _light.On = false;
            }

            return;
        }

        _light ??= new SpotShadowLight(this, Map.Pack, SpotShadowShape.Torch);
        FVector location = best.Location;
        FRotator rotation = best.Rotation;
        FromBone = false;
        bool inCamera = pawn.bUsingCamNetwork && pawn.UsedObject is SCamNetwork;
        if (!inCamera && Pawns is not null)
        {
            _bones ??= new PoseCameraBones(Pawns);
            if (_bones.TryGetBone(pawn, LaserBone, out FVector boneAt, out FRotator boneRot))
            {
                location = boneAt;
                FromBone = true;
                if (SGadTorche.AimRotation(pawn) is null)
                {
                    rotation = boneRot;
                }
            }
        }

        LitBy = pawn;
        _light.Place(location, rotation);
        if (!_light.On)
        {
            _light.On = true;
        }

        LitFrames++;
    }

    /// <summary>The projector spot lighting the scene (an SProjoFixe's SProjoLight), or null.</summary>
    public SProjoLight? LitByProjector { get; private set; }

    /// <summary>
    /// The nearest lit searchlight spot (SProjoLight, LE_ESpotShadow, spec/devices/projector.md) takes a spot slot,
    /// shaped by the values its projector copied to it; it keeps to its own zone (bAffectOwnZoneOnly).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "SProjoLight has no EchelonLightMaterial: drawn with the torch cookie and attenuation; only the nearest is drawn")]
    private void DrawProjector(Level? level, FVector cameraLocation)
    {
        SProjoLight? best = null;
        float bestD = float.MaxValue;
        if (level is not null)
        {
            foreach (SProjoLight spot in level.AllActors<SProjoLight>())
            {
                if (spot.bDeleteMe || spot.LightType == ELightType.LT_None)
                {
                    continue;
                }

                float d = FVector.Dist(spot.Location, cameraLocation);
                if (d < bestD && d <= MathF.Max(spot.EchelonRange, 1f) * 2f)
                {
                    bestD = d;
                    best = spot;
                }
            }
        }

        LitByProjector = best;
        if (best is null || Map is null)
        {
            if (_projo is { On: true })
            {
                _projo.On = false;
            }

            return;
        }

        var shape = new SpotShadowShape(MathF.Max(best.EchelonRange, 1f), best.SpotWidth, best.SpotHeight, best.NearPlaneRatio,
            best.LightBrightness, best.LightHue, best.LightSaturation, best.EchelonNormalInfluence, RenderK.TorchAttenuation,
            RenderPack.TorchCookie);
        if (_projo is not null && _projo.Shape != shape)
        {
            _projo.On = false;
            _projo.Dispose();
            _projo = null;
        }

        _projo ??= new SpotShadowLight(this, Map.Pack, shape);
        _projo.Zone = Map.ZoneIndexAt(best.Location);
        _projo.Place(best.Location, best.Rotation);
        if (!_projo.On)
        {
            _projo.On = true;
        }
    }

    private SpotShadowLight? _projo;

    public override void _ExitTree()
    {
        _light?.Dispose();
        _light = null;
        _projo?.Dispose();
        _projo = null;
        _mapLights?.Dispose();
        _mapLights = null;
    }
}
