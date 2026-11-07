"""Triangle/box clearance for authored model traversal, using the separating-axis test."""
import math
import numpy as np

class MeshContacts:
    def __init__(self,models):
        self.models=[];self.cells={}
        for model in models:
            vertices=np.asarray(model['vertices']);tri=vertices[np.asarray(model['triangles'])]
            lo=tri.min(1);hi=tri.max(1)
            index=len(self.models);self.models.append((model['id'],tri,lo,hi))
            a=vertices.min(0);b=vertices.max(0)
            for x in range(math.floor(a[0]/512),math.floor(b[0]/512)+1):
                for y in range(math.floor(a[1]/512),math.floor(b[1]/512)+1):self.cells.setdefault((x,y),set()).add(index)

    def supported(self,point,normal):
        """A short vertical trace on either side of a rim must meet its physical surface."""
        for side in (-1,1):
            xy=np.asarray(point[:2])+np.asarray(normal)*side*2
            nearest=float('-inf')
            for index in self.cells.get((math.floor(xy[0]/512),math.floor(xy[1]/512)),()):
                _,tri,lo,hi=self.models[index]
                selected=np.all(lo[:,:2]<=xy,axis=1)&np.all(hi[:,:2]>=xy,axis=1)
                t=tri[selected]
                if not len(t):continue
                a=t[:,0,:2];u=t[:,1,:2]-a;v=t[:,2,:2]-a;q=xy-a
                det=u[:,0]*v[:,1]-u[:,1]*v[:,0]
                valid=np.abs(det)>1e-8
                det=np.where(valid,det,1)
                b=(q[:,0]*v[:,1]-q[:,1]*v[:,0])/det
                c=(u[:,0]*q[:,1]-u[:,1]*q[:,0])/det
                z=t[:,0,2]+b*(t[:,1,2]-t[:,0,2])+c*(t[:,2,2]-t[:,0,2])
                found=z[valid&(b>=0)&(c>=0)&(b+c<=1)&(np.abs(z-point[2])<30)]
                if len(found):nearest=max(nearest,float(found.max()))
            if abs(nearest-point[2])<8:return True
        return False

    def hits(self,centre,extent,only=None):
        centre=np.asarray(centre);extent=np.asarray(extent)-.01
        low=centre-extent;high=centre+extent;candidates=set()
        for x in range(math.floor(low[0]/512),math.floor(high[0]/512)+1):
            for y in range(math.floor(low[1]/512),math.floor(high[1]/512)+1):candidates.update(self.cells.get((x,y),()))
        found=[]
        for index in sorted(candidates):
            ident,tri,lo,hi=self.models[index]
            if only is not None and ident!=only:continue
            selected=np.all(lo<high,axis=1)&np.all(hi>low,axis=1)
            if not np.any(selected):continue
            t=tri[selected]-centre
            edges=np.roll(t,-1,axis=1)-t
            axes=[np.cross(edges[:,0],edges[:,1])]
            for k in range(3):
                for axis in np.eye(3):axes.append(np.cross(edges[:,k],axis))
            valid=np.ones(len(t),dtype=bool)
            for axis in axes:
                dots=np.einsum('nij,nj->ni',t,axis);r=np.abs(axis)@extent
                valid &= (dots.min(1)<=r+1e-6)&(dots.max(1)>=-r-1e-6)
                if not np.any(valid):break
            if np.any(valid):found.append(ident)
        return found
