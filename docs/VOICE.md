# Voice chat

In-game voice, as the Reloaded patch gives it (spec/reloaded/FEATURES.md section 5): push to talk or a voice
activation gate, team and all channels, the stock rules for who hears whom (team only, the dead, the downed,
spectators, grab talk, hacked comms, the four Enhanced voice modes), per-player mute and volume, and a speaking
indicator. Routing rules: `spec/game/voice-chat.md`. What Reloaded's code does: `spec/native/reloaded-features-from-code.md`
rule 1.

| Path | What it holds |
|---|---|
| `src/OpenScct.Net/Voice/` | codec, wire format, transport, jitter buffer, gate, routing, server relay, client (Godot-free) |
| `game/Scripts/Voice/` | microphone capture and playback in Godot, keys, the `VoiceChat` node, the overlay, hosting glue, the `voice` smoke test |
| `tests/OpenScct.Net.Tests/Voice/` | codec, jitter buffer, routing, relay, gate, wire and driver tests |

## The original

The stock game already had voice: DirectSound capture and Speex (8 kHz, 11,025 Hz from Enhanced 3.5), carried in
auxiliary records of the game's datagrams, with the server deciding who is muted for whom. Reloaded v4.0 keeps that
system and swaps the codec for Opus: 16 kHz mono, VOIP, 16 kbit/s constrained VBR, complexity 10, 20 ms frames of 320
samples, at most 62 bytes per packet. The rewrite keeps the user controls and the routing rules, and uses its own
transport (no wire compatibility is needed).

## Pipeline

```
microphone -> AudioStreamMicrophone on bus SvmVoiceCapture (muted) -> AudioEffectCapture
  -> mono, resampled to 48 kHz (Speex resampler in Concentus) -> 960-sample frames -> input gain
  -> VoiceGate (push to talk, team key, activation gate with hold) -> VoiceEncoder (Opus)
  -> Up datagram -> VoiceTransport (same socket as the game) -> server
server: VoiceTransport -> VoiceRelay: who may hear (VoiceRouting over the roster), joined, not muted, talker cap
  -> Down datagram per listener (the server writes the talker id)
client: VoiceTransport -> VoiceClient -> VoiceTalker per talker: VoiceJitterBuffer -> VoiceDecoder (packet, FEC or
  concealment) -> volume -> AudioStreamGenerator at 48 kHz on bus SvmVoice -> Master
```

Everything in `OpenScct.Net/Voice` runs on one thread per side: the driver's thread on the server (the relay works inside
the driver's receive pass), the client's thread on a client. A listen host's own player talks to the relay through a
`VoiceLocalLink` (two lock-free queues) because its game traffic is loopback inside the listen server.

## Codec

Concentus 2.2.2 (a managed port of Opus, pinned in `Directory.Packages.props`), always the managed classes, never a
native libopus the factory might find on the machine. Its licence is the Opus three-clause BSD licence (the package's
LICENSE file), which allows redistribution with the notice kept; releases must ship that notice.

| Setting | Rewrite | Reloaded |
|---|---|---|
| Rate, channels, application | 48 kHz, mono, VOIP | 16 kHz, mono, VOIP |
| Frame | 20 ms (960 samples) | 20 ms (320 samples) |
| Bitrate | 20 kbit/s constrained VBR | 16 kbit/s constrained VBR |
| Packet cap | 62 bytes | 62 bytes |
| Complexity, signal, depth | 10, voice, 16 bit | 10, voice, 16 bit |
| In-band FEC | on, loss hint 5% (`UplinkLossPercent`) | not set |
| DTX | off (the gate stops sending in silence) | not set |
| Encoder reset | at each talk spurt | when voice restarts |

Measured (Release, on the development PC): encode 0.72 ms and decode 0.17 ms per frame; one encoder and four decoders use about
6% of a core while talking. Concentus allocates internally (about 120 KB per encoded and 29 KB per decoded frame; the
rest of `OpenScct.Net/Voice` allocates nothing per frame once a talker exists, and Godot's capture API returns a new array
per read): over 10 s of the worst case (one encoder,
four decoders) that was 6 gen0 and 1 gen1 collections with 1.3 ms of total pause.

