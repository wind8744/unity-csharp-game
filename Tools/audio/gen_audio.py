#!/usr/bin/env python3
"""
Procedural sound set for "쪼꼬미 공성전" (cute chibi tower defense).

Everything is synthesised with numpy only and written as 16-bit PCM mono
44100 Hz WAV files via the standard `wave` module.  The script is fully
deterministic (fixed RNG seed) so re-running it reproduces identical files.

Usage:  python3 Tools/audio/gen_audio.py
Output: Assets/Resources/Audio/*.wav
"""
import math
import os
import wave

import numpy as np

SR = 44100
HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "Resources", "Audio"))
RNG = np.random.default_rng(20260926)

SFX_DB = -3.0
MUSIC_DB = -8.0
FADE_MS = 3.0


# ----------------------------------------------------------------------------
# basic helpers
# ----------------------------------------------------------------------------
def nsamp(sec):
    return int(round(sec * SR))


def tline(n):
    return np.arange(n) / SR


def _phase(freq, n):
    f = np.broadcast_to(np.asarray(freq, dtype=float), (n,))
    return 2.0 * np.pi * np.cumsum(f) / SR


def sine(freq, n):
    return np.sin(_phase(freq, n))


def square(freq, n, duty=0.5):
    ph = (_phase(freq, n) / (2.0 * np.pi)) % 1.0
    s = np.where(ph < duty, 1.0, -1.0)
    return s - (2.0 * duty - 1.0)  # remove DC so envelopes do not thump


def triangle(freq, n):
    ph = (_phase(freq, n) / (2.0 * np.pi)) % 1.0
    return 4.0 * np.abs(ph - 0.5) - 1.0


def saw(freq, n):
    ph = (_phase(freq, n) / (2.0 * np.pi)) % 1.0
    return 2.0 * ph - 1.0


def noise(n):
    return RNG.uniform(-1.0, 1.0, n)


def expsweep(f0, f1, n):
    """Exponential frequency sweep from f0 to f1 over n samples."""
    return f0 * (f1 / f0) ** np.linspace(0.0, 1.0, n)


def vibrato(freq, n, rate=5.5, depth=0.006, delay=0.08):
    t = tline(n)
    ramp = np.clip((t - delay) / 0.12, 0.0, 1.0)
    return freq * (1.0 + depth * ramp * np.sin(2.0 * np.pi * rate * t))


def adsr(n, a=0.005, d=0.05, s=0.7, r=0.05):
    """Linear ADSR whose release always ends exactly at sample n-1."""
    env = np.full(n, float(s))
    ia = min(nsamp(a), n)
    if ia > 0:
        env[:ia] = np.linspace(0.0, 1.0, ia, endpoint=False)
    idd = min(nsamp(d), n - ia)
    if idd > 0:
        env[ia:ia + idd] = np.linspace(1.0, s, idd, endpoint=False)
    ir = min(nsamp(r), n)
    if ir > 0:
        env[n - ir:] *= np.linspace(1.0, 0.0, ir)
    return env


def decay(n, tau, attack=0.002):
    """Exponential decay with a short attack and a guaranteed zero ending."""
    t = tline(n)
    env = np.exp(-t / tau)
    ia = min(nsamp(attack), n)
    if ia > 0:
        env[:ia] *= np.linspace(0.0, 1.0, ia, endpoint=False)
    ie = min(nsamp(0.004), n)
    if ie > 0:
        env[n - ie:] *= np.linspace(1.0, 0.0, ie)
    return env


def lowpass(x, fc, order=2):
    """Zero-phase Butterworth-like low-pass via FFT (circular: loop friendly)."""
    n = len(x)
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(n, 1.0 / SR)
    H = 1.0 / np.sqrt(1.0 + (f / fc) ** (2 * order))
    return np.fft.irfft(X * H, n)


def highpass(x, fc, order=2):
    n = len(x)
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(n, 1.0 / SR)
    with np.errstate(divide="ignore"):
        H = 1.0 / np.sqrt(1.0 + (fc / np.maximum(f, 1e-9)) ** (2 * order))
    H[0] = 0.0
    return np.fft.irfft(X * H, n)


def bandpass(x, lo, hi, order=2):
    return highpass(lowpass(x, hi, order), lo, order)


