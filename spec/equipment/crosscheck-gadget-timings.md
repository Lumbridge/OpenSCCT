# Cross-check with the Bridge gadget timing research

Units: none (comparison note). Compared: `SCCT_Versus_UE5_Bridge/docs/GADGET_TIMINGS.md` and `docs/gadget_timings.json`
against the specs in this folder.

## Purpose
Record where the earlier timing research and these specs agree, where they differ, and which differences come from
new evidence (decoded script, live class defaults, clip data, live reflection).

## Data
Rows compared: smoke flight, smoke cloud, smoke on a merc, smoke faint, chaff fuse and field, chaff on devices,
frag fuse and explosion, shocker hit on a merc, shocked pawn sparks, taser, torch malfunction, muzzle flashes,
gadget launch flash; json sections `on_player_effects`, `addresses`, `smoke_lifetime`.

## Rules
Agreements (same values, same reasons):
1. Smoke: 3 s fuse, 10 s cloud, 300 uu + collision radius, cough 1.5 s, faint after 6 s continuous exposure with the
   1.5 s reset, blur 0.8 over 6 s, 2 s wake grace, cough every 1 s (`smoke-grenade.md`).
2. Chaff: 3 s fuse, 10 s field, 2 s HUD perturbation, torch malfunction 5 s (`merc-flashlight.md`).
3. Frag: 2 s fuse; light fade 0.5 s; explosion actor 23 s (`frag-grenade.md`).
4. Shocker on a dry merc: freeze 3.5 s, electric 4.5 s, re-shock immunity 12.5 s, head-shot HUD shutdown 12.5 s,
   grab immunity 5 s, whole-second truncation of the end times (`sticky-shock-effect.md`).
5. Spy muzzle 0.1 s, launch flash 0.1 s.

Differences and additions:
1. **Smoke "thrown at 900 uu/s"**: 900 is only `ProjectileSpeed`. The aimed throw passes
   `Force = int((v + 1)^2 * 450)`, from 450 (hold just past 0.25 s) to 1800 (tap under 0.25 s, or hold 1.25 s); 900
   corresponds to v of about 0.41. Nothing clamps the launch to `MaxSpeed`. The quick launch drops the grenade
   with force -200 straight down from under the spy's hand (`grenade-throw.md` rule 6a).
2. **Impact explosions**: the research decodes stock behaviour (smoke explodes on touching a pawn, and on the first
   wall hit when quick-launched). The quick launch part is confirmed and kept: the HitWall overrides of the smoke
   (game 0x10ac1c60) and of the chaff and flash-bang (0x10ab8760) explode a quick-launched grenade at its first hit,
   floors included, Reloaded does not patch them, and in Reloaded quick smoke, chaff and flash grenades pop at once
   (`grenade-throw.md` rules 6a and 10a). Reloaded's rl:fix.instas (`spec/reloaded/FEATURES.md`) presumably covers
   aimed throws only; the pawn-touch explosion (ProcessTouch) is not ported and stays open.
3. **Frag damage**: the research covers only visuals. Script forces 200 damage for SDamage_Explosion on pawns
   (always lethal at 100 health) and gives spies 3 s spawn protection against explosions; the plan's "damage 334"
   is the field value. The damage type the native passes is still open.
4. **Wet shock**: the research gives only the wet freeze (4.55 s). Wet also lengthens the electric state to 5.55 s,
   the immunity to 13.55 s, the head-shot shutdown to 13.55 s and the torch malfunction to 5.55 s. Spies: 3.0 s
   freeze and electric (3.9 s wet), 11 s immunity, no HUD shutdown.
5. **Level-start immunity**: `RealTimeInvulSticky` starts at 5 s with `TimeShock` 0, so no shock can land in the
   first 5 s of level time (not in the research).
6. **Taser knock-down** happens only if the victim is in a plain walking state (St2H, St0H, Cr0H, Cr2H) when the
   freeze ends; the research states it unconditionally.
7. **Perturbation side effects**: the 2 s chaff perturbation and the shock perturbation also refuse merc reload,
   full reload, scope entry and goggle switching (not in the research).
8. **Smoke KO authority**: the KO is decided on the merc's client and the server accepts the state change without a
   smoke check; on a dedicated server the emitter never detects anyone (no local player), so attribution happens in
   `PCChangeServerState` (not in the research).
9. **Reloaded hooks**: the research reads stock natives in the game image. The live reflection shows that
   `ThrowGren`, `NativeTickTorche`, `SMotionTrackingVision.GetActors`, `SPawnAttaque.TickNative`,
   `SBasePawn.ExecuteAction`, `SHud.ProcessSFX`, `Core.Object.FRand`, `Actor.SetRotation` and `Controller.GotoState`
   point into Reloaded.Core.dll, so stock decodes of those paths may not match the live game.
10. **Frag charges**: the item text says "5 Units Pack" but `Charge` is 4 (live class default).

## States
Not applicable.

## Constants
See the per-gadget specs; no new constants here.

## Network
Not applicable.

## Edge cases
None beyond the per-gadget specs.

## Evidence
- Bridge docs/GADGET_TIMINGS.md (rows cited above), docs/gadget_timings.json (`on_player_effects`, `addresses`).
- local/reflect/game.json function pointers (Reloaded.Core.dll base 0x64750000, size 0x42e000).
- local/re-out/uc/SBase decompiled script; local/reflect/game_cdo.json.

## Open questions
- Re-run the research's native decodes on the Reloaded replacements listed in difference 9.
