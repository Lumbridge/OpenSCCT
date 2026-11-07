"""Framed diamond wire mesh, fitted inside the native thin climbing planes."""
import math


def build(L):
    L.mat('fence_wire',surface=14,art='fence_wire')
    for fence in (s for s in L.SOLIDS if s.material=='fence'):
        axis=0 if fence.hi[0]-fence.lo[0]>fence.hi[1]-fence.lo[1] else 1
        normal=1-axis
        span=fence.hi[axis]-fence.lo[axis]
        height=fence.hi[2]-fence.lo[2]
        def point(u,z,offset=0):
            p=list(fence.lo)
            p[axis]+=u; p[normal]=(fence.lo[normal]+fence.hi[normal])/2+offset; p[2]+=z
            return tuple(p)
        def frame(u0,u1,z0,z1):
            lo=list(fence.lo);hi=list(fence.hi)
            lo[axis]+=u0;hi[axis]=fence.lo[axis]+u1
            lo[2]+=z0;hi[2]=fence.lo[2]+z1
            L.box(fence.name+'WireFrame',lo,hi,'metal')
        for u in (0,span-8):frame(u,u+8,0,height)
        frame(8,span-8,0,8)
        frame(8,span-8,height-8,height)
        # Actual open diamonds rather than a dark panel with bright painted lines.
        # Slightly separated wire families give the crossings physical depth.
        u0,u1,z0,z1=8.5,span-8.5,8.5,height-8.5
        for slope in (-1,1):
            offsets=[z-slope*u for u in (u0,u1) for z in (z0,z1)]
            for intercept in range(math.floor(min(offsets)/14)*14,math.ceil(max(offsets)/14)*14+1,14):
                ends=[]
                for u in (u0,u1):
                    z=slope*u+intercept
                    if z0<=z<=z1:ends.append((u,z))
                for z in (z0,z1):
                    u=(z-intercept)/slope
                    if u0<=u<=u1 and (u,z) not in ends:ends.append((u,z))
                if len(ends)!=2 or math.dist(*ends)<1:continue
                L.beam(fence.name+'WireMesh',point(*ends[0],slope*.25),
                       point(*ends[1],slope*.25),.6,'fence_wire')
