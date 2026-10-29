using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Controls;

/// <summary>
/// Plays keyboard and mouse input sent by a mirror tool (kept in the private OpenSCCT-DevTools repo) as if it came
/// from this machine, so the original game and OpenSCCT can be driven side by side with one set of inputs. Started with <c>--svm-mirror</c> (port
/// <see cref="DefaultPort"/>) or <c>--svm-mirror=port</c>. While mirroring, the window never takes focus or the real
/// pointer (the original game has both), and every input goes through Godot's own input path.
/// <para>
/// Packets on 127.0.0.1 UDP, little-endian, one input each: 1 key (u16 scancode, u8 flags: 1 E0 prefix, 2 pressed);
/// 2 mouse motion (i32 dx, i32 dy, f32 x, f32 y: the pointer within the original's window, 0..1, or -1 when
/// unknown); 3 mouse button (u8 button: 1 left, 2 right, 3 middle, 4 X1, 5 X2, 6 wheel up, 7 wheel down; u8 pressed;
/// f32 x, f32 y); 4 release everything (the original lost focus); 5 reset (f32 x, y, z, i32 pitch, i32 yaw in
/// rotator units): every input is released and the local player's pawn is put there, facing that way, on the listen
/// server and on the client (kept until a pawn exists, so a reset sent at start lands once the map has loaded).
/// </para>
/// </summary>
public sealed partial class InputMirror : Node
{
    public const string Flag = "--svm-mirror";
    public const int DefaultPort = 27950;

    private readonly ConcurrentQueue<byte[]> _packets = new();
    private readonly HashSet<Key> _keysDown = new();
    private readonly HashSet<MouseButton> _buttonsDown = new();
    private UdpClient? _socket;
    private Thread? _thread;
    private volatile bool _stop;
    private Vector2 _pointer;

    /// <summary>Where the player starts (<c>--svm-mirror-reset=x,y,z,pitch,yaw</c>: the original's spawn), as packet 5.</summary>
    public const string ResetFlag = "--svm-mirror-reset=";

