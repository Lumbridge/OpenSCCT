"""Authored reload passes on compatible weapon skeletons.

Contact-driven weapon, hand and mechanism timelines with the existing event clock.
No gameplay timing is changed. Generated retail assets stay local.
"""
import math
import struct
import zlib
import numpy as np

from .sidearm_models import read, write
from .pistol_arms import turn


PRIMARY = {'1DEF_01': 'rifle', '1DEF_Uzi': 'uzi', '1DEF_shotgun': 'shotgun'}


def smooth(keys, values, t):
    i = int(np.clip(np.searchsorted(keys, t, side='right') - 1, 0, len(keys)-2))
    u = float(np.clip((t-keys[i])/(keys[i+1]-keys[i]), 0, 1))
    return values[i] + (values[i+1]-values[i]) * u*u*(3-2*u)


def solve_arm(old, chain, target, orientation, pole_offset=None):
    """Two-bone solve with the source elbow as pole; no arm stretch."""
    upper, lower, hand = chain
    shoulder, elbow, wrist = [old[n][:3, 3] for n in chain]
    a, b = np.linalg.norm(elbow-shoulder), np.linalg.norm(wrist-elbow)
    direction = target-shoulder
    length = np.clip(np.linalg.norm(direction), abs(a-b)+1e-6, a+b-1e-6)
    direction /= max(1e-9, np.linalg.norm(direction))
    target = shoulder + direction*length
    pole = elbow-shoulder
    if pole_offset is not None:
        pole = pole + pole_offset
    pole -= direction*(pole@direction)
    if np.linalg.norm(pole) < 1e-7:
        pole = np.cross(direction, [0, 0, 1])
    pole /= max(1e-9, np.linalg.norm(pole))
    along = (a*a-b*b+length*length)/(2*length)
    bent = shoulder + direction*along + pole*math.sqrt(max(0, a*a-along*along))
    result = {n: old[n].copy() for n in chain}
    result[upper][:3, :3] = turn(elbow-shoulder, bent-shoulder) @ old[upper][:3, :3]
    result[lower][:3, :3] = turn(wrist-elbow, target-bent) @ old[lower][:3, :3]
    result[lower][:3, 3] = bent
    result[hand][:3, :3] = orientation
    result[hand][:3, 3] = target
    return result


def author(doc, data, profile):
    from .reload_choreography import author as choreograph
    return choreograph(doc, data, profile)


def install_primaries(chars_dir):
    # Flat shell colours are external PNGs because the game's custom material
    # compiler does not use glTF baseColorFactor. No new texture dependency.
    textures=chars_dir/'SWeaponFirst/textures'
    textures.mkdir(parents=True,exist_ok=True)
    def chunk(kind,data):
        return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
    for name,color in [('brass',(140,82,20,255)),('red',(117,6,5,255))]:
        raw=b''.join(b'\0'+bytes(color)*4 for _ in range(4))
        png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',4,4,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b'')
        (textures/('OpenScct_shell_'+name+'.png')).write_bytes(png)
    result = {}
    for model, profile in PRIMARY.items():
        path = chars_dir/'SWeaponFirst'/(model+'.glb')
        doc, data = read(path)
        result[model] = author(doc, data, profile)
        write(path, doc, data)
    return result
