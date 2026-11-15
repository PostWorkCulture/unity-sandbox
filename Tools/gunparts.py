#!/usr/bin/env python3
# Extra parts for the exported weapon models (Street Life, Unity): built here as plain meshes, written to
# Assets/StreetLife/Resources/StreetLife/GunParts.json, and added at load by WeaponFixes (one node per gun).
# Coordinates are the held models' (Unity space: x across, y up, -z toward the muzzle). Materials are guns_materials.json ids.
#   python3 Tools/gunparts.py [out.json]
import json, math, sys

# ---------------------------------------------------------------- mesh building
class MB:
    def __init__(s): s.v=[]; s.n=[]; s.t=[]
    def quad(s,a,b,c,d,nr):
        i=len(s.v); s.v+= [a,b,c,d]; s.n+=[nr]*4
        cr=cross(sub(b,a),sub(c,a))
        if dot(cr,nr)>0: s.t+=[i,i+1,i+2,i,i+2,i+3]
        else: s.t+=[i,i+2,i+1,i,i+3,i+2]
    def tri(s,a,b,c,nr):
        i=len(s.v); s.v+=[a,b,c]; s.n+=[nr]*3
        cr=cross(sub(b,a),sub(c,a))
        s.t+= [i,i+1,i+2] if dot(cr,nr)>0 else [i,i+2,i+1]
    def poly(s,pts,nr):   # convex fan
        for k in range(1,len(pts)-1): s.tri(pts[0],pts[k],pts[k+1],nr)
    def add(s,o):
        k=len(s.v); s.v+=o.v; s.n+=o.n; s.t+=[i+k for i in o.t]

def sub(a,b): return (a[0]-b[0],a[1]-b[1],a[2]-b[2])
def addv(a,b): return (a[0]+b[0],a[1]+b[1],a[2]+b[2])
def mul(a,k): return (a[0]*k,a[1]*k,a[2]*k)
def dot(a,b): return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def norm(a): l=math.sqrt(dot(a,a)) or 1; return (a[0]/l,a[1]/l,a[2]/l)

def box(m,c,s):
    hx,hy,hz=s[0]/2,s[1]/2,s[2]/2
    P=lambda x,y,z:(c[0]+x*hx,c[1]+y*hy,c[2]+z*hz)
    m.quad(P(1,-1,-1),P(1,1,-1),P(1,1,1),P(1,-1,1),(1,0,0)); m.quad(P(-1,-1,1),P(-1,1,1),P(-1,1,-1),P(-1,-1,-1),(-1,0,0))
    m.quad(P(-1,1,-1),P(-1,1,1),P(1,1,1),P(1,1,-1),(0,1,0)); m.quad(P(-1,-1,1),P(-1,-1,-1),P(1,-1,-1),P(1,-1,1),(0,-1,0))
    m.quad(P(-1,-1,1),P(1,-1,1),P(1,1,1),P(-1,1,1),(0,0,1)); m.quad(P(1,-1,-1),P(-1,-1,-1),P(-1,1,-1),P(1,1,-1),(0,0,-1))

def prism(m,pts,x0,x1,caps=(True,True)):
    """convex (z,y) polygon extruded across x from x0 to x1"""
    A=[(x0,p[1],p[0]) for p in pts]; B=[(x1,p[1],p[0]) for p in pts]
    sx=1 if x1>x0 else -1
    if caps[1]: m.poly(B,(sx,0,0))
    if caps[0]: m.poly(A,(-sx,0,0))
    cz=sum(p[0] for p in pts)/len(pts); cy=sum(p[1] for p in pts)/len(pts)
    for i in range(len(pts)):
        j=(i+1)%len(pts); dz=pts[j][0]-pts[i][0]; dy=pts[j][1]-pts[i][1]
        nr=norm((0,-dz,dy))
        mz=(pts[i][0]+pts[j][0])/2-cz; my=(pts[i][1]+pts[j][1])/2-cy
        if nr[2]*mz+nr[1]*my<0: nr=mul(nr,-1)
        m.quad(A[i],A[j],B[j],B[i],nr)

def both(fn):
    """run a builder for +x and its mirror on -x"""
    m=MB(); fn(m,1); fn(m,-1); return m

