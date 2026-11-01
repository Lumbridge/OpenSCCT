using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>One read path for the merc's own HUD and the body drawn by another player.</summary>
public static class MercShockPresentation
{
    public static ShockImmunityView Read(SBasePawn pawn, float renderAlpha = 1f)
    {
        if (!MercShockImmunity.VisiblePawn(pawn)) return default;
        if (pawn.Role == ENetRole.ROLE_Authority)
            return MercShockImmunity.Capture(pawn).At(pawn.Level.TimeSeconds);
        if (pawn.XLevel.GetService<ClientSession>() is not { } client || !client.Driver.Timeline.IsValid) return default;
        float alpha = float.IsFinite(renderAlpha) ? Math.Clamp(renderAlpha, 0f, 1f) : 1f;
        bool remote = pawn.Role == ENetRole.ROLE_SimulatedProxy;
        long now;
        if (remote && client.PawnProxies != PawnProxyMode.DeadReckoning)
        {
            long current = client.RenderTimeFixed;
            long previous = client.PreviousRenderTimeFixed >= 0 ? client.PreviousRenderTimeFixed : current;
            now = previous + (long)((current - previous) * alpha);
        }
        else now = client.Driver.Timeline.RenderTimeFixed(client.LocalTick, 0);
        if (now < 0) return default;
        if (remote && client.PawnProxies == PawnProxyMode.Snapshots)
            return pawn.NetworkPoseReady && client.PawnStates?.TryGetShockSample(pawn, now, out MercShockSample sample) == true
                ? sample.At(now) : default;
        return pawn.XLevel.GetService<MercShockStream>()?.Read(pawn, now) ?? default;
    }
}

/// <summary>Game seconds are anchored to a server tick; they are not tick/60 because the game uses float accumulation.</summary>
internal readonly record struct MercShockSample(ShockImmunityState State, long Tick, float Seconds, float Step)
{
    public bool SameStatus(in MercShockSample other) => State == other.State && (!State.TimerBlocked || Step == other.Step);

    public ShockImmunityView At(long tickFixed) => State.At(Seconds +
        (float)((tickFixed - Tick * NetTimeline.Fraction) * (double)Step / NetTimeline.Fraction));

    public void Write(FBitWriter writer)
    {
        writer.WriteBit(State.TimerBlocked); writer.WriteBit(State.StateProtected);
        if (!State.TimerBlocked) return;
        writer.WriteFloat(State.ExpiresAt); writer.WriteFloat(State.Duration);
        writer.WriteFloat(Seconds); writer.WriteFloat(Step);
    }

    public static MercShockSample Read(FBitReader reader, long tick)
    {
        bool timer = reader.ReadBit(), blocked = reader.ReadBit();
        if (!timer) return new(new(false, blocked, 0f, 0f), tick, 0f, 0f);
        float expires = reader.ReadFloat(), duration = reader.ReadFloat(), seconds = reader.ReadFloat(), step = reader.ReadFloat();
        if (!float.IsFinite(expires) || !float.IsFinite(duration) || duration < 0f || !float.IsFinite(seconds) ||
            !float.IsFinite(step) || step < 0f) { reader.SetError(); return default; }
        return new(new(timer, blocked, expires, duration), tick, seconds, step);
    }
}

/// <summary>Atomic status changes for owners and legacy proxies; snapshot observers use their existing body history.</summary>
internal sealed class MercShockStream
{
    private const string Function = "SvmMercShockStatus";
    private const string RequestFunction = "SvmMercShockStatusRequest";
    private readonly NetDriver _driver;
    private readonly Dictionary<SBasePawn, Source> _sources = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ActorChannel, (uint Epoch, uint Revision)> _sent = new();
    private readonly Dictionary<ActorChannel, uint> _resetTokens = new();
    private readonly Dictionary<SBasePawn, Source> _received = new(ReferenceEqualityComparer.Instance);
    private readonly List<SBasePawn> _gone = new();
    private readonly List<ActorChannel> _closed = new();
    private long _clockTick = -1;
    private float _clockSeconds;
    private float _clockStep = Sim.DeltaTime;
    public long RecordsSent { get; private set; }

