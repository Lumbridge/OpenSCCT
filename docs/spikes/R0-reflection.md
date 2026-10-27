# R0 spike: reflection dumps of the editor and the game

**Result: done.** A headless sandbox editor and its Play Level game (SteeE, spy) were dumped with
`re/tools/reflect/dump.py`, read-only, and diffed. The dumps, class defaults and diff are in
`local/reflect/` (not tracked). Script-side layouts agree between the two builds wherever both
have a class; the differences are in native classes and in two reflection types.

Captured 2026-10-23 from Enhanced SCCT Versus v4.0 Public Beta 3 (game `SCCT Versus` SHA-256
a98873aa..., editor `ChaosTheory_Editor.exe` bce63aa2...).

## Sandbox

- `F:\SCCT-Automation\svm\Sandbox`, made with `New-Sandbox.ps1` from the Beta 3 install, with the
  private RE+ editor DLL built in the `_mapbuilder` worktree at 66e6ed1 (the newest C++ change on
  private main; main has no C++ difference from it), SHA-256 `f9c189dcf4f33684840d0cad4895764d42b2291ace628ce00d1176b3adeb19d0`,
  and the headless proxy built in the RE+ `mb-main` checkout (`headless/bin/d3d9.dll`), SHA-256
  `6dd1d5fc0796b8403f6e57801bc3f52b2b1e21018bfb62d6676053deaa1618e4`.
- The Beta 3 install has never been run, so it has no `SCCT_Versus.config`. The sandbox's copy
  was taken from the Vanilla Reference install (same Reloaded.Core build), then `New-Sandbox.ps1`
  set it windowed. The game is kept off the master server and UPnP, on ports 9168 and 8056.
- Every file of the Beta 3 install carries a downloaded-file zone marker. Play Level then stops
  at the "Open File - Security Warning" box for `SCCT_Versus.exe` on the hidden desktop, where
  nobody can answer it, and the game never starts. The markers were removed from the sandbox copy
  only (625 files). The reference install still has them (628 files): any sandbox made from it
  needs the same step.
- **Not slotted.** It was slotted as `S` first; the first run failed (the zone marker, found
  later) and the slot was undone, since the Beta 3 Reloaded.Core carries an integrity check for
  modified game files and the slot patch changes one byte of the game exe. The sandbox runs the
  unmodified exe under the machine-wide play lock. Whether a slotted exe passes that check was not
  tried.

## Run

`python -I -B re/tools/reflect/dump.py sandbox --sandbox F:\SCCT-Automation\svm\Sandbox --map SteeE --out local/reflect --unslotted`

| Step | Time |
|---|---|
| Editor start to listening channel | 4.1 s |
| Editor dump with class defaults | 1.4 to 1.8 s |
| `map.load` SteeE (MapsEd) | 0.3 to 0.5 s |
| Play Level to game process | 4.2 s |
| Play Level to spy pawn | 10.2 to 10.6 s |
| Game dump with class defaults (heap name scan included) | 2.5 s |
| Play lock held | 17 s |
| Whole run | 24 s |

Seven Play Level starts in all: the first two stopped at the zone-marker box (see Sandbox), the other
five ran with no dialog from the game side. Every run ended every process it started; nothing but
this sandbox's own editor and game ever ran from it.

## Counts

| | Editor | Game (SteeE) |
|---|---|---|
| Live objects | 32,102 | 30,322 |
| Names | 19,463 (GNames array, 0 index mismatches) | 23,281 (heap scan, 5 clashes) |
| Objects whose name did not resolve | 0 | 1 |
| Classes (static in the image) | 871 (522) | 613 (438) |
| SBase classes / functions / states | 248 / 3,686 / 381 | 218 / 3,651 / 377 |
| Structs, states, enums, constants | 101, 435, 137, 966 | 81, 417, 130, 965 |
| Functions (native) | 6,476 (1,080) | 5,761 (1,066) |
| Properties (net) | 17,969 (379) | 15,263 (379) |
| Net functions | 369 | 366 |
| Class defaults dumped (SBase + Engine) | 443 | 406 (79,891 decoded values) |