def cyl(m,c,axis,r,ln,seg=14,caps=True):
    ax=norm(axis); t=(1,0,0) if abs(ax[0])<.9 else (0,1,0); u=norm(cross(ax,t)); w=cross(ax,u)
    a0=addv(c,mul(ax,-ln/2)); a1=addv(c,mul(ax,ln/2))
    ring=lambda base,k:addv(base,addv(mul(u,r*math.cos(2*math.pi*k/seg)),mul(w,r*math.sin(2*math.pi*k/seg))))
    for k in range(seg):
        p0,p1,q0,q1=ring(a0,k),ring(a0,k+1),ring(a1,k),ring(a1,k+1)
        nr=norm(addv(mul(u,math.cos(2*math.pi*(k+.5)/seg)),mul(w,math.sin(2*math.pi*(k+.5)/seg))))
        m.quad(p0,p1,q1,q0,nr)
        if caps: m.tri(a1,q0,q1,ax); m.tri(a0,p1,p0,mul(ax,-1))

def torus(m,c,axis,R,r,seg=18,sides=8,arc=1.0,start=0.0):
    ax=norm(axis); t=(1,0,0) if abs(ax[0])<.9 else (0,1,0); u=norm(cross(ax,t)); w=cross(ax,u)
    def P(i,j):
        a=2*math.pi*(start+arc*i/seg); b=2*math.pi*j/sides
        d=addv(mul(u,math.cos(a)),mul(w,math.sin(a)))
        nr=addv(mul(d,math.cos(b)),mul(ax,math.sin(b)))
        return addv(c,addv(mul(d,R),mul(nr,r))),nr
    for i in range(seg):
        for j in range(sides):
            (a,na),(b,nb),(cc,nc),(dd,nd)=P(i,j),P(i+1,j),P(i+1,j+1),P(i,j+1)
            m.quad(a,b,cc,dd,norm(addv(addv(na,nb),addv(nc,nd))))

def sweep(m,path,w,h,closed=False,plane='zy',x=0.0):
    """flat band (half width w across the plane's normal, half thickness h in-plane) along a path of 2D points.
    plane 'zy': points are (z,y), band across x at x; plane 'xy': points are (x,y), band across z at z=x"""
    n=len(path)
    def P3(p,off_in,off_n):
        if plane=='zy': return (x+off_n, p[1]+off_in[1], p[0]+off_in[0])
        return (p[0]+off_in[0], p[1]+off_in[1], x+off_n)
    def tang(i):
        a=path[(i-1)%n] if (closed or i>0) else path[i]; b=path[(i+1)%n] if (closed or i<n-1) else path[i]
        d=(b[0]-a[0],b[1]-a[1]); l=math.hypot(*d) or 1; return (d[0]/l,d[1]/l)
    rng=range(n if closed else n-1)
    for i in rng:
        j=(i+1)%n; ti,tj=tang(i),tang(j)
        ni=(-ti[1],ti[0]); nj=(-tj[1],tj[0])
        a_o=P3(path[i],(ni[0]*h,ni[1]*h),0); a_i=P3(path[i],(-ni[0]*h,-ni[1]*h),0)
        b_o=P3(path[j],(nj[0]*h,nj[1]*h),0); b_i=P3(path[j],(-nj[0]*h,-nj[1]*h),0)
        def W(p,s): return (p[0]+(s*w if plane=='zy' else 0), p[1], p[2]+(s*w if plane=='xy' else 0))
        nm=((ni[0]+nj[0])/2,(ni[1]+nj[1])/2)
        N3=(0,nm[1],nm[0]) if plane=='zy' else (nm[0],nm[1],0)
        AX=(1,0,0) if plane=='zy' else (0,0,1)
        m.quad(W(a_o,-1),W(a_o,1),W(b_o,1),W(b_o,-1),norm(N3))
        m.quad(W(a_i,1),W(a_i,-1),W(b_i,-1),W(b_i,1),norm(mul(N3,-1)))
        m.quad(W(a_o,1),W(a_i,1),W(b_i,1),W(b_o,1),AX)
        m.quad(W(a_i,-1),W(a_o,-1),W(b_o,-1),W(b_i,-1),mul(AX,-1))

