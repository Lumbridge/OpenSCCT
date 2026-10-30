using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Moves;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Server;

/// <summary>
/// The server's send schedule for other players' pawns (spec/native/net-proxy.md rules 1, 2, 4 and 5, the position
/// part of ASBasePawn's native property list): a pawn never replicates Location or Velocity (bCompressedPosition);
/// instead each viewer gets position samples, decided by dead reckoning per viewer, as
/// <c>ClientGetPawnPosition(WhichPawn, CompPosition)</c> on the viewer's own controller. Run once per network frame,
/// before the flush.
/// <para>
/// The action batches ride with the samples (rule 5): a new batch forces a sample, which carries it reliably
/// (<c>ClientReliableGetPawnPositionAndActions</c>) the first time and unreliably while it is under 0.4 s old; between
/// samples a batch under 0.15 s old is repeated alone (<c>ClientGetPawnActions</c>). The batch bookkeeping (rule 6) is
/// the controller's tick (OpenScct.Game.Animation.ProxyAnimation.ServerUpdateActionCounts).
/// Rule 3's Rotation pushes go through <see cref="NetDriver.PropertyPush"/>, and its pacing of the aim properties
/// (iDesiredYawPitch, iYawPitchFromView: at most every 0.09 s) through <see cref="NetDriver.PropertyWithhold"/>.
/// </para>
/// <para>With <see cref="PawnProxyMode.Interpolate"/> (the Enhanced profile) the samples keep this schedule, but Rotation
/// and the aim properties go on every pass they changed, so the client's interpolation buffer gets them at the send
/// rate (docs/NETCODE.md, "Proxy pawn interpolation").</para>
/// </summary>
[UcNative("ASBasePawn::GetOptimizedRepList", Editor = 0x1120c9d0)]
[PortOf("eng:ASBasePawn::GetOptimizedRepList", Spec = "spec/native/net-proxy.md#server-what-each-viewer-gets-per-update-of-a-pawn-to-a-non-owning-connection")]
public sealed class PawnProxySender
{
    [Provenance(ProvenanceKind.Decoded, "net-proxy.md constants: forced sample interval 0.7 s")]
    public const float ForcedInterval = 0.7f;

    [Provenance(ProvenanceKind.Decoded, "net-proxy.md constants: minimum sample interval 0.09 s")]
    public const float MinInterval = 0.09f;