## Verified

- Object tables: every slot's object has `Index` equal to its slot (both). `Class` is its own class.
- Editor GNames at 0x1169CFBC: every entry's Index equals its slot. GObjObjects at 0x11697B70
  (editor) and 0x10CC9E80 (game) confirmed.
- SBase in the editor: 248 classes, 381 states, 3,686 functions, as in the plan's counts.
- Game natives against `natives_all.txt` (Bridge): all 1,066 found; 1,029 match in iNative, flags,
  Func and ParmsSize; the other 37 match except Func, which points into Reloaded.Core.dll (below).
- Game static classes: the same 438 addresses and names as `scct_versus_symbols.json`; all 193 of
  its vtables come out of this dump for one of the same classes. 882 of its 884 native Func
  addresses agree.
- UFunction: `NumParms` equals the counted parameters for every function (both); ParmsSize and
  ReturnValueOffset agree with the Bridge list.
- RepOffset: every net property's RepOffset lies inside its class's script (both); editor and
  game give the same values. 23 net functions point past their own class's script; all 23 are
  overrides (their replication is declared by the parent).
- Class defaults: `Defaults.Num` equals `PropertiesSize` for every class (both).
- Vtables: where the constructor-code guess can be checked against live instances it agrees for
  160 of 160 classes (editor) and 234 of 243 (game; instances win where they exist).
- Name indices 0 to 1,288,882 (3,665 names) carry the same text in both processes; every index
  from 1,288,883 up is given out at run time and differs.

## Assumed

- Flag names (CPF_, FUNC_, CLASS_, STATE_) are the usual Unreal Engine 2 ones. Values are read.
- Field names `RefLink`, `ConfigLink`, `ConstructorLink`, `NextRef`, `RepOwner`, `RepIndex`,
  `ClassReps`, `NetFields`, `Dependencies`, `PackageImports`, `ClassConstructor` are inferred from
  what the fields hold (lists that follow the flags, arrays of the expected things).
- The editor's extra dword in UState and its 12 extra bytes in UProperty are not identified.

## Layout differences between the builds

- UProperty: the editor's subclass fields start 12 bytes later (RepOffset +60 / +54, the
  Enum, BitMask, Struct, Inner or PropertyClass field +64 / +58).
- UState is 4 bytes longer in the editor, so every UClass field from ClassFlags on is 4 bytes
  later there (ClassFlags +8C / +88, Defaults +B0 / +AC, ClassConstructor +108 / +104).
- ClassReps and NetFields are filled only in the game.
- FString is 8-bit in the editor and UTF-16 in the game.

Full table: `re/tools/reflect/README.md`.

## Diff (editor vs game)

| | Editor | Game | Common | Editor only | Game only | Changed |
|---|---|---|---|---|---|---|
| Classes | 871 | 613 | 612 | 259 | 1 | 34 (33 size, 1 static) |
| Properties | 17,969 | 15,263 | 15,242 | 2,727 | 21 | 21 (offset) |
| Functions | 6,476 | 5,761 | 5,761 | 715 | 0 | 0 |
| States | 435 | 417 | 417 | 18 | 0 | 0 |
| Structs | 101 | 81 | 81 | 20 | 0 | 0 |
| Enums | 137 | 130 | 130 | 7 | 0 | 0 |

- Editor-only classes by package: GUI 115, Editor 73, SBase 30, UnrealEd 30, Engine 7, Core 4.
  The SBase and Engine ones are classes SteeE does not load (for example `SBase.SFish`,
  `SBase.SMessageTrigger`, `SBase.SBombInteraction`, `Engine.UseTrigger`, `Engine.ZoneTrigger`),
  plus `Core.HelloWorldCommandlet` and `Core.SimpleCommandlet`.
