# Builds the game's rifle, LMG and scout shots (and a fuller explosion) from The Free Firearm Sound Library
# (CC0, Ben Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney; opengameart.org/content/the-free-firearm-sound-library),
# unpacked in Workspace/Sounds/Prepared SFX Library (not in git: 345 MB).
# A raw recorded gunshot is a few milliseconds of crack and then almost nothing, so at the same peak it sounds far
# quieter than other effects. Each shot here is the library's first near shot, pushed into a soft clipper with a
# little added low end (the usual way recorded gunshots are made loud), faded, and saved as 44.1 kHz mono.
# Usage (from the project folder, needs ffmpeg and numpy): python3 Tools/gunsounds.py
import subprocess, numpy as np, math, os
SR = 44100
LIB = 'Workspace/Sounds/Prepared SFX Library/'
OUT = 'Assets/StreetLife/Resources/StreetLife/sfx/'
def db(v): return 20*math.log10(max(v, 1e-9))
def first_shot(path, length):
    raw = subprocess.run(['ffmpeg','-v','error','-i',path,'-f','f32le','-'], capture_output=True).stdout
    x = np.frombuffer(raw, dtype=np.float32).reshape(-1, 2); sr = 96000
    ch = int(np.argmax(np.abs(x).max(axis=0))); m = x[:, ch]
    on = int(np.argmax(np.abs(m) > 0.05*np.abs(m).max())); a = max(0, on - int(sr*.002))
    return m[a:a+int(sr*length)].copy(), sr
def process(seg, sr, drive, length, bass=3.0, fade=True):
    f = f",afade=t=out:st={length*0.45:.3f}:d={length*0.55:.3f}:curve=exp" if fade else ""
    af = f"highpass=f=45,bass=g={bass}:f=120,volume={drive}dB,asoftclip=type=tanh:oversample=4{f},aresample={SR}"
    out = subprocess.run(['ffmpeg','-v','error','-f','f32le','-ar',str(sr),'-ac','1','-i','-','-af',af,'-f','f32le','-'], input=seg.tobytes(), capture_output=True).stdout
    y = np.frombuffer(out, dtype=np.float32).astype(np.float64); return y/np.abs(y).max()*0.89
def loud(y, ms=100):
    k = subprocess.run(['ffmpeg','-v','error','-f','f64le','-ar',str(SR),'-ac','1','-i','-','-af','highpass=f=38,highshelf=f=1500:g=4','-f','f64le','-'], input=y.tobytes(), capture_output=True).stdout
    k = np.frombuffer(k, dtype=np.float64); n = int(SR*ms/1000); c = np.cumsum(np.concatenate([[0], k**2]))
    return db(math.sqrt(np.max(c[n:]-c[:-n])/n))
def save(y, path):
    subprocess.run(['ffmpeg','-v','error','-y','-f','f64le','-ar',str(SR),'-ac','1','-i','-','-c:a','pcm_s16le',path], input=y.tobytes(), check=True)
if __name__ == '__main__':
    #        file          library recording   seconds  drive dB
    for name, src, length, drive in [('gun_ar15', 'AR-15/D_32P.wav', .55, 10), ('gun_ak47', 'AK-47/C_28P.wav', .55, 8),
                                     ('gun_1917', '1917/B_24P.wav', .6, 10), ('gun_tikka', 'Tikka/W_29P.wav', 1.3, 12)]:
        seg, sr = first_shot(LIB + src, length); y = process(seg, sr, drive, length); save(y, OUT + name + '.wav')
        print(f"{name:10s} {len(y)/SR:.2f}s loud100 {loud(y):6.1f}")
    # the explosion: the game's own file, driven the same way so it sits above every gun
    raw = subprocess.run(['ffmpeg','-v','error','-i',OUT + 'explode.mp3','-ac','1','-ar',str(SR),'-f','f32le','-'], capture_output=True).stdout
    seg = np.frombuffer(raw, dtype=np.float32)
    for d in (6, 9):
        y = process(seg, SR, d, len(seg)/SR, bass=4.0, fade=False); print(f"explode2 drive {d}: loud100 {loud(y):6.1f} loud400 {loud(y, 400):6.1f}")
    y = process(seg, SR, 9, len(seg)/SR, bass=4.0, fade=False); save(y, OUT + 'explode2.wav')
