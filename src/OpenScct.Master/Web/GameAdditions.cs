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
        new("play", "Matches and practice", "Choose a game mode, set the rules and play with people or bots.",
        [
            // docs/AI.md; src/OpenScct.Bots/ServerBots.cs; LobbySetup.BotSkill.
            new("bots", "Spy and mercenary bots", "Bots can fill empty places or form custom teams.",
                ["Choose a difficulty level.", "Use bots in solo or multiplayer sessions."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/AI.md", "Bot details"),

            // docs/MAPS.md, Modes; CreateSessionModel.Modes.
            new("modes", "Game modes", "Play Story, Disk Hunt, Deathmatch and Merc Deathmatch on compatible maps.",
                ["Available modes and objectives depend on the map.", "Custom maps can also define Extraction, Sabotage and Neutralization modes."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/MAPS.md", "Maps and modes"),

            // docs/MENUS.md, Create Session and Alternate Mode; LobbySetup.MaxMaxPlayers.
            new("match-settings", "Match settings and larger teams", "Sessions support up to 24 players, including bots, with separate spectator places.",
                ["Set time limits, lives, respawn times and team balance.", "Use side-swapping rounds, match series and random map changes.", "Alternate rules let the host choose allowed abilities, gadgets and gadget slots."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/MENUS.md#create-session", "Match settings"),

            // docs/MENUS.md, Tutorials; committed docs/ONLINE.md, Practice servers.
            new("practice", "Practice and map visits", "Practice sessions and offline map visits provide ways to learn maps and controls.",
                ["Practice uses unlimited lives, quick respawns and commands for health and ammunition; its rounds do not count towards stats.", "Visit a Map opens a map alone with its guidance markers.", "Tutorial pages include training maps, gadget explanations, help with the on-screen display and saved completion progress."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/MENUS.md#tutorials", "Tutorials and map visits"),
        ]),

        new("equipment", "Equipment and player options", "Choose your loadout and configure how you control your character.",
        [
            // spec/additions/merc-sidearms.md; src/OpenScct.Core/Engine/Sidearms.cs.
            new("sidearms", "Mercenary sidearms", "Hosts can enable a dedicated pistol slot for mercenaries.",
                ["Sidearms are off by default and do not use a gadget slot.", "Five pistols have different ammunition, firing and reload characteristics.", "Pistols unlock as you level up; all are available in offline, LAN and practice sessions when the host enables them."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/spec/additions/merc-sidearms.md", "Sidearm details"),

            // docs/HUD.md, Options and settings; docs/MENUS.md, Role configuration.
            new("cloak", "Spy cloak", "Spies have a cloak ability with its own control.",
                ["It does not use a gadget slot.", "Hold the control to stay cloaked, or choose Toggle to switch cloak on and off with a press."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/HUD.md", "Cloak and control details"),

            // spec/additions/gear-change.md; GearK.SwapWindowSeconds.
            new("gear", "Change gear during a round", "Save a new weapon and gadget selection from the in-game menu.",
                ["In a normal match, unused gear can change within ten seconds of spawning; later changes apply on the next spawn.", "Practice sessions allow immediate changes while alive.", "The host's equipment restrictions still apply."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/spec/additions/gear-change.md", "Gear change rules"),

            // docs/MENUS.md, Walk, gadgets and scope input.
            new("spy-view", "First-person spy view", "Spies can switch between third-person and first-person views.",
                ["Choose Hold or Toggle and assign the control to a key, mouse button or controller.", "Binoculars, remote cameras and views controlled by the map take priority."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/MENUS.md#walk-gadgets-and-scope-input", "View controls"),
        ]),

        new("hub", "Community hubs", "Walk around a shared space, take part in activities and join matches.",
        [
            // docs/HUB.md; HubServer.Social.cs; HubPlayerList.cs.
            new("hub-social", "A shared space between matches", "Players can visit or host a hub as either a spy or a mercenary.",
                ["Talk to everyone, your team or nearby players.", "Nearby voice gets quieter with distance.", "The player list provides individual mute and volume controls."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/HUB.md", "Hub guide"),

            // docs/HUB.md, Activities; src/OpenScct.Server/Hub/HubActivities*.cs.
            new("hub-activities", "Hub activities", "The hub includes a spy course, mercenary aim range and other activities.",
                ["The course records completion times and the aim range records scores.", "Kick the plaza ball into a goal.", "Use character emotes or change the shared jukebox track."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/HUB.md#activities-h3", "Activity details"),

            // docs/HUB.md, Match pads; HubLaptopPanel; HubPads; HubMatches.
            new("hub-matches", "Start and watch hub matches", "Enter a marked match area in the hub, then ready up to join its queue.",
                ["Use the nearby laptop to choose a map, mode, teams and match rules.", "Bots can fill the remaining places when configured.", "Watch a running match as a spectator and return to the hub afterwards."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/HUB.md#match-pads-h2", "Hub match details"),
        ]),

        new("replays", "Match recordings", "Save complete rounds and watch them through the game's replay library.",
        [
            // Committed docs/DEMOS.md, Saving your own matches; OnlineServer.Demos.
            new("recording", "Record your matches", "Each player can choose whether to save upcoming matches.",
                ["The server records the full match and sends a copy to players who requested it.", "Guests can request a recording without enabling recording for everyone.", "Stay connected until the game reports that the recording is saved."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/DEMOS.md#saving-your-own-matches", "Recording guide"),

            // Committed docs/DEMOS.md, Watching; DemosScreen and DemoLibrary.
            new("replay-library", "Local and online replay library", "Find recordings by map, player, server or filename.",
                ["Filter by mode and storage, and sort by date.", "Signed-in players can download recent online recordings when the server provides them.", "Keep a downloaded recording permanently or remove a local copy."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/DEMOS.md#watching-game", "Replay library details"),

            // Committed docs/DEMOS.md; DemoHud; DemoChaseCamera; DemoClock.
            new("replay-controls", "Replay cameras and controls", "Watch different players in first person, third person or with a free camera.",
                ["Pause, change speed, skip through the timeline or jump to recorded events.", "Orbit the third-person camera and hide the playback controls.", "Recordings require compatible game and map versions."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/DEMOS.md#watching-game", "Playback controls"),
        ]),

        new("player", "Player records and communication", "Track your matches, choose your appearance and manage your account.",
        [
            // docs/MENUS.md, Player stats and match history; committed docs/PLAYER-HUB.md.
            new("stats", "Stats and match history", "View career totals, recorded rounds and individual match results.",
                ["Stats include wins, kills, deaths, weapons and maps.", "Match details include team scores and an event timeline.", "The website has public player profiles and lifetime leaderboards."],
                "/matches", "Match history"),

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
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/VOICE.md", "Voice settings"),

            // docs/HUD.md, Text chat; ModernChat; HubServer.Social.
            new("text-chat", "Text chat", "Send messages to everyone or your team, with a Nearby channel in hubs.",
                ["Scroll through recent messages while new messages arrive.", "Change channels without losing the message you are writing.", "Chat supports text selection, copy and paste."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/HUD.md#text-chat", "Chat details"),

            // docs/DISCORD.md; DiscordPresence._Ready and HandleJoin.
            new("discord", "Discord activity and joining", "Show your current map, mode or hub on your Discord profile.",
                ["Friends can use Join or Ask to Join for eligible games; password-protected servers still ask for their password.", "Requires the Discord desktop app on Windows.", "Turn the activity display on or off in the game's options."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/DISCORD.md", "Discord details"),
        ]),

        new("setup", "Installation, hosting and settings", "Install content, connect to a session and adjust the game for your computer.",
        [
            // README.md; committed docs/LAUNCHER.md; launcher Install and Update workflows.
            new("launcher", "Windows launcher", "The launcher installs the game and checks for updates.",
                ["Download required content or prepare it from your own game installation.", "Updates download changed files where available, and the launcher updates itself.", "Rebuild content or open the release notes from the launcher."],
                "/download", "Download OpenSCCT"),

            // docs/CONTENT-PACKS.md; ContentScreen; MapPickerView; committed docs/ONLINE.md, Missing maps.
            new("maps", "Map browsing and downloads", "Browse maps with pictures, search and playlist filters.",
                ["Download additional maps through the launcher or in-game Content page.", "A lobby can offer a missing map when a matching download is available.", "Hub queues can download the map needed for their match."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/CONTENT-PACKS.md", "Map download details"),

            // Committed docs/ONLINE.md, Joining, Hosting, Rejoining; WebsiteJoinService.
            new("hosting", "Hosting and reconnecting", "Run a session from the game or use a standalone server.",
                ["Join through the server list, a direct address or a local network; the game can connect through a relay server when a direct connection is unavailable.", "Website Join links open the installed game.", "If a disconnected round is still running, the main menu can offer to rejoin it."],
                "/servers", "Server list"),

            // docs/MENUS.md, Role configuration and Walk; GamepadInput; PadRumble.
            new("input", "Keyboard, mouse and controller", "Configure separate controls for spies and mercenaries or share a layout.",
                ["Adjust aiming sensitivity, controller dead zones and vibration.", "Choose Hold or Toggle for walking and mercenary scope aiming.", "Assign gadget selection and quick-use actions independently; mercenaries also have a walk control."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/MENUS.md#role-configuration-and-shared-controls", "Control settings"),

            // docs/HUD.md, HUD editor and Optional teammate names; HudEditorPanel; CrosshairSettings.
            new("hud", "On-screen display and crosshair", "Move, resize or hide parts of the on-screen display separately for each side.",
                ["The HUD editor includes previews, presets, undo and shared layouts.", "Mercenaries have a separate crosshair editor.", "Optional teammate labels can show names and health."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/HUD.md#hud-editor", "Display layout settings"),

            // docs/HUD.md, Options and settings; TelemetrySettings; FrameRateSlider; SettingsApplier.
            new("display-audio", "Graphics, performance and sound", "Adjust display, rendering, frame-rate and volume settings.",
                ["Choose windowed or fullscreen modes, monitor, resolution and graphics quality.", "Use a frame-rate limit, match the monitor refresh rate or leave it unlimited.", "Optional performance and connection graphs can appear only when issues occur; music, effects and voice have separate volume controls."],
                "https://github.com/Lumbridge/OpenSCCT/blob/main/docs/HUD.md#options-and-settings", "Display and sound settings"),

            // docs/LINUX.md: native x86_64 and Flatpak; Steam Deck hardware validation pending.
            new("linux", "Experimental Linux support", "A native Linux version is available as a standalone build or Flatpak package.",
                ["First-run setup downloads the content needed to play.", "Steam Deck hardware testing is still pending.", "The Flatpak package is updated by installing a newer bundle."],
                "/download", "Linux download options"),
        ]),
    ];
}
