# Railed surfaces: tileable PBR texture sets made in code (albedo with smoothness in alpha, tangent-space normal, Y up).
import numpy as np, sys, os
from scipy import ndimage as ndi
from scipy.spatial import cKDTree
from PIL import Image
N = 1024
OUT = sys.argv[1] if len(sys.argv) > 1 else 'out'
os.makedirs(OUT, exist_ok=True)

def rng(seed): return np.random.default_rng(seed)

def spectral(seed, beta=2.0, lo=0, hi=None, n=N):
    """tileable noise with a 1/f^beta spectrum between lo and hi cycles per tile, 0..1"""
    r = rng(seed)
    fx = np.fft.fftfreq(n) * n; fy = np.fft.fftfreq(n) * n
    f = np.sqrt(fx[None, :] ** 2 + fy[:, None] ** 2); f[0, 0] = 1
    amp = 1 / f ** (beta / 2 * 2 / 2 + beta / 2)  # = f^-beta
    amp = f ** (-beta)
    if lo: amp *= (f >= lo)
    if hi: amp *= np.exp(-(f / hi) ** 4)
    amp[0, 0] = 0
    ph = r.uniform(0, 2 * np.pi, (n, n))
    x = np.fft.ifft2(amp * np.exp(1j * ph)).real
    x -= x.min(); x /= x.max() + 1e-9
    return x

def norm01(x): x = x - x.min(); return x / (x.max() + 1e-9)
def blur(x, s): return ndi.gaussian_filter(x, s, mode='wrap')
def smooth(e0, e1, x): t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)
def lerp(a, b, t): return a + (b - a) * t
def hexc(h): h = h.lstrip('#'); return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], float) / 255
def srgb2lin(c): return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)
def lin2srgb(c): c = np.clip(c, 0, 1); return np.where(c <= .0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - .055)

