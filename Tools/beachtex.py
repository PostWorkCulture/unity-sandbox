# Beach Please surfaces (same kit as railtex.py, which must sit next to this file): sand detail and base, rock, deck planks,
# thatch, bamboo, wicker.   python3 beachtex.py <out folder> [names]
import numpy as np, sys, os
HERE = os.path.dirname(os.path.abspath(__file__))
exec(open(os.path.join(HERE, 'railtex.py')).read().split("which = sys.argv[2:]")[0])   # the noise, normal-map and save helpers
from scipy.interpolate import LinearNDInterpolator

def save_detail(name, grey, normal):
    Image.fromarray((np.clip(grey, 0, 1) * 255 + .5).astype(np.uint8), 'L').convert('RGB').save(f'{OUT}/{name}_detail.png')
    Image.fromarray(((normal * .5 + .5) * 255 + .5).astype(np.uint8), 'RGB').save(f'{OUT}/{name}_normal.png')
    print(name, 'detail mean', round(float(grey.mean()), 3))

# ------------------------------------------------------------------ sand detail (3 m): wind ripples and grain, mid grey = no change
def sand():
    warp = (spectral(201, 2.2, 1, 6) - .5) * 1.6 + (spectral(202, 2.0, 4, 20) - .5) * .25
    rip = np.sin((V * 14 + warp) * 6.2831853)                      # ripples about 21 cm apart, running along u
    rip = np.sign(rip) * np.abs(rip) ** .8
    fade = smooth(.25, .75, spectral(203, 2.4, 1, 5))               # ripples come and go
    grain = spectral(204, .5, 80); mid = spectral(205, 2.0, 3, 40)
    pock = blur(smooth(.86, .93, spectral(206, 1.6, 6, 40)), 2)      # scuffs and soft dents
    h = norm01(rip * .5 * (.35 + .65 * fade) + (mid - .5) * .5 - pock * .6 + (grain - .5) * .12)
    g = .5 + (h - .5) * .16 + (grain - .5) * .09 + (mid - .5) * .05
    save_detail('sand', g, normal_map(blur(h, .9), 3.0, .022))

# ------------------------------------------------------------------ sand base: the map's own vertex colours (dry sand fading to wet and to the sea bed),
# drawn top-down over x -60..140, z -80..80, with smoothness in alpha (a glossy wet band where the waves run up)
def sand_base():
    # the sand mesh from the map's extras: float32 positions, normals, uvs and colours, then the indices
    import json
    mdir = os.path.join(HERE, '..', 'Assets', 'StreetLife', 'Maps', 'wreck')
    rec = json.load(open(os.path.join(mdir, 'extras.json')))['meshes'][0]; n = rec['verts']
    f = np.frombuffer(open(os.path.join(mdir, 'extras.bytes'), 'rb').read(), dtype=np.float32, count=n * 11, offset=rec['offset'])
    pos = f[:n * 3].reshape(n, 3); col = f[n * 8:n * 11].reshape(n, 3)
    W, H = 1024, 820
    xs = np.linspace(-60, 140, W); zs = np.linspace(80, -80, H)     # image row 0 is v = 1 = z max
    X, Z = np.meshgrid(xs, zs)
    c = LinearNDInterpolator(pos[:, [0, 2]], col, fill_value=0)(X, Z)
    bad = (c.sum(-1) == 0)
    if bad.any():
        from scipy.interpolate import NearestNDInterpolator
        c[bad] = NearestNDInterpolator(pos[:, [0, 2]], col)(X[bad], Z[bad])
    shore = 18 + 3 * np.sin(-Z * .11) + 1.5 * np.sin(-Z * .27 + 1)
    dist = X - shore
    r = rng(211); n = r.random((H, W)); n = ndi.gaussian_filter(n, 14); n = norm01(n)
    n2 = norm01(ndi.gaussian_filter(r.random((H, W)), 4))
    dry = (1 - smooth(-6, 2, dist))[..., None]
    lum = c.mean(-1, keepdims=True)
    c = c * (1 - dry) + (c * .72 + lum * .28) * 1.16 * dry
    c = c * (.94 + n[..., None] * .12) * (.97 + n2[..., None] * .06)
    wet = smooth(-4.5, -1.2, dist) * (1 - smooth(2.5, 7, dist))     # the swash zone
    c = c * (1 - wet[..., None] * .14)
    sm = .1 + wet * .62 + smooth(2, 8, dist) * .1
    a = np.dstack([lin2srgb(np.clip(c, 0, 1)), np.clip(sm, 0, 1)])
    Image.fromarray((a * 255 + .5).astype(np.uint8), 'RGBA').save(f'{OUT}/sand_base.png')
    print('sand_base', c.reshape(-1, 3).mean(0).round(3))