def rrect(cx,cy,hw,hh,r,seg=4):
    """rounded rectangle path (x,y), counter-clockwise"""
    pts=[]
    for (qx,qy,a0) in ((hw-r,hh-r,0),(-(hw-r),hh-r,90),(-(hw-r),-(hh-r),180),(hw-r,-(hh-r),270)):
        for k in range(seg+1):
            a=math.radians(a0+90*k/seg); pts.append((cx+qx+r*math.cos(a),cy+qy+r*math.sin(a)))
    return pts

# ---------------------------------------------------------------- 2D polygon helpers (z,y)
def area(P): return sum(P[i][0]*P[(i+1)%len(P)][1]-P[(i+1)%len(P)][0]*P[i][1] for i in range(len(P)))/2
def inset(P,d):
    """offset a simple polygon inward by d (mitred)"""
    if area(P)<0: P=P[::-1]
    n=len(P); out=[]
    for i in range(n):
        a,b,c=P[i-1],P[i],P[(i+1)%n]
        e1=(b[0]-a[0],b[1]-a[1]); e2=(c[0]-b[0],c[1]-b[1])
        l1=math.hypot(*e1); l2=math.hypot(*e2)
        n1=(-e1[1]/l1,e1[0]/l1); n2=(-e2[1]/l2,e2[0]/l2)   # inward for CCW
        bis=(n1[0]+n2[0],n1[1]+n2[1]); bl=math.hypot(*bis) or 1; bis=(bis[0]/bl,bis[1]/bl)
        cs=max(.35,bis[0]*n1[0]+bis[1]*n1[1])
        out.append((b[0]+bis[0]*d/cs,b[1]+bis[1]*d/cs))
    return out

def band(m,P,d0,d1,x,h,sx):
    """a raised trim band between insets d0 and d1 of polygon P, on the face x*sx, standing h proud"""
    A=inset(P,d0); B=inset(P,d1); n=len(P)
    for i in range(n):
        j=(i+1)%n
        prism(m,[A[i],A[j],B[j],B[i]],sx*x,sx*(x+h),caps=(False,True))

def clip_seg(p,q,poly):
    """clip segment p-q to a convex polygon (z,y)"""
    if area(poly)<0: poly=poly[::-1]
    t0,t1=0.0,1.0; d=(q[0]-p[0],q[1]-p[1])
    for i in range(len(poly)):
        a,b=poly[i],poly[(i+1)%len(poly)]
        nrm=(-(b[1]-a[1]),b[0]-a[0])   # inward for CCW
        num=nrm[0]*(p[0]-a[0])+nrm[1]*(p[1]-a[1]); den=nrm[0]*d[0]+nrm[1]*d[1]
        if abs(den)<1e-12:
            if num<0: return None
            continue
        t=-num/den
        if den>0: t0=max(t0,t)
        else: t1=min(t1,t)
        if t0>t1: return None
    return (p[0]+d[0]*t0,p[1]+d[1]*t0),(p[0]+d[0]*t1,p[1]+d[1]*t1)

def checkering(m,panel,step,lw,x,h,sx,border=True):
    """diamond lattice of raised lines inside a convex panel, on the face x*sx"""
    zs=[p[0] for p in panel]; ys=[p[1] for p in panel]; cz=sum(zs)/len(zs); cy=sum(ys)/len(ys)
    R=max(max(zs)-min(zs),max(ys)-min(ys))
    for ang in (35,-35):
        a=math.radians(ang); d=(math.cos(a),math.sin(a)); nr=(-d[1],d[0])
        k=-R
        while k<=R:
            p=(cz+nr[0]*k-d[0]*R,cy+nr[1]*k-d[1]*R); q=(cz+nr[0]*k+d[0]*R,cy+nr[1]*k+d[1]*R)
            s=clip_seg(p,q,panel)
            if s and math.hypot(s[1][0]-s[0][0],s[1][1]-s[0][1])>lw*2:
                (z0,y0),(z1,y1)=s; o=(nr[0]*lw/2,nr[1]*lw/2)
                prism(m,[(z0+o[0],y0+o[1]),(z1+o[0],y1+o[1]),(z1-o[0],y1-o[1]),(z0-o[0],y0-o[1])],sx*x,sx*(x+h),caps=(False,True))
            k+=step
    if border: band(m,panel,0,lw*1.4,x,h*1.2,sx)

