using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;

namespace OpenScct.Server;

/// <summary>
/// Read-only, server-estimated policy observations. Call ObserveTick on the host thread before input dispatch,
/// then Capture at 10 Hz. Audio is bounded to 128 cues per player between snapshots.
/// Each row belongs to its PlayerId: other rows' Self values are ground truth, not that player's observations.
/// This never installs services, advances a bot, consumes its noise board, or reads its private memory.
/// </summary>
public sealed class TrainingObservations
{
    private const int MaxPlayers = 16;
    private const int MaxObjectives = 128;
    private const int MaxNoises = 128;
    private const int MaxGadgets = 16;
    private static readonly AiDifficulty PerceptionProfile = AiDifficulty.For(AiSkill.Normal);
    private readonly ServerHost _host;
    private readonly AiWorld? _observer;
    private readonly string? _contextError;
    private readonly List<SPlayerController> _players = new(MaxPlayers);
    private readonly List<SObjective> _objectives = new();
    private readonly bool _objectivesTruncated;
    private readonly AiNoiseBoard _audio = new();
    private readonly Dictionary<int, List<TrainingHeardCue>> _heard = new();
    private readonly HashSet<int> _heardTruncated = new();
    private long _lastAudioTick = -1;
    private float _lastMakeNoiseTime = float.NegativeInfinity;

    public TrainingObservations(ServerHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        foreach (SObjective objective in host.World.Level.AllActors<SObjective>())
        {
            if (!objective.bShowStatusOnHud || !objective.bAlwaysRelevant) continue;
            if (_objectives.Count == MaxObjectives)
            {
                _objectivesTruncated = true;
                break;
            }

            _objectives.Add(objective);
        }

        if (host.Map.Physics is not { } physics)
        {
            _contextError = "collision_unavailable";
            return;
        }

        // Load a separate light field: BeginTick updates its switch/time cache, never the live AI's field.
        AiLightField light;
        try
        {
            MapCatalog maps = host.Settings.Maps ?? MapCatalog.CreateDefault();
            light = MapAiLight.Load(maps.FindFolder(host.Map.Name), physics.Collision, host.Map.Loaded);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or
                                      System.Text.Json.JsonException or FormatException or InvalidOperationException)
        {
            light = new AiLightField([]);
            _contextError = "lighting_load_failed";
        }

        // Unknown is a shared singleton, so even its time must not be changed by this private observer.
        if (!light.HasData) light = new AiLightField([]);
        _observer = new AiWorld(host.World.Level, physics.Collision,
            NavGraph.Empty(NavProfile.Spy), NavGraph.Empty(NavProfile.Merc), light);
    }

    /// <summary>
    /// Read each completed simulation tick's sounds before the next input dispatch. Cues accumulate between
    /// snapshots, using each listener's position/view when heard. The private board never consumes live AI noise.
    /// This only visits the controller list and audio events; sight/light/world-list refresh remains at capture rate.
    /// </summary>
    public void ObserveTick()
    {
        Level level = _host.World.Level;
        if (_observer is null || _lastAudioTick == level.TickCount) return;
        _lastAudioTick = level.TickCount;
        _audio.Gather(level);
        var makeNoises = new List<AiNoise>();
        float latest = _lastMakeNoiseTime;
        if (AiWorld.Of(level) is { } liveAi)
        {
            foreach (AiNoise noise in liveAi.Noise.Noises)
            {
                if (!noise.FromMakeNoise || noise.Time <= _lastMakeNoiseTime) continue;
                latest = MathF.Max(latest, noise.Time);
                makeNoises.Add(noise);
            }
        }

        _lastMakeNoiseTime = latest;
        int scanned = 0;
        for (Controller? controller = level.Info.ControllerList; controller is not null && ++scanned <= 128; controller = controller.nextController)
        {
            if (controller is not SPlayerController { bDeleteMe: false,
                    PlayerReplicationInfo: { bOnlySpectator: false, bDeleteMe: false } pri,
                    Pawn: SBasePawn { bDeleteMe: false, Health: > 0 } pawn } pc) continue;
            if (!_heard.TryGetValue(pri.PlayerID, out List<TrainingHeardCue>? cues))
            {
                if (_heard.Count >= MaxPlayers) continue;
                cues = new List<TrainingHeardCue>();
                _heard.Add(pri.PlayerID, cues);
            }

            int count = 0;
            bool truncated = false;
            CaptureNoises(_audio.Noises, false, pawn, pc, cues, ref count, ref truncated);
            CaptureNoises(makeNoises, true, pawn, pc, cues, ref count, ref truncated);
            if (truncated) _heardTruncated.Add(pri.PlayerID);
        }
    }

