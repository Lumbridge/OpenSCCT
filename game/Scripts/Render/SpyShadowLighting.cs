using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.Lighting;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using OpenScct.Server;
using OpenScctGame.Render.Data;
using OpenScctGame.Views;

namespace OpenScctGame.Render;

/// <summary>
/// The local spy's logical projected shadow, sampled after simulation and before calc view. Its light cache uses
/// live actors and collision rather than the previous draw's shader uniforms; a rendered shadow texture invokes
/// the original sticky-camera view solver a second time even when the texture is later culled.
/// </summary>
[PortOf("eng:Render.ActorShadowLighting", Spec = "spec/native/camera.md")]
public sealed class SpyShadowLighting
{
    private readonly RenderMap _map;
    private readonly ClientSession _client;
    private readonly Func<PawnViews?> _views;
    private readonly ActorShadowLighting _lighting = new();
    private readonly List<ActorShadowLight> _lights = [];
    private readonly Dictionary<string, Actor?> _mapActors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Actor> _bound = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Actor> _runtime = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _ignoredNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Actor> _gone = [];
    private MapLoadResult? _loaded;
    private SBasePawn? _pawn;
    private uint _reset;
    private FVector _lastLocation;
    private float _lastTime;
    private bool _lastTeleported;

    public SpyShadowLighting(RenderMap map, ClientSession client, Func<PawnViews?> views)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _views = views ?? throw new ArgumentNullException(nameof(views));
    }

    /// <summary>The shadow opacity computed by the last logical render update (zero means no projected shadow).</summary>
    public byte Darkness => _lighting.Darkness;

    /// <summary>Warms the native light cache every tick and returns whether this tick has a shadow view update.</summary>
    public bool Update(SPlayerController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (controller.Pawn is not SPawnAttaque pawn || pawn.bDeleteMe || pawn.Health <= 0)
        {
            _pawn = null;
            _lighting.Reset();
            return false;
        }

        MapLoadResult? loaded = _client.Map?.Loaded;
        if (!ReferenceEquals(_loaded, loaded))
        {
            _loaded = loaded;
            BindMapActors();
            _lighting.Reset();
        }

        float time = pawn.XLevel.Info.TimeSeconds;
        bool teleported = pawn.bJustTeleported && !_lastTeleported;
        if (!ReferenceEquals(_pawn, pawn) || _reset != pawn.NetworkResetId || time < _lastTime || teleported ||
            FVector.DistSquared(_lastLocation, pawn.Location) > 256f * 256f)
        {
            _lighting.Reset();
        }

        _pawn = pawn;
        _reset = pawn.NetworkResetId;
        _lastLocation = pawn.Location;
        _lastTime = time;
        _lastTeleported = pawn.bJustTeleported;

        // ShadowProjector.Tick attaches from the previous render's darkness; this render then refreshes it.
        bool updateView = _lighting.Darkness != 0 && !pawn.bHidden;
        SamplePawn(pawn, out FVector center, out float radius, out FVector shadowOrigin);
        CollectLights(pawn, center, radius);
        _lighting.Update(_lights, center, radius, shadowOrigin, time, pawn.XLevel.Info.ShadowDarknessScale,
            pawn.bLightingVisibility, pawn.GlassEffect, Visible);
        return updateView;
    }

    private void BindMapActors()
    {
        _mapActors.Clear();
        _bound.Clear();
        _runtime.Clear();
        foreach (LightEntry light in _map.Lighting.Lights)
        {
            Actor? actor = _loaded?.FindActor(light.Name);
            _mapActors[light.Name] = actor;
            if (actor is not null) _bound.Add(actor);
        }
    }

    private void RebindMapActors(Level level)
    {
        foreach (LightEntry light in _map.Lighting.Lights)
        {
            if (!_mapActors.TryGetValue(light.Name, out Actor? previous) || previous is null || !previous.bDeleteMe)
                continue;
            Actor? replacement = _loaded?.Gameplay.FindActor(light.Name) is { } record
                ? _client.Driver.MapBinding.Get(record.Index) : null;
            if (replacement is null && !previous.bStatic && !previous.bNoDelete)
            {
                // Dynamic map actors are destroyed on the client before replication. As DeviceViews does,
                // bind the server copy by class and placed location (the wire rounds positions to whole units).
                foreach (Actor candidate in level.DynamicActors<Actor>())
                {
                    if (candidate.bDeleteMe || _bound.Contains(candidate) || candidate.GetType() != previous.GetType()) continue;
                    FVector delta = candidate.Location - light.Location;
                    if (MathF.Abs(delta.X) <= 1f && MathF.Abs(delta.Y) <= 1f && MathF.Abs(delta.Z) <= 1f)
                    {
                        replacement = candidate;
                        break;
                    }
                }
            }
            if (replacement is null) continue;
            _mapActors[light.Name] = replacement;
            _bound.Remove(previous);
            _bound.Add(replacement);
            _runtime.Remove(replacement);
        }
    }

    private void CollectLights(SBasePawn pawn, FVector center, float radius)
    {
        _lights.Clear();
        Level level = pawn.XLevel;
        RebindMapActors(level);
        _ignoredNames.Clear();
        foreach (Actor? ignored in pawn.IgnoredEchelonLights)
        {
            if (ignored is not null && _loaded?.RecordOf(ignored) is { } record)
                _ignoredNames.Add(record.Id);
        }
        ICollisionWorld? collision = _client.Map?.Physics?.Collision;
        int zone = collision?.GetZoneIndex(center) ?? _map.ZoneIndexAt(center);
        foreach (LightEntry light in _map.Lighting.Lights)
        {
            if (_mapActors.TryGetValue(light.Name, out Actor? actor) && actor is not null)
            {
                _lights.Add(LiveLight(actor, pawn, zone, center, radius, collision, _ignoredNames.Contains(light.Name)));
                continue;
            }

            FVector location = light.Location;
            FRotator rotation = light.Rotation;
            if (_map.TryGetLightPlace(light.Name, out FVector moved, out FRotator turned))
            {
                location = moved;
                rotation = turned;
            }

            int lightZone = light.ZoneIndex >= 0 ? light.ZoneIndex : collision?.GetZoneIndex(location) ?? -1;
            bool present = ActorLightRules.LightsActors(light.ApplyInGame, light.Dynamic, light.LightType, light.Effect) &&
                _map.IsLightOn(light.Name, light.InitiallyOn) &&
                (!light.OwnZoneOnly || zone < 0 || lightZone < 0 || zone == lightZone) &&
                pawn.bSpecialLit == light.SpecialLit &&
                (pawn.bUseDynamicLights || !light.Dynamic) && !_ignoredNames.Contains(light.Name) &&
                Intersects(light.Effect, location, light.Radius, center, radius);
            _lights.Add(new ActorShadowLight(light, location, rotation, light.Brightness, light.Radius, light.Cone,
                light.Effect, light.ActorShadows, light.Static, light.Dynamic, present));
        }

        foreach (Actor actor in level.DynamicActors<Actor>())
        {
            if (!_bound.Contains(actor) && actor.LightType != ELightType.LT_None)
                _runtime.Add(actor);
        }

        // Retain off lights so the native cache can fade them, and report destruction before dropping their key.
        _gone.Clear();
        foreach (Actor actor in _runtime)
        {
            _lights.Add(LiveLight(actor, pawn, zone, center, radius, collision));
            if (actor.bDeleteMe) _gone.Add(actor);
        }
        foreach (Actor actor in _gone) _runtime.Remove(actor);
    }

    private static ActorShadowLight LiveLight(Actor actor, SBasePawn pawn, int zone, FVector center, float radius,
        ICollisionWorld? collision, bool ignoredName = false)
    {
        int lightZone = collision?.GetZoneIndex(actor.Location) ?? actor.Region.ZoneNumber;
        bool present = !actor.bDeleteMe &&
            ActorLightRules.LightsActors(actor.bApplyToInGameLighting, actor.bDynamicLight, (int)actor.LightType, (int)actor.LightEffect) &&
            (!actor.bAffectOwnZoneOnly || zone < 0 || lightZone < 0 || zone == lightZone) &&
            pawn.bSpecialLit == actor.bSpecialLit && (pawn.bUseDynamicLights || !actor.bDynamicLight) &&
            !ignoredName && !pawn.IgnoredEchelonLights.Contains(actor) &&
            Intersects((int)actor.LightEffect, actor.Location, actor.LightRadius, center, radius);
        return new ActorShadowLight(actor, actor.Location, actor.Rotation, actor.LightBrightness, actor.LightRadius,
            actor.LightCone, (int)actor.LightEffect, actor.bActorShadows, actor.bStatic, actor.bDynamicLight, present, !actor.bDeleteMe);
    }

    private static bool Intersects(int effect, FVector location, float lightRadius, FVector center, float radius)
    {
        if (effect == ActorLightRules.LE_Sunlight) return true;
        float reach = ActorLightRules.WorldRadius(lightRadius) + radius;
        return reach > 0f && FVector.DistSquared(location, center) <= reach * reach;
    }

    [Provenance(ProvenanceKind.Estimated, "Use the drawn mesh bounds and package bone 0; before the first pose use the pawn cylinder and native root fallback Location + (0,0,5)")]
    private void SamplePawn(SBasePawn pawn, out FVector center, out float radius, out FVector shadowOrigin)
    {
        center = pawn.Location;
        radius = MathF.Sqrt(pawn.CollisionRadius * pawn.CollisionRadius + pawn.CollisionHeight * pawn.CollisionHeight);
        shadowOrigin = pawn.Location + new FVector(0f, 0f, 5f);
        if (_views()?.ViewOf(pawn) is not { Model: { } model } view) return;

        // A newly bound model has not been placed/posed yet. Its Godot origin is not a world-space pawn sample.
        if (view.DrawnLocation == default && pawn.Location != default) return;

        FVector tickOffset = pawn.Location - view.DrawnLocation;
        Aabb bounds = default;
        bool found = false;
        foreach (MeshInstance3D mesh in model.Meshes)
        {
            Aabb box = mesh.GlobalTransform * mesh.GetAabb();
            if (box.Size.LengthSquared() <= 0f) continue;
            bounds = found ? bounds.Merge(box) : box;
            found = true;
        }

        if (found)
        {
            center = UeSpace.ToUe(bounds.GetCenter()) + tickOffset;
            radius = bounds.Size.Length() * 0.5f * UeSpace.UnitsPerMetre;
        }

        if (model.PackageBones.Count > 0 && CameraBones.TryGet(pawn, new Name(model.PackageBones[0]), out FVector bone, out _))
            shadowOrigin = bone + tickOffset;
    }

    [Provenance(ProvenanceKind.Decoded, "Actor light visibility, game 0x10a363d0: point mask 0x386, sunlight BSP 0x104 and geometry 0x382")]
    private bool Visible(ActorShadowLight light, FVector center)
    {
        if (_client.Map?.Physics?.Collision is not { } collision) return true;
        if (light.Effect != ActorLightRules.LE_Sunlight)
            return !collision.LineCheck(out _, _pawn, light.Location, center, default, (ETraceFlags)0x386);

        FVector end = center - light.Rotation.Vector() * 65536f;
        return !collision.LineCheck(out _, _pawn, end, center, default, (ETraceFlags)0x104) &&
            !collision.LineCheck(out _, _pawn, end, center, default, (ETraceFlags)0x382);
    }
}
