"""Stage every fitted skin and render both sides on the actual mesh without installing it.

python tools/assets/preview_fitted_skins.py --workers 4
Outputs an HTML index, model contact sheets, and a hash-bound build manifest.
"""
import argparse
from concurrent.futures import ProcessPoolExecutor, as_completed
import hashlib
import html
import json
import os
import shutil
from datetime import datetime, timezone
from pathlib import Path
import sys

os.environ['OPENBLAS_NUM_THREADS'] = '1'
os.environ['OMP_NUM_THREADS'] = '1'
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'content'))
from svmbuild import cosmetics, gunskin, skeleton
from svmbuild.fitted import SurfaceMesh, ARMS
from svmbuild.sidearms import IDS, FINISHES, SOURCE


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def mask_group(group):
    from svmbuild.fitted import load_mesh, coverage
    job=group[0];src,dst,pattern,stem,size,path=job
    stat=Path(path).stat();mesh=load_mesh(path,stem.startswith('OpenScctSidearms_'),stat.st_mtime_ns,stat.st_size)
    with Image.open(src) as image:
        retail=np.array(image.convert('RGBA').resize(size,Image.Resampling.LANCZOS))
    mask=coverage(retail,mesh,stem,pattern,lighting=True)
    image=Image.fromarray(np.clip(mask*255+.5,0,255).astype(np.uint8))
    for job in group:image.save(Path(job[1]).with_suffix('.mask.png'))


def write_masks(jobs,workers):
    groups={}
    for job in jobs:
        key=(job[0],job[3],tuple(job[4]),job[5],job[2]=='skeleton')
        groups.setdefault(key,[]).append(job)
    with ProcessPoolExecutor(max_workers=workers) as pool:
        for _ in pool.map(mask_group,groups.values()):pass
    print(f'Wrote {len(jobs)} finish-lighting masks',flush=True)