# ------------------------------------------------------------------ rock, tinted (4 m): bedded stone, cracks, pitting
def rock():
    big = spectral(221, 2.6, 1, 6); mid = spectral(222, 2.0, 3, 40); fine = spectral(223, 1.0, 30)
    beds = norm01(np.sin((V * 9 + (ndi.gaussian_filter(spectral(224, 2.2, 1, 6), (0, 40), mode='wrap') - .5) * 3.2 + (mid - .5) * .5) * 6.2831853))
    beds = smooth(.2, .8, beds)
    F1, F2, ids, n = worley(225, 4, 1.0)
    crack = (1 - smooth(.0, .03, F2 - F1 + (mid - .5) * .06)) * smooth(.35, .7, spectral(229, 2.2, 1, 6))
    r = rng(226); blockh = r.random(n)[ids]
    pits = smooth(.8, .88, spectral(227, .9, 40))
    h = norm01(.5 + (big - .5) * .7 + (beds - .5) * .07 + (blockh - .5) * .3 + (mid - .5) * .45 + (fine - .5) * .1 - crack * .3 - pits * .12)
    m = (.84 + big * .18) * (.97 + beds * .05) * (.92 + (blockh - .5) * .14) * (.95 + fine * .08) * (1 - crack * .25) * (1 - pits * .2)
    m *= (.8 + .2 * cavity(h, 6))
    warm = smooth(.5, .9, spectral(228, 2.4, 1, 5))
    c = np.dstack([m * (1 + warm * .05), m, m * (1 - warm * .07)])
    sm = .14 + (big - .5) * .08 - crack * .08
    save('rock', np.clip(c, 0, 1), sm, normal_map(blur(h, .8), 4.0, .09))

# ------------------------------------------------------------------ deck boards, tinted (1.68 m: 12 boards of 14 cm running along u)
def planks():
    nb = 12; r = rng(231)
    row = np.floor(V * nb).astype(int); fv = V * nb - row
    shift = r.random(nb)[row]; tone = (.88 + r.random(nb) * .22)[row]
    uu = (U + shift) % 1
    g = spectral(232, 1.2, 4, 110)
    grain = norm01(ndi.gaussian_filter(g, (.7, 26), mode='wrap'))
    # each board gets its own stretch of grain
    grain = np.take_along_axis(grain, ((np.arange(N)[None, :] + (shift * N).astype(int)) % N), axis=1)
    gap = 1 - smooth(.0, .06, np.minimum(fv, 1 - fv))
    butt = ((uu * 2) % 1); buttj = (1 - smooth(.0, .004, np.minimum(butt, 1 - butt))) * (r.random(nb)[row] > .45)
    cup = (np.abs(fv - .5) * 2) ** 2                                   # boards cup a little: edges sit lower
    fine = spectral(233, 1.0, 40); big = spectral(234, 2.4, 1, 6)
    cr = smooth(.84, .93, norm01(ndi.gaussian_filter(spectral(235, 1.0, 20), (.7, 34), mode='wrap')))
    h = norm01(.7 + (grain - .5) * .35 - cup * .12 - gap * .9 - buttj * .6 - cr * .3 + (fine - .5) * .04)
    m = tone * (.8 + grain * .26) * (.93 + big * .12) * (1 - cr * .35) * (1 - gap * .75) * (1 - buttj * .5)
    c = np.dstack([m, m * .985, m * .95])
    sm = .2 + grain * .12 - cr * .1 - gap * .15 + (big - .5) * .1
    save('planks', np.clip(c, 0, 1), sm, normal_map(blur(h, .6), 1.68, .012))

