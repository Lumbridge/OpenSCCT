"""Resolve a material object reference to a diffuse texture (and blend info).

SCCT material classes wrap textures: Shader.Diffuse, FinalBlend.Material,
Combiner.Material1, LitSpecularMaterial.Diffuse, TexPanner/TexScaler/
TexRotator/TexOscillator/TexEnvMap.Material, MaterialSequence, Cubemap.
The chain is followed until a Texture export is found.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, Optional, Tuple

from .package import Export, Package
from .packageset import PackageSet
from .properties import read_properties, skip_state_frame
from .texture import Texture, read_texture

_CHAIN_PROPS = {
    "Shader": ("Diffuse",),
    "FinalBlend": ("Material",),
    "Combiner": ("Material1", "Material2"),
    "LitSpecularMaterial": ("Diffuse", "FallbackMaterial"),
    "TexPanner": ("Material",),
    "TexFriendlyPanner": ("Material",),
    "TexScaler": ("Material",),
    "TexRotator": ("Material",),
    "TexOscillator": ("Material",),
    "TexEnvMap": ("Material",),
    "MaterialSequence": ("FallbackMaterial",),
    "SpecialVisionTextureModifier": ("NormalMaterial", "Material"),   # normal-vision texture first; Material is the heat/EMF map
    "HeatTextureModifier": ("NormalMaterial", "Material"),   # normal-vision texture first; Material is the heat/EMF map
    "EMFTextureModifier": ("NormalMaterial", "Material"),   # normal-vision texture first; Material is the heat/EMF map
    "EchelonLightingMaterial": ("Diffuse", "Material"),
    "ColorModifier": ("Material",),
    "ConstantMaterial": (),
}


# Additive strength of a Brighten Shader with a lit Diffuse (see ResolvedMaterial.additive_gain): the first-aid
# cabinet's panel under the glass measures ~40/255 in FactD's dim infirmary corner, i.e. at most ~5% of fullbright.
LIT_BRIGHTEN_GAIN = 0.05


def _soft_alpha(tx: Texture) -> bool:
    """True when the texture's alpha is a gradient rather than a cut-out: many mid values, or nothing a 0.5 alpha test
    would keep (TXT_INI.mh_glass peaks at 99/255, GAR_Cloud_Pan at 122)."""
    if tx.image is None or "A" not in tx.image.getbands():
        return False
    hist = tx.image.getchannel("A").histogram()
    total = float(sum(hist)) or 1.0
    return sum(hist[16:240]) / total > 0.15 or sum(hist[128:]) / total < 0.02


@dataclass
class ResolvedMaterial:
    key: str                      # unique key "Package.Name"
    texture: Optional[Texture]
    two_sided: bool = False
    translucent: bool = False
    masked: bool = False
    unlit: bool = False
    invisible: bool = False   # OB_Invisible / FB_Invisible: never draw
    additive: bool = False    # brighten (ONE, ONE): light beams, glows, water sheets; exported as unlit additive
    # Strength of the unlit additive relative to a fullbright texture. A Brighten Shader whose Diffuse is lit (no
    # SelfIllumination: the first-aid cabinet's glass) adds diffuse x the local light, which is dim indoors; drawn
    # fullbright it washed the whole cabinet out to white.
    additive_gain: float = 1.0
    darken: bool = False      # darken blending (dest x (1 - src)): shadow decals, tinted glass
    # Multiplicative blends, drawn with UE's Modulate blend (dest x emissive): "2x" for OB_Modulate / FB_Modulate
    # (D3D DESTCOLOR, SRCCOLOR: dest x src x 2, mid grey leaves the scene unchanged) and "darken" for OB_Darken /
    # FB_Darken (ZERO, INVSRCCOLOR: dest x (1 - src)). Empty: not multiplicative.
    modulate: str = ""
    opacity_key: str = ""
    # Shader.Opacity resolved to a texture: UE2 alpha-blends the shader through it (export_gltf bakes it into the
    # exported texture's alpha). Water sheets, aquarium/window glass, fog planes, fish shadows, puddles.
    opacity: Optional[Texture] = None
    # Shader.SelfIllumination / SelfIlluminationMask (resolved to textures): the masked part is drawn fullbright,
    # the rest is lit normally (first-aid cross, terminal screens).
    self_illum_key: str = ""
    self_illum: Optional[Texture] = None
    self_illum_mask_key: str = ""
    self_illum_mask: Optional[Texture] = None
    # An animated modifier (TexPanner, ...) wraps the Shader that carries the SelfIllumination: UE2 transforms every
    # stage of the wrapped Shader, so the glow moves with the diffuse (AquaE info kiosks scroll their text; the glow
    # layer drawn static showed a fixed "INFORMATION" over the scroll). False when only the Diffuse is animated.
    self_illum_anim: bool = False
    # Static texture-coordinate transform of a TexScaler / fixed TexRotator in the chain: (a, b, c, d, e, f) with
    # u' = a*u + c*v + e, v' = b*u + d*v + f (UE2 row-vector texture matrix). Baked into the exported UVs.
    uv_matrix: Optional[Tuple[float, float, float, float, float, float]] = None
    # Animated modifiers (TexPanner / TexFriendlyPanner / TexOscillator / rotating TexRotator): parameters, animated
    # by the importer's AnimatedUV graph.
    uv_anim: Optional[dict] = None
    # Combiner: Material1 (self.texture) combined with Material2 / Mask, baked into one texture on export.
    # {"op", "alpha_op", "mod": 1|2|4, "tex2": Texture|None, "mask": Texture|None, "invert_mask": bool}
    combine: Optional[dict] = None
    # Shader.Specular is an environment map (TexEnvMap / Cubemap): the game adds a reflection. The importer gives the
    # surface a glossy roughness so Lumen reflects the real scene instead.
    env_map: bool = False
    # Average colour (display RGB 0-1) of the env-map texture the game adds: its reflected energy for the Faithful look.
    env_color: Optional[Tuple[float, float, float]] = None
    # Shader.SpecularityMask of an env-mapped shader: where the reflection applies (its alpha), e.g. gun metal.
    spec_mask_key: str = ""
    spec_mask: Optional[Texture] = None
    # ConstantColor material (no texture): flat colour, (R, G, B, A) 0-255.
    color: Optional[Tuple[int, int, int, int]] = None
    # Texture.AnimNext chain (animated texture, e.g. Aquarium Caustic1.C01..C16): {"frames": [Texture], "fps": float};
    # `texture` stays the first frame.
    flipbook: Optional[dict] = None
    # Shader inputs with their own texture modifiers. UE2 transforms each stage of a Shader by its own modifier chain,
    # so an Opacity or SpecularityMask that pans at another rate than the Diffuse (ClubD's CLU_Water_01_SH) cannot be
    # baked into the diffuse's alpha: the exporter writes them as separate layers when they differ from the Diffuse.
    opacity_anim: Optional[dict] = None
    opacity_matrix: Optional[Tuple[float, float, float, float, float, float]] = None
    opacity_layer: bool = False
    spec_mask_anim: Optional[dict] = None
    spec_mask_matrix: Optional[Tuple[float, float, float, float, float, float]] = None
    # The env map's own 2D texture (a TexEnvMap over a Texture; cube maps leave it None): the client looks it up by the
    # camera-space reflection instead of adding its mean colour.
    env_texture: Optional[Texture] = None
    env_key: str = ""

    @property
    def skip(self) -> bool:
        return self.invisible


def _plane(m: dict, name: str) -> Tuple[float, float]:
    p = m.get(name) if isinstance(m, dict) else None
    if not isinstance(p, dict):
        return (0.0, 0.0)
    return (float(p.get("X", 0.0) or 0.0), float(p.get("Y", 0.0) or 0.0))


def _static_uv_matrix(cls: str, props: dict) -> Optional[Tuple[float, float, float, float, float, float]]:
    """The engine caches each modifier's texture matrix in its `M` property; for the static ones (TexScaler, fixed
    TexRotator) that cached matrix is exact. fac_fence_scaler: UScale 0.09 -> M.X.X = 11.11 (tiled 11x)."""
    if cls == "TexScaler" or (cls == "TexRotator" and int(props.get("TexRotationType", 0) or 0) == 0):
        m = props.get("M")
        if isinstance(m, dict):
            (a, b), (c, d), (e, f) = _plane(m, "XPlane"), _plane(m, "YPlane"), _plane(m, "ZPlane")
        elif cls == "TexScaler":
            us = float(props.get("UScale", 1.0) or 1.0)
            vs = float(props.get("VScale", 1.0) or 1.0)
            a, b, c, d, e, f = 1.0 / us, 0.0, 0.0, 1.0 / vs, 0.0, 0.0
        else:
            return None
        if (a, b, c, d, e, f) == (1.0, 0.0, 0.0, 1.0, 0.0, 0.0):
            return None
        return (a, b, c, d, e, f)
    return None


def _uv_anim(cls: str, props: dict) -> Optional[dict]:
    if cls == "TexFriendlyPanner":
        # SCCT's panner: PanU / PanV per second times PanRate, after scaling UV by scaleU / scaleV.
        # Class defaults read from Engine.u: PanRate 0.1, scaleU / scaleV 1 (TexPanner PanRate 0.1 too).
        return {"type": "friendly_panner", "rate": float(props.get("PanRate", 0.1)),
                "pan_u": float(props.get("PanU", 0.0) or 0.0), "pan_v": float(props.get("PanV", 0.0) or 0.0),
                "scale_u": float(props.get("scaleU", 1.0) or 1.0), "scale_v": float(props.get("scaleV", 1.0) or 1.0)}
    if cls == "TexPanner":
        return {"type": "panner", "rate": float(props.get("PanRate", 0.1) or 0.0),
                "direction": list(props.get("PanDirection", (0, 0, 0)))}
    if cls == "TexOscillator":
        keys = ("UOscillationRate", "VOscillationRate", "UOscillationAmplitude", "VOscillationAmplitude",
                "UOscillationType", "VOscillationType", "UOffset", "VOffset", "UOscillationPhase", "VOscillationPhase")
        # Engine.u class defaults (maps store only what differs): rates 1, amplitudes 0.1.
        osc = {"UOscillationRate": 1.0, "VOscillationRate": 1.0, "UOscillationAmplitude": 0.1, "VOscillationAmplitude": 0.1}
        osc.update({k: props[k] for k in keys if k in props})
        return {"type": "oscillator", **osc}
    if cls == "TexRotator" and int(props.get("TexRotationType", 0) or 0) != 0:
        return {"type": "rotator", "rotation": list(props.get("Rotation", (0, 0, 0))),
                "rotation_type": int(props.get("TexRotationType", 0) or 0),
                "u_offset": float(props.get("UOffset", 0.0) or 0.0), "v_offset": float(props.get("VOffset", 0.0) or 0.0)}
    return None


def _copy_layers(result: ResolvedMaterial, inner: ResolvedMaterial) -> None:
    """A FinalBlend or modifier around a Shader keeps the Shader's separately moving layers (when it has none itself)."""
    if result.opacity_layer or result.opacity_anim or result.opacity_matrix:
        return
    if result.opacity is inner.opacity:
        result.opacity_anim, result.opacity_matrix = inner.opacity_anim, inner.opacity_matrix
        result.opacity_layer = inner.opacity_layer
    if result.spec_mask is inner.spec_mask and result.spec_mask_anim is None and result.spec_mask_matrix is None:
        result.spec_mask_anim, result.spec_mask_matrix = inner.spec_mask_anim, inner.spec_mask_matrix
    if result.env_texture is None:
        result.env_texture, result.env_key = inner.env_texture, inner.env_key


