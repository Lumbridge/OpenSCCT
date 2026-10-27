using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.AI;

/// <summary>The vision mode a bot looks through (PlayerController.RendMap values).</summary>
public enum AiVisionMode : byte
{
    Normal = 5,
    Night = 34,
    Thermal = 36,
    Motion = 37,
    Emf = 38,
}

/// <summary>What one look at a target found.</summary>
public readonly record struct AiSighting(bool Visible, float Strength, float Distance, float Light, bool InFocus);

/// <summary>
/// Sight as a player has it: a target is seen only through the observer's own view (the controller's rotation and
/// FovAngle), along an open line from the eye (the level geometry and movers, smoke clouds for the modes that draw
/// them), within range, and lit enough for the vision mode: in normal view by the pawn lighting rule on the side that
/// faces the observer plus every torch on it; night vision boosts the light; thermal shows heat through smoke and in
/// the dark, fading with depth; motion vision shows nothing by sight (only the tracker's locks, <see cref="AiMotionSense"/>);
/// EMF shows only electronics (a spy with its weapon out, its
/// goggles on, binoculars, a spy bullet, a cloak or a phosphorus mark). Nothing hidden is read (no position of a pawn
/// out of view, no state of another player's controller).
/// </summary>
public static class AiSight
{
    private static readonly float[] HeightFractions = [0.8f, 0.1f, -0.7f];

    /// <summary>The eye of <paramref name="pawn"/> (its location raised by the stance's eye height).</summary>
    public static FVector Eye(Pawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        return pawn.Location + new FVector(0f, 0f, pawn.bIsCrouched ? AiK.EyeHeightCrouched : AiK.EyeHeightStanding);
    }

    /// <summary>The RendMap of <paramref name="pc"/>, with 0 (goggles never switched) read as normal view (5).</summary>
    public static int RendMapOf(PlayerController pc)
    {
        ArgumentNullException.ThrowIfNull(pc);
        return pc.RendMap == 0 ? SGoggle.REN_DynLight : pc.RendMap;
    }

    /// <summary>The vision mode of <paramref name="pc"/>.</summary>
    public static AiVisionMode ModeOf(PlayerController pc)
    {
        ArgumentNullException.ThrowIfNull(pc);
        return pc.RendMap switch
        {
            SGoggle.REN_NightVision => AiVisionMode.Night,
            SGoggle.REN_ThermalVision => AiVisionMode.Thermal,
            SGoggle.REN_MotionTrackingVision => AiVisionMode.Motion,
            SGoggle.REN_EMFVision => AiVisionMode.Emf,
            _ => AiVisionMode.Normal,
        };
    }

