"""Acquire credited scenery into the Hub's reproducible source-asset cache.

Run explicitly when updating assets; normal map generation never uses the network.
Forest models are pinned to the asset-library revision. Poly Haven downloads are recorded with SHA-256.
Only model data and images are read. No upstream scripts or addons are installed.
"""
from pathlib import Path
import concurrent.futures, hashlib, io, json, struct, urllib.request
from PIL import Image

HERE = Path(__file__).resolve().parent
OUT = HERE.parents[1] / "fixtures" / "hub-assets"
CACHE = HERE.parents[1] / "local" / "hub-asset-downloads"
FOREST = "https://raw.githubusercontent.com/GamesNotDeveloped/godot-forest-demo/623212ffeb4efbf4c95323a4a6a57e726be4f6e9/"

def fetch(url):
    name = CACHE / hashlib.sha256(url.encode()).hexdigest()
    if not name.exists():
        name.parent.mkdir(parents=True, exist_ok=True)
        request=urllib.request.Request(url,headers={'User-Agent':'OpenSCCT-Hub-Asset-Build/1.0'})
        with urllib.request.urlopen(request, timeout=90) as response: name.write_bytes(response.read())
    return name.read_bytes()

def pack(gltf_url, folder_url, name):
    doc = json.loads(fetch(gltf_url)); data = bytearray(); offsets=[]
    def append(raw):
        while len(data)%4:data.append(0)
        offset=len(data);data.extend(raw);return offset
    for b in doc['buffers']: offsets.append(append(fetch(folder_url+b['uri'])))
    for view in doc['bufferViews']:
        view['byteOffset']=view.get('byteOffset',0)+offsets[view['buffer']];view['buffer']=0
    for img in doc.get('images',[]):
        if 'uri' not in img:continue
        image=Image.open(io.BytesIO(fetch(folder_url+img.pop('uri'))));image.thumbnail((1024,1024),Image.Resampling.LANCZOS)
        alpha=image.getchannel('A').getextrema() if 'A' in image.getbands() else (255,255)
        opaque=alpha==(255,255)
        raw=io.BytesIO()
        if opaque:image.convert('RGB').save(raw,format='JPEG',quality=90,optimize=True)
        else:image.save(raw,format='PNG',optimize=True)
        payload=raw.getvalue()
        doc['bufferViews'].append({'buffer':0,'byteOffset':append(payload),'byteLength':len(payload)})
        img.update(bufferView=len(doc['bufferViews'])-1,mimeType='image/jpeg' if opaque else 'image/png')
    doc.pop('animations',None)
    doc['buffers']=[{'byteLength':len(data)}]
    raw=json.dumps(doc,separators=(',',':')).encode();raw+=b' '*((-len(raw))%4);data+=b'\0'*((-len(data))%4)
    payload=struct.pack('<4sII',b'glTF',2,12+8+len(raw)+8+len(data))+struct.pack('<I4s',len(raw),b'JSON')+raw+struct.pack('<I4s',len(data),b'BIN\0')+data
    (OUT/(name+'.glb')).write_bytes(payload)
    return {'asset':name,'source':gltf_url,'sha256':hashlib.sha256(payload).hexdigest(),'changes':'Embedded materials and textures; textures capped at 1024px; unused animations removed.'}

def forest(name):
    url=FOREST+'objects/'+name+'/'
    record=pack(url+'scene.gltf',url,name)
    (OUT/(name+'.license.txt')).write_bytes(fetch(url+'license.txt'))
    record['license']='CC-BY-4.0';return record

def poly_model(name):
    files=json.loads(fetch('https://api.polyhaven.com/files/'+name));gltf=files['gltf']['1k']['gltf'];url=gltf['url']
    # glTF references texture/bin paths relative to its folder; API includes their actual download locations.
    doc=json.loads(fetch(url))
    for rel,info in gltf['include'].items():
        source=url.rsplit('/',1)[0]+'/'+rel
        (CACHE/hashlib.sha256(source.encode()).hexdigest()).write_bytes(fetch(info['url']))
    record=pack(url,url.rsplit('/',1)[0]+'/',name);record['license']='CC0-1.0';return record

