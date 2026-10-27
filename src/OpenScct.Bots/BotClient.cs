using System;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;

namespace OpenScct.Bots;

/// <summary>Which side a bot plays.</summary>
public enum BotTeam : byte
{
    Spy = 0,
    Merc = 1,
}

/// <summary>
/// A headless bot client: a <see cref="ClientSession"/> with the gameplay services that logs in on a team and, each
/// tick before its world ticks, lets its <see cref="BotBrain"/> write the local player's input, as a player at the
/// keyboard would (PawnRig-style input bytes and view). Everything else (moves, corrections, replication, the combat
/// calls) is the normal client. Create it, then call <see cref="Tick"/> on the client's thread.
/// </summary>
public sealed class BotClient : IDisposable
{
    private readonly BotContext _context;
    private bool _disposed;

    public BotClient(ITransport transport, string serverAddress, long nowMicros, string name, BotTeam team, BotBrain brain, NavCache nav,
        ClientSettings? settings = null, bool ownsTransport = false)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(nav);
        Name = name;
        Team = team;
        Brain = brain;
        ClientSettings baseSettings = settings ?? new ClientSettings();
        Session = new ClientSession(transport, serverAddress, nowMicros, baseSettings with
        {
            PlayerName = name,
            LoginOptions = "?Team=" + ((int)team).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Gameplay = true,
        }, ownsTransport);
        _context = new BotContext(Session, nav);
        Session.BeforeWorldTick += OnBeforeWorldTick;
    }

    public string Name { get; }

    public BotTeam Team { get; }

    public BotBrain Brain { get; }

    public ClientSession Session { get; }

    /// <summary>The local player's controller, once it arrived.</summary>
    public SPlayerController? Controller => Session.LocalPlayer as SPlayerController;

    /// <summary>Frames the brain decided (the player had a pawn).</summary>
    public long ThinkFrames { get; private set; }

    /// <summary>The last input written.</summary>
    public BotInput LastInput { get; private set; }

    /// <summary>One client tick (receive, think, tick the world, send).</summary>
    public void Tick(long nowMicros) => Session.Tick(nowMicros);

    private void OnBeforeWorldTick(ClientSession session)
    {
        if (session.LocalPlayer is not SPlayerController pc)
        {
            return;
        }

        if (pc.Pawn is not SBasePawn { bDeleteMe: false } pawn || pawn.Health <= 0 || session.Map?.Physics is null)
        {
            Brain.Reset();
            BotInput.Idle.ApplyTo(pc);
            LastInput = BotInput.Idle;
            return;
        }

        _context.Controller = pc;
        _context.Pawn = pawn;
        BotInput input = Brain.Think(_context);
        input.ApplyTo(pc);
        LastInput = input;
        ThinkFrames++;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Session.BeforeWorldTick -= OnBeforeWorldTick;
        Session.Dispose();
    }
}
