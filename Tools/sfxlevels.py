# Measures how loud every sound effect and music track is, so levels can be balanced by numbers.
# Usage: python3 Tools/sfxlevels.py [folder] [out.json]   (needs ffmpeg and numpy)
# loud100 / loud400 = the loudest 100 ms / 400 ms of the file after a rough hearing-weighting filter, in dB below full
# scale. Files that peak the same can differ by 15 dB here; Sfx.Trim in Assets/StreetLife/Scripts/Sfx.cs evens them out.
import subprocess, sys, os, math, json
import numpy as np
d = sys.argv[1] if len(sys.argv) > 1 else "Assets/StreetLife/Resources/StreetLife/sfx"
out = {}
for fn in sorted(os.listdir(d)):
    if fn.endswith('.meta') or os.path.isdir(os.path.join(d, fn)): continue
    p = os.path.join(d, fn)
    def dec(filt):
        a = ['ffmpeg', '-v', 'error', '-i', p] + (['-af', filt] if filt else []) + ['-ac', '1', '-ar', '44100', '-f', 'f32le', '-']
        return np.frombuffer(subprocess.run(a, capture_output=True).stdout, dtype=np.float32)
    raw = dec(None); k = dec('highpass=f=38,highshelf=f=1500:g=4')
    if len(raw) == 0: print(fn, 'DECODE FAIL'); continue
    db = lambda v: 20 * math.log10(max(v, 1e-9))
    peak = db(float(np.max(np.abs(raw))))
    def winmax(x, ms):
        n = max(1, int(44100 * ms / 1000))
        if len(x) < n: return db(float(np.sqrt(np.mean(x ** 2))))
        c = np.cumsum(np.concatenate([[0], x.astype(np.float64) ** 2]))
        return db(float(np.sqrt(np.max(c[n:] - c[:-n]) / n)))
    rms = db(float(np.sqrt(np.mean(k.astype(np.float64) ** 2))))
    out[fn.rsplit('.', 1)[0]] = dict(dur=round(len(raw) / 44100, 2), peak=round(peak, 1), m100=round(winmax(k, 100), 1), m400=round(winmax(k, 400), 1), rms=round(rms, 1))
if len(sys.argv) > 2: json.dump(out, open(sys.argv[2], "w"), indent=0)
for n, v in sorted(out.items(), key=lambda kv: -kv[1]['m100']):
    print(f"{n:12s} dur {v['dur']:6.2f}s  peak {v['peak']:6.1f}  loud100 {v['m100']:6.1f}  loud400 {v['m400']:6.1f}  rms {v['rms']:6.1f}")
