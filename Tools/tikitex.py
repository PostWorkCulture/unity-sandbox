# Textures for the Tiki Bar on Beach Please (Scripts/Maps/TikiBar.cs), made from noise so nothing is downloaded:
#   marble_albedo.png / marble_normal.png   polished white marble with grey and gold veins (smoothness in alpha), tiles
#   onyx.png                                honey onyx, the lit panels behind the bottles (used as the light they give off)
#   bar_labels.png                          a 4 x 4 sheet: twelve bottle labels (invented names), then cap and foil colours
# python3 tikitex.py <out folder> [marble onyx labels]      needs numpy and Pillow
import sys, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

def noise(n, beta, seed, m=None):
    """tileable fractal noise in 0..1 (random phases under a 1/f^beta spectrum)"""
    m = m or n
    rng = np.random.default_rng(seed)
    fy = np.fft.fftfreq(m)[:, None] * m; fx = np.fft.fftfreq(n)[None, :] * n
    f = np.sqrt(fx * fx + fy * fy); f[0, 0] = 1
    amp = 1.0 / f ** beta; amp[0, 0] = 0
    field = np.real(np.fft.ifft2(amp * np.exp(2j * np.pi * rng.random((m, n)))))
    field -= field.min(); field /= field.max()
    return field

def warp(img, wx, wy, amount):
    """look the picture up at places pushed about by two noise fields (wraps, so it still tiles)"""
    h, w = img.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    sx = (xx + (wx - .5) * amount).astype(np.int64) % w; sy = (yy + (wy - .5) * amount).astype(np.int64) % h
    return img[sy, sx]

def ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)

def marble(out, n=1024):
    wx, wy = noise(n, 2.4, 11), noise(n, 2.4, 12)
    big = warp(noise(n, 2.6, 1), wx, wy, 180)
    fine = warp(noise(n, 2.0, 2), wx, wy, 90)
    ridge1 = (1 - np.abs(2 * big - 1)) ** 26            # a few bold veins
    ridge2 = (1 - np.abs(2 * fine - 1)) ** 60           # hairlines
    where = ss(.35, .7, noise(n, 3.0, 3))               # veins come and go
    vein = np.clip(ridge1 * (.35 + .65 * where) + ridge2 * .35 * where, 0, 1)
    cloud = noise(n, 2.8, 4)
    base = np.array([244, 241, 235], float) - (cloud[..., None] - .5) * np.array([14, 14, 12])
    grey = np.array([128, 124, 120], float); gold = np.array([176, 142, 92], float)
    tone = ss(.55, .8, noise(n, 3.0, 5))[..., None]
    veincol = grey * (1 - tone) + gold * tone
    rgb = base * (1 - vein[..., None]) + veincol * vein[..., None]
    alpha = 238 - vein * 26                              # polished; the veins a touch duller
    Image.fromarray(np.dstack([np.clip(rgb, 0, 255), alpha]).astype(np.uint8), 'RGBA').save(os.path.join(out, 'marble_albedo.png'))
    hgt = -vein * .6 + cloud * .15                       # veins sit a hair low
    dx = (np.roll(hgt, -1, 1) - np.roll(hgt, 1, 1)) * 1.2; dy = (np.roll(hgt, -1, 0) - np.roll(hgt, 1, 0)) * 1.2
    nz = 1 / np.sqrt(dx * dx + dy * dy + 1)
    nrm = np.dstack([(-dx * nz) * .5 + .5, (dy * nz) * .5 + .5, nz * .5 + .5])
    Image.fromarray((nrm * 255).astype(np.uint8), 'RGB').save(os.path.join(out, 'marble_normal.png'))