## Wire format and transport

Voice datagrams go on the game's own transport, so they reach the same endpoints as the game with no extra port, NAT
work or handshake. `VoiceTransport` wraps the transport a `NetDriver` reads: game packets (first byte 0xA7) pass
through, voice datagrams (first byte 0x56) go to the voice side as they are read. They are unreliable and never part
of actor replication.

```
byte 0     0x56
byte 1     bits 0-2 kind (1 Up, 2 Down, 3 Join), bit 3 team channel, bit 4 talk start, bit 5 talk end, bits 6-7 version 1
bytes 2-3  sequence (per sender, one per frame, wrapping)
bytes 4-5  talker: the sender's PlayerID, written by the server on Down; 0 (or a test claim) elsewhere
byte 6     codec 0x11 (Opus, 48 kHz, mono, one 20 ms frame)
bytes 7..  Up and Down: the Opus packet (1 to 62 bytes)
           Join: receive flag, mute count, muted talker ids (at most 32)
```

A client sends Join when it connects and every 2 s: it says the client takes part (the server sends no voice to a
client that has not joined within 6 s) and which talkers it muted. The server rebuilds its routing roster from the
game every 100 ms, and at once for a datagram from a player it does not know yet.

## Jitter buffer and loss

One `VoiceJitterBuffer` per talker, in 20 ms frames, 32 frames of window. Playback starts once
`voice_jitter_frames` frames are banked (1 to 12, default 3), or at once for a short utterance whose end frame is
already here. Each output frame is the packet; or, when only that packet is missing and the next one is here, the
next packet's in-band FEC; or concealment. With nothing banked, the frame is concealed without moving the playout
point, so a packet that is late rather than lost still plays (the delay grows by a frame, counted as `Stretched`, and the
backlog trim takes it back); after three such frames (60 ms) playback stops and banks again (re-buffer after an
underrun). Late packets and duplicates are dropped; reordering inside the
window is undone; a backlog beyond the target plus six frames drops the oldest frames; a talker whose sequence jumps
or who marks a new talk spurt behind the playout point (a restarted client) is followed.

The audio output paces playout: a talker's generator is kept two frames ahead, so the jitter buffer, not the
generator, does the banking.

## Channels, keys and the gate

| Action | Default key | Does |
|---|---|---|
| `voice_ptt` | Caps Lock | hold to talk on "all"; in activation mode it also forces transmission |
| `voice_ptt_team` | Y | hold to talk to your team only (both modes) |
| `voice_mute_toggle` | F8 | self mute |

The stock Hack Comm key (H) stays a gameplay input: it hears tagged enemies, and during a grab it talks to the held
merc (spec/game/voice-chat.md rules 13 and 14). "All" follows the routing rules as they are; "team" reaches only your
team plus eavesdroppers on hacked comms (rule R1 of the spec). In a Modern, Classic or Team match the two differ only
for grab talk and downed chat; in Open mode and the lobby "team" keeps you off the other team.

Activation (`voice_push_to_talk` off, the Reloaded default): the frame RMS after gain opens the gate at
`voice_activation_threshold` (0.02; 0 always transmits, 1 never), the gate stays open while the level is at least 60%
of it, and closes after `voice_activation_hold_ms` (300) below that. The frame after the gate closes goes out marked
as the end of the talk spurt.

## Eavesdropping

The original's ways to hear the other team (spec/game/voice-chat.md rules 12 to 14), all decided by the server from
its copy of each controller and read by `SessionVoiceLinks` every 100 ms:

