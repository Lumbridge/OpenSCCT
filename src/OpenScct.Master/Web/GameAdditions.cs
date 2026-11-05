using System.Collections.Generic;

namespace OpenScct.Master.Web;

/// <summary>Documented OpenSCCT features for the public website.</summary>
internal static class GameAdditions
{
    internal sealed record Addition(string Id, string Title, string Description, string[] Details,
        string Url, string LinkLabel);

    internal sealed record FeatureSection(string Id, string Title, string Description, Addition[] Items);

    internal static readonly IReadOnlyList<FeatureSection> Sections =
    [
        new("play", "Matches and practice", "Game modes, match rules, team sizes and bot settings.",
        [
            // docs/AI.md; src/OpenScct.Bots/ServerBots.cs; LobbySetup.BotSkill.
            new("bots", "Spy and mercenary bots", "Bots can fill empty places or form custom teams.",
                ["Choose a difficulty level.", "Use bots in solo or multiplayer sessions."],
                "/wiki/practice", "Practice and bot guide"),

            // docs/MAPS.md, Modes; CreateSessionModel.Modes.
            new("modes", "Game modes", "Play Story, Disk Hunt, Deathmatch and Merc Deathmatch on compatible maps.",
                ["Available modes and objectives depend on the map.", "Custom maps can also define Extraction, Sabotage and Neutralization modes."],
                "/wiki/modes-and-objectives", "Modes and objective guide"),

            // docs/MENUS.md, Create Session and Alternate Mode; LobbySetup.MaxMaxPlayers.
            new("match-settings", "Match settings", "Sessions support up to 24 players, including bots, with separate spectator places.",
                ["Set time limits, lives, respawn times and team balance.", "Use side-swapping rounds, match series and random map changes.", "Alternate rules let the host choose allowed abilities, gadgets and gadget slots."],
                "/wiki/hosting", "Hosting and match settings"),

            // docs/MENUS.md, Tutorials; committed docs/ONLINE.md, Practice servers.
            new("practice", "Practice and map visits", "Practice sessions use modified match rules. Map visits run offline with guidance markers.",
                ["Practice uses unlimited lives, quick respawns and commands for health and ammunition; its rounds do not count towards stats.", "Visit a Map opens a map alone with its guidance markers.", "Tutorial pages include training maps, gadget explanations, help with the on-screen display and saved completion progress."],
                "/wiki/practice", "Practice and tutorials"),
        ]),

        new("equipment", "Equipment and player options", "Loadouts, sidearms, cloak and view controls.",
        [
            // spec/additions/merc-sidearms.md; src/OpenScct.Core/Engine/Sidearms.cs.
            new("sidearms", "Mercenary sidearms", "Hosts can enable a dedicated pistol slot for mercenaries.",
                ["Sidearms are off by default and do not use a gadget slot.", "Five pistols have different ammunition, firing and reload characteristics.", "Pistols unlock as you level up; all are available in offline, LAN and practice sessions when the host enables them."],
                "/wiki/gadgets-and-loadouts", "Equipment and loadout guide"),

            // docs/HUD.md, Options and settings; docs/MENUS.md, Role configuration.
            new("cloak", "Spy cloak", "Spies have a cloak ability with its own control.",
                ["It does not use a gadget slot and shares energy with the shocker.", "Cloak can activate with any remaining energy when its movement and weapon restrictions allow it. Energy drains continuously while it is on and recharges after it finishes fading out.", "Hold the control to stay cloaked, or choose Toggle to switch cloak on and off with a press."],
                "/wiki/gadgets-and-loadouts", "Cloak and equipment guide"),

            // spec/additions/gear-change.md; GearK.SwapWindowSeconds.
            new("gear", "Change gear during a round", "Save a new weapon and gadget selection from the in-game menu.",
                ["In a normal match, unused gear can change within ten seconds of spawning; later changes apply on the next spawn.", "Practice sessions allow immediate changes while alive.", "The host's equipment restrictions still apply."],
                "/wiki/gadgets-and-loadouts", "Gear change rules"),

            // docs/MENUS.md, Walk, gadgets and scope input.
            new("spy-view", "First-person spy view", "Spies can switch between third-person and first-person views.",
                ["Choose Hold or Toggle and assign the control to a key, mouse button or controller.", "Binoculars, remote cameras and views controlled by the map take priority."],
                "/wiki/controls-and-hud", "View and control settings"),
        ]),

        new("hub", "Community hubs", "Hubs support chat, activities and queues for matches.",
        [
            // docs/HUB.md; HubServer.Social.cs; HubPlayerList.cs.
            new("hub-social", "Hub sessions", "Players can visit or host a hub as either a spy or a mercenary.",
                ["Talk to everyone, your team or nearby players.", "Nearby voice gets quieter with distance.", "The player list provides individual mute and volume controls."],
                "/wiki/social-hubs", "Hub guide"),

            // docs/HUB.md, Activities; src/OpenScct.Server/Hub/HubActivities*.cs.
            new("hub-activities", "Hub activities", "The hub includes a spy course, mercenary aim range and other activities.",
                ["The course records completion times and the aim range records scores.", "Kick the plaza ball into a goal.", "Use character emotes or change the shared jukebox track."],
                "/wiki/social-hubs", "Hub activities guide"),

            // docs/HUB.md, Match pads; HubLaptopPanel; HubPads; HubMatches.
            new("hub-matches", "Start and watch hub matches", "Enter a marked match area in the hub, then ready up to join its queue.",
                ["Use the nearby laptop to choose a map, mode, teams and match rules.", "Bots can fill the remaining places when configured.", "Watch a running match as a spectator and return to the hub afterwards."],
                "/wiki/social-hubs", "Hub match guide"),
        ]),

        new("replays", "Match recordings", "Server recordings, the replay library and playback controls.",
        [
            // Committed docs/DEMOS.md, Saving your own matches; OnlineServer.Demos.
            new("recording", "Match recording", "Each player can choose whether to save upcoming matches.",
                ["The server records the full match and sends a copy to players who requested it.", "Guests can request a recording without enabling recording for everyone.", "Stay connected until the game reports that the recording is saved."],
                "/wiki/demos", "Recording guide"),

            // Committed docs/DEMOS.md, Watching; DemosScreen and DemoLibrary.
            new("replay-library", "Local and online replay library", "Find recordings by map, player, server or filename.",
                ["Filter by mode and storage, and sort by date.", "Signed-in players can download recent online recordings when the server provides them.", "Keep a downloaded recording permanently or remove a local copy."],
                "/wiki/demos", "Replay library guide"),

            // Committed docs/DEMOS.md; DemoHud; DemoChaseCamera; DemoClock.
            new("replay-controls", "Replay cameras and controls", "Watch different players in first person, third person or with a free camera.",
                ["Pause, change speed, skip through the timeline or jump to recorded events.", "Orbit the third-person camera and hide the playback controls.", "Recordings require compatible game and map versions."],
                "/wiki/demos", "Playback controls"),
        ]),

        new("player", "Player records and communication", "Career statistics, appearance, account settings, voice and text chat.",
        [
            // docs/MENUS.md, Player stats and match history; committed docs/PLAYER-HUB.md.
            new("stats", "Stats and match history", "View career totals, recorded rounds and individual match results.",
                ["Career categories cover spy actions, mercenary combat, gadgets, weapons and map wins.", "Review overall and role-specific results, win rates and kills/deaths.", "Match details include role-specific scoreboards and, while retained, an event timeline."],
                "/wiki/progression-and-stats", "Stats and progression guide"),

            // ProgressionK; Achievements; TitleCatalog; committed docs/ONLINE.md.
            new("progression", "Levels and achievements", "Qualifying online rounds earn experience points (XP) and progress towards achievements.",
                ["At level 55, you can choose to restart your level through prestige, up to ten times.", "Achievements cover matches, roles, weapons, gadgets and maps.", "Unlock appearance items and player titles as you progress."],
                "/leaderboards", "Player leaderboards"),

            // CosmeticCatalog; ArmouryScreen; committed docs/PLAYER-HUB.md, Appearance synchronization.
            new("appearance", "Player appearance", "Choose unlocked suits, weapon finishes, light colours and titles.",
                ["The Armoury shows a character preview and available items.", "You can hide other players' cosmetics or hide your own from others.", "Appearance choices can be updated in the game or through your website account."],
                "/account/appearance", "Appearance settings"),

            // Committed docs/PLAYER-HUB.md; committed docs/ONLINE.md, Settings sync; ProfileSync.
            new("accounts", "Accounts, privacy and saved settings", "Discord sign-in links your player name, progress and settings.",
                ["Offline play and open servers remain available without signing in.", "Choose whether your website profile is public.", "Key bindings and supported settings sync between computers; display and microphone settings stay local."],
                "/account", "Account settings"),

            // docs/VOICE.md; VoiceSettings; VoiceDsp; OptionsMenu.Voice.
            new("voice", "Voice chat", "Use push-to-talk or automatic voice activation with adjustable microphone settings.",
                ["Noise reduction, automatic microphone volume and a microphone test are available.", "Set volume or mute for individual players.", "Hosts choose team and lobby voice rules; echo cancellation is experimental."],
                "/wiki/voice", "Voice settings and troubleshooting"),

            // docs/HUD.md, Text chat; ModernChat; HubServer.Social.
            new("text-chat", "Text chat", "Send messages to everyone or your team, with a Nearby channel in hubs.",
                ["Scroll through recent messages while new messages arrive.", "Change channels without losing the message you are writing.", "Chat supports text selection, copy and paste."],
                "/wiki/voice", "Chat and communication guide"),

            // docs/DISCORD.md; DiscordPresence._Ready and HandleJoin.
            new("discord", "Discord activity and joining", "Show your current map, mode or hub on your Discord profile.",
                ["Friends can use Join or Ask to Join for eligible games; password-protected servers still ask for their password.", "Requires the Discord desktop app on Windows.", "Turn the activity display on or off in the game's options."],
                "/wiki/hosting", "Joining friends"),
        ]),

        new("setup", "Installation, hosting and settings", "Launchers, content downloads, hosting and client settings.",
        [
            // README.md; committed docs/LAUNCHER.md; launcher Install and Update workflows.
            new("launcher", "Windows and Linux launchers", "Native launchers install the game and keep it up to date on Windows and Linux.",
                ["Download the game, required content and additional maps from the launcher.", "Updates download changed files where available, and the launcher updates itself.", "Play an installed version offline or open the latest release notes."],
                "/download", "Download OpenSCCT"),

            // docs/CONTENT-PACKS.md; ContentScreen; MapPickerView; committed docs/ONLINE.md, Missing maps.
            new("maps", "Map browsing and downloads", "Browse maps with pictures, search and playlist filters.",
                ["Filter Stock, Enhanced, Community and 3v3 maps; search by name or short code.", "Choose a picture grid, a compact list or a thumbnail list, with one saved preference across map browsers.", "Download additional maps through the launcher or Content page; lobbies and hub queues can offer a missing map when a matching download is available."],
                "/wiki/maps", "Map browsing and downloads"),

            // Committed docs/ONLINE.md, Joining, Hosting, Rejoining; WebsiteJoinService.
            new("hosting", "Hosting and reconnecting", "Run a session from the game or use a standalone server.",
                ["Join through the server list, a direct address or a local network; the game can connect through a relay server when a direct connection is unavailable.", "Website Join links open the installed game.", "If a disconnected round is still running, the main menu can offer to rejoin it."],
                "/servers", "Server list"),

            // docs/MENUS.md, Role configuration and Walk; GamepadInput; PadRumble.
            new("input", "Keyboard, mouse and controller", "Configure separate controls for spies and mercenaries or share a layout.",
                ["Search actions or assigned inputs and browse movement, combat, gadgets, vision and interface categories.", "Edit primary and alternate inputs independently, including controller bindings; choose Hold or Toggle where supported.", "Adjust aiming sensitivity, controller dead zones and vibration. Apply or Save & Close keeps your edits."],
                "/wiki/controls-and-hud", "Control settings"),

            // docs/HUD.md, HUD editor and Optional teammate names; HudEditorPanel; CrosshairSettings.
            new("hud", "On-screen display and crosshair", "Move, resize or hide parts of the on-screen display separately for each side.",
                ["Search for an element, drag it in the preview or enter exact offsets; adjust its scale, opacity and visibility.", "Use presets, undo/redo and copy/paste layouts, including chat, event feed and performance widgets.", "Mercenaries have a separate crosshair editor with a preview; optional teammate labels can show names and health."],
                "/wiki/controls-and-hud", "HUD and crosshair guide"),

            // docs/HUD.md, Options and settings; TelemetrySettings; FrameRateSlider; SettingsApplier.
            new("display-audio", "Graphics, performance and sound", "Adjust display, rendering, frame-rate and volume settings.",
                ["Browse settings by category or search for a specific option, with explanations for the selected control.", "Choose display quality and shared or role-specific frame-rate limits; music, effects and voice have separate volume controls.", "Optional performance and connection graphs can appear only when issues occur."],
                "/wiki/controls-and-hud", "Display, performance and sound"),

            // OpenSCCT-Editor README.md, docs/WORKFLOW.md and docs/PARITY.md; editor remains in early development.
            new("map-editor", "Map creation and editing", "The OpenSCCT Editor supports map blockouts, gameplay setup and testing in OpenSCCT.",
                ["Work with brushes, actors, event links, objectives and lighting in a saved map project.", "Check and build a map before testing both roles in the game.", "The editor is in early development; the wiki explains the available workflow and its limitations."],
                "/wiki/editor-getting-started", "Editor setup guide"),

            // docs/LINUX.md: native x86-64 launcher and Flatpak; Steam Deck hardware validation pending.
            new("linux", "Experimental Linux support", "Install with the native Linux launcher or choose the alternative Flatpak game package.",
                ["The native launcher installs and updates the game without Wine, Proton or a separate .NET installation.", "Linux gameplay remains experimental; Steam Deck hardware testing is still pending.", "The Flatpak package is updated by installing a newer bundle."],
                "/download", "Linux download options"),
        ]),
    ];
}
