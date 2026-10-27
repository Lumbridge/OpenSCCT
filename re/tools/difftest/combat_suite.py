"""Writes the combat scenario set (tests/scenarios/combat/*.combat.json and the single-player
merc-fall-ledge.scenario.json) from the definitions below, so the files stay consistent and can be regenerated.

    python -I -B re/tools/difftest/combat_suite.py [--check]

Spots: the SteeE east yard lane of the R4 locomotion recordings (x -492, floor z -1143.5, 1792 uu clear along +Y
from y -2032; re/tools/difftest/spots.py) for every two-player case, and the east yard roof edge of
movement.spy-fall-ledge for the merc's fall. The local player stands at the lane's south end facing +Y.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(REPO, 'tests', 'scenarios', 'combat')

LANE_X, LANE_Y0, FLOOR = -492.0, -2032.0, -1143.5
NORTH, SOUTH, EAST, WEST = 16384, 49152, 0, 32768        # yaw facing +Y, -Y, +X, -X
FAR_POINT = [LANE_X, 2000.0, -1000.0]                    # down the lane, past its clear end
SPOT_NOTE = ('SteeE east yard lane (x -492, floor z -1143.5, clear for 1792 uu along +Y from y -2032; found with '
             're/tools/difftest/spots.py for the R4 locomotion recordings)')

RIFLE_UNITS = ['uc:SBase.SPlayerController:Fire', 'uc:SBase.SWeaponDefense:FireWeapon',
               'uc:SBase.SWeaponDefense:ServerFireWeapon', 'uc:SBase.SWeaponDefense:TraceFire',
               'nat:Engine.Actor.TraceBone', 'nat:SBase.SWeaponDefense.GetAccuracy',
               'uc:SBase.SPlayerController:StandardReactionsToDamage', 'uc:SBase.SPlayerController:PawnTakeDamage']
ZONES = [('head', 'head', None), ('torso', 'spine2', 'pelvis'), ('upper-arm-l', 'l_upperarm', 'l_forearm'),
         ('forearm-l', 'l_forearm', 'l_hand'), ('hand-l', 'l_hand', None), ('thigh-l', 'l_thigh', 'l_calf'),
         ('calf-l', 'l_calf', 'l_foot'), ('foot-l', 'l_foot', None), ('upper-arm-r', 'r_upperarm', 'r_forearm'),
         ('thigh-r', 'r_thigh', 'r_calf')]


def lane(d):
    return [LANE_X, LANE_Y0 + d, FLOOR]


def base(ident, title, local, target, duration, events, measure, covers, setup=None, settle=30, repeats=2,
         notes=None, order=None):
    doc = {'format': 'svm.combat/1', 'id': 'combat.' + ident, 'title': title, 'map': 'SteeE', 'mode': 'playlevel',
           'local': local, 'target': target}
    if order is not None:
        doc['order'] = order
    if setup:
        doc['setup'] = setup
    doc.update({'settleFrames': settle, 'durationFrames': duration, 'events': events, 'repeats': repeats,
                'measure': measure, 'covers': covers})
    doc['spot'] = SPOT_NOTE
    if notes:
        doc['notes'] = notes
    return doc


def merc(at=0.0, yaw=NORTH, crouched=False):
    return {'team': 'merc', 'floor': lane(at), 'yaw': yaw, 'crouched': crouched}


def spy(at=0.0, yaw=NORTH, crouched=False):
    return {'team': 'spy', 'floor': lane(at), 'yaw': yaw, 'crouched': crouched}


def press(frame, do='fire'):
    return [{'frame': frame, 'do': do}, {'frame': frame + 1, 'do': do + 'Up'}]


def buttons(frame, name, length=4):
    return [{'frame': frame, 'do': 'buttons', 'buttons': {name: 1}},
            {'frame': frame + length, 'do': 'buttons', 'buttons': {name: 0}}]


# -- rifle ------------------------------------------------------------------------------------------------------------

def rifle_zones(name, d):
    events = [{'frame': 0, 'do': 'rof', 'mode': 'single', 'note': 'single shots: every shot spawns a tracer (impact)'}]
    k = 4
    for zone, bone, to in ZONES:
        events.append({'frame': k, 'do': 'heal', 'hp': 100, 'note': zone})
        aim = {'frame': k + 2, 'do': 'aim', 'bone': bone}
        if to:
            aim['to'] = to
        events.append(aim)
        events += press(k + 4)
        k += 30
    return base('rifle-zones-' + name,
                'Merc rifle single shots at a standing spy %d uu away, one per hit zone (head, torso, arms, legs)' % d,
                merc(), spy(d, SOUTH), k + 30, events, {'hits': {}, 'shots': {}},
                RIFLE_UNITS + ['def:SBase.SWeaponDefense', 'def:SBase.SPawnAttaque'],
                notes='The spy faces the merc; each shot aims at a bone or the middle of a limb capsule\'s two bones '
                      '(spec/native/hit-zones.md pills) and the spy is healed to 100 before it. Expected damage: '
                      'int(int(17 c(d)) table[pill] / 100), hit-zones-and-weapons.md rules 7 to 9.')


def rifle_auto():
    events = [{'frame': 0, 'do': 'rof', 'mode': 'auto'}, {'frame': 2, 'do': 'aimPoint', 'point': FAR_POINT},
              {'frame': 6, 'do': 'fire', 'note': 'held for 25 rounds, the auto-reload and 15 more'},
              {'frame': 606, 'do': 'fireUp'}]
    return base('rifle-auto-standing', 'Merc rifle on full auto down the lane: a whole clip held, the automatic '
                'reload with fire held, then more rounds; BulletInARow decay after release',
                merc(), None, 740, events, {'shots': {}, 'reload': {}},
                RIFLE_UNITS + ['uc:SBase.SWeaponDefense:s_Firing.Timer', 'uc:SBase.SWeaponDefense:ApplyWeaponJump',
                               'uc:SBase.SWeaponDefense:s_Selected.Tick', 'uc:SBase.SWeaponDefense:s_Reloading.AnimEnd',
                               'nat:SBase.SWeaponDefense.NativeTickSWeaponDefense'])


def rifle_burst():
    events = [{'frame': 0, 'do': 'rof', 'mode': 'burst'}, {'frame': 2, 'do': 'aimPoint', 'point': FAR_POINT}]
    for k in (10, 70, 130, 190):
        events += press(k)
    return base('rifle-burst', 'Merc rifle in burst mode down the lane: four presses of three rounds',
                merc(), None, 260, events, {'shots': {}},
                RIFLE_UNITS + ['uc:SBase.SWeaponDefense:GetNbBulletsFromROF', 'uc:SBase.SWeaponDefense:s_Firing.Timer'])


def rifle_single():
    events = [{'frame': 0, 'do': 'rof', 'mode': 'single'}, {'frame': 2, 'do': 'aimPoint', 'point': FAR_POINT}]
    for k in range(10, 190, 15):
        events += press(k)
    return base('rifle-single-spread', 'Merc rifle single shots every 0.25 s down the lane (each spawns a tracer: '
                'the spread and the aim kick per shot)', merc(), None, 240, events, {'shots': {}},
                RIFLE_UNITS + ['uc:SBase.SWeaponDefense:ApplyWeaponJump'])


def rifle_walking(crouched):
    events = [{'frame': 0, 'do': 'rof', 'mode': 'auto'}, {'frame': 2, 'do': 'aimPoint', 'point': FAR_POINT},
              {'frame': 3, 'do': 'buttons', 'buttons': {'BT_Up': 1}},
              {'frame': 40, 'do': 'fire'}, {'frame': 160, 'do': 'fireUp'},
              {'frame': 170, 'do': 'buttons', 'buttons': {'BT_Up': 0}}]
    name = 'rifle-auto-crouch-walking' if crouched else 'rifle-auto-walking'
    return base(name, 'Merc rifle on full auto while %s forward (the speed term of the spread)'
                % ('crouch-walking' if crouched else 'walking'), merc(crouched=crouched), None, 220, events,
                {'shots': {}}, RIFLE_UNITS)


def rifle_reload():
    events = [{'frame': 0, 'do': 'rof', 'mode': 'single'}, {'frame': 2, 'do': 'aimPoint', 'point': FAR_POINT}]
    for k in (10, 25, 40, 55, 70):
        events += press(k)
    events += buttons(100, 'bReload')
    events += buttons(260, 'bReload')
    return base('rifle-reload', 'Merc rifle: five shots, the reload button (s_Reloading, clip refill at the end of '
                'the clip), then the button again on a full clip', merc(), None, 330, events,
                {'shots': {}, 'reload': {}},
                ['uc:SBase.SWeaponDefense:Reload', 'uc:SBase.SWeaponDefense:s_Reloading.BeginState',
                 'uc:SBase.SWeaponDefense:s_Reloading.AnimEnd', 'uc:SBase.SWeaponDefense:s_Reloading.EndState'])


def death_directions():
    events = []
    # Yaw WEST puts the merc (south of the spy) on the spy's right (its Y axis points south), yaw EAST on its left.
    for k, (side, yaw) in enumerate((('front', SOUTH), ('back', NORTH), ('right', WEST), ('left', EAST))):
        b = 10 + 220 * k
        events += [{'frame': b, 'do': 'spawnTarget', 'team': 'spy', 'floor': lane(300), 'yaw': yaw,
                    'note': "the merc is on the spy's %s; the rig moves it to its spot 0.5 s and 1 s later" % side},
                   {'frame': b + 110, 'do': 'crouchTarget', 'crouched': False},
                   {'frame': b + 170, 'do': 'heal', 'hp': 10},
                   {'frame': b + 172, 'do': 'aim', 'bone': 'spine2', 'to': 'pelvis'}]
        events += press(b + 174)
    return base('death-directions', 'Lethal rifle shots into a standing spy from its front, back, left and right: '
                'the death action and clip ChooseDeathAnimation picks', merc(), None, 900, events,
                {'hits': {}, 'states': {}, 'deaths': {}},
                ['nat:SBase.SBasePawn.ChooseDeathAnimation', 'uc:SBase.SPlayerController:PlayAnimDeath'] + RIFLE_UNITS)


# -- shocks -----------------------------------------------------------------------------------------------------------

SHOCK_UNITS = ['uc:SBase.SPlayerController:Checkshock', 'uc:SBase.SPlayerController:ClientStickyShock',
               'uc:SBase.SPlayerController:PlayerTick']


def shocker_on_merc(name, bone, shots):
    events = []
    k = 4
    for n in range(shots):
        events.append({'frame': k, 'do': 'aim', 'bone': bone})
        events += press(k + 2)
        k += 30
    return base(name, 'Spy shocker at a merc %d uu away (%s): %d shots 0.5 s apart, then the energy refill' % (
        500, bone, shots), spy(), merc(500, SOUTH), 900, events,
        {'energy': {}, 'shock': {'victim': 'T'}, 'hits': {}},
        ['uc:SBase.SStickyShocker:Fire', 'uc:SBase.SWeaponAttaque:ServerFireWeaponWithLocation',
         'uc:SBase.SWeaponAttaque:TraceFireWithPos', 'uc:SBase.SPawnAttaque:AuthorizeEnergy',
         'nat:SBase.SPawnAttaque.ComputeEnergyLevel'] + SHOCK_UNITS,
        setup=[{'op': 'draw'}],
        notes='The merc target has no PlayerTick (no viewport), so its freeze never ends; its deadlines '
              '(iTimeToStopStickyShock, iTimeElecStickyShock), TimeShock and RealTimeInvulSticky are what the shock '
              'wrote. The freeze as a player lives it is recorded with the local player as the victim.')


def shock_victim(name, team, bone, title):
    events = [{'frame': 30, 'do': 'targetFire', 'bone': bone, 'note': 'the target spy\'s shocker, server side'}]
    if team == 'merc':
        events += [{'frame': 100, 'do': 'aimPoint', 'point': FAR_POINT}]
        events += press(120)
        events += press(270)
        events += press(400)
        events += [{'frame': 500, 'do': 'targetFire', 'bone': bone, 'note': 'inside the immunity: refused'},
                   {'frame': 860, 'do': 'targetFire', 'bone': bone, 'note': 'after the immunity'}]
        duration = 1150
    else:
        events += [{'frame': 500, 'do': 'targetFire', 'bone': bone, 'note': 'inside the immunity: refused'},
                   {'frame': 760, 'do': 'targetFire', 'bone': bone, 'note': 'after the immunity'}]
        duration = 1000
    local = merc() if team == 'merc' else spy()
    return base(name, title, local, spy(200, SOUTH), duration, events, {'shock': {'victim': 'L'}, 'states': {}},
                SHOCK_UNITS + ['uc:SBase.SWeaponAttaque:TraceFireWithPos'], order=90,
                notes='The target spy fires its shocker through ServerFireWeaponWithLocation from its eye at the local '
                      'player\'s bone (what its client would ask the server); the local player runs the freeze, the '
                      'electric time and the immunity as a player does.')


# -- grenades ---------------------------------------------------------------------------------------------------------

BLAST_UNITS = ['nat:Engine.Projectile.Explode', 'def:SBase.SProjFrag', 'uc:SBase.SProjFrag:PostBeginPlay',
               'uc:SBase.SPlayerController:StandardReactionsToDamage', 'uc:SBase.SPlayerController:ExplosionHit',
               'uc:SBase.SPlayerController:ClientDamage']


def frag_on_spy(d, hp):
    events = [{'frame': 0, 'do': 'heal', 'hp': hp},
              {'frame': 2, 'do': 'grenade', 'kind': 'frag', 'fromTarget': [0.0, float(d), 8.0], 'by': 'local',
               'note': 'dropped %d uu north of the spy\'s feet, the grenade\'s own 2 s fuse' % d}]
    name = 'frag-spy-%d%s' % (d, '-lethal' if hp <= 100 else '')
    return base(name, 'Merc frag grenade %d uu from a standing spy (health %d)' % (d, hp), merc(), spy(500, SOUTH),
                360, events, {'blast': {}, 'states': {}}, BLAST_UNITS, settle=200,
                notes='Settle 200 frames so the spy is past its 3 s spawn protection. Expected (natives2-damage.md '
                      'rule 4, damage-pipeline.md rule 21): inside 350 uu SDamage_Explosion, 200 taken whatever the '
                      'distance; 350 to 500 uu SDTBase int((1 - d / 500) 334); beyond 500 nothing.')


def frag_on_merc(d, by):
    events = [{'frame': 0, 'do': 'healLocal', 'hp': 1000},
              {'frame': 2, 'do': 'grenade', 'kind': 'frag', 'fromLocal': [0.0, float(d), 8.0], 'by': by}]
    name = 'frag-merc-%d' % d if by == 'target' else 'frag-self-%d' % d
    title = ('Frag grenade (the spy target\'s) %d uu in front of the merc, health 1000: damage, blast and recovery' % d
             if by == 'target' else 'The merc\'s own frag grenade %d uu in front of him, health 1000' % d)
    return base(name, title, merc(), spy(1500, SOUTH), 420, events, {'blast': {}, 'states': {}}, BLAST_UNITS,
                order=90,
                notes='Health 1000 so the explosion (200) does not kill and the blast reaction can be recorded.')


def smoke_on_merc():
    events = [{'frame': 2, 'do': 'grenade', 'kind': 'smoke', 'fromLocal': [0.0, 80.0, 8.0], 'by': 'target'}]
    return base('smoke-merc', 'Spy smoke grenade at a merc\'s feet: cough, slow-down and the knock-out after 6 s',
                merc(), spy(1500, SOUTH), 1100, events, {'states': {}},
                ['uc:SBase.SPlayerController:GoInSmoke', 'uc:SBase.SSmokeEmitter:DetectedSPlayerController',
                 'def:SBase.SSmokeEmitter', 'def:SBase.SProjSmoke', 'uc:SBase.SPlayerController:CountGasKO'],
                order=95,
                notes='The cloud test runs for the local player only (smoke-grenade.md rule 4), so the merc is the '
                      'local player.')


# -- melee and grab ---------------------------------------------------------------------------------------------------

GRAB_UNITS = ['uc:SBase.SPlayerController:AskToGrabPawn', 'uc:SBase.SPlayerController:CanGrabPawn',
              'uc:SBase.SPlayerController:AskServerToGrabPawn']


def grab(name, end, duration, title):
    events = buttons(10, 'bUsing')
    if end == 'neck':
        events += buttons(160, 'bUsing')
    elif end == 'ko':
        events += press(160)
    return base(name, title, spy(410), merc(500, NORTH), duration, events, {'states': {}},
                GRAB_UNITS + ['uc:SBase.SPlayerController:GrabServerReleaseRequest'],
                notes='The spy stands 90 uu behind the merc, both facing +Y; Use starts the grab.')


def spy_elbow():
    events = buttons(10, 'bUsing')
    return base('spy-elbow', 'Spy elbow strike on a merc facing him 110 uu away', spy(390), merc(500, SOUTH), 240,
                events, {'states': {}},
                ['uc:SBase.SPlayerController:FindPawnToElbowShotAndGoForIt',
                 'uc:SBase.SPlayerController:AskServerToElbowAttack', 'uc:SBase.SPlayerController:Handle_ElbowDmg'])


def merc_berserk():
    events = buttons(10, 'bUsing')
    return base('merc-berserk', 'Merc berserk spin (Use standing still) next to a spy 100 uu in front', merc(),
                spy(100, SOUTH), 420, events, {'states': {}},
                ['uc:SBase.SPlayerController:s_Berserk360.BeginState', 'uc:SBase.SPlayerController:Handle_ChargedDmg'])


def merc_charge():
    events = [{'frame': 2, 'do': 'buttons', 'buttons': {'BT_Up': 1}}]
    events += buttons(40, 'bUsing')
    events += [{'frame': 110, 'do': 'buttons', 'buttons': {'BT_Up': 0}}]
    return base('merc-charge', 'Merc charge (Use while walking forward) into a spy 450 uu ahead', merc(),
                spy(450, SOUTH), 420, events, {'states': {}},
                ['uc:SBase.SPlayerController:s_PlayerCharging.BeginState',
                 'uc:SBase.SPlayerController:FindTargetToCharge',
                 'uc:SBase.SPlayerController:Handle_ChargedDmg'])


def merc_elbow():
    events = buttons(10, 'bUsing')
    events += [{'frame': 120, 'do': 'place', 'floor': lane(110), 'yaw': SOUTH,
                'note': 'the spy is moved in front of the merc while his berserk cools down'}]
    events += buttons(170, 'bUsing')
    return base('merc-elbow', 'Merc elbow strike: a berserk spin that hits nobody, then Use within its 3 s cooldown '
                'with a spy 110 uu in front', merc(), spy(400, SOUTH), 420, events, {'states': {}},
                ['uc:SBase.SPlayerController:FindPawnToElbowShotAndGoForIt',
                 'uc:SBase.SPlayerController:AskServerToElbowAttack', 'uc:SBase.SPlayerController:Handle_ElbowDmg'],
                notes="melee-elbow-charge-berserk.md rule 1: with the default options a merc's Use is a charge "
                      '(moving) or a berserk (still); it is an elbow only while those cool down.')


# -- spawn protection -------------------------------------------------------------------------------------------------

def spawn_protection():
    events = [{'frame': 0, 'do': 'spawnTarget', 'team': 'spy', 'floor': lane(500), 'yaw': SOUTH,
               'note': 'a fresh spy (RestartPlayer: fSpawnProtectionEnd = now + 3), moved into the lane by the rig '
                       '0.5 s and 1 s later'},
              {'frame': 34, 'do': 'grenade', 'kind': 'frag', 'fromTarget': [0.0, 150.0, 8.0], 'by': 'local',
               'note': 'goes off about 2.6 s after the spawn: protected'},
              {'frame': 100, 'do': 'aim', 'bone': 'spine2', 'to': 'pelvis'}]
    events += press(104)
    events += [{'frame': 200, 'do': 'heal', 'hp': 1000},
               {'frame': 240, 'do': 'grenade', 'kind': 'frag', 'fromTarget': [0.0, 150.0, 8.0], 'by': 'local',
                'note': 'goes off about 6 s after the spawn'}]
    return base('spawn-protection', 'Spawn protection of a fresh spy: a frag going off inside the 3 s window, a '
                'bullet at 1.7 s, a frag going off after the window', merc(), None, 460, events,
                {'hits': {}, 'blast': {}, 'states': {}, 'deaths': {}},
                ['uc:SBase.SPlayerController:EnterStartState', 'uc:SBase.SGameInfo:RestartPlayer',
                 'uc:SBase.SPlayerController:StandardReactionsToDamage'])


def merc_fall():
    """A plain svm.scenario/1 (record.py, replayable by the sim runner): the merc walks off the R4 roof edge."""
    return {
        'format': 'svm.scenario/1', 'id': 'combat.merc-fall-ledge',
        'title': 'Merc walks off a 790 uu roof edge, falls, lands and takes fall damage',
        'map': 'SteeE', 'mode': 'playlevel', 'players': [{'team': 'merc'}],
        'spawn': {'floor': [-684.0, 464.0, -351.0], 'yaw': 12288, 'clearance': 2.0,
                  'spot': 'east yard roof, edge 288 uu ahead at yaw 12288 (67.5 degrees); the spot of '
                          'movement.spy-fall-ledge',
                  'notes': 'The yard floor z -1141.0 lies 790 uu below the roof z -351.0, but at the merc\'s '
                           'walking speed (346.5 uu/s, no slow mode) he flies further than the spy of '
                           'movement.spy-fall-ledge and lands on a step 18 uu above the yard: the recorded drop '
                           '(fZMaxFalling - Location.Z at landing) is 772.6 uu (774.9 when the last air frame is '
                           'already below the step top). Merc fall damage int((h - 450) / 550 * 100).'},
        'setup': [{'op': 'teleport'}, {'op': 'stance', 'crouched': False},
                  {'op': 'write', 'target': 'pawn', 'field': 'Health', 'value': 100},
                  {'op': 'view', 'pitch': 0, 'yaw': 12288}, {'op': 'waitStill', 'maxFrames': 180}],
        'settleFrames': 30, 'durationFrames': 300,
        'inputs': [{'frame': 0, 'buttons': {'BT_Up': 1}, 'note': 'walk to the edge'},
                   {'frame': 100, 'buttons': {'BT_Up': 0}, 'note': 'let go while falling, before landing'}],
        'repeats': 3, 'profile': 'locomotion',
        'measure': {'fall': {'expectDrop': 772.6}, 'fallDamage': {'expect': 58}},
        'covers': ['uc:SBase.SPlayerController:MyFalling', 'uc:SBase.SPlayerController:PlayerFalling.BeginState',
                   'uc:SBase.SPlayerController:MyNotifyLanded', 'uc:SBase.SPlayerController:PlayerLanding.BeginState',
                   'uc:SBase.SBasePawn:TakeFallingDamage', 'def:SBase.SPAWNDEFENSE'],
    }


def suite():
    docs = [rifle_zones('near', 250), rifle_zones('mid', 900), rifle_zones('far', 1500), rifle_auto(), rifle_burst(),
            rifle_single(), rifle_walking(False), rifle_walking(True), rifle_reload(), death_directions(),
            shocker_on_merc('shocker-merc', 'spine2', 8), shocker_on_merc('shocker-merc-head', 'head', 1),
            shock_victim('shock-merc-freeze', 'merc', 'spine2', 'A spy\'s shocker hits the local merc: freeze, '
                         'electric time, refused fire, the immunity window'),
            shock_victim('shock-merc-head', 'merc', 'head', 'A spy\'s shocker hits the local merc in the head'),
            shock_victim('shock-spy-freeze', 'spy', 'spine2', 'A spy\'s shocker hits the local spy: freeze and '
                         'electric time (spy values), the immunity window'),
            frag_on_spy(100, 1000), frag_on_spy(300, 1000), frag_on_spy(450, 1000), frag_on_spy(600, 1000),
            frag_on_spy(200, 100), frag_on_merc(200, 'target'), frag_on_merc(420, 'target'), frag_on_merc(200, 'local'),
            smoke_on_merc(),
            grab('grab-neckbreak', 'neck', 420, 'Spy grabs a merc from behind, then Use: the neck break'),
            grab('grab-ko', 'ko', 1000, 'Spy grabs a merc from behind, then Fire: the knock-out (51) and the merc '
                 'lying down'),
            grab('grab-choke', None, 1260, 'Spy grabs a merc from behind and holds him: the choke after 15 s'),
            spy_elbow(), merc_berserk(), merc_charge(), merc_elbow(), spawn_protection()]
    return docs


def write(check_only=False):
    sys.path.insert(0, HERE)
    import combat_scenario as cs
    os.makedirs(OUT, exist_ok=True)
    changed = []
    docs = suite()
    for doc in docs:
        problems = cs.validate(doc)
        if problems:
            raise SystemExit('%s: %s' % (doc['id'], problems))
    docs.append(merc_fall())
    for doc in docs:
        stem = doc['id'].split('.', 1)[1]
        ext = '.combat.json' if doc['format'] == 'svm.combat/1' else '.scenario.json'
        path = os.path.join(OUT, stem + ext)
        text = json.dumps(doc, indent=2) + '\n'
        old = open(path, encoding='utf-8').read() if os.path.isfile(path) else None
        if old != text:
            changed.append(path)
            if not check_only:
                with open(path, 'w', encoding='utf-8', newline='\n') as f:
                    f.write(text)
    return changed


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    ap.add_argument('--check', action='store_true', help='fail when a file differs from the definitions')
    args = ap.parse_args()
    changed = write(args.check)
    for p in changed:
        print(('differs ' if args.check else 'wrote ') + os.path.relpath(p, REPO))
    if args.check and changed:
        sys.exit(1)


if __name__ == '__main__':
    main()
