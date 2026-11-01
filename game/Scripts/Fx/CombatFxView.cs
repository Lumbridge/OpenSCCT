using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Play.Session;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Particles;
using OpenScctGame.Views;

namespace OpenScctGame.Fx;

/// <summary>
/// What a shot looks like on this machine: the muzzle flashes (the Shocker's SMuzzleSticky, the third-person rifle's
/// ClientFireMuzzle, the first-person rifle's SpawnSFX), the rifle tracers (SFireTrace actors), the impact marks of
/// the level's <see cref="ImpactTable"/> (spec/equipment/impact-manager.md) and their spark bursts. It reads the
/// client world after each tick and never writes to it; marks and the local merc's shots are read from the state
/// (the table's slots, the clip) since a call received or a key pressed between two ticks raises its view events
/// outside the tick.
/// <para>
/// With the converted effects (shared/fx, the content step <c>fx</c>, docs/EFFECTS.md) each is drawn with the
/// original's assets: the muzzle flash meshes of SEffectStatic (SMuzzleGenerique, sMuzzleCanon, SMuzzleSticky) for
/// their 0.1 s, the SDouilleEmitter shell casing, the impact classes' decal textures and their spark emitters
/// (SSparkBeton, SMetalSpark, SSparkShocker...) run as particles. The gameplay emitters, thrown gadgets and flash
/// lights are <see cref="Effects"/>. Without the converted effects the stand-ins remain: a soft flash, a streak, a
/// dark spot and a generic spark burst with the original's timing, colour and place.
/// </para>
/// </summary>
public partial class CombatFxView : Node3D
{
    /// <summary>The right hand bone the pistol and the third-person rifle hang from.</summary>
    public static readonly Name BoneRightHand = new("B R Hand");

    /// <summary>The muzzle bone of the first-person rifle.</summary>
    public static readonly Name BoneFx = new("FX");

    private static readonly Name SpawnSFX = new("SpawnSFX");

    private readonly List<Flash> _flashes = new();
    private readonly List<Burst> _bursts = new();
    private readonly Dictionary<SFireTrace, Streak> _tracers = new(ReferenceEqualityComparer.Instance);
    private readonly List<SFireTrace> _gone = new();
    private readonly List<(MeshInstance3D Node, int Serial)> _marks = new();
    private readonly List<PendingFlash> _pending = new();
    private Texture2D? _flashTexture;
    private StandardMaterial3D? _bulletMark;
    private StandardMaterial3D? _shockMark;
    private StandardMaterial3D? _tracerMaterial;
    private bool _loaded;
    private EffectViews? _effects;
    private FxLibrary? _library;
    private readonly Dictionary<string, StandardMaterial3D?> _decals = new(StringComparer.Ordinal);
    private SWeaponDefense? _rifle;
    private int _clip;
    private readonly Dictionary<SCamNetwork, CamnetDisabledView> _disabledCameras = new();
    private readonly List<SCamNetwork> _recoveredCameras = new();

    public CombatFxView()
    {
        Name = "CombatFx";
    }

    /// <summary>The pawn views, for the hand bones the flashes start from.</summary>
    public PawnViews? Pawns { get; set; }

    /// <summary>The first-person rifle, for its muzzle bone.</summary>
    public FirstPersonView? FirstPerson { get; set; }

    /// <summary>The shared content root (for the effect assets once they are converted).</summary>
    public string? SharedRoot { get; set; }

    /// <summary>The gameplay effects (particles of the effect classes, thrown gadgets, flash lights).</summary>
    public EffectViews? Effects => _effects;

    /// <summary>Muzzle flash meshes of the original drawn so far.</summary>
    public int MuzzleMeshes { get; private set; }

    /// <summary>Shell casing emitters started so far.</summary>
    public int Casings { get; private set; }

    /// <summary>The last impact mark was drawn with its class's converted decal texture.</summary>
    public bool LastMarkDecal { get; private set; }

    /// <summary>Muzzle flashes shown so far.</summary>
    public int MuzzleFlashes { get; private set; }

    /// <summary>Muzzle flashes by kind (SMuzzleSticky, ClientFireMuzzle, SpawnSFX).</summary>
    public Dictionary<string, int> FlashesByKind { get; } = new(StringComparer.Ordinal);

    /// <summary>Tracers shown so far.</summary>
    public int Tracers { get; private set; }