| Feature | Owner's machine | Server state | Heard |
|---|---|---|---|
| Hack Comm (H held) | `UpdateSpecialsChat` after each PlayerMove sends each change of `bSpecialsChat` (`ReplicateSpecialsChatOnServer`) | `bSpecialsChat` | every enemy carrying your team's tag (spy bullet, merc laser mark), one way, while the key is down, neither side downed and not grabbing (any grab, a ledge grab too, from its start to its release) |
| Grab talk (H while holding a merc) | `s_GrabbingPlayer.VoiceChatButtonToggle` sends `ServerSetVoiceComWhenGrabbing` | `bVoiceComWhenGrabbing` (cleared by the grab's EndState) | spy and merc both ways; the spy's team stops hearing him |
| Voice trace (merc scope, spy binoculars) | once a second a trace along the view, 50 to 6000 × zoom uu (`BinocularVoiceTrace`); an enemy hit plays `Snd_VoiceHacking` and sends `ServerSetVoiceChatTarget`, losing it sends `ServerClearVoiceChatTarget` | `VoiceChatTargetPC`, accepted only for an enemy within 7000 uu | the target, one way |

The HUD: "Hack" while the trace has a target (`VoiceChatTarget`) or an enemy carries your team's tag
(`bCanListenSpyBulletedGuy`), "Speak" while holding a merc (`bCanTalkToGuy`, set by the server when the mode allows it)
or near a downed enemy; the eavesdropped talker shows in the speaker list like any other. Team mode (`CanHackEnemyComs`
false) allows none of it. The mode reaches each client's GRI through `ClientSetVoiceChatMode` (the GRI never
replicates it; docs/ONLINE.md "Voice"), which also sets the downed chat distance to 0 in Team mode.

Voice bans: `PlayerReplicationInfo.bVoiceBanned` (rule 1 of the routing: a banned player hears nobody and nobody hears
him). The original set it from Xbox Live; the rewrite's host or admin sets it (`voiceban`, `!voiceban`, the lobby's
Voice Ban; docs/ONLINE.md).

SetAudioZoom: while the local player's scope or binoculars are up, `PlayAudio` hands `AudioZoomFactor` and the view
FOV to `SoundSystem.SetAudioZoom`, and each 3D sound's volume is multiplied by 1 + zoom × (3 - 3.9 r), r = min(1,
112.5 / FOV × the sound's sideways share of its direction), never above full volume (`SoundRules.AudioZoomGain`,
decoded from the game's UNIAudio at 0x10b06280; the reading of r is estimated). Sounds on the aim line get louder,
the rest quieter.

## Settings