    public TrainingObservationSnapshot Capture()
    {
        Level level = _host.World.Level;
        ObserveTick(); // Idempotent if the recorder already collected this tick's sounds.
        _observer?.BeginTick(); // Private lists/board only; PlaySound events are read, not removed.
        AiWorld? liveAi = AiWorld.Of(level);
        _players.Clear();
        int scanned = 0;
        bool playersTruncated = false;
        for (Controller? controller = level.Info.ControllerList; controller is not null; controller = controller.nextController)
        {
            if (++scanned > 128)
            {
                playersTruncated = true;
                break;
            }

            if (controller is not SPlayerController { bDeleteMe: false, PlayerReplicationInfo: { bOnlySpectator: false, bDeleteMe: false } } pc)
                continue;
            if (_players.Count == MaxPlayers)
            {
                playersTruncated = true;
                break;
            }

            _players.Add(pc);
        }

        _players.Sort(static (a, b) => a.PlayerReplicationInfo!.PlayerID.CompareTo(b.PlayerReplicationInfo!.PlayerID));
        var players = new TrainingPlayerObservation[_players.Count];
        for (int i = 0; i < players.Length; i++) players[i] = CapturePlayer(_players[i]);
        var objectives = new List<TrainingPublicObjective>(_objectives.Count);
        foreach (SObjective objective in _objectives)
        {
            if (!objective.bDeleteMe)
                objectives.Add(new(objective.ActorId, objective.ObjectiveName, objective.ObjectiveTaken,
                    Finite(objective.MinTriggerTime), Finite(objective.iSimulatedObjectiveIsBeingTookUntil)));
        }

        _heard.Clear();
        _heardTruncated.Clear();
        return new(level.TimeSeconds, players, objectives.ToArray(),
            new("before_input_dispatch", "server_authoritative_self", "server_estimate_ai_normal",
                _observer?.Light.HasData == true, liveAi is not null, playersTruncated, _objectivesTruncated,
                "sampled_current_body_sight_no_memory_or_tracker_locks",
                "accumulated_simulated_play_sound_and_available_make_noise_since_previous_snapshot",
                "client_pixels_audio_ui_flash_effects_camera_offsets_and_sounds_cleared_before_world_step_unavailable"));
    }

    private TrainingPlayerObservation CapturePlayer(SPlayerController pc)
    {
        PlayerReplicationInfo pri = pc.PlayerReplicationInfo!;
        SBasePawn? pawn = pc.Pawn is SBasePawn { bDeleteMe: false } p ? p : null;
        string state = pc.GetStateName().ToString();
        AiVisionMode mode = AiSight.ModeOf(pc);
        TrainingWeapon? weapon = pawn?.MyWeapon is { bDeleteMe: false } w
            ? new(w.ClassName.ToString(), w.Ammo, w is SWeaponDefense merc ? merc.ClipAmmo : null, w.GetStateName().ToString())
            : null;
        TrainingGadget[]? gadgets = null;
        bool inventoryTruncated = false;
        if (pawn is not null)
        {
            var list = new List<TrainingGadget>();
            var seen = new HashSet<SGadget>(ReferenceEqualityComparer.Instance);
            for (SGadget? gadget = pawn.MyGadget; gadget is not null; gadget = gadget.MyGadget)
            {
                if (!seen.Add(gadget) || list.Count == MaxGadgets)
                {
                    inventoryTruncated = true;
                    break;
                }

                if (!gadget.bDeleteMe)
                    list.Add(new(list.Count, gadget.ClassName.ToString(), gadget.Charge,
                        gadget.Charge == OpenScct.Game.Equipment.K.UnlimitedCharge,
                        ReferenceEquals(pawn.SelectedGadget, gadget)));
            }

            gadgets = list.ToArray();
        }

        var self = new TrainingSelfState(pawn?.ActorId, pawn is null ? null : Vector(pawn.Location),
            pawn is null ? null : Vector(pawn.Velocity), Rotation(pc.Rotation), Rotation(pc.MyDesiredRotation),
            Finite(pc.FovAngle), pawn?.Health, pawn is null ? null : pawn.Health > 0,
            pawn is null ? null : new TrainingStance(pawn.bIsCrouched, pawn.bIsWalking, pawn.bLyingOnGround,
                pawn.bSleeping, pawn.bStickyShocking, pawn.Physics.ToString()),
            weapon, gadgets, inventoryTruncated, mode.ToString(), state);

        string? unavailable = pawn is null || pawn.Health <= 0 ? "no_live_own_pawn" :
            _observer is null ? _contextError ?? "observer_unavailable" : null;
        string? sightUnavailable = unavailable;
        if (sightUnavailable is null &&
            ((pc.ViewTarget is not null && !ReferenceEquals(pc.ViewTarget, pawn)) ||
             state.StartsWith("s_UseObject", StringComparison.Ordinal) || state.Contains("CamNetwork", StringComparison.Ordinal)))
            sightUnavailable = "remote_camera_view_unavailable";
        if (sightUnavailable is null && !_observer!.Light.HasData && mode is not AiVisionMode.Thermal and not AiVisionMode.Emf)
            sightUnavailable = _contextError ?? "lighting_unavailable";

        TrainingVisibleContact[]? visible = null;
        if (sightUnavailable is null)
        {
            var contacts = new List<TrainingVisibleContact>();
            FVector eye = AiSight.Eye(pawn!);
            foreach (SPlayerController other in _players)
            {
                if (ReferenceEquals(pc, other) || other.Pawn is not SBasePawn { bDeleteMe: false } target) continue;
                AiSighting sight = AiSight.Look(_observer!, eye, pc.Rotation, pc.FovAngle, mode, target, PerceptionProfile);
                if (!sight.Visible) continue;
                PlayerReplicationInfo otherPri = other.PlayerReplicationInfo!;
                // Current target position is copied only after sight succeeds. No target health or controller state.
                contacts.Add(new(otherPri.PlayerID, otherPri.Team?.TeamIndex, Vector(target.Location), Finite(sight.Strength)));
            }

            visible = contacts.ToArray();
        }

        TrainingHeardCue[]? heard = unavailable is not null ? null :
            _heard.TryGetValue(pri.PlayerID, out List<TrainingHeardCue>? cues) ? cues.ToArray() : [];
        bool noisesTruncated = _heardTruncated.Contains(pri.PlayerID);

        return new(pri.PlayerID, pc.ActorId, pri.Team?.TeamIndex, pc.Player is AiViewport ? "bot" : "human",
            self, new(visible, heard, sightUnavailable, unavailable, noisesTruncated));
    }

