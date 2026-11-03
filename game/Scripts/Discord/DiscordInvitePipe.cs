using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordRPC.IO;
using DiscordRPC.Logging;

namespace OpenScctGame.Discord;

/// <summary>
/// Keeps the retired match secret out of DiscordRichPresence 1.6.1.70's outgoing activity JSON.
/// That release serializes its obsolete public MatchSecret field even when null, which breaks game invites.
/// Remove this adapter when the package includes https://github.com/Lachee/discord-rpc-csharp/issues/286.
/// </summary>
public sealed class DiscordInvitePipe : INamedPipeClient
{
    private readonly INamedPipeClient _inner;

    public DiscordInvitePipe() : this(new ManagedNamedPipeClient()) { }

    public DiscordInvitePipe(INamedPipeClient inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public ILogger Logger { get => _inner.Logger; set => _inner.Logger = value; }
    public bool IsConnected => _inner.IsConnected;
#pragma warning disable CS0618 // Required by INamedPipeClient; preserve the wrapped transport's value.
    public int ConnectedPipe => _inner.ConnectedPipe;
#pragma warning restore CS0618
    public bool Connect(int pipe) => _inner.Connect(pipe);
    public bool ReadFrame(out PipeFrame frame) => _inner.ReadFrame(out frame);
    public void Close() => _inner.Close();
    public void Dispose() => _inner.Dispose();

    public bool WriteFrame(PipeFrame frame)
    {
        if (frame.Opcode == Opcode.Frame)
        {
            try
            {
                if (JsonNode.Parse(frame.Data) is JsonObject payload &&
                    payload["cmd"] is JsonValue command && command.TryGetValue(out string? name) &&
                    name == "SET_ACTIVITY" &&
                    payload["args"] is JsonObject args &&
                    args["activity"] is JsonObject activity &&
                    activity["secrets"] is JsonObject secrets)
                {
                    // Evaluate both removals; retain every supported secret and every other activity field.
                    bool removed = secrets.Remove("MatchSecret") | secrets.Remove("match");
                    if (removed)
                    {
                        frame.Message = payload.ToJsonString();
                    }
                }
            }
            catch (JsonException)
            {
                // Let the underlying transport handle an unexpected frame without modifying its bytes.
            }
        }

        return _inner.WriteFrame(frame);
    }
}