def normal_map(h, metres, depth):
    """h 0..1 height, depth in metres at h = 1, tile size in metres"""
    px = metres / h.shape[1]
    du = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) / 2 * depth / px
    dv = -(np.roll(h, -1, 0) - np.roll(h, 1, 0)) / 2 * depth / px
    n = np.dstack([-du, -dv, np.ones_like(h)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return n

def cavity(h, s=3):
    """dark where the surface is lower than its surroundings, 0..1 (1 = open)"""
    d = h - blur(h, s)
    return np.clip(1 + d * 4, 0, 1)

def save(name, albedo, smooth_, normal):
    a = np.dstack([np.clip(albedo, 0, 1), np.clip(smooth_, 0, 1)[..., None]])
    Image.fromarray((a * 255 + .5).astype(np.uint8), 'RGBA').save(f'{OUT}/{name}_albedo.png')
    Image.fromarray(((normal * .5 + .5) * 255 + .5).astype(np.uint8), 'RGB').save(f'{OUT}/{name}_normal.png')
    print(name, 'albedo mean', albedo.reshape(-1, 3).mean(0).round(3), 'smooth', round(float(smooth_.mean()), 3))

U, V = np.meshgrid((np.arange(N) + .5) / N, 1 - (np.arange(N) + .5) / N)   # u right, v up

def worley(seed, cells, jitter=.9):
    """tileable cell noise: F1, F2, id of the nearest point (cells x cells points)"""
    r = rng(seed)
    gx, gy = np.meshgrid(np.arange(cells), np.arange(cells))
    p = np.stack([(gx + .5 + (r.random((cells, cells)) - .5) * jitter) / cells, (gy + .5 + (r.random((cells, cells)) - .5) * jitter) / cells], -1).reshape(-1, 2)
    n = len(p)
    tiles = np.concatenate([p + [dx, dy] for dx in (-1, 0, 1) for dy in (-1, 0, 1)])
    d, i = cKDTree(tiles).query(np.stack([U.ravel(), V.ravel()], -1), k=2)
    return d[:, 0].reshape(N, N) * cells, d[:, 1].reshape(N, N) * cells, (i[:, 0] % n).reshape(N, N), n

def crack_lines(seed, count, length, width=1.2):
    """thin wandering cracks, 0..1 mask"""
    r = rng(seed); m = np.zeros((N, N))
    for _ in range(count):
        x, y = r.random(2) * N; a = r.random() * 6.283
        for _ in range(int(length * N)):
            m[int(y) % N, int(x) % N] = 1
            a += r.normal(0, .045) + (r.normal(0, .5) if r.random() < .02 else 0); x += np.cos(a); y += np.sin(a)
    return np.clip(blur(m, width) * width * 2.5, 0, 1)

# ------------------------------------------------------------------ brick (2.4 m: 8 x 24, running bond)
def brick():
    cols, rows = 8, 24
    r = rng(11)
    row = np.floor(V * rows); fv = V * rows - row
    off = (row % 2) * .5
    col = np.floor(U * cols + off); fu = U * cols + off - col
    bid = (row * cols + (col % cols)).astype(int)
    nb = cols * rows
    # distance to the brick's edge in metres
    bw, bh = 2.4 / cols, 2.4 / rows
    du = np.minimum(fu, 1 - fu) * bw; dv = np.minimum(fv, 1 - fv) * bh
    edge = np.minimum(du, dv)
    wob = (spectral(1, 1.6, 6) - .5) * .004
    face = smooth(.006, .013, edge + wob)                      # 0 in the mortar, 1 on the face
    fine = spectral(2, 1.2, 20); mid = spectral(3, 2.0, 3, 60); big = spectral(4, 2.4, 1, 8)
    hb = r.random(nb)[bid]                                      # each brick sits a little differently
    tilt = (r.random(nb)[bid] - .5) * (fu - .5) * .5
    pits = smooth(.72, .8, spectral(5, 1.0, 40)) * face
    chips = smooth(.78, .9, spectral(6, 2.2, 5, 40)) * smooth(.03, .0, edge) * face * 0
    h = face * (.62 + hb * .22 + tilt * .2 + (fine - .5) * .12 + (mid - .5) * .16) - pits * .12
    h += (1 - face) * (.18 + (fine - .5) * .16)
    h = norm01(h)
    base = np.array([hexc('#a5543a'), hexc('#8f4530'), hexc('#b86647'), hexc('#7c3b2b'), hexc('#ab5c44')])
    pick = r.choice(len(base), nb, p=[.42, .2, .16, .08, .14])
    bc = srgb2lin(base[pick])[bid]
    bc = bc * (0.9 + r.random(nb)[bid][..., None] * .2)
    bc = bc * (.86 + mid[..., None] * .28) * (.94 + fine[..., None] * .12)
    mortar = srgb2lin(hexc('#b3aa9c')) * (.8 + fine[..., None] * .3)
    alb = lerp(mortar, bc, face[..., None])
    alb *= (.55 + .45 * cavity(h, 4))[..., None]
    alb *= (.88 + big * .2)[..., None]                           # soot and weather
    sm = lerp(.1, .2 + mid * .14, face) * (1 - pits * .5)
    save('brick', lin2srgb(alb), sm, normal_map(blur(h, .6), 2.4, .014))

# ------------------------------------------------------------------ yard slabs (5 m: 2 x 2 cast slabs)
def slabs():
    r = rng(21)
    fu = (U * 2) % 1; fv = (V * 2) % 1
    sid = (np.floor(V * 2) * 2 + np.floor(U * 2)).astype(int)
    edge = np.minimum(np.minimum(fu, 1 - fu), np.minimum(fv, 1 - fv)) * 2.5      # metres
    joint = 1 - smooth(.008, .02, edge)
    fine = spectral(22, 1.0, 30); mid = spectral(23, 2.0, 2, 40); big = spectral(24, 2.6, 1, 6)
    pits = smooth(.80, .86, spectral(25, .8, 60))
    cr = crack_lines(26, 3, .3, 1.0)
    brush = spectral(27, 1.0, 80); brush = norm01(ndi.gaussian_filter(brush, (0, 5), mode='wrap'))   # broom finish, along u
    h = .7 + (mid - .5) * .12 + (fine - .5) * .05 + (brush - .5) * .07 - pits * .1 - cr * .25
    h = h * (1 - joint) + joint * .15
    h = norm01(h)
    tone = np.array([1.0, .93, .97, 1.04])[sid]
    stains = smooth(.55, .8, big) * .16 + smooth(.6, .9, spectral(28, 2.2, 2, 14)) * .12
    c = srgb2lin(hexc('#c4beb2'))[None, None, :] * tone[..., None]
    c = c * (.9 + mid[..., None] * .18) * (.95 + fine[..., None] * .1)
    c = c * (1 - stains[..., None] * np.array([.9, 1.0, 1.1]))
    c = c * (1 - cr[..., None] * .45) * (1 - pits[..., None] * .3)
    c = lerp(c, c * .38, joint[..., None])
    sm = .24 + stains * .5 + (mid - .5) * .12 - pits * .1 - joint * .1
    save('slabs', lin2srgb(c), sm, normal_map(blur(h, .5), 5.0, .012))

# ------------------------------------------------------------------ sealed shed floor (3 m)
def floor():
    fine = spectral(31, 1.0, 40); mid = spectral(32, 2.2, 2, 30); big = spectral(33, 2.8, 1, 5)
    cut = 1 - smooth(.004, .012, np.minimum(np.minimum(U, 1 - U), np.minimum(V, 1 - V)) * 3)   # one saw cut round the tile
    cr = crack_lines(34, 2, .25, .9)
    scuff = smooth(.5, .85, ndi.gaussian_filter(spectral(35, 1.4, 6), (1, 7), mode='wrap') * 1.0)
    scuff = norm01(scuff)
    h = norm01(.8 + (mid - .5) * .06 + (fine - .5) * .02 - cut * .5 - cr * .2)
    c = srgb2lin(hexc('#9d978b'))[None, None, :] * (.86 + big[..., None] * .22) * (.94 + mid[..., None] * .12) * (.97 + fine[..., None] * .06)
    c = c * (1 - cr[..., None] * .4); c = lerp(c, c * .45, cut[..., None])
    sm = .62 + (big - .5) * .3 - scuff * .28 - cut * .3 - cr * .2
    save('floor', lin2srgb(c), sm, normal_map(blur(h, .7), 3.0, .006))

# ------------------------------------------------------------------ ballast (2 m of crushed stone)
def ballast():
    r = rng(41)
    F1, F2, ids, n = worley(42, 30)
    F1b, F2b, idb, nb = worley(43, 52)
    stone = smooth(.0, .5, F2 - F1); small = smooth(.0, .45, F2b - F1b)
    hs = r.random(n)[ids]; hsb = r.random(nb)[idb]
    fine = spectral(44, 1.0, 40)
    big_h = stone * (.55 + hs * .45); small_h = small * (.25 + hsb * .3)
    h = np.maximum(big_h, small_h); h = norm01(h + (fine - .5) * .06)
    pal = srgb2lin(np.array([hexc('#a49c90'), hexc('#928a7f'), hexc('#b8afa1'), hexc('#857f77'), hexc('#9c8674'), hexc('#c4bdb1')]))
    cb = pal[r.choice(len(pal), n, p=[.3, .22, .18, .12, .08, .1])][ids] * (.9 + r.random(n)[ids][..., None] * .2)
    cs = pal[r.choice(len(pal), nb)][idb] * (.8 + r.random(nb)[idb][..., None] * .3)
    c = np.where((big_h >= small_h)[..., None], cb, cs)
    c = c * (.9 + fine[..., None] * .2)
    c = c * (.5 + .5 * smooth(.05, .45, h))[..., None]            # dark between the stones
    dust = spectral(45, 2.4, 1, 6)
    c = c * (.88 + dust[..., None] * .22)
    sm = .12 + h * .14
    save('ballast', lin2srgb(c), sm, normal_map(blur(h, .8), 2.0, .03))

# ------------------------------------------------------------------ cast concrete, tinted by the material (2.4 m)
def concrete():
    fine = spectral(51, 1.0, 30); mid = spectral(52, 2.0, 2, 40); big = spectral(53, 2.6, 1, 6)
    pits = smooth(.78, .84, spectral(54, .8, 50))
    board = (V * 4) % 1                                           # form boards every 0.6 m
    seam = 1 - smooth(.0, .012, np.minimum(board, 1 - board))
    grainb = norm01(ndi.gaussian_filter(spectral(55, 1.0, 60), (0, 9), mode='wrap'))
    streak = norm01(ndi.gaussian_filter(spectral(56, 1.2, 12), (60, 2.5), mode='wrap'))    # rain marks run down
    h = norm01(.7 + (mid - .5) * .1 + (fine - .5) * .05 + (grainb - .5) * .05 - pits * .14 - seam * .1)
    m = (.9 + mid * .14) * (.96 + fine * .08) * (1 - pits * .3) * (1 - seam * .12) * (.9 + big * .14) * (1 - smooth(.5, 1.0, streak) * .16)
    c = np.dstack([m * 1.0, m * .995, m * .985])
    sm = .22 + (mid - .5) * .1 - pits * .1 + smooth(.55, .9, streak) * .08
    save('concrete', np.clip(c, 0, 1), sm, normal_map(blur(h, .5), 2.4, .008))

# ------------------------------------------------------------------ ribbed steel sheet, tinted (2 m: ribs every 0.2 m)
def corrugated():
    ribs = 10
    t = (U * ribs) % 1
    prof = smooth(.12, .3, t) * (1 - smooth(.62, .8, t))          # a flat-topped rib
    fine = spectral(61, 1.0, 40); mid = spectral(62, 2.0, 2, 30)
    streak = norm01(ndi.gaussian_filter(spectral(63, 1.4, 10), (30, 1.2), mode='wrap'))
    rust = smooth(.66, .95, norm01(ndi.gaussian_filter(spectral(64, 1.0, 14), (70, 1.6), mode='wrap'))) * smooth(.3, .75, spectral(66, 2.4, 1, 5))
    lap = 1 - smooth(.0, .01, np.minimum(V, 1 - V))               # sheets lap every 2 m
    dents = blur(smooth(.86, .95, spectral(65, 2.4, 3, 12)), 6)
    h = norm01(prof * 1.0 + (fine - .5) * .02 - dents * .15 + lap * .05)
    ao = .78 + .22 * smooth(.0, .5, prof)
    m = ao * (.93 + mid * .1) * (1 - smooth(.5, .9, streak) * .16) * (1 - lap * .25)
    c = np.dstack([m, m, m])
    rc = np.array([.62, .36, .2])
    c = lerp(c, c * rc, (rust * .55)[..., None])
    sm = .46 + (mid - .5) * .16 - rust * .3 - smooth(.5, .9, streak) * .12
    save('corrugated', np.clip(c, 0, 1), sm, normal_map(blur(h, 1.2), 2.0, .028))

# ------------------------------------------------------------------ roofing felt, tinted (2 m, a seam every metre)
def roofing():
    fine = spectral(71, .6, 60); mid = spectral(72, 2.0, 2, 30); big = spectral(73, 2.6, 1, 5)
    t = (U * 2) % 1; seam = smooth(.0, .012, t) * (1 - smooth(.012, .03, t)) + (1 - smooth(.0, .008, t))
    seam = np.clip(seam, 0, 1)
    h = norm01(.5 + (fine - .5) * .25 + (mid - .5) * .1 + seam * .35)
    m = (.88 + fine * .2) * (.92 + mid * .12) * (.9 + big * .16) * (1 - (1 - smooth(.0, .01, t)) * .3)
    pud = smooth(.62, .85, big)
    c = np.dstack([m, m, m]) * (1 - pud[..., None] * .14)
    sm = .12 + pud * .3 + seam * .05
    save('roofing', np.clip(c, 0, 1), sm, normal_map(blur(h, .5), 2.0, .006))

# ------------------------------------------------------------------ painted steel, tinted (1.5 m)
def metal():
    fine = spectral(81, .8, 50); mid = spectral(82, 2.0, 2, 30); big = spectral(83, 2.6, 1, 6)
    r = rng(84); scr = np.zeros((N, N))
    for _ in range(28):
        x, y = r.random(2) * N; a = r.normal(0, .5) + (0 if r.random() < .5 else 1.57); L = int(r.uniform(30, 220))
        xs = (x + np.cos(a) * np.arange(L)).astype(int) % N; ys = (y + np.sin(a) * np.arange(L)).astype(int) % N
        scr[ys, xs] = r.uniform(.4, 1)
    scr = np.clip(blur(scr, .7) * 1.4, 0, 1)
    wear = smooth(.6, .85, mid) * .5
    h = norm01(.5 + (fine - .5) * .08 + (mid - .5) * .05 - scr * .2)
    m = (.96 + mid * .05) * (.97 + fine * .05) * (.94 + big * .08) * (1 - scr * .06)
    c = np.dstack([m, m, m])
    sm = .5 + (big - .5) * .2 - wear * .3 - scr * .25
    save('metal', np.clip(c, 0, 1), sm, normal_map(blur(h, .6), 1.5, .0025))

# ------------------------------------------------------------------ weathered timber, tinted (1.2 m, grain along u)
def wood():
    g = spectral(91, 1.2, 4, 90)
    grain = norm01(ndi.gaussian_filter(g, (.8, 22), mode='wrap'))
    rings = norm01(np.sin((V * 30 + (ndi.gaussian_filter(spectral(92, 2.2, 1, 5), (0, 60), mode='wrap') - .5) * 2.2) * 6.283) * .5 + .5)
    rings = norm01(ndi.gaussian_filter(rings, (.6, 6), mode='wrap'))
    fine = spectral(93, 1.0, 40); big = spectral(94, 2.4, 1, 6)
    cr = smooth(.80, .9, norm01(ndi.gaussian_filter(spectral(95, 1.0, 20), (.7, 30), mode='wrap')))
    h = norm01(.6 + (grain - .5) * .6 + (rings - .5) * .08 - cr * .5 + (fine - .5) * .05)
    m = (.74 + grain * .36) * (.94 + rings * .08) * (.9 + big * .16) * (1 - cr * .5)
    c = np.dstack([m, m * .97, m * .92])
    sm = .16 + grain * .1 - cr * .1
    save('wood', np.clip(c, 0, 1), sm, normal_map(blur(h, .6), 1.2, .008))

# ------------------------------------------------------------------ grass, tinted (6 m)
def grass():
    fine = spectral(101, .7, 30); mid = spectral(102, 1.8, 3, 60); big = spectral(103, 2.6, 1, 6)
    blades = norm01(ndi.gaussian_filter(spectral(104, .6, 60), (2.2, .6), mode='wrap'))
    clump = smooth(.2, .85, mid)
    h = norm01(clump * .6 + blades * .3 + fine * .1)
    m = (.9 + clump * .12) * (.84 + blades * .3) * (.94 + fine * .1)
    dry = smooth(.55, 1.0, big) * .35
    c = np.dstack([m * (1 + dry * .18), m * (1 + dry * .04), m * (1 - dry * .25)])
    c *= (.92 + big[..., None] * .14)
    sm = .08 + clump * .08
    save('grass', np.clip(c, 0, 1), sm, normal_map(blur(h, .8), 6.0, .05))

which = sys.argv[2:] or ['brick', 'slabs', 'floor', 'ballast', 'concrete', 'corrugated', 'roofing', 'metal', 'wood', 'grass']
for w in which: globals()[w]()
