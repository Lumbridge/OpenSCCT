# Voice chat: who hears whom

Units:
- uc:Engine.GameInfo:{ChangeVoiceChatter, MuteVoiceChatters, TeamVoiceOnlyApplies, MuteVoiceChattersByPlayer, HearVoiceChatter}
- uc:SBase.SGameInfo:{ChangeVoiceChatter, UpdateVoiceChatMuting}
- uc:Engine.PlayerController:{ServerChangeVoiceChatter, ServerGetVoiceChatters, ServerHearVoiceChatter, ServerMuteVoiceChatters, ClientMuteVoiceChatter, ClientChangeVoiceChatter, ClientSetVoiceChatterId, ClientEnterVoiceChat, ClientLeaveVoiceChat, WantsTeamVoiceOnly, SetAllowVoiceChat, OpenVoiceChatSession, CloseVoiceChatSession, ServerSetAllowVoiceChat}
- uc:SBase.SPlayerController:{TalkToMate, OtherTeamCantHearMe, HearPC, s_Inactive.HearPC, CanHackEnemyComs, CanAlwaysTalkToMates, CanRouteVoice}
- uc:SBase.SPlayerController:{ServerSetVoiceChatTarget, ServerClearVoiceChatTarget, StopHackComm, HackCommHear, IsHackCommUnmuted, ServerHearSpybullets, UpdateSpecialsChat, ReplicateSpecialsChatOnServer, VoiceChatButtonToggle, s_GrabbingPlayer.VoiceChatButtonToggle, ServerSetVoiceComWhenGrabbing}
- uc:SBase.SPlayerController:{ServerSetTeamVoiceOnly, WantsTeamVoiceOnly, ServerToggleTeamVoiceChat, SetVoiceBanned, VoiceBanned, PropagateMuteToServer, PropagateMuteToServerInternal, PropagateMuteToClient, MutePlayerChange, ServerGetVoiceChatters, ClientSetVoiceChatMode, ClientEnterVoiceChat}
- rl:voice.routing

