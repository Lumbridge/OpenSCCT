# Spy Shocker Gun: how it must play (owner notes, 2026-10-25)

Companion to `spy-shocker.md`. These are the behaviours Ryan expects from playing the original (Enhanced SCCT Versus
4.0 with the Reloaded patch). Each one must be confirmed against the original's code or a sandbox capture before it is
ported, and any difference must be reported, not guessed.

1. **Shock bullet impact.** Every shot leaves the shocker impact effect where it hits (SImpactEffectShocker, or
   SImpactEffectShockerMetal on metal and water; rule 7 of `spy-shocker.md`).
2. **Hits shock mercs.** A merc hit by the shot gets the sticky shock (`sticky-shock-effect.md`): no health damage,
   the shocked state and its effects.
3. **Toggleable laser.** With the gun drawn, the laser key (L, `SwitchLaser`) turns the aiming laser on and off.
4. **Two draw modes on the fire-mode key (E):**
   - **Tap:** quick draw with the laser on. A spy drawn this way shows up on the merc's motion tracking.
   - **Hold (0.5 s):** slower draw with no laser. A spy drawn this way does not show up on motion tracking.

Open question for item 4: the motion tracking actor list (`SMotionTrackingVision.GetActors`) is replaced by
Reloaded.Core.dll in this build (`motion-tracking.md`), so the draw-mode rule may come from the Reloaded patch. Decode
that native (or capture it in the sandbox) to find the exact condition: the WeaponLoaded value, the laser state, or a
movement speed threshold.

## What the original does (checked 2026-10-25)

| Note | Original | Source |
|---|---|---|
| 1. Impact effect | Every shot leaves SImpactEffectShocker (SImpactEffectShockerMetal on metal and water) at the hit, on world, devices and pawns alike. Matches. | spy-shocker.md rule 11 |
| 2. Hits shock mercs | No health damage; freeze 3.5 s, electric 4.5 s, re-shock after 12.5 s, the electric clip, seven spark emitters and the Stickess skin on the shocked pawn. Matches. | sticky-shock-effect.md, R4-combat.md |
| 3. Laser on L | `SwitchLaser` toggles `bLaserOn` in the armed walk; the dot is drawn for everyone, the beam only for the spy himself. Matches. | spy-shocker.md rules 6, 14 |
| 4. Tap draw | Starts at the release, laser on when the armed walk starts. Matches. | spy-shocker.md rule 15 |
| 4. Hold draw | Starts 0.5 s after the press, no laser. The draw itself is not slower: the same 0.3 s transition and 0.267 s pistol clip as a tap; only the 0.5 s wait differs. | spy-shocker.md rule 15 |
| 4. Motion tracking | **Differs from the note.** The merc's tracker locks moving pawns (150 uu/s or more) and projectiles only; it reads nothing the draw mode changes, and the capture shows the same spy velocity after either draw. A spy walking with the gun out is locked after either draw (about 150.1 uu/s); a still spy after neither. What a merc can see after a tap draw and not after a hold draw is the spy's red laser dot. | spy-shocker.md rule 16, motion-tracking.md |