def texture(name):
    files=json.loads(fetch('https://api.polyhaven.com/files/'+name));info=files['Diffuse']['1k']['jpg'];payload=fetch(info['url'])
    (OUT/(name+'.jpg')).write_bytes(payload)
    return {'asset':name,'source':info['url'],'license':'CC0-1.0','sha256':hashlib.sha256(payload).hexdigest()}

def grass_texture():
    url=FOREST+'grass/grassbushcc008.png';payload=fetch(url)
    (OUT/'grassbushcc008.png').write_bytes(payload)
    return {'asset':'grassbushcc008','source':url,'license':'MIT',
            'author':'IcterusGames / SimpleGrassTextured', 'sha256':hashlib.sha256(payload).hexdigest()}

if __name__=='__main__':
    OUT.mkdir(parents=True,exist_ok=True);CACHE.mkdir(parents=True,exist_ok=True)
    jobs=[(forest,n) for n in ('pine-tree-1','pine-tree-2','birch-tree-1','flower-1','old-house','old-well')]
    jobs += [(poly_model,n) for n in ('painted_wooden_bench','boulder_01','old_military_crate','wooden_picnic_table','Barrel_01','Sofa_01','CoffeeTable_01','street_lamp_01','fern_02')]
    jobs += [(texture,n) for n in ('grass_ground','leafy_grass','mossy_stone_wall','castle_brick_broken_06','snow_02','weathered_planks','concrete_floor_worn_02','grassy_cobblestone')]
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        records=list(pool.map(lambda job:job[0](job[1]),jobs))
    records.append(grass_texture())
    sky=json.loads(fetch('https://api.polyhaven.com/files/autumn_field_puresky'))['hdri']['2k']['hdr']
    payload=fetch(sky['url']);(OUT/'autumn_field_puresky.hdr').write_bytes(payload)
    records.append({'asset':'autumn_field_puresky','source':sky['url'],'license':'CC0-1.0','sha256':hashlib.sha256(payload).hexdigest()})
    (OUT/'sources.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf8')
    (OUT/'CC-BY-4.0.txt').write_bytes(fetch(FOREST+'LICENSE'))
    credits=['OPENSCCT EXPANDED HUB — SCENERY CREDITS','',
             'Forest assets selected from GamesNotDeveloped, Godot Procedural Forest Demo:',
             'https://github.com/GamesNotDeveloped/godot-forest-demo/tree/623212ffeb4efbf4c95323a4a6a57e726be4f6e9',
             'Pack adaptations: CC BY 4.0. Original model authors and licenses follow.',
             'OpenSCCT changes: embedded model data; textures capped at 1024 px, opaque images JPEG-compressed; animations removed; authored placement and scale; materials adapted to the display-space renderer; simplified physical meshes and traversal outlines derived from the models.',
             'No upstream music, addons or runtime scripts included.','']
    for license in sorted(OUT.glob('*.license.txt')):credits.extend([license.read_text(), ''])
    credits += ['GRASS TEXTURE — IcterusGames / SimpleGrassTextured (MIT)',
                'https://github.com/IcterusGames/SimpleGrassTextured',
                'Selected from the pinned forest demo grass/grassbushcc008.png.',
                'See SimpleGrassTextured-MIT.txt. Texture unchanged; original OpenSCCT mesh, wind and batch renderer.', '']
    credits+=['POLY HAVEN — CC0 1.0','https://polyhaven.com/license','https://creativecommons.org/publicdomain/zero/1.0/','']
    for record in records:
        if record['license']!='CC0-1.0':continue
        info=json.loads(fetch('https://api.polyhaven.com/info/'+record['asset']))
        credits.extend([info['name']+' — '+', '.join(info.get('authors',{})), 'https://polyhaven.com/a/'+record['asset']])
    (OUT/'CREDITS.txt').write_text('\n'.join(credits)+'\n',encoding='utf8')
    from prepare_collision import prepare
    prepare()
    print('Prepared',len(records),'credited assets;',sum(p.stat().st_size for p in OUT.iterdir()),'bytes')