    private sealed class Source
    {
        public uint Epoch, Revision, LocalReset, RequestedReset;
        public MercShockSample Sample;
        public List<MercShockSample>? History;
    }

    private MercShockStream(NetDriver driver) { _driver = driver; driver.World!.Level.SetService(this); }

    public static MercShockStream For(NetDriver driver) => driver.World!.Level.GetService<MercShockStream>() ?? new(driver);

    public static RepExtensionFunction Extension() => new(Function, true, false, "merc-shock-status-2", Receive);
    public static RepExtensionFunction RequestExtension() => new(RequestFunction, true, true, "merc-shock-status-request-1", ReceiveRequest);

    public void Record()
    {
        if (!_driver.IsServer) return;
        Level level = _driver.World!.Level;
        if (_clockTick == level.TickCount) return;
        _clockStep = _clockTick >= 0 && level.TickCount > _clockTick
            ? (level.TimeSeconds - _clockSeconds) / (level.TickCount - _clockTick) : level.DeltaTime;
        _clockTick = level.TickCount; _clockSeconds = level.TimeSeconds;
        foreach (SBasePawn pawn in level.DynamicActors<SBasePawn>())
        {
            if (pawn.bDeleteMe || pawn.SPawnType != PawnType.SPAWNDEFENSE) continue;
            var sample = new MercShockSample(MercShockImmunity.Capture(pawn), _clockTick, _clockSeconds, _clockStep);
            if (!_sources.TryGetValue(pawn, out Source? source)) _sources.Add(pawn, source = new());
            if (source.Revision == 0 || source.Epoch != pawn.NetworkResetId || !sample.SameStatus(source.Sample)) source.Revision++;
            source.Epoch = pawn.NetworkResetId; source.Sample = sample;
        }
        foreach (SBasePawn pawn in _sources.Keys) if (pawn.bDeleteMe) _gone.Add(pawn);
        foreach (SBasePawn pawn in _gone) _sources.Remove(pawn);
        _gone.Clear();
        foreach (ActorChannel channel in _sent.Keys) if (channel.Closing || channel.Connection.State == EConnectionState.USOCK_Closed) _closed.Add(channel);
        foreach (ActorChannel channel in _closed) { _sent.Remove(channel); _resetTokens.Remove(channel); }
        _closed.Clear();
    }

    public MercShockSample Capture(SBasePawn pawn) => _sources.TryGetValue(pawn, out Source? source) ? source.Sample : default;

    public void Send(SBasePawn pawn, NetConnection connection)
    {
        if (!_sources.TryGetValue(pawn, out Source? source) ||
            connection.FindActorChannel(pawn) is not { OpenAcked: true, Closing: false, NumOutRec: < 96 } channel ||
            !connection.IsNetReady || _sent.TryGetValue(channel, out var sent) && sent == (source.Epoch, source.Revision)) return;
        FOutBunch bunch = channel.BeginFunction(channel.Layout!.FindFunction(Function)!, true);
        bunch.Writer.WriteIntPacked(_resetTokens.GetValueOrDefault(channel));
        bunch.Writer.WriteIntPacked(source.Epoch); bunch.Writer.WriteIntPacked(source.Revision);
        bunch.Writer.WriteIntPacked((uint)source.Sample.Tick); source.Sample.Write(bunch.Writer);
        if (channel.SendFunction(bunch) >= 0) { _sent[channel] = (source.Epoch, source.Revision); RecordsSent++; }
    }