# ---------------------------------------------------------------- the parts
BLACK, DARKSTEEL, STEEL, GOLD, CHROME, RUBBER, WOOD_DARK, RED_SHELL, BRASS = 1, 73, 74, 2, 32, 7, 85, 79, 52
TRIM = 1001   # gloss-black trim (WeaponModels.TrimBlack): stays black in the menu's showroom
parts=[]
def part(gun,name,mat,m): parts.append({"gun":gun,"name":name,"mat":mat,"v":[round(c,4) for p in m.v for c in p],"n":[round(c,3) for p in m.n for c in p],"t":m.t})

# ===== Kar98 (scout): white stock framed in black
KAR_SIDE=[(1.0,0.06),(0.55,0.075),(0.42,0.03),(0.3,0.062),(-0.95,0.062),(-0.95,0.0),(-0.1,-0.04),(0.2,-0.05),(0.3,-0.15),(0.38,-0.14),(0.44,-0.07),(1.0,-0.22)]
KX=.0425
def kar_trim(m,sx):
    band(m,KAR_SIDE,.003,.023,KX,.0022,sx)               # the frame round the side
    band(m,KAR_SIDE,.030,.036,KX,.0016,sx)               # a pinstripe inside it
    checkering(m,[(0.305,0.026),(0.392,0.0),(0.386,-0.088),(0.318,-0.098)],.011,.0022,KX,.0012,sx)   # wrist
    checkering(m,[(-0.80,0.036),(-0.56,0.030),(-0.56,0.006),(-0.80,0.013)],.011,.0022,KX,.0012,sx)  # fore-end
m=both(kar_trim)
# end caps: steel buttplate, fore-end nose cap, grip cap
box(m,(0,-0.080,1.0315),(.0875,.330,.026))
box(m,(0,0.031,-0.972),(.088,.112,.03))
prism(m,[(0.287,-0.168),(0.393,-0.158),(0.392,-0.172),(0.289,-0.183)],-0.036,0.036)
part("scout","kar black trim",TRIM,m)
m=MB()
# barrel bands round fore-end and barrel, with the sling swivels on the lower band and under the butt
for zc,ybot,ytop in ((-0.885,-0.028,0.128),(-0.585,-0.042,0.128)):
    sweep(m,rrect(0,(ybot+ytop)/2,.048,(ytop-ybot)/2+.004,.02),.011,.0035,closed=True,plane='xy',x=zc)
torus(m,(0,-0.072,-0.585),(1,0,0),.017,.0035)
box(m,(0,-0.050,-0.585),(.01,.012,.03))
torus(m,(0,-0.226,0.85),(1,0,0),.017,.0035)
box(m,(0,-0.205,0.85),(.012,.014,.04))
cyl(m,(0,0.052,-1.085),(0,0,1),.0055,.25)              # cleaning rod under the barrel
part("scout","kar steel fittings",DARKSTEEL,m)

# ===== Shotty Horror (autoshot): engraved receiver, vent rib, trigger guard, shell carrier, gold-framed stock
AS_SIDE=[(0.26,-0.1),(0.2,-0.065),(0.2,0.06),(0.34,0.03),(0.5,0.052),(0.92,0.03),(0.92,-0.24),(0.86,-0.26),(0.46,-0.12),(0.4,-0.2),(0.32,-0.2)]
AX=.0425
BOLT=[(0.267,-0.017),(0.36,-0.06),(0.39,-0.18),(0.30,-0.09)]   # the lightning inlay on the wrist
def as_stock(m,sx):
    band(m,AS_SIDE,.006,.0105,AX,.0014,sx)
    band(m,BOLT,-.0065,-.002,AX,.0034,sx)                 # gold edge round the inlay
m=both(as_stock)
# receiver side plates: gold frame and screws, hinge pin
def as_recv(m,sx):
    band(m,[(-0.03,0.075),(0.235,0.075),(0.235,-0.07),(-0.03,-0.07)],.006,.012,.0505,.0012,sx)
    for zc,yc in ((0.0,0.045),(0.0,-0.045),(0.205,0.045),(0.205,-0.045)):
        cyl(m,(sx*.0515,yc,zc),(1,0,0),.006,.003)
    cyl(m,(sx*.051,-0.058,-0.05),(1,0,0),.010,.004)