    /// <summary>
    /// One look from <paramref name="eye"/> along <paramref name="view"/> (horizontal FOV <paramref name="fovDegrees"/>)
    /// at <paramref name="target"/>.
    /// </summary>
    public static AiSighting Look(AiWorld world, FVector eye, FRotator view, float fovDegrees, AiVisionMode mode, Pawn target,
        AiDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(difficulty);
        FVector to = target.Location - eye;
        float distance = to.Size();
        if (distance < 1f)
        {
            return new AiSighting(true, 1f, distance, 1f, true);
        }

        // The view: a target outside the horizontal FOV, or the 4:3 vertical one, is not on screen.
        FVector forward = view.Vector();
        float cos = (to | forward) / distance;
        float angle = SvmMath.Acos(SvmMath.Clamp(cos, -1f, 1f)) * (180f / MathF.PI);
        float halfFov = fovDegrees * 0.5f;
        if (angle > halfFov + 4f)
        {
            return default;
        }

        bool inFocus = angle <= difficulty.FocusHalfAngle;
        float range = difficulty.SightRange * (inFocus ? 1f : difficulty.PeripheralFactor);
        if (mode == AiVisionMode.Thermal)
        {
            range = SvmMath.Min(range, AiK.ThermalRange);
        }

        if (distance > range)
        {
            return default;
        }

        // The line: three points up the target's body; the share that is open is how much of it shows.
        bool smokeBlocks = mode is AiVisionMode.Normal or AiVisionMode.Night or AiVisionMode.Motion;
        int open = 0;
        foreach (float f in HeightFractions)
        {
            FVector p = target.Location + new FVector(0f, 0f, target.CollisionHeight * f);
            if (world.LineOpen(eye, p) && !(smokeBlocks && world.SmokeBetween(eye, p)))
            {
                open++;
            }
        }

        if (open == 0)
        {
            return default;
        }

        float exposure = open / (float)HeightFractions.Length;
        float light = 0f;
        float strength;
        switch (mode)
        {
            case AiVisionMode.Thermal:
                // Pawns are hot (HeatIntensity 1): seen in the dark, through smoke at 0.8 (HeatOpacity 0.2).
                strength = 1f - distance / AiK.ThermalRange;
                if (world.SmokeBetween(eye, target.Location))
                {
                    strength *= 0.8f;
                }

                break;
            case AiVisionMode.Emf:
                if (target is SBasePawn sp && target is SPawnAttaque && !PawnVision.IsSpyEmfVisible(sp))
                {
                    return default;
                }

                strength = 1f - distance / (range + 1f);
                break;
            case AiVisionMode.Motion:
                // Motion vision shows a target only through the tracker's locks (AiMotionSense), never by sight.
                return default;
            case AiVisionMode.Night:
                light = (world.LightOn(target, eye) + AiK.NightBoost) * AiK.NightGain;
                strength = LitStrength(light, distance, difficulty);
                break;
            default:
                light = world.LightOn(target, eye);
                strength = LitStrength(light, distance, difficulty);
                if (BotSpeed(target) > AiK.RunSpeed * 0.7f)
                {
                    strength *= 1.5f;
                }

                break;
        }

        // The spy's cloak (GlassEffect 0 to 255) hides it in the light-based modes.
        if (mode is AiVisionMode.Normal or AiVisionMode.Night && target.GlassEffect > 0)
        {
            strength *= 1f - 0.85f * (target.GlassEffect / 255f);
        }

        strength *= exposure * (inFocus ? 1f : difficulty.PeripheralFactor);
        strength = SvmMath.Clamp(strength, 0f, 1f);
        return new AiSighting(strength > 0.02f, strength, distance, light, inFocus);
    }

    /// <summary>
    /// How well a target with light level <paramref name="light"/> shows at <paramref name="distance"/>: the level it
    /// needs grows from half the threshold at the observer to three times it at 2500 uu.
    /// </summary>
    public static float LitStrength(float light, float distance, AiDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(difficulty);
        float needed = difficulty.LightThreshold * (0.5f + distance / 1000f);
        if (distance < 100f)
        {
            // Close enough to touch: a dark shape still shows.
            return SvmMath.Max(0.5f, light / needed - 0.5f);
        }

        return SvmMath.Clamp(light / needed - 0.5f, 0f, 1f);
    }

    private static float BotSpeed(Pawn p) => SvmMath.Sqrt(p.Velocity.X * p.Velocity.X + p.Velocity.Y * p.Velocity.Y);
}

/// <summary>
/// A merc bot in motion vision sees what its own goggle tracker shows a player (spec/equipment/motion-tracking.md):
/// the tracker (SGoggle.MotionTrackingVision, run by the game's own scan on the bot's controller) locks pawns that
/// move at 150 uu/s or more within range and in sight, and a lock's bracket is drawn while it lasts, following the
/// locked pawn, when the pawn is in front of the view. A lock out of view is only the detect sound: something moved
/// within range, no direction (<see cref="LastBeep"/>). Nothing else is read: a still, slow, cloaked or hidden spy
/// does not show.
/// </summary>
public sealed class AiMotionSense
{
    private float _newest = -1f;

    /// <summary>Last time a new lock beeped while its pawn was out of view.</summary>
    public float LastBeep { get; private set; } = -100f;

    /// <summary>Locks seen so far (diagnostics).</summary>
    public int Locks { get; private set; }

    /// <summary>The tracker of <paramref name="pc"/>'s goggles while it is active, else null.</summary>
    public static SMotionTrackingVision? Tracker(SPlayerController pc)
    {
        ArgumentNullException.ThrowIfNull(pc);
        return pc.Goggle?.MotionTrackingVision is { } t && t.IsActive() ? t : null;
    }

    /// <summary>
    /// True while the tracker draws a bracket on <paramref name="target"/>: a lock on it that is still held or
    /// fading (EndTime + OutTime not passed), with the pawn within the view's horizontal FOV (plus its box).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "RenderMotionTracking 0x10a31fb0: every lock with an actor is drawn while in front (ScreenPosition1.Z < 1) until GetActors removes it")]
    public static bool Shows(SMotionTrackingVision tracker, Pawn target, FVector eye, FRotator view, float fovDegrees)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(target);
        int i = tracker.FindLock(target);
        if (i < 0)
        {
            return false;
        }

