using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SAnimatedMesh: a skeletal prop that plays its sequences when triggered, carries actors on its bones and
/// lets its sequences' notifies fire events and sounds (the SteeE crane and collapsing wall), written from
/// spec/devices/animated-mesh.md.
/// <para>
/// The prop plays one channel. Sequence data (frame count, rate, notifies) comes from the level's
/// <see cref="IMeshAnimations"/> service. The channel frame is normalised over the frame count N and advances by
/// rate / N per second; a sequence played forwards ends at (N - 1) / N, backwards at 0.
/// </para>
/// </summary>
public partial class SAnimatedMesh
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates,
        s_AnimateBackward.Instance, s_AnimateForward.Instance, s_Wait.Instance);

    public override StateTable GetStateTable() => ClassStates;

    // Channel 0 (native animation state).
    private MeshAnimSequence? _sequence;
    private float _frame;
    private float _rate;
    private bool _playing;
    private bool _backward;
    private bool _fromStart;
    private int _plays;

    // A client's proxy: the replicated SimAnim it last applied, and whether the server said the play repeats.
    private FAnimRep _followed;
    private bool _following;
    private bool _loop;
    private bool _channelInitialized;
    private bool _initialStatePending;

    /// <summary>Enter the auto state only after PostNetBeginPlay has prepared and frozen the initial pose.</summary>
    protected override void SetInitialState()
    {
        if (!_channelInitialized)
        {
            _initialStatePending = true;
            return;
        }

        base.SetInitialState();
    }

    /// <summary>The sequence on the channel, or null.</summary>
    public MeshAnimSequence? AnimSequence => _sequence;

    /// <summary>The channel frame, normalised over the sequence's frame count.</summary>
    public float AnimFrame => _frame;

    /// <summary>True while the channel plays (not frozen and not ended).</summary>
    public bool IsAnimating => _playing;

    /// <summary>Normalised frames per second the channel moves while playing (negative backwards, 0 when held).</summary>
    public float AnimFrameRate => !_playing ? 0f : _backward ? -_rate : _rate;

    /// <summary>True on a client's proxy while the server replays the sequence (the channel wraps at its end).</summary>
    public bool AnimLooping => _loop;

    /// <summary>Times the channel started a sequence (views count starts from it).</summary>
    public int AnimStarts { get; private set; }

    /// <summary>
    /// Hangs the attachments on their bones (every machine); on the authority: no physics, drawn as a mesh, the
    /// loop counts padded to the sequence count (1 each, 0 for the last when chained), and the first sequence's
    /// first frame shown, not moving.
    /// </summary>
    [Uc("SBase.SAnimatedMesh.PostNetBeginPlay", Sha1 = "551a9bce42393bfe867c1ba52d7b34dfa536e2d7")]
    [PortOf("uc:SBase.SAnimatedMesh:PostNetBeginPlay", Spec = "spec/devices/animated-mesh.md#rules")]
    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        foreach (FAttachActor attach in ActorsToAttach)
        {
            AttachActorToBone(attach.ActorName, attach.BoneName);
        }

        if (Role != ENetRole.ROLE_Authority)
        {
            EnterPreparedInitialState();
            return;
        }

        SetPhysics(EPhysics.PHYS_None);
        SetDrawType(EDrawType.DT_Mesh);
        for (int i = LoopStatus_0forInfinite.Count; i < Animations.Count; i++)
        {
            LoopStatus_0forInfinite.Insert(i, bChainedAnims && i == Animations.Count - 1 ? 0 : 1);
        }

        iCurrentAnim = 0;
        if (Animations.Count > 0)
        {
            PlayAnim(Animations[0], 1f, false);
        }

        FreezeAnimAt(0f);
        EnterPreparedInitialState();
    }

    private void EnterPreparedInitialState()
    {
        // Level's startup passes currently request the auto state before PostNetBeginPlay. Starting an automatic
        // prop there plays it before its loop counts exist, then FreezeAnimAt above stops it permanently.
        _channelInitialized = true;
        if (_initialStatePending)
        {
            _initialStatePending = false;
            eventSetInitialState();
        }
    }

    /// <summary>Global version: any end goes back to s_Wait.</summary>
    [Uc("SBase.SAnimatedMesh.AnimEnd", Sha1 = "baccfa15e24f44a7f55ddbd0fa0b247e9ad77f95")]
    [PortOf("uc:SBase.SAnimatedMesh:AnimEnd", Spec = "spec/devices/animated-mesh.md#rules")]
    protected override void AnimEnd(int Channel) => GotoState(nameof(s_Wait));

    /// <summary>Plays the current sequence forwards (rate 1) and makes the prop visible, with root-motion physics.</summary>
    private void PlayCurrentForward()
    {
        if (iCurrentAnim >= 0 && iCurrentAnim < Animations.Count)
        {
            PlayAnim(Animations[iCurrentAnim], 1f, false);

            // The replicated loop flag: set while plays of this sequence are left, so clients wrap the channel
            // instead of waiting for a SimAnim that does not change when the same sequence starts again.
            SimAnim = SimAnim with { AnimLoop = (byte)(iLoopLeft != 0 ? 1 : 0) };
        }

        bHidden = false;
        SetPhysics(EPhysics.PHYS_None);
        SetPhysics(EPhysics.PHYS_RootMotion);
    }

    // ---- native animation ---------------------------------------------------------------------------------

    /// <summary>
    /// Starts <paramref name="Sequence"/> on channel 0 at <paramref name="Rate"/> from its first frame (its end
    /// frame when backward). An unknown sequence leaves the channel as it is.
    /// </summary>
    [UcNative("AActor::execPlayAnim", Exe = 0x10a23610)]
    [PortOf("nat:Engine.Actor.PlayAnim", Spec = "spec/devices/animated-mesh.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Channel timing rule of animated-mesh.md rule 9; tweening is not modelled (props play with no tween)")]
    private void PlayAnim(Name Sequence, float Rate, bool bBackward)
    {
        MeshAnimSequence? sequence = XLevel.GetService<IMeshAnimations>()?.FindSequence(Mesh, Sequence);
        if (sequence is null)
        {
            return;
        }

        StartChannel(sequence, Rate, bBackward);

        // A backward play starts at its end frame, so the replicated start frame is the end (255): clients read it
        // as the direction, which SimAnim has no field for.
        SimAnim = new FAnimRep
        {
            AnimSequence = sequence.Name,
            AnimRate = (byte)SvmMath.Clamp(SvmMath.Trunc(Rate * 64f), 0, 255),
            AnimFrame = (byte)(bBackward ? BackwardStartFrame : 0),
        };
    }

    /// <summary>The replicated start frame that marks a backward play.</summary>
    private const int BackwardStartFrame = 255;

    /// <summary>Starts <paramref name="sequence"/> on channel 0 from its first frame (its end frame when backward).</summary>
    private void StartChannel(MeshAnimSequence sequence, float Rate, bool bBackward)
    {
        _sequence = sequence;
        _rate = Rate * sequence.Rate / sequence.NumFrames;
        _backward = bBackward;
        _frame = bBackward ? EndFrame(sequence) : 0f;
        _playing = true;
        _fromStart = true;
        _plays++;
        AnimStarts++;
    }

    /// <summary>
    /// A client's proxy plays what the server's replicated SimAnim says (UE2 animation replication, so the notify
    /// sounds play on every machine, rule 12): a rate above 0 after a held channel or another sequence starts it
    /// (backwards when the start frame is the end), a rate of 0 holds the replicated frame, and the loop flag makes
    /// the channel wrap at its end.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Client start rule of SimAnim (UE2 client animation replication); the backward and loop encodings are this port's")]
    private void FollowSimAnim()
    {
        FAnimRep rep = SimAnim;
        if (_following && rep == _followed)
        {
            return;
        }

        bool start = rep.AnimRate > 0 && (!_following || rep.AnimSequence != _followed.AnimSequence || _followed.AnimRate == 0);
        _followed = rep;
        _following = true;
        _loop = rep.AnimLoop != 0 && rep.AnimRate > 0;
        if (rep.AnimSequence.IsNone || XLevel.GetService<IMeshAnimations>()?.FindSequence(Mesh, rep.AnimSequence) is not { } sequence)
        {
            return;
        }

        if (start)
        {
            StartChannel(sequence, rep.AnimRate / 64f, rep.AnimFrame == BackwardStartFrame);
        }
        else if (rep.AnimRate == 0)
        {
            _sequence = sequence;
            _frame = rep.AnimFrame / 255f;
            _playing = false;
        }
    }

    /// <summary>Holds the channel at <paramref name="Time"/> (a normalised frame), not moving.</summary>
    [UcNative("AActor::execFreezeAnimAt", Exe = 0x10a23af0)]
    [PortOf("nat:Engine.Actor.FreezeAnimAt", Spec = "spec/devices/animated-mesh.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The replicated channel state (SimAnim) of a frozen channel has rate 0, so clients see the next PlayAnim as a start")]
    private void FreezeAnimAt(float Time)
    {
        _frame = Time;
        _playing = false;
        _plays++;
        SimAnim = SimAnim with { AnimRate = 0, AnimFrame = (byte)SvmMath.Clamp(SvmMath.Trunc(Time * 255f), 0, 255) };
    }

    /// <summary>The last frame of a one-shot play: (N - 1) / N.</summary>
    private static float EndFrame(MeshAnimSequence sequence) => (sequence.NumFrames - 1) / (float)sequence.NumFrames;

    /// <summary>
    /// One animation update: the frame advances; every notify whose time the frame crossed fires in list order;
    /// reaching the end holds the end frame and sends AnimEnd.
    /// </summary>
    [UcNative("AActor::UpdateAnimation")]
    [PortOf("nat:Engine.Actor.PlayAnim", Spec = "spec/devices/animated-mesh.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Notify interval rule (animated-mesh.md rule 10), update once per tick after physics")]
    private void UpdateAnimation(float DeltaSeconds)
    {
        if (!_playing || _sequence is not { } sequence)
        {
            return;
        }

        float old = _frame;
        float end = _backward ? 0f : EndFrame(sequence);
        float next = _backward ? old - DeltaSeconds * _rate : old + DeltaSeconds * _rate;
        bool ended = _backward ? next <= end : next >= end;
        if (ended)
        {
            next = end;
        }

        _frame = next;
        bool fromStart = _fromStart;
        _fromStart = false;
        int plays = _plays;
        for (int i = 0; i < sequence.Notifies.Count; i++)
        {
            MeshAnimNotify notify = sequence.Notifies[i];
            bool crossed = _backward
                ? notify.Time < old && notify.Time >= next || (fromStart && notify.Time == old)
                : notify.Time > old && notify.Time <= next || (fromStart && notify.Time == old);
            if (!crossed)
            {
                continue;
            }

            notify.Notify.Notify(this);
            if (bDeleteMe || plays != _plays)
            {
                return;
            }
        }

        if (ended)
        {
            _playing = false;

            // AnimEnd is not simulated: a client's proxy holds the end pose and leaves the state to the server, or
            // wraps while the server replays the sequence.
            if (Role >= ENetRole.ROLE_AutonomousProxy)
            {
                // The channel holds its end: clients see a held channel unless AnimEnd plays again in this tick.
                SimAnim = SimAnim with { AnimRate = 0, AnimFrame = (byte)SvmMath.Clamp(SvmMath.Trunc(_frame * 255f), 0, 255) };
                eventAnimEnd(0);
            }
            else if (_loop)
            {
                StartChannel(sequence, _followed.AnimRate / 64f, _backward);
            }
        }
    }

    /// <summary>FinishAnim waits for the channel to stop playing.</summary>
    protected override bool IsAnimFinished(int Channel) => !_playing;

    /// <summary>Updates the animation after the rest of the native tick.</summary>
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (bDeleteMe)
        {
            return;
        }

        if (Role < ENetRole.ROLE_Authority)
        {
            FollowSimAnim();
        }

        UpdateAnimation(DeltaSeconds);
    }

    /// <summary>
    /// Hangs <paramref name="Attachment"/> on <paramref name="BoneName"/>: based on this prop with that bone. Where
    /// the bone puts it is the animation system's (views pose it); the simulation keeps the base and bone name.
    /// </summary>
    [UcNative("AActor::execAttachToBone", Exe = 0x10a27330)]
    [PortOf("nat:Engine.Actor.AttachToBone", Spec = "spec/devices/animated-mesh.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Whether static, non-movable lights attach is animated-mesh.md open question 1 (the plan says they ride the boom)")]
    private bool AttachActorToBone(Actor? Attachment, Name BoneName)
    {
        if (Attachment is null || Attachment.bDeleteMe || ReferenceEquals(Attachment, this))
        {
            return false;
        }

        Attachment.AttachmentBone = BoneName;
        Attachment.SetBase(this);
        return true;
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        for (int i = 0; i < ActorsToAttach.Count; i++)
        {
            FAttachActor attach = ActorsToAttach[i];
            if (attach.ActorName is { bDeleteMe: true })
            {
                attach.ActorName = null;
                ActorsToAttach[i] = attach;
            }
        }
    }
}