m.add(both(as_recv))
cyl(m,(0,-0.058,-0.05),(1,0,0),.0045,.112,caps=True)   # hinge pin through
# muzzle bead and the rib's front ramp
cyl(m,(0,0.0915,-0.955),(0,1,0),.0045,.007)
# triggers (double)
for zc in (0.095,0.135):
    sweep(m,[(zc-.004,-0.082),(zc-.002,-0.105),(zc+.004,-0.125),(zc+.014,-0.137)],.005,.003)
part("autoshot","shotty gold",GOLD,m)  # plates, screws, pin, bead, triggers

m=MB()
# muzzle rings on both barrels, a chrome band where the fore-end meets the barrels, fore-end cap
for x in (-0.0295,0.0295):
    torus(m,(x,0.038,-0.975),(0,0,1),.031,.0045,seg=20,sides=6)
    torus(m,(x,0.038,-0.955),(0,0,1),.031,.0025,seg=20,sides=6)
box(m,(0,-0.009,-0.447),(.128,.085,.016))
box(m,(0,-0.052,-0.25),(.04,.006,.26))                 # fore-end latch plate underneath
# trigger guard loop
sweep(m,[(0.015,-0.082),(0.02,-0.13),(0.05,-0.16),(0.12,-0.168),(0.19,-0.158),(0.235,-0.125),(0.255,-0.098)],.008,.004)
# top lever and tang safety
prism(m,[(0.14,0.086),(0.235,0.086),(0.235,0.096),(0.16,0.096)],-0.012,0.012)
prism(m,[(0.20,0.084),(0.26,0.084),(0.26,0.092),(0.20,0.092)],-0.035,-0.012)
part("autoshot","shotty chrome",CHROME,m)

m=MB()
# ventilated top rib between the barrels, and the bottom rib
box(m,(0,0.0835,-0.50),(.016,.006,.94))
k=-0.95
while k< -0.06:
    box(m,(0,0.076,k),(.010,.010,.012)); k+=.055
box(m,(0,0.005,-0.52),(.014,.010,.90))
# shell carrier on the right of the butt (-x, the bolt side in this data): a leather pad with five shells held by a strap
box(m,(-0.0465,-0.055,0.70),(.008,.115,.28))
box(m,(-0.0755,-0.050,0.70),(.005,.020,.29))
for i in range(6): box(m,(-0.062,-0.050,0.5625+i*.055),(.03,.020,.004))   # strap stitching between the loops
part("autoshot","shotty black",BLACK,m)
m=MB()
for i in range(5):
    cyl(m,(-0.062,-0.055,0.59+i*.055),(0,1,0),.0115,.085)
part("autoshot","shotty shells",RED_SHELL,m)
m=MB()
for i in range(5):
    cyl(m,(-0.062,0.0005,0.59+i*.055),(0,1,0),.0122,.026)
    cyl(m,(-0.062,0.0145,0.59+i*.055),(0,1,0),.0128,.003)
part("autoshot","shotty shell heads",BRASS,m)
m=MB()
def as_check(m,sx):
    checkering(m,[(0.405,0.022),(0.47,0.026),(0.465,-0.092),(0.405,-0.104)],.010,.0022,AX,.0012,sx)
    checkering(m,[(-0.41,0.022),(-0.09,0.022),(-0.09,-0.040),(-0.41,-0.040)],.012,.0024,.0602,.0012,sx)
m.add(both(as_check))
prism(m,[(0.318,-0.198),(0.402,-0.198),(0.402,-0.212),(0.318,-0.212)],-0.036,0.036)   # grip cap
torus(m,(0,-0.262,0.80),(1,0,0),.017,.0035)            # sling swivel under the butt
part("autoshot","shotty dark",WOOD_DARK,m)

out=sys.argv[1] if len(sys.argv)>1 else "Assets/StreetLife/Resources/StreetLife/GunParts.json"
json.dump({"parts":parts},open(out,"w"),separators=(",",":"))
print(out, len(parts), "parts,", sum(len(p["v"])//3 for p in parts), "verts")
