# Reloaded.Core features, as the code implements them

Units: rl:voice.codec, rl:voice.activation, rl:voice.device, rl:voice.test, rl:net.master, rl:net.upnp,
rl:net.ports, rl:gfx.fps, rl:gfx.pacing, rl:gfx.fpscounter, rl:gfx.widescreen, rl:gfx.hud, rl:input.sens,
rl:input.curves, rl:input.mouse, rl:input.pad, rl:input.rumble

## Purpose

`spec/reloaded/FEATURES.md` lists what Reloaded v4.0d gives players, from its config file, strings and
release notes. This file adds what the code shows: formats, numbers, timings and where each feature hooks
the game, and says what the rewrite needs to match. Behaviour only, in our own words. The rewrite never
talks to Reloaded's master server and never reproduces or works around its integrity system (section 9).
Native replacements are in `reloaded-hooks.md`.

## Data

Reloaded.Core v4.0d, base 0x10000000, statically links Opus 1.5.2, speexdsp, MiniUPnPc 2.3.2, c-ares,
SDL3, Dear ImGui and nlohmann::json. The JSON library serves the settings file only; no network code uses
JSON or HTTP except MiniUPnPc's own SOAP requests to the router.

## Rules

### 1. Voice chat

1. **Pipeline.** Reloaded keeps the game's own voice system (DirectSound capture and playback, the game's
   voice channel, its routing rules: team talk, grab talk, Hack Comms, Enhanced voice modes) and replaces
   the codec. It patches the game's voice code (several sites in the audio toolkit, for example game
   0x10b08af0, 0x10b08b18, 0x10b097e0) so that captured audio goes to Opus and received payloads come back
   from Opus.
   So what crosses the network is the game's voice traffic with Opus frames as payload.
2. **Encoder.** One Opus encoder: 16,000 Hz, mono, VOIP application. Settings: target bitrate 16,000 bit/s,
   complexity 10, signal type voice, LSB depth 16, variable bitrate on and constrained. The encoder is reset
   (not recreated) when voice restarts.
