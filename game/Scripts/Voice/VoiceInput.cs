using Godot;
using System;
using OpenScctGame.Controls;
using OpenScct.Net.Voice;

namespace OpenScctGame.Voice;

/// <summary>
/// Voice key binds (Enhanced 4.0's Communications rows "Push To Talk" and "Mute Microphone", FEATURES enh:voice.binds),
/// plus a team push to talk. The actions are added to the InputMap with these defaults when the key layout does not
/// already define them; the layout owner may rebind them. The stock Hack Comm key (H, <c>bSpecialsChat</c>) is a
/// gameplay input and stays with the controller.
/// </summary>
public static class VoiceInput
{
    /// <summary>Uses the shared binding state so capture and focus suppression apply in every visit.</summary>
    public static bool ReadKeys(VoiceClient client, bool blocked = false)
    {
        BindingInput? bindings = BindingInput.Current;
        bool mute = bindings is null ? Input.IsActionJustPressed(ToggleMute) : (bindings.TakeMuteEdges() & 1) != 0;
        blocked |= bindings?.CaptureSuspended ?? false;
        client.TalkKey = !blocked && (bindings?.VoiceAllHeld ?? Input.IsActionPressed(PushToTalk));
        client.TeamKey = !blocked && (bindings?.VoiceTeamHeld ?? Input.IsActionPressed(TeamPushToTalk));
        if (mute && !blocked) client.Settings.MicMuted = !client.Settings.MicMuted;
        return mute && !blocked;
    }

    /// <summary>Hold to talk on the "all" channel (also forces transmission in voice activation mode).</summary>
    public const string PushToTalk = "voice_ptt";

    /// <summary>Hold to talk to your team only.</summary>
    public const string TeamPushToTalk = "voice_ptt_team";

    /// <summary>Toggle self mute (<c>ToggleVoiceMute</c>).</summary>
    public const string ToggleMute = "voice_mute_toggle";

    /// <summary>Adds the voice actions with their default keys unless they exist.</summary>
    public static void EnsureActions()
    {
        Ensure(PushToTalk, Key.Capslock);
        Ensure(TeamPushToTalk, Key.Y);
        Ensure(ToggleMute, Key.F8);
        if (!BindingInput.HasConfiguredLayout)
            BindLayout(InputBindings.Load(OpenScctGame.Play.Session.PlaySide.Spy, seedInputMap: false, draft: true));
    }

    /// <summary>Commit the active role's voice shortcuts, including intentionally unbound shortcuts.</summary>
    public static void BindLayout(InputBindings bindings) => BindLayout(bindings.Layout);

    public static void BindLayout(KeyLayout layout)
    {
        BindingInput.SetLayout(layout);
        foreach ((string command, string action) in new[]
        {
            ("PushToTalk", PushToTalk), ("TeamPushToTalk", TeamPushToTalk), ("ToggleVoiceMute", ToggleMute),
        })
        {
            if (InputMap.HasAction(action)) InputMap.EraseAction(action);
            InputMap.AddAction(action);
            foreach (string key in layout.Bindings.Keys)
            {
                foreach (KeyAction part in layout.Resolve(key))
                    if (part.Kind == KeyActionKind.Command && part.Name.Equals(command, StringComparison.OrdinalIgnoreCase) && GodotKeys.ToEvent(key) is { } ev)
                    {
                        InputMap.ActionAddEvent(action, ev);
                        break;
                    }
            }
        }
    }

    private static void Ensure(string action, Key key)
    {
        if (InputMap.HasAction(action))
        {
            return;
        }

        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