    public ShockImmunityView Read(SBasePawn pawn, long tickFixed)
    {
        if (!_received.TryGetValue(pawn, out Source? status) || status.LocalReset != pawn.NetworkResetId) return default;
        if (pawn.Role == ENetRole.ROLE_SimulatedProxy && _driver.World!.Level.GetService<ClientSession>()?.PawnProxies == PawnProxyMode.Interpolate)
        {
            // A reliable transition can arrive before its displayed pose. Keep its predecessor until playout reaches it.
            if (status.History is { } history)
                for (int i = history.Count - 1; i >= 0; i--)
                    if (history[i].Tick * NetTimeline.Fraction <= tickFixed) return history[i].At(tickFixed);
            return default;
        }
        return status.Sample.At(tickFixed);
    }

    /// <summary>A local correction invalidates its old tuple immediately. Request a baseline with this local generation echoed.</summary>
    public void AfterReceive()
    {
        if (_driver.IsServer || _driver.ServerConnection is not { } connection || connection.Actor is not PlayerController controller ||
            connection.FindActorChannel(controller) is not { Closing: false, NumOutRec: < 96 } channel) return;
        foreach (var pair in _received)
        {
            SBasePawn pawn = pair.Key; Source status = pair.Value;
            if (pawn.bDeleteMe || status.LocalReset == pawn.NetworkResetId || status.RequestedReset == pawn.NetworkResetId ||
                !connection.IsNetReady || connection.FindActorChannel(pawn) is not { Closing: false } pawnChannel) continue;
            FOutBunch bunch = channel.BeginFunction(channel.Layout!.FindFunction(RequestFunction)!, true);
            bunch.Writer.WriteIntPacked(pawnChannel.NetGuid); bunch.Writer.WriteIntPacked(pawn.NetworkResetId);
            if (channel.SendFunction(bunch) >= 0) status.RequestedReset = pawn.NetworkResetId;
        }
    }

    private static void ReceiveRequest(Actor actor, FBitReader reader, NetConnection connection)
    {
        uint guid = reader.ReadIntPacked(), reset = reader.ReadIntPacked();
        if (reader.IsError || reset == 0 || actor is not PlayerController || !ReferenceEquals(connection.Actor, actor)) return;
        ActorChannel? channel = null;
        foreach (ActorChannel candidate in connection.ActorChannels)
            if (candidate.NetGuid == guid && candidate is { OpenAcked: true, Closing: false }) { channel = candidate; break; }
        if (channel?.Actor is not SBasePawn pawn) return;
        MercShockStream stream = For(connection.Driver);
        stream._resetTokens[channel] = reset; stream._sent.Remove(channel);
        stream.Send(pawn, connection);
    }

    private static void Receive(Actor actor, FBitReader reader, NetConnection connection)
    {
        uint reset = reader.ReadIntPacked(), epoch = reader.ReadIntPacked(), revision = reader.ReadIntPacked();
        long tick = reader.ReadIntPacked();
        MercShockSample sample = MercShockSample.Read(reader, tick);
        if (reader.IsError || actor is not SBasePawn pawn || epoch == 0 || revision == 0) return;
        MercShockStream stream = For(connection.Driver);
        List<MercShockSample>? history = null;
        if (stream._received.TryGetValue(pawn, out Source? previous))
        {
            bool newLocalGeneration = previous.LocalReset != pawn.NetworkResetId;
            if (newLocalGeneration && reset != pawn.NetworkResetId || reset != 0 && reset != pawn.NetworkResetId ||
                epoch < previous.Epoch || epoch == previous.Epoch && (revision < previous.Revision || revision == previous.Revision && !newLocalGeneration)) return;
            if (!newLocalGeneration && epoch == previous.Epoch) history = previous.History;
        }
        history ??= new();
        history.Add(sample);
        if (history.Count > PawnStateReplication.History) history.RemoveAt(0);
        stream._received[pawn] = new Source { Epoch = epoch, Revision = revision, Sample = sample, LocalReset = pawn.NetworkResetId, History = history };
        foreach (SBasePawn old in stream._received.Keys) if (old.bDeleteMe) stream._gone.Add(old);
        foreach (SBasePawn old in stream._gone) stream._received.Remove(old);
        stream._gone.Clear();
    }
}
