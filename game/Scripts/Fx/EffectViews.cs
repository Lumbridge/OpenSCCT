using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Particles;

namespace OpenScctGame.Fx;

/// <summary>
/// The gameplay effects on this machine (docs/EFFECTS.md), drawn from the client world after each tick:
/// <list type="bullet">
/// <item>every Emitter actor spawned during play (SSmokeEmitter, SChaffeEmitter, SGrenadeExplosion, SStickyCamEmitter,
/// SPoisonEmitter...) is a <see cref="ParticleSystemSim"/> of its class's converted sub-emitters, following the actor
/// and gone with it;</item>
/// <item>every spawned actor drawn as a static mesh (thrown grenades, stuck sticky cams, mines, alarm snares) shows its
/// class mesh at its place, rotation and draw scale, actor-lit, with the actor's live heat and EMF values;</item>
/// <item>effects the original spawns on each drawing machine from script (Level.NetMode != NM_DedicatedServer): the
/// grenades' launch trails (SSmokeLaunchEmitter, SFragLaunchEmitter), and what <see cref="Spawn"/> is asked for (impact
/// sparks, shell casings);</item>
/// <item>the flash lights (SFlashLight, SFlashLight_FragGrenade).</item>
/// </list>
/// It reads the client world and never writes to it.
/// </summary>
public sealed partial class EffectViews : Node3D
{
    private const int DT_StaticMesh = 8;

    private readonly Dictionary<Actor, Tracked> _tracked = new(ReferenceEqualityComparer.Instance);
    private readonly List<Loose> _loose = [];
    private readonly List<Actor> _gone = [];
    private readonly List<LightEntry> _worldLights = [];
    private ParticleTrace? _trace;
    private Level? _level;
    private uint _seed = 1;

    public EffectViews()
    {
        Name = "Effects";
    }

    public FxLibrary? Library { get; set; }

    /// <summary>The drawn pawns, for effects attached to a bone (AttachToBone).</summary>
    public OpenScctGame.Views.PawnViews? Pawns { get; set; }

    /// <summary>The map, for lighting the meshes of thrown gadgets.</summary>
    public RenderMap? Map { get; set; }

    /// <summary>Particle systems started so far, by class name.</summary>
    public Dictionary<string, int> Started { get; } = new(StringComparer.Ordinal);

    /// <summary>Actor meshes shown so far, by class name.</summary>
    public Dictionary<string, int> MeshesShown { get; } = new(StringComparer.Ordinal);

    /// <summary>The most particles drawn at once so far, by class name.</summary>
    public Dictionary<string, int> PeakParticles { get; } = new(StringComparer.Ordinal);

    /// <summary>Lights other views add for the next <see cref="Draw"/> only (the muzzle flashes' lights).</summary>
    public List<LightEntry> ExtraLights { get; } = [];

    /// <summary>Flash lights the last <see cref="Draw"/> handed to the world's dynamic lights.</summary>
    public int WorldLit { get; private set; }

    /// <summary>The most flash lights lighting the world at once so far.</summary>
    public int PeakWorldLit { get; private set; }

    /// <summary>Particles drawn by the last <see cref="Draw"/>.</summary>
    public int Particles { get; private set; }

    /// <summary>Particle systems alive now.</summary>
    public int LiveSystems
    {
        get
        {
            int n = _loose.Count;
            foreach (Tracked t in _tracked.Values)
            {
                n += t.View is null ? 0 : 1;
            }

            return n;
        }
    }

    /// <summary>Actor meshes drawn now.</summary>
    public int LiveMeshes
    {
        get
        {
            int n = 0;
            foreach (Tracked t in _tracked.Values)
            {
                n += t.Mesh is { Visible: true } ? 1 : 0;
            }

            return n;
        }
    }

