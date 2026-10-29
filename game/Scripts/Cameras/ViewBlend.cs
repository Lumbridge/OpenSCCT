using System;
using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Cameras;

/// <summary>
/// The drawn camera between two simulated views (Godot-free, so the logic tests run it): the location, rotation and
/// FOV of the last two client ticks blended by how far the clock is into the next tick.
/// </summary>
public static class ViewBlend
{
    /// <summary>
    /// A camera move longer than this in one tick is drawn as a cut, not blended: a change of view source (the eyes to
    /// the following camera 250 uu behind when the binoculars or a scope are put away, a respawn, a sticky cam). The
    /// fastest camera that moves on its own (a falling pawn, the smooth move to a sticky cam at 2000 uu/s) covers
    /// under 50 uu a tick.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Render interpolation threshold; the original draws each frame's own view, so a change of view source is instant")]
    public const float CutDistance = 128f;

    /// <summary>True when the view from <paramref name="previous"/> to <paramref name="current"/> is drawn as a cut.</summary>
    public static bool IsCut(ViewSample previous, ViewSample current) =>
        !previous.Valid || (current.Location - previous.Location).SizeSquared() > CutDistance * CutDistance;

    /// <summary>The camera <paramref name="alpha"/> of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static (FVector Location, FRotator Rotation, float Fov) Blend(ViewSample a, ViewSample b, float alpha)
    {
        float t = Math.Clamp(alpha, 0f, 1f);
        FVector location = a.Location + ((b.Location - a.Location) * t);
        return (location, Blend(a.Rotation, b.Rotation, t), a.Fov + ((b.Fov - a.Fov) * t));
    }

    /// <summary>The rotation <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>, each axis the short way round.</summary>
    public static FRotator Blend(FRotator a, FRotator b, float t) => new(
        a.Pitch + (int)(FRotator.NormalizeAxis(b.Pitch - a.Pitch) * t),
        a.Yaw + (int)(FRotator.NormalizeAxis(b.Yaw - a.Yaw) * t),
        a.Roll + (int)(FRotator.NormalizeAxis(b.Roll - a.Roll) * t));
}

/// <summary>
/// Mouse look with no frame of delay. The original runs its controller once per rendered frame, so a mouse move is
/// in the view of the frame it arrives in; the rewrite simulates at 60 Hz and draws between the last two ticks, which
/// put a mouse move on the screen one to two ticks late. While the view rotation is the plain mouse model of the
/// controller (the merc's <c>UpdateRotationAttaquant_Mouse</c>, the spy's <c>UpdateCamRotation</c> without a target
/// snap) and the camera is the pawn's own first-person view, or while the view is a sticky cam or a network camera
/// turned by <c>s_UseObject.UpdateRotation</c> (<see cref="CamTurn"/>), the drawn rotation is
/// <c>blend(previous, current) + (1 - alpha) x turn of the last tick + turn of the counts not yet simulated</c>: the
/// turns come from the same formulas the next tick will run, so when the tick takes the counts the drawn rotation
/// does not move. Any other rotation change (a kick, a state's own view code) is still drawn between ticks.
/// <para>Whether the plain model holds is checked after every tick that took mouse counts: the turn the tick made
/// must equal the predicted one exactly. Otherwise (the scope's chasing view, the limited views of the traversal
/// states, a cam at its limit) nothing is added and the view is drawn between ticks as before.</para>
/// </summary>
public sealed class LookAhead
{
    private FRotator _before;
    private FRotator _predicted;
    private bool _hadCounts;
    private FRotator _tickTurn;
    private FRotator _lagTurn;
    private FRotator _lastTurn;
    private Name _state;
    private bool _stateChanged;
    private int _misses;
    private bool _follow;
    private static readonly Name NAME_s_Following = new("s_Following");

    /// <summary>Ticks in a row whose turn may differ from the prediction before the look-ahead stops (a kick on top of the turn).</summary>
    public const int MissesAllowed = 1;

    /// <summary>True while the last tick that took mouse counts turned exactly as predicted.</summary>
    public bool Active { get; private set; }

