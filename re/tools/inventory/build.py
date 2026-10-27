"""Coverage inventory: every unit of the original game's script layer, with names, sizes and flags.

Reads the decompiler index (uc-index.json), the native table (natives_all.txt) and a reflection dump
of the running game (game.json), and writes:

  re/inventory/inventory.json   one record per unit (ids, kind, package, class, state, flags, sizes,
                                native index and address, subsystem guess, slice flag)
  re/inventory/README.md        summary tables (counts per package and kind, slice, natives)

Only names, sizes, flags, hashes and addresses are written. No bytecode, no decompiled text and no
replication condition text leave the local folders.

Unit id grammar:
  uc:<Pkg>.<Class>:<Func>            script function of a class
  uc:<Pkg>.<Class>:<State>.<Func>    script function declared in a state
  uc:<Pkg>.<Class>:<State>@code      state code (labels and latent code)
  nat:<Pkg>.<Class>.<Func>           native function (nat:<Pkg>.<Class>.<State>.<Func> if in a state)
  rep:<Pkg>.<Class>.<Member>         one member of a replication block (variable or function)
  def:<Pkg>.<Class>                  class default properties

Usage (from the repository root):
  python -I -B re/tools/inventory/build.py [--uc-index PATH] [--natives PATH] [--reflect PATH]
                                           [--out-dir re/inventory] [--check]

--check rebuilds in memory and exits 1 when the files on disk differ (for CI on a machine with the
local inputs).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
DEFAULT_UC_INDEX = REPO / "local" / "re-out" / "uc" / "uc-index.json"
DEFAULT_REFLECT = REPO / "local" / "reflect" / "game.json"
DEFAULT_NATIVES = Path(
    os.environ.get(
        "SVM_NATIVES_ALL",
        os.path.expanduser(r"~\source\repos\SCCT_Versus_UE5_Bridge\out\_agents\merc\natives_all.txt"),
    )
)
DEFAULT_OUT = REPO / "re" / "inventory"

FORMAT = "svm.inventory/1"

# ---------------------------------------------------------------------------------------------
# Slice: the SteeE Story 2v2 online vertical slice (plan M6).
# "listed": named by the slice plan. "steee": placed in SteeE.sdc (counted from the map exports).
# "runtime": spawned at run time in a slice match. "ancestor" is added automatically for every
# superclass of a slice class.
SLICE_LISTED = {
    "SBase.SBasePawn", "SBase.SPawnAttaque", "SBase.SPAWNDEFENSE", "SBase.SPlayerController",
    "SBase.SWeaponDefense", "SBase.SStickyShocker", "SBase.SGoggle", "SBase.SObjective",
    "SBase.SMission", "SBase.SComputerObjectiveTrigger", "SBase.SFlag", "SBase.SFlagDropZone",
    "SBase.SInterrupteur", "Engine.Mover", "SBase.SVitre", "SBase.SGamePlayObject",
    "SBase.SAnimatedMesh",
    # The plan names SPlayerReplicationInfo and SGameReplicationInfo; the game has no such
    # classes and uses the Engine ones directly.
    "Engine.PlayerReplicationInfo", "Engine.GameReplicationInfo",
}
SLICE_STEEE = {
    "SBase.SDisk", "SBase.SCamNetwork", "SBase.SPresenceTrigger", "SBase.SGETrigger",
    "SBase.SExtincteur", "SBase.SInfirmary", "SBase.SAmmoStock", "SBase.SDamageVolume",
    "SBase.SMagicEvent", "SBase.SShaker", "SBase.STriggerLight", "SBase.STriggerPlayerStart",
    "SBase.SAmbientSoundTrigger", "SBase.SHelpPath", "SBase.SZoneTips", "Engine.LevelInfo",
    "Engine.ZoneInfo", "Engine.SkyZoneInfo", "Engine.PlayerStart", "Engine.Trigger",
    "Engine.MusicTrigger", "Engine.Emitter", "Engine.Light", "Engine.AmbientSound",
    "Engine.SoundTrigger", "Engine.BlockingVolume", "Engine.Volume", "Engine.StaticMeshActor",
    "Engine.AntiPortalActor",
}
SLICE_RUNTIME = {
    "SBase.SGameInfo", "SBase.STeamInfo", "SBase.STeamAttaquant", "SBase.STeamDefenseur",
    "SBase.SInteractionManager", "SBase.SInteractObject", "SBase.SComputerInteraction",
    "SBase.SFlagDropZoneInteraction", "SBase.SPickupInteraction", "SBase.STriggerInteraction",
    "SBase.SInfirmaryInteraction", "SBase.SAmmoStockInteraction", "SBase.SSavedMove",
    "SBase.SPlayerCam", "SBase.SHud", "SBase.SGoggleBeam", "SBase.SGoggleBeam_ATT",
    "SBase.SGoggleBeam_DEF", "SBase.SWeapon", "SBase.SWeaponAttaque", "SBase.SWeaponDefenseDummy",
    "SBase.SGadget", "SBase.SGadTorche", "SBase.SGadGrenLauncher", "SBase.SGadFragGrenade",
    "SBase.SGadSmokeGrenade", "SBase.SProjGrenade", "SBase.SProjFrag", "SBase.SProjSmoke",
    "SBase.SSmokeEmitter", "SBase.SGrenadeExplosion", "SBase.SPersistentData",
    "SBase.SGameMessages", "SBase.SDeathMessage", "SBase.SPawnAttaque_1Mesh",
    "SBase.SPawnDefense_1Mesh", "SBase.SImpactEffectBase", "SBase.SMotionTrackingVision",
}

# ---------------------------------------------------------------------------------------------
# Subsystem guess. Ordered rules; the first match wins. Function-level rules run before
# class-level ones so that, for example, the move protocol inside SPlayerController is "net.move".
FUNC_RULES: list[tuple[str, str]] = [
    (r"ServerMove|ClientAdjustPosition|ReplicateMove|BigServerMove|ClientUpdatePosition|"
     r"SavedSMove|CompressActions|ProcessMove", "net.move"),
    (r"MirrorState|PCChange(Client|Server)State|^ChangeState$|ReinitPawnBecauseOfDivergences|"
     r"ReinitBecauseOfDivergences|DistantClientReinit|ClientIsReinit|WaitForClientToReinit|"
     r"TellServerIAMReadyToReinit", "net.statesync"),
    (r"SendPawn|ClientGetPawn|ReliableSendPawn|GetNewActions|SendActions|ReplicateActionToServer|"
     r"ExecuteReceivedAction|ClientDistantMoveSmooth|UpdatePositionForServer|UpdateDesiredRotation",
     "net.proxy"),
    (r"Ledge|Pipe|Zip|Fence|Ladder|HandOverHand|HOH|WallJump|Vandamme|BTW|BackToWall|Hoist|"
     r"ShortLadder|Polerope|Pole|Split|Barrier|GE[A-Z]|NoGE", "movement.traversal"),
    (r"PlayerMove|UpdateRotation|Falling|Landed|Jump|Crouch|Roll|physics|Physics|Walk|Velocity",
     "movement"),
    (r"Grab|Elbow|Terminate|Reanimate|Charge|Berserk|DropAttack|FallAttack|Strangl|NeckSnap",
     "combat.melee"),
    (r"Damage|TakeDamage|Died|Dying|Dead|Kill|Fire|Shot|Shoot|Bullet|Reload|Ammo|Weapon", "combat"),
    (r"CalcView|Camera|Cam[A-Z]|FOV|Zoom|ViewTarget|BehindView", "camera"),
    (r"Anim|ExecuteAction|ChangeEnumAction|Notify", "anim"),
    (r"Goggle|Vision|EMF|Thermal|NightVision", "vision"),
    (r"Hud|HUD|Message|Menu|Magma|Scoreboard|ScoreBoard", "ui.hud"),
    (r"Sound|Music|Voice|Audio|EAX", "audio"),
    (r"Rumble|Vibration|Pad[A-Z]|Input|Key", "input"),
]
CLASS_RULES: list[tuple[str, str]] = [
    (r"^Core\.", "core"),
    (r"^GUI\.", "ui.gui"),
    (r"^IpDrv\.", "online.ipdrv"),
    (r"^SBase\.EFFECT_", "audio.eax"),
    (r"^SBase\.(SXBoxLiveRef|SLAN\w+|SPersistentData|SPlayerProfile|SRJ45Checker|SMatchInfo|"
     r"LancementMenu)$", "online"),
    (r"^SBase\.(SHud|SScoreBoard\w*|SGameMessages|SDeathMessage)$", "ui.hud"),
    (r"^SBase\.SPlayerController$", "controller"),
    (r"^SBase\.SPlayerCam$", "camera"),
    (r"^SBase\.(SBasePawn|SPawnAttaque\w*|SPAWNDEFENSE|SPawnDefense\w*|SSavedMove)$", "pawn"),
    (r"^SBase\.(SGoggle\w*|SMotionTrackingVision)$", "vision"),
    (r"^SBase\.(SWeapon\w*|SStickyShocker|SLethalWeapon|SMuzzle\w*|sMuzzleCanon)$", "combat.weapon"),
    (r"^SBase\.(SGad\w+|SGadget|SProj\w+|SStickyCam|SCamNetwork|SMine\w*|SMines|SPresDetector\w*|"
     r"SBackPack\w*|SGasMask|SNoiseEmitter|SBallTrap)$", "gadgets"),
    (r"^SBase\.(SObjective\w*|SMission|SFlag|SFlagDropZone|SComputerObjectiveTrigger|SBomb\w+|"
     r"SDisk)$", "objectives"),
    (r"^SBase\.(S\w*Interaction|SInteractObject|SInteractionManager)$", "interaction"),
    (r"^SBase\.(SGameInfo|STeam\w*|SMapSettings)$", "rules"),
    (r"^SBase\.(SImpact\w+|S\w*Emitter|SSpark\w+|SEffect\w+|SFireTrace|SFlashBang|SBloodSplat|"
     r"SFootPrint\w*|SLaserBeam\w*|SLaserProjector|SFlareBurnProjector|SMetalHotSpot|SMetalSpark|"
     r"STazerEmitter|SProjoLight|SShotGunProjector|SWaterFootStepEffect|SGadLaunchEffects|"
     r"SGrenLaunchEffects|SGrenadeExplosion|SDouilleEmitter)$", "fx"),
    (r"^SBase\.(SFish\w*)$", "map.ambient"),
    (r"^SBase\.", "map.gameplay"),
    (r"^Engine\.(Mover|MoverRotating)$", "map.mover"),
    (r"^Engine\.(Pawn|Scout)$", "pawn"),
    (r"^Engine\.(Controller|PlayerController|AIController|PlayerInput|CheatManager|AdminBase|"
     r"Admin)$", "controller"),
    (r"^Engine\.(GameInfo|GameReplicationInfo|PlayerReplicationInfo|TeamInfo|ReplicationInfo|"
     r"GameStats|Security|MatchInfo|BroadcastHandler|AccessControl|Mutator|LocalMessage|"
     r"GameMessage)$", "rules"),
    (r"^Engine\.(LevelInfo|ZoneInfo|SkyZoneInfo|WarpZoneInfo|LevelSummary|Info|Level)$", "level"),
    (r"^Engine\.(Inventory|Weapon|Pickup|Ammunition|Powerups|InventoryAttachment|"
     r"WeaponAttachment|Projectile)$", "inventory"),
    (r"^Engine\.(\w*Trigger\w*|Triggers|Teleporter|NavigationPoint|PlayerStart|PathNode)$",
     "map.trigger"),
    (r"^Engine\.(\w*Volume)$", "map.volume"),
    (r"^Engine\.(\w*Emitter|Emitter|Effects|ParticleEmitter|\w*Particle\w*)$", "fx"),
    (r"^Engine\.(HUD|Canvas|Console|Interaction|Interactions|BaseGUIController|Player|Viewport)$",
     "ui"),
    (r"^Engine\.(\w*Light\w*|Projector\w*|ShadowProjector|\w*Material\w*|\w*Texture\w*|"
     r"\w*Modifier|Shader|Combiner|FinalBlend|Palette|Camera|StaticMesh\w*|Mesh\w*|Model|"
     r"\w*Render\w*|AntiPortalActor|Brush|Decoration|Decal\w*)$", "render"),
    (r"^Engine\.(\w*Sound\w*|Sound|Music|I3DL2Listener)$", "audio"),
    (r"^Engine\.(Actor|Object|ActorNetCollection)$", "engine.actor"),
    (r"^Engine\.", "engine"),
]

_FUNC_RX = [(re.compile(p), s) for p, s in FUNC_RULES]
_CLASS_RX = [(re.compile(p), s) for p, s in CLASS_RULES]


def guess_subsystem(full_class: str, state: str | None, name: str | None) -> str:
    for text in (name, state):
        if text:
            for rx, sub in _FUNC_RX:
                if rx.search(text):
                    return sub
    for rx, sub in _CLASS_RX:
        if rx.search(full_class):
            return sub
    return "other"


# ---------------------------------------------------------------------------------------------
# Inputs

_NATIVE_LINE = re.compile(
    r"^\s*(\d+)\s+0x([0-9a-fA-F]+)\s+(\S+)\s+flags\s+0x([0-9a-fA-F]+)\s+func\s+0x([0-9a-fA-F]+)"
)


def load_natives(path: Path) -> dict[str, dict]:
    """natives_all.txt: '<index> 0x<hex> <Pkg.Class.Func> flags 0x.. func 0x<addr> <parm word>'."""
    out: dict[str, dict] = {}
    if not path.is_file():
        return out
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        m = _NATIVE_LINE.match(line)
        if not m:
            continue
        idx, _hx, fpath, flags, addr = m.groups()
        out[fpath] = {"index": int(idx), "flags": int(flags, 16), "addr": int(addr, 16)}
    return out


def load_reflect(path: Path) -> dict:
    if not path.is_file():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def module_of(addr: int, modules: list[dict]) -> str | None:
    for m in modules:
        base = int(m["base"], 16)
        size = int(m["size"], 16)
        if base <= addr < base + size:
            return m["name"]
    return None


def hx(v: int | None) -> str | None:
    return None if v is None else f"0x{v:08x}"


# ---------------------------------------------------------------------------------------------
# Build

def _direction(cond: str) -> str:
    c = cond.replace(" ", "")
    to_server = "Role<ROLE_Authority" in c
    to_client = ("Role==ROLE_Authority" in c or "RemoteRole" in c or "bNetInitial" in c
                 or "bNetOwner" in c or "bNetDirty" in c)
    if to_server and not to_client:
        return "client_to_server"
    if to_client and not to_server:
        return "server_to_client"
    if c in ("false", "(false)"):
        return "never"
    return "conditional"


def _cond_flags(cond: str) -> list[str]:
    c = cond.replace(" ", "")
    flags = []
    for token, flag in (("bNetInitial", "bNetInitial"), ("bNetOwner", "bNetOwner"),
                        ("bNetDirty", "bNetDirty"), ("RemoteRole", "RemoteRole"),
                        ("bSkipActorPropertyReplication", "bSkipActorPropertyReplication"),
                        ("bReplicateMovement", "bReplicateMovement"),
                        ("bUpdateSimulatedPosition", "bUpdateSimulatedPosition")):
        if token in c:
            flags.append(flag)
    if c in ("false", "(false)"):
        flags.append("never")
    return flags


def build(uc_index: dict, natives: dict[str, dict], reflect: dict) -> dict:
    classes = uc_index.get("classes", [])
    by_full = {f"{c['package']}.{c['class']}": c for c in classes}

    # Reflection lookups.
    modules = reflect.get("modules", [])
    exe = next((m for m in modules if m["name"].lower() == "scct versus"), None)
    r_funcs = {f["path"]: f for f in reflect.get("functions", [])}
    r_props = {p["path"]: p for p in reflect.get("properties", [])}
    r_classes = {c["path"]: c for c in reflect.get("classes", [])}

    # Slice set with ancestors.
    slice_reason: dict[str, str] = {}
    for group, reason in ((SLICE_LISTED, "listed"), (SLICE_STEEE, "steee"),
                          (SLICE_RUNTIME, "runtime")):
        for full in sorted(group):
            if full in by_full and full not in slice_reason:
                slice_reason[full] = reason
    for full in list(slice_reason):
        sup = by_full[full].get("super")
        while sup and sup in by_full:
            slice_reason.setdefault(sup, "ancestor")
            sup = by_full[sup].get("super")

    units: list[dict] = []
    seen_natives: set[str] = set()

    def func_unit(full: str, cls: dict, state: str | None, f: dict) -> dict:
        pkg, cname = cls["package"], cls["class"]
        is_native = "Native" in f["flags"]
        path = f"{full}.{state}.{f['name']}" if state else f"{full}.{f['name']}"
        if is_native:
            uid = f"nat:{path}"
            kind = "nat"
        else:
            uid = f"uc:{full}:{state}.{f['name']}" if state else f"uc:{full}:{f['name']}"
            kind = "uc-state-func" if state else "uc-func"
        rf = r_funcs.get(path)
        rec = {
            "id": uid,
            "kind": kind,
            "pkg": pkg,
            "class": cname,
            "state": state,
            "name": f["name"],
            "flags": sorted(f["flags"]),
            "script_size": f.get("script_size"),
            "file_bytes": f.get("file_bytes"),
            "params": len(f.get("params") or []),
            "locals": len(f.get("locals") or []),
            "returns": f.get("return") is not None,
            "latent_calls": len(f.get("latent_calls") or []),
            "net": "Net" in f["flags"],
            "net_reliable": ("NetReliable" in f["flags"]) if "Net" in f["flags"] else None,
            "simulated": "Simulated" in f["flags"],
            "rep_offset": f.get("rep_offset"),
            "sha1": f.get("sha1"),
            "line": f.get("line"),
        }
        if is_native:
            seen_natives.add(path)
            nat = natives.get(path)
            rec["native_index"] = f.get("native") or (nat or {}).get("index")
            addr = int(rf["func"], 16) if rf and rf.get("func") else None
            rec["native_addr"] = hx(addr)
            rec["native_module"] = module_of(addr, modules) if addr is not None else None
            rec["native_rva"] = (hx(addr - int(exe["base"], 16))
                                 if addr is not None and exe and rec["native_module"] == exe["name"]
                                 else None)
            rec["native_addr_natives_all"] = hx(nat["addr"]) if nat else None
        rec["subsystem"] = guess_subsystem(full, state, f["name"])
        rec["slice"] = full in slice_reason
        rec["slice_reason"] = slice_reason.get(full)
        return rec

    for cls in sorted(classes, key=lambda c: (c["package"], c["class"])):
        pkg, cname = cls["package"], cls["class"]
        full = f"{pkg}.{cname}"
        rcls = r_classes.get(full, {})
        flags = sorted(cls.get("flags") or [])
        # def: unit
        units.append({
            "id": f"def:{full}",
            "kind": "def",
            "pkg": pkg,
            "class": cname,
            "state": None,
            "name": None,
            "super": cls.get("super"),
            "flags": flags,
            "native_class": bool(cls.get("native")),
            "native_replication": "NativeReplication" in flags,
            "placeable": "Placeable" in flags,
            "defaults_values": (cls.get("defaults") or {}).get("defaults_values"),
            "export_bytes": (cls.get("sizes") or {}).get("export_bytes"),
            "script_bytes": (cls.get("sizes") or {}).get("script_bytes"),
            "properties_size": rcls.get("propertiesSize"),
            "vars": len(cls.get("vars") or []),
            "functions": len(cls.get("functions") or []),
            "states": len(cls.get("states") or []),
            "rep_members": sum(len(b.get("members") or []) for b in cls.get("replication") or []),
            "class_reps": rcls.get("classReps"),
            "probe_mask": rcls.get("probeMask"),
            "constructor_rva": rcls.get("constructorRva"),
            "vtable_rva": rcls.get("vtableRva"),
            "subsystem": guess_subsystem(full, None, None),
            "slice": full in slice_reason,
            "slice_reason": slice_reason.get(full),
        })
        for f in sorted(cls.get("functions") or [], key=lambda x: x["name"]):
            units.append(func_unit(full, cls, None, f))
        for st in sorted(cls.get("states") or [], key=lambda x: x["name"]):
            sname = st["name"]
            for f in sorted(st.get("functions") or [], key=lambda x: x["name"]):
                units.append(func_unit(full, cls, sname, f))
            if st.get("labels"):
                units.append({
                    "id": f"uc:{full}:{sname}@code",
                    "kind": "uc-state-code",
                    "pkg": pkg,
                    "class": cname,
                    "state": sname,
                    "name": None,
                    "flags": sorted(st.get("flags") or []),
                    "script_size": st.get("script_size"),
                    "labels": len(st.get("labels") or []),
                    "latent_calls": len(st.get("latent_calls") or []),
                    "extends": st.get("extends"),
                    "ignores": len(st.get("ignores") or []),
                    "simulated": "Simulated" in (st.get("flags") or []),
                    "sha1": st.get("sha1"),
                    "subsystem": guess_subsystem(full, sname, None),
                    "slice": full in slice_reason,
                    "slice_reason": slice_reason.get(full),
                })
        # rep: units
        fn_by_name = {f["name"]: f for f in cls.get("functions") or []}
        for bi, block in enumerate(cls.get("replication") or []):
            cond = block.get("condition") or ""
            kinds = block.get("kinds") or []
            for mi, member in enumerate(block.get("members") or []):
                mkind = kinds[mi] if mi < len(kinds) else "unknown"
                rec = {
                    "id": f"rep:{full}.{member}",
                    "kind": "rep-func" if mkind == "function" else "rep-var",
                    "pkg": pkg,
                    "class": cname,
                    "state": None,
                    "name": member,
                    "block": bi,
                    "block_keyword": "reliable" if block.get("reliable") else "unreliable",
                    "direction": _direction(cond),
                    "condition_flags": _cond_flags(cond),
                    "condition_sha1": hashlib.sha1(cond.encode("utf-8")).hexdigest()[:12],
                    "native_replication": "NativeReplication" in flags,
                    "subsystem": "net",
                    "slice": full in slice_reason,
                    "slice_reason": slice_reason.get(full),
                }
                if mkind == "function":
                    f = fn_by_name.get(member)
                    rf = r_funcs.get(f"{full}.{member}")
                    fflags = (f or {}).get("flags") or (rf or {}).get("flagNames") or []
                    rec["flags"] = sorted(fflags)
                    rec["net_reliable"] = "NetReliable" in fflags
                    rec["simulated"] = "Simulated" in fflags
                    rec["native"] = "Native" in fflags
                    rec["rep_offset"] = (f or {}).get("rep_offset")
                else:
                    rp = r_props.get(f"{full}.{member}")
                    rec["flags"] = sorted((rp or {}).get("flagNames") or [])
                    rec["prop_kind"] = (rp or {}).get("kind")
                    rec["array_dim"] = (rp or {}).get("arrayDim")
                    rec["element_size"] = (rp or {}).get("elementSize")
                    rec["offset"] = (rp or {}).get("offset")
                    rec["rep_offset"] = (rp or {}).get("repOffset")
                    rec["rep_index"] = (rp or {}).get("repIndex")
                units.append(rec)

    # Natives listed in natives_all.txt with no UFunction in the index (kept so coverage sees them).
    for path, nat in sorted(natives.items()):
        if path in seen_natives:
            continue
        parts = path.split(".")
        full = ".".join(parts[:2])
        units.append({
            "id": f"nat:{path}",
            "kind": "nat",
            "pkg": parts[0],
            "class": parts[1] if len(parts) > 1 else None,
            "state": ".".join(parts[2:-1]) or None,
            "name": parts[-1],
            "flags": [],
            "native_index": nat["index"],
            "native_addr": None,
            "native_module": None,
            "native_rva": None,
            "native_addr_natives_all": hx(nat["addr"]),
            "orphan": True,
            "subsystem": guess_subsystem(full, None, parts[-1]),
            "slice": full in slice_reason,
            "slice_reason": slice_reason.get(full),
        })

    units.sort(key=lambda u: u["id"])
    ids = [u["id"] for u in units]
    dupes = sorted(k for k, v in Counter(ids).items() if v > 1)

    meta = {
        "format": FORMAT,
        "inputs": {
            "uc_index_totals": uc_index.get("totals"),
            "natives_all_entries": len(natives),
            "reflect_image": reflect.get("image"),
            "reflect_image_sha256": reflect.get("imageSha256"),
            "reflect_map": reflect.get("map"),
            "reflect_side": reflect.get("side"),
            "reflect_captured_at": reflect.get("capturedAt"),
        },
        "slice_classes": {k: slice_reason[k] for k in sorted(slice_reason)},
        "duplicate_ids": dupes,
        "unit_count": len(units),
    }
    return {"meta": meta, "units": units}


# ---------------------------------------------------------------------------------------------
# Output

def dump_inventory(inv: dict) -> str:
    """One unit per line keeps diffs readable."""
    lines = ["{", '"meta": ' + json.dumps(inv["meta"], sort_keys=True) + ",", '"units": [']
    units = inv["units"]
    for i, u in enumerate(units):
        lines.append(json.dumps(u, sort_keys=True) + ("," if i + 1 < len(units) else ""))
    lines.append("]}")
    return "\n".join(lines) + "\n"


def _table(headers: list[str], rows: list[list]) -> list[str]:
    out = ["| " + " | ".join(headers) + " |", "|" + "|".join("---" for _ in headers) + "|"]
    for r in rows:
        out.append("| " + " | ".join(str(x) for x in r) + " |")
    return out


KIND_ORDER = ["def", "uc-func", "uc-state-func", "uc-state-code", "nat", "rep-var", "rep-func"]


def render_readme(inv: dict) -> str:
    units = inv["units"]
    meta = inv["meta"]
    pkgs = sorted({u["pkg"] for u in units})
    by_pk = Counter((u["pkg"], u["kind"]) for u in units)
    size_pk = Counter()
    for u in units:
        if u["kind"] in ("uc-func", "uc-state-func", "uc-state-code"):
            size_pk[u["pkg"]] += u.get("script_size") or 0

    lines = [
        "# re/inventory: coverage inventory",
        "",
        "Generated by `re/tools/inventory/build.py` (do not edit by hand). Every unit of the original",
        "game's script layer, with names, sizes and flags only: no bytecode, no decompiled text and no",
        "replication condition text. `coverage/status.yaml` and `svm coverage` read `inventory.json`.",
        "",
        "Rebuild from the repository root (needs the local decompiler index and reflection dump):",
        "",
        "    python -I -B re/tools/inventory/build.py",
        "",
        "## Unit ids",
        "",
        "| Kind | Id form | One per |",
        "|---|---|---|",
        "| `uc-func` | `uc:<Pkg>.<Class>:<Func>` | script function of a class |",
        "| `uc-state-func` | `uc:<Pkg>.<Class>:<State>.<Func>` | script function declared in a state |",
        "| `uc-state-code` | `uc:<Pkg>.<Class>:<State>@code` | state with labels (state code) |",
        "| `nat` | `nat:<Pkg>.<Class>.<Func>` | native function (indexed natives; `orphan` when only the native table has it) |",
        "| `rep-var` | `rep:<Pkg>.<Class>.<Var>` | replicated variable (one per member of a replication block) |",
        "| `rep-func` | `rep:<Pkg>.<Class>.<Func>` | replicated function (one per member of a replication block) |",
        "| `def` | `def:<Pkg>.<Class>` | class default properties |",
        "",
        "Rep units are per member, so a glob meant to cover a class's replication needs a trailing",
        "`.*` (for example `rep:*SXBoxLiveRef.*`).",
        "",
        "Fields: `flags` (function, state or class flags; property flags for `rep-var`), `script_size`",
        "(in-memory bytecode bytes, the port weight), `file_bytes` (bytes on disk), `native_index`,",
        "`native_addr` (from the reflection dump, `native_module` says whether it lands in the game",
        "image or in `Reloaded.Core.dll`), `native_rva` (game image only), `native_addr_natives_all`",
        "(address in the older native table capture), `net`/`net_reliable`/`simulated`, `rep_offset`,",
        "`direction` and `condition_flags` (rep units), `subsystem` (a guess from names), `slice` and",
        "`slice_reason` (`listed`, `steee`, `runtime` or `ancestor`).",
        "",
        "## Inputs",
        "",
        f"- Decompiler index totals: `{json.dumps(meta['inputs']['uc_index_totals'], sort_keys=True)}`",
        f"- Native table entries: {meta['inputs']['natives_all_entries']}",
        f"- Reflection: image `{meta['inputs']['reflect_image']}`, map `{meta['inputs']['reflect_map']}`, "
        f"side `{meta['inputs']['reflect_side']}`, captured {meta['inputs']['reflect_captured_at']}",
        f"- Units: {meta['unit_count']}; duplicate ids: {len(meta['duplicate_ids'])}",
        "",
        "## Units per package and kind",
        "",
    ]
    rows = []
    for p in pkgs:
        rows.append([p] + [by_pk.get((p, k), 0) for k in KIND_ORDER]
                    + [sum(by_pk.get((p, k), 0) for k in KIND_ORDER), size_pk.get(p, 0)])
    rows.append(["**all**"] + [sum(by_pk.get((p, k), 0) for p in pkgs) for k in KIND_ORDER]
                + [len(units), sum(size_pk.values())])
    lines += _table(["Package"] + KIND_ORDER + ["total", "script bytes"], rows)

    # Slice
    sl = [u for u in units if u.get("slice")]
    lines += ["", "## Slice (SteeE Story 2v2)", ""]
    rows = []
    for reason in ("listed", "steee", "runtime", "ancestor"):
        us = [u for u in sl if u.get("slice_reason") == reason]
        cls_n = len({(u["pkg"], u["class"]) for u in us})
        kc = Counter(u["kind"] for u in us)
        sz = sum(u.get("script_size") or 0 for u in us
                 if u["kind"] in ("uc-func", "uc-state-func", "uc-state-code"))
        rows.append([reason, cls_n] + [kc.get(k, 0) for k in KIND_ORDER] + [len(us), sz])
    lines += _table(["Reason", "classes"] + KIND_ORDER + ["total", "script bytes"], rows)
    lines += ["", "Slice classes: " + ", ".join(
        f"`{k}` ({v})" for k, v in sorted(meta["slice_classes"].items())) + "."]

    # Subsystems
    sub = Counter(u["subsystem"] for u in units if u["kind"] != "def")
    sub_sz = Counter()
    for u in units:
        if u["kind"] in ("uc-func", "uc-state-func", "uc-state-code"):
            sub_sz[u["subsystem"]] += u.get("script_size") or 0
    lines += ["", "## Units per subsystem guess (excluding def units)", ""]
    lines += _table(["Subsystem", "units", "script bytes"],
                    [[k, v, sub_sz.get(k, 0)] for k, v in sorted(sub.items(), key=lambda x: -x[1])])

    # Natives
    nats = [u for u in units if u["kind"] == "nat"]
    mod = Counter((u.get("native_module") or "unknown") for u in nats)
    lines += ["", "## Natives", ""]
    lines += _table(["Module of the native thunk (reflection)", "natives"],
                    [[k, v] for k, v in sorted(mod.items())])
    hooked = sorted(u["id"] for u in nats if (u.get("native_module") or "").lower().startswith("reloaded"))
    if hooked:
        lines += ["", "Natives whose thunk lives in `Reloaded.Core.dll` in the captured game (behaviour",
                  "changed by the Reloaded patch, each needs an `rl:` note): " +
                  ", ".join(f"`{h[4:]}`" for h in hooked) + "."]
    orphans = sorted(u["id"] for u in nats if u.get("orphan"))
    lines += ["", f"Natives only in the native table (no UFunction in the index): {len(orphans)}."]
    if orphans:
        lines += [", ".join(f"`{o[4:]}`" for o in orphans) + "."]

    # Replication
    reps = [u for u in units if u["kind"] in ("rep-var", "rep-func")]
    lines += ["", "## Replication members", ""]
    rows = []
    for k in ("rep-var", "rep-func"):
        us = [u for u in reps if u["kind"] == k]
        d = Counter(u["direction"] for u in us)
        rows.append([k, len(us), d.get("server_to_client", 0), d.get("client_to_server", 0),
                     d.get("conditional", 0), d.get("never", 0),
                     sum(1 for u in us if u.get("native_replication")),
                     sum(1 for u in us if u.get("slice"))])
    lines += _table(["Kind", "members", "server to client", "client to server", "conditional",
                     "never", "in nativereplication classes", "slice"], rows)
    rf = [u for u in reps if u["kind"] == "rep-func"]
    lines += ["", f"Replicated functions: {sum(1 for u in rf if u.get('net_reliable'))} reliable, "
              f"{sum(1 for u in rf if not u.get('net_reliable'))} unreliable, "
              f"{sum(1 for u in rf if u.get('simulated'))} simulated, "
              f"{sum(1 for u in rf if u.get('native'))} native."]
    lines.append("")
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--uc-index", type=Path, default=DEFAULT_UC_INDEX)
    ap.add_argument("--natives", type=Path, default=DEFAULT_NATIVES)
    ap.add_argument("--reflect", type=Path, default=DEFAULT_REFLECT)
    ap.add_argument("--out-dir", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--check", action="store_true", help="exit 1 if the outputs on disk differ")
    a = ap.parse_args(argv)

    if not a.uc_index.is_file():
        print(f"missing decompiler index: {a.uc_index}", file=sys.stderr)
        return 2
    uc_index = json.loads(a.uc_index.read_text(encoding="utf-8"))
    natives = load_natives(a.natives)
    if not natives:
        print(f"warning: no native table at {a.natives}; native_addr_natives_all stays empty",
              file=sys.stderr)
    reflect = load_reflect(a.reflect)
    if not reflect:
        print(f"warning: no reflection dump at {a.reflect}; addresses and rep indexes stay empty",
              file=sys.stderr)

    inv = build(uc_index, natives, reflect)
    inv_text = dump_inventory(inv)
    readme_text = render_readme(inv)

    inv_path = a.out_dir / "inventory.json"
    readme_path = a.out_dir / "README.md"
    if a.check:
        ok = (inv_path.is_file() and inv_path.read_text(encoding="utf-8") == inv_text
              and readme_path.is_file() and readme_path.read_text(encoding="utf-8") == readme_text)
        print("inventory up to date" if ok else "inventory out of date")
        return 0 if ok else 1
    a.out_dir.mkdir(parents=True, exist_ok=True)
    inv_path.write_text(inv_text, encoding="utf-8", newline="\n")
    readme_path.write_text(readme_text, encoding="utf-8", newline="\n")
    print(f"wrote {inv_path} ({inv['meta']['unit_count']} units) and {readme_path}")
    if inv["meta"]["duplicate_ids"]:
        print(f"warning: {len(inv['meta']['duplicate_ids'])} duplicate ids", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