Related: `spec/reloaded/FEATURES.md` section 5 (Reloaded's codec and controls), `spec/enhanced/FEATURES.md`
`enh:voice.modes` (the four modes), `spec/native/reloaded-features-from-code.md` rule 1. The voice transport of the
stock executable (DirectSound capture, Speex, the auxiliary datagram records of `spec/native/net-driver.md` rule 12)
is not ported: the rewrite has its own (docs/VOICE.md). Callers that already reach the voice units:
`death-lives-respawn.md` rules 10 and 13, `knockout-and-terminate.md`, `spy-grab.md` rules 15 and 20,
`spectating.md` rules 11 and 22, `login-and-teams.md` rules 34 and 43, `match-lifecycle.md` (VoiceChatMode).

## Purpose

Decides, for every pair of players, whether the listener hears the speaker. The original keeps this as a mute
state per pair that the scripts change when something happens (a player joins or changes team, dies, goes down,
respawns, grabs, uses binoculars, holds the Hack Comm key, spectates, is muted or banned). The audio itself goes
through the engine, which plays what the listener has not muted.

## Data

| Field | Owner | Meaning |
|---|---|---|
| VoiceChatters (array of controller and id byte) | GameInfo | players in voice chat; ids are the smallest free byte from 1 |
| MutedPlayers (array of actors) | PlayerController | players this one muted (online mute list, voice ban); always kept symmetric |
| VoiceChatMode | GameReplicationInfo | Classic 0, Modern 1, Team 2, Open 3; default Modern |
| allowVoiceChat, allowVoiceChatInLobby | GameReplicationInfo | the host's switches (profile setting, lobby setting) |
| allowVoiceChat, bVoiceBanned | PlayerReplicationInfo | the player's own switch; banned by the host |
| bTeamVoiceOnly | SPlayerController | the lobby's "Restrict Mic to Team" |
| PCChattingWith[3] | SPlayerController | enemies chatting with this downed player |
| VoiceChatTargetPC | SPlayerController | the enemy the scope's or binoculars' voice trace found (server) |
| VoiceChatTarget, bVoiceChatTargetSent | SPlayerController | the same on the owning machine, and whether the server was told |
| HackCommUnmuted (array) | SPlayerController | enemies heard through hacked comms |
| bSpecialsChat | SPlayerController | the Hack Comm key (input button, stock bind H); the owner sends each change with ReplicateSpecialsChatOnServer |
| bCanTalkToGuy | SPlayerController | set by the server while a spy holds a merc and enemy coms can be hacked: the HUD's "Speak" prompt |
| bCanListenSpyBulletedGuy | SPlayerController | set by the server while an enemy carries the player's team's tag: the HUD's "Hack" prompt |
| bSpyBulletOn, iTeamSpying | SBasePawn | tagged by a spy bullet or the merc laser, and by which team |
| fDistToTalkWhenOnGround | SPlayerController | downed chat range, 300 uu; 0 in Team mode |

### Inputs

The rewrite reads the same state as a snapshot. Per player: id (PlayerReplicationInfo.PlayerID), team index (-1
without one), life state, bTeamVoiceOnly, bVoiceBanned, the mute lists, and the special cases (grab talk partner,
binocular target, Hack Comm key held, tagged-by team, downed chat partners). Life state:

| Life state | Controller state or flag |
|---|---|
| Visitor | PlayerReplicationInfo.bOnlySpectator (late joiners, s_Spectating_Visitor) |
| LyingOnGround | s_LyingOnGround |
| Dead | s_Dead, s_MultiSpectating_WhenKilled |
| Alive | anything else (playing, waiting to spawn, s_WaitRespawn after the respawn request) |

## Rules

Voice on at all:
1. Nobody hears anyone when the host's `allowVoiceChat` is off, or in the lobby when `allowVoiceChatInLobby` is off.
   A player whose own profile has voice off never enters voice chat. Nobody hears himself.

Mutes:
2. A voice ban (`SetVoiceBanned`) puts the banned player on every other player's mute list and theirs on his: nobody
   hears him and he hears nobody, in every mode.
3. The mute lists are symmetric: when one player mutes another (the online mute list, `PropagateMuteToServer`), both
   lists get the other, and neither hears the other. Every request to un-mute a pair checks both lists first, so a
   mute beats every rule below.

Modes and the lobby:
4. Open mode: every mute or un-mute request between two different players becomes "hear". Everyone hears everyone,
   whatever their team or state, except for rules 2 and 3 and, in the lobby, rule 5.
5. In the lobby the team rules are skipped: everyone hears everyone. A player who restricted his mic to his team
   (`ServerSetTeamVoiceOnly`, only acted on in the lobby) is not heard by the other team.

The match (Modern, Classic, Team):
6. Visitors have no team. The team rule mutes them both ways with every team player, and their spectating code mutes
   every controller from hearing them (each new spectating target). So, outside Open mode, a visitor hears no one and
   no one hears him.
7. Team rule: when a player joins, changes team or anyone enters voice chat, players on different teams are muted
   both ways and teammates hear each other.
8. Talking to mates: on death, on going down, and when a dead player starts spectating, the player's teammates stop
   hearing him unless the mode lets him always talk to mates (Modern and Team). Respawning and standing up restore
   it. A dead or downed player still hears his living teammates. In Classic, therefore, the dead and the downed are
   silent to their team (two dead teammates do not hear each other); in Modern and Team they talk normally.
9. Downed chat: while a player is down, the engine reports enemies within `fDistToTalkWhenOnGround` (300 uu; 0 in
   Team mode) as chat partners, up to three. The downed player hears each partner; the partner's own teammates stop
   hearing the partner. Standing up, death and respawn release every partner and give their teams back.
   The report is native (decoded, editor ASPlayerController::Tick 0x111f41b0 and its helper 0x111f1740): every tick
   on the server, while the player's pawn has `bLyingOnGround`, each partner who has a pawn, is on the other side and
   is farther than the range or has let go of the Hack Comm key (`bSpecialsChat` 0) is dropped with
   `HearPC(partner, false)`. Then, if the downed pawn is alive, every enemy controller with a pawn closer than the
   range who holds the Hack Comm key and is not yet a partner takes the first free slot of the three, with
   `HearPC(enemy, true)` (no slot free: ignored). Only `s_Inactive` and its children act on `HearPC`. `bLyingOnGround`
   stays set while a merc finishes the spy (`s_TerminatedPawn`), so the merc holding the key talks to the spy he is
   finishing; the spy is not heard by him (rule 11 still holds from going down) unless another rule links them.
10. Team mode refuses every un-mute between players of different teams outside the lobby: none of rules 9 and 12 to
    14 ever connects enemies, and Hack Comm and the binocular trace do not start.

Across teams in Modern and Classic:
11. The other team never hears a dead or downed player: on death and on going down every enemy controller mutes him,
    which also ends any eavesdropping on him.
12. Voice trace: a merc in his rifle scope (s_Binoculars_Def) and a spy in his binoculars (s_Binoculars_Att) trace
    once per whole second along the view, from 50 uu ahead of the eyes to 6000 × AudioZoomFactor (0.33, 0.66, 1.0 at
    the three zoom levels). When the trace hits an enemy pawn (a spy for the scope, a merc for the binoculars) and
    enemy coms can be hacked, a hacking sound plays (ClientPlaySound Snd_VoiceHacking), the HUD shows the Hack prompt,
    and the tracer hears that enemy one way (ServerSetVoiceChatTarget). Hitting nothing or anything else, leaving the
    scope or binoculars, and the tracer's death or downing end it (ServerClearVoiceChatTarget). The trace runs on the
    owning machine; the server only keeps the target.
13. Hacked comms: a pawn tagged by a team (a spy bullet, or the merc laser's mark; `bSpyBulletOn`, `iTeamSpying`,
    90 s) is heard, one way, by every player of that team who holds the Hack Comm key. It is not heard while either
    side is down. Pressing the key hears every tagged enemy; releasing it, death and going down stop it; a new tag
    while the key is held is heard at once. A grab stops it from the moment it begins (a ledge grab too) until its
    release, which gives it back if the key is still held.
14. Grab talk: a spy holding a merc who presses the Hack Comm key talks with that merc both ways, and his own team stops
    hearing him; releasing the key or the end of the grab gives both back. Without the key the grab changes nothing.

The state at any moment:
15. Because every change above is made at an event and undone at the opposite event, the result is a function of the
    players' current state. The rewrite evaluates that function (`VoiceRouting.CanHear`), checked in this order:
    rule 1; rules 2 and 3; Open mode or the lobby (rules 4 and 5); a visitor on either side (rule 6); same team
    (rules 7 to 9: heard unless the speaker grab-talks, chats with a downed enemy, or is dead or down in Classic);
    Team mode (rule 10); otherwise across teams (rules 11 to 14).

## Server relay

The rewrite's transport (docs/VOICE.md) sends every frame to the server, which forwards a copy to each listener rule
15 allows. Additions that are not in the original, kept apart so the rules above stay as decoded:
- R1. Channels. A talker transmits on "all" (the rules as they are) or "team" (a second push to talk key). Team
  frames reach only the talker's team, plus eavesdroppers through rules 12 and 13. This extends the lobby's
  team-only switch (rule 5) to every map; in a Modern, Classic or Team match "all" and "team" differ only for grab
  talk and downed chat.
- R2. A listener's own mute list is one way (the Reloaded overlay mutes a talker for you only) and is applied by
  the server so muted audio is not sent. The stock symmetric lists of rule 3 stay as they are.
- R3. At most four talkers reach a listener at once; a fifth waits for a free slot (a slot frees 320 ms after its
  talker's last frame).
- R4. A talker may send at most 60 frames a second (burst 15); the server writes the talker's id itself, so a client
  cannot speak for someone else, and audio from an endpoint that is no player is dropped.
- R5. The server accepts a voice trace target only on the other side and within 6000 uu plus 1000 of slack (the
  trace's longest reach and a second of movement; estimated), so a modified client cannot pick any enemy to hear.

## States

None of its own: the rules read the controller states listed under Inputs.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| fDistToTalkWhenOnGround | 300 | uu | decoded | SPlayerController defaults; ClientSetVoiceChatMode sets 0 in Team mode |
| Binocular trace length | 6000 × AudioZoomFactor | uu | decoded | s_Binoculars_Def PlayerMove (spec/equipment/merc-scope.md) |
| AudioZoomFactor in binoculars | 0.33 | | decoded | s_Binoculars_Def BeginState |
| Binocular trace start | 50 | uu | decoded | s_Binoculars_Def PlayerMove |
| Binocular trace period | 1 | s | decoded | once per whole second of TimeSeconds |
| TimeSpyBullet | 90 | s | decoded | SBasePawn defaults |
| Downed chat partners | 3 | | decoded | PCChattingWith[3] |
| VoiceChatMode from the host's setting | 1 Classic, 2 Team, 3 Open, else Modern | | decoded | SGameInfo.PostLogin |
| EVoiceChatMode values | Classic 0, Modern 1, Team 2, Open 3 | | decoded | Engine.GameReplicationInfo |

## Network

All routing runs on the server (`CanRouteVoice`: a dedicated or listen server with a game info). The owner's inputs
reach it through reliable calls: `ReplicateSpecialsChatOnServer` (the Hack Comm key, from UpdateSpecialsChat after
each PlayerMove), `ServerSetVoiceComWhenGrabbing` (the key during a grab), `ServerSetVoiceChatTarget` and
`ServerClearVoiceChatTarget` (the voice trace). The stock client
only mutes and unmutes chatter ids it is told to (`ClientMuteVoiceChatter`, reliable). The GRI does not replicate
`VoiceChatMode` through its native list (spec/native/net-replists.md); the server sends it with
`ClientSetVoiceChatMode` when a player asks for the chatter list. In the rewrite only audio crosses the network;
routing state never does.

## Edge cases

- Team mode keeps rule 8's exception (dead and downed talk to mates) but rule 14's mute of the grabbing spy's own
  team still applies: a spy who presses the Hack Comm key while grabbing in Team mode is heard by no one until he
  releases it. The rewrite keeps this.
- A dead player who is out of lives stays in s_MultiSpectating_WhenKilled: in Classic his team never hears him again
  that round.
- A late joiner in Story is a visitor for the rest of the round (login-and-teams.md rule 15): silent in Modern,
  Classic and Team.
- Open mode overrides rule 11 too: the other team hears the dead and the downed.

## Evidence

- Local decompiler output `local/re-out/uc/Engine/GameInfo.uc` (ChangeVoiceChatter, MuteVoiceChatters,
  TeamVoiceOnlyApplies, MuteVoiceChattersByPlayer, HearVoiceChatter, Logout), `SBase/SGameInfo.uc`
  (UpdateVoiceChatMuting, ChangeTeam, PostLogin, Logout), `SBase/SPlayerController.uc` (the functions in Units,
  s_Dead, s_LyingOnGround, s_StandUp, s_GrabbingPlayer, s_Binoculars_Def, ServerRespawn, Server_FindNextVisitor),
  `SBase/SBasePawn.uc` (HitBySpyBullet, Tick), `SBase/SProjMarqueurSpy.uc`.
- Editor natives `APlayerController::execMuteMeForVoiceChatter`, `execAmIMutedForVoiceChatter`,
  `execMuteVoiceChatter`, `execChangeVoiceChatter`, `execEnterVoiceChat` (re/ghidra/annotations/editor.json).
- Tests: `tests/OpenScct.Net.Tests/Voice/VoiceRoutingTests.cs` (every rule), `VoiceRelayTests.cs` (R1 to R4).

## Open questions

1. Answered in rule 9 (editor executable). Not checked against the game executable's copy.
2. Whether the engine itself filters audio by the mute state on the server (MuteMeForVoiceChatter) or only on the
   receiving client; the rewrite filters on the server either way.
3. Whether `SetVoiceMask` (voice masking, an Xbox Live feature) does anything on PC.
4. Whether `bTeamVoiceChatState` (ServerToggleTeamVoiceChat) is read anywhere; no reader was found in script.