def apply_reviewed(stage, chars):
    """Install only the validated texture outputs, with a restorable backup."""
    manifest=json.loads((stage/'manifest.json').read_text())
    audit=json.loads((stage/'coverage-audit.json').read_text())
    if audit['manifest_sha256'] != digest(stage/'manifest.json') or audit['failures']:
        raise ValueError('The staged outputs need a passing coverage audit')
    if manifest['cap'] != 0:raise ValueError('Preview-resolution textures cannot be installed')
    for source,sha in manifest['sources'].items():
        if digest(source)!=sha:raise ValueError(f'Source changed since preview: {source}')
    for model in manifest['models'].values():
        destination=chars/model['package']/Path(model['path']).name
        if not destination.exists() or digest(destination)!=digest(model['path']):
            raise ValueError(f'Installed geometry differs from preview: {destination}')
    copies=[]
    for relative,sha in manifest['outputs'].items():
        source=(stage/relative).resolve();destination=(chars/relative).resolve()
        if not source.is_relative_to(stage) or not destination.is_relative_to(chars.resolve()):
            raise ValueError(f'Unsafe output path: {relative}')
        if not source.name.startswith('cosmetic_') or source.suffix!='.png':raise ValueError(relative)
        if digest(source)!=sha:raise ValueError(f'Output changed since preview: {relative}')
        copies.append((source,destination,relative))
    backup=stage/('backup-'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ'))
    backup.mkdir()
    created=[]
    for source,destination,relative in copies:
        if destination.exists():
            old=backup/relative;old.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(destination,old)
        else:created.append(relative)
    (backup/'new-files.json').write_text(json.dumps(created,indent=2))
    for source,destination,relative in copies:
        destination.parent.mkdir(parents=True,exist_ok=True)
        temporary=destination.with_suffix('.installing')
        shutil.copy2(source,temporary);temporary.replace(destination)
    (stage/'installed.json').write_text(json.dumps({'files':len(copies),'backup':str(backup)},indent=2))
    print(f'Installed {len(copies)} texture/mask files; backup: {backup}')


def audit_coverage(stage):
    from svmbuild.fitted import secondary_parts, _LOWER_FRAME, sidearm
    manifest=json.loads((stage/'manifest.json').read_text());results=[];failures=[]
    for key,item in manifest['models'].items():
        authored=item['package']=='OpenScctSidearms'
        mesh=SurfaceMesh(item['path']) if authored else skeleton.Mesh(Path(item['path']))
        layouts={stem:skeleton.bake(mesh,stem,128,128) for stem in item['textures']}
        originals={stem:np.array(Image.open(source).convert('RGB').resize((128,128),Image.Resampling.LANCZOS)).astype(int)
                   for stem,source in item['textures'].items()}
        guns={}
        for stem in item['textures']:
            if sidearm(stem) or stem in _LOWER_FRAME:
                _,forward,up,sleeves=gunskin.GUN_MESH.get(stem,('',gunskin._X,gunskin._Y,False))
                guns[stem]=gunskin._fitted_gun(mesh,stem,128,128,forward,up,sleeves)
        for pattern in item['patterns']:
            counts={}
            for stem,source in item['textures'].items():
                if pattern not in item['texture_patterns'][stem]:continue
                size=(128,128);layout=layouts[stem]
                base=originals[stem]
                output=stage/item['package']/'textures'/f'cosmetic_{pattern}_{stem}.png'
                painted=np.array(Image.open(output).convert('RGB').resize(size,Image.Resampling.LANCZOS)).astype(int)
                changed=np.max(abs(painted-base),axis=2)>2
                regions={'surface':layout['cover']}
                if key=='SPerso/ATT_01':
                    parts=secondary_parts(mesh,layout)
                    regions.update(head=parts[skeleton.HEAD],boots=parts[skeleton.FOOT_L]|parts[skeleton.FOOT_R])
                elif sidearm(stem) or stem in _LOWER_FRAME:
                    p=layout['P'];cover=layout['cover']
                    if sidearm(stem):
                        grip=cover&(p[...,1]<-.025)&(p[...,0]<mesh.pos[:,0].min()+np.ptp(mesh.pos[:,0])*.40)
                    else:
                        grip=cover&(p[...,1]<mesh.pos[:,1].min()+np.ptp(mesh.pos[:,1])*_LOWER_FRAME[stem])
                    regions['grip']=grip
                    gun=guns[stem]
                    small=cover&(gun.role==gunskin.SMALL)
                    if small.sum()>=4:regions['small_parts']=small
                for name,region in regions.items():
                    count=int(region.sum())
                    if count:
                        old=counts.get(name,[0,0]);counts[name]=[old[0]+int((changed&region).sum()),old[1]+count]
            ratios={name:round(a/b,4) for name,(a,b) in counts.items()}
            results.append({'model':key,'pattern':pattern,'changed_fraction':ratios})
            for name,ratio in ratios.items():
                if ratio<.10:failures.append(f'{key}/{pattern}: {name} only {ratio:.1%} changed')
    report=dict(manifest_sha256=digest(stage/'manifest.json'),checked=len(results),failures=failures,results=results)
    (stage/'coverage-audit.json').write_text(json.dumps(report,indent=2))
    if failures:raise ValueError('\n'.join(failures))
    print(f'Coverage audit passed: {len(results)} model/finish combinations',flush=True)


def inventory(chars, stage, cap):
    models, jobs = {}, []
    def add(package, stem, patterns, path, source):
        key = package + '/' + path.stem
        item = models.setdefault(key, dict(path=str(path), package=package, textures={}, texture_patterns={}, patterns=[]))
        item['textures'][stem] = str(source)
        item['texture_patterns'][stem] = list(patterns)
        item['patterns'] = sorted(set(item['patterns']) | set(patterns))
        with Image.open(source) as im:
            original_size = im.size
        for pattern in patterns:
            size = ((2048,2048) if stem == ARMS else (1024,1024)) if package == 'OpenScctSidearms' else cosmetics.output_size(package, pattern, *original_size)
            if cap and max(size) > cap:
                size = tuple(max(1, round(x * cap / max(size))) for x in size)
            dest = stage / package / 'textures' / f'cosmetic_{pattern}_{stem}.png'
            dest.parent.mkdir(parents=True, exist_ok=True)
            jobs.append((str(source), str(dest), pattern, stem, size, str(path)))
    for package, stem, patterns in cosmetics.TARGETS:
        name = gunskin.GUN_MESH[stem][0] if stem in gunskin.GUN_MESH else cosmetics.SKELETON_MESH[stem]
        add(package, stem, patterns, chars / package / name, chars / package / 'textures' / (stem + '.png'))
    for name in (*IDS, 'arms_base'):
        path = SOURCE / (name + '.glb')
        mesh = SurfaceMesh(path)
        for stem in mesh.tris:
            add('OpenScctSidearms', stem, cosmetics._MERC_SUITS if stem == ARMS else FINISHES,
                path, SOURCE / 'textures' / (stem + '.png'))
    return models, jobs


def projection(mesh, width, height, character, reverse=False):
    """Rasterize actual triangles once to a UV/depth buffer, then reuse for every skin."""
    if character:
        right = np.array([1., 0, .28])
        up = np.array([0, 1., 0])
    else:
        right = np.array([1., 0, .22])
        up = np.array([0, 1., .14])
    right /= np.linalg.norm(right)
    up -= right * (right @ up); up /= np.linalg.norm(up)
    view = np.cross(right, up)
    if reverse:
        right *= -1; view *= -1
    p = np.stack([mesh.pos @ right, mesh.pos @ up, mesh.pos @ view], -1)
    extent = np.ptp(p[:, :2], axis=0)
    scale = min((width - 18) / max(extent[0], .001), (height - 18) / max(extent[1], .001))
    p[:, :2] = (p[:, :2] - (p[:, :2].max(0) + p[:, :2].min(0)) / 2) * scale
    p[:, 0] += width / 2; p[:, 1] = height / 2 - p[:, 1]
    depth = np.full((height, width), -np.inf)
    uv = np.zeros((height, width, 2), np.float32)
    material = np.full((height, width), -1, int)
    shade = np.ones((height, width), np.float32)
    light = view + np.array([-.35, .7, .1]); light /= np.linalg.norm(light)
    for mi, tris in enumerate(mesh.tris.values()):
        for a, b, c in np.concatenate(tris):
            tri = p[[a, b, c]]
            x0, y0 = np.maximum(np.floor(tri[:, :2].min(0)).astype(int), 0)
            x1, y1 = np.minimum(np.ceil(tri[:, :2].max(0)).astype(int), [width - 1, height - 1])
            if x1 < x0 or y1 < y0: continue
            aa, bb, cc = tri
            den = (bb[1]-cc[1])*(aa[0]-cc[0])+(cc[0]-bb[0])*(aa[1]-cc[1])
            if abs(den) < 1e-9: continue
            yy, xx = np.mgrid[y0:y1+1, x0:x1+1]
            wa = ((bb[1]-cc[1])*(xx+.5-cc[0])+(cc[0]-bb[0])*(yy+.5-cc[1]))/den
            wb = ((cc[1]-aa[1])*(xx+.5-cc[0])+(aa[0]-cc[0])*(yy+.5-cc[1]))/den
            wc = 1-wa-wb
            z = wa*aa[2]+wb*bb[2]+wc*cc[2]
            region = np.s_[y0:y1+1,x0:x1+1]
            take = (wa >= 0)&(wb >= 0)&(wc >= 0)&(z > depth[region])
            depth[region][take] = z[take]
            material[region][take] = mi
            interp = wa[...,None]*mesh.uv[a]+wb[...,None]*mesh.uv[b]+wc[...,None]*mesh.uv[c]
            uv[region][take] = interp[take]
            n = wa[...,None]*mesh.nrm[a]+wb[...,None]*mesh.nrm[b]+wc[...,None]*mesh.nrm[c]
            shade[region][take] = (.65+.35*np.clip(n @ light, 0, 1))[take]
    return uv, material, shade


def render(models, stage):
    links = []
    for key, item in models.items():
        authored = item['package'] == 'OpenScctSidearms'
        mesh = SurfaceMesh(item['path']) if authored else skeleton.Mesh(Path(item['path']))
        character = item['package'] == 'SPerso'
        width, height = (240, 320) if character else (320, 180)
        buffers = [projection(mesh, width, height, character, r) for r in (False, True)]
        patterns = ['original', *item['patterns']]
        columns = 3
        sheet = Image.new('RGB', (columns*width*2, ((len(patterns)+columns-1)//columns)*(height+26)), '#151c27')
        draw = ImageDraw.Draw(sheet)
        for k, pattern in enumerate(patterns):
            for side, (uv, mat, shade) in enumerate(buffers):
                rgb = np.full((height, width, 3), [21, 28, 39], np.uint8)
                for mi, stem in enumerate(mesh.tris):
                    base = item['textures'].get(stem)
                    if base is None:
                        base = str(Path(item['path']).parent / 'textures' / (stem+'.png'))
                    tex = stage/item['package']/'textures'/f'cosmetic_{pattern}_{stem}.png'
                    if pattern == 'original' or pattern not in item['texture_patterns'].get(stem, []): tex = Path(base)
                    if not tex.exists(): raise ValueError(f'Missing preview material {tex}')
                    pixels = np.array(Image.open(tex).convert('RGB'))
                    take = mat == mi
                    coords = uv[take] % 1
                    x = np.minimum((coords[:,0]*pixels.shape[1]).astype(int), pixels.shape[1]-1)
                    y = np.minimum((coords[:,1]*pixels.shape[0]).astype(int), pixels.shape[0]-1)
                    rgb[take] = (pixels[y,x]*shade[take,None]).clip(0,255).astype(np.uint8)
                ox = (k%columns)*width*2+side*width; oy=(k//columns)*(height+26)
                sheet.paste(Image.fromarray(rgb), (ox,oy))
            draw.text(((k%columns)*width*2+8,(k//columns)*(height+26)+height+5), pattern, fill='white')
        name = key.replace('/', '-')+'.jpg'
        sheet.save(stage/name, quality=92)
        # Readable review pages as well as the complete per-model contact sheet.
        page_height=4*(height+26)
        for page,top in enumerate(range(0,sheet.height,page_height),1):
            sheet.crop((0,top,sheet.width,min(top+page_height,sheet.height))).save(stage/(key.replace('/','-')+f'-page{page}.jpg'),quality=94)
        links.append(f'<h2>{html.escape(key)}</h2><a href="{name}"><img style="width:100%" src="{name}"></a>')
        print('Rendered', key, flush=True)
    (stage/'index.html').write_text('<!doctype html><meta charset="utf-8"><title>Fitted skins review</title><body style="background:#151c27;color:white;font:16px sans-serif"><h1>Fitted skins — actual mesh previews</h1><p>Each finish: both sides. Original first. Orthographic diffuse preview; game lighting and animation require separate checks.</p>'+''.join(links), encoding='utf-8')
    runtime_gallery(stage)


def runtime_gallery(stage):
    screenshots=sorted((stage/'runtime').glob('fitted-skins-*.png'))
    if not screenshots:return
    body=''.join(f'<a href="{p.name}"><img loading="lazy" style="width:100%;max-width:1280px" src="{p.name}"></a>' for p in screenshots)
    (stage/'runtime/index.html').write_text('<!doctype html><meta charset="utf-8"><title>Godot fitted skin previews</title><body style="background:#10141b;color:white;font:16px sans-serif"><h1>Every wearable unlock in Godot</h1><p>Actual Armoury models and finish shaders. Item IDs appear under each model.</p>'+body,encoding='utf-8')
    index=stage/'index.html';text=index.read_text(encoding='utf-8')
    if 'runtime/index.html' not in text:
        index.write_text(text.replace('</h1>','</h1><p><a style="color:#7bdcff" href="runtime/index.html">Open the in-engine preview gallery</a></p>',1),encoding='utf-8')


def main():
    ap=argparse.ArgumentParser();ap.add_argument('--workers', type=int, default=4)
    ap.add_argument('--cap', type=int, default=0);ap.add_argument('--render-only', action='store_true')
    ap.add_argument('--pattern-prefix', default='')
    ap.add_argument('--masks-only', action='store_true')
    ap.add_argument('--package', default='')
    ap.add_argument('--apply-reviewed', action='store_true')
    ap.add_argument('--audit-only', action='store_true')
    ap.add_argument('--refinish-parts', action='store_true')
    ap.add_argument('--output', type=Path, default=ROOT/'local/fitted-skins-review')
    args=ap.parse_args();stage=args.output.resolve();stage.mkdir(parents=True,exist_ok=True)
    if args.apply_reviewed:
        apply_reviewed(stage,ROOT/'.content/shared/chars')
        return
    if args.audit_only:
        audit_coverage(stage)
        return
    models,jobs=inventory(ROOT/'.content/shared/chars',stage,args.cap)
    source_hashes={p:digest(p) for j in jobs for p in (j[0],j[5])}
    if args.masks_only:
        write_masks(jobs,args.workers)
    elif not args.render_only:
        selected=[j for j in jobs if j[2].startswith(args.pattern_prefix)
                  and (not args.package or Path(j[1]).parent.parent.name == args.package)]
        print(f'Staging {len(selected)} of {len(jobs)} textures for {len(models)} models',flush=True)
        with ProcessPoolExecutor(max_workers=args.workers) as pool:
            from svmbuild.fitted import refinish_parts
            bake=refinish_parts if args.refinish_parts else cosmetics._paint_job
            futures=[pool.submit(bake,j) for j in selected]
            for i,f in enumerate(as_completed(futures),1):
                f.result()
                if i%10==0:print(f'{i}/{len(selected)} textures',flush=True)
    render(models,stage)
    sources={p:digest(p) for j in jobs for p in (j[0],j[5])}
    if sources!=source_hashes:raise ValueError('Source assets changed during preview; rebuild before installation')
    outputs={}
    for j in jobs:
        for path in (Path(j[1]),Path(j[1]).with_suffix('.mask.png')):
            if not path.exists():raise ValueError(f'Missing staged output: {path}')
            with Image.open(path) as image:
                if image.size != tuple(j[4]):raise ValueError(f'Wrong output size: {path}')
            outputs[str(path.relative_to(stage))]=digest(path)
    (stage/'manifest.json').write_text(json.dumps(dict(models=models,jobs=jobs,sources=sources,outputs=outputs,cap=args.cap),indent=2))
    audit_coverage(stage)


if __name__=='__main__':main()