- Game-only class: `GameService.GameServiceSubsystem`.
- Classes whose size differs: every UProperty subclass (12 bytes), `Core.State` and `Core.Class`
  (4), and the native classes `D3DDrv.D3DRenderDevice`, `Engine.Level`, `Engine.LodMesh`,
  `Engine.Model`, `Engine.NetConnection`, `Engine.NetDriver`, `Engine.SkeletalMesh`,
  `Engine.SkeletalMeshInstance`, `Engine.Sound`, `Engine.StaticMesh`, `Engine.VertMesh`,
  `Engine.Viewport`, `IpDrv.TcpNetDriver`, `IpDrv.TcpipConnection`, `Menus.MenuManager`,
  `UNIAudio.UNIAudioSubsystem`, `WinDrv.WindowsViewport`. `Engine.MagmaInteraction` is static
  only in the game.
- No common SBase class, property, function or state differs in any compared field.
- Game-only properties: 21 `Engine.NetDriver.CONF*` net speed and lag settings (editor-only there:
  `DefaultPktDup`, `DefaultPktLag`, `DefaultPktLoss`, `DefaultPktOrder`). Changed offsets: 5 in
  `Engine.NetDriver` / `Engine.StaticMesh`, 2 in `IpDrv.TcpNetDriver`, 14 in
  `UNIAudio.UNIAudioSubsystem` (each one 4 bytes later in the game).
- Class defaults of the 406 common SBase and Engine classes: 366 identical; 77,984 of 78,125
  values identical. Of the 141 that differ, 116 are localised strings (the game applies the
  `.int` text, the editor shows the script's) and 25 are the `Engine.NetDriver` properties above.

## Reloaded.Core

37 native functions have their Func pointing into Reloaded.Core.dll in the game (iNative, flags
and ParmsSize unchanged): `Core.Object.FRand`, `Engine.Actor.SetRotation`,
`Engine.Controller.GotoState`, `Engine.PlayerController.Exit`, `IpDrv.UdpLink.BindPort`,
`GUI.GUIController.LANParty_DumpPlayerInfo`, `GUI.GUIController.LANParty_MatchStarted`,
`GUI.GUIController.OpenSettingsTab`, `GUI.GUIController.StepPlaylistMap`,
`GUI.StrategicMap.UpdateTeamMate`, `SBase.SBasePawn.ExecuteAction`,
`SBase.SGadGrenLauncher.ThrowGren`, `SBase.SGadTorche.NativeTickTorche`, `SBase.SHud.ProcessSFX`,
`SBase.SLANClientLink.GetServerEngineVersion`, `SBase.SLANClientLink.PingServers`,
`SBase.SLANClientLink.ReceivedBinary`, `SBase.SLANClientLink.getGameMode`,
`SBase.SLANServerLink.ReceivedBinary`, `SBase.SMotionTrackingVision.GetActors`,
`SBase.SPawnAttaque.TickNative`, and in `SBase.SPlayerController`: `CheckGEForWallJump`,
`MyServerMoveWithPCRot`, `NativeOnlineFlags`, `NativeRenderCaps`, `NativeRumbleEnable`,
`NativeRumblePadStart`, `NativeRumblePadStop`, `NativeRumblePadTime`, `NativeRumblePadUpdate`,
`NativeRumbleTriggers`, `NativeSetLiveState`, `NativeStoreRenderCaps`, `RaiseChatLine`,
`RaiseTeamChatLine`, `ToggleFreeCamera`, `ToggleHideHud`. These are behaviour changes of the
Reloaded patch the port has to account for (`ExecuteAction`, `CheckGEForWallJump`,
`MyServerMoveWithPCRot` and `SetRotation` among them).

## Next

- Class defaults for the 30 SBase classes SteeE does not load: take them from the editor dump
  (their layouts match wherever both builds have the class), or dump the game on maps that load them.
- Localised defaults: the game's values are the ground truth; the class-defaults decoder should
  apply the `.int` files the way the game does.
- Feed the layout table and the class list into R1 (one Ghidra struct per class) and the
  generators; key everything by path, never by name index.
- Ryan to decide whether slotting may be tried on the Beta 3 build; until then the sandbox shares
  the machine-wide play lock.