    [Provenance(ProvenanceKind.Decoded, "net-proxy.md constants: velocity deviation ratio 0.2 of speed squared")]
    public const float VelocityDeviation = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "net-proxy.md constants: position error threshold 1 (still) / 30 (moving) uu squared")]
    public const float PositionErrorStill = 1f;

    public const float PositionErrorMoving = 30f;

    [Provenance(ProvenanceKind.Decoded, "net-proxy.md constants: prediction gravity 1500 (Z velocity), 750 (Z position)")]
    public const float PredictionGravity = 1500f;

    [Provenance(ProvenanceKind.Decoded, "net-proxy.md constants: action resend window 0.15 s")]
    public const float ActionResendWindow = 0.15f;

    [Provenance(ProvenanceKind.Decoded, "net-proxy.md constants: actions with position window 0.4 s")]
    public const float ActionsWithPositionWindow = 0.4f;

    private const string FunctionName = "ClientGetPawnPosition";
    private const string ActionsFunction = "ClientGetPawnActions";
    private const string PositionAndActionsFunction = "ClientGetPawnPositionAndActions";
    private const string ReliablePositionAndActionsFunction = "ClientReliableGetPawnPositionAndActions";

    private readonly NetDriver _driver;
    private readonly Dictionary<(int Pawn, NetConnection Viewer), ViewerState> _states = new();
    private readonly Dictionary<int, EPhysics> _lastPhysics = new();
    private readonly List<(int, NetConnection)> _stale = new();
    private readonly NetCallArgs _args = new();
    private readonly HashSet<(int Pawn, NetConnection Viewer)> _rotationPushes = new();
    private readonly HashSet<(int Pawn, NetConnection Viewer)> _aimDue = new();
    private long _pass;

    public PawnProxySender(NetDriver driver)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _driver.PropertyPush = PushesRotation;
        _driver.PropertyWithhold = WithholdsAim;
    }

    private bool Interpolating => _driver.Settings.PawnProxies == PawnProxyMode.Interpolate;

    [Provenance(ProvenanceKind.Decoded, "net-proxy.md constants: rotation property threshold 1800")]
    public const int RotationPushYaw = 1800;

    /// <summary>Rotation properties pushed so far (rule 3).</summary>
    public long RotationPushes { get; private set; }

    /// <summary>Samples sent so far.</summary>
    public long Samples { get; private set; }

    /// <summary>Action batches sent so far (alone or with a sample).</summary>
    public long ActionBatches { get; private set; }

    private sealed class ViewerState
    {
        public float LastPositionTime = float.NegativeInfinity;
        public FVector Location;
        public FVector Velocity;
        public bool Initial = true;
        public long Pass;

        /// <summary>The rotation this viewer last got (the channel's reference) and when the aim block last ran.</summary>
        public FRotator Rotation;
        public float LastAimTime = float.NegativeInfinity;

        /// <summary>The batch time last sent to this viewer (the channel's copy of fRepTimeOfLastActionChange).</summary>
        public float ActionTime;
    }

    /// <summary>One pass over every joined viewer and every other player's pawn it has a channel for.</summary>
    public void Pass(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _pass++;
        _rotationPushes.Clear();
        _aimDue.Clear();
        float now = level.TimeSeconds;
        INetInterest? interest = _driver.Settings.Interest;
        IReadOnlyList<NetConnection> viewers = _driver.ClientConnections;
        for (int v = 0; v < viewers.Count; v++)
        {
            NetConnection viewer = viewers[v];
            if (viewer.State != EConnectionState.USOCK_Open || !viewer.bJoined || viewer.Actor is not PlayerController pc)
            {
                continue;
            }

            foreach (SBasePawn pawn in level.DynamicActors<SBasePawn>())
            {
                if (pawn.bDeleteMe || ReferenceEquals(pawn.Controller, pc) || viewer.FindActorChannel(pawn) is not { OpenAcked: true, Closing: false })
                {
                    continue;
                }

                Sample(pawn, viewer, pc, now, interest is null || viewer.IsRecorder ? 1f : interest.ProxySampleScale(pawn, _driver.ViewerFor(viewer)));
            }
        }

        foreach (SBasePawn pawn in level.DynamicActors<SBasePawn>())
        {
            _lastPhysics[pawn.ActorId] = pawn.Physics;
        }

        // Forget viewers and pawns that are gone.
        foreach (KeyValuePair<(int Pawn, NetConnection Viewer), ViewerState> kv in _states)
        {
            if (kv.Value.Pass != _pass)
            {
                _stale.Add(kv.Key);
            }
        }

        foreach ((int, NetConnection) key in _stale)
        {
            _states.Remove(key);
        }

        _stale.Clear();
    }

    private void Sample(SBasePawn pawn, NetConnection viewer, PlayerController pc, float now, float scale)
    {
        if (!_states.TryGetValue((pawn.ActorId, viewer), out ViewerState? s))
        {
            s = new ViewerState();
            _states[(pawn.ActorId, viewer)] = s;
        }

        s.Pass = _pass;
        // Rule 4: the Actor list never sends this pawn's Location, Velocity or Rotation; the samples carry them.
        pawn.bCompressedPosition = true;

        // Rule 1: a physics change, a new action batch or the first update forces a sample; root motion and
        // follow-parent send nothing otherwise.
        bool newBatch = pawn.fRepTimeOfLastActionChange > s.ActionTime;
        bool force = s.Initial || newBatch || (_lastPhysics.TryGetValue(pawn.ActorId, out EPhysics old) && old != pawn.Physics);
        if (!force && pawn.Physics is EPhysics.PHYS_RootMotion or EPhysics.PHYS_FollowParent)
        {
            return;
        }

        // Rule 2: dead reckoning from the reference this viewer last got.
        float dt = now - s.LastPositionTime;
        float minInterval = MinInterval;
        if (scale > 1f)
        {
            // Interest management (an OpenSCCT addition, NetDriverSettings.Interest): a distant viewer's samples are
            // spaced by the scale, forced ones included; a batch that waits goes with the next sample.
            minInterval = MinInterval * scale;
            if (!s.Initial && dt < minInterval)
            {
                return;
            }
        }

        if (!force)
        {
            FVector v = pawn.Velocity;
            if (dt >= ForcedInterval * scale)
            {
                force = true;
            }
            else if (v.IsZero() && !s.Velocity.IsZero())
            {
                force = true;
            }
            else if (dt >= minInterval)
            {
                bool falling = pawn.Physics == EPhysics.PHYS_Falling;
                FVector vPred = s.Velocity;
                FVector lPred = s.Location + s.Velocity * dt;
                if (falling)
                {
                    vPred.Z -= PredictionGravity * dt;
                    lPred.Z -= PredictionGravity * 0.5f * dt * dt;
                }

                float speed2 = v.SizeSquared();
                if ((v - vPred).SizeSquared() > VelocityDeviation * speed2)
                {
                    force = true;
                }
                else
                {
                    float limit = speed2 <= 1f ? PositionErrorStill : PositionErrorMoving;
                    force = (pawn.Location - lPred).SizeSquared() > limit;
                }
            }
        }

        // Rule 3: Rotation and the aim properties between samples, at most every 0.09 s (Rotation sooner only for a
        // turn of more than 1800 units from what the viewer last got); a forced sample carries Rotation instead.
        // Enhanced: every pass, for the client's interpolation buffer.
        if (Interpolating)
        {
            _aimDue.Add((pawn.ActorId, viewer));
            if (!force && pawn.Rotation != s.Rotation)
            {
                PushRotation(pawn, viewer, s);
            }

            s.LastAimTime = now;
        }
        else if (now - s.LastAimTime >= minInterval || s.Initial)
        {
            _aimDue.Add((pawn.ActorId, viewer));
            if (!force && pawn.Rotation != s.Rotation)
            {
                PushRotation(pawn, viewer, s);
            }

            s.LastAimTime = now;
        }
        else if (!force && YawDifference(pawn.Rotation.Yaw, s.Rotation.Yaw) > RotationPushYaw)
        {
            PushRotation(pawn, viewer, s);
        }

        float sinceActions = now - pawn.fRepTimeOfLastActionChange;
        if (!force)
        {
            // Rule 5: a recent batch is repeated alone, unreliably.
            if (sinceActions < ActionResendWindow && Send(pc, ActionsFunction, pawn))
            {
                ActionBatches++;
            }

            return;
        }

        // Rules 4 and 5: the sample (Location x 10, Rotation, Velocity) on the viewer's own controller, with the
        // batch while it is recent (reliably when this viewer has not had it).
        string function = sinceActions >= ActionsWithPositionWindow ? FunctionName
            : newBatch ? ReliablePositionAndActionsFunction
            : PositionAndActionsFunction;
        if (!Send(pc, function, pawn))
        {
            return;
        }

        if (!ReferenceEquals(function, FunctionName))
        {
            ActionBatches++;
        }

        s.ActionTime = pawn.fRepTimeOfLastActionChange;
        s.LastPositionTime = now;
        s.Location = pawn.Location;
        s.Velocity = pawn.Velocity;
        s.Rotation = pawn.Rotation;
        s.Initial = false;
        Samples++;
    }
    private void PushRotation(SBasePawn pawn, NetConnection viewer, ViewerState s)
    {
        _rotationPushes.Add((pawn.ActorId, viewer));
        s.Rotation = pawn.Rotation;
        RotationPushes++;
    }

    /// <summary>The driver's push test: this pass pushed the pawn's Rotation to this viewer.</summary>
    private bool PushesRotation(Actor actor, NetConnection viewer, RepProperty property) =>
        actor is SBasePawn && _rotationPushes.Contains((actor.ActorId, viewer)) &&
        string.Equals(property.Name, "Rotation", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The driver's withhold test (rule 3): a pawn's aim properties go to a viewer only on a pass whose aim block ran
    /// for it; a change held back goes with the next one.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "net-proxy.md rule 3: iDesiredYawPitch and iYawPitchFromView pushed if changed when 0.09 s passed since LastAimTime")]
    private bool WithholdsAim(Actor actor, NetConnection viewer, RepProperty property) =>
        actor is SBasePawn && !_aimDue.Contains((actor.ActorId, viewer)) &&
        (string.Equals(property.Name, "iDesiredYawPitch", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(property.Name, "iYawPitchFromView", StringComparison.OrdinalIgnoreCase));

    /// <summary>The shortest-arc yaw difference in rotator units (0 to 32768).</summary>
    private static int YawDifference(int a, int b)
    {
        int d = (a - b) & 0xFFFF;
        return d > 0x7FFF ? 0x10000 - d : d;
    }

    /// <summary>One of the client calls of rule 5 for <paramref name="pawn"/> on the viewer's controller.</summary>
    private bool Send(PlayerController pc, string function, SBasePawn pawn)
    {
        RepFunction? f = _driver.Classes.Find(pc.GetType())?.Layout.FindFunction(function);
        if (f is null)
        {
            return false;
        }

        _args.Clear();
        Span<int> actions = stackalloc int[6];
        int count = Math.Clamp(pawn.iNbActionForDistantClient, 0, 6);
        for (int i = 0; i < count; i++)
        {
            actions[i] = pawn.AllActions[i];
        }

        MovePacking.CompressActions(actions, count, out int val0, out int val1);
        foreach (RepParam p in f.Parameters)
        {
            NetValue value = p.Name switch
            {
                "WhichPawn" => NetValue.FromObject(pawn),
                "CompPosition" => new NetValue { V = pawn.Location * 10f, V2 = pawn.Velocity, R = pawn.Rotation },
                "Val0" => NetValue.FromInt(val0),
                "Val1" => NetValue.FromInt(val1),
                "fRepTimeOfLastActionChange" => NetValue.FromFloat(pawn.fRepTimeOfLastActionChange),
                "ZoneConfigForClient" => NetValue.FromInt(pawn.ZoneConfigForClient),
                "Target" => NetValue.FromObject(pawn.m_AttackTarget),
                "AttackedBy" => NetValue.FromObject(pawn.m_AttackedBy),
                "iSpecialYaw" => NetValue.FromInt(pawn.SpecialYaw),
                _ => default,
            };
            _args.Values[p.ValueOffset] = value;
        }

        _driver.ProcessRemoteFunction(pc, function, _args);
        return true;
    }
}

/// <summary>
/// A client's side of other players' pawns: the position samples (<c>ClientGetPawnPosition</c>, state-and-network.md
/// actions rule 6) set the proxy's ErrorDistance, NormErrDist, ReceivedRotation and ReceivedVelocity, and each proxy's
/// own tick runs ASBasePawn's simulated tick (<see cref="Tick"/>, net-proxy.md rules 10 to 15) in place of the stock
/// physics: the received velocity carried by the proxy's simulated walking (or a swept move), the error blended in at
/// up to 200 uu/s walking (ramping over the second after a sample) or 400 uu/s otherwise, a snap beyond six collision
/// radii, and the yaw turned toward the received one.
/// <para>The action batches (proxy-pawns.md rules 3 and 4, GetNewActions), their playback (rule 8) and the zone
/// configuration (rule 9) are OpenScct.Game.Animation.ProxyAnimation's, run first in the tick. The aim
/// (iDesiredYawPitch) follows the received one smoothed, as in rule 15.</para>
/// <para>Installed with a <see cref="PawnInterpolation"/> (Enhanced, <see cref="PawnProxyMode.Interpolate"/>), the
/// samples, Rotation and aim go into its buffer instead and the tick shows the buffered state at the render time
/// (<see cref="TickInterpolated"/>); the actions and zone configuration run as in Stock.</para>
/// </summary>
[UcNative("ASBasePawn::TickSimulated", Editor = 0x1121dc30)]
[PortOf("eng:ASBasePawn::TickSimulated", Spec = "spec/native/net-proxy.md#client-proxy-tick-ticksimulated-every-frame-for-a-simulated-pawn")]
public static class PawnProxyReceiver
{
    [Provenance(ProvenanceKind.Decoded, "net-proxy.md constants: snap distance 6 x CollisionRadius")]
    public const float SnapRadii = 6f;

    [Provenance(ProvenanceKind.Decoded, "net-proxy.md constants: correction speed walking 200, other 400 uu/s")]
    public const float WalkingCorrection = 200f;

    public const float OtherCorrection = 400f;

    [Provenance(ProvenanceKind.Decoded, "net-proxy.md constants: yaw interpolation threshold 1299, turn rates 65535 and 131071")]
    public const int YawInterpolationThreshold = 1299;

    public const float YawTurnRate = 65535f;

    public const float SlowYawTurnRate = 131071f;

    /// <summary>Below this speed (uu/s) the yaw turns at the faster rate.</summary>
    [Provenance(ProvenanceKind.Decoded, "net-proxy.md rule 15: 131071 x dt under 10 uu/s (editor 0x1121dc30)")]
    public const float SlowTurnSpeed = 10f;

    [Provenance(ProvenanceKind.Decoded, "net-proxy.md constants: proxy gravity 1500")]
    public const float ProxyGravity = 1500f;

    /// <summary>The aim yaw smoothing window: at or above it a frame takes the whole offset change (seconds).</summary>
    [Provenance(ProvenanceKind.Decoded, "TickSimulated editor 0x1121dc30: Level.m_dT compared with 0.3 (0x1147e204), blend divided by 0.3 (x 3.3333 at 0x114a2d5c)")]
    public const float AimSmoothWindow = 0.3f;

    /// <summary>The aim pitch turn rate toward the received pitch (units per second of Level.m_dT).</summary>
    [Provenance(ProvenanceKind.Decoded, "TickSimulated editor 0x1121dc30: m_dT x 16384 (0x11476cb8) as the fixed turn step")]
    public const float AimPitchTurnRate = 16384f;

    /// <summary>
    /// Registers the sample handlers on a client driver and installs the proxies' simulated tick in its level. With
    /// <paramref name="interpolation"/> (Enhanced) the proxies are shown from its buffer; without, as in Stock.
    /// </summary>
    public static void Install(Level level, NetDriver driver, PawnInterpolation? interpolation = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(driver);
        foreach (string function in new[] { "ClientGetPawnPosition", "ClientGetPawnPositionAndActions", "ClientReliableGetPawnPositionAndActions", "ClientGetPawnActions" })
        {
            driver.Functions.Register("SBase.SPlayerController", function,
                (self, f, args, from) => Receive(self, f, args, from, driver, interpolation));
        }

        var receive = new NetReceive(interpolation);
        driver.PreNetReceive = receive.Pre;
        driver.PostNetReceive = receive.Post;
        InstallTick(level, interpolation);
    }

    /// <summary>
    /// ASBasePawn's native PreNetReceive and PostNetReceive (editor 0x111f8c60 and 0x1120be70, rule 15a): a Rotation
    /// received as a property (rule 3) does not turn a simulated proxy; it becomes the proxy's ReceivedRotation, which
    /// the proxy tick then turns toward (rule 15). The pawn keeps the rotation it had before the bunch. Before this
    /// the property turned the proxy at once and the next tick turned it back toward the last sample's yaw, so a bot
    /// turning in place swung back and forth (1.9 times the server's yaw travel).
    /// </summary>
    private sealed class NetReceive(PawnInterpolation? interpolation)
    {
        private SBasePawn? _pawn;
        private bool _buffered;
        private Mover? _mover;
        private FVector _simInterpolate;
        private FRotator _rotation;
        private int _aim;
        private Actor? _meshActor;
        private AssetRef _mesh;
        private EDrawType _drawType;

        [UcNative("ASBasePawn::PreNetReceive", Editor = 0x111f8c60)]
        [PortOf("eng:ASBasePawn::PreNetReceive", Spec = "spec/native/net-proxy.md#client-proxy-tick-ticksimulated-every-frame-for-a-simulated-pawn")]
        public void Pre(Actor actor)
        {
            _mover = actor as Mover;
            _simInterpolate = _mover?.SimInterpolate ?? default;
            _meshActor = actor;
            _mesh = actor.StaticMesh;
            _drawType = actor.DrawType;
            _pawn = actor as SBasePawn;
            if (_pawn is not null)
            {
                _rotation = _pawn.Rotation;
                _aim = _pawn.iDesiredYawPitch;
                _buffered = interpolation is not null && _pawn.Role == ENetRole.ROLE_SimulatedProxy;
                if (_buffered)
                {
                    interpolation!.BeginReceive(_pawn);
                }
            }
        }

        [UcNative("ASBasePawn::PostNetReceive", Editor = 0x1120be70)]
        [PortOf("eng:ASBasePawn::PostNetReceive", Spec = "spec/native/net-proxy.md#client-proxy-tick-ticksimulated-every-frame-for-a-simulated-pawn")]
        [Provenance(ProvenanceKind.Decoded, "net-proxy.md rule 15a: a received iDesiredYawPitch becomes iReceivedDesiredYawPitch and the kept aim stays for the rule 15 smoothing")]
        [Provenance(ProvenanceKind.Decoded, "editor 0x1120be70: the swap back of Rotation (no move for a pawn without a base or on world geometry), then ReceivedRotation = the received Rotation for ROLE_SimulatedProxy outside PHYS_FollowParent")]
        public void Post(Actor actor)
        {
            PostMesh(actor);
            PostMover(actor);
            if (_pawn is not { } pawn || !ReferenceEquals(pawn, actor))
            {
                return;
            }

            _pawn = null;
            if (_buffered)
            {
                // Enhanced: the received aim words go into the buffer; the shown ones stay until the tick.
                interpolation!.EndReceive(pawn);
            }
            else if (pawn.iDesiredYawPitch != _aim)
            {
                pawn.iReceivedDesiredYawPitch = pawn.iDesiredYawPitch;
                pawn.iDesiredYawPitch = _aim;
            }

            FRotator received = pawn.Rotation;
            if (received == _rotation || (pawn.Base is { } b && !b.bWorldGeometry))
            {
                return;
            }

            pawn.Rotation = _rotation;
            if (pawn.Role == ENetRole.ROLE_SimulatedProxy && pawn.Physics != EPhysics.PHYS_FollowParent)
            {
                pawn.ReceivedRotation = received;
            }
        }

        /// <summary>
        /// AMover's native net receive (rep-map-actors.md rule 3): a mover whose SimInterpolate changed in the bunch
        /// starts the server's leg on this client. Before this, client movers (doors, shutters, lifts) only snapped to
        /// RealPosition every 4 to 8 s.
        /// </summary>
        [UcNative("AMover::PostNetReceive", Editor = 0x110b3020)]
        [PortOf("eng:AMover::PostNetReceive", Spec = "spec/net/rep-map-actors.md#rules")]
        private void PostMover(Actor actor)
        {
            if (_mover is not { } mover || !ReferenceEquals(mover, actor))
            {
                return;
            }

            _mover = null;
            if (mover.SimInterpolate != _simInterpolate)
            {
                mover.ClientStartInterpolation();
            }
        }

        /// <summary>
        /// A received StaticMesh (a swapped gameplay prop) takes the collision of the new mesh on the client too, as the
        /// engine's collision is the mesh's own: the same collider switch as the server's SetStaticMesh. A static mesh
        /// actor whose DrawType became DT_None (a broken pane or vase, natives2-damage.md rule 1.6: the mesh is kept)
        /// has no collision left, and gets the mesh's back when it is drawn again (a reset). The server turns it off
        /// with SetCollision, whose flags reach a client only in the first bunch. Before this a client kept the broken
        /// pane's colliders.
        /// </summary>
        [Provenance(ProvenanceKind.Estimated, "Static mesh collision follows the actor's mesh and draw type on every machine; the client applies it when they arrive")]
        private void PostMesh(Actor actor)
        {
            if (!ReferenceEquals(_meshActor, actor))
            {
                return;
            }

            _meshActor = null;
            bool drawn = actor.DrawType != EDrawType.DT_None;
            bool wasDrawn = _drawType != EDrawType.DT_None;
            if (actor.StaticMesh != _mesh || (drawn != wasDrawn && !actor.StaticMesh.IsNone))
            {
                actor.XLevel?.GetService<IActorNatives>()?.SetStaticMesh(actor, drawn ? actor.StaticMesh : default);
            }
        }
    }

    /// <summary>
    /// Makes <see cref="Tick"/> (or, with <paramref name="interpolation"/>, <see cref="TickInterpolated"/>) the simulated
    /// tick of every SBasePawn proxy in <paramref name="level"/>.
    /// </summary>
    public static void InstallTick(Level level, PawnInterpolation? interpolation = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.SetService<ISimulatedTick>(new SimulatedPawnTick(interpolation));
    }

    /// <summary>The level's simulated tick for SBasePawn proxies (the pawn's own tick runs <see cref="Tick"/>).</summary>
    private sealed class SimulatedPawnTick(PawnInterpolation? interpolation) : ISimulatedTick
    {
        public bool TickSimulated(Actor Actor, float DeltaSeconds)
        {
            if (Actor is not SBasePawn pawn)
            {
                return false;
            }

            if (interpolation is not null)
            {
                TickInterpolated(pawn, interpolation, DeltaSeconds);
            }
            else
            {
                Tick(pawn, DeltaSeconds);
            }

            return true;
        }
    }

    private static void Receive(Actor self, RepFunction f, NetCallArgs args, NetConnection from, NetDriver driver,
        PawnInterpolation? interpolation)
    {
        SBasePawn? pawn = null;
        NetValue position = default;
        bool havePosition = false;
        foreach (RepParam p in f.Parameters)
        {
            if (p.Kind == RepLeafKind.Object && pawn is null)
            {
                pawn = args.Values[p.ValueOffset].O as SBasePawn;
            }
            else if (p.Kind == RepLeafKind.CompressedPosition)
            {
                position = args.Values[p.ValueOffset];
                havePosition = true;
            }
        }

        if (pawn is not { bDeleteMe: false, Role: ENetRole.ROLE_SimulatedProxy })
        {
            return;
        }

        if (havePosition)
        {
            if (interpolation is not null)
            {
                pawn.ReceivedRotation = position.R;
                pawn.ReceivedVelocity = position.V2;
                interpolation.OnSample(pawn, driver.ReceivingTick, position.V * 0.1f, position.V2);
            }
            else
            {
                ApplySample(pawn, position.V * 0.1f, position.R, position.V2);
            }
        }

        if (f.Name != "ClientGetPawnPosition")
        {
            ReceiveActions(pawn, f, args);
        }
    }

    /// <summary>
    /// The actions part of ClientGetPawnActions and ClientGetPawnPositionAndActions (proxy-pawns.md rule 3): a batch
    /// newer than the one last played becomes the pawn's (GetNewActions); the grab partners and the special yaw are
    /// taken from every call.
    /// </summary>
    [Uc("SBase.SPlayerController.ClientGetPawnActions", Sha1 = "4b6244aa9f66c963ca322cc003942fbdecd2eabe")]
    [PortOf("uc:SBase.SPlayerController:ClientGetPawnActions", Spec = "spec/net/proxy-pawns.md#rules")]
    private static void ReceiveActions(SBasePawn pawn, RepFunction f, NetCallArgs args)
    {
        int val0 = 0;
        int val1 = 0;
        float time = 0f;
        byte zc = 0;
        SBasePawn? target = null;
        SBasePawn? attackedBy = null;
        int specialYaw = 0;
        foreach (RepParam p in f.Parameters)
        {
            NetValue v = args.Values[p.ValueOffset];
            switch (p.Name)
            {
                case "Val0": val0 = v.I; break;
                case "Val1": val1 = v.I; break;
                case "fRepTimeOfLastActionChange": time = v.F; break;
                case "ZoneConfigForClient": zc = (byte)v.I; break;
                case "Target": target = v.O as SBasePawn; break;
                case "AttackedBy": attackedBy = v.O as SBasePawn; break;
                case "iSpecialYaw": specialYaw = v.I; break;
                default: break;
            }
        }

        if (time > pawn.fOldTimeOfLastActionChange)
        {
            Span<int> actions = stackalloc int[6];
            int count = MovePacking.UnCompressActions(val0, val1, actions);
            OpenScct.Game.Animation.ProxyAnimation.GetNewActions(pawn, actions, count, time, zc);
        }

        pawn.m_AttackedBy = attackedBy;
        pawn.m_AttackTarget = target;
        pawn.SpecialYaw = specialYaw;
    }

    /// <summary>The sample's effect on the proxy (state-and-network.md actions rule 6).</summary>
    [Uc("SBase.SPlayerController.ClientGetPawnPosition", Sha1 = "32f5d3ef1fbbad459edd4a6a8a03aaa9c4b9a685")]
    [PortOf("uc:SBase.SPlayerController:ClientGetPawnPosition", Spec = "spec/movement/state-and-network.md#actions-changeenumaction-and-executeaction")]
    public static void ApplySample(SBasePawn pawn, FVector location, FRotator rotation, FVector velocity)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        if (pawn.Physics != EPhysics.PHYS_FollowParent)
        {
            FVector error = location - pawn.Location;
            if (error != pawn.ErrorDistance)
            {
                pawn.fSmoothTime = 0f;
            }

            pawn.ErrorDistance = error;
            pawn.NormErrDist = error.SafeNormal();
        }

        pawn.ReceivedRotation = rotation;
        pawn.ReceivedVelocity = velocity;
    }

    /// <summary>
    /// ASBasePawn::TickSimulated for one proxy (rules 8 to 15; the stock physics does not run for it): the relayed
    /// actions and zone configuration, the received velocity, the snap on a physics change, then the position: with
    /// no error left a walking proxy walks by its velocity (physWalkingSimulated) and any other moves by it
    /// (ClientDistantMoveSmooth); an error beyond six collision radii is applied at once; a smaller one is blended in
    /// at up to 200 uu/s walking (ramping over the second after a sample) or 400 uu/s otherwise. Last the yaw turns
    /// toward the received one. Root motion and follow-parent run the stock physics instead of the position and
    /// rotation steps.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "net-proxy.md rules 10 to 15 (editor 0x1121dc30); the ClientSmoothFlags 7 path")]
    [Provenance(ProvenanceKind.Decoded, "net-proxy.md rule 15 aim: iDesiredYawPitch toward iReceivedDesiredYawPitch (editor 0x1121dc30)")]
    public static void Tick(SBasePawn pawn, float dt)
    {
        ArgumentNullException.ThrowIfNull(pawn);

        // Rules 8 and 9: the relayed actions and the zone configuration.
        OpenScct.Game.Animation.ProxyAnimation.TickSimulated(pawn);
        if (pawn.bDeleteMe || dt <= 0f)
        {
            return;
        }

        PhysicsDriver? driver = pawn.XLevel.PhysicsDriver as PhysicsDriver;

        // Rule 10: the acceleration along the old velocity, then the received velocity (gravity while falling).
        pawn.Acceleration = pawn.Velocity.SafeNormal();
        if (pawn.Physics == EPhysics.PHYS_Falling)
        {
            pawn.ReceivedVelocity.Z -= ProxyGravity * dt;
        }

        pawn.Velocity = pawn.Physics == EPhysics.PHYS_None ? default : pawn.ReceivedVelocity;

        // Rule 11: a physics change applies the whole error and the received rotation at once.
        if (pawn.OldPhysics != pawn.Physics && !pawn.bInternalChangePhysics)
        {
            MoveNoWorld(pawn, driver, pawn.ErrorDistance, pawn.ReceivedRotation);
            pawn.ErrorDistance = default;
            pawn.iDesiredYawPitch = pawn.iReceivedDesiredYawPitch;
        }

        pawn.bInternalChangePhysics = false;

        // Rule 12: root motion and follow-parent keep the stock physics.
        if (pawn.Physics is EPhysics.PHYS_RootMotion or EPhysics.PHYS_FollowParent)
        {
            driver?.performPhysics(pawn, dt);
            return;
        }

        SmoothPosition(pawn, driver, dt);
        if (pawn.bDeleteMe)
        {
            return;
        }

        TurnProxy(pawn, driver, dt);
    }

    /// <summary>
    /// The Enhanced proxy tick: the relayed actions and zone configuration (rules 8 and 9) and the stock physics in root
    /// motion and follow-parent (rule 12) as in <see cref="Tick"/>; otherwise
    /// the pawn takes its buffered state at the render time: position (a move with world collision off, as the snaps),
    /// velocity and acceleration (the animation's inputs), rotation and both aim words.
    /// </summary>
    public static void TickInterpolated(SBasePawn pawn, PawnInterpolation interpolation, float dt)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        ArgumentNullException.ThrowIfNull(interpolation);
        OpenScct.Game.Animation.ProxyAnimation.TickSimulated(pawn);
        if (pawn.bDeleteMe || dt <= 0f)
        {
            return;
        }

        // Rule 11's snap is not needed: Physics arrives at once while the position is shown late, so snapping to the
        // newest state would pop the pawn forward by the delay at every jump and landing. The forced sample of the change
        // rides the buffer; a teleport still snaps (PawnInterpolation).
        PhysicsDriver? driver = pawn.XLevel.PhysicsDriver as PhysicsDriver;
        pawn.bInternalChangePhysics = false;
        pawn.ErrorDistance = default;
        if (pawn.Physics is EPhysics.PHYS_RootMotion or EPhysics.PHYS_FollowParent)
        {
            driver?.performPhysics(pawn, dt);
            interpolation.Follow(pawn);
            return;
        }

        if (!interpolation.TrySample(pawn, out ProxyState state))
        {
            return;
        }

        pawn.Velocity = pawn.Physics == EPhysics.PHYS_None ? default : state.Velocity;
        pawn.Acceleration = pawn.Velocity.SafeNormal();
        FVector delta = state.Location - pawn.Location;
        if (!delta.IsZero() || state.Rotation != pawn.Rotation)
        {
            MoveNoWorld(pawn, driver, delta, state.Rotation);
        }

        pawn.iDesiredYawPitch = state.Aim;
        pawn.iReceivedDesiredYawPitch = state.Aim;
        pawn.iSmoothDifYaw = 0;
        pawn.iYawPitchFromView = state.View;
    }

    /// <summary>Rule 13 (ClientSmoothFlags 7: bits 1 and 4 set, bit 8 clear).</summary>
    private static void SmoothPosition(SBasePawn pawn, PhysicsDriver? driver, float dt)
    {
        FVector e = pawn.ErrorDistance;
        bool walking = pawn.Physics == EPhysics.PHYS_Walking;
        FVector before = pawn.Location;
        if (e.IsZero())
        {
            if (pawn.Velocity.IsZero())
            {
                return;
            }

            if (walking)
            {
                WalkSimulated(pawn, driver, pawn.Velocity, dt);
            }
            else
            {
                ClientDistantMoveSmooth(pawn, driver, pawn.Velocity * dt);
            }

            return;
        }

        float size = e.Size();
        if (size > SnapRadii * pawn.CollisionRadius)
        {
            MoveNoWorld(pawn, driver, pawn.Velocity * dt + e, pawn.Rotation);
            pawn.ErrorDistance = default;
            return;
        }

        float vmax;
        if (walking)
        {
            pawn.fSmoothTime += dt;
            vmax = WalkingCorrection;
        }
        else
        {
            pawn.fSmoothTime = 1f;
            vmax = OtherCorrection;
        }

        float vc = MathF.Min(vmax, vmax * pawn.fSmoothTime);
        FVector c = pawn.NormErrDist * vc;
        if (size < vc * dt)
        {
            // The last step: the rest of the error with this tick's move.
            if (walking)
            {
                WalkSimulated(pawn, driver, (e * (1f / dt)) + pawn.Velocity, dt);
            }
            else
            {
                ClientDistantMoveSmooth(pawn, driver, (pawn.Velocity * dt) + e);
            }

            pawn.Velocity = (pawn.Location - before) * (1f / dt);
            pawn.ErrorDistance = default;
            return;
        }

        pawn.ErrorDistance = e - (c * dt);
        if (walking)
        {
            WalkSimulated(pawn, driver, pawn.Velocity + c, dt);
        }
        else
        {
            ClientDistantMoveSmooth(pawn, driver, (pawn.Velocity + c) * dt);
            if (pawn.Location != before)
            {
                pawn.Velocity = (pawn.Location - before) * (1f / dt);
            }
        }
    }

    /// <summary>A walking proxy's move: vDeltaMove, then physWalkingSimulated (which sets Velocity).</summary>
    private static void WalkSimulated(SBasePawn pawn, PhysicsDriver? driver, FVector deltaMove, float dt)
    {
        pawn.vDeltaMove = deltaMove;
        if (driver is not null)
        {
            driver.PhysWalkingSimulated(pawn, dt);
        }
        else
        {
            pawn.Location += deltaMove * dt;
        }
    }

    /// <summary>
    /// ClientDistantMoveSmooth(D) (native, editor 0x11206650, rule 14): a swept move by D; when blocked, the rest of D
    /// slid along the hit plane is tried once unless it points backwards.
    /// </summary>
    [UcNative("ASBasePawn::execClientDistantMoveSmooth", Editor = 0x11206850)]
    [PortOf("nat:SBase.SBasePawn.ClientDistantMoveSmooth", Spec = "spec/native/net-proxy.md#client-proxy-tick-ticksimulated-every-frame-for-a-simulated-pawn")]
    [Provenance(ProvenanceKind.Decoded, "net-proxy.md rule 14 (editor 0x11206650)")]
    public static void ClientDistantMoveSmooth(SBasePawn pawn, PhysicsDriver? driver, FVector d)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        if (driver is null)
        {
            pawn.Location += d;
            return;
        }

        driver.MoveActor(pawn, d, pawn.Rotation, out FCheckResult hit);
        if (!hit.bHit || hit.Time >= 1f || pawn.bDeleteMe)
        {
            return;
        }

        FVector n = hit.Normal;
        FVector slide = (d - (n * (d | n))) * (1f - hit.Time);
        if ((slide | d) >= 0f)
        {
            driver.MoveActor(pawn, slide, pawn.Rotation, out _);
        }
    }

    /// <summary>A move with world collision off (the snaps of rules 11 and 13).</summary>
    private static void MoveNoWorld(SBasePawn pawn, PhysicsDriver? driver, FVector delta, FRotator rotation)
    {
        if (driver is null)
        {
            pawn.Location += delta;
            pawn.Rotation = rotation;
            return;
        }

        bool collideWorld = pawn.bCollideWorld;
        pawn.bCollideWorld = false;
        driver.MoveActor(pawn, delta, rotation, out _);
        pawn.bCollideWorld = collideWorld;
    }

    /// <summary>Rule 15 (ClientSmoothFlags bit 2): the rotation toward the received one.</summary>
    private static void TurnProxy(SBasePawn pawn, PhysicsDriver? driver, float dt)
    {
        // The received rotation as it is when the yaw already matches; else the yaw turned toward it, pitch level and
        // the roll levelled with the received one.
        int yaw = pawn.Rotation.Yaw;
        int target = pawn.ReceivedRotation.Yaw;
        int pitch = pawn.ReceivedRotation.Pitch;
        int roll = pawn.ReceivedRotation.Roll;
        if (yaw != target)
        {
            pawn.bNoInterpoleSimulatedRotation = pawn.BTW != BTW_.NOBTW;
            if (Math.Abs(yaw - target) > YawInterpolationThreshold && !pawn.bNoInterpoleSimulatedRotation)
            {
                float rate = pawn.Velocity.Size() < SlowTurnSpeed ? SlowYawTurnRate : YawTurnRate;
                yaw = TurnToward(yaw, target, (int)(rate * dt));
            }
            else
            {
                yaw = target;
            }

            pitch = 0;
            roll = pawn.ReceivedRotation.Roll == 0 ? 0 : pawn.Rotation.Roll;
        }

        SmoothAim(pawn, yaw);
        var rotation = new FRotator(pitch, yaw, roll);
        if (rotation != pawn.Rotation)
        {
            if (driver is not null)
            {
                driver.MoveActor(pawn, default, rotation, out _);
            }
            else
            {
                pawn.Rotation = rotation;
            }
        }
    }

    /// <summary>
    /// Rule 15, the aim: while the kept aim differs from the received one, the aim yaw keeps a smoothed offset from
    /// the body's new yaw and the aim pitch turns toward the received pitch at 16384 units per second (editor
    /// 0x1121dc30, after the yaw step and before the rotation move).
    /// </summary>
    private static void SmoothAim(SBasePawn pawn, int newYaw)
    {
        int current = pawn.iDesiredYawPitch;
        int received = pawn.iReceivedDesiredYawPitch;
        if (current == received)
        {
            return;
        }

        int delta = ((newYaw - (received & 0xFFFF)) - (pawn.Rotation.Yaw - (current & 0xFFFF))) & 0xFFFF;
        if (delta > 0x7FFF)
        {
            delta -= 0x10000;
        }

        int receivedPitch = received >> 16;
        if (receivedPitch > 0x7FFF)
        {
            receivedPitch -= 0x10000;
        }

        float mdt = pawn.Level.m_dT;
        if (mdt <= 0f)
        {
            // A frame without time: the blend below would divide by zero (a guard of the port).
            return;
        }

        pawn.iSmoothDifYaw = mdt >= AimSmoothWindow
            ? delta
            : SvmMath.Trunc((((AimSmoothWindow - mdt) / mdt * pawn.iSmoothDifYaw) + delta) * mdt * (1f / AimSmoothWindow));
        int aimYaw = newYaw - pawn.iSmoothDifYaw;
        int aimPitch = FixedTurn(mdt * AimPitchTurnRate, current >> 16, receivedPitch);
        pawn.iDesiredYawPitch = (aimPitch << 16) | (aimYaw & 0xFFFF);
    }

    /// <summary>The engine's fixed turn (editor 0x10fdeef0): both angles as 16-bit, the shorter way, at most <paramref name="step"/>.</summary>
    private static int FixedTurn(float step, int current, int target)
    {
        current &= 0xFFFF;
        target &= 0xFFFF;
        if (current == target)
        {
            return current;
        }

        int diff = (target - current) & 0xFFFF;
        int sign = 1;
        if (diff > 0x7FFF)
        {
            diff = 0x10000 - diff;
            sign = -1;
        }

        return step < diff ? SvmMath.Trunc(current + sign * step) & 0xFFFF : target;
    }

    private static int TurnToward(int from, int to, int step)
    {
        int delta = ((to - from) & 0xFFFF) - ((to - from) & 0x8000) * 2;
        if (Math.Abs(delta) <= step)
        {
            return to;
        }

        return from + Math.Sign(delta) * step;
    }
}