def lowpass_sweep(x, fc):
    """Time-varying one-pole low-pass (per-sample loop; use for short SFX)."""
    fc = np.broadcast_to(np.asarray(fc, dtype=float), (len(x),))
    a = 1.0 - np.exp(-2.0 * np.pi * fc / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += a[i] * (x[i] - acc)
        y[i] = acc
    return y


def echo(x, delay, gain=0.35, reps=3):
    """Simple feedback echo; output is extended to hold the tail."""
    dn = nsamp(delay)
    out = np.zeros(len(x) + dn * reps)
    for k in range(reps + 1):
        out[k * dn:k * dn + len(x)] += x * (gain ** k)
    return out


def echo_wrap(x, delay, gain=0.3, reps=2):
    """Echo that wraps around the buffer end -> seamless for loops."""
    dn = nsamp(delay)
    out = x.copy()
    for k in range(1, reps + 1):
        out += np.roll(x, k * dn) * (gain ** k)
    return out


def mix(*parts):
    """Sum signals of different lengths with a soft limiter for safety."""
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[:len(p)] += p
    peak = np.max(np.abs(out)) if n else 0.0
    if peak > 1.0:
        out = np.tanh(out / peak * 1.2) / math.tanh(1.2) * peak  # gentle soft-clip
    return out


def place(buf, start_sec, sig, gain=1.0):
    s = nsamp(start_sec)
    n = min(len(sig), len(buf) - s)
    if n > 0:
        buf[s:s + n] += sig[:n] * gain
    return buf


def normalize(x, db):
    peak = np.max(np.abs(x))
    if peak <= 0:
        return x
    return x * (10.0 ** (db / 20.0) / peak)


def fade(x, ms=FADE_MS):
    n = min(nsamp(ms / 1000.0), len(x) // 2)
    x = x.copy()
    if n > 0:
        x[:n] *= np.linspace(0.0, 1.0, n)
        x[-n:] *= np.linspace(1.0, 0.0, n)
    return x


def write_wav(name, x):
    x = np.clip(x, -1.0, 1.0)
    pcm = (x * 32767.0).astype(np.int16)
    path = os.path.join(OUT_DIR, name)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    return path


# ----------------------------------------------------------------------------
# note helpers
# ----------------------------------------------------------------------------
_NOTE_IDX = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def midi(name):
    """'C#4', 'Eb3', 'A4' -> midi number."""
    letter = name[0].upper()
    rest = name[1:]
    acc = 0
    while rest and rest[0] in "#b":
        acc += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    octave = int(rest)
    return 12 * (octave + 1) + _NOTE_IDX[letter] + acc


def mfreq(m):
    return 440.0 * 2.0 ** ((m - 69) / 12.0)


def nf(name):
    return mfreq(midi(name))


C_MAJOR = [0, 2, 4, 5, 7, 9, 11]


def diatonic_shift(name, steps, scale=C_MAJOR):
    """Move a (diatonic) note by scale steps; non-scale notes shift chromatically."""
    m = midi(name)
    pc = m % 12
    if pc not in scale:
        return m - 3 if steps < 0 else m + 3
    idx = scale.index(pc)
    new_idx = idx + steps
    octave_shift = new_idx // 7
    return m - pc + scale[new_idx % 7] + 12 * octave_shift


# ----------------------------------------------------------------------------
# tiny reusable sound bits
# ----------------------------------------------------------------------------
def tone(freq, dur, wave="sine", env=None, duty=0.5, vib=None):
    n = nsamp(dur)
    f = vibrato(freq, n, *vib) if vib else freq
    if wave == "sine":
        s = sine(f, n)
    elif wave == "square":
        s = square(f, n, duty)
    elif wave == "tri":
        s = triangle(f, n)
    elif wave == "saw":
        s = saw(f, n)
    else:
        raise ValueError(wave)
    if env is None:
        env = adsr(n)
    return s * env


def coin_ping(freq, dur=0.12, step=0.04):
    """Classic two-step coin ping: freq for `step` seconds then a fourth up."""
    n = nsamp(dur)
    f = np.full(n, freq)
    f[nsamp(step):] = freq * 2 ** (5 / 12)
    s = sine(f, n) * 0.8 + triangle(f, n) * 0.25
    return s * decay(n, 0.05, attack=0.001)


def hammer_tap(dur=0.12):
    n = nsamp(dur)
    tink = sine(expsweep(1400, 700, n), n) * decay(n, 0.02, 0.001)
    body = sine(expsweep(220, 120, n), n) * decay(n, 0.03, 0.001)
    click = highpass(noise(n), 1500) * decay(n, 0.008, 0.0005)
    return tink * 0.6 + body * 0.7 + click * 0.5


def bell(freq, dur):
    n = nsamp(dur)
    partials = [(1.0, 1.0, 0.45), (2.0, 0.5, 0.3), (2.4, 0.35, 0.25),
                (3.0, 0.25, 0.18), (4.5, 0.12, 0.1)]
    out = np.zeros(n)
    for ratio, amp, tau in partials:
        out += sine(freq * ratio, n) * decay(n, tau, 0.001) * amp
    return out


def brass(freqs, dur, vib=(5.0, 0.004, 0.1), env=None, cutoff=2200):
    n = nsamp(dur)
    out = np.zeros(n)
    for f in freqs:
        out += saw(vibrato(f, n, *vib), n) * 0.6 + square(vibrato(f * 1.003, n, *vib), n, 0.4) * 0.4
    env = adsr(n, 0.03, 0.08, 0.8, 0.06) if env is None else env
    return lowpass(out * env, cutoff)


# ----------------------------------------------------------------------------
# drums (used by SFX and music)
# ----------------------------------------------------------------------------
def drum_kick(dur=0.2):
    n = nsamp(dur)
    body = sine(expsweep(150, 42, n), n) * decay(n, 0.07, 0.001)
    click = highpass(noise(n), 2500) * decay(n, 0.004, 0.0003) * 0.35
    return np.tanh((body + click) * 1.4)


def drum_snare(dur=0.16):
    n = nsamp(dur)
    nz = bandpass(noise(n), 1200, 8000) * decay(n, 0.045, 0.0005)
    tone_ = sine(expsweep(210, 160, n), n) * decay(n, 0.04, 0.001)
    return nz * 0.8 + tone_ * 0.5


def drum_hat(dur=0.05):
    n = nsamp(dur)
    return highpass(noise(n), 7000) * decay(n, 0.012, 0.0003)


def drum_tom(freq=140, dur=0.22):
    n = nsamp(dur)
    return sine(expsweep(freq * 1.5, freq, n), n) * decay(n, 0.09, 0.001)


# ----------------------------------------------------------------------------
# SFX
# ----------------------------------------------------------------------------
def sfx_shoot_arrow():
    n = nsamp(0.12)
    nz = lowpass_sweep(noise(n), expsweep(7000, 700, n))
    nz = highpass(nz, 600)
    return nz * decay(n, 0.04, 0.004)


def sfx_shoot_fire():
    n = nsamp(0.25)
    crackle = noise(n)
    crackle = crackle * (np.abs(noise(n)) ** 3 * 3.0)  # spiky gating
    crackle = lowpass(crackle, 3500) * decay(n, 0.09, 0.005)
    whoomp = sine(expsweep(130, 50, n), n) * adsr(n, 0.02, 0.1, 0.4, 0.1)
    puff = lowpass(noise(n), 500) * decay(n, 0.06, 0.01)
    return crackle * 0.6 + whoomp * 0.9 + puff * 0.5


def sfx_shoot_cannon():
    n = nsamp(0.45)
    click = highpass(noise(n), 3000) * decay(n, 0.005, 0.0003) * 0.8
    boom = sine(expsweep(95, 34, n), n) * decay(n, 0.16, 0.001)
    body = lowpass(noise(n), 350) * decay(n, 0.1, 0.002) * 0.7
    return np.tanh((click + boom + body) * 1.5)


def sfx_shoot_laser():
    n = nsamp(0.15)
    f = expsweep(2600, 320, n)
    s = square(f, n, 0.5) * 0.6 + sine(f * 1.5, n) * 0.3
    s = lowpass(s, 6000)
    return s * adsr(n, 0.002, 0.06, 0.5, 0.05)


def sfx_shoot_magic():
    n = nsamp(0.2)
    out = np.zeros(n)
    notes = ["C6", "E6", "G6", "C7"]
    for i, nm in enumerate(notes):
        seg = nsamp(0.09)
        s = (sine(nf(nm), seg) * 0.7 + triangle(nf(nm), seg) * 0.3) * decay(seg, 0.035, 0.001)
        place(out, i * 0.045, s)
    sparkle = highpass(noise(n), 6000) * decay(n, 0.05, 0.002) * 0.25
    return out + sparkle


def sfx_shoot_bolt():
    n = nsamp(0.15)
    gate = (np.abs(noise(n)) > 0.55).astype(float)
    crack = highpass(noise(n), 2500) * gate * decay(n, 0.05, 0.001)
    zap = square(expsweep(1900, 600, n), n, 0.3) * decay(n, 0.03, 0.001) * 0.5
    return lowpass(crack * 0.9 + zap, 9000)


def sfx_hit():
    n = nsamp(0.08)
    pop = sine(expsweep(320, 110, n), n) * decay(n, 0.025, 0.001)
    tick = lowpass(noise(n), 2500) * decay(n, 0.008, 0.0005) * 0.5
    return pop + tick


def sfx_hit_heavy():
    n = nsamp(0.2)
    body = sine(expsweep(160, 48, n), n) * decay(n, 0.07, 0.001)
    thud = lowpass(noise(n), 700) * decay(n, 0.04, 0.001) * 0.8
    return np.tanh((body + thud) * 1.3)


def sfx_death_small():
    n = nsamp(0.25)
    out = np.zeros(n)
    p1 = nsamp(0.1)
    place(out, 0.0, triangle(expsweep(950, 420, p1), p1) * decay(p1, 0.035, 0.001))
    place(out, 0.12, triangle(expsweep(640, 260, p1), p1) * decay(p1, 0.04, 0.001))
    return out


def sfx_death_big():
    n = nsamp(0.5)
    boom = sine(expsweep(230, 38, n), n) * decay(n, 0.18, 0.001)
    body = lowpass(noise(n), 900) * decay(n, 0.12, 0.002) * 0.7
    rumble = sine(expsweep(60, 30, n), n) * decay(n, 0.3, 0.01) * 0.5
    return np.tanh((boom + body + rumble) * 1.2)


def sfx_leak():
    n = nsamp(0.3)
    out = np.zeros(n)
    a = nsamp(0.13)
    b = nsamp(0.16)
    place(out, 0.0, square(nf("E5"), a, 0.5) * adsr(a, 0.005, 0.03, 0.7, 0.03))
    place(out, 0.14, square(nf("B4"), b, 0.5) * adsr(b, 0.005, 0.03, 0.7, 0.05))
    return lowpass(out, 2600)


def sfx_base_hit():
    n = nsamp(0.4)
    impact = lowpass(noise(n), 450) * decay(n, 0.05, 0.001)
    thump = sine(expsweep(120, 45, n), n) * decay(n, 0.1, 0.001)
    rumble = sine(expsweep(55, 38, n), n) * decay(n, 0.3, 0.02) * 0.7
    return np.tanh((impact * 0.9 + thump + rumble) * 1.3)


def sfx_build():
    n = nsamp(0.3)
    out = np.zeros(n)
    place(out, 0.0, hammer_tap())
    place(out, 0.15, hammer_tap() * 0.9)
    return out


def sfx_upgrade():
    n = nsamp(0.35)
    out = np.zeros(n)
    for i, nm in enumerate(["C5", "E5", "G5"]):
        seg = nsamp(0.16)
        s = square(nf(nm), seg, 0.25) * 0.5 + sine(nf(nm), seg) * 0.5
        place(out, i * 0.09, s * adsr(seg, 0.004, 0.05, 0.6, 0.08))
    return lowpass(out, 5000)


def sfx_merge():
    n = nsamp(0.6)
    out = np.zeros(n)
    sw = nsamp(0.36)
    sweep = sine(vibrato(1.0, sw, 30, 0.02, 0.0) * expsweep(420, 3400, sw), sw) * adsr(sw, 0.02, 0.05, 0.8, 0.08)
    sparkle = lowpass_sweep(noise(sw), expsweep(800, 9000, sw)) * adsr(sw, 0.05, 0.05, 0.7, 0.1) * 0.35
    place(out, 0.0, sweep * 0.7 + highpass(sparkle, 1500))
    chord_n = nsamp(0.26)
    chord = np.zeros(chord_n)
    for nm in ["C5", "E5", "G5", "C6"]:
        chord += square(nf(nm), chord_n, 0.25) * 0.35 + sine(nf(nm), chord_n) * 0.45
    place(out, 0.34, lowpass(chord * adsr(chord_n, 0.004, 0.08, 0.6, 0.1), 6000) * 0.6)
    return out


def sfx_fuse():
    n = nsamp(0.7)
    out = np.zeros(n)
    sw = nsamp(0.42)
    whoosh = lowpass_sweep(noise(sw), np.concatenate([expsweep(300, 6000, sw // 2), expsweep(6000, 1200, sw - sw // 2)]))
    whoosh = highpass(whoosh, 250) * adsr(sw, 0.08, 0.05, 0.9, 0.1)
    swirl = sine(vibrato(1.0, sw, 18, 0.03, 0.0) * expsweep(280, 2200, sw), sw) * adsr(sw, 0.03, 0.05, 0.7, 0.1)
    place(out, 0.0, whoosh * 0.6 + swirl * 0.5)
    chord_n = nsamp(0.3)
    chord = np.zeros(chord_n)
    for nm in ["C4", "E4", "G4", "C5"]:
        f = nf(nm)
        chord += square(f, chord_n, 0.3) * 0.3 + sine(f * 1.004, chord_n) * 0.4 + triangle(f, chord_n) * 0.3
    place(out, 0.4, lowpass(chord * adsr(chord_n, 0.006, 0.1, 0.6, 0.12), 4500) * 0.6)
    return out


def sfx_sell():
    n = nsamp(0.2)
    clink = np.zeros(n)
    for f, amp, tau in [(2500, 1.0, 0.05), (3400, 0.6, 0.035), (5100, 0.4, 0.025)]:
        clink += sine(f, n) * decay(n, tau, 0.0005) * amp
    ping = coin_ping(nf("B5"), 0.18, 0.045)
    return place(clink * 0.5, 0.02, ping * 0.8)


def sfx_coin():
    return coin_ping(nf("B6"), 0.12, 0.035)


def sfx_income():
    n = nsamp(0.35)
    out = np.zeros(n)
    for i, nm in enumerate(["E6", "G6", "B6"]):
        place(out, i * 0.11, coin_ping(nf(nm), 0.12, 0.035))
    return out


def sfx_draw():
    n = nsamp(0.15)
    swish_n = nsamp(0.11)
    sw = lowpass_sweep(noise(swish_n), expsweep(1200, 7000, swish_n))
    sw = highpass(sw, 900) * adsr(swish_n, 0.03, 0.02, 0.8, 0.05)
    out = np.zeros(n)
    place(out, 0.0, sw * 0.7)
    tick_n = nsamp(0.04)
    tick = sine(expsweep(2200, 1500, tick_n), tick_n) * decay(tick_n, 0.008, 0.0005)
    tick += highpass(noise(tick_n), 3000) * decay(tick_n, 0.004, 0.0003) * 0.6
    place(out, 0.1, tick)
    return out


def sfx_send():
    n = nsamp(0.4)
    out = np.zeros(n)
    place(out, 0.0, brass([nf("G4")], 0.19, env=adsr(nsamp(0.19), 0.02, 0.05, 0.8, 0.04)))
    place(out, 0.18, brass([nf("C5")], 0.22, env=adsr(nsamp(0.22), 0.02, 0.06, 0.8, 0.08)))
    return out


def sfx_wave():
    n = nsamp(0.5)
    out = np.zeros(n)
    place(out, 0.0, drum_kick() * 1.0)
    place(out, 0.0, drum_snare() * 0.7)
    stab = brass([nf("C4"), nf("E4"), nf("G4"), nf("C5")], 0.4,
                 env=adsr(nsamp(0.4), 0.015, 0.1, 0.55, 0.15), cutoff=2600)
    place(out, 0.04, stab * 0.55)
    return out


def sfx_boss():
    n = nsamp(0.9)
    out = np.zeros(n)
    bn = nsamp(0.85)
    low = brass([nf("A2"), nf("E3"), nf("A3"), nf("C4")], 0.85,
                env=adsr(bn, 0.12, 0.15, 0.8, 0.25), cutoff=1200)
    # slowly opening filter feel: cross-fade dull -> brighter
    ramp = np.linspace(0.0, 1.0, bn)
    low = low * (1 - ramp) + lowpass(low, 2600) * ramp
    place(out, 0.0, low * 0.7)
    for t in (0.0, 0.42):
        tn = nsamp(0.4)
        timp = sine(expsweep(120, 68, tn), tn) * decay(tn, 0.14, 0.001)
        timp += lowpass(noise(tn), 500) * decay(tn, 0.03, 0.001) * 0.4
        place(out, t, timp * 0.9)
    return np.tanh(out * 1.3)


def sfx_augment_open():
    n = nsamp(0.8)
    out = np.zeros(n)
    for i, nm in enumerate(["E6", "C6", "G5"]):
        seg = nsamp(0.2)
        s = (sine(nf(nm), seg) * 0.7 + triangle(nf(nm), seg) * 0.3) * decay(seg, 0.06, 0.002)
        place(out, i * 0.1, s * 0.7)
    hold_n = n - nsamp(0.3)
    hold = np.zeros(hold_n)
    for nm, amp in [("E5", 0.5), ("G5", 0.4), ("C6", 0.35)]:
        hold += sine(vibrato(nf(nm), hold_n, 4.5, 0.004, 0.1), hold_n) * amp
    shimmer = highpass(noise(hold_n), 7000) * 0.06
    hold = (hold + shimmer) * adsr(hold_n, 0.06, 0.1, 0.8, 0.2)
    place(out, 0.3, hold)
    return out


def sfx_augment_pick():
    n = nsamp(0.35)
    chord = np.zeros(n)
    for nm in ["C5", "E5", "G5", "C6"]:
        chord += square(nf(nm), n, 0.25) * 0.3 + sine(nf(nm), n) * 0.5
    chord = lowpass(chord * adsr(n, 0.004, 0.1, 0.55, 0.12), 5500)
    tick = highpass(noise(n), 4000) * decay(n, 0.006, 0.0003) * 0.3
    return chord + tick


def sfx_mission():
    n = nsamp(0.9)
    out = np.zeros(n)
    notes = [("G4", 0.0, 0.14), ("C5", 0.15, 0.14), ("E5", 0.30, 0.14), ("G5", 0.45, 0.43)]
    for nm, t, d in notes:
        env = adsr(nsamp(d), 0.01, 0.05, 0.8, 0.05 if d < 0.3 else 0.15)
        place(out, t, brass([nf(nm)], d, env=env, cutoff=2600) * 0.8)
    chord = brass([nf("C4"), nf("E4"), nf("G4")], 0.43,
                  env=adsr(nsamp(0.43), 0.02, 0.1, 0.7, 0.15), cutoff=1800)
    place(out, 0.45, chord * 0.4)
    return out


def sfx_event_warn():
    n = nsamp(0.8)
    out = np.zeros(n)
    place(out, 0.0, lowpass(bell(330, 0.42), 5000))
    place(out, 0.4, lowpass(bell(330, 0.4), 5000) * 0.9)
    return out


def sfx_event_start():
    n = nsamp(0.7)
    out = np.zeros(n)
    rn = nsamp(0.46)
    riser = square(expsweep(180, 1500, rn), rn, 0.4) * adsr(rn, 0.05, 0.05, 0.9, 0.03)
    nz = lowpass_sweep(noise(rn), expsweep(400, 8000, rn)) * np.linspace(0.1, 1.0, rn) ** 2
    place(out, 0.0, lowpass(riser, 4000) * 0.5 + highpass(nz, 300) * 0.5)
    place(out, 0.45, drum_kick(0.25) * 1.0)
    place(out, 0.45, drum_snare(0.2) * 0.6)
    hit_n = nsamp(0.25)
    chord = np.zeros(hit_n)
    for nm in ["C4", "G4", "C5", "E5"]:
        chord += square(nf(nm), hit_n, 0.3) * 0.3 + sine(nf(nm), hit_n) * 0.4
    place(out, 0.45, lowpass(chord * decay(hit_n, 0.09, 0.003), 3500) * 0.6)
    return np.tanh(out * 1.2)


def sfx_click():
    n = nsamp(0.04)
    s = sine(1300, n) * decay(n, 0.008, 0.0005)
    s += highpass(noise(n), 3000) * decay(n, 0.003, 0.0003) * 0.4
    return s


def sfx_hover():
    n = nsamp(0.03)
    return sine(2100, n) * decay(n, 0.005, 0.0005)


def sfx_error():
    n = nsamp(0.15)
    s = square(220, n, 0.5) + square(233, n, 0.5)
    return lowpass(s * adsr(n, 0.005, 0.02, 0.8, 0.04), 1800)


def sfx_win():
    total = 2.5
    n = nsamp(total)
    out = np.zeros(n)
    mel = [("C5", 0.00, 0.14), ("E5", 0.15, 0.14), ("G5", 0.30, 0.14), ("C6", 0.45, 0.38),
           ("G5", 0.90, 0.14), ("A5", 1.05, 0.14), ("B5", 1.20, 0.14), ("C6", 1.35, 1.05)]
    for nm, t, d in mel:
        dn = nsamp(d)
        s = square(vibrato(nf(nm), dn, 5.5, 0.006, 0.1), dn, 0.25) * 0.5 + sine(nf(nm), dn) * 0.5
        rel = 0.05 if d < 0.3 else 0.25
        place(out, t, lowpass(s * adsr(dn, 0.005, 0.06, 0.75, rel), 5000) * 0.6)
    # chord hits on the two long notes
    for t, d, names in [(0.45, 0.4, ["C4", "E4", "G4"]), (1.35, 1.1, ["C4", "E4", "G4", "C5"])]:
        dn = nsamp(d)
        ch = np.zeros(dn)
        for nm in names:
            ch += triangle(nf(nm), dn) * 0.5 + sine(nf(nm), dn) * 0.5
        place(out, t, lowpass(ch * adsr(dn, 0.02, 0.1, 0.7, 0.3), 3000) * 0.3)
    # bass
    for t, d, nm in [(0.0, 0.42, "C3"), (0.45, 0.42, "C3"), (0.9, 0.42, "G2"), (1.35, 1.1, "C3")]:
        dn = nsamp(d)
        place(out, t, triangle(nf(nm), dn) * adsr(dn, 0.005, 0.05, 0.8, 0.1) * 0.5)
    # little drum accents
    place(out, 0.0, drum_kick() * 0.5)
    place(out, 0.45, drum_kick() * 0.5)
    place(out, 0.45, drum_snare() * 0.3)
    place(out, 1.35, drum_kick() * 0.6)
    place(out, 1.35, drum_snare() * 0.35)
    for t in (0.15, 0.3, 0.6, 0.75, 1.05, 1.2):
        place(out, t, drum_hat() * 0.15)
    return out


def sfx_lose():
    total = 2.5
    n = nsamp(total)
    out = np.zeros(n)
    mel = [("E5", 0.0, 0.42), ("D5", 0.45, 0.42), ("C5", 0.9, 0.42), ("B4", 1.35, 0.5), ("A4", 1.85, 0.62)]
    for nm, t, d in mel:
        dn = nsamp(d)
        s = triangle(vibrato(nf(nm), dn, 4.5, 0.008, 0.15), dn) * 0.6 + sine(nf(nm), dn) * 0.4
        place(out, t, lowpass(s * adsr(dn, 0.02, 0.1, 0.7, 0.15), 3500) * 0.6)
    # soft minor pad, slow tremolo
    pads = [(0.0, 0.9, ["A3", "C4", "E4"]), (0.9, 0.95, ["F3", "A3", "C4"]), (1.85, 0.62, ["A3", "C4", "E4"])]
    for t, d, names in pads:
        dn = nsamp(d)
        ch = np.zeros(dn)
        for nm in names:
            ch += sine(nf(nm), dn) * 0.6 + triangle(nf(nm), dn) * 0.4
        trem = 1.0 - 0.25 * (0.5 + 0.5 * np.sin(2 * np.pi * 4.0 * tline(dn)))
        place(out, t, lowpass(ch * trem * adsr(dn, 0.1, 0.2, 0.8, 0.25), 2000) * 0.3)
    for t, d, nm in [(0.0, 0.88, "A2"), (0.9, 0.93, "F2"), (1.85, 0.62, "A2")]:
        dn = nsamp(d)
        place(out, t, triangle(nf(nm), dn) * adsr(dn, 0.02, 0.1, 0.8, 0.2) * 0.45)
    return out


SFX = {
    "shoot_arrow.wav": sfx_shoot_arrow,
    "shoot_fire.wav": sfx_shoot_fire,
    "shoot_cannon.wav": sfx_shoot_cannon,
    "shoot_laser.wav": sfx_shoot_laser,
    "shoot_magic.wav": sfx_shoot_magic,
    "shoot_bolt.wav": sfx_shoot_bolt,
    "hit.wav": sfx_hit,
    "hit_heavy.wav": sfx_hit_heavy,
    "death_small.wav": sfx_death_small,
    "death_big.wav": sfx_death_big,
    "leak.wav": sfx_leak,
    "base_hit.wav": sfx_base_hit,
    "build.wav": sfx_build,
    "upgrade.wav": sfx_upgrade,
    "merge.wav": sfx_merge,
    "fuse.wav": sfx_fuse,
    "sell.wav": sfx_sell,
    "coin.wav": sfx_coin,
    "income.wav": sfx_income,
    "draw.wav": sfx_draw,
    "send.wav": sfx_send,
    "wave.wav": sfx_wave,
    "boss.wav": sfx_boss,
    "augment_open.wav": sfx_augment_open,
    "augment_pick.wav": sfx_augment_pick,
    "mission.wav": sfx_mission,
    "event_warn.wav": sfx_event_warn,
    "event_start.wav": sfx_event_start,
    "click.wav": sfx_click,
    "hover.wav": sfx_hover,
    "error.wav": sfx_error,
    "win.wav": sfx_win,
    "lose.wav": sfx_lose,
}


# ----------------------------------------------------------------------------
# music: sequencer + instruments
# ----------------------------------------------------------------------------
CHORDS = {
    "C": ["C4", "E4", "G4"], "G": ["G3", "B3", "D4"], "Am": ["A3", "C4", "E4"],
    "F": ["F3", "A3", "C4"], "Dm": ["D4", "F4", "A4"], "Em": ["E4", "G4", "B4"],
    "E": ["E3", "G#3", "B3"], "Am7": ["A3", "C4", "E4", "G4"],
}
ROOTS = {"C": "C2", "G": "G2", "Am": "A2", "F": "F2", "Dm": "D2", "Em": "E2", "E": "E2", "Am7": "A2"}


def parse_bars(bars, beats_per_bar=4):
    """Bar strings of 'NOTE:dur' tokens (NOTE may be 'A4+C5' chord, '-' rest,
    optional ':vel'). Returns [(start_beat, dur_beats, [names], vel)]."""
    events = []
    for bi, bar in enumerate(bars):
        pos = 0.0
        for tok in bar.split():
            parts = tok.split(":")
            name, dur = parts[0], float(parts[1])
            vel = float(parts[2]) if len(parts) > 2 else 1.0
            if name != "-":
                events.append((bi * beats_per_bar + pos, dur, name.split("+"), vel))
            pos += dur
        if abs(pos - beats_per_bar) > 1e-6:
            raise ValueError("bar %d has %.2f beats: %r" % (bi + 1, pos, bar))
    return events


def render(events, inst, total_n, beat_sec, gain=1.0):
    buf = np.zeros(total_n)
    for start_b, dur_b, names, vel in events:
        s = nsamp(start_b * beat_sec)
        n = min(nsamp(dur_b * beat_sec), total_n - s)
        if n <= 0:
            continue
        for nm in names:
            buf[s:s + n] += inst(nf(nm), n) * vel
    return buf * gain


def render_drums(pattern_bars, sample_fn, total_n, beat_sec, gain=1.0, soft=0.5):
    """pattern: 16 chars per bar (sixteenths); 'x' hit, 'o' soft hit, '.' rest."""
    buf = np.zeros(total_n)
    step = beat_sec / 4.0
    for bi, pat in enumerate(pattern_bars):
        if len(pat) != 16:
            raise ValueError("drum pattern must be 16 chars: %r" % pat)
        for i, ch in enumerate(pat):
            if ch == ".":
                continue
            v = 1.0 if ch == "x" else soft
            place(buf, (bi * 16 + i) * step, sample_fn(), v * gain)
    return buf


def chord_events(chord_bars):
    """'C:4' or 'F:2 G:2' per bar -> chord-note events + root events."""
    notes, roots = [], []
    for bi, bar in enumerate(chord_bars):
        pos = 0.0
        for tok in bar.split():
            name, dur = tok.split(":")
            dur = float(dur)
            notes.append((bi * 4 + pos, dur, CHORDS[name], 1.0))
            roots.append((bi * 4 + pos, dur, name))
            pos += dur
    return notes, roots


def bass_events(roots, pattern, beats_per_step=1.0, octave_up_shift=12):
    """pattern: list per chord-beat-cycle of (semitone offset or None, vel)."""
    events = []
    for start, dur, name in roots:
        root = midi(ROOTS[name])
        steps = int(round(dur / beats_per_step))
        for i in range(steps):
            off, vel = pattern[i % len(pattern)]
            if off is None:
                continue
            m = root + off
            events.append((start + i * beats_per_step, beats_per_step, [midi_name(m)], vel))
    return events


_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]


def midi_name(m):
    return "%s%d" % (_NAMES[m % 12], m // 12 - 1)


def harmony_events(events, steps=-2):
    out = []
    for start, dur, names, vel in events:
        out.append((start, dur, [midi_name(diatonic_shift(nm, steps)) for nm in names], vel))
    return out


# instruments: f, n -> signal of length n  (release always ends at n)
def inst_lead(f, n, duty=0.25, gate=0.86):
    out = np.zeros(n)
    m = max(int(n * gate), min(n, nsamp(0.03)))
    fr = vibrato(f, m, 5.5, 0.006, 0.1)
    out[:m] = square(fr, m, duty) * adsr(m, 0.004, 0.08, 0.72, 0.03)
    return out


def inst_lead_soft(f, n):
    out = np.zeros(n)
    m = max(int(n * 0.9), min(n, nsamp(0.03)))
    fr = vibrato(f, m, 5.0, 0.008, 0.15)
    out[:m] = (triangle(fr, m) * 0.6 + square(fr, m, 0.5) * 0.4) * adsr(m, 0.02, 0.1, 0.7, 0.08)
    return out


def inst_bass_tri(f, n, gate=0.8):
    out = np.zeros(n)
    m = max(int(n * gate), min(n, nsamp(0.02)))
    out[:m] = triangle(f, m) * adsr(m, 0.004, 0.06, 0.8, 0.02)
    return out


def inst_bass_pulse(f, n, gate=0.7):
    out = np.zeros(n)
    m = max(int(n * gate), min(n, nsamp(0.02)))
    out[:m] = (triangle(f, m) * 0.7 + square(f, m, 0.5) * 0.3) * adsr(m, 0.003, 0.05, 0.75, 0.02)
    return out


def inst_pad(f, n):
    s = sine(f, n) * 0.6 + triangle(f, n) * 0.4
    return s * adsr(n, 0.15, 0.1, 0.85, 0.2)


def inst_pad_tremolo(f, n):
    s = sine(f, n) * 0.5 + square(f, n, 0.5) * 0.2 + triangle(f * 1.002, n) * 0.3
    trem = 1.0 - 0.45 * (0.5 + 0.5 * np.sin(2 * np.pi * 6.0 * tline(n)))
    return s * trem * adsr(n, 0.2, 0.1, 0.9, 0.25)


def finalize_music(x, total_n):
    x = x[:total_n]
    return normalize(x, MUSIC_DB)


# ----------------------------------------------------------------------------
# bgm_title : 100 BPM, C major, cosy
# ----------------------------------------------------------------------------
def bgm_title():
    bpm = 100.0
    beat = 60.0 / bpm
    bars = 16
    total_n = nsamp(bars * 4 * beat)

    chords = ["C:4", "G:4", "Am:4", "F:4", "C:4", "G:4", "F:2 G:2", "C:4",
              "C:4", "G:4", "Am:4", "F:4", "C:4", "G:4", "F:2 G:2", "C:4"]
    melody = [
        # A phrase
        "E5:1 G5:.5 E5:.5 C5:1 D5:1",
        "D5:1 B4:.5 D5:.5 G4:2",
        "A4:.5 C5:.5 E5:1 E5:.5 D5:.5 C5:1",
        "D5:1.5 C5:.5 A4:2",
        "E5:1 G5:.5 E5:.5 C5:1 D5:1",
        "D5:1 B4:.5 D5:.5 G5:2",
        "A5:.5 G5:.5 E5:1 D5:1 B4:.5 D5:.5",
        "C5:3 -:1",
        # A' phrase (answer)
        "G5:1 E5:.5 G5:.5 C6:1 B5:1",
        "A5:1 G5:.5 A5:.5 D5:2",
        "C5:.5 E5:.5 A5:1 G5:.5 E5:.5 C5:1",
        "F5:1.5 E5:.5 D5:2",
        "E5:1 G5:.5 E5:.5 C5:1 D5:1",
        "D5:1 B4:.5 D5:.5 G5:2",
        "A5:.5 G5:.5 F5:1 E5:1 D5:1",
        "C5:2.5 -:1.5",
    ]
    chord_ev, roots = chord_events(chords)
    lead_ev = parse_bars(melody)
    bass_ev = bass_events(roots, [(0, 1.0), (0, 0.8), (7, 0.9), (0, 0.8)], 1.0)
    hat_soft = ["..x...x...x...x."] * bars

    lead = lowpass(render(lead_ev, inst_lead, total_n, beat), 4200)
    lead = echo_wrap(lead, beat * 0.75, 0.22, 2)
    bass = lowpass(render(bass_ev, inst_bass_tri, total_n, beat), 1500)
    pad = lowpass(render(chord_ev, inst_pad, total_n, beat), 1800)
    hats = render_drums(hat_soft, drum_hat, total_n, beat, soft=0.5)
    # gentle kick on 1 and 3 with a soft "rim" on 2/4 for a cosy pulse
    kick = render_drums(["x.......o......."] * bars, lambda: drum_kick(0.15), total_n, beat, soft=0.6)

    music = lead * 0.42 + bass * 0.5 + pad * 0.16 + hats * 0.13 + kick * 0.35
    return finalize_music(music, total_n)


# ----------------------------------------------------------------------------
# bgm_battle : 128 BPM, C major, marching
# ----------------------------------------------------------------------------
def bgm_battle():
    bpm = 128.0
    beat = 60.0 / bpm
    bars = 20  # 4 intro + A(8) + B(8)
    total_n = nsamp(bars * 4 * beat)

    chords = (["C:4", "C:4", "F:4", "G:4"] +
              ["C:4", "C:4", "F:4", "G:4", "C:4", "Am:4", "F:4", "G:4"] +
              ["Am:4", "F:4", "C:4", "G:4", "Am:4", "F:4", "G:4", "G:4"])
    melody = ["-:4"] * 4 + [
        # A
        "C5:.75 C5:.25 E5:1 G5:1 A5:.5 G5:.5",
        "E5:1.5 C5:.5 D5:2",
        "F5:.75 F5:.25 A5:1 F5:1 E5:.5 D5:.5",
        "D5:1.5 B4:.5 D5:2",
        "C5:.75 C5:.25 E5:1 G5:1 C6:1",
        "A5:1.5 E5:.5 G5:1 E5:1",
        "F5:.5 E5:.5 D5:1 C5:1 D5:1",
        "D5:1 B4:1 -:2",
        # B
        "E5:.75 E5:.25 A5:1 E5:1 C5:1",
        "D5:1 C5:1 A4:2",
        "E5:.75 E5:.25 G5:1 E5:1 C5:1",
        "D5:1 E5:1 D5:1 B4:1",
        "E5:.75 E5:.25 A5:1 B5:1 C6:1",
        "A5:1 G5:1 F5:1 E5:1",
        "D5:1 E5:1 F5:1 G5:1",
        "G5:1.5 -:2.5",
    ]
    chord_ev, roots = chord_events(chords)
    lead_ev = parse_bars(melody)
    harm_ev = harmony_events(lead_ev, -2)
    bass_pat = [(0, 1.0), (0, 0.75), (12, 0.85), (0, 0.75), (7, 0.95), (7, 0.75), (12, 0.85), (0, 0.75)]
    bass_ev = bass_events(roots, bass_pat, 0.5)

    kick_pat = ["x...x...x...x..."] * bars
    snare_pat = ["....x.......x..."] * bars
    hat_pat = ["x.o.x.o.x.o.x.o."] * bars
    tom_pat = ["................"] * bars
    # intro: build up (bar 1-2 kick only, bar 3-4 add snare); fills at bar 8 and 16 of the tune
    snare_pat[0] = "................"
    snare_pat[1] = "................"
    for fb in (4 + 7, 4 + 15):
        snare_pat[fb] = "....x...x.x.x.xx"
        tom_pat[fb] = "..........x...x."
        hat_pat[fb] = "x.o.x.o.x.o.....".ljust(16, ".")
    snare_pat[3] = "....x.......xxxx"

    lead = lowpass(render(lead_ev, inst_lead, total_n, beat), 5000)
    lead = echo_wrap(lead, beat * 0.5, 0.18, 1)
    harm = lowpass(render(harm_ev, lambda f, n: inst_lead(f, n, duty=0.5), total_n, beat), 3500)
    bass = lowpass(render(bass_ev, inst_bass_pulse, total_n, beat), 1600)
    pad = lowpass(render(chord_ev, inst_pad, total_n, beat), 1600)
    kick = render_drums(kick_pat, drum_kick, total_n, beat)
    snare = render_drums(snare_pat, drum_snare, total_n, beat, soft=0.55)
    hats = render_drums(hat_pat, drum_hat, total_n, beat, soft=0.45)
    toms = render_drums(tom_pat, lambda: drum_tom(150), total_n, beat)

    music = (lead * 0.42 + harm * 0.18 + bass * 0.5 + pad * 0.12 +
             kick * 0.6 + snare * 0.32 + hats * 0.12 + toms * 0.45)
    return finalize_music(music, total_n)


# ----------------------------------------------------------------------------
# bgm_tension : 128 BPM, A minor, sparse & dark
# ----------------------------------------------------------------------------
def bgm_tension():
    bpm = 128.0
    beat = 60.0 / bpm
    bars = 12
    total_n = nsamp(bars * 4 * beat)

    chords = ["Am:4", "Am:4", "F:4", "E:4",
              "Am:4", "Am:4", "Dm:4", "E:4",
              "Am:4", "F:4", "E:4", "E:4"]
    melody = [
        "A4:3 -:1",
        "C5:1.5 B4:1.5 -:1",
        "A4:2 -:2",
        "G#4:2.5 -:1.5",
        "E5:3 -:1",
        "D5:1 C5:1 B4:2",
        "A4:1.5 F4:1.5 -:1",
        "G#4:3 -:1",
        "A4:1 -:1 E5:1.5 -:.5",
        "F5:2 E5:2",
        "B4:2 G#4:1 -:1",
        "E4:2.5 -:1.5",
    ]
    chord_ev, roots = chord_events(chords)
    lead_ev = parse_bars(melody)
    bass_pat = [(0, 1.0), (0, 0.55), (0, 0.8), (0, 0.55), (0, 1.0), (0, 0.55), (0, 0.8), (12, 0.5)]
    bass_ev = bass_events(roots, bass_pat, 0.5)

    kick_pat = ["x.......o......."] * bars
    tom_pat = ["........x......."] * bars
    hat_pat = ["..o...o...o...o."] * bars
    snare_pat = ["................"] * bars
    for b in (3, 7, 11):
        snare_pat[b] = "............o.x."
        tom_pat[b] = "........x...x.x."

    lead = lowpass(render(lead_ev, inst_lead_soft, total_n, beat), 2800)
    lead = echo_wrap(lead, beat * 1.5, 0.3, 2)
    bass = lowpass(render(bass_ev, inst_bass_pulse, total_n, beat), 900)
    pad = lowpass(render(chord_ev, inst_pad_tremolo, total_n, beat), 1400)
    kick = render_drums(kick_pat, lambda: drum_kick(0.25), total_n, beat, soft=0.5)
    toms = render_drums(tom_pat, lambda: drum_tom(95, 0.3), total_n, beat, soft=0.5)
    hats = render_drums(hat_pat, drum_hat, total_n, beat, soft=0.4)
    snare = render_drums(snare_pat, drum_snare, total_n, beat, soft=0.5)

    # dark noise swell into every 4th bar (rises over the last 2 beats of bars 4/8/12)
    swell = np.zeros(total_n)
    for b in (3, 7, 11):
        sn = nsamp(beat * 2)
        s = lowpass_sweep(noise(sn), expsweep(300, 3000, sn)) * (np.linspace(0, 1, sn) ** 2)
        s[-nsamp(0.01):] *= np.linspace(1, 0, nsamp(0.01))
        place(swell, (b * 4 + 2) * beat, highpass(s, 200))

    music = (lead * 0.38 + bass * 0.5 + pad * 0.2 + kick * 0.55 + toms * 0.4 +
             hats * 0.08 + snare * 0.25 + swell * 0.12)
    return finalize_music(music, total_n)


MUSIC = {
    "bgm_title.wav": bgm_title,
    "bgm_battle.wav": bgm_battle,
    "bgm_tension.wav": bgm_tension,
}


# ----------------------------------------------------------------------------
# main
# ----------------------------------------------------------------------------
def db(x):
    p = np.max(np.abs(x))
    return 20 * math.log10(p) if p > 0 else -999.0


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    written = []
    print("Output dir:", OUT_DIR)
    print("\n== SFX ==")
    for name, fn in SFX.items():
        sig = normalize(fade(np.asarray(fn(), dtype=float)), SFX_DB)
        write_wav(name, sig)
        written.append(name)
        print("  %-20s %6.3f s  peak %6.2f dBFS" % (name, len(sig) / SR, db(sig)))

    print("\n== MUSIC (loops) ==")
    for name, fn in MUSIC.items():
        sig = normalize(fade(fn()), MUSIC_DB)
        write_wav(name, sig)
        written.append(name)
        w = nsamp(0.02)
        rms_head = math.sqrt(np.mean(sig[:w] ** 2))
        rms_tail = math.sqrt(np.mean(sig[-w:] ** 2))
        seam = abs(sig[-1] - sig[0]) * 32767
        print("  %-20s %6.3f s  peak %6.2f dBFS | head RMS %.4f  tail RMS %.4f  seam jump %.1f/32767"
              % (name, len(sig) / SR, db(sig), rms_head, rms_tail, seam))

    expected = list(SFX.keys()) + list(MUSIC.keys())
    missing = [n for n in expected if not os.path.isfile(os.path.join(OUT_DIR, n))]
    total_bytes = sum(os.path.getsize(os.path.join(OUT_DIR, n)) for n in written)
    print("\nFiles written: %d  (%.1f MB total)" % (len(written), total_bytes / 1e6))
    if missing:
        print("MISSING:", missing)
        raise SystemExit(1)
    print("All %d expected files present." % len(expected))


if __name__ == "__main__":
    main()