`VoiceSettings` holds the Reloaded keys: `Enabled` (the stock "Allow VoiceChat"), `PushToTalk`
(`voice_push_to_talk`), `MicMuted` (`voice_muted`), `ActivationThreshold`, `ActivationHoldMs`, `InputGain`
(`voice_input_gain`), `OutputVolume` (`voice_output_volume`, the `SvmVoice` bus volume) and `JitterFrames`
(`voice_jitter_frames`). Per talker: `VoiceChat.SetMuted` (one way, also stops the server sending him) and
`VoiceChat.SetVolume` (0 to 2). `VoiceAudio.StartMicrophone(device)` picks an input device by name and reports
`MicrophoneProblem` ("the system reports no microphones", a missing device falls back to the system's choice).
`game/project.godot` enables `audio/driver/enable_input`; Godot opens the microphone only when the capture player
starts. Persisting the settings belongs to the options screen.

## HUD

`VoiceChat` keeps `Speakers` (local player first, then talkers in the order they spoke; a row stays while speaking
or muted) and raises `SpeakerChanged(VoiceSpeaker)` on the main thread whenever a row changes: talker id, name, team,
local or remote, speaking, channel, muted, volume. Names and teams come from the client world's replicated players
(`VoiceHosting.Bind`). `VoiceIndicator` is a plain overlay drawn from that list (top right, team coloured, "(team)",
"muted", "Your microphone is muted") until the Magma HUD draws its own from the same event.

## Hosting

`VoiceHosting` has the glue; none of it is wired into a session yet.

- Dedicated server: `var t = VoiceHosting.Wrap(udp); var host = new ServerHost(t, settings);
  var relay = VoiceHosting.CreateRelay(host, t, clock);`
- Listen server with remote players: pass `VoiceHosting.WrapListenRemote(udp)` as the ListenServer's remote
  transport, create the relay with it, and connect the host's own player with `VoiceHosting.AttachLocalPlayer`.
- Client of a remote host: `new ClientSession(VoiceHosting.Wrap(udp), ...)`, then `VoiceHosting.AttachRemotePlayer`.
- Routing reads the server's world through `NetDriverVoiceDirectory` (each connection's PlayerController and
  PlayerReplicationInfo, the GRI's voice mode and switches) and the SBase special cases through `SBaseVoiceLinks`.

## Bandwidth

| Flow | Per 20 ms | Per second |
|---|---|---|
| One talker, payload and header (at most) | 69 bytes | 3,450 bytes |
| One talker, typical (measured on a voice-like two-tone signal: 44-byte packets) | about 51 bytes | about 2,550 bytes |
| With UDP and IPv4 headers (28 bytes) | at most 97 bytes | at most 4,850 bytes |
| One listener, four talkers at once (the cap) | | at most 13,800 bytes (19,400 with UDP) |
| Join keep-alive | | 4 to 36 bytes every 2 s |

The original's limits: Reloaded's codec cap of 62 bytes per 20 ms (24.8 kbit/s) is kept exactly; the talker cap keeps
a listener's voice download (at most 13.8 KB/s) plus the game's (1.1 to 2.3 KB/s in the bot matches of
docs/NETCODE.md) under the original's `MaxClientRate` of 20,000 bytes a second per client. Uplink is one talker's
stream. The server forwards and never transcodes. Voice is not counted in the game's send budget (it does not take
bandwidth away from replication); the cap and the rate limit bound it instead.

## Tests

- `dotnet test --project tests/OpenScct.Net.Tests/OpenScct.Net.Tests.csproj -- --filter-namespace OpenScct.Net.Tests.Voice`: codec round
  trip (a 440 Hz tone correlates above 0.9 after decoding, stays under the packet cap), concealment and FEC, the
  jitter buffer (banking, reorder, duplicates, late packets, FEC and concealment, underrun and re-buffer, backlog trim,
  a packet delayed while the buffer is dry, restart, wrap-around, and a property test over random loss, duplication and reordering), every routing rule, the
  gate, the wire format, and the relay end to end (team only, the speaking indicator, mutes, the talker cap, rate
  limit, forged ids, the local link, death in Classic, a lossy SimLink C, bandwidth, activation, 44.1 kHz capture),
  plus voice on real `NetDriver`s routed by `NetDriverVoiceDirectory`.
- `pwsh tools/ci/godot-smoke.ps1 -Tests voice`: the loopback smoke. A 440 Hz tone plays on the capture bus, player
  1's chat captures and sends it, the relay routes it to player 2 (same team) and not to player 3, and player 2 must
  hear 440 Hz; the speaking indicator must go on and off with push to talk and the uplink stay within budget. When the
  audio driver does not mix (headless), the tone is fed to the client directly and the detail says so.

## In the online session

Voice is wired into every online visit (docs/ONLINE.md "Voice", docs/MENUS.md): the server's relay on each map, lobby
included; `OnlineVoice` and the game's `OnlineVoiceChat` on each player (microphone, keys, the options' Voice page
settings saved in `user://settings.json`, per-player volume and mute from the lobby, Reloaded's overlay in a round).
Talker ids on the wire are PlayerReplicationInfo ids plus one (`VoiceK.TalkerOf`), session player ids in the lobby.

## Not done yet

- Noise suppression, automatic gain and echo cancellation (FEATURES rl:voice.noise, gain, echo; P1 and P2). The
  microphone test is on the Voice page.
- Offline play (the local listen server of `--svm-play`) has no voice.
- Downed chat waits for the native code that finds chat partners (spec open question 1).
- The lobby rules apply on the online session's lobby level, through the session's own directory.