class MaterialResolver:
    def __init__(self, pset: PackageSet):
        self.pset = pset
        self.cache: Dict[object, ResolvedMaterial] = {}
        self.texture_cache: Dict[str, Optional[Texture]] = {}

    def _props(self, pkg: Package, e: Export) -> dict:
        r = pkg.export_reader(e)
        skip_state_frame(r, e.flags)
        try:
            return read_properties(r)
        except Exception:
            return {}

    def _is_env_map(self, pkg: Package, ref) -> bool:
        if not isinstance(ref, int) or ref == 0:
            return False
        _, e = self.pset.resolve(pkg, ref)
        return e is not None and e.class_name in ("TexEnvMap", "Cubemap")

    def texture_for(self, pkg: Package, e: Export) -> Optional[Texture]:
        key = f"{pkg.path.stem}.{e.name}"
        if key in self.texture_cache:
            return self.texture_cache[key]
        try:
            tx = read_texture(pkg, e)
        except Exception:
            tx = None
        self.texture_cache[key] = tx
        return tx

    def _anim_frames(self, pkg: Package, first: Texture) -> Optional[dict]:
        """Texture.AnimNext chain from `first` until it loops (or 64 frames), same-size decodable frames only. UE2
        steps one frame per MaxFrameRate tick; 0 / unset steps every rendered frame, taken here as 30 fps."""
        frames, seen, owner, tx = [first], set(), pkg, first
        while len(frames) < 64:
            ref = tx.props.get("AnimNext")
            if not isinstance(ref, int) or ref == 0:
                break
            owner, ne = self.pset.resolve(owner, ref)
            if owner is None or ne is None or ne.class_name != "Texture" or (str(owner.path), ne.index) in seen:
                break
            seen.add((str(owner.path), ne.index))
            tx = self.texture_for(owner, ne)
            if tx is None or tx is first or tx.image is None or tx.image.size != first.image.size:
                break
            frames.append(tx)
        if len(frames) < 2 or first.image is None:
            return None
        fps = float(first.props.get("MaxFrameRate") or first.props.get("MinFrameRate") or 30.0)
        return {"frames": frames, "fps": fps}

    def resolve(self, pkg: Package, ref: int, depth: int = 0) -> ResolvedMaterial:
        key = self.pset.import_path(pkg, ref)
        owner, e = self.pset.resolve(pkg, ref)
        # Cache by the object itself, not its dotted name: a package can hold a Shader and the Texture it wraps under
        # the same name (TXT_MAL.BSP.DOO_elevator01), and a name-keyed cache handed the Shader's unfinished entry
        # back as its own Diffuse (the elevator doors exported untextured).
        ckey = (str(owner.path), e.index) if owner is not None and e is not None else key
        if ckey in self.cache:
            return self.cache[ckey]
        result = ResolvedMaterial(key=key, texture=None)
        self.cache[ckey] = result  # guard against cycles
        if owner is None or e is None or depth > 12:
            return result
        cls = e.class_name
        if cls == "Texture" or cls == "Cubemap":
            if cls == "Texture":
                result.texture = self.texture_for(owner, e)
                # SCCT stores specular/heat data in DXT5 alpha, so only explicit flags mean "masked".
                if result.texture is not None:
                    tp = result.texture.props
                    # UE2: bMasked is the alpha test, bAlphaTexture alpha blending. Blended textures with a near-binary
                    # alpha (fences, foliage) stay masked here, which looks the same and keeps depth; soft ones (lamp
                    # and window light beams, glass, sky clouds) blend, since a 0.5 alpha test removed them entirely.
                    if tp.get("bMasked"):
                        result.masked = True
                    elif tp.get("bAlphaTexture"):
                        if _soft_alpha(result.texture):
                            result.translucent = True
                        else:
                            result.masked = True
                    # UE2 textures carry their own two-sided flag (window grids, fences, girders, handrails):
                    # ignoring it back-face culled whole Main Hall structures.
                    result.two_sided = bool(tp.get("bTwoSided"))
                    if tp.get("AnimNext"):
                        result.flipbook = self._anim_frames(owner, result.texture)
            return result
        props = self._props(owner, e)
        if cls == "Shader":
            # UE2 EOutputBlending: 0 Normal, 1 Masked, 2 Modulate, 3 Translucent, 4 Invisible, 5 Brighten, 6 Darken
            ob = int(props.get("OutputBlending", 0) or 0)
            result.two_sided = bool(props.get("TwoSided"))
            result.masked = ob == 1
            result.translucent = ob == 3
            result.invisible = ob == 4
            result.additive = ob == 5
            result.darken = ob == 6
            result.modulate = "2x" if ob == 2 else ("darken" if ob == 6 else "")
            if props.get("Opacity"):
                result.opacity_key = self.pset.import_path(owner, int(props["Opacity"]))
                op = self.resolve(owner, int(props["Opacity"]), depth + 1)
                if op.texture is not None:
                    result.opacity = op.texture
                    result.opacity_anim, result.opacity_matrix = op.uv_anim, op.uv_matrix
                    # OB_Normal with an Opacity texture is alpha blended (it was exported opaque: AquaE's fog sheet
                    # covered a wall, glass and water drew as solid slabs).
                    if ob == 0:
                        result.translucent = True
            elif ob == 3:
                # OB_Translucent without an Opacity texture is D3D (ONE, INVSRCCOLOR), a screen blend: black adds nothing
                # and the texture brightens what is behind it. Drawn as additive (godrays, light hotspots, rain), not as
                # an alpha-blended slab through the texture's (usually opaque) alpha.
                result.translucent = False
                result.additive = True
            # Self-illuminated shaders (lamps, neon, screens) are drawn at full brightness by the game; with a
            # SelfIlluminationMask only the mask's alpha is fullbright.
            if self._is_env_map(owner, props.get("Specular")):
                result.env_map = True
                env = self.resolve(owner, int(props["Specular"]), depth + 1)
                if env.texture is not None and env.texture.image is not None:
                    import numpy as _np
                    px = _np.asarray(env.texture.image.convert("RGB"), dtype=_np.float32) / 255.0
                    result.env_color = tuple(float(c) for c in px.reshape(-1, 3).mean(0))
                    result.env_texture, result.env_key = env.texture, env.key
                if props.get("SpecularityMask"):
                    sm = self.resolve(owner, int(props["SpecularityMask"]), depth + 1)
                    if sm.texture is not None:
                        result.spec_mask, result.spec_mask_key = sm.texture, sm.key
                        result.spec_mask_anim, result.spec_mask_matrix = sm.uv_anim, sm.uv_matrix
            if props.get("SelfIllumination"):
                result.unlit = True
                si = self.resolve(owner, int(props["SelfIllumination"]), depth + 1)
                if si.texture is not None:
                    result.self_illum, result.self_illum_key = si.texture, si.key
                if props.get("SelfIlluminationMask"):
                    sm = self.resolve(owner, int(props["SelfIlluminationMask"]), depth + 1)
                    if sm.texture is not None:
                        result.self_illum_mask, result.self_illum_mask_key = sm.texture, sm.key
            elif ob == 5 and props.get("Diffuse") and self._is_env_map(owner, props.get("Specular")):
                # Glass: lit diffuse plus an environment reflection, brightening what is behind it. Glow and FX
                # shaders (light rays, holograms, lens flares) stay fullbright; not on bUnlit actors either (their
                # __unlit variant, export_gltf.py).
                result.additive_gain = LIT_BRIGHTEN_GAIN
            elif props.get("Specular") and not props.get("SpecularityMask"):
                # An unmasked Specular is added on top of the lit diffuse at full strength. A plain texture there
                # (Bunker.whiteglow_1: Diffuse = Specular = White_0, the MissE corridor lamp's glow card) reads as
                # fullbright in the game; cube maps and env-map modifiers (reflections) stay lit.
                _, spec_e = self.pset.resolve(owner, int(props["Specular"]))
                if spec_e is not None and spec_e.class_name == "Texture":
                    result.unlit = True
        elif cls == "FinalBlend":
            # UE2 EFrameBufferBlending: 0 Overwrite, 1 Modulate, 2 AlphaBlend, 3 AlphaModulate, 4 Translucent, 5 Darken, 6 Brighten, 7 Invisible
            result.two_sided = bool(props.get("TwoSided"))
            fb = int(props.get("FrameBufferBlending", 0) or 0)
            # FB_Translucent (4) is the screen blend (ONE, INVSRCCOLOR): additive, like OB_Translucent above.
            result.translucent = fb in (2, 3)
            result.invisible = fb == 7
            result.additive = fb in (4, 6)
            result.darken = fb == 5
            result.modulate = "2x" if fb == 1 else ("darken" if fb == 5 else "")
            result.masked = bool(props.get("AlphaTest"))
        if cls == "Combiner":
            # UE2 ECombineOperation / EAlphaOperation; the inputs are resolved to textures and the combination is
            # baked on export (TV screens, hack FX, laser glows, sand and wall blends).
            def _tex(pname):
                ref = props.get(pname)
                if isinstance(ref, int) and ref != 0:
                    return self.resolve(owner, ref, depth + 1).texture
                return None
            # UCombiner::RequiresSorting: either input alpha-blended (bAlphaTexture) sorts the whole Combiner, which
            # then blends with its combined alpha (EAlphaOperation). AquaE/D/T's sea (osc -> comb: FX_sea, FX_onde
            # with bAlphaTexture) is dark water showing only the FX_sea sparkle alpha; drawn opaque it was flat grey.
            for pname in ("Material1", "Material2"):
                tx = _tex(pname)
                if tx is not None and tx.props.get("bAlphaTexture"):
                    result.translucent = True
            # Each input's own texture modifiers (the sea's two TexPanners), lost in the t=0 bake: exported with the
            # separate inputs so the importer can animate them ("combine" flags, export_gltf.py).
            def _anim(pname):
                ref = props.get(pname)
                if isinstance(ref, int) and ref != 0:
                    r = self.resolve(owner, ref, depth + 1)
                    return r.uv_anim, r.uv_matrix
                return None, None
            anims = {k: _anim(p) for k, p in (("1", "Material1"), ("2", "Material2"), ("mask", "Mask"))}
            # Material2 an env map (TexEnvMap): looked up by the reflection, not the mesh UVs, and it may play an
            # AnimNext chain (ClarD / Sche* puddles: final_flaque_envmap, rain ripples flaque_onde0000..). Baked
            # through the UVs it stretched one ripple frame over the puddle as a large bright blot.
            env2 = self._is_env_map(owner, props.get("Material2"))
            flip2 = None
            if env2:
                r2 = self.resolve(owner, int(props["Material2"]), depth + 1)
                flip2 = r2.flipbook
            result.combine = {"anims": anims, "env2": env2, "flip2": flip2,
                              "op": int(props.get("CombineOperation", 0) or 0),
                              "alpha_op": int(props.get("AlphaOperation", 0) or 0),
                              "mod": 4 if props.get("Modulate4X") else (2 if props.get("Modulate2X") else 1),
                              "tex2": _tex("Material2"), "mask": _tex("Mask"),
                              "invert_mask": bool(props.get("InvertMask"))}
        result.uv_matrix = _static_uv_matrix(cls, props)
        result.uv_anim = _uv_anim(cls, props)
        for pname in _CHAIN_PROPS.get(cls, ("Material", "Diffuse")):
            sub = props.get(pname)
            if isinstance(sub, int) and sub != 0:
                inner = self.resolve(owner, sub, depth + 1)
                if inner.texture is not None:
                    result.texture = inner.texture
                    result.flipbook = inner.flipbook
                    result.two_sided = result.two_sided or inner.two_sided
                    result.unlit = result.unlit or inner.unlit
                    if result.self_illum is None and inner.self_illum is not None:
                        result.self_illum, result.self_illum_key = inner.self_illum, inner.self_illum_key
                        result.self_illum_mask, result.self_illum_mask_key = inner.self_illum_mask, inner.self_illum_mask_key
                        result.self_illum_anim = inner.self_illum_anim or result.uv_anim is not None
                    if result.uv_matrix is None:
                        result.uv_matrix = inner.uv_matrix   # one static modifier per chain (nesting is not composed)
                    if result.uv_anim is None:
                        result.uv_anim = inner.uv_anim
                    if result.combine is None and getattr(inner, "combine", None):
                        # A FinalBlend / Shader / modifier around a Combiner keeps the Combiner's bake.
                        result.combine = inner.combine
                    if cls == "FinalBlend":
                        # FinalBlend only sets the frame-buffer blend: the wrapped Shader's specular, env map,
                        # opacity and self-illumination still apply.
                        result.env_map = result.env_map or inner.env_map
                        if result.env_color is None:
                            result.env_color = inner.env_color
                        if result.spec_mask is None and inner.spec_mask is not None:
                            result.spec_mask, result.spec_mask_key = inner.spec_mask, inner.spec_mask_key
                        if result.opacity is None and inner.opacity is not None:
                            result.opacity, result.opacity_key = inner.opacity, getattr(inner, "opacity_key", "")
                        _copy_layers(result, inner)
                    if cls not in ("Shader", "FinalBlend"):
                        result.translucent = result.translucent or inner.translucent
                        result.masked = result.masked or inner.masked
                        result.invisible = result.invisible or inner.invisible
                        result.additive = result.additive or inner.additive
                        result.additive_gain = min(result.additive_gain, inner.additive_gain)
                        result.darken = result.darken or inner.darken
                        result.modulate = result.modulate or inner.modulate
                        result.env_map = result.env_map or inner.env_map
                        if result.env_color is None:
                            result.env_color = inner.env_color
                        if result.spec_mask is None and inner.spec_mask is not None:
                            result.spec_mask, result.spec_mask_key = inner.spec_mask, inner.spec_mask_key
                        if result.opacity is None:
                            result.opacity = inner.opacity   # a panner/scaler around an opacity Shader keeps its alpha
                        _copy_layers(result, inner)
                    break
        if cls == "Shader" and result.opacity is not None:
            # The Opacity moves on its own (another panner or rate than the Diffuse): a separate layer, not a bake.
            result.opacity_layer = (result.opacity_anim, result.opacity_matrix) != (result.uv_anim, result.uv_matrix)
        if cls == "Combiner" and result.combine and (result.combine.get("env2")
                                                      or any(a or m for a, m in result.combine["anims"].values())):
            # The inputs carry their own modifiers (exported per input): Material1's must not also apply to the whole
            # Combiner, or it would move twice.
            result.uv_anim, result.uv_matrix = None, None
        if result.texture is None and cls == "MaterialSequence":
            # Animated screens (security-room feeds, monitors, blinking mines) have no FallbackMaterial: show the
            # sequence's first frame.
            for item in props.get("SequenceItems") or []:
                sub = item.get("Material") if isinstance(item, dict) else None
                if isinstance(sub, int) and sub != 0:
                    inner = self.resolve(owner, sub, depth + 1)
                    if inner.texture is not None:
                        result.texture, result.unlit = inner.texture, inner.unlit
                        result.two_sided = result.two_sided or inner.two_sided
                        result.translucent = result.translucent or inner.translucent
                        result.masked = result.masked or inner.masked
                        break
        if result.texture is None and result.self_illum is not None:
            # A Shader with only SelfIllumination (screens, neon, EMF/heat displays) draws that texture fullbright;
            # without this it exported untextured (flat white in UE5).
            result.texture = result.self_illum
            result.unlit = True
        if result.texture is None and cls == "ConstantColor" and isinstance(props.get("Color"), (tuple, list)):
            result.color = tuple(int(c) for c in props["Color"][:4])   # (R, G, B, A) 0-255
        return result