    private void CaptureNoises(IReadOnlyList<AiNoise> noises, bool makeNoiseOnly, SBasePawn pawn, SPlayerController pc,
        List<TrainingHeardCue> cues, ref int count, ref bool truncated)
    {
        FVector eye = AiSight.Eye(pawn);
        foreach (AiNoise noise in noises)
        {
            if (noise.FromMakeNoise != makeNoiseOnly || ReferenceEquals(noise.Instigator, pawn)) continue;
            if (++count > MaxNoises || cues.Count >= MaxNoises)
            {
                truncated = true;
                break;
            }

            float strength = AiNoiseBoard.Heard(_observer!, in noise, eye, PerceptionProfile);
            if (!(strength > 0f)) continue;
            FVector direction = noise.Location - eye;
            float bearing = SvmMath.Atan2(direction.Y, direction.X) * (180f / MathF.PI) - pc.Rotation.Yaw * (360f / 65536f);
            bearing = ((bearing + 180f) % 360f + 360f) % 360f - 180f;
            int sector = (int)MathF.Round(bearing / 45f);
            if (sector == 4) sector = -4;
            // No source ID, exact position, distance, target health or live pose is exposed by hearing.
            cues.Add(new(sector * 45, strength < 0.3f ? "quiet" : strength < 0.7f ? "medium" : "loud",
                makeNoiseOnly ? "make_noise" : "play_sound", Finite(noise.Time)));
        }
    }

    private static float? Finite(float value) => float.IsFinite(value) ? value : null;
    private static TrainingVector? Vector(FVector value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z)
        ? new(value.X, value.Y, value.Z) : null;
    private static TrainingRotation Rotation(FRotator value) => new(value.Pitch, value.Yaw, value.Roll);
}

public sealed record TrainingObservationSnapshot(float TimeSeconds, TrainingPlayerObservation[] Players,
    TrainingPublicObjective[] PublicObjectives, TrainingObservationCoverage Coverage);

public sealed record TrainingObservationCoverage(string Timing, string Self, string Perception, bool LightingAvailable,
    bool MakeNoiseBoardAvailable, bool PlayersTruncated, bool ObjectivesTruncated, string Sight, string Hearing, string Unsupported);

public sealed record TrainingPlayerObservation(int PlayerId, int ControllerActorId, int? Team, string Kind,
    TrainingSelfState Self, TrainingPerceptionObservation Perception);

public sealed record TrainingSelfState(int? PawnActorId, TrainingVector? Position, TrainingVector? Velocity,
    TrainingRotation View, TrainingRotation Aim, float? FovDegrees, int? Health, bool? Alive, TrainingStance? Stance,
    TrainingWeapon? Weapon, TrainingGadget[]? Gadgets, bool InventoryTruncated, string Vision, string ControllerState);

public sealed record TrainingStance(bool Crouched, bool Walking, bool Downed, bool Sleeping, bool Shocked, string Physics);
public sealed record TrainingWeapon(string Class, int Ammo, int? ClipAmmo, string State);
public sealed record TrainingGadget(int Slot, string Class, int Charge, bool Unlimited, bool Selected);
public sealed record TrainingPerceptionObservation(TrainingVisibleContact[]? Visible, TrainingHeardCue[]? Heard,
    string? SightUnavailable, string? HearingUnavailable, bool NoisesTruncated);
public sealed record TrainingVisibleContact(int PlayerId, int? Team, TrainingVector? Position, float? Strength);
public sealed record TrainingHeardCue(int RelativeBearingDegrees, string Strength, string Kind, float? TimeSeconds);
public sealed record TrainingPublicObjective(int ActorId, string Name, bool Taken, float? MinimumTriggerTime,
    float? SimulatedTakingUntil);
public readonly record struct TrainingVector(float X, float Y, float Z);
public readonly record struct TrainingRotation(int Pitch, int Yaw, int Roll);
