# Textures for The House and Backyard looks (Scripts/Maps/HouseLook.cs, YardLook.cs), made from noise:
#   plaster   painted wall: near white, a faint roller stipple
#   carpet    loop pile: near white (it takes the room's colour), soft and matt
#   tiles     six ceramic tiles a side with grey grout: glossy tiles, matt joints
# Each is <name>_albedo.png (smoothness in alpha) and <name>_normal.png, and tiles seamlessly.
# python3 housetex.py <out folder> [plaster carpet tiles]      needs numpy and Pillow
import sys, os
import numpy as np
from PIL import Image

def noise(n, beta, seed):
    rng = np.random.default_rng(seed)
    fy = np.fft.fftfreq(n)[:, None] * n; fx = np.fft.fftfreq(n)[None, :] * n
    f = np.sqrt(fx * fx + fy * fy); f[0, 0] = 1
    amp = 1.0 / f ** beta; amp[0, 0] = 0
    field = np.real(np.fft.ifft2(amp * np.exp(2j * np.pi * rng.random((n, n)))))
    field -= field.min(); field /= field.max()
    return field

def save(out, name, rgb, smooth, height, bump):
    a = np.dstack([np.clip(rgb, 0, 255), np.clip(smooth * 255, 0, 255)]).astype(np.uint8)
    Image.fromarray(a, 'RGBA').save(os.path.join(out, name + '_albedo.png'))
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * bump; dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * bump
    nz = 1 / np.sqrt(dx * dx + dy * dy + 1)
    nrm = np.dstack([(-dx * nz) * .5 + .5, (dy * nz) * .5 + .5, nz * .5 + .5])
    Image.fromarray((nrm * 255).astype(np.uint8), 'RGB').save(os.path.join(out, name + '_normal.png'))

def plaster(out, n=1024):
    fine = noise(n, .9, 1); cloud = noise(n, 2.6, 2)
    v = 244 - (cloud - .5) * 10 - (fine - .5) * 6
    save(out, 'plaster', np.dstack([v, v, v * .995]), np.full((n, n), .22) + (fine - .5) * .06, fine + cloud * .6, 2.2)

def carpet(out, n=1024):
    pile = noise(n, .35, 3); tuft = noise(n, 1.4, 4); cloud = noise(n, 2.8, 5)
    v = 232 - (pile - .5) * 46 - (tuft - .5) * 22 - (cloud - .5) * 10
    save(out, 'carpet', np.dstack([v, v, v]), np.full((n, n), .06), pile * 1.0 + tuft * .8, 5.0)

def tiles(out, n=1024, per=6):
    c = n // per; yy, xx = np.mgrid[0:n, 0:n]
    u = (xx % c) / c; v = (yy % c) / c
    edge = np.minimum(np.minimum(u, 1 - u), np.minimum(v, 1 - v))          # distance to the nearest joint, in tiles
    joint = np.clip(edge / .018, 0, 1); bevel = np.clip(edge / .05, 0, 1)
    rng = np.random.default_rng(6); shade = rng.random((per, per))[(yy // c) % per, (xx // c) % per]
    glaze = noise(n, 2.4, 7)
    tile = 242 + (shade - .5) * 9 - (glaze - .5) * 6
    val = 172 * (1 - joint) + tile * joint
    smooth = .28 * (1 - joint) + (.88 - (glaze - .5) * .08) * joint
    save(out, 'tiles', np.dstack([val, val, val * .995]), smooth, bevel + glaze * .03, 3.0)

if __name__ == '__main__':
    out = sys.argv[1]; which = sys.argv[2:] or ['plaster', 'carpet', 'tiles']
    os.makedirs(out, exist_ok=True)
    for w in which: globals()[w](out)
