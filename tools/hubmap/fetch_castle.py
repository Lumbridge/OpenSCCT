"""Fetch pinned CC0 Kenney castle modules from the public distribution mirror."""
import urllib.request,json,hashlib
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]/'fixtures'/'hub-assets'
REVISION='ea2f2aa1ca8da0898330c97df8a5489976b051d0'

def fetch():
    commit=REVISION
    sources=json.loads((ROOT/'sources.json').read_text())
    for name in ('tower-square','wall','gate','bridge-straight-pillar'):
        url=f'https://raw.githubusercontent.com/Hidencod/tge-assets/{commit}/packs/castle-kit/{name}.glb'
        payload=urllib.request.urlopen(url).read()
        asset='castle-'+name
        (ROOT/(asset+'.glb')).write_bytes(payload)
        sources=[s for s in sources if s['asset']!=asset]
        sources.append(dict(asset=asset,source=url,license='CC0-1.0',author='Kenney',sha256=hashlib.sha256(payload).hexdigest()))
    (ROOT/'sources.json').write_text(json.dumps(sources,indent=2)+'\n')
    credit='\nCastle Kit by Kenney — CC0 1.0 (public domain).\nhttps://kenney.nl/assets/castle-kit\nDistributed GLB mirror: https://github.com/Hidencod/tge-assets\nPinned mirror revision: '+commit+'\nOpenSCCT adaptations: authored scale, placement, collision and traversal outlines.\n'
    credits=ROOT/'CREDITS.txt'
    if credit not in credits.read_text(encoding='utf8'):
        with credits.open('a',encoding='utf8') as f:f.write(credit)
    print('Downloaded four castle modules at',commit)

if __name__=='__main__':fetch()