    /// <summary>
    /// The turn of the mouse counts not yet simulated, from the last <see cref="Offset"/>: the controller's rotation
    /// plus this is the view drawn this frame (the local pawn's arm aims at it, SimPawnView.DrawnView).
    /// </summary>
    public FRotator Pending { get; private set; }

    /// <summary>The input turn of the last tick, drawn ahead of the blend for the rest of the tick.</summary>
    public FRotator TickTurn => _tickTurn;

    /// <summary>Before a client tick, after its input is filled: the counts the tick will take, and the rotation before it.</summary>
    public void BeforeTick(SPlayerController? pc, MouseLook mouse, float countsX, float countsY, (int Width, int Height) viewport)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        _hadCounts = pc is not null && (countsX != 0f || countsY != 0f);
        if (pc is null)
        {
            return;
        }

        _before = pc.Rotation;
        Name state = pc.GetStateName();
        _stateChanged = state != _state;
        _state = state;
        _predicted = Turn(pc, mouse, countsX, countsY, viewport, pc.Level?.m_dT ?? Sim.DeltaTime);
    }

    /// <summary>After the tick and its calc view: whether the tick turned as predicted.</summary>
    public void AfterTick(SPlayerController? pc, ViewSample view)
    {
        // The cam views draw the cam actor, which takes the controller's angles a tick later (its view is the rotation
        // before this tick while turning): that tick of turn is drawn ahead too.
        bool cam = pc is not null && !OwnView(pc, view) && CamView(pc, view);
        bool lagging = cam && view.Rotation != pc!.Rotation;
        _follow = pc is not null && FollowView(pc, view);
        if (pc is null || !(cam || _follow || OwnView(pc, view)))
        {
            Active = false;
            _tickTurn = default;
            _lagTurn = default;
            _lastTurn = default;
            _hadCounts = false;
            return;
        }

        if (_hadCounts)
        {
            FRotator r = pc.Rotation;
            int yaw = FRotator.NormalizeAxis(r.Yaw - _before.Yaw), pitch = FRotator.NormalizeAxis(r.Pitch - _before.Pitch);
            if (yaw == _predicted.Yaw && pitch == _predicted.Pitch)
            {
                Active = true;
                _misses = 0;
            }
            else if (_stateChanged || (yaw == 0 && pitch == 0) || ++_misses > MissesAllowed)
            {
                // A new state may turn the view its own way; a view that did not turn at all is not taking the mouse
                // (a roll keeps the view it began with); a second miss in a row means the model is not the plain one.
                Active = false;
            }
        }

        FRotator turn = Active && _hadCounts ? _predicted : default;
        _tickTurn = lagging ? _lastTurn : turn;
        _lagTurn = lagging ? turn : default;
        _lastTurn = turn;
        _hadCounts = false;
    }

    /// <summary>
    /// What is added to the blended rotation this frame: the rest of the last tick's input turn (for a cam view that
    /// trails the controller by a tick, the rest of the tick before's and all of the last tick's) and the turn of the
    /// counts not yet simulated. Zero while <see cref="Active"/> is false.
    /// </summary>
    public FRotator Offset(SPlayerController? pc, MouseLook mouse, float pendingX, float pendingY, (int Width, int Height) viewport, float alpha)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (!Active || pc is null)
        {
            Pending = default;
            return default;
        }

        float rest = 1f - Math.Clamp(alpha, 0f, 1f);
        // A cam turns only past its dead zone a tick; the counts of the tick so far are drawn as the turn they make
        // in that tick when the last tick turned (a steady turn), not held back until enough have come.
        FRotator pending = pendingX != 0f || pendingY != 0f
            ? Turn(pc, mouse, pendingX, pendingY, viewport, pc.Level?.m_dT ?? Sim.DeltaTime, _lastTurn.Yaw != 0, _lastTurn.Pitch != 0)
            : default;
        Pending = pending;
        return new FRotator((int)(_tickTurn.Pitch * rest) + _lagTurn.Pitch + pending.Pitch,
            (int)(_tickTurn.Yaw * rest) + _lagTurn.Yaw + pending.Yaw, 0);
    }

    /// <summary>
    /// The camera distance to orbit the drawn camera by while the look-ahead turns the spy's following view: that
    /// camera sits the solver's distance behind its target along the controller's rotation (spec/native/camera.md
    /// rule 3.11), so turning the rotation ahead moves it round the target (<c>PlayCamera.Apply</c>). 0 otherwise.
    /// </summary>
    public float OrbitDistance(SPlayerController? pc) =>
        Active && _follow && pc?.m_camera is { } cam ? cam.m_prevDistance : 0f;

    /// <summary>
    /// The camera is the following view of the pawn's behind view (SPlayerCam s_Following: the controller placed
    /// behind the pawn, turned by the controller rotation, which the spy's <c>UpdateCamRotation</c> turns by the mouse).
    /// </summary>
    public static bool FollowView(SPlayerController pc, ViewSample view)
    {
        ArgumentNullException.ThrowIfNull(pc);
        return view.Valid && pc.bBehindView && pc.Pawn is not null && pc.m_camera is { } cam && cam.GetStateName() == NAME_s_Following
            && view.Rotation.Yaw == pc.Rotation.Yaw && view.Rotation.Pitch == pc.Rotation.Pitch;
    }

    /// <summary>The camera is the pawn's own first-person view, turned by the controller rotation.</summary>
    public static bool OwnView(SPlayerController pc, ViewSample view)
    {
        ArgumentNullException.ThrowIfNull(pc);
        return view.Valid && !pc.bBehindView && pc.Pawn is { } pawn && ReferenceEquals(pc.ViewTarget ?? pawn, pawn)
            && view.Rotation.Yaw == pc.Rotation.Yaw && view.Rotation.Pitch == pc.Rotation.Pitch;
    }

    /// <summary>
    /// The camera is a sticky cam or a network camera the controller turns: its rotation is the controller's, or the
    /// controller's before this tick (<see cref="AfterTick"/>).
    /// </summary>
    private bool CamView(SPlayerController pc, ViewSample view)
    {
        ArgumentNullException.ThrowIfNull(pc);
        FRotator v = view.Rotation;
        return view.Valid && pc.SBPawn?.UsedObject is SOrientableObject && GadgetStates.InStickyCamViewState(pc)
            && v.Roll == 0 && ((v.Yaw == pc.Rotation.Yaw && v.Pitch == pc.Rotation.Pitch) || (v.Yaw == _before.Yaw && v.Pitch == _before.Pitch));
    }

    /// <summary>
    /// The turn of the cam views' <c>s_UseObject.UpdateRotation</c> (SPlayerController.UseObjectUpdateRotation) for
    /// these mouse axes in one tick: each axis turns only past the dead zone (or always, with
    /// <paramref name="turning"/> or <paramref name="lookingUp"/>: the look-ahead's part of a tick of a steady turn),
    /// then the object's limits clamp it.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/equipment/sticky-cam.md (using a cam) and spec/devices/cam-network.md rule 7, as SPlayerController.UseObjectUpdateRotation ports them")]
    public static FRotator CamTurn(SPlayerController pc, SOrientableObject o, float turn, float lookUp, float dt,
        bool turning = false, bool lookingUp = false)
    {
        ArgumentNullException.ThrowIfNull(pc);
        ArgumentNullException.ThrowIfNull(o);
        int pitch = o.iPitch;
        int yaw = o.iYaw;
        // The dead zone is on aTurn and aLookUp, the turn by fTurnValue and fLookUpValue (0.75 of them).
        float scale = K.CamTurnScale * MouseLook.SensibiliteFor(pc) * dt * pc.DesiredFOV / K.CamTurnFovReference;
        if (turning || SvmMath.Abs(turn) > K.CamInputDeadZone)
        {
            yaw += SvmMath.Trunc(scale * turn * MovementK.LookScale);
        }

        if (lookingUp || SvmMath.Abs(lookUp) > K.CamInputDeadZone)
        {
            pitch += SvmMath.Trunc(scale * lookUp * MovementK.LookScale);
        }

        pitch &= 65535;
        yaw &= 65535;
        if (o.PitchConstraint.Y != 0f && o.PitchConstraint.Z != 0f)
        {
            pitch = SPlayerController.ClampRotation(SvmMath.Trunc(o.PitchConstraint.X), pitch, SvmMath.Trunc(o.PitchConstraint.Y), SvmMath.Trunc(o.PitchConstraint.Z));
        }

        if (o.YawConstraint.Y != 0f && o.YawConstraint.Z != 0f)
        {
            yaw = SPlayerController.ClampRotation(SvmMath.Trunc(o.YawConstraint.X), yaw, SvmMath.Trunc(o.YawConstraint.Y), SvmMath.Trunc(o.YawConstraint.Z));
        }

        return new FRotator(FRotator.NormalizeAxis(pitch - o.iPitch), FRotator.NormalizeAxis(yaw - o.iYaw), 0);
    }

    /// <summary>
    /// The yaw and pitch the controller's mouse view model turns by for these counts in one tick of
    /// <paramref name="dt"/> seconds (pitch clamped as the model clamps it), from the controller's rotation now.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/movement/input-and-rotation.md, UpdateRotationAttaquant_Mouse and UpdateCamRotation: int(input x ratio x sensitivity x dt x 50) per axis, the pitch clamps and the armed spy's pitch repulsion")]
    public static FRotator Turn(SPlayerController pc, MouseLook mouse, float countsX, float countsY, (int Width, int Height) viewport, float dt,
        bool camTurning = false, bool camLookingUp = false)
    {
        ArgumentNullException.ThrowIfNull(pc);
        ArgumentNullException.ThrowIfNull(mouse);
        (_, _, float aTurn, float aLookUp) = mouse.Axes(pc, countsX, countsY, viewport.Width, viewport.Height);
        if (pc.SBPawn?.UsedObject is SOrientableObject cam && GadgetStates.InStickyCamViewState(pc))
        {
            return CamTurn(pc, cam, aTurn, aLookUp, dt, camTurning, camLookingUp);
        }

        // fTurnValue and fLookUpValue (ControlInput).
        float turn = aTurn * MovementK.LookScale;
        float lookUp = aLookUp * MovementK.LookScale;
        float r = pc.bBinocularVision ? MovementK.BinocularRotationRatio : 1f;
        float sens = MouseLook.SensibiliteFor(pc);
        float k = MovementK.RotationRateScale;
        if (pc.SBPawn is SPAWNDEFENSE)
        {
            // UpdateRotation starts from LocalMyDesiredRotation.
            FRotator v = pc.LocalMyDesiredRotation;
            int yaw = MovementMath.Int(turn * r * sens * dt * k);
            int pitch = FRotator.NormalizeAxis(v.Pitch + MovementMath.Int(lookUp * r * sens * dt * k));
            pitch = Math.Clamp(pitch, MovementK.PitchMin, MovementK.PitchMax);
            return new FRotator(pitch - FRotator.NormalizeAxis(v.Pitch), yaw, 0);
        }

        FRotator c0 = pc.Rotation.Normalize();
        if (pc.SBPawn is SPawnAttaque { WeaponLoaded: 2 })
        {
            float c = 1f;
            int abs = Math.Abs(c0.Pitch);
            if (abs > MovementK.ArmedPitchRepulsionStart)
            {
                c = 1f - (abs - MovementK.ArmedPitchRepulsionStart) / MovementK.ArmedPitchRepulsionSpan;
            }

            if ((c0.Pitch > 0 && lookUp < 0f) || (c0.Pitch < 0 && lookUp > 0f))
            {
                c = 1f;
            }

            int pitch = FRotator.NormalizeAxis(c0.Pitch + MovementMath.Int(r * c * lookUp * dt * sens * k));
            pitch = Math.Clamp(pitch, MovementK.PitchMin, MovementK.ArmedPitchMax);
            return new FRotator(pitch - c0.Pitch, MovementMath.Int(r * turn * dt * sens * k), 0);
        }

        float s = pc.bInvertHorizontalAxisCamSpy ? -1f : 1f;
        int p = FRotator.NormalizeAxis(c0.Pitch + MovementMath.Int(r * sens * k * dt * lookUp));
        p = Math.Clamp(p, MovementK.PitchMin, MovementK.PitchMax);
        return new FRotator(p - c0.Pitch, MovementMath.Int(r * sens * k * dt * s * turn), 0);
    }
}