        FVector to = target.Location - eye;
        float d = to.Size();
        if (d < 1f)
        {
            return true;
        }

        float cos = (to | view.Vector()) / d;
        float angle = SvmMath.Acos(SvmMath.Clamp(cos, -1f, 1f)) * (180f / MathF.PI);
        float box = SvmMath.Atan2(SvmMath.Max(target.CollisionRadius, SMotionTrackingVisionK.BoxMinExtent), d) * (180f / MathF.PI);
        return angle <= fovDegrees * 0.5f + box;
    }

    /// <summary>One look through the tracker: what <see cref="Shows"/> says, as a sighting of full strength.</summary>
    public AiSighting Look(SPlayerController pc, FVector eye, FRotator view, float fovDegrees, Pawn target)
    {
        ArgumentNullException.ThrowIfNull(pc);
        ArgumentNullException.ThrowIfNull(target);
        if (Tracker(pc) is not { } tracker || !Shows(tracker, target, eye, view, fovDegrees))
        {
            return default;
        }

        return new AiSighting(true, 1f, (target.Location - eye).Size(), 0f, true);
    }

    /// <summary>Notes the locks that started since the last call: one out of view is a beep (<see cref="LastBeep"/>).</summary>
    public void Update(SPlayerController pc, FVector eye, FRotator view, float fovDegrees, float now)
    {
        ArgumentNullException.ThrowIfNull(pc);
        if (Tracker(pc) is not { } tracker)
        {
            _newest = -1f;
            return;
        }

        float newest = _newest;
        foreach (FLockedObject l in tracker.LockedObjects)
        {
            if (l.StartTime <= _newest || l.Actor is null)
            {
                continue;
            }

            Locks++;
            newest = SvmMath.Max(newest, l.StartTime);
            if (l.Actor is not Pawn p || !Shows(tracker, p, eye, view, fovDegrees))
            {
                LastBeep = now;
            }
        }

        _newest = newest;
    }
}

/// <summary>One noise a bot may hear this tick.</summary>
public readonly record struct AiNoise(FVector Location, float Radius, float Volume, Actor? Source, Pawn? Instigator, bool FromMakeNoise, float Time);

/// <summary>
/// The noises of the level, gathered once a tick for every bot: MakeNoise calls through the decoded
/// CheckNoiseHearing (spec/native/make-noise.md: two noise slots per instigator, 0.2 s and 50 uu repeats dropped unless
/// louder), and the sounds the simulation played (PlaySound events, as a player hears them: radius 80 for 0, full
/// volume within the radius and R / (R + 0.5 (d - R)) beyond, spec/native/audio.md rule 11). The crouched and walking
/// footsteps (SW_FootCrouch, SW_FootWalk) resolve to no sound in the original and are not heard.
/// </summary>
public sealed class AiNoiseBoard : INoiseHearing
{
    private static readonly Name NAME_SW_FootCrouch = new("SW_FootCrouch");
    private static readonly Name NAME_SW_FootWalk = new("SW_FootWalk");

    private readonly List<AiNoise> _noises = new();
    private readonly List<AiNoise> _pending = new();
    private readonly Dictionary<Pawn, NoiseSlots> _slots = new(ReferenceEqualityComparer.Instance);

    private sealed class NoiseSlots
    {
        public float Time1 = -100f;
        public FVector Spot1;
        public float Loudness1;
        public float Time2 = -100f;
        public FVector Spot2;
        public float Loudness2;
    }

    /// <summary>The noises of the last tick.</summary>
    public IReadOnlyList<AiNoise> Noises => _noises;

    /// <summary>MakeNoise noises delivered so far (diagnostics).</summary>
    public int MakeNoiseCount { get; private set; }