    /// <summary>Classes asked for that the converted effects do not have.</summary>
    public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);

    /// <summary>The particles drawn now of one effect class (actor-backed and spawned).</summary>
    public int ParticlesOf(string cls)
    {
        int n = 0;
        foreach (Tracked t in _tracked.Values)
        {
            if (t.View is { } v && t.Class == cls)
            {
                n += v.Drawn;
            }
        }

        foreach (Loose l in _loose)
        {
            if (l.Class == cls)
            {
                n += l.View.Drawn;
            }
        }

        return n;
    }

    /// <summary>The mesh node shown for an actor, or null.</summary>
    public MeshInstance3D? MeshOf(Actor actor) => _tracked.TryGetValue(actor, out Tracked? t) ? t.Mesh : null;

    /// <summary>After a client tick: finds the new effect actors and the ones that went.</summary>
    public void OnTick(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Library is not { Available: true } lib)
        {
            return;
        }

        if (!ReferenceEquals(level, _level))
        {
            _level = level;
            _trace = TraceOf(level);
            foreach (Puddle p in _puddles)
            {
                p.Node.QueueFree();
            }

            _puddles.Clear();

            // The puddles the map places (PrisD) are there from the load; the map's render draws them.
            _placedPuddles.Clear();
            foreach (SFlaque placed in level.AllActors<SFlaque>())
            {
                _placedPuddles.Add(placed);
            }
        }

        _gone.Clear();
        foreach ((Actor a, Tracked t) in _tracked)
        {
            if (a.bDeleteMe)
            {
                _gone.Add(a);
            }
        }

        foreach (Actor a in _gone)
        {
            Tracked t = _tracked[a];
            if (a is Emitter { Killed: true } && t.View is { } killed)
            {
                // Emitter.Kill: no more spawning; the live particles die out where the emitter was.
                killed.Sim.Kill();
                _loose.Add(new Loose(killed, t.Class, PlaceOf(a), a.Rotation, float.PositiveInfinity, autoDestroy: true));
                t.View = null;
            }

            t.View?.Free();
            t.Mesh?.QueueFree();
            t.Light?.QueueFree();
            if (t.Trail is { } trail)
            {
                // The frag's Explode destroys its trail; the others are left where the grenade went (SetBase, LifeSpan).
                if (a is SProjFrag)
                {
                    trail.Life = 0f;
                }
                else
                {
                    trail.Follow = null;
                }
            }

            _tracked.Remove(a);
        }

        foreach (Actor a in level.AllActors<Actor>())
        {
            if (a.bDeleteMe || a.bNoDelete || a.bStatic || _tracked.ContainsKey(a) || DrawnByOwnView(a))
            {
                continue;
            }

            string cls = a.GetUcClass().Path;
            if (lib.Find(cls) is not { } fx)
            {
                continue;
            }

            var t = new Tracked(a, a.GetUcClass().Name);
            _tracked[a] = t;
            switch (fx.Kind)
            {
                case "emitter" when a is SSpawnableEmitter spawnable:
                    t.View = StartSpawnable(spawnable, fx);
                    break;
                case "emitter" when a is Emitter:
                    t.View = Start(fx, a.GetUcClass().Name, a is SSprinklerEmitter { SplashHeight: { } splash } ? SprinklerSplash(fx, splash) : null);
                    if (a is SFlicFlocEmitter flicFloc && t.View is { } flicView)
                    {
                        HookFlicFloc(t, flicFloc, flicView);
                    }

                    // A dynamic light emitter (the flare and its trail) also lights its surroundings.
                    if (a.bDynamicLight)
                    {
                        t.Light = LightFor(a);
                    }

                    break;
                case "projectile" or "mine" or "gadget" or "beam" when a.DrawType == (EDrawType)DT_StaticMesh:
                case "decal" when a is SFlaque && !_placedPuddles.Contains(a) && a.DrawType == (EDrawType)DT_StaticMesh:
                    // A sprinkler's puddle (the map's own placed puddles are drawn with the map).
                    t.Mesh = MeshFor(a, lib);
                    t.Trail = TrailFor(a);
                    break;
                case "light":
                    t.Light = LightFor(a);
                    break;
            }
        }
    }

    /// <summary>
    /// Actors another view draws: the spy pistol's SLaserBeam is drawn from the drawn hand by
    /// <see cref="SpyLaserView"/> (the simulation has no skeleton, so its own place starts in mid-air), and the merc
    /// laser's by <see cref="OpenScctGame.Render.MercLaserView"/>. Drawing them here as well showed every beam twice. The
    /// map's laser tripwire beams and the mines' beams (LasDetectBeam, not in the effects meshes) are drawn by
    /// <see cref="LaserTriggerView"/>.
    /// </summary>
    private static bool DrawnByOwnView(Actor a) =>
        a is SLaserBeam_MineLaser || (a is SLaserBeam && a.Owner is SWeaponAttaque or SGadTorcheLaser or SLaserTrigger);

    /// <summary>
    /// Starts an effect class at a place, as script's Spawn of a cosmetic class does on each drawing machine; it
    /// follows <paramref name="follow"/> while that lives. Null when the class has no converted sub-emitters.
    /// </summary>
    public ParticleSystemSim? Spawn(string cls, FVector location, FRotator rotation, Actor? follow = null)
    {
        if (Library is not { Available: true } lib)
        {
            return null;
        }

        if (lib.Find(cls) is not { } fx)
        {
            Missing.Add(cls);
            return null;
        }

        if (Start(fx, cls) is not { } view)
        {
            Missing.Add(cls);
            return null;
        }

        float life = fx.Float("LifeSpan");
        var loose = new Loose(view, ShortName(cls), location, rotation, life > 0f ? life : float.PositiveInfinity, fx.Bool("AutoDestroy"))
        {
            Follow = follow,
        };
        _loose.Add(loose);
        if (loose.Class == "SEffectWaterPipe")
        {
            StartPuddle(location, rotation);
        }

        return view.Sim;
    }

    /// <summary>
    /// SEffectWaterPipe.PostBeginPlay (a shot water pipe): a line 300 uu down from 30 uu ahead of the emitter (actors
    /// included); on a floor that is not water an SFlaque puddle 3 uu above it, turned to the emitter's yaw, that grows
    /// (SFlaque.Grow) and starts to shrink 4 s later (the emitter's Timer, SFlaque.Reduce). The puddle is the drawing
    /// machine's own (RemoteRole None).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SEffectWaterPipe.PostBeginPlay: Trace(Location - vect(0, 0, 300) + X * 30, Location + X * 30, true); not on SURFACE_MediumWater or SURFACE_DeepWater; Spawn(SFlaque, Hit + vect(0, 0, 3), yaw only); SetTimer(4.0)")]
    [Provenance(ProvenanceKind.Estimated, "The puddle is drawn by the view, without the SFlaque actor's collision (bBlockPlayers)")]
    private void StartPuddle(FVector location, FRotator rotation)
    {
        if (_level is null || OpenScct.Game.Equipment.GadgetWorld.Collision(_level.Info) is not { } world ||
            Library?.Mesh(PuddleMesh, lit: true) is not { } mesh)
        {
            return;
        }

        rotation.GetAxes(out FVector x, out _, out _);
        FVector start = location + (x * PuddleAhead);
        FVector end = start - new FVector(0f, 0f, PuddleTraceDepth);
        if (!world.LineCheck(out FCheckResult hit, null, end, start, FVector.Zero, (ETraceFlags)OpenScct.Game.Movement.MovementK.TraceScriptActors) ||
            !hit.bHit)
        {
            return;
        }

        var surface = (ESurfaceType)hit.SurfaceType;
        if (surface is ESurfaceType.SURFACE_MediumWater or ESurfaceType.SURFACE_DeepWater)
        {
            return;
        }

        var puddle = new Puddle(hit.Location + new FVector(0f, 0f, PuddleRaise), new FRotator(0, rotation.Yaw, 0))
        {
            Node = new MeshInstance3D { Name = "SFlaque", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off },
        };
        AddChild(puddle.Node);
        puddle.Place();
        if (Map is { } map)
        {
            ActorLighting.Apply(puddle.Node, map.LightActor(puddle.Location, 64f, 0));
        }

        _puddles.Add(puddle);
        MeshesShown["SFlaque"] = MeshesShown.GetValueOrDefault("SFlaque") + 1;
    }

    /// <summary>SFlaque.Tick for the view's puddles, and the water pipe's 4 s timer that turns growth into shrinking.</summary>
    private void TickPuddles(float dt)
    {
        for (int i = _puddles.Count - 1; i >= 0; i--)
        {
            Puddle p = _puddles[i];
            p.Age += dt;
            if (!p.Reduced && p.Age >= PuddleGrowTime)
            {
                p.Reduced = true;
                p.Variation = PuddleShrinkRate;
            }

            FVector scale = p.Scale;
            scale.X += p.Variation * dt;
            scale.Y += p.Variation * dt;
            if (scale.X < PuddleMaxScale)
            {
                p.Scale = scale;
                p.Place();
            }

            if (scale.X < PuddleMinScale)
            {
                p.Node.QueueFree();
                _puddles.RemoveAt(i);
            }
        }
    }

    private const string PuddleMesh = "SEffectStatic.water.Flaque";

    [Provenance(ProvenanceKind.Decoded, "SEffectWaterPipe.PostBeginPlay: X * 30.0")]
    private const float PuddleAhead = 30f;

    [Provenance(ProvenanceKind.Decoded, "SEffectWaterPipe.PostBeginPlay: vect(0, 0, 300)")]
    private const float PuddleTraceDepth = 300f;

    [Provenance(ProvenanceKind.Decoded, "SEffectWaterPipe.PostBeginPlay: vect(0, 0, 3)")]
    private const float PuddleRaise = 3f;

    [Provenance(ProvenanceKind.Decoded, "SEffectWaterPipe.PostBeginPlay: SetTimer(4.0, false)")]
    private const float PuddleGrowTime = 4f;

    [Provenance(ProvenanceKind.Decoded, "SFlaque.Grow: variation = 0.1")]
    private const float PuddleGrowRate = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "SFlaque.Reduce: variation = -0.01")]
    private const float PuddleShrinkRate = -0.01f;

    [Provenance(ProvenanceKind.Decoded, "SFlaque.Tick: the scale is applied while X < 1.5; Destroy once X < 0.1")]
    private const float PuddleMaxScale = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "SFlaque.Tick: Destroy once X < 0.1")]
    private const float PuddleMinScale = 0.1f;

    private readonly List<Puddle> _puddles = [];
    private readonly HashSet<Actor> _placedPuddles = new(ReferenceEqualityComparer.Instance);

    /// <summary>A puddle of the view (SFlaque): DrawScale3D starts at (0.1, 0.1, 1), as the class default.</summary>
    private sealed class Puddle(FVector location, FRotator rotation)
    {
        public FVector Location { get; } = location;

        public FRotator Rotation { get; } = rotation;

        public FVector Scale { get; set; } = new(0.1f, 0.1f, 1f);

        public float Variation { get; set; } = PuddleGrowRate;

        public float Age { get; set; }

        public bool Reduced { get; set; }

        public required MeshInstance3D Node { get; init; }

        public void Place() => Node.Transform = UeSpace.TransformToGodot(Location, Rotation, Scale, default);
    }

    /// <summary>Each frame: steps and draws the particles, places the meshes and lights.</summary>
    public void Draw(float dt, FVector cameraLocation, FRotator cameraRotation, int rendMap)
    {
        dt = Math.Clamp(dt, 0f, 0.1f);
        ParticleCamera camera = ParticleCamera.From(cameraLocation, cameraRotation);
        int total = 0;
        foreach (Tracked t in _tracked.Values)
        {
            Actor a = t.Actor;
            if (t.View is { } view)
            {
                FVector at = PlaceOf(a);
                t.Place = at;
                if (a is SFlicFlocEmitter flicFloc)
                {
                    ApplyFlicFloc(t, flicFloc, view);
                }

                view.Sim.Step(dt, at, a.Rotation, a.Velocity);
                view.Root.Visible = !a.bHidden;
                view.Draw(new ParticleOwner(at, a.Rotation, a.Velocity, view.Sim.GlobalOffset), camera);
                total += view.Drawn;
                Peak(t.Class, view.Drawn);
            }

            if (t.Mesh is { } mesh)
            {
                FVector scale = a.DrawScale3D * a.DrawScale;
                bool onBone = BoneMeshFrame(a, scale, out Transform3D boneFrame, out bool pawnShown);
                mesh.Transform = onBone ? boneFrame : UeSpace.TransformToGodot(a.Location, a.Rotation, scale, a.PrePivot);
                bool visionOnly = a.bHideInNormalVision;
                mesh.Visible = !a.bHidden && (!visionOnly || rendMap is 36 or 38) && (!onBone || pawnShown);
                Vector4 vision = GameplayObjects.VisionOf(a);
                if (vision != t.Vision)
                {
                    t.Vision = vision;
                    mesh.SetInstanceShaderParameter(GameplayObjects.VisionActorParameter, vision);
                }

                ApplySkins(t, a, mesh);
            }

            if (t.Light is { } light)
            {
                PlaceLight(light, a);
                if (light.Visible)
                {
                    _worldLights.Add(WorldLightOf(a, light.LightEnergy * 255f));
                }
            }
        }

        // The flash lights light the world as the map's dynamic lights do (the template's dynamic term), and the
        // actors (pawns, weapons, props) through their light sets from the next frame.
        _worldLights.AddRange(ExtraLights);
        ExtraLights.Clear();
        Map?.WorldLights.SetSpawned(_worldLights);
        Map?.ActorLighting.SetSpawned(_worldLights);
        WorldLit = Map?.WorldLights.SpawnedCount ?? 0;
        PeakWorldLit = Math.Max(PeakWorldLit, WorldLit);
        _worldLights.Clear();

        for (int i = _loose.Count - 1; i >= 0; i--)
        {
            Loose l = _loose[i];
            l.Age += dt;
            if (l.Follow is { bDeleteMe: false } f)
            {
                l.Location = f.Location;
                l.Rotation = f.Rotation;
            }

            if (l.Age >= l.Life || (l.AutoDestroy && l.View.Sim.AllDead && l.Age > 0.1f))
            {
                l.View.Free();
                _loose.RemoveAt(i);
                continue;
            }

            l.View.Sim.Step(dt, l.Location, l.Rotation, default);
            l.View.Draw(new ParticleOwner(l.Location, l.Rotation, default, l.View.Sim.GlobalOffset), camera);
            total += l.View.Drawn;
            Peak(l.Class, l.View.Drawn);
        }

        Particles = total;
        TickPuddles(dt);
    }

    private void Peak(string cls, int drawn)
    {
        if (drawn > PeakParticles.GetValueOrDefault(cls))
        {
            PeakParticles[cls] = drawn;
        }
    }

    private ParticleSystemView? Start(FxClass fx, string name, Func<int, string, JsonElement?>? change = null)
    {
        uint seed = _seed++ * 2246822519u;
        ParticleSystemSim? sim = change is null ? Library!.CreateSystem(fx.Path, seed) : Library!.CreateSystem(fx.Path, seed, change);
        if (sim is null)
        {
            return null;
        }

        sim.Trace = _trace;
        Started[ShortName(fx.Path)] = Started.GetValueOrDefault(ShortName(fx.Path)) + 1;
        return new ParticleSystemView(sim, Library, this, name, fx.Float("HeatOpacity", RenderK.DefaultHeatOpacity));
    }

    /// <summary>
    /// A damaged gameplay object's effect (SGamePlayObject.SpawnDamageEmitter): the class has no sub-emitters of its
    /// own; the object's SpawnableObjects entry inserts the map's sub-emitter, made to reset on its trigger and not to
    /// be switched by it, its start box scaled by the object's DrawScale3D for a Scaled entry, then triggered. The
    /// map's own textures and meshes draw it.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SGamePlayObject.SpawnDamageEmitter: ResetOnTrigger = true, TriggerDisabled = false, StartLocationRange *= DrawScale3D when Scaled, Trigger(), Emitters[0] = SpawnParticle")]
    [Provenance(ProvenanceKind.Estimated, "The script scales the map's shared sub-emitter in place, so in the original each later spawn of a Scaled entry compounds the scale; the port scales once")]
    private ParticleSystemView? StartSpawnable(SSpawnableEmitter a, FxClass fx)
    {
        if (a.Emitters.Count == 0 || a.Emitters[0].Path is not { } path)
        {
            return null;
        }

        if (Map?.Emitters is not { } mapFx || !mapFx.SpawnParticles.TryGetValue(path, out FxSubEmitter? sub))
        {
            Missing.Add(path);
            return null;
        }

        FVector? scale = null;
        if (a.Owner is SGamePlayObject gpo)
        {
            foreach (FSpawnInfo info in gpo.SpawnableObjects)
            {
                if (ReferenceEquals(info.SpawnedActor, a) && info.Scaled)
                {
                    scale = gpo.DrawScale3D;
                }
            }
        }

        JsonElement? Get(string name)
        {
            switch (name)
            {
                case "ResetOnTrigger":
                    return JsonTrue;
                case "TriggerDisabled":
                    return JsonFalse;
                case "StartLocationRange" when scale is { } s:
                    FxRangeVector r = sub.TryGet(name, out JsonElement v) ? FxRangeVector.From(v, default) : default;
                    return ScaledRange(r, s);
            }

            return sub.TryGet(name, out JsonElement value) ? value : null;
        }

        ParticleParams p = ParticleParams.From(sub.Class, sub.ObjectPath, Get, sub.Texture);
        if (p.MaxParticles <= 0)
        {
            return null;
        }

        var settings = new ParticleSystemParams(a.AutoDestroy, a.AutoReset, default, default);
        var sim = new ParticleSystemSim([p], settings, _seed++ * 2246822519u) { Trace = _trace };
        sim.Trigger();
        string name = a.GetUcClass().Name;
        Started[name] = Started.GetValueOrDefault(name) + 1;
        return new ParticleSystemView(sim, mapFx, this, name, fx.Float("HeatOpacity", RenderK.DefaultHeatOpacity));
    }

    /// <summary>
    /// SSprinklerEmitter.PreBeginPlay: the splash sub-emitter (Emitters[1]) starts its particles at the floor's height
    /// below the emitter (StartLocationRange.Z.Min = Max = the floor trace's height), its X and Y spread kept.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SSprinklerEmitter.PreBeginPlay: Emitters[1].StartLocationRange.Z.Min = Max = HitLocation.Z - Location.Z")]
    private static Func<int, string, JsonElement?> SprinklerSplash(FxClass fx, float height)
    {
        FxRangeVector r = fx.Sources.Count > 1 && fx.Sources[1].TryGet("StartLocationRange", out JsonElement v)
            ? FxRangeVector.From(v, default)
            : default;
        JsonElement range = ScaledRange(r with { Z = new FxRange(height, height) }, new FVector(1f, 1f, 1f));
        return (i, n) => i == 1 && n == "StartLocationRange" ? range : null;
    }

    private static readonly JsonElement JsonTrue =JsonDocument.Parse("true").RootElement.Clone();
    private static readonly JsonElement JsonFalse = JsonDocument.Parse("false").RootElement.Clone();

    private static JsonElement ScaledRange(FxRangeVector r, FVector s)
    {
        string Range(FxRange x, float k) => string.Create(CultureInfo.InvariantCulture, $"{{\"Min\":{x.Min * k},\"Max\":{x.Max * k}}}");
        return JsonDocument.Parse($"{{\"X\":{Range(r.X, s.X)},\"Y\":{Range(r.Y, s.Y)},\"Z\":{Range(r.Z, s.Z)}}}").RootElement.Clone();
    }

    /// <summary>The launch trail script spawns for a thrown grenade on every drawing machine (not for a quick throw).</summary>
    [Provenance(ProvenanceKind.Decoded, "SProjSmoke / SProjChaffe / SProjFlashBang / SProjPhospho.PostBeginPlay: SSmokeLaunchEmitter unless QuickLaunch; SProjFrag.PostBeginPlay: SFragLaunchEmitter (Level.NetMode != NM_DedicatedServer)")]
    private Loose? TrailFor(Actor a)
    {
        string? cls = a switch
        {
            SProjFrag => "SBase.SFragLaunchEmitter",
            SProjGrenade { QuickLaunch: true } => null,
            SProjSmoke or SProjChaffe or SProjFlashBang or SProjPhospho => "SBase.SSmokeLaunchEmitter",
            _ => null,
        };
        if (cls is null || Spawn(cls, a.Location, a.Rotation, a) is null)
        {
            return null;
        }

        return _loose[^1];
    }

    private MeshInstance3D? MeshFor(Actor a, FxLibrary lib)
    {
        // The taser's beam (bIgnoreEchelonLighting, additive glow) is drawn unlit.
        bool lit = !a.bUnlit && a is not STazerEmitter;
        if (a.StaticMesh.IsNone || lib.Mesh(a.StaticMesh.Path, lit) is not { } mesh)
        {
            Missing.Add(a.StaticMesh.Path ?? a.GetUcClass().Path);
            return null;
        }

        var node = new MeshInstance3D
        {
            Name = a.GetUcClass().Name,
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Transform = UeSpace.TransformToGodot(a.Location, a.Rotation, a.DrawScale3D * a.DrawScale, a.PrePivot),
        };
        AddChild(node);
        if (lit && Map is { } map)
        {
            Aabb box = node.Transform * mesh.GetAabb();
            float radius = UeSpace.UnitsPerMetre * box.Size.Length() * 0.5f;
            ActorLighting.Apply(node, map.LightActor(a.Location, MathF.Max(radius, 8f), a.AmbientGlow));
        }

        string name = a.GetUcClass().Name;
        MeshesShown[name] = MeshesShown.GetValueOrDefault(name) + 1;
        return node;
    }

    /// <summary>
    /// The actor's Skins over its mesh's surfaces as script sets them at run time (SMineProx swaps LightOn and LightOff
    /// into Skins[1] as it arms, is chaffed and comes back): each slot whose skin is an additive material of the
    /// effects' texture table draws that texture added, unlit; other slots keep the mesh's own material.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Only additive skins are drawn (the only run-time skins of the effect meshes, SMineProx's proxy_mine_On and proxy_mine_Off_final, are additive Shaders); drawn as their diffuse texture")]
    private void ApplySkins(Tracked t, Actor a, MeshInstance3D mesh)
    {
        int slots = Math.Min(a.Skins.Count, mesh.GetSurfaceOverrideMaterialCount());
        if (t.Skins.Length != slots)
        {
            t.Skins = new string?[slots];
        }

        for (int i = 0; i < slots; i++)
        {
            string? path = a.Skins[i].IsNone ? null : a.Skins[i].Path;
            if (string.Equals(path, t.Skins[i], StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            t.Skins[i] = path;
            mesh.SetSurfaceOverrideMaterial(i, SkinMaterial(path));
        }
    }

    private readonly Dictionary<string, Material?> _skinMaterials = new(StringComparer.OrdinalIgnoreCase);

    private Material? SkinMaterial(string? path)
    {
        if (path is null || Library is not { Available: true } lib || !lib.TextureAdditive(path))
        {
            return null;
        }

        if (!_skinMaterials.TryGetValue(path, out Material? material))
        {
            material = lib.Texture(lib.TexturePng(path)) is { } texture
                ? new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    AlbedoTexture = texture,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                }
                : null;
            _skinMaterials[path] = material;
        }

        return material;
    }

    private OmniLight3D LightFor(Actor a)
    {
        var light = new OmniLight3D { Name = a.GetUcClass().Name, ShadowEnabled = false };
        AddChild(light);
        PlaceLight(light, a);
        string name = a.GetUcClass().Name;
        Started[name] = Started.GetValueOrDefault(name) + 1;
        return light;
    }

    /// <summary>
    /// A flash light: LightBrightness, hue and saturation at the actor, reach 25 (LightRadius + 1) uu. The frag's
    /// flash fades its brightness from 255 to 0 over fTempsDisparition (its native tick).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SFlashLight_FragGrenade tick (game 0x10abd280): LightBrightness = 255 - (TimeSeconds - fDateDepart) / fTempsDisparition x 255")]
    [Provenance(ProvenanceKind.Estimated, "The world's own light model is not fed by these lights; a Godot omni light of the hue stands in, energy brightness / 255")]
    private static void PlaceLight(OmniLight3D light, Actor a)
    {
        light.GlobalPosition = UeSpace.ToGodot(a.Location);
        float brightness = a.LightBrightness;
        if (a is SFlashLight_FragGrenade frag && frag.fTempsDisparition > 0f)
        {
            brightness = 255f - ((a.Level.TimeSeconds - frag.fDateDepart) / frag.fTempsDisparition * 255f);
        }

        // Light actors are bHidden (Engine.Light defaults) and still light their surroundings.
        light.Visible = (int)a.LightType != 0 && brightness > 0f;
        light.LightColor = Color.FromHsv(a.LightHue / 255f, a.LightSaturation / 255f, 1f);
        light.LightEnergy = Math.Clamp(brightness / 255f, 0f, 8f);
        light.OmniRange = UeSpace.LengthToGodot(RenderK.LightRadiusUnits * (a.LightRadius + 1f));
    }

    /// <summary>
    /// A spawned light as a dynamic world light: the actor's light properties at <paramref name="brightness"/>, a dynamic
    /// light applied to the world (UE2 lights a spawned bDynamicLight actor's surroundings each frame).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "The world falloff, gain and radius are those of the map's dynamic lights (docs/RENDERING.md#dynamic-lights)")]
    private LightEntry WorldLightOf(Actor a, float brightness) => new(
        a.GetUcClass().Name, a.Location, a.Rotation, brightness, a.LightHue, a.LightSaturation, a.LightRadius, a.LightCone,
        (int)a.LightEffect, 1, true, true, true, true, null, a.LightPeriod, a.LightPhase, a.bAffectOwnZoneOnly)
    {
        // SFlashLight is bAffectOwnZoneOnly: it lights only the surfaces of the zone it is in.
        ZoneIndex = Map?.ZoneIndexAt(a.Location) ?? -1,
    };

    /// <summary>
    /// A static mesh attached to a pawn's bone (the backpack on 'B Spine2'): the drawn bone's frame turned by the
    /// actor's RelativeRotation and moved by its RelativeLocation, then its scale and PrePivot; false when it is not
    /// attached or the bone is not drawn. <paramref name="pawnShown"/> is false while the pawn's view is hidden (the
    /// own pawn in first person).
    /// </summary>
    private bool BoneMeshFrame(Actor a, FVector scale, out Transform3D frame, out bool pawnShown)
    {
        frame = default;
        pawnShown = false;
        if (a.AttachmentBone.IsNone || a.Base is not SBasePawn pawn || Pawns is null || !Pawns.TryGetBone(pawn, a.AttachmentBone, out Transform3D bone))
        {
            return false;
        }

        Basis boneBasis = bone.Basis.Orthonormalized();
        Basis basis = boneBasis * UeSpace.RotationToGodot(a.RelativeRotation) * Basis.FromScale(UeSpace.DirectionToGodot(scale));
        Vector3 origin = bone.Origin + (boneBasis * UeSpace.ToGodot(a.RelativeLocation)) - (basis * UeSpace.ToGodot(a.PrePivot));
        frame = new Transform3D(basis, origin);
        pawnShown = Pawns.ViewOf(pawn)?.Visible ?? false;
        return true;
    }

    /// <summary>
    /// Where an effect is drawn: an actor attached to a pawn's bone (AttachToBone: Base the pawn, AttachmentBone set)
    /// follows that bone of the drawn skeleton (the simulation has none), moved by its RelativeLocation in the bone's
    /// frame; others their own Location.
    /// </summary>
    private FVector PlaceOf(Actor a)
    {
        if (!a.AttachmentBone.IsNone && (a.Base ?? a.Owner) is SBasePawn pawn && Pawns is not null &&
            Pawns.TryGetBone(pawn, a.AttachmentBone, out Transform3D bone))
        {
            if (a.RelativeLocation.IsZero())
            {
                return UeSpace.ToUe(bone.Origin);
            }

            return UeSpace.ToUe(bone.Origin + (bone.Basis.Orthonormalized() * UeSpace.ToGodot(a.RelativeLocation)));
        }

        if (!a.AttachmentBone.IsNone && a.Base is SWeaponDefense { bDeleteMe: false } weapon)
        {
            return WeaponBonePlace(weapon, a);
        }

        return a.Location;
    }

    /// <summary>
    /// An effect on a merc gun's bone (the barrel smoke on FX): the simulation's place of that bone, the actor's
    /// RelativeLocation taken along the bone's axes.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "The gun's FX bone is the simulation's estimate (GetBoneCoords), not the drawn first-person gun's bone")]
    private static FVector WeaponBonePlace(SWeaponDefense weapon, Actor a)
    {
        FCoords c = OpenScct.Game.Combat.CombatServices.Ports(weapon).BoneCoords(weapon, a.AttachmentBone);
        FVector r = a.RelativeLocation;
        return c.Origin + (c.XAxis * r.X) + (c.YAxis * r.Y) + (c.ZAxis * r.Z);
    }

    private static string ShortName(string cls) => cls[(cls.LastIndexOf('.') + 1)..];

    /// <summary>The particle collision trace on the client's collision (level and world geometry, flags 0x84).</summary>
    private static ParticleTrace? TraceOf(Level level)
    {
        if (OpenScct.Game.Equipment.GadgetWorld.Collision(level.Info) is not { } world)
        {
            return null;
        }

        return (FVector start, FVector end, out FVector hit, out FVector normal) =>
        {
            if (world.LineCheck(out FCheckResult r, null, end, start, FVector.Zero, (ETraceFlags)ParticleK.TraceFlags) && r.bHit)
            {
                hit = r.Location;
                normal = r.Normal;
                return true;
            }

            hit = default;
            normal = default;
            return false;
        };
    }

    private sealed class Tracked(Actor actor, string cls)
    {
        public Actor Actor { get; } = actor;

        public string Class { get; } = cls;

        public ParticleSystemView? View { get; set; }

        public MeshInstance3D? Mesh { get; set; }

        public OmniLight3D? Light { get; set; }

        public Loose? Trail { get; set; }

        /// <summary>The skin path last put on each surface of <see cref="Mesh"/> (null: the mesh's own material).</summary>
        public string?[] Skins { get; set; } = [];

        /// <summary>The vision values last written to <see cref="Mesh"/> (NaN: none yet).</summary>
        public Vector4 Vision { get; set; } = new(float.NaN, 0f, 0f, 0f);

        /// <summary>Where the particle system was placed this frame.</summary>
        public FVector Place { get; set; }

        /// <summary>An SFlicFlocEmitter's restarts and triggers already applied to the drawn copy.</summary>
        public int FlicSplashStarts { get; set; }

        public int FlicDripStarts { get; set; }

        public int FlicPuffTriggers { get; set; }
    }

    private sealed class Loose(ParticleSystemView view, string cls, FVector location, FRotator rotation, float life, bool autoDestroy)
    {
        public ParticleSystemView View { get; } = view;

        public string Class { get; } = cls;

        public FVector Location { get; set; } = location;

        public FRotator Rotation { get; set; } = rotation;

        public float Life { get; set; } = life;

        public bool AutoDestroy { get; } = autoDestroy;

        public float Age { get; set; }

        public Actor? Follow { get; set; }
    }
}