    /// <summary>Impact marks shown so far.</summary>
    public int Marks { get; private set; }

    /// <summary>Spark bursts shown so far.</summary>
    public int Sparks { get; private set; }

    /// <summary>Marks on screen now.</summary>
    public int LiveMarks
    {
        get
        {
            int n = 0;
            foreach ((MeshInstance3D node, _) in _marks)
            {
                if (node.Visible)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>Flashes alight now.</summary>
    public int LiveFlashes => _flashes.Count;

    /// <summary>Where the last flash was drawn (Godot space), for checks.</summary>
    public Vector3 LastFlashAt { get; private set; }

    /// <summary>Where the last mark was drawn (UE space), for checks.</summary>
    public FVector LastMarkAt { get; private set; }

    /// <summary>After a client tick: takes the tick's view events and the new tracers and marks.</summary>
    public void OnTick(PlaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureLoaded();
        Level level = session.Client.World.Level;
        // The pistol's and the third-person rifle's flashes are raised inside the tick (their muzzle counts); they
        // are placed at draw time, once the pawn's pose for this tick is drawn.
        foreach (SimEvent e in session.Client.World.Events.Current)
        {
            if (e.Kind == SimEventKind.Notify && (e.What == CombatFx.MuzzleSticky || e.What == CombatFx.ThirdPersonMuzzle))
            {
                _pending.Add(new PendingFlash(e.What, level.FindActor(e.ActorId)));
            }
        }

        // The local merc's own shots: the first one of a press runs from the Fire command before the tick (its SpawnSFX
        // notify is dropped with the tick's start), so each round leaving the clip is a flash.
        if (session.LocalPawn?.MyWeapon is SWeaponDefense rifle)
        {
            if (ReferenceEquals(rifle, _rifle) && rifle.ClipAmmo < _clip)
            {
                for (int i = rifle.ClipAmmo; i < _clip; i++)
                {
                    _pending.Add(new PendingFlash(SpawnSFX, rifle));
                }
            }

            _rifle = rifle;
            _clip = rifle.ClipAmmo;
        }

        TrackTracers(level);
        TrackMarks(level);
        TrackProjections(level);
        TrackDisabledCameras(level);
        _effects?.OnTick(level);
    }

    internal int DisabledCameraCount => _disabledCameras.Count;

    internal void TrackDisabledCameras(Level level)
    {
        _recoveredCameras.Clear();
        foreach (SCamNetwork camera in _disabledCameras.Keys)
        {
            if (camera.bDeleteMe || !camera.IsShotDisabled || !ReferenceEquals(camera.XLevel, level))
            {
                _recoveredCameras.Add(camera);
            }
        }

        foreach (SCamNetwork camera in _recoveredCameras)
        {
            _disabledCameras[camera].Visible = false;
            _disabledCameras[camera].QueueFree();
            _disabledCameras.Remove(camera);
        }
        foreach (SCamNetwork camera in level.AllActors<SCamNetwork>())
        {
            if (!camera.IsShotDisabled || camera.bHidden || camera.bDeleteMe || _disabledCameras.ContainsKey(camera))
            {
                continue;
            }

            var cue = new CamnetDisabledView();
            AddChild(cue);
            cue.SetCamera(camera);
            _disabledCameras.Add(camera, cue);
        }
    }

    /// <summary>Each frame: places the new flashes, fades the flashes and bursts, flies the tracers.</summary>
    public void Draw(float dt, FVector cameraLocation, FRotator cameraRotation, SPlayerController? viewer)
    {
        foreach (CamnetDisabledView cue in _disabledCameras.Values) cue.Update(dt);
        foreach (PendingFlash p in _pending)
        {
            AddFlash(p, cameraLocation, cameraRotation, viewer);
        }

        _pending.Clear();
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            Flash f = _flashes[i];
            f.Age += dt;
            if (f.Age >= f.Life || (f.FirstPerson && viewer is { bBinocularVision: true }))
            {
                f.Node.Visible = false;
                f.Node.QueueFree();
                _flashes.RemoveAt(i);
                continue;
            }

            float k = 1f - (f.Age / f.Life);
            f.Light.LightEnergy = f.Energy * k;
            f.Quad.Transparency = 1f - k;
        }

        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            Burst b = _bursts[i];
            b.Age += dt;
            if (b.Age > b.Life)
            {
                b.Node.QueueFree();
                _bursts.RemoveAt(i);
            }
        }

        foreach ((SFireTrace trace, Streak s) in _tracers)
        {
            s.Age += dt;
            float travelled = MathF.Min(s.Age * s.Speed, s.Length);
            FVector at = s.Start + (s.Direction * travelled);
            s.Node.GlobalPosition = UeSpace.ToGodot(at);
            s.Node.Visible = travelled < s.Length && s.Age < s.Life;
        }

        if (_effects is not null)
        {
            foreach (Flash f in _flashes)
            {
                foreach (LightEntry l in f.WorldLights)
                {
                    _effects.ExtraLights.Add(l);
                }
            }
        }

        _effects?.Draw(dt, cameraLocation, cameraRotation, viewer?.RendMap ?? 0);
    }

    // ---- muzzle flashes ------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SMuzzleFlash Lifetime 0.1; SMuzzleGenerique / sMuzzleCanon LightHue 40, LightSaturation 190; SMuzzleSticky LightHue 148, LightSaturation 127, LightBrightness 255")]
    [Provenance(ProvenanceKind.Estimated, "The flash place: the right hand bone 24 uu along the aim, or the first-person rifle's FX bone (the meshes' FX bones are not drawn)")]
    private void AddFlash(PendingFlash p, FVector cameraLocation, FRotator cameraRotation, SPlayerController? viewer)
    {
        bool sticky = p.Kind == CombatFx.MuzzleSticky;
        Vector3 at;
        Vector3 forward;
        if (p.Kind == SpawnSFX)
        {
            // The local merc's first-person rifle.
            if (viewer is { bBinocularVision: true })
            {
                // The scope hides the rifle; its muzzle flash must not cover the sight either.
                return;
            }

            cameraRotation.GetAxes(out FVector x, out FVector y, out FVector z);
            FVector muzzle = cameraLocation + (x * 60f) + (y * 9f) - (z * 9f);
            at = UeSpace.ToGodot(muzzle);
            forward = UeSpace.DirectionToGodot(x);
            if (FirstPerson is { Shown: true, Model: { } model } && model.Skeleton.FindBone(BoneFx.ToString()) is var fx and >= 0)
            {
                at = (model.Skeleton.GlobalTransform * model.Skeleton.GetBoneGlobalPose(fx)).Origin;
            }
        }
        else
        {
            Pawn? pawn = p.Source?.Instigator ?? p.Source as Pawn;
            if (pawn is null)
            {
                return;
            }

            FRotator aim = pawn.Controller is SPlayerController pc ? pc.MyDesiredRotation : pawn.Rotation;
            if (pawn.Controller is SPlayerController { } own && ReferenceEquals(own, viewer))
            {
                aim = own.Rotation;
            }

            aim.GetAxes(out FVector x, out _, out _);
            forward = UeSpace.DirectionToGodot(x);
            at = Pawns is not null && Pawns.TryGetBone(pawn as SBasePawn, BoneRightHand, out Transform3D hand)
                ? hand.Origin + (forward * UeSpace.LengthToGodot(24f))
                : UeSpace.ToGodot(pawn.Location + new FVector(0f, 0f, pawn.EyeHeight * 0.6f) + (x * 40f));
        }

        Color colour = sticky ? FromHue(148, 127) : FromHue(40, 190);
        var node = new Node3D { Name = "MuzzleFlash" };
        AddChild(node);
        node.GlobalPosition = at;
        bool meshes = AddMuzzleMeshes(node, p.Kind, p.Source, at, forward);
        var light = new OmniLight3D
        {
            LightColor = colour,
            LightEnergy = sticky ? 2.5f : 4f,
            OmniRange = UeSpace.LengthToGodot(sticky ? 220f : 320f),
            ShadowEnabled = false,
        };
        node.AddChild(light);
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One * UeSpace.LengthToGodot(sticky ? 14f : 22f) },
            MaterialOverride = FlashMaterial(colour),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = !meshes,
        };
        node.AddChild(quad);
        _flashes.Add(new Flash(node, light, quad, light.LightEnergy, 0.1f)
        {
            FirstPerson = p.Kind == SpawnSFX,
            WorldLights = MuzzleLights(p.Kind, UeSpace.ToUe(at)),
        });
        MuzzleFlashes++;
        LastFlashAt = at;
        string kind = p.Kind.ToString();
        FlashesByKind[kind] = FlashesByKind.GetValueOrDefault(kind) + 1;
    }

    /// <summary>
    /// The original's muzzle meshes for one shot, under <paramref name="node"/> (a 0.1 s flash): the Shocker's
    /// SMuzzleSticky (draw scale 0.8 to 1.3, a random roll); the rifle's SMuzzleGenerique (draw scale 1.5 to 3.5, a
    /// random roll; the first-person one also stretched 3.5 to 5.5 along the barrel) with sMuzzleCanon, and its
    /// SDouilleEmitter casing. True when a mesh was drawn.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SMuzzleFlash / SMuzzleSticky.PostBeginPlay, SWeaponDefense.SpawnSFX, SWeaponDefenseDummy.ClientFireMuzzle")]
    [Provenance(ProvenanceKind.Estimated, "The third-person gun's FX and FX_DOUILLE bones are not drawn: the flash sits at the hand's estimate and the casing starts there")]
    private bool AddMuzzleMeshes(Node3D node, Name kind, Actor? source, Vector3 at, Vector3 forward)
    {
        if (_library is not { Available: true } lib)
        {
            return false;
        }

        bool sticky = kind == CombatFx.MuzzleSticky;
        bool firstPerson = kind == SpawnSFX;
        FVector origin = UeSpace.ToUe(at);
        FRotator aim = UeSpace.DirectionToUe(forward).Rotation();
        var rng = new FxRandom(((uint)MuzzleFlashes * 2654435761u) + 7u);
        bool any = false;
        void Add(string cls, float drawScale, FVector scale3D)
        {
            if (lib.Find(cls) is not { } fx || !fx.Assets.TryGetValue("StaticMesh", out string? path) || lib.Mesh(path) is not { } mesh)
            {
                return;
            }

            var rot = new FRotator(aim.Pitch, aim.Yaw, (int)(rng.Next() * 16000f));
            var m = new MeshInstance3D
            {
                Name = cls,
                Mesh = mesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            node.AddChild(m);
            m.GlobalTransform = UeSpace.TransformToGodot(origin, rot, scale3D * drawScale, default);
            any = true;
            MuzzleMeshes++;
        }

        if (sticky)
        {
            Add("SBase.SMuzzleSticky", 0.8f + (rng.Next() / 2f), new FVector(1f, 1f, 1f));
            return any;
        }

        bool pistol = source is MercSidearm or MercSidearmDummy;
        bool suppressed = source is MercSidearm { Kind: 0 } or MercSidearmDummy { Kind: 0 };
        bool revolver = source is MercSidearm { Kind: 2 } or MercSidearmDummy { Kind: 2 };
        if (!suppressed) Add("SBase.SMuzzleGenerique", pistol ? 0.6f : 1.5f + (rng.Next() * 2f), firstPerson ? new FVector(3.5f + (rng.Next() * 2f), 1f, 1f) : new FVector(1f, 1f, 1f));
        // The first-person Uzi and shotgun have no barrel flash; both shotguns add their spray (merc-guns.md).
        if (!pistol && !(firstPerson && source is SWeaponDefense_Uzi or SWeaponDefense_Shotgun))
        {
            Add("SBase.sMuzzleCanon", 1f, new FVector(1f, 1f, 1f));
        }

        if ((source is SWeaponDefense_Shotgun or SWeaponDefenseDummy_Shotgun) &&
            _effects?.Spawn("SBase.SShotGunEmitter", origin, aim) is { } spray && source.Instigator is { } shooter)
        {
            AddShooterVelocity(spray, shooter);
        }

        FVector casing = origin;
        if (firstPerson && FirstPerson?.Model is { } model && model.Skeleton.FindBone("FX_DOUILLE") is var bone and >= 0)
        {
            Transform3D t = model.Skeleton.GlobalTransform * model.Skeleton.GetBoneGlobalPose(bone);
            casing = UeSpace.ToUe(t.Origin);
        }

        if (!revolver && _effects?.Spawn("SBase.SDouilleEmitter", casing, aim) is not null)
        {
            Casings++;
        }

        return any;
    }

    /// <summary>
    /// SShotGunEmitter.PostBeginPlay: the shooter's velocity along his own axes (each part cut to a whole number) is
    /// added to the start velocity of all three sub-emitters, so the spray keeps up with a running merc. The start
    /// velocity is in the emitter's own axes, so the shift is added before the particle is turned.
    /// </summary>
    [PortOf("uc:SBase.SShotGunEmitter:PostBeginPlay", Spec = "spec/equipment/merc-guns.md")]
    private static void AddShooterVelocity(ParticleSystemSim spray, Pawn shooter)
    {
        shooter.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        var add = new FVector((int)shooter.Velocity.Dot(x), (int)shooter.Velocity.Dot(y), (int)shooter.Velocity.Dot(z));
        if (add == FVector.Zero)
        {
            return;
        }

        int count = Math.Min(3, spray.Emitters.Count);
        for (int i = 0; i < count; i++)
        {
            ParticleSpawnHook? before = spray.Emitters[i].OnSpawn;
            spray.Emitters[i].OnSpawn = (ref Particle q) =>
            {
                q.Velocity += add;
                before?.Invoke(ref q);
            };
        }
    }

    private StandardMaterial3D FlashMaterial(Color colour) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        AlbedoColor = colour.Lightened(0.4f),
        AlbedoTexture = _flashTexture,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        NoDepthTest = false,
    };

    // ---- tracers ---------------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SFireTrace FTSpeed 7000, LifeSpan 2, DrawScale3D (10, 3, 3), unlit")]
    [Provenance(ProvenanceKind.Estimated, "The streak's size: the balle_TR mesh is not converted; 120 x 1.5 uu stands in")]
    private void TrackTracers(Level level)
    {
        _gone.Clear();
        foreach (SFireTrace t in _tracers.Keys)
        {
            if (t.bDeleteMe)
            {
                _gone.Add(t);
            }
        }

        foreach (SFireTrace t in _gone)
        {
            _tracers[t].Node.QueueFree();
            _tracers.Remove(t);
        }

        foreach (SFireTrace t in level.AllActors<SFireTrace>())
        {
            if (t.bDeleteMe || _tracers.ContainsKey(t) || t.ImpactLoc.IsZero())
            {
                continue;
            }

            FVector start = t.Location;
            FVector to = t.ImpactLoc - start;
            float length = to.Size();
            if (length < 1f)
            {
                continue;
            }

            FVector dir = to * (1f / length);
            var node = new MeshInstance3D
            {
                Name = "Tracer",
                Mesh = new BoxMesh { Size = new Vector3(UeSpace.LengthToGodot(1.5f), UeSpace.LengthToGodot(1.5f), UeSpace.LengthToGodot(120f)) },
                MaterialOverride = _tracerMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(node);
            node.GlobalPosition = UeSpace.ToGodot(start);
            Vector3 g = UeSpace.DirectionToGodot(dir);
            Vector3 up = MathF.Abs(g.Y) > 0.99f ? Vector3.Right : Vector3.Up;
            node.LookAt(node.GlobalPosition + g, up);
            _tracers[t] = new Streak(node, start, dir, length, t.FTSpeed > 0f ? t.FTSpeed : 7000f, 2f);
            Tracers++;
        }
    }

    // ---- impact marks ----------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SImpactManager: 50 marks, 15 s, decal at the hit facing out of the surface")]
    [Provenance(ProvenanceKind.Estimated, "The decal textures (sfx.Impacts.decal_*, Burn_sticky) are not converted: a 12 uu dark spot, or a blue-black scorch for the Shocker")]
    private void TrackMarks(Level level)
    {
        if (level.GetService<ImpactTable>() is not { } table)
        {
            return;
        }

        IReadOnlyList<ImpactMark> slots = table.Slots;
        while (_marks.Count < slots.Count)
        {
            var node = new MeshInstance3D
            {
                Name = "ImpactMark",
                Mesh = new QuadMesh { Size = Vector2.One * UeSpace.LengthToGodot(12f) },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(node);
            _marks.Add((node, 0));
        }

        for (int i = 0; i < slots.Count; i++)
        {
            ImpactMark m = slots[i];
            (MeshInstance3D node, int serial) = _marks[i];
            if (!m.Live)
            {
                node.Visible = false;
                _decalAttachments.Remove(node);
                continue;
            }

            if (m.Serial == serial)
            {
                UpdateDecalAttachment(node);
                node.Visible = m.DecalShown;
                continue;
            }

            _marks[i] = (node, m.Serial);
            bool shock = m.Type.ToString().Contains("Shocker", StringComparison.Ordinal);
            StandardMaterial3D? decal = DecalFor(m.Type.ToString());
            LastMarkDecal = decal is not null;
            node.MaterialOverride = decal ?? (shock ? _shockMark : _bulletMark);
            m.Rotation.GetAxes(out FVector x, out _, out _);
            FVector at = m.Location - (x * (m.Type.ToString().Contains("Shocker", StringComparison.Ordinal) ? 9.5f : 0f)) + (x * 0.4f);
            node.GlobalPosition = UeSpace.ToGodot(at);

            // The quad faces +Z; turn it to face out of the surface.
            Vector3 n = UeSpace.DirectionToGodot(x);
            Vector3 up = MathF.Abs(n.Y) > 0.99f ? Vector3.Right : Vector3.Up;
            node.LookAt(node.GlobalPosition - n, up);
            AttachDecal(node, m.Surface, m.SurfaceLocation, m.SurfaceRotation);
            node.Visible = m.DecalShown;
            Marks++;
            LastMarkAt = at;
            if (!m.Spark.IsNone)
            {
                string spark = m.Spark.Path!;
                // SImpactEffectBase.Effects: Spawn(SparkEmitter, self) at the mark, facing out of the surface.
                if (_effects?.Spawn(spark, m.Location, m.Rotation) is not null)
                {
                    Sparks++;
                }
                else
                {
                    AddBurst(new Name(spark[(spark.LastIndexOf('.') + 1)..]), at, m.Rotation);
                }
            }
        }
    }

    // ---- sparks ------------------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Estimated, "The SSpark* particle classes are not converted: 14 short-lived sparks thrown out of the surface")]
    private void AddBurst(Name kind, FVector location, FRotator rotation)
    {
        string s = kind.ToString();
        Color colour = s.Contains("Shocker", StringComparison.Ordinal) ? new Color(0.45f, 0.75f, 1f)
            : s.Contains("Water", StringComparison.Ordinal) ? new Color(0.7f, 0.8f, 0.9f)
            : s.Contains("Metal", StringComparison.Ordinal) ? new Color(1f, 0.8f, 0.4f)
            : new Color(0.75f, 0.7f, 0.6f);
        rotation.GetAxes(out FVector x, out _, out _);
        var particles = new CpuParticles3D
        {
            Name = "Sparks",
            Amount = 14,
            Lifetime = 0.35,
            OneShot = true,
            Explosiveness = 1f,
            Direction = UeSpace.DirectionToGodot(x),
            Spread = 40f,
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 4f,
            Gravity = new Vector3(0f, -9.8f, 0f),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.2f,
            Color = colour,
            Mesh = new QuadMesh { Size = Vector2.One * UeSpace.LengthToGodot(1.5f) },
            Emitting = true,
        };
        particles.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
        };
        AddChild(particles);
        particles.GlobalPosition = UeSpace.ToGodot(location);
        _bursts.Add(new Burst(particles, 1f));
        Sparks++;
    }

    // ---- materials -------------------------------------------------------------------------------------------

    /// <summary>
    /// The decal of an impact class: its ImpactMaterial texture, alpha-blended (SBulletImpact: frame buffer alpha
    /// blend), or null without the converted effects.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SImpactEffect* ImpactMaterial (sfx.Impacts.decal_*, Burn_sticky); SBulletImpact FrameBufferBlendingOp PB_AlphaBlend")]
    private StandardMaterial3D? DecalFor(string type)
    {
        if (_library is not { Available: true } lib)
        {
            return null;
        }

        if (_decals.TryGetValue(type, out StandardMaterial3D? cached))
        {
            return cached;
        }

        StandardMaterial3D? material = null;
        if (lib.Find(type) is { } fx && fx.Assets.TryGetValue("ImpactMaterial", out string? path) && lib.Texture(lib.TexturePng(path)) is { } texture)
        {
            material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoTexture = texture,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
        }

        _decals[type] = material;
        return material;
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (SharedRoot is { } shared)
        {
            _library = FxLibrary.Load(shared);
            if (_library.Available)
            {
                _library.Warm();
                _effects = new EffectViews { Library = _library, Map = Pawns?.Map, Pawns = Pawns };
                AddChild(_effects);
            }
        }

        // The converted SWeaponTex mask is the flash's opacity mask, not its colour: a soft white star stands in.
        _flashTexture = RadialTexture(new Color(1f, 1f, 1f), 0.2f);
        _bulletMark = MarkMaterial(RadialTexture(new Color(0.05f, 0.045f, 0.04f), 0.55f));
        _shockMark = MarkMaterial(RadialTexture(new Color(0.08f, 0.12f, 0.25f), 0.7f));
        _tracerMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = new Color(1f, 0.85f, 0.55f),
        };
        WarmMaterials();
    }

    /// <summary>
    /// Makes the shaders of the flash, tracer, mark and decal materials now, at load: a material's shader is made the
    /// first time it is used, which stopped the frame of a match's first shot. Materials with the same features share
    /// one shader, so one of each kind is enough.
    /// </summary>
    private void WarmMaterials()
    {
        static void Warm(Material? material)
        {
            if (material is not null)
            {
                _ = material.GetRid();
            }
        }

        Warm(_tracerMaterial);
        Warm(_bulletMark);
        Warm(_shockMark);
        Warm(FlashMaterial(FromHue(40, 190)));
        if (_library is { Available: true } lib)
        {
            foreach (KeyValuePair<string, FxClass> c in lib.Classes)
            {
                if (c.Value.Kind == "impact")
                {
                    Warm(DecalFor(c.Key));
                }
            }
        }

        // The first light made in a match also initialises Godot's light class (its method table).
        new OmniLight3D().Free();
    }

    private static StandardMaterial3D MarkMaterial(Texture2D texture) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoTexture = texture,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>A soft round spot of <paramref name="colour"/>, opaque in the middle (radius 0 to 1 of the hard core).</summary>
    private static ImageTexture RadialTexture(Color colour, float core)
    {
        const int size = 32;
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = ((x + 0.5f) / size * 2f) - 1f;
                float dy = ((y + 0.5f) / size * 2f) - 1f;
                float r = MathF.Sqrt((dx * dx) + (dy * dy));
                float a = r <= core ? 1f : MathF.Max(0f, 1f - ((r - core) / (1f - core)));
                image.SetPixel(x, y, new Color(colour.R, colour.G, colour.B, a * a));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// The muzzle actors' own lights on the world while they live (0.1 s): each is a steady LE_PointAmbient dynamic
    /// light, brightness 255, LightRadius 1 (50 uu); the rifle spawns two (SMuzzleGenerique and sMuzzleCanon), the
    /// Shocker one (SMuzzleSticky).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SMuzzleGenerique, sMuzzleCanon, SMuzzleSticky defaults: LT_Steady, LE_PointAmbient, bDynamicLight, LightBrightness 255, LightRadius 1, hue 40 / 148, saturation 190 / 127")]
    private static LightEntry[] MuzzleLights(Name kind, FVector at)
    {
        LightEntry Light(string name, byte hue, byte saturation) => new(name, at, default, 255f, hue, saturation, 1f, 128,
            OpenScctGame.Render.RenderK.LE_PointAmbient, 1, true, true, true, true, null, 0f, 0f, false);
        return kind == CombatFx.MuzzleSticky
            ? [Light("SMuzzleSticky", 148, 127)]
            : [Light("SMuzzleGenerique", 40, 190), Light("sMuzzleCanon", 40, 190)];
    }

    /// <summary>A UE2 light hue and saturation (0 to 255) as a colour at full brightness.</summary>
    private static Color FromHue(int hue, int saturation) => Color.FromHsv(hue / 255f, saturation / 255f, 1f);

    private readonly record struct PendingFlash(Name Kind, Actor? Source);

    private sealed class Flash(Node3D node, OmniLight3D light, MeshInstance3D quad, float energy, float life)
    {
        public Node3D Node { get; } = node;

        public OmniLight3D Light { get; } = light;

        public MeshInstance3D Quad { get; } = quad;

        public float Energy { get; } = energy;

        public float Life { get; } = life;

        public float Age { get; set; }

        public bool FirstPerson { get; init; }

        public LightEntry[] WorldLights { get; init; } = [];
    }

    private sealed class Burst(Node3D node, float life)
    {
        public Node3D Node { get; } = node;

        public float Life { get; } = life;

        public float Age { get; set; }
    }

    private sealed class Streak(MeshInstance3D node, FVector start, FVector direction, float length, float speed, float life)
    {
        public MeshInstance3D Node { get; } = node;

        public FVector Start { get; } = start;

        public FVector Direction { get; } = direction;

        public float Length { get; } = length;

        public float Speed { get; } = speed;

        public float Life { get; } = life;

        public float Age { get; set; }
    }
}
