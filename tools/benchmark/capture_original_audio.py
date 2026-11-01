"""Read native UNIAudio source routing during an already-owned sandbox session.

This helper never launches a game, changes volume, writes process memory or takes
focus. Pass game_tools.Session.attach()'s Game while the coordinator drives the
local spy with normal run/roll input. Source layout is decoded from retail
UUNIAudioSubsystem::PlaySound (0x10b03d50), checked against the editor (0x10f6c340).
"""
from __future__ import annotations

import json
import math
import struct
import time
from pathlib import Path


def capture(game, output, seconds=4.0, interval=0.005):
    """Save bounded observations; unchanged slots are omitted except every 50 ms."""
    pc = game.local_pc()
    pawn = game.local_pawn(pc)
    objects = game.r.objects
    pawn_index = objects.index(pawn) if pawn in objects else None
    systems = []
    for obj in objects:
        if not obj or game.r.class_name(obj) != 'UNIAudioSubsystem':
            continue
        viewport = game.p.u32(obj + 0x2c)
        if viewport and game.p.u32(viewport + 0x30) == pc:
            systems.append(obj)
    if len(systems) != 1:
        raise RuntimeError(f'expected one audio subsystem for viewport controller; found {len(systems)}')
    audio = systems[0]
    volumes = {key: game.get(audio, key) for key in ('AmbientVolume', 'SoundVolume', 'MusicVolume', 'VoiceVolume')}
    if any(value != 0 for value in volumes.values()):
        raise RuntimeError('The owning sandbox session must mute the game before an unattended capture.')
    # Retail runtime: PlaySound 0x10b04136/0x10b04145 uses 0x10c/0x108.
    # The editor's corresponding fields are eight bytes later and identify a wave registry here.
    count = game.p.u32(audio + 0x10c)
    table = game.p.u32(audio + 0x108)
    if not table or not count or count > 128:
        raise RuntimeError(f'unexpected native source table: {table!r}, {count!r}')

    def vector(address):
        return list(struct.unpack('<3f', game.p.read(address, 12)))

    seen = {}
    object_set = set(objects)
    rows = []
    started = time.perf_counter()
    deadline = started + min(max(seconds, 0.1), 60.0)
    while time.perf_counter() < deadline:
        elapsed = time.perf_counter() - started
        listener = vector(audio + 0x44)
        data = game.p.read(table, count * 0x68)
        for slot in range(count):
            record = data[slot * 0x68:(slot + 1) * 0x68]
            sound, = struct.unpack_from('<I', record)
            if not sound:
                continue
            actor, bone = struct.unpack_from('<Ii', record, 0x10)
            location = list(struct.unpack_from('<3f', record, 0x18))
            radius, = struct.unpack_from('<f', record, 0x2c)
            volume, = struct.unpack_from('<f', record, 0x44)
            flags, sound_id = struct.unpack_from('<II', record, 0x54)
            if not sound_id:
                continue
            if sound not in object_set or (actor and actor not in object_set) or bone < -1 or bone > 1024:
                raise RuntimeError(f'invalid voice layout at slot {slot}: sound={sound:#x}, actor={actor:#x}, bone={bone}')
            if not all(math.isfinite(v) for v in location + listener + [radius, volume]) or radius <= 0 or volume < 0 or volume > 1.001:
                raise RuntimeError(f'invalid source values at slot {slot}')
            key = (sound, actor, bone, sound_id)
            prior = seen.get(slot)
            if prior and prior[0] == key and elapsed - prior[1] < 0.05:
                continue
            seen[slot] = (key, elapsed)
            rows.append(dict(t=elapsed, slot=slot, sound=game.r.obj_name(sound),
                             sourceActor=hex(actor), sourceIsController=actor == pc,
                             sourceIsPawn=actor == pawn, bone=bone, flags=flags,
                             soundId=sound_id, callingActorIndex=sound_id >> 4,
                             pawnIndex=pawn_index, location=location, listener=listener,
                             distance=math.dist(location, listener), radius=radius,
                             volume=volume, pawnLocation=vector(pawn + game.off_loc)))
        time.sleep(max(interval, 0.001))
    result = dict(format='svm.original-audio/1', muted=True, volumes=volumes,
                  note='Positions and native routing only; muted source volumes are not loudness evidence.',
                  seconds=time.perf_counter() - started, audio=hex(audio), controller=hex(pc),
                  pawn=hex(pawn), pawnIndex=pawn_index, observations=rows)
    Path(output).parent.mkdir(parents=True, exist_ok=True)
    own = [r for r in rows if (r['callingActorIndex'] == pawn_index or r['sourceIsController']) and
           ('Roll' in r['sound'] or 'Foot' in r['sound'] or 'matos' in r['sound'] or r['sound'].startswith('pas'))]
    result['ownMovementSamples'] = len(own)
    Path(output).write_text(json.dumps(result, indent=2), encoding='utf-8')
    if len(own) < 3:
        raise RuntimeError(f'no local pawn footstep/roll voices captured; inspect input/stance before using {output}')
    return result
