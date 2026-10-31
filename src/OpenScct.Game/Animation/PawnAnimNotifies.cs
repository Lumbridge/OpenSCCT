using System;
using System.Collections.Generic;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Animation;

/// <summary>
/// The script notifies (AnimNotify_Script) of a level: NotifyName to the owner's function. The footstep notifies of
/// the walk cycles run SBasePawn.PlayFootStepLeft and PlayFootStepRight by default; other areas register the ports
/// of their notifies (for example the quick draw NotifyDrawUndrawWeapon) once their own call sites no longer stand in
/// for them. Installed as a level service; without one only the defaults run.
/// </summary>
public sealed class AnimNotifyHandlers
{
    private readonly Dictionary<Name, Action<Actor>> _handlers = new();

    /// <summary>Runs <paramref name="handler"/> for the script notify <paramref name="notifyName"/> (replaces a default).</summary>
    public AnimNotifyHandlers Register(Name notifyName, Action<Actor> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[notifyName] = handler;
        return this;
    }

    /// <summary>True when a handler ran for <paramref name="notifyName"/>.</summary>
    public bool TryRun(Actor owner, Name notifyName)
    {
        if (!_handlers.TryGetValue(notifyName, out Action<Actor>? handler))
        {
            return false;
        }

        handler(owner);
        return true;
    }

    /// <summary>The level's handlers, made and installed on first use.</summary>
    public static AnimNotifyHandlers Of(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (level.GetService<AnimNotifyHandlers>() is not { } handlers)
        {
            handlers = new AnimNotifyHandlers();
            level.SetService(handlers);
        }

        return handlers;
    }
}

/// <summary>
/// The notifies of the pawn clips: read from the converted <c>svm.anim/1</c> sequences, and fired by the channel
/// update (spec/native/ANSWERS-anim.md A2.6): sounds and stopped sounds at the owner, script notifies to the owner's
/// function by name.
/// </summary>
public static class PawnAnimNotifies
{
    private static readonly Name PlayFootStepLeft = new("PlayFootStepLeft");
    private static readonly Name PlayFootStepRight = new("PlayFootStepRight");

    /// <summary>Fires one notify of <paramref name="owner"/>'s channel <paramref name="channel"/>.</summary>
    [UcNative("UAnimNotify::Notify")]
    [PortOf("eng:USkeletalMeshInstance::UpdateAnimation", Spec = "spec/native/ANSWERS-anim.md#a2-updateanimation-0x111056c0-complete")]
    [Provenance(ProvenanceKind.Estimated, "AnimNotify_Script calls the owner's function NotifyName; AnimNotify_StopSound stops the sound on the owner (the native bodies are not decoded)")]
    public static void Fire(Actor owner, int channel, UAnimNotify notify)
    {
        if (owner.XLevel.World.IsReplaying) return;
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(notify);
        switch (notify)
        {
            case UAnimNotify_Script script:
                RunScript(owner, script.NotifyName);
                break;
            case UAnimNotify_StopSound stop:
                if (!stop.Sound.IsNone)
                {
                    owner.StopSound(stop.Sound);
                }

                break;
            default:
                notify.Notify(owner);
                break;
        }
    }

    /// <summary>A script notify: the level's registered handler, else the ported default.</summary>
    public static void RunScript(Actor owner, Name notifyName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (owner.XLevel?.GetService<AnimNotifyHandlers>() is { } handlers && handlers.TryRun(owner, notifyName))
        {
            return;
        }

        if (owner is not SBasePawn pawn)
        {
            return;
        }

        if (notifyName == PlayFootStepLeft)
        {
            pawn.PlayFootStepLeft();
        }
        else if (notifyName == PlayFootStepRight)
        {
            pawn.PlayFootStepRight();
        }
        else
        {
            Equipment.PawnVision.RunGoggleNotify(pawn, notifyName);
        }
    }

    /// <summary>The notifies of one converted sequence (its <c>notifies</c> array), in package order.</summary>
    public static IReadOnlyList<PawnClipNotify> Read(JsonElement sequence)
    {
        if (!sequence.TryGetProperty("notifies", out JsonElement list) || list.ValueKind != JsonValueKind.Array ||
            list.GetArrayLength() == 0)
        {
            return [];
        }

        var result = new List<PawnClipNotify>(list.GetArrayLength());
        foreach (JsonElement n in list.EnumerateArray())
        {
            if (!n.TryGetProperty("time", out JsonElement t) || !n.TryGetProperty("class", out JsonElement c))
            {
                continue;
            }

            JsonElement props = n.TryGetProperty("props", out JsonElement p) ? p : default;
            UAnimNotify? notify = c.GetString() switch
            {
                "Engine.AnimNotify_Sound" => new UAnimNotify_Sound
                {
                    Sound = Asset(props, "Sound"),
                    Volume = Num(props, "Volume", 1f),
                    Radius = (int)Num(props, "Radius", 0f),
                    BoneName = new Name(Str(props, "BoneName")),
                },
                "Engine.AnimNotify_StopSound" => new UAnimNotify_StopSound { Sound = Asset(props, "Sound") },
                "Engine.AnimNotify_Script" => new UAnimNotify_Script { NotifyName = new Name(Str(props, "NotifyName")) },
                _ => null,
            };
            if (notify is not null)
            {
                result.Add(new PawnClipNotify(t.GetSingle(), notify));
            }
        }

        return result;
    }

    private static AssetRef Asset(JsonElement props, string key) =>
        props.ValueKind == JsonValueKind.Object && props.TryGetProperty(key, out JsonElement v) &&
        v.ValueKind == JsonValueKind.Object && v.TryGetProperty("$asset", out JsonElement a)
            ? new AssetRef(a.GetString())
            : default;

    private static float Num(JsonElement props, string key, float fallback) =>
        props.ValueKind == JsonValueKind.Object && props.TryGetProperty(key, out JsonElement v) &&
        v.ValueKind == JsonValueKind.Number
            ? v.GetSingle()
            : fallback;

    private static string? Str(JsonElement props, string key) =>
        props.ValueKind == JsonValueKind.Object && props.TryGetProperty(key, out JsonElement v) &&
        v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
