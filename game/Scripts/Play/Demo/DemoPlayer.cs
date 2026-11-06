using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Online;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Play.Demo;

/// <summary>The playback clock: real time scaled by the speed, stopped while paused, moved on by seeks.</summary>
public sealed class DemoClock : IClock
{
    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
    private long _lastReal;
    private long _now;
    private double _speed = 1.0;

    public bool Paused { get; set; }

    /// <summary>Playback speed (0.25 to 4).</summary>
    public double Speed
    {
        get => _speed;
        set => _speed = Math.Clamp(value, 0.25, 4.0);
    }

    public long NowMicroseconds
    {
        get
        {
            long real = (long)_watch.Elapsed.TotalMicroseconds;
            if (!Paused)
            {
                _now += (long)((real - _lastReal) * _speed);
            }

            _lastReal = real;
            return _now;
        }
    }

    /// <summary>Moves the clock on by <paramref name="micros"/> (a fast-forward ran those ticks itself).</summary>
    public void Advance(long micros)
    {
        _ = NowMicroseconds;
        _now += micros;
    }
}

/// <summary>The camera perspective used to watch a demo.</summary>
public enum DemoCameraMode
{
    FirstPerson,
    ThirdPerson,
    Free,
    AsPlayed,
}

/// <summary>
/// Watches a .scctdemo (docs/DEMOS.md): a client session fed the recorded packets by a
/// <see cref="DemoPlaybackTransport"/>, played through the remote <see cref="PlaySession"/> in the normal play scene.
/// The recorder was a spectator, so the client has no pawn of its own; the cameras here replace its view. Pause and
/// speed run the playback clock. A seek forward fast-forwards the client. A seek back restores a checkpoint or
/// starts at the beginning, then fast-forwards to the target.
/// </summary>
public sealed class DemoPlayer : IDisposable
{
    /// <summary>Third-person distance behind the watched player, in unreal units.</summary>
    public const float ChaseDistance = 260f;

    /// <summary>Free camera speed, in unreal units per second (doubled with Shift).</summary>
    public const float FreeSpeed = 600f;

    private const long TickMicros = 1_000_000 / Sim.Hz;

    private readonly DemoPlaybackTransport _transport;
    private readonly List<SBasePawn> _targets = new();
    private readonly DemoChaseCamera _chase = new();
    private DemoCameraMode _mode = DemoCameraMode.ThirdPerson;
    private long _played;
    private uint _seekTarget;
    private bool _disposed;
    private int _targetPlayerId = -1;
    private FVector _freeLocation;
    private bool _freePlaced;

    private DemoPlayer(string path, DemoRecording recording, uint seekTo, bool useCheckpoint = true)
    {
        Path = path;
        Recording = recording;
        SeekTarget = seekTo;
        _transport = new DemoPlaybackTransport(recording, useCheckpoint ? SeekTarget : 0);
    }

    public string Path { get; }

    public DemoRecording Recording { get; }

    public DemoHeader Header => Recording.Header;

    public DemoClock Clock { get; } = new();

    /// <summary>Show player outlines through walls while watching this recording.</summary>
    public bool XRay { get; set; }

    public ClientSession? Client { get; private set; }

    public PlaySession? Session { get; private set; }