def onyx(out, w=1024, h=512):
    wx, wy = noise(w, 2.6, 21, h), noise(w, 2.6, 22, h)
    yy = np.mgrid[0:h, 0:w][0] / h
    flow = warp(noise(w, 2.8, 23, h), wx, wy, 120)
    bands = .5 + .5 * np.sin(2 * np.pi * (yy * 2.2 + flow * 3.4))
    fine = .5 + .5 * np.sin(2 * np.pi * (yy * 9 + flow * 14 + wx * 2))
    t = np.clip(bands * .75 + fine * .25, 0, 1)
    deep = np.array([198, 118, 40], float); honey = np.array([255, 176, 74], float); cream = np.array([255, 232, 178], float)
    rgb = deep * (1 - ss(0, .55, t))[..., None] + honey * (ss(0, .55, t) * (1 - ss(.6, 1, t)))[..., None] + cream * ss(.6, 1, t)[..., None]
    crack = (1 - np.abs(2 * warp(noise(w, 2.0, 24, h), wx, wy, 60) - 1)) ** 50
    rgb = rgb * (1 - crack[..., None] * .3)
    # a little darker towards the edges of the panel, as a lit stone slab is
    xx = np.mgrid[0:h, 0:w][1] / w
    edge = (.82 + .18 * ss(0, .08, xx) * ss(0, .08, 1 - xx) * ss(0, .1, yy) * ss(0, .1, 1 - yy))
    Image.fromarray(np.clip(rgb * edge[..., None], 0, 255).astype(np.uint8), 'RGB').save(os.path.join(out, 'onyx.png'))

def font(size, bold=True, serif=True):
    for p in ['/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf' if serif else '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',
              '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', '/usr/share/fonts/truetype/liberation/LiberationSerif-Bold.ttf']:
        if os.path.exists(p): return ImageFont.truetype(p, size)
    return ImageFont.load_default()

LABELS = [  # name, small line, paper, ink, trim
    ('KOA', 'AGED RUM', '#f3ead6', '#2a1a10', '#b8862e'), ('ISLA', 'DRY GIN', '#1d3557', '#f4efe6', '#c9a14a'),
    ('REEF', 'VODKA', '#f6f7f8', '#14344a', '#2a9d8f'), ('ORO', 'TEQUILA', '#16171c', '#e9c46a', '#e9c46a'),
    ('MAUNA', 'SPICED', '#7a1f1f', '#f3ead6', '#e9c46a'), ('TIDE', 'BLANCO', '#dff1f2', '#1f5a66', '#1f9a9a'),
    ('COPRA', 'COCONUT', '#fbf6ea', '#6b4428', '#3bb54a'), ('SOL', 'APERITIVO', '#e8563a', '#fff6e0', '#fff6e0'),
    ('NO.7', 'RESERVE', '#2b2b2b', '#f4efe6', '#b8862e'), ('PALMA', 'LIQUEUR', '#0f5a3c', '#f3ead6', '#e9c46a'),
    ('BRUT', 'CUVEE', '#f1e6c8', '#3a2414', '#b8862e'), ('LUNA', 'MEZCAL', '#3b2a55', '#f1e6c8', '#c9a14a'),
]
SWATCH = ['#c9a14a', '#17181c', '#a3211c', '#c9ccd2']   # brass foil, black, red wax, silver

def labels(out, cell=256):
    sheet = Image.new('RGB', (cell * 4, cell * 4), '#000000')
    for i, (name, small, paper, ink, trim) in enumerate(LABELS):
        im = Image.new('RGB', (cell, cell), paper); d = ImageDraw.Draw(im)
        d.rectangle([10, 10, cell - 11, cell - 11], outline=trim, width=5)
        d.rectangle([20, 20, cell - 21, cell - 21], outline=trim, width=1)
        size = 74
        while True:
            f = font(size); tw = d.textlength(name, font=f)
            if tw <= cell - 64 or size <= 30: break
            size -= 4
        d.text(((cell - tw) / 2, 62), name, font=f, fill=ink)
        d.line([60, 150, cell - 60, 150], fill=trim, width=3)
        f2 = font(24, serif=False); tw = d.textlength(small, font=f2)
        d.text(((cell - tw) / 2, 164), small, font=f2, fill=ink)
        d.ellipse([cell / 2 - 9, 204, cell / 2 + 9, 222], fill=trim)
        sheet.paste(im, ((i % 4) * cell, (i // 4) * cell))
    for i, c in enumerate(SWATCH):
        sheet.paste(Image.new('RGB', (cell, cell), c), (i * cell, 3 * cell))
    sheet.save(os.path.join(out, 'bar_labels.png'))

if __name__ == '__main__':
    out = sys.argv[1]; which = sys.argv[2:] or ['marble', 'onyx', 'labels']
    os.makedirs(out, exist_ok=True)
    if 'marble' in which: marble(out)
    if 'onyx' in which: onyx(out)
    if 'labels' in which: labels(out)
