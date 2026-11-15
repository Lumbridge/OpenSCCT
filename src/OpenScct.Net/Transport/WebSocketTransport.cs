using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace OpenScct.Net;

/// <summary>One game datagram per binary WebSocket message. The normal net driver still owns packet reliability.</summary>
public sealed class WebSocketTransport : ITransport
{
    public const int DatagramLimit = 1200;
    private static readonly EndpointId Server = new(1);
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<byte[]> _outgoing = System.Threading.Channels.Channel.CreateBounded<byte[]>(256);
    private readonly Channel<byte[]> _incoming = System.Threading.Channels.Channel.CreateBounded<byte[]>(256);
    private string? _address;
    private bool _disposed;
    public WebSocketTransport(string? origin = null)
    {
        // A real browser supplies Origin itself. Native integration clients pass the site origin explicitly.
        if (origin is not null) _socket.Options.SetRequestHeader("Origin", origin);
    }
    public string? Error { get; private set; }
    public bool Connected => _socket.State == WebSocketState.Open;
    public int MaxDatagramSize => DatagramLimit;

    public EndpointId Connect(string address)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_address is not null && _address != address) throw new InvalidOperationException("A WebSocket transport has one server.");
        if (_address is null)
        {
            var uri = new Uri(address, UriKind.Absolute);
            if (uri.Scheme is not ("ws" or "wss")) throw new ArgumentException("A WebSocket address is required.", nameof(address));
            _address = address;
            _ = Run(uri);
        }
        return Server;
    }

    public bool Send(EndpointId to, ReadOnlySpan<byte> datagram) => !_disposed && Error is null && to == Server &&
        datagram.Length is > 0 and <= DatagramLimit && _outgoing.Writer.TryWrite(datagram.ToArray());

    public bool TryReceive(Span<byte> buffer, out EndpointId from, out int length)
    {
        if (!_disposed && _incoming.Reader.TryRead(out byte[]? data))
        {
            data.CopyTo(buffer);
            from = Server;
            length = data.Length;
            return true;
        }
        from = EndpointId.None;
        length = 0;
        return false;
    }

    private async Task Run(Uri uri)
    {
        try
        {
            await _socket.ConnectAsync(uri, _stop.Token).ConfigureAwait(false);
            Task receive = Receive();
            Task send = SendQueued();
            await Task.WhenAny(receive, send).ConfigureAwait(false);
            _stop.Cancel();
            _socket.Abort();
            await Task.WhenAll(receive, send).ConfigureAwait(false);
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            if (!_disposed) Error = error.Message;
        }
        finally { _socket.Abort(); }
    }

    private async Task SendQueued()
    {
        await foreach (byte[] data in _outgoing.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            await _socket.SendAsync(data.AsMemory(), WebSocketMessageType.Binary, true, _stop.Token).ConfigureAwait(false);
    }

    private async Task Receive()
    {
        var buffer = new byte[DatagramLimit + 1];
        while (!_stop.IsCancellationRequested)
        {
            int count = 0;
            ValueWebSocketReceiveResult part;
            do
            {
                part = await _socket.ReceiveAsync(buffer.AsMemory(count), _stop.Token).ConfigureAwait(false);
                if (part.MessageType == WebSocketMessageType.Close) { Error = "The browser connection closed."; return; }
                count += part.Count;
                if (part.MessageType != WebSocketMessageType.Binary || count > DatagramLimit)
                    throw new WebSocketException("Invalid game datagram.");
            } while (!part.EndOfMessage);
            if (count == 0 || !_incoming.Writer.TryWrite(buffer.AsSpan(0, count).ToArray()))
                throw new WebSocketException("The browser receive queue is full.");
        }
    }

    public void Disconnect(EndpointId endpoint) { if (endpoint == Server) Dispose(); }
    public string? RemoteAddress(EndpointId endpoint) => endpoint == Server ? _address : null;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _outgoing.Writer.TryComplete();
        _incoming.Writer.TryComplete();
        _socket.Abort();
        _socket.Dispose();
    }
}
