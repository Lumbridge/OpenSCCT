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

/// <summary>How a demo is watched (CS2 style): through a player's eyes, behind them, or flying free.</summary>
public enum DemoCameraMode
{
    FirstPerson,
    ThirdPerson,
    Free,
}

/// <summary>
/// Watches a .scctdemo (docs/DEMOS.md): a client session fed the recorded packets by a
/// <see cref="DemoPlaybackTransport"/>, played through the remote <see cref="PlaySession"/> in the normal play scene.
/// The recorder was a spectator, so the client has no pawn of its own; the cameras here replace its view. Pause and
/// speed run the playback clock, a seek forward fast-forwards the client, a seek back starts again from the beginning
/// (<see cref="Restart"/>) and fast-forwards.
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
    private long _played;
    private bool _disposed;
    private int _targetPlayerId = -1;
    private FVector _freeLocation;
    private bool _freePlaced;

    private DemoPlayer(string path, DemoRecording recording, uint seekTo)
    {
        Path = path;
        Recording = recording;
        SeekTarget = seekTo;
        _transport = new DemoPlaybackTransport(recording);
    }

    public string Path { get; }

    public DemoRecording Recording { get; }

    public DemoHeader Header => Recording.Header;

    public DemoClock Clock { get; } = new();

    public ClientSession? Client { get; private set; }

    public PlaySession? Session { get; private set; }

    public DemoCameraMode Mode { get; set; } = DemoCameraMode.ThirdPerson;

    /// <summary>The user's camera turn (free camera, and the orbit round the watched player).</summary>
    public FRotator Look { get; set; } = new(-2000, 0, 0);

    /// <summary>The server tick the playback has reached.</summary>
    public uint CurrentTick => Recording.Index.FirstTick + (uint)Math.Min(_played, uint.MaxValue);

    public uint FirstTick => Recording.Index.FirstTick;

    public uint LastTick => Recording.Index.LastTick;

    /// <summary>A tick to fast-forward to (a seek in progress), or 0.</summary>
    public uint SeekTarget { get; set; }

    public bool IsSeeking => SeekTarget > CurrentTick;

    /// <summary>True once the last packet was played.</summary>
    public bool AtEnd => _transport.AtEnd && CurrentTick >= LastTick;

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

    /// <summary>A new player on the same recording from the start, fast-forwarding to <paramref name="seekTo"/>.</summary>
    public DemoPlayer Restart(uint seekTo)
    {
        var next = new DemoPlayer(Path, Recording, seekTo)
        {
            Mode = Mode,
            Look = Look,
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
                Session = new PlaySession(settings, Clock, client, catalog.Maps, folder, loader)
                {
                    ViewOverride = ViewFor,
                    FirstPersonOverride = () => Mode == DemoCameraMode.FirstPerson ? Target : null,
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
        _transport.ReleaseUpTo(FirstTick + (uint)_played);
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
        while (run < maxTicks && CurrentTick < SeekTarget && !AtEnd)
        {
            Clock.Advance(TickMicros);
            session.Step(Clock.NowMicroseconds);
            run++;
        }

        if (CurrentTick >= SeekTarget || AtEnd)
        {
            SeekTarget = 0;
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
        Target = pawn;
        _targetPlayerId = pawn.PlayerReplicationInfo?.PlayerID ?? -1;
        if (Mode == DemoCameraMode.Free)
        {
            Mode = DemoCameraMode.ThirdPerson;
        }
    }

    /// <summary>First person, third person, free camera, round again.</summary>
    public void CycleMode()
    {
        if (Mode == DemoCameraMode.ThirdPerson && Target is { } pawn)
        {
            // The free camera starts where the chase camera was.
            _freeLocation = Eye(pawn) - Look.Vector() * ChaseDistance;
            _freePlaced = true;
        }

        Mode = Mode switch
        {
            DemoCameraMode.FirstPerson => DemoCameraMode.ThirdPerson,
            DemoCameraMode.ThirdPerson => DemoCameraMode.Free,
            _ => DemoCameraMode.FirstPerson,
        };
    }

    /// <summary>Turns the user's camera by mouse counts (free camera and the chase orbit).</summary>
    public void Turn(float dx, float dy)
    {
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
        float fov = calc.Valid && calc.Fov > 1f ? calc.Fov : 85f;
        SBasePawn? pawn = Target is { bDeleteMe: false } t ? t : null;
        switch (Mode)
        {
            case DemoCameraMode.FirstPerson when pawn is not null:
                return new ViewSample(tick, Eye(pawn), AimOf(pawn), fov, true);
            case DemoCameraMode.ThirdPerson when pawn is not null:
                return new ViewSample(tick, Eye(pawn) - Look.Vector() * ChaseDistance, Look, fov, true);
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

    /// <summary>Where the player aims: the aim the proxy samples carry (pitch high, yaw low), else the body.</summary>
    private static FRotator AimOf(SBasePawn pawn)
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