    public InputMirror(int port)
    {
        Name = "InputMirror";
        Port = port;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(ResetFlag, StringComparison.Ordinal))
            {
                string[] v = arg[ResetFlag.Length..].Split(',');
                if (v.Length == 5)
                {
                    float F(int i) => float.Parse(v[i], System.Globalization.CultureInfo.InvariantCulture);
                    _reset = new ResetSpot(new FVector(F(0), F(1), F(2)), new FRotator((int)F(3), (int)F(4), 0));
                }
            }
        }
    }

    public int Port { get; }

    /// <summary>The mirror port asked for on the command line, or null when not mirroring.</summary>
    public static int? Wanted(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        foreach (string arg in args)
        {
            if (arg == Flag)
            {
                return DefaultPort;
            }

            if (arg.StartsWith(Flag + "=", StringComparison.Ordinal)
                && int.TryParse(arg[(Flag.Length + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int port)
                && port is > 0 and < 65536)
            {
                return port;
            }
        }

        return null;
    }

    /// <summary>True for the arguments this class reads (kept out of the unknown-argument warnings).</summary>
    public static bool IsFlag(string arg) =>
        arg == Flag || arg.StartsWith(Flag + "=", StringComparison.Ordinal) || arg.StartsWith(ResetFlag, StringComparison.Ordinal);

    public override void _Ready()
    {
        // The original game keeps the focus and the real mouse: this window only takes the mirrored input.
        Pointer.Detached = true;
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
        }

        try
        {
            _socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, Port));
        }
        catch (SocketException e)
        {
            GD.PushError($"Input mirror: cannot listen on 127.0.0.1:{Port} ({e.Message}).");
            return;
        }

        _thread = new Thread(Receive) { IsBackground = true, Name = "InputMirror" };
        _thread.Start();
        GD.Print($"Input mirror: listening on 127.0.0.1:{Port}.");
    }

    public override void _ExitTree()
    {
        if (_hookedHost is { } host)
        {
            host.BeforeWorldTick -= OnServerTick;
            _hookedHost = null;
        }

        _stop = true;
        _socket?.Dispose();
        _socket = null;
    }

    private void Receive()
    {
        var from = new IPEndPoint(IPAddress.Any, 0);
        while (!_stop && _socket is { } socket)
        {
            try
            {
                _packets.Enqueue(socket.Receive(ref from));
            }
            catch (SocketException)
            {
                // A port-unreachable report from an earlier send, or the socket closing.
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public override void _Process(double delta)
    {
        ApplyReset();
        while (_packets.TryDequeue(out byte[]? packet))
        {
            try
            {
                Capture(packet);
                Play(packet);
            }
            catch (ArgumentOutOfRangeException)
            {
                // A short packet: ignore it.
            }
        }
    }

    private PlayInput? _playInput;

    private sealed record ResetSpot(FVector Location, FRotator Rotation);

    /// <summary>The reset asked for, until the client's pawn has been placed.</summary>
    private ResetSpot? _reset;

    /// <summary>The reset the listen server applies on its next tick (written here, taken on the server thread).</summary>
    private ResetSpot? _serverReset;

    private ServerHost? _hookedHost;
    private int _clientPlacedFrames;

    /// <summary>
    /// A reset (packet 5): the listen server's player pawn is moved on the server's thread before its next tick, then
    /// the client's copy is put at the same place for a few frames (the client's own moves carry its place to the
    /// server, as the smokes' teleports do).
    /// </summary>
    private void ApplyReset()
    {
        PlaySession? session = FindPlayRoot(GetTree().Root)?.Session;
        if (session?.Host is { } host && !ReferenceEquals(host, _hookedHost))
        {
            if (_hookedHost is { } old)
            {
                old.BeforeWorldTick -= OnServerTick;
            }

            host.BeforeWorldTick += OnServerTick;
            _hookedHost = host;
        }

        if (_reset is not { } spot || session?.LocalPawn is not { } pawn || session.LocalController is not { } pc)
        {
            return;
        }

        if (_clientPlacedFrames == 0)
        {
            Interlocked.Exchange(ref _serverReset, spot);
        }

        pawn.SetLocation(spot.Location);
        pawn.Velocity = default;
        pawn.Rotation = new FRotator(0, spot.Rotation.Yaw, 0);
        pc.Rotation = spot.Rotation;
        pc.MyDesiredRotation = spot.Rotation;
        pc.LocalMyDesiredRotation = spot.Rotation;
        if (++_clientPlacedFrames >= 3)
        {
            _reset = null;
            _clientPlacedFrames = 0;
        }
    }

    private void OnServerTick(ServerHost host)
    {
        if (Interlocked.Exchange(ref _serverReset, null) is not { } spot)
        {
            return;
        }

        foreach (SBasePawn pawn in host.World.Level.AllActors<SBasePawn>())
        {
            if (pawn.bDeleteMe || pawn.Controller is not SPlayerController pc)
            {
                continue;
            }

            pawn.SetLocation(spot.Location);
            pawn.Velocity = default;
            pawn.Rotation = new FRotator(0, spot.Rotation.Yaw, 0);
            pc.Rotation = spot.Rotation;
            pc.MyDesiredRotation = spot.Rotation;
            pc.LocalMyDesiredRotation = spot.Rotation;
        }
    }

    private static PlayRoot? FindPlayRoot(Node node)
    {
        if (node is PlayRoot root)
        {
            return root;
        }

        foreach (Node child in node.GetChildren())
        {
            if (child is PlayRoot found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// The play input reads keys only while the pointer is captured, which the first click in the window does. The
    /// original game has the pointer from the start, so mirrored play takes it the same way pad play does (Escape
    /// is left to free it, and an open in-game menu disables the play input, so nothing is taken then).
    /// </summary>
    private void Capture(byte[] p)
    {
        if (p.Length >= 4 && p[0] == 1 && BitConverter.ToUInt16(p, 1) == 0x01)
        {
            return;
        }

        if (_playInput is null || !IsInstanceValid(_playInput) || !_playInput.IsInsideTree())
        {
            _playInput = GetTree().Root.FindChild("PlayInput", true, false) as PlayInput;
        }

        if (_playInput is { Enabled: true, RequireCapture: true, CapturePointer: true, IsCaptured: false })
        {
            Pointer.Capture();
        }
    }

    private void Play(byte[] p)
    {
        if (p.Length == 0)
        {
            return;
        }

        switch (p[0])
        {
            case 1 when p.Length >= 4:
            {
                int scancode = BitConverter.ToUInt16(p, 1);
                bool e0 = (p[3] & 1) != 0;
                bool pressed = (p[3] & 2) != 0;
                if (MirrorKeys.FromScancode(scancode, e0) is { } key)
                {
                    Key(key, pressed);
                }

                break;
            }

            case 2 when p.Length >= 17:
            {
                var relative = new Vector2(BitConverter.ToInt32(p, 1), BitConverter.ToInt32(p, 5));
                MovePointer(BitConverter.ToSingle(p, 9), BitConverter.ToSingle(p, 13));
                Input.ParseInputEvent(new InputEventMouseMotion
                {
                    Relative = relative,
                    ScreenRelative = relative,
                    Position = _pointer,
                    GlobalPosition = _pointer,
                    ButtonMask = Mask(),
                });
                break;
            }

            case 3 when p.Length >= 11:
            {
                MouseButton button = p[1] switch
                {
                    1 => MouseButton.Left,
                    2 => MouseButton.Right,
                    3 => MouseButton.Middle,
                    4 => MouseButton.Xbutton1,
                    5 => MouseButton.Xbutton2,
                    6 => MouseButton.WheelUp,
                    7 => MouseButton.WheelDown,
                    _ => MouseButton.None,
                };
                if (button == MouseButton.None)
                {
                    break;
                }

                MovePointer(BitConverter.ToSingle(p, 3), BitConverter.ToSingle(p, 7));
                if (button is MouseButton.WheelUp or MouseButton.WheelDown)
                {
                    // A wheel step is a press and a release, as Godot's own wheel events are.
                    Button(button, true);
                    Button(button, false);
                }
                else
                {
                    Button(button, p[2] != 0);
                }

                break;
            }

            case 4:
                ReleaseAll();
                break;
            case 5 when p.Length >= 21:
                ReleaseAll();
                _reset = new ResetSpot(
                    new FVector(BitConverter.ToSingle(p, 1), BitConverter.ToSingle(p, 5), BitConverter.ToSingle(p, 9)),
                    new FRotator(BitConverter.ToInt32(p, 13), BitConverter.ToInt32(p, 17), 0));
                break;
        }
    }

    private void MovePointer(float x, float y)
    {
        if (x < 0f || y < 0f)
        {
            return;
        }

        Vector2 size = GetViewport().GetVisibleRect().Size;
        _pointer = new Vector2(Math.Clamp(x, 0f, 1f) * size.X, Math.Clamp(y, 0f, 1f) * size.Y);
    }

    private void Key(Key key, bool pressed)
    {
        bool echo = pressed && !_keysDown.Add(key);
        if (!pressed && !_keysDown.Remove(key))
        {
            return;
        }

        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = key,
            PhysicalKeycode = key,
            Pressed = pressed,
            Echo = echo,
        });
    }

    private void Button(MouseButton button, bool pressed)
    {
        if (pressed)
        {
            _buttonsDown.Add(button);
        }
        else if (!_buttonsDown.Remove(button) && button is not (MouseButton.WheelUp or MouseButton.WheelDown))
        {
            return;
        }

        if (button is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _buttonsDown.Remove(button);
        }

        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = pressed,
            Position = _pointer,
            GlobalPosition = _pointer,
            ButtonMask = Mask(),
            Factor = 1f,
        });
    }

    private MouseButtonMask Mask()
    {
        MouseButtonMask mask = 0;
        foreach (MouseButton b in _buttonsDown)
        {
            mask |= (MouseButtonMask)(1 << ((int)b - 1));
        }

        return mask;
    }

    private void ReleaseAll()
    {
        foreach (Key key in new List<Key>(_keysDown))
        {
            Key(key, false);
        }

        foreach (MouseButton button in new List<MouseButton>(_buttonsDown))
        {
            Button(button, false);
        }
    }
}

/// <summary>Keyboard scancodes (set 1, as Windows raw input reports them) to Godot's physical keys.</summary>
public static class MirrorKeys
{
    private static readonly Key[] s_plain = BuildPlain();

    public static Key? FromScancode(int scancode, bool e0)
    {
        if (e0)
        {
            return scancode switch
            {
                0x1C => Godot.Key.KpEnter,
                0x1D => Godot.Key.Ctrl,
                0x35 => Godot.Key.KpDivide,
                0x37 => Godot.Key.Print,
                0x38 => Godot.Key.Alt,
                0x47 => Godot.Key.Home,
                0x48 => Godot.Key.Up,
                0x49 => Godot.Key.Pageup,
                0x4B => Godot.Key.Left,
                0x4D => Godot.Key.Right,
                0x4F => Godot.Key.End,
                0x50 => Godot.Key.Down,
                0x51 => Godot.Key.Pagedown,
                0x52 => Godot.Key.Insert,
                0x53 => Godot.Key.Delete,
                0x5B or 0x5C => Godot.Key.Meta,
                0x5D => Godot.Key.Menu,
                // E0 2A / E0 36 are the fake shifts Windows wraps around the navigation keys.
                _ => null,
            };
        }

        if (scancode is >= 0 and < 0x80 && s_plain[scancode] != Godot.Key.None)
        {
            return s_plain[scancode];
        }

        return null;
    }

    private static Key[] BuildPlain()
    {
        var k = new Key[0x80];
        k[0x01] = Godot.Key.Escape;
        for (int i = 0; i < 9; i++)
        {
            k[0x02 + i] = Godot.Key.Key1 + i;
        }

        k[0x0B] = Godot.Key.Key0;
        k[0x0C] = Godot.Key.Minus;
        k[0x0D] = Godot.Key.Equal;
        k[0x0E] = Godot.Key.Backspace;
        k[0x0F] = Godot.Key.Tab;
        Row(k, 0x10, "QWERTYUIOP");
        k[0x1A] = Godot.Key.Bracketleft;
        k[0x1B] = Godot.Key.Bracketright;
        k[0x1C] = Godot.Key.Enter;
        k[0x1D] = Godot.Key.Ctrl;
        Row(k, 0x1E, "ASDFGHJKL");
        k[0x27] = Godot.Key.Semicolon;
        k[0x28] = Godot.Key.Apostrophe;
        k[0x29] = Godot.Key.Quoteleft;
        k[0x2A] = Godot.Key.Shift;
        k[0x2B] = Godot.Key.Backslash;
        Row(k, 0x2C, "ZXCVBNM");
        k[0x33] = Godot.Key.Comma;
        k[0x34] = Godot.Key.Period;
        k[0x35] = Godot.Key.Slash;
        k[0x36] = Godot.Key.Shift;
        k[0x37] = Godot.Key.KpMultiply;
        k[0x38] = Godot.Key.Alt;
        k[0x39] = Godot.Key.Space;
        k[0x3A] = Godot.Key.Capslock;
        for (int i = 0; i < 10; i++)
        {
            k[0x3B + i] = Godot.Key.F1 + i;
        }

        k[0x45] = Godot.Key.Numlock;
        k[0x46] = Godot.Key.Scrolllock;
        k[0x47] = Godot.Key.Kp7;
        k[0x48] = Godot.Key.Kp8;
        k[0x49] = Godot.Key.Kp9;
        k[0x4A] = Godot.Key.KpSubtract;
        k[0x4B] = Godot.Key.Kp4;
        k[0x4C] = Godot.Key.Kp5;
        k[0x4D] = Godot.Key.Kp6;
        k[0x4E] = Godot.Key.KpAdd;
        k[0x4F] = Godot.Key.Kp1;
        k[0x50] = Godot.Key.Kp2;
        k[0x51] = Godot.Key.Kp3;
        k[0x52] = Godot.Key.Kp0;
        k[0x53] = Godot.Key.KpPeriod;
        k[0x56] = Godot.Key.Backslash;
        k[0x57] = Godot.Key.F11;
        k[0x58] = Godot.Key.F12;
        return k;
    }

    private static void Row(Key[] k, int first, string letters)
    {
        for (int i = 0; i < letters.Length; i++)
        {
            k[first + i] = Godot.Key.A + (letters[i] - 'A');
        }
    }
}