3. **Frames.** 320 samples (20 ms at 16 kHz) per Opus frame, at most 62 bytes per packet (so at most about
   24.8 kbit/s). The decoder produces 320 samples per packet and hands them to the game's playback in two
   blocks of 160 samples (the game's own 10 ms voice frame); missing or failed packets are filled with
   silence and counted.
4. **Device.** Capture devices are listed with DirectSound's capture enumeration and stored by GUID in
   `voice_input_device`; the last seen name is cached in `z_cache_voice_input_device_name`. Empty follows
   Windows, which for voice means the default communications recording device. "Windows reports no
   microphones" when the list is empty.
5. **Transmit control.** Push to talk and mute are key binds that call the script execs `PushToTalk` and
   `ToggleVoiceMute`, which Reloaded handles. Voice activation uses `voice_activation_threshold` (0 to 1 on
   the level meter's scale) and `voice_activation_hold_ms`; the config text describes a close level of 60%
   of the threshold. Gain (`voice_input_gain` or automatic), noise suppression (speexdsp, four depths) and
   the experimental echo canceller run before the encoder; the jitter buffer holds `voice_jitter_frames`
   frames.
6. **Microphone test.** Records through the full send chain (encode and decode) for up to 3 s (48,000
   samples at 16 kHz), then plays it back with the Windows wave output, 16 kHz mono 16-bit.

Rewrite needs: own voice transport and codec (FEATURES `rl:voice.codec`: Opus); keep the same user
controls (PTT, mute, threshold plus hold, gain, suppression levels, jitter frames, mic test) and the
game's routing rules. No wire compatibility is needed. Note that Reloaded's sample rate (16 kHz) and frame
(20 ms) are a floor for perceived quality, not a target.

### 2. Master server

7. **Transport.** UDP, binary, on the same socket as the LAN browser (the `UdpLink` query socket, bound to
   `host_port_query` on a listen host and `client_port_query` otherwise; `reloaded-hooks.md` rule 5). Every
   message starts with an 8-character UTF-16 tag. Text fields are UTF-16 except the master's messages, which
   are UTF-8.
8. **Address.** `master_server_dns` (default `master-v2.scct-reloaded.ovh`) resolved with c-ares, falling back
   to public resolvers and then to the cached `z_cache_master_server_ip`. Only datagrams from that address
   and port are treated as master replies.
9. **Host registration.** A listen host with `list_on_reloaded_master_server` sends `SCLIREGI` every 60 s
   (15 s after a send error), 128 bytes: tag, protocol version 0x0210, a "first registration" flag, the host
   player's name (16 UTF-16 characters), a 64-byte client string, five status bytes and an 8-byte value.
   With listing off it says once "Server will not be added the Reloaded master server".
10. **Registration reply.** `SCLIREGR`, exactly 288 bytes: an update level byte, a status byte (accepted;
    optional update; mandatory update of Reloaded; new version of Enhanced required; rejected; temporary
    timeout), the external query port the master saw (16-bit) and a UTF-8 message of up to 256 bytes,
    printed between "Master Server" rulers. When the seen port differs from `host_port_query` the host is
    told port forwarding is wrong and that it is not listed; with UPnP off it names the two ports to forward.
11. **List request and reply.** A searching client sends `SCLIREQL` (128 bytes, same layout as `SCLIREGI`) at
    most every 3 s. `SCLILIST` (at least 153 bytes): a status byte (below 17 is valid), an update level byte
    (above 2: "New Reloaded features are available"), a flag byte, a UTF-8 message of up to 128 bytes at
    offset 25, then 6-byte server entries (IPv4 address and port, network order) from offset 153. The
    client sends the normal LAN query to each entry and to each `server_list` address.
12. **LAN query.** The stock protocol stays: `SCLIPING` (a 40-byte broadcast with a random nonce) answered by
    `SSRVINFO` (204 bytes) or `SSRVSTOP`. Reloaded adds `SCPINGEX` answered by `SSRVINEX` (422 bytes) with the
    request identifiers echoed and the server's processing time, which gives an accurate ping.
13. **Join.** A "JOIN H=" text is built from a host identifier for joining by invite (not decoded further).

Rewrite needs: its own master (`OpenScct.Master`, FEATURES `rl:net.master`) with the same player-facing
results: listing on and off, update notices with three levels, a message of the day, a reachability check
that names the ports to forward, a manual server list, and accurate pings. Not the same packets.

### 3. UPnP

14. On a listen host with `host_upnp`, binding the query socket also maps that port, and hosting maps the
    game port (`host_port_game`, 8888 by default). For each port not already mapped in this session: delete
    any existing mapping, then add a UDP mapping with the same external and internal port, the host's LAN
    address, Reloaded's description, no remote host restriction and a lease of 86,400 s. The result is logged
    as "UPNP_AddPortMapping <port>: <code> (<text>)" and shown in the lobby chat; failures name the ports to
    forward by hand.

Rewrite needs: the same on the client process only (FEATURES `rl:net.upnp`), with a 24 h lease and the
lobby message.

### 4. Frame limiter and pacing

15. **Cap.** `fps_spy` and `fps_merc` (30 to 240). Each frame the cap is chosen from the local player:
    the spy cap when the player is on team 0 (spies) and the GUI controller says in game (`bInGame`), the
    merc cap otherwise (merc team, menus, lobby). The frame period is 1/cap.
16. **Schedule.** Absolute deadlines: each frame's deadline is the previous deadline plus one period, so
    small overruns are paid back instead of drifting.
17. **Waiting.** With a high-resolution waitable timer (Windows 10 1803 and later): sleep the whole
    milliseconds left minus one when the fraction is under half a millisecond, then spin to the deadline.
    Without it: raise the timer resolution to 1 ms, call `Sleep(1)` while the time left is at least 1 ms
    plus the mean plus two standard deviations of the measured overshoot of `Sleep(1)`, then spin. The
    statistics are a running mean and variance over up to 256 samples, scaled back to 64 when full.
    `alternate_frame_timing_mode` spins only.
18. **Reduce input lag.** `low_latency_mode` On or Ultra places a GPU event query at the end of each frame
    (two alternating queries) and waits, at most 100 ms, until the GPU has finished: On waits for the
    previous frame's query, Ultra for the current frame's.
19. **Readout.** `show_fps` keeps frame times for the last 5 s (at most 2,048 samples) and shows fps with the
    slowest and fastest 1%.

Rewrite needs: the sim does not depend on any of this (fixed 60 Hz tick). Keep per-team render caps, a
precise limiter with the same hybrid sleep and spin, and the latency modes mapped onto Godot's frame
queue (FEATURES `rl:gfx.fps`, `rl:gfx.pacing`).

### 5. Widescreen field of view and HUD

20. **FOV.** With `widescreen` on, every camera FOV the game sets (a 4:3 horizontal FOV) is converted to
    keep the 4:3 vertical view (Hor+): `fov = 2 × atan(tan(fov43 / 2) × 3/4 × aspect)`, where aspect is the
    render width over height. A second path scales by another runtime factor and 4/3 instead (its use was
    not identified). The result is then capped at `ws_fov` (console range 95 to 112, the settings slider
    the same) except for one view class that Reloaded marks (the merc's first person is capped, which is
    what the UI calls "Mercenary FOV cap").
21. **HUD extents.** The Enhanced scripts read two controller fields, `HudScreenRight` and `HudScreenBottom`,
    as the virtual screen for world-to-HUD projection (Enhanced Reality icons, arrows). Reloaded sets them
    every frame: 640 and 480 by default; with `widescreen` or `widescreen_hud` on, either
    `HudScreenRight = 320 + 240 × aspect` (with `ws_er_small`: icons keep their 4:3 size) or
    `HudScreenBottom = 240 + 320 / aspect` (without it: the 640 wide layout is kept and icons grow).
22. Further HUD placement changes are code patches (for example `ASHud::ERDrawOneObjective`, see
    `reloaded-hooks.md`), not decoded here.

Rewrite needs: Hor+ in the camera port with the same formula and a merc first-person cap; a HUD layout
that offers both ER icon behaviours (FEATURES `rl:gfx.widescreen`, `rl:gfx.hud`).

### 6. Sensitivity

23. Settings per team and per device: spy `free_look`, `gun_look` (each with a response curve), `scope`,
    `vertical`, `cam`, one clutch; merc `look` (with a curve), `scope`, `scope_vertical`, `vertical`, `cam`,
    two clutches; stick and bumper swaps for pads. Curves: `Linear`, `CurveSoft`, `CurveMedium`,
    `CurveAggressive`. They act inside the game's input handling: Reloaded redirects two places of the
    `NativePlayerInput` body (game 0x10aaa973 and 0x10aaaa32) to its own code. The exact scaling was not
    decoded (Open questions).

Rewrite needs: the same groups and fields applied in the PlayerInput port (FEATURES `rl:input.sens`).

### 7. Raw input and mouse

24. Reloaded's only raw input registration is SDL's game pad registration (HID usage page 1, usage 5, with
    device notifications and input in the background); there is no raw mouse registration. The mouse still
    comes through the game's DirectInput path, and the "modern mouse" fixes live in the input patches above.

Rewrite needs: relative mouse motion from the OS (Godot captured mode), summed per tick without clamps
(FEATURES `rl:input.mouse`).

### 8. Controllers

25. SDL3 provides game pads (its mapping database, `SDL_GAMECONTROLLERCONFIG` accepted), "Gamepad connected"
    and "Gamepad disconnected" messages, and rumble. Native controller support is off unless
    `controller_enabled`; deadzones, walk threshold, slow walk, 8-way options and vertical scale are
    settings (FEATURES `rl:input.pad`). Script rumble goes through natives 1910 to 1915
    (`reloaded-hooks.md` rule 28): two motor strengths 0 to 65,535 scaled by `controller_rumble_strength`,
    timed or until stopped, plus trigger motors.

Rewrite needs: Godot joypads (SDL backend), the same settings, rumble from sim events.

### 9. Integrity check (description only)

26. Reloaded checks that the game and the mod are unmodified, on disk and in memory. When the check fails
    it says that modified or unverified files were found, disables mod support on beta builds and asks for
    the CD key again before online play. The check also runs inside its `GotoState` replacement for the local
    player (`reloaded-hooks.md` rule 3), and the Enhanced 4.0 scripts run a server-initiated challenge and
    reply through natives 1906 to 1909 (rule 27). Its strings address automated analysis tools and ask them
    not to help defeat it; this study did not analyse how it works.

Rewrite needs: nothing. The rewrite is a separate game with its own servers and its own trust model; it
does not reproduce, emulate or bypass this check, and its ports of the Enhanced scripts make natives 1906
to 1909 no-ops. For difftest work: in-process probes in the reference game can trip the check, which is why
the recorder reads memory from outside only.

## States

Free camera and hidden HUD are Reloaded modes on the local player (`reloaded-hooks.md` rules 24, 25).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Opus sample rate | 16,000 | Hz | decoded | Reloaded 0x1006ba60 |
| Opus bitrate | 16,000 | bit/s | decoded | Reloaded 0x1006ba60 |
| Opus complexity | 10 | | decoded | Reloaded 0x1006ba60 |
| Opus frame | 320 | samples | decoded | Reloaded 0x1006bb50 |
| Opus packet maximum | 62 | bytes | decoded | Reloaded 0x1006bb50 |
| Game voice block | 160 | samples | decoded | Reloaded 0x1006d3f0 |
| Mic test length | 48,000 | samples | decoded | Reloaded 0x1006ca60 |
| Registration period | 60 | s | decoded | Reloaded 0x100c2c80 |
| Registration retry | 15 | s | decoded | Reloaded 0x100c2c80 |
| List request throttle | 3 | s | decoded | Reloaded 0x100c1060 |
| SCLIREGI and SCLIREQL size | 128 | bytes | decoded | Reloaded 0x100c2c80, 0x100c1060 |
| SCLIREGR size | 288 | bytes | decoded | Reloaded 0x100c20b0 |
| SCLILIST header | 153 | bytes | decoded | Reloaded 0x100c2430 |
| Protocol version field | 0x0210 | | decoded | Reloaded 0x100c2c80 |
| UPnP lease | 86,400 | s | decoded | Reloaded 0x100bd5f0 |
| fps caps | 30 to 240 | fps | decoded | Reloaded 0x100746f0, 0x10074870 |
| Latency query wait | 100 | ms | decoded | Reloaded 0x100a6f10 |
| fps readout window | 5 s, at most 2,048 frames | | decoded | Reloaded 0x100a67a0 |
| `ws_fov` range | 95 to 112 | degrees | decoded | Reloaded 0x100745a0 |
| HUD virtual screen | 640 × 480 | | decoded | Reloaded 0x10079880 |

## Network

Voice (rule 1) uses the game's voice traffic; the master protocol (rules 7 to 13) uses the query port.
Nothing else here is networked.

## Edge cases

- The master treats a host whose external query port differs from its configured one as unreachable and
  does not list it (rule 10), which is why the config warns that ports other than 7776 break LAN joins.
- Fire prediction and the frame limiter are local; difftest runs pin both caps to 60
  (`spec/reloaded/FEATURES.md` section 2).

## Evidence

- Voice: encoder set-up 0x1006ba60 and 0x1006bbf0, encode 0x1006bb50, receive and decode 0x1006d3f0, mic test
  0x10091650 and 0x1006ca60, playback 0x1000b760, device list 0x1007a160 and 0x100911e0, voice patches
  0x1006d720.
- Master: registration 0x100c2c80, reply dispatch 0x100c28f0, list reply 0x100c2430, registration reply
  0x100c20b0, list request and LAN fan-out 0x100c1060, ping 0x100c8c70, client info 0x100c8fa0, server info
  0x100c9d50, join text 0x100c38b0.
- UPnP 0x100bd5f0; query socket bind 0x100cc2b0.
- Frame limiter 0x100a6f10, cap and statistics 0x100a67a0, wait 0x100a6bc0, timer 0x10079dd0.
- FOV 0x100a7080; HUD extents 0x10079880; `ws_fov` 0x100745a0; settings UI 0x100921d0.
- Sensitivity settings 0x1003dfe0, 0x1003e6b0; curves 0x10051f60.
- Raw input 0x101ac390, 0x101acee0; game pads 0x100b2010.
- Strings: `local/reloaded_strings/` (local only).

## Open questions

1. The sensitivity scaling inside the two `NativePlayerInput` redirects (rule 23): read the two stubs at
   Reloaded 0x1001ad20 and 0x10019d30 (not recognised as functions by analysis).
2. Which view class is exempt from the `ws_fov` cap (rule 20; Reloaded flag at 0x1036abbc).
3. The fields of the 64-byte client string and the five status bytes in `SCLIREGI` (rule 9), and the extended
   fields of `SSRVINEX` (rule 12). Only needed if the rewrite ever wants to read Reloaded's browser, which
   it does not plan to.
4. Whether the jitter buffer counts 10 ms or 20 ms frames (rule 5).
