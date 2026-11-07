"""Authored traversal sequences for the Hub's two exploration courses.

The route guide is test data, not a player breadcrumb. It describes normal-input
approaches to the same physical geometry/GE network used by players.
"""
import math


class Course:
    def __init__(self, L, ident, title, start, length, sign, path, mover):
        self.L, self.id, self.title = L, ident, title
        self.p = tuple(start)
        self.d = (0, 1)
        self.start = tuple(start)
        self.length = length
        self.sign, self.path, self.mover = sign, path, mover
        self.material = 'ruin_stone' if ident == 'medium' else 'frozen_stone'
        self.accent = 'neon_gold' if ident == 'medium' else 'neon_ice'
        self.gates, self.sections, self.guide = [], [], []
        self.serial = 0
        self.walk(512, gate=False)

    def point(self, forward=0, right=0, up=0):
        x, y, z = self.p
        dx, dy = self.d
        return (x + dx*forward + dy*right, y + dy*forward - dx*right, z + up)

    def box(self, name, lo, hi, material=None):
        corners = [self.point(f, r, z) for f in (lo[0], hi[0]) for r in (lo[1], hi[1]) for z in (lo[2], hi[2])]
        self.serial += 1
        self.L.solid(self.id + str(self.serial) + name,
                     tuple(min(v[k] for v in corners) for k in range(3)),
                     tuple(max(v[k] for v in corners) for k in range(3)), material or self.material)

    def floor(self, begin, end, width=384, height=0, name='Deck'):
        self.box(name, (begin, -width/2, height-64), (end, width/2, height))
        # Buttresses sit outside the running lane. Upper galleries do not put
        # support columns through the route beneath them.
        for f in range(int(begin)+480, int(end)-384, 960):
            for side in (-1, 1):
                r=side*(width/2+112)
                self.box('Buttress', (f-40, r-32, max(-self.p[2],height-512)), (f+40, r+32, height))

    def ramp(self, distance=1024, rise=192, width=384):
        lo,hi=self._bounds(0,distance,-width/2,width/2,-64,rise)
        axis=0 if self.d[0] else 1
        direction=('+' if self.d[axis]>0 else '-')+('x' if axis==0 else 'y')
        self.serial+=1
        self.L.solid(self.id+str(self.serial)+'BattlementRamp',lo,hi,self.material,up=direction)
        self.advance(distance,rise)
        self.floor(0,512,width,name='RampCrown')
        self.step('walk',self.point(256),min(140,width/2-12),validate=False)
        self.advance(512)

    def bridge(self, distance=1280, width=192):
        self.floor(-160,distance,width,name='TimberBridge')
        # Outboard frames carry a timber span; the running lane stays open.
        for f in (256,distance-256):
            for side in (-1,1):
                r=side*320
                self.box('BridgePier',(f-32,r-24,-self.p[2]),(f+32,r+24,72))
            self.box('BridgeCrossbeam',(f-32,-344,-56),(f+32,344,-32),'ruin_wood')
        for side in (-1,1):
            r=side*320
            self.L.beam(self.id+'BridgeRail',self.point(256,r,72),self.point(distance-256,r,72),8,'ruin_wood')
        self.step('walk',self.point(distance-128),min(140,width/2-12),validate=False)
        self.advance(distance)

    def _bounds(self, f0, f1, r0, r1, z0, z1):
        pts=[self.point(f,r,z) for f in (f0,f1) for r in (r0,r1) for z in (z0,z1)]
        return tuple(min(p[k] for p in pts) for k in range(3)),tuple(max(p[k] for p in pts) for k in range(3))

    def advance(self, length, rise=0):
        self.p = self.point(length, up=rise)

    def step(self, kind, at, radius=120, validate=True, **data):
        if validate:self.gates.append({'at': list(at), 'radius': radius, 'depth': 24, 'height': 160})
        self.guide.append({'kind':kind, 'at':list(at), 'yaw':int(math.atan2(self.d[1],self.d[0])*65536/(2*math.pi)) & 65535,
                           'gate':len(self.gates)-1 if validate else None, 'section':len(self.sections)-1, **data})

    def walk(self, distance, width=384, gate=False):
        self.floor(-160, distance, width)
        self.step('walk', self.point(distance-128), min(140,width/2-12),validate=gate)
        self.advance(distance)

    def jumps(self, gaps, width=240, landing=384):
        for gap in gaps:
            takeoff=self.point(-48)
            self.floor(gap,gap+landing,width,name='BrokenAqueduct')
            self.step('jump',self.point(gap+landing/2),min(128,width/2-8),takeoff=list(takeoff))
            self.advance(gap+landing)

    def climb(self, rise=264, kind='ledge'):
        if kind=='wallkick':self.walk(384)
        # The front face is deliberately plain; trim must not interrupt native
        # wall/split-jump probes or the full-span ledge.
        approach=self.point(-72)
        self.box('ClimbFace',(0,-192,-64),(512,192,rise))
        ledge=self.id+str(self.serial)+'Catch'
        lip=96 if kind=='wallkick' else 192
        self.L.ge(ledge,'LG',self.point(0,-lip,rise),self.point(0,lip,rise),note='full width climb face')
        if kind=='wallkick':
            for side in (-1,1):
                r=side*112
                self.box('SplitWall',(-384,r-16,-64),(0,r+16,rise+112))
            approach=self.point(-256)
        self.step(kind,self.point(288,up=rise),140,approach=list(approach),ge=ledge,rise=rise)
        self.advance(512,rise)

    def vertical(self, kind, rise):
        self.box('ServiceTower',(0,-192,-64),(512,192,rise))
        offset=20 if kind=='pipe' else 4
        a=self.point(-offset);b=self.point(-offset,up=rise);n=self.point(-offset-10)
        ge=self.id+str(self.serial)+kind
        self.L.ge(ge,'PIPE' if kind=='pipe' else 'LADDER',a,b,n,note='native service ascent, flush exit')
        if kind=='pipe':self.L.beam(self.id+'Pipes',self.point(-16),self.point(-16,up=rise),4,'metal')
        else:
            for z in range(16,rise,32):self.L.beam(self.id+'Rungs',self.point(-offset-3,-24,z),self.point(-offset-3,24,z),4,'metal')
        self.step(kind,self.point(288,up=rise),140,approach=list(self.point(-96)),ge=ge,rise=rise)
        self.advance(512,rise)

    def hoh(self, gap=640):
        a=self.point(-192,up=277);b=self.point(gap+192,up=277)
        ge=self.id+str(self.serial)+'HOH'
        self.L.ge(ge,'HOH',a,b,note='ceiling traverse; crouch releases above the exit')
        self.L.beam(self.id+'Overhead',a,b,4,'metal')
        for end in (a,b):
            self.L.beam(self.id+'OverheadHanger',end,(end[0],end[1],end[2]+59),6,'metal')
        for f0,f1 in ((-224,-192),(gap+192,gap+224)):
            self.L.beam(self.id+'OverheadBracket',self.point(f0,up=336),self.point(f1,up=336),8,'metal')
        self.floor(gap,gap+512,name='HohExit')
        for f in (-224,gap+224):
            for r in (-288,288):self.box('OverheadSupport',(f-16,r-16,-self.p[2]),(f+16,r+16,400))
            self.box('OverheadCrossarm',(f-16,-304,320),(f+16,304,352))
        self.step('hoh',self.point(gap+288),140,approach=list(self.point(-112)),release=list(self.point(gap+160)),ge=ge)
        self.advance(gap+512)

    def shimmy(self, gap=640):
        # A wall along the left side carries the route across a broken gallery.
        # A shallow soffit prevents pulling up until the exit landing.
        self.box('GalleryWall',(-192,-256,-256),(gap+256,-192,264))
        self.box('GallerySoffit',(-192,-240,376),(gap+256,-144,408))
        a=self.point(-192,-192,264);b=self.point(gap+256,-192,264)
        ge=self.id+str(self.serial)+'Shimmy'
        self.L.ge(ge,'LG',a,b,note='hang-only: gallery handhold, full wall edge; drop onto the exit terrace')
        self.floor(gap,gap+512,name='GalleryExit')
        self.step('shimmy',self.point(gap+288),140,approach=list(self.point(-112,-153)),
                  release=list(self.point(gap+160,-153)),ge=ge)
        self.advance(gap+512)

    def rail_roll(self, rail=True, length=448):
        # Use a consistent vaulted gate module; approaches vary around it.
        length=max(length,448)
        self.floor(-160,length+1056,name='GateGallery')
        if rail:
            self.box('VaultRail',(64,-192,0),(88,192,100))
            self.step('vault',self.point(280),120,approach=list(self.point(-64)))
        # The standing roll's initiation probe reaches 131.5 uu above this floor.
        # 144 clears that probe while still requiring the low-profile movement.
        self.box('LowArch',(608,-224,144),(608+length,224,240))
        for side in (-1,1):
            r=side*208
            self.box('ArchPier',(608,r-16,0),(608+length,r+16,240))
        self.step('roll',self.point(800+length),140,approach=list(self.point(448)),exit=list(self.point(688+length)))
        self.advance(length+1056)

    def fence(self):
        self.floor(-160,768,name='FenceTerrace')
        self.box('Fence',(160,-192,0),(162,192,376),'fence')
        ge=self.id+str(self.serial)+'Fence'
        self.L.ge(ge,'FENCE',self.point(161,-192,376),self.point(161,192,376),note='full-width climbing screen')
        self.step('fence',self.point(512),140,approach=list(self.point(64)),ge=ge)
        self.advance(768)

    def lift(self, ident, travel_seconds=7):
        # Board from a broad stationary quay and transfer to an upper gallery.
        start=self.point(176,up=-32);end=(start[0],start[1],start[2]+640)
        self.mover(ident,start,end,'ruin_wood' if self.id=='medium' else 'observatory',travel_seconds)
        self.floor(352,992,384,height=640,name='LiftExit')
        self.step('lift',self.point(672,up=640),140,approach=list(self.point(-96)),mover=ident)
        self.advance(992,640)

    def shuttle(self):
        start=self.point(176,up=-32);end=self.point(1312,up=-32)
        self.mover('hardShuttle',start,end,'observatory',8)
        self.floor(1488,2128,name='ShuttleExit')
        self.step('shuttle',self.point(1808),140,approach=list(self.point(-96)),mover='hardShuttle')
        self.advance(2128)

    def zip(self):
        # Give the previous landing/roll a clear run-up before the cable grab.
        self.walk(512)
        high=self.point(-112,up=304);low=self.point(1648,up=-16)
        ge=self.id+str(self.serial)+'Zip'
        self.L.ge(ge,'ZL',low,high,note='native downhill cable; broad landing')
        self.L.beam(self.id+'ZipCable',high,low,3,'metal')
        self.floor(1408,1920,height=-224,name='ZipExit')
        for f,z,base in ((-112,328,0),(1648,8,-224)):
            a=self.point(f,96,base);b=self.point(f,96,z)
            self.L.beam(self.id+'ZipSupport',a,b,8,'metal')
            self.L.beam(self.id+'ZipSupport',b,self.point(f,0,z),8,'metal')
            self.L.ge(self.id+str(self.serial)+'ZipPost'+str(f).replace('-','Neg'),'PIPE',a,b,self.point(f,106,base),note='outboard cable support')
        self.L.beam(self.id+'ZipAnchor',high,self.point(-112,0,328),6,'metal')
        self.L.beam(self.id+'ZipAnchor',low,self.point(1648,0,8),6,'metal')
        self.step('zip',self.point(1744,up=-224),140,approach=list(self.point(-52)),ge=ge)
        self.advance(1920,-224)

    def section(self,name,operations):
        # Each section begins on a generous solid breather/recovery terrace.
        recovery=self.point(-128)
        if self.sections:self.step('walk',recovery,256)
        index=len(self.sections)
        self.sections.append({'name':name,'firstGate':len(self.gates),'recovery':list(recovery),
                              'yaw':int(math.atan2(self.d[1],self.d[0])*65536/(2*math.pi)) & 65535})
        if index:
            self.box('CheckpointBalcony',(-384,192,-64),(128,816,0))
            self.sign(self.id+'SectionSign'+str(index),self.point(-128,520,300),f'{index+1}  {name}',
                      'CHECKPOINT',width=480,normal=(-self.d[0],-self.d[1]),base=self.p[2])
        self.L.light(self.id+'SectionLight'+str(index),self.point(-128,0,512),170,100,hue=45 if self.id=='medium' else 145,saturation=170)
        initial=self.p
        for op,args in operations:getattr(self,op)(*args)
        used=(self.p[0]-initial[0])*self.d[0]+(self.p[1]-initial[1])*self.d[1]
        if used < self.length:self.walk(self.length-used)
        # A continuous outer curtain wall makes the running terraces part of a
        # castle. Keep it outside the route and its recovery balconies; heights
        # follow the ascent without filling the lower running gallery.
        if not hasattr(self.L,'CASTLE_WALLS'):self.L.CASTLE_WALLS=[]
        direction=self.d
        for f in (800,2400):
            at=(initial[0]+direction[0]*f+direction[1]*640,
                initial[1]+direction[1]*f-direction[0]*640,0)
            self.L.CASTLE_WALLS.append(dict(at=at,height=max(initial[2]+192,512),
                yaw=0 if direction[0] else 90,size=(1600,192,max(initial[2]+192,512))))
        # A stone belvedere joins each perpendicular district facade. It provides
        # an optional view/exploration branch and a clear preview of the next leg.
        self.floor(-384,256,768,name='Belvedere')
        self.step('walk',self.point(-128),140,validate=False)
        self.p=self.point(-128)
        self.d=(-self.d[1],self.d[0])
        self.walk(384)

    def finish(self, clean_seconds, revision=2):
        finish=self.point(-128)
        # The finish circle validates the final open terrace. Test walking targets
        # are not extra invisible obstacles: players may take any line to the circle.
        last_obstacle=next(s['gate'] for s in reversed(self.guide) if s['kind']!='walk')
        del self.gates[last_obstacle+1:]
        for step in self.guide:
            if step['gate'] is not None and step['gate']>last_obstacle:step['gate']=None
        self.step('walk',finish,140,validate=False)
        points=[self.start,finish,*[tuple(g['at']) for g in self.gates]]
        lo=[min(p[k] for p in points)-768 for k in range(2)]
        hi=[max(p[k] for p in points)+768 for k in range(2)]
        for i,section in enumerate(self.sections):
            end=self.sections[i+1]['firstGate'] if i+1<len(self.sections) else len(self.gates)
            bottom=min([section['recovery'][2],*[g['at'][2] for g in self.gates[section['firstGate']:end]]])-160
            section['failures']=[{'lo':[*lo,-128],'hi':[*hi,bottom]}]
        self.box('FinishBalcony',(-512,192,-64),(256,1200,0))
        self.sign(self.id+'Finish',self.point(-128,700,400),'SUMMIT REACHED',self.title,
                  width=768,normal=(-self.d[0],-self.d[1]),base=self.p[2])
        x,y,z=self.start
        # One entrance at the actual green start gate. The scoreboard stands to
        # the side on the same terrace, rather than duplicating its title overhead.
        self.L.solid(self.id+'EntranceTerrace',(x-768,y-320,z-64),(x+1408,y+192,z),self.material)
        self.sign(self.id+'Entrance',(x,y-128,z+576),self.title,'START HERE / SPY COURSE',width=1152,base=z)
        board=(x+1024,y-128,z+304)
        self.L.solid(self.id+'ScorePanel',(board[0]-288,board[1]+1,z+64),(board[0]+288,board[1]+25,z+544),'darkpanel')
        for side in (-1,1):
            bx=board[0]+side*300
            self.L.solid(self.id+'ScorePost'+str(side),(bx-12,board[1],z),(bx+12,board[1]+28,z+568),'wood')
        travel=next(t for t in self.L.TELEPORTS if t['id']==self.id)
        self.path(self.id+'ApproachPath',tuple(travel['station'][:2]),(self.start[0],self.start[1]-1536),material='track')
        self.L.solid(self.id+'Approach',(self.start[0]-192,self.start[1]-1536,0),(self.start[0]+192,self.start[1],self.start[2]),self.material,up='+y')
        self.L.area(self.id.title()+'Course','movementCourse',self.title,tuple(lo),tuple(hi),'H3',
                    courseId=self.id,revision=revision,start=list(self.start),finish=list(finish),checkpoints=self.gates,
                    sections=self.sections,failures=[],startRadius=160,finishRadius=192,maxSeconds=2700 if self.id=='medium' else 5400,
                    leaderboardAnchor=list(board),leaderboardYaw=49152,leaderboardSize=[576,480],
                    cleanRunSeconds=list(clean_seconds),playtestRoute=self.guide)


