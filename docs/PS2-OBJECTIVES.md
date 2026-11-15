# Original PS2 objectives

Recovered PS2 maps opt into their original objective rules with `bPS2Objective` on their objective actors. Existing maps retain their current rules. Map codes end in `P` to distinguish the original layouts from PC remakes.

The map's own class defaults and actor properties bind the recovered assets. The published shared classes and audio packs do not change. Both peers need the runtime that registers `Ps2Modem` and `Ps2ModemEffect`.

## Vials and consoles

`SFlag` retains its original green vial mesh. Its authored computer reference selects the animated console; the vial sits 25 units above it and the pickup interaction sits at the source forward offset. Carrying uses the source thigh attachment offset and rotation. Alarm locking lowers the vial, and unlocking raises it at the source speed. Extraction delivery uses the PS2 drop volume's touch behavior.

`SComputerObjectiveTrigger` uses the original four ND133 animation clips, five state materials, and nine-way animation dispatcher. The original material graph includes the liquid texture panner and its separate emissive mask. Replicated state drives the client view; gameplay timers run on the server independently of rendering.

The runtime network layout carries an explicit animation command and serial. It handles a paused neutralization capture, a reversal or replay with the same visual state, and a joining client's current source frame. Neutralization restores its saved progress only after the original reverse-alarm and accelerated hack timeline; a renderer callback cannot complete that gameplay delay.

The retained `nd133_1mesh` asset contains the original PS2-authored skeleton and animation tracks. The recovery verifies the bind skeleton and animation arrays against the disc. The cooked PS2 GPU mesh does not retain the same editable point representation, so rendered vertex equality for this retained skeletal mesh is not asserted.

## Sabotage modem

On PS2 maps, the spy selects the Modem gadget and places it against nearby world geometry inside an active bomb target. Placement checks the source range, target cylinder, lock state, console visibility, refire delay, and existing modem. One charge is available; refilling restores that charge.

The placed modem uses the recovered PS2 mesh, has one hit point, and starts the target's capture timer. Mercenaries can shoot it or remove it. Interrupted capture retains elapsed whole seconds. Destruction uses the recovered three-emitter smoke/flash preset and map-local textures.

The active beep uses the retail `Armes_impacts.Armes.ProxMine_ACTI_1` asset, identified through the same source mine class/default reference. This is a documented compatibility audio alias; exact PS2 PCM identity is not asserted for that beep.

The hidden placement target remains relevant to clients and replicates its source cylinder separately from collision, so local gadget validation uses the same bounds as the authority. The gadget carries the map profile with its initial inventory properties, even if its target channels arrive later. Placing uses the modem's surface check instead of the PC weapon's muzzle-clearance check.

## Verification

Objective tests cover the opt-in rules and existing PC behavior. Integration tests use the actual host/client actor factory, replication, and destruction paths. The `ps2-objectives` game smoke loads `DTK02P`, checks the recovered console clips and liquid shader, changes the server's alarm state, and renders the replicated modem and destruction effect. It saves screenshots when `SVM_PLAY_SHOTS` is set.

Network tests run profiles 100 and 300. They prepare a client map by deleting its replaceable objectives, respawn the server's instances through their PC class factories, and verify source meshes, console associations and materials. A dynamic spawn header overrides the factory's persistent-actor defaults: the authored PS2 instances can have `bNoDelete=false` while the PC class has `bNoDelete=true`. Remote objective startup waits for the initial profile instead of replacing the source vial with the PC disk. Other tests cycle to the fifth gadget, invoke the owning controller's actual alternate-fire input, and check placement, charge replication, destruction and the three original effect components.

Content validation separately checks the recovered package bodies, source mesh triangles and textures, source UI pixels, native named-world loading, and gameplay. A successful conversion or unit test alone does not establish visual or match parity.

The independently decoded emitter-reference JSON stays in ignored `local/ps2/original-modem-effect.json`, following the repository's source-data rules. `OPENSCCT_PS2_MODEM_REFERENCE` can name another local capture. Its property-parity test explicitly skips when that original reference is absent; the synthetic gameplay and network tests do not require it.