    /// <summary>
    /// CheckNoiseHearing: the per-instigator repeat filter and slot update, then the noise is kept for the bots of the
    /// next tick.
    /// </summary>
    [UcNative("AActor::CheckNoiseHearing", Exe = 0x109f1a50)]
    [PortOf("eng:AActor::CheckNoiseHearing", Spec = "spec/native/make-noise.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "CheckNoiseHearing 0x109f1a50: slots, 0.2 s / 50 uu / 0.9 repeat filter, 0.18 s reuse")]
    public void CheckNoiseHearing(Actor NoiseMaker, Pawn Instigator, float Loudness)
    {
        ArgumentNullException.ThrowIfNull(NoiseMaker);
        ArgumentNullException.ThrowIfNull(Instigator);
        if (Instigator.Controller is null)
        {
            return;
        }

        float now = NoiseMaker.XLevel.TimeSeconds;
        if (!_slots.TryGetValue(Instigator, out NoiseSlots? s))
        {
            s = new NoiseSlots();
            _slots[Instigator] = s;
        }

        FVector at = NoiseMaker.Location;
        if (now - AiK.NoiseRepeatSeconds < s.Time1 && FVector.DistSquared(s.Spot1, at) < AiK.NoiseRepeatDistanceSquared &&
            Loudness * AiK.NoiseRepeatRatio <= s.Loudness1)
        {
            return;
        }

        if (now - AiK.NoiseRepeatSeconds < s.Time2 && FVector.DistSquared(s.Spot2, at) < AiK.NoiseRepeatDistanceSquared &&
            Loudness * AiK.NoiseRepeatRatio <= s.Loudness2)
        {
            return;
        }

        if (now - AiK.NoiseSlotSeconds > s.Time1)
        {
            Store1(s, now, at, Loudness);
        }
        else if (now - AiK.NoiseSlotSeconds > s.Time2)
        {
            s.Time2 = now;
            s.Spot2 = at;
            s.Loudness2 = Loudness;
        }
        else if ((FVector.DistSquared(s.Spot1, at) < AiK.NoiseRepeatDistanceSquared && s.Loudness1 <= Loudness) || s.Loudness2 <= Loudness)
        {
            Store1(s, now, at, Loudness);
        }

        MakeNoiseCount++;
        _pending.Add(new AiNoise(at, AiK.HearingThreshold * SvmMath.Sqrt(SvmMath.Max(0f, Loudness)), 1f, NoiseMaker, Instigator, true, now));
    }

    private static void Store1(NoiseSlots s, float now, FVector at, float loudness)
    {
        s.Time1 = now;
        s.Spot1 = at;
        s.Loudness1 = loudness;
    }

    /// <summary>Collects the last tick's sounds and MakeNoise noises (call once a tick, before the bots think).</summary>
    public void Gather(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _noises.Clear();
        _noises.AddRange(_pending);
        _pending.Clear();
        float now = level.TimeSeconds;
        foreach (ref readonly SimEvent e in level.World.Events.Current)
        {
            if (e.Kind != SimEventKind.PlaySound || e.What.IsNone || e.What == NAME_SW_FootCrouch || e.What == NAME_SW_FootWalk)
            {
                continue;
            }

            Actor? source = e.ActorId != 0 ? level.FindActor(e.ActorId) : null;
            Pawn? instigator = source as Pawn ?? source?.Instigator;
            float radius = e.Radius > 0f ? e.Radius : AiK.SoundDefaultRadius;
            _noises.Add(new AiNoise(e.Location, radius, e.Volume, source, instigator, false, now));
        }

        // Forget the slots of pawns that are gone.
        if (_slots.Count > 32)
        {
            _gone.Clear();
            foreach (Pawn p in _slots.Keys)
            {
                if (p.bDeleteMe)
                {
                    _gone.Add(p);
                }
            }

            foreach (Pawn p in _gone)
            {
                _slots.Remove(p);
            }
        }
    }

    private readonly List<Pawn> _gone = new();

    /// <summary>
    /// The loudness <paramref name="listener"/> hears <paramref name="noise"/> with (0 when not heard): a MakeNoise
    /// noise within its radius (HearingThreshold x sqrt(Loudness), halved through walls) reads 1 at the source and 0 at
    /// the radius; a sound reads its gain at the listener (halved through walls).
    /// </summary>
    public static float Heard(AiWorld world, in AiNoise noise, FVector listener, AiDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(difficulty);
        float d = (noise.Location - listener).Size();
        bool open = world.LineOpen(listener, noise.Location + new FVector(0f, 0f, 10f));
        if (noise.FromMakeNoise)
        {
            float radius = noise.Radius * difficulty.HearingScale * (open ? 1f : 0.5f);
            return d >= radius ? 0f : 1f - d / radius;
        }

        float r = noise.Radius;
        float gain = d <= r ? 1f : r / (r + AiK.SoundRolloff * (d - r));
        float heard = noise.Volume * gain * difficulty.HearingScale * (open ? 1f : 0.5f);
        return heard >= difficulty.SoundThreshold ? heard : 0f;
    }
}

/// <summary>How a contact was last known.</summary>
public enum AiContactSource : byte
{
    None,
    Seen,
    Heard,
    Told,
    Alert,
}

/// <summary>What a bot remembers of one enemy.</summary>
public sealed class AiContact
{
    public AiContact(Pawn pawn)
    {
        Pawn = pawn;
    }

    public Pawn Pawn { get; }

    /// <summary>Where the enemy was last believed to be, and when.</summary>
    public FVector Position;

    public float Time = -100f;

    /// <summary>Its velocity when last seen.</summary>
    public FVector Velocity;

    /// <summary>0 to 1: how sure the bot is the enemy is at <see cref="Position"/>.</summary>
    public float Confidence;

    /// <summary>0 to 1: the spotting meter; at 1 the enemy counts as seen.</summary>
    public float Spot;

    /// <summary>Seen this tick.</summary>
    public bool Visible;

    /// <summary>When the enemy was first seen in the current run of sightings (for the reaction time).</summary>
    public float VisibleSince = -1f;

    public AiContactSource Source;

    /// <summary>True once the spotting meter filled during the current run of sightings.</summary>
    public bool Confirmed => Spot >= 1f;
}

/// <summary>
/// A bot's memory of its enemies: positions from sight, hearing, teammates and HUD alerts, with the confidence
/// halving every <see cref="AiDifficulty.MemoryHalfLife"/> seconds without news.
/// </summary>
public sealed class AiMemory
{
    private readonly List<AiContact> _contacts = new();

    public IReadOnlyList<AiContact> Contacts => _contacts;

    public AiContact For(Pawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        foreach (AiContact c in _contacts)
        {
            if (ReferenceEquals(c.Pawn, pawn))
            {
                return c;
            }
        }

        var contact = new AiContact(pawn);
        _contacts.Add(contact);
        return contact;
    }

    public AiContact? Find(Pawn pawn)
    {
        foreach (AiContact c in _contacts)
        {
            if (ReferenceEquals(c.Pawn, pawn))
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>Decays every contact by <paramref name="dt"/> seconds and drops the dead and the forgotten.</summary>
    public void Decay(float dt, AiDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(difficulty);
        float k = SvmMath.Exp(-0.6931472f * dt / SvmMath.Max(0.1f, difficulty.MemoryHalfLife));
        for (int i = _contacts.Count - 1; i >= 0; i--)
        {
            AiContact c = _contacts[i];
            if (c.Pawn.bDeleteMe || c.Pawn.Health <= 0)
            {
                _contacts.RemoveAt(i);
                continue;
            }

            if (!c.Visible)
            {
                c.Confidence *= k;
                c.Spot = SvmMath.Max(0f, c.Spot - dt / SvmMath.Max(0.1f, difficulty.SpotSeconds * 2f));
                c.VisibleSince = -1f;
            }

            if (c.Confidence < 0.02f && c.Spot <= 0f)
            {
                _contacts.RemoveAt(i);
            }
        }
    }

    /// <summary>A report from outside sight (a noise, a teammate, an alert), kept when it is fresher or surer.</summary>
    public void Report(Pawn pawn, FVector position, float now, float confidence, AiContactSource source)
    {
        AiContact c = For(pawn);
        if (c.Visible)
        {
            return;
        }

        if (confidence >= c.Confidence || now - c.Time > 1f)
        {
            c.Position = position;
            c.Time = now;
            c.Confidence = SvmMath.Max(c.Confidence, confidence);
            c.Source = source;
        }
    }

    /// <summary>The surest contact, or null.</summary>
    public AiContact? Best()
    {
        AiContact? best = null;
        foreach (AiContact c in _contacts)
        {
            if (best is null || Rank(c) > Rank(best))
            {
                best = c;
            }
        }

        return best;
    }

    private static float Rank(AiContact c) => c.Confidence + (c.Visible && c.Confirmed ? 1f : 0f);

    public void Clear() => _contacts.Clear();
}