def build(L,sign,path,mover):
    medium=Course(L,'medium','Medium - Castle Ramparts',(11008,15872,320),4608,sign,path,mover)
    medium.section('Gatehouse Dash',[('rail_roll',()),('ramp',(1024,192)),('jumps',([155,205],))])
    medium.section('Broken Aqueduct',[('bridge',(1280,192)),('jumps',([180,205,155],)),('climb',(208,))])
    medium.section('Castle Courtyard',[('fence',()),('ramp',(1024,256)),('rail_roll',(True,256)),('bridge',(768,240))])
    medium.section('Siege Lift',[('jumps',([180,205],)),('lift',('mediumLift',3.5)),('rail_roll',(False,256)),('climb',(208,))])
    medium.section('Wallside Escape',[('shimmy',(384,)),('bridge',(1152,176)),('jumps',([205,180],)),('ramp',(768,192))])
    medium.section('Tower Extraction',[('vertical',('ladder',384)),('hoh',(384,)),('climb',(264,)),('zip',())])
    medium.finish((110,180),revision=6)

    hard=Course(L,'hard','Hard - Frozen Citadel',(27264,15360,512),5120,sign,path,mover)
    hard.section('Razor Approach',[('jumps',([220,240,255],112,384)),('rail_roll',(True,256)),('ramp',(1024,256))])
    hard.section('Split Bell Tower',[('climb',(408,'wallkick')),('bridge',(1024,112)),('jumps',([230,250],128,384)),('ramp',(768,192))])
    hard.section('Frozen Gallery',[('shimmy',(512,)),('climb',(264,)),('rail_roll',(False,384)),('bridge',(1280,128))])
    hard.section('Shuttle Crossing',[('shuttle',()),('jumps',([235,250],112,384)),('fence',()),('vertical',('ladder',384))])
    hard.section('Roofline Chase',[('vertical',('pipe',384)),('ramp',(1024,256)),('jumps',([240,255],104,384)),('rail_roll',(True,256))])
    hard.section('Vaulted Passages',[('rail_roll',(True,384)),('hoh',(512,)),('jumps',([240,255],104,384)),('bridge',(1024,112))])
    hard.section('High Lift',[('lift',('hardLift',3.5)),('ramp',(768,192)),('jumps',([235,250],112,384)),('bridge',(1024,112))])
    hard.section('Citadel Summit',[('climb',(264,)),('vertical',('ladder',384)),('rail_roll',(True,256)),('zip',())])
    hard.finish((180,270),revision=5)