# ------------------------------------------------------------------ thatch, tinted (2 m): reeds running along v in overlapping courses
def thatch():
    r = rng(241)
    strands = norm01(ndi.gaussian_filter(spectral(242, .6, 60), (30, .55), mode='wrap'))
    clump = norm01(ndi.gaussian_filter(spectral(243, 1.4, 10), (40, 3), mode='wrap'))
    courses = 4; fv = (V * courses + (spectral(244, 2.0, 2, 12) - .5) * .25) % 1
    lap = fv ** 1.5                                                    # each course rises to a ragged cut edge
    edge = smooth(.9, 1.0, fv)
    big = spectral(245, 2.4, 1, 6)
    h = norm01(lap * .55 + (strands - .5) * .35 + (clump - .5) * .25)
    m = (.72 + strands * .34) * (.88 + clump * .16) * (.78 + lap * .26) * (.92 + big * .12) * (1 - edge * .0)
    m *= (.6 + .4 * smooth(.0, .12, fv))                               # shadow under the course above
    c = np.dstack([m, m * .98, m * .9])
    sm = .1 + strands * .08
    save('thatch', np.clip(c, 0, 1), sm, normal_map(blur(h, .7), 2.0, .05))

# ------------------------------------------------------------------ bamboo (1.2 m: 8 canes standing along v), its own colour
def bamboo():
    nc = 8; r = rng(251)
    col_i = np.floor(U * nc).astype(int); fu = U * nc - col_i
    rad = np.sqrt(np.clip(1 - (fu * 2 - 1) ** 2, 0, 1))                # round section
    off = r.random(nc)[col_i]; tone = (.86 + r.random(nc) * .26)[col_i]; hue = (r.random(nc) - .5)[col_i]
    vv = (V * 3 + off) % 1                                             # a node every 40 cm
    node = np.exp(-((np.minimum(vv, 1 - vv)) / .012) ** 2)
    fib = norm01(ndi.gaussian_filter(spectral(252, .8, 50), (24, .6), mode='wrap'))
    big = spectral(253, 2.4, 1, 6)
    h = norm01(rad * 1.0 + node * .12 * rad + (fib - .5) * .05)
    base = srgb2lin(hexc('#d2aa5c'))[None, None, :] * tone[..., None]
    base = base * (1 + hue[..., None] * np.array([.06, .0, -.12]))
    c = base * (.88 + fib[..., None] * .2) * (.93 + big[..., None] * .12)
    c = c * (1 - node[..., None] * .35)
    c = c * (.35 + .65 * smooth(.0, .55, rad))[..., None]              # dark between the canes
    sm = (.42 + fib * .12 - node * .15) * smooth(.1, .5, rad)
    save('bamboo', lin2srgb(c), sm, normal_map(blur(h, .8), 1.2, .05))

# ------------------------------------------------------------------ woven cane panel (0.8 m), its own colour
def wicker():
    k = 16
    a = np.sin(U * k * np.pi); b = np.sin(V * k * np.pi)
    cu = np.floor(U * k).astype(int); cv = np.floor(V * k).astype(int)
    over = ((cu + cv) % 2 == 0)                                        # which strand is on top in this cell
    fu = U * k - cu; fv = V * k - cv
    su = np.sqrt(np.clip(1 - (fv * 2 - 1) ** 2, 0, 1)); sv = np.sqrt(np.clip(1 - (fu * 2 - 1) ** 2, 0, 1))   # strand cross-sections
    bow_u = .6 + .4 * np.sin(fu * np.pi); bow_v = .6 + .4 * np.sin(fv * np.pi)
    hu = su * np.where(over, bow_u, .45 * (1 - np.sin(fu * np.pi) * .6)); hv = sv * np.where(~over, bow_v, .45 * (1 - np.sin(fv * np.pi) * .6))
    h = np.maximum(hu, hv); top_u = hu >= hv
    fib_u = norm01(ndi.gaussian_filter(spectral(261, .8, 50), (.6, 14), mode='wrap')); fib_v = norm01(ndi.gaussian_filter(spectral(262, .8, 50), (14, .6), mode='wrap'))
    fib = np.where(top_u, fib_u, fib_v)
    r = rng(263); tone = np.where(top_u, (.85 + r.random(k) * .3)[cv], (.85 + r.random(k) * .3)[cu])
    big = spectral(264, 2.4, 1, 6)
    base = srgb2lin(hexc('#8a5a34'))[None, None, :]
    c = base * tone[..., None] * (.82 + fib[..., None] * .3) * (.92 + big[..., None] * .14) * (.3 + .7 * smooth(.05, .6, h))[..., None]
    sm = .3 + fib * .14
    save('wicker', lin2srgb(c), sm * smooth(.1, .5, h), normal_map(blur(norm01(h), .9), .8, .02))

which = sys.argv[2:] or ['sand', 'sand_base', 'rock', 'planks', 'thatch', 'bamboo', 'wicker']
for w in which: globals()[w]()
