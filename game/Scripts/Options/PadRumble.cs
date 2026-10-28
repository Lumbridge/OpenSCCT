using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScctGame.Options;

/// <summary>
/// One rumble of the original's <c>SPlayerController.Vibration_*</c> set: its priority for
/// <c>Vibration_CheckSiPossible</c>, the two motor strengths (0 to 65535: the low-frequency left motor, the
/// high-frequency right one) and how long it lasts.
/// </summary>
public readonly record struct RumbleShape(int Priority, int Low, int High, float Seconds);

/// <summary>
/// Controller vibration (rl:input.rumble): the simulation raises the original's <c>Vibration_*</c> calls as
/// <see cref="CombatFx"/> notifications for the local player, and this plays each on the controller in use with the
/// strengths and lengths of the script (Reloaded's NativeRumblePadStart, spec/native/reloaded-hooks.md rule 28), scaled
/// by <see cref="PadSettings.RumbleStrength"/>. <c>Vibration_CheckSiPossible</c> decides as in the script: a new rumble
/// starts when the last one has run out or when it has a higher priority. Godot-free apart from <see cref="Play"/>.
/// </summary>
public sealed class PadRumble
{
    [Provenance(ProvenanceKind.Decoded, "SPlayerController.Vibration_* NativeRumblePadStart arguments and Vibration_CheckSiPossible priorities")]
    [PortOf("uc:SBase.SPlayerController:Vibration_CoupParDefaut", Spec = "spec/combat/rumble.md#rules")]
    [PortOf("uc:SBase.SPlayerController:Vibration_SautHaut", Spec = "spec/combat/rumble.md#rules")]
    [PortOf("uc:SBase.SPlayerController:Vibration_Bousculade", Spec = "spec/combat/rumble.md#rules")]
    [PortOf("uc:SBase.SPlayerController:Vibration_Blast", Spec = "spec/combat/rumble.md#rules")]
    [PortOf("uc:SBase.SPlayerController:Vibration_Grab_Kill", Spec = "spec/combat/rumble.md#rules")]
    [PortOf("uc:SBase.SPlayerController:Vibration_Charge_Course", Spec = "spec/combat/rumble.md#rules")]
    [PortOf("uc:SBase.SPlayerController:Vibration_Charge_Obstacle", Spec = "spec/combat/rumble.md#rules")]
    private static readonly Dictionary<Name, RumbleShape> Shapes = new()
    {
        [CombatFx.Vibration_CoupParDefaut] = new(1, 65535, 32000, 0.1f),
        [CombatFx.Vibration_Tir] = new(2, 0, 32000, 0.12f),
        [CombatFx.Vibration_Gadget] = new(2, 0, 19200, 0.12f),
        [CombatFx.Vibration_SautHaut] = new(3, 65535, 0, 0.2f),
        [CombatFx.Vibration_Bousculade] = new(5, 65535, 0, 0.35f),
        [CombatFx.Vibration_Blast] = new(7, 65535, 65535, 0.35f),
        [CombatFx.Vibration_Grab_Kill] = new(5, 65535, 0, 0.2f),
        [CombatFx.Vibration_Charge_Course] = new(1, 40000, 0, 0.1f),
        [CombatFx.Vibration_Charge_Obstacle] = new(3, 65535, 0, 0.2f),
    };

    /// <summary>Vibration_StickyShock: priority 5, (20000, 65535), for the freeze's length (the event's first parameter).</summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerController.Vibration_StickyShock: CheckSiPossible(5, fDuree), NativeRumblePadStart(20000, 65535, fDuree)")]
    public static RumbleShape StickyShock(float seconds) => new(5, 20000, 65535, seconds);

    /// <summary>Vibration_Grabbed: priority 1 for 0.2 s, motors 2000 + 0..5000 and 5000 + 0..10000 drawn each time.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerController.Vibration_Grabbed: NativeRumblePadStart(2000 + FRand x 5000, 5000 + FRand x 10000, 0.2)")]
    [PortOf("uc:SBase.SPlayerController:Vibration_Grabbed", Spec = "spec/combat/rumble.md#rules")]
    public static RumbleShape Grabbed(float r1, float r2) => new(1, (int)(2000f + r1 * 5000f), (int)(5000f + r2 * 10000f), 0.2f);

    private readonly Random _random = new();
    private int _priority = -1;
    private float _until;

    /// <summary>The rumble shape of a notification, or null when it is not a rumble.</summary>
    public RumbleShape? ShapeOf(in SimEvent e)
    {
        if (e.What == CombatFx.Vibration_StickyShock)
        {
            return StickyShock(e.Location.X);
        }

        if (e.What == CombatFx.Vibration_Grabbed)
        {
            return Grabbed((float)_random.NextDouble(), (float)_random.NextDouble());
        }

        return Shapes.TryGetValue(e.What, out RumbleShape s) ? s : null;
    }

    /// <summary>Vibration_CheckSiPossible: true (and the rumble taken) when the last has run out or this one ranks higher.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerController.Vibration_CheckSiPossible: TimeSeconds > fVibrationTempsLimite or iPriorite > iVibrationPriorite")]
    [PortOf("uc:SBase.SPlayerController:Vibration_CheckSiPossible", Spec = "spec/combat/rumble.md#rules")]
    public bool Take(RumbleShape shape, float now)
    {
        if (now > _until || shape.Priority > _priority)
        {
            _priority = shape.Priority;
            _until = now + shape.Seconds;
            return true;
        }

        return false;
    }

    /// <summary>Vibration_StopTout: forgets the rumble running.</summary>
    [PortOf("uc:SBase.SPlayerController:Vibration_StopTout", Spec = "spec/combat/rumble.md#rules")]
    public void Reset()
    {
        _priority = -1;
        _until = 0f;
    }

    /// <summary>A notification of the local player: starts its rumble on the controller in use when the settings allow it.</summary>
    public void OnNotify(SPlayerController pc, SimEvent e, float now, PadSettings pad)
    {
        ArgumentNullException.ThrowIfNull(pc);
        ArgumentNullException.ThrowIfNull(pad);
        // The end of the round turns script rumble off (GameEnded: NativeRumbleEnable(false)).
        if (pc.bGameEnded)
        {
            Reset();
            return;
        }

        if (!pad.Enabled || !pad.Rumble || ShapeOf(e) is not { } shape || !Take(shape, now))
        {
            return;
        }

        Play(PadMenus.CurrentDevice(), shape, pad.RumbleStrength);
    }

    /// <summary>
    /// NativeRumblePadStart on a Godot joypad: the left (low-frequency) motor is Godot's strong magnitude, the right
    /// (high-frequency) one its weak magnitude; Reloaded scales both by the strength setting.
    /// </summary>
    public static void Play(int device, RumbleShape shape, float strength)
    {
        if (device < 0 || shape.Seconds <= 0f)
        {
            return;
        }

        float s = Math.Clamp(strength, 0f, 1f);
        Godot.Input.StartJoyVibration(device, shape.High / 65535f * s, shape.Low / 65535f * s, shape.Seconds);
    }
}