    public DemoCameraMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            if (value == DemoCameraMode.Free && Target is { bDeleteMe: false })
            {
                ViewSample current = ViewFor(Session?.View ?? default);
                _freeLocation = current.Location;
                Look = current.Rotation;
                _freePlaced = true;
            }
            _mode = value;
            _chase.Reset();
        }
    }

    /// <summary>The user's free-camera direction; third person follows the recorded view with a temporary orbit.</summary>
    public FRotator Look { get; set; } = new(-2000, 0, 0);

    /// <summary>The server tick shown on the timeline (the loading prefix stays at the round's start).</summary>
    public uint CurrentTick => Math.Max(FirstTick, PacketTick);

    private uint PacketTick => (uint)Math.Min(_transport.StartTick + Math.Max(0, _played - 1), uint.MaxValue);

    internal bool UsingCheckpoint => _transport.StartTick > Recording.Index.FirstTick;

    public uint FirstTick => Recording.PlaybackFirstTick;

    public uint LastTick => Recording.Index.LastTick;

    /// <summary>A tick to fast-forward to (a seek in progress), or 0.</summary>
    public uint SeekTarget
    {
        get => _seekTarget;
        set => _seekTarget = value == 0 ? 0 : Math.Clamp(value, FirstTick, LastTick);
    }

    // Even a paused restart at the first tick needs its initial packets, including the recorded loading prefix.
    public bool IsSeeking => (_played == 0 && Session is not null) || PacketTick < Math.Max(FirstTick, SeekTarget);

    /// <summary>True once the last packet was played.</summary>
    public bool AtEnd => _played > 0 && _transport.AtEnd && PacketTick >= LastTick;

    /// <summary>The player being watched (first or third person), or null.</summary>
    public SBasePawn? Target { get; private set; }

    /// <summary>The players that can be watched, in player id order (refreshed every tick).</summary>
    public IReadOnlyList<SBasePawn> Targets => _targets;

    /// <summary>Opens a demo file and checks it was recorded by this build's network protocol.</summary>
    public static DemoPlayer Open(string path, uint seekTo = 0)
    {
        DemoRecording recording = DemoFile.Read(path);
        return new DemoPlayer(path, recording, seekTo);
    }

    /// <summary>A new player on the same recording, fast-forwarding to <paramref name="seekTo"/>.</summary>
    public DemoPlayer Restart(uint seekTo, bool useCheckpoint = true)
    {
        var next = new DemoPlayer(Path, Recording, seekTo, useCheckpoint)
        {
            Mode = Mode,
            Look = Look,
            XRay = XRay,
            _targetPlayerId = _targetPlayerId,
            _freeLocation = _freeLocation,
            _freePlaced = _freePlaced,
        };
        next.Clock.Speed = Clock.Speed;
        next.Clock.Paused = Clock.Paused;
        return next;
    }

    /// <summary>
    /// The play scene's config for this demo: the map loaded through <paramref name="catalog"/> and the client session
    /// on the recording. Throws <see cref="InvalidOperationException"/> when the demo needs another game version or
    /// map files.
    /// </summary>
    public PlayConfig CreateConfig(PlayCatalog catalog, string[] mapRoots, string? gameRoot)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        string map = Header.Map;
        if (catalog.Maps.FindFolder(map) is not { } folder || !catalog.CanRender(map))
        {
            throw new InvalidOperationException($"The demo is on {map}, which is not installed.");
        }

        if (catalog.Maps.FindMap(map)?.ContentHash is { } content && content != 0 && Header.ContentHash != 0 && content != Header.ContentHash)
        {
            throw new InvalidOperationException($"The demo was recorded with other {map} files (version {Header.GameVersion}).");
        }

        var settings = new PlaySettings
        {
            Map = map,
            Mode = ModeKey(Header.GameOptions),
            PlayerName = "Demo",
            MapRoots = mapRoots,
        };
        var clientSettings = new ClientSettings
        {
            PlayerName = "Demo",
            Gameplay = true,
            InstallGameplayServices = false,
            Maps = catalog.Maps,
        };
        Client = new ClientSession(_transport, "demo", Clock.NowMicroseconds, clientSettings, ownsTransport: true);
        if (Client.Driver.ProtocolHash != Header.ProtocolHash)
        {
            Client.Dispose();
            Client = null;
            throw new InvalidOperationException($"The demo was recorded on another game version ({Header.GameVersion}).");
        }

        PlayMapLoader loader = catalog.Loader(map);
        ClientSession client = Client;
        return new PlayConfig
        {
            Settings = settings,
            GameRoot = gameRoot,
            Demo = this,
            CreateSession = _ =>
            {
                Session = new PlaySession(settings, Clock, client, catalog.Maps, folder, loader, _ => BeforeTick())
                {
                    ViewOverride = ViewFor,
                    FirstPersonOverride = FirstPersonBody,
                };
                return Session;
            },
        };
    }

    /// <summary>The mode key of the recorded game options (MapCatalog.ModeKey; Story when absent or unknown).</summary>
    public static string ModeKey(string gameOptions)
    {
        string mode = UcOptions.ParseOption(gameOptions ?? string.Empty, "GameMode");
        return byte.TryParse(mode, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out byte id)
            ? MapCatalog.ModeKey((EGameMode)id)
            : "Story";
    }

    // ---- ticking (called by the play scene on its client ticks) -----------------------------------------------

    /// <summary>Before a client tick: the packets of the next server tick may arrive.</summary>
    public void BeforeTick()
    {
        _transport.ReleaseUpTo((uint)Math.Min(_transport.StartTick + _played, uint.MaxValue));
        _played++;
    }

    /// <summary>After a client tick: the watchable players and the watched one.</summary>
    public void AfterTick(PlaySession session)
    {
        _targets.Clear();
        foreach (SBasePawn pawn in session.Client.World.Level.DynamicActors<SBasePawn>())
        {
            if (!pawn.bDeleteMe && pawn.PlayerReplicationInfo is { bOnlySpectator: false })
            {
                _targets.Add(pawn);
            }
        }

        _targets.Sort(static (a, b) => a.PlayerReplicationInfo!.PlayerID.CompareTo(b.PlayerReplicationInfo!.PlayerID));
        SBasePawn? previousTarget = Target;
        Target = null;
        foreach (SBasePawn pawn in _targets)
        {
            if (pawn.PlayerReplicationInfo!.PlayerID == _targetPlayerId)
            {
                Target = pawn;
            }
        }

        // The watched player died (a new pawn comes with the same id) or none was picked yet: the first one.
        if (Target is null && _targets.Count > 0 && (_targetPlayerId < 0 || !HasPlayer(_targetPlayerId)))
        {
            Target = _targets[0];
            _targetPlayerId = Target.PlayerReplicationInfo!.PlayerID;
        }

        if (!ReferenceEquals(previousTarget, Target))
        {
            // A newly watched player or respawn starts behind them, even if the last target was being orbited.
            bool orbiting = _chase.Orbiting;
            _chase.Reset();
            _chase.Orbiting = orbiting;
        }

        if (AtEnd)
        {
            Clock.Paused = true;
        }
    }

    private bool HasPlayer(int playerId)
    {
        foreach (PlayerReplicationInfo pri in Session!.Client.World.Level.DynamicActors<PlayerReplicationInfo>())
        {
            if (!pri.bDeleteMe && pri.PlayerID == playerId && !pri.bOnlySpectator)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Runs up to <paramref name="maxTicks"/> client ticks at once towards <see cref="SeekTarget"/> (moving the clock
    /// on with them, so the normal update does not run them again). True while ticks remain.
    /// </summary>
    public bool FastForward(int maxTicks)
    {
        if (Session is not { } session || !IsSeeking)
        {
            return false;
        }

        int run = 0;
        while (run < maxTicks && IsSeeking && !AtEnd)
        {
            Clock.Advance(TickMicros);
            session.Step(Clock.NowMicroseconds);
            run++;
        }

        if (!IsSeeking || AtEnd)
        {
            SeekTarget = 0;
            session.SetViewNow(ViewFor(session.View));
        }

        return IsSeeking;
    }

    // ---- cameras ---------------------------------------------------------------------------------------------

    /// <summary>Watches the next (or previous) player.</summary>
    public void CycleTarget(int step)
    {
        if (_targets.Count == 0)
        {
            return;
        }

        int index = Target is null ? 0 : _targets.IndexOf(Target);
        index = ((index + step) % _targets.Count + _targets.Count) % _targets.Count;
        Watch(_targets[index]);
    }

    /// <summary>Watches the player at <paramref name="index"/> of <see cref="Targets"/> (number keys).</summary>
    public void WatchIndex(int index)
    {
        if (index >= 0 && index < _targets.Count)
        {
            Watch(_targets[index]);
        }
    }

    private void Watch(SBasePawn pawn)
    {
        if (!ReferenceEquals(Target, pawn)) _chase.Reset();
        Target = pawn;
        _targetPlayerId = pawn.PlayerReplicationInfo?.PlayerID ?? -1;
        if (Mode == DemoCameraMode.Free)
        {
            Mode = DemoCameraMode.ThirdPerson;
        }
    }

    /// <summary>Cycle through the playback cameras.</summary>
    public void CycleMode()
    {
        Mode = Mode switch
        {
            DemoCameraMode.FirstPerson => DemoCameraMode.ThirdPerson,
            DemoCameraMode.ThirdPerson => DemoCameraMode.Free,
            DemoCameraMode.Free => DemoCameraMode.AsPlayed,
            _ => DemoCameraMode.FirstPerson,
        };
    }

    /// <summary>Holds the third-person orbit while right mouse is down; releasing starts a smooth return.</summary>
    public void SetLooking(bool looking) => _chase.Orbiting = looking && Mode == DemoCameraMode.ThirdPerson;

    /// <summary>Updates the chase return in real time, including while playback is paused.</summary>
    public void AdvanceCamera(float seconds) => _chase.Advance(seconds);

    /// <summary>Turns the free camera or temporarily orbits relative to the watched player's recorded view.</summary>
    public void Turn(float dx, float dy)
    {
        if (Mode == DemoCameraMode.ThirdPerson)
        {
            if (Target is { bDeleteMe: false } pawn) _chase.Turn(pawn, dx, dy);
            return;
        }
        if (Mode != DemoCameraMode.Free) return;
        FRotator look = Look;
        look.Yaw += (int)(dx * 20f);
        look.Pitch = Math.Clamp(look.Pitch - (int)(dy * 20f), -16000, 16000);
        Look = look;
    }

    /// <summary>Flies the free camera (forward/right/up in -1..1) for <paramref name="seconds"/> of real time.</summary>
    public void Fly(float forward, float right, float up, bool fast, float seconds)
    {
        if (Mode != DemoCameraMode.Free)
        {
            return;
        }

        FVector dir = Look.Vector();
        FRotator flat = new(0, Look.Yaw + 16384, 0);
        FVector side = flat.Vector();
        float speed = FreeSpeed * (fast ? 2f : 1f) * seconds;
        _freeLocation += (dir * forward + side * right + new FVector(0f, 0f, up)) * speed;
    }

    /// <summary>The view to draw for the client's calc view <paramref name="calc"/>.</summary>
    public ViewSample ViewFor(ViewSample calc)
    {
        long tick = Session?.Client.LocalTick ?? calc.Tick;
        float fov = Session?.LocalController?.FovAngle is > 1f and < 180f and var spectatorFov ? spectatorFov : 85f;
        SBasePawn? pawn = Target is { bDeleteMe: false } t ? t : null;
        switch (Mode)
        {
            case DemoCameraMode.FirstPerson or DemoCameraMode.AsPlayed when pawn is not null:
                if (TryRecordedView(pawn, out DemoViewFrame recorded) && (Mode == DemoCameraMode.AsPlayed || recorded.FirstPersonBody))
                    return new ViewSample(tick, recorded.Location, recorded.Rotation, recorded.Fov, true, recorded.SpyFirstPerson);
                return Mode == DemoCameraMode.AsPlayed && pawn is SPawnAttaque
                    ? _chase.ViewFor(pawn, tick, fov, ChaseDistance)
                    : EyeView(pawn, tick, fov);
            case DemoCameraMode.ThirdPerson when pawn is not null:
                return _chase.ViewFor(pawn, tick, fov, ChaseDistance);
            default:
                if (!_freePlaced)
                {
                    _freeLocation = calc.Valid ? calc.Location : pawn is not null ? Eye(pawn) : default;
                    _freePlaced = calc.Valid || pawn is not null;
                }

                return new ViewSample(tick, _freeLocation, Look, fov, _freePlaced);
        }
    }

    private static FVector Eye(SBasePawn pawn) => pawn.Location + new FVector(0f, 0f, pawn.EyeHeight);

    private static ViewSample EyeView(SBasePawn pawn, long tick, float fov) =>
        new(tick, Eye(pawn), pawn is SPawnAttaque ? DemoChaseCamera.ReplicatedView(pawn) : MercAim(pawn), fov, true);

    private bool TryRecordedView(SBasePawn pawn, out DemoViewFrame view)
    {
        view = default;
        return Client is not null && Client.TryRecordedDemoView(Recording, pawn, PacketTick, out view);
    }

    private SBasePawn? FirstPersonBody()
    {
        if (Target is not { bDeleteMe: false } pawn) return null;
        if (Mode == DemoCameraMode.FirstPerson) return pawn;
        if (Mode != DemoCameraMode.AsPlayed) return null;
        bool showEyes = TryRecordedView(pawn, out DemoViewFrame view) ? view.FirstPersonBody : pawn is not SPawnAttaque;
        return showEyes ? pawn : null;
    }

    /// <summary>The Merc's replicated weapon aim (pitch high, yaw low), with body facing as a fallback.</summary>
    private static FRotator MercAim(SBasePawn pawn)
    {
        int packed = pawn.iDesiredYawPitch;
        if (packed == 0)
        {
            return new FRotator(0, pawn.Rotation.Yaw, 0);
        }

        int pitch = (packed >> 16) & 0xFFFF;
        if (pitch > 0x7FFF)
        {
            pitch -= 0x10000;
        }

        return new FRotator(pitch, packed & 0xFFFF, 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Session?.Dispose();
        Client?.Dispose();
    }
}
