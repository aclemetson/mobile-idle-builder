# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy>=1.26"]
# ///
"""Procedural music prototype for the game.

Each piece is defined as note data (chords, bass, drums, melody, generative "particle pings").
For every piece the script writes:
  * <name>.mid  - type-1 multi-track MIDI (one track per part, drums on channel 10 / GM map)
                  for import into Ableton
  * <name>.wav  - a rendered preview (44.1 kHz stereo 16-bit); loops are seamless

Run:  uv run scripts/music/compose.py [--out DIR] [--seed N] [--piece NAME ...]
"""
from __future__ import annotations

import argparse
import struct
import wave
from dataclasses import dataclass
from pathlib import Path

import numpy as np

SR = 44100


@dataclass
class Note:
    track: str
    pitch: int      # MIDI note number (GM drum map on the Drums track)
    start: float    # beats
    length: float   # beats
    vel: int        # 1..127


def mtof(p: float) -> float:
    return 440.0 * 2 ** ((p - 69) / 12)


# =========================================================================== pieces

# ---- quantum_field: D major / Lydian colour: Dmaj9 - Bm11 - Gmaj7#11 - Asus(add9)
QF_CHORDS = [
    (38, [50, 57, 61, 64, 66]),
    (35, [47, 54, 57, 62, 64]),
    (31, [43, 50, 54, 59, 61]),
    (33, [45, 52, 59, 62, 64]),
]


def quantum_field(rng: np.random.Generator) -> dict:
    """Ambient loop: slow pads, sub bass, sparse glassy particle pings."""
    bpm, bar, bars_per_chord, cycles = 72, 4, 2, 2
    notes: list[Note] = []
    beat = 0.0
    for cycle in range(cycles):
        density = 0.20 if cycle == 0 else 0.30
        for root, voicing in QF_CHORDS:
            span = bars_per_chord * bar
            notes += [Note("Pad", p, beat, span, 60) for p in voicing]
            notes.append(Note("Bass", root, beat, span, 72))
            pool = [p + 24 for p in voicing] + [p + 12 for p in voicing[2:]]
            for i in range(int(span * 2)):  # 8th-note grid
                if rng.random() < density:
                    notes.append(Note("Pings", int(rng.choice(pool)), beat + i * 0.5, 0.5,
                                      int(rng.integers(45, 95))))
            beat += span
    return dict(name="quantum_field_loop", bpm=bpm, length_beats=beat, notes=notes, loop=True,
                tracks=["Pad", "Bass", "Pings"])


def discovery(rng: np.random.Generator) -> dict:
    """Short stinger, e.g. research complete / new element discovered."""
    run = [62, 66, 69, 73, 76, 78, 81, 85]
    notes = [Note("Pings", p, i * 0.25, 0.5, 70 + i * 4) for i, p in enumerate(run)]
    notes += [Note("Pad", p, 0, 5, 58) for p in [50, 57, 61, 64, 66]]
    notes.append(Note("Bass", 38, 0, 5, 70))
    return dict(name="discovery_stinger", bpm=96, length_beats=6, notes=notes, loop=False,
                tracks=["Pad", "Bass", "Pings"])


# ---- lofi_lab: calm lo-fi electronica, 80 bpm, 36 bars (108 s), sectioned to avoid
#      feeling loopy: Intro(4) A(8) A-melody(8) B-bridge(8) A-return(8) -> wraps to Intro.

# (root for bass, electric-piano voicing). Rootless jazz voicings in the middle register.
PROG_A = [(38, [53, 57, 60, 64]),   # Dm9
          (43, [53, 57, 59, 64]),   # G13
          (36, [52, 55, 59, 62]),   # Cmaj9
          (45, [55, 59, 60, 64])]   # Am9
PROG_B = [(41, [57, 60, 64, 67]),   # Fmaj9
          (40, [55, 59, 62, 66]),   # Em9
          (38, [53, 57, 60, 64]),   # Dm9
          (39, [55, 58, 62, 65]),   # Ebmaj9 (borrowed colour)
          (41, [57, 60, 64, 67]),   # Fmaj9
          (40, [55, 59, 62, 66]),   # Em9
          (45, [55, 59, 60, 64]),   # Am9
          (43, [53, 57, 60, 64])]   # G13sus
MELODY_SCALE = [64, 67, 69, 72, 74, 76, 79, 81]   # C major pentatonic, E4..A5

KEYS_COMP = [[(0, 1.5), (2.5, 1.0)],
             [(0, 2.0), (3.5, 0.5)],
             [(0, 3.5)],
             [(0, 1.0), (1.5, 1.0), (3, 0.75)],
             [(0.5, 1.5), (2.5, 1.5)]]
KEYS_COMP_B = [[(0, 4.0)], [(0, 2.0), (2.5, 1.5)], [(0, 3.0), (3.5, 0.5)]]
BASS_PATTERNS = [[(0, 1.5, 0), (2.5, 1.0, 0)],
                 [(0, 1.0, 0), (1.5, 0.5, 7), (2, 1.5, 0)],
                 [(0, 2.5, 0), (3, 0.75, 12)],
                 [(0, 1.5, 0), (2.5, 0.5, 0), (3.5, 0.5, 7)]]

KICK, RIM, SNARE, HAT, OPEN_HAT = 36, 37, 38, 42, 46


def _phrase(rng, bars: int = 2) -> list[tuple[float, int, float]]:
    """A short melodic idea: (beat offset, scale index, length) on a swung 8th grid."""
    slots = sorted(rng.choice(np.arange(1, bars * 8 - 2), size=int(rng.integers(5, 8)), replace=False))
    idx = int(rng.integers(2, 6))
    out = []
    for i, s in enumerate(slots):
        idx = int(np.clip(idx + rng.choice([-2, -1, -1, 1, 1, 2]), 0, len(MELODY_SCALE) - 1))
        nxt = slots[i + 1] if i + 1 < len(slots) else bars * 8
        out.append((s * 0.5, idx, min(1.5, (nxt - s) * 0.5)))
    last_beat, last_idx, _ = out[-1]
    out[-1] = (last_beat, last_idx, bars * 4 - last_beat)  # let the last note ring
    return out


def _vary(rng, phrase):
    """Same rhythm, nudge the tail pitches - recognisable but not identical."""
    out = list(phrase)
    for j in range(max(0, len(out) - 2), len(out)):
        b, i, l = out[j]
        out[j] = (b, int(np.clip(i + rng.choice([-1, 1]), 0, len(MELODY_SCALE) - 1)), l)
    return out


def lofi_lab(rng: np.random.Generator) -> dict:
    notes: list[Note] = []
    sections = ([("intro", PROG_A)] + [("groove", PROG_A * 2)] + [("melody", PROG_A * 2)]
                + [("bridge", PROG_B)] + [("return", PROG_A * 2)])

    p, q = _phrase(rng), _phrase(rng)
    melody_plan = {"melody": [p, q, _vary(rng, p), None],
                   "return": [_vary(rng, q), p, _vary(rng, _vary(rng, p)), None]}

    bar_no = 0
    total_bars = sum(len(c) for _, c in sections)
    for name, chords in sections:
        for i, (root, voicing) in enumerate(chords):
            b0 = bar_no * 4
            last_bar = bar_no == total_bars - 1
            phrase_end = i % 4 == 3

            # --- electric piano
            comp = KEYS_COMP_B if name == "bridge" else KEYS_COMP
            pattern = comp[int(rng.integers(len(comp)))]
            if last_bar or (name == "intro" and i == 0):
                pattern = [(0, 4.0)]
            for off, ln in pattern:
                strum = rng.choice([-1, 1]) * 0.03
                vel = int(rng.integers(50, 70))
                for k, pitch in enumerate(voicing):
                    notes.append(Note("Keys", pitch, b0 + off + max(0, k * strum + (3 * 0.03 if strum < 0 else 0)),
                                      ln, vel + int(rng.integers(-5, 6))))

            # --- bass
            if not (name == "intro" and i < 2):
                if name == "bridge":
                    notes.append(Note("Bass", root, b0, 3.5, 78))
                else:
                    for off, ln, iv in BASS_PATTERNS[int(rng.integers(len(BASS_PATTERNS)))]:
                        notes.append(Note("Bass", root + iv, b0 + off, ln, int(rng.integers(70, 88))))

            # --- drums
            if name == "intro":
                if i >= 2:
                    notes += [Note("Drums", HAT, b0 + h, 0.25, int(rng.integers(28, 40))) for h in range(4)]
            elif name == "bridge" or last_bar:
                if not last_bar:
                    notes.append(Note("Drums", KICK, b0, 0.5, 90))
                    if rng.random() < 0.5:
                        notes.append(Note("Drums", KICK, b0 + 2.5, 0.5, 70))
                    notes.append(Note("Drums", RIM, b0 + 2, 0.25, 70))
                notes += [Note("Drums", HAT, b0 + h, 0.25, int(rng.integers(30, 45))) for h in range(4)]
            else:
                notes.append(Note("Drums", KICK, b0, 0.5, 100))
                notes.append(Note("Drums", KICK, b0 + 2.5, 0.5, 88))
                if rng.random() < 0.3:
                    notes.append(Note("Drums", KICK, b0 + 1.75, 0.5, 70))
                notes.append(Note("Drums", SNARE, b0 + 1, 0.5, int(rng.integers(78, 90))))
                notes.append(Note("Drums", SNARE, b0 + 3, 0.5, int(rng.integers(78, 90))))
                if rng.random() < 0.25:
                    notes.append(Note("Drums", SNARE, b0 + 2.75, 0.25, 34))
                for h in range(8):
                    if phrase_end and h == 7:
                        notes.append(Note("Drums", OPEN_HAT, b0 + 3.5, 0.5, 50))
                    elif rng.random() > 0.08:
                        notes.append(Note("Drums", HAT, b0 + h * 0.5, 0.25,
                                          int(rng.integers(50, 62) if h % 2 == 0 else rng.integers(32, 44))))

            # --- particle pings (ties the lo-fi track to the game's identity)
            if name in ("bridge", "return") and not last_bar:
                pool = [v + 24 for v in voicing]
                for s in range(8):
                    if rng.random() < (0.16 if name == "bridge" else 0.08):
                        notes.append(Note("Pings", int(rng.choice(pool)), b0 + s * 0.5, 0.5,
                                          int(rng.integers(45, 80))))
            bar_no += 1

        # --- lead melody, in 2-bar phrases
        if name in melody_plan:
            start_bar = bar_no - len(chords)
            for slot, ph in enumerate(melody_plan[name]):
                if ph is None:
                    continue
                for off, idx, ln in ph:
                    notes.append(Note("Lead", MELODY_SCALE[idx], (start_bar + slot * 2) * 4 + off, ln,
                                      int(rng.integers(60, 80))))

    return dict(name="lofi_lab_loop", bpm=80, length_beats=total_bars * 4, notes=notes, loop=True,
                tracks=["Keys", "Bass", "Lead", "Drums", "Pings"], swing=0.58, humanize=0.008,
                post=lofi_post, master_lowpass=7000)


PIECES = {"quantum_field": quantum_field, "discovery": discovery, "lofi_lab": lofi_lab}


# =========================================================================== instruments
# Signature: (pitch, duration_seconds, velocity, rng) -> mono float array

def _hold_release(n: int, hold: float, rel: float) -> np.ndarray:
    env = np.ones(n)
    nh = int(hold * SR)
    if nh < n:
        env[nh:] = np.exp(-np.arange(n - nh) / (rel * SR / 4.6))
    return env


def _attack(n: int, a: float) -> np.ndarray:
    env = np.ones(n)
    na = min(n, max(1, int(a * SR)))
    env[:na] = np.linspace(0, 1, na) ** 2
    return env


def pad(pitch, dur, vel, rng):
    f, rel = mtof(pitch), 2.5
    n = int((dur + rel) * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for cents in (-7, 7):
        ff = f * 2 ** (cents / 1200)
        for k in range(1, 9):
            if ff * k > 6000:
                break
            out += np.sin(2 * np.pi * ff * k * t + rng.random() * 6.283) / k ** 1.3
    out *= 1 + 0.15 * np.sin(2 * np.pi * 0.13 * t + rng.random() * 6.283)
    return out * _attack(n, 1.8) * _hold_release(n, dur, rel) * (vel / 127) * 0.035


def sub_bass(pitch, dur, vel, rng):
    f, rel = mtof(pitch), 1.5
    n = int((dur + rel) * SR)
    t = np.arange(n) / SR
    out = np.sin(2 * np.pi * f * t) + 0.25 * np.sin(4 * np.pi * f * t)
    return out * _attack(n, 0.6) * _hold_release(n, dur, rel) * (vel / 127) * 0.22


def ping(pitch, dur, vel, rng):
    f = mtof(pitch)
    n = int(4.0 * SR)
    t = np.arange(n) / SR
    index = 2.2 * np.exp(-t / 0.25)                       # bright strike, then pure tone
    out = np.sin(2 * np.pi * f * t + np.sin(2 * np.pi * f * 3.5 * t) * index)
    env = np.exp(-t / 0.8) * np.minimum(1, t / 0.003)
    return out * env * (vel / 127) * 0.07


def epiano(pitch, dur, vel, rng):
    """Rhodes-ish FM: 1:1 modulator for the bark, quick high 'tine', slow tremolo."""
    f, rel = mtof(pitch), 0.35
    n = int((dur + rel) * SR)
    t = np.arange(n) / SR
    index = (0.6 + 1.4 * vel / 127) * np.exp(-t / 0.3) + 0.12
    out = np.sin(2 * np.pi * f * t + index * np.sin(2 * np.pi * f * t))
    out += 0.10 * np.sin(2 * np.pi * f * 14 * t) * np.exp(-t / 0.02)
    out *= 1 + 0.12 * np.sin(2 * np.pi * 4.2 * t)
    env = _attack(n, 0.004) * np.exp(-t / 2.2) * _hold_release(n, dur, rel)
    return out * env * (vel / 127) * 0.09


def lofi_bass(pitch, dur, vel, rng):
    f, rel = mtof(pitch), 0.15
    n = int((dur + rel) * SR)
    t = np.arange(n) / SR
    out = np.tanh(1.6 * (np.sin(2 * np.pi * f * t) + 0.3 * np.sin(4 * np.pi * f * t)))
    env = _attack(n, 0.01) * np.exp(-t / 1.5) * _hold_release(n, dur, rel)
    return out * env * (vel / 127) * 0.2


def lead(pitch, dur, vel, rng):
    """Soft triangle-ish lead with delayed vibrato."""
    f, rel = mtof(pitch), 0.45
    n = int((dur + rel) * SR)
    t = np.arange(n) / SR
    vib = 1 + 0.004 * np.sin(2 * np.pi * 5 * t) * np.clip((t - 0.3) / 0.4, 0, 1)
    phase = 2 * np.pi * np.cumsum(f * vib) / SR
    out = sum(((-1) ** ((k - 1) // 2)) * np.sin(k * phase) / k ** 2 for k in (1, 3, 5, 7))
    env = _attack(n, 0.03) * _hold_release(n, dur, rel)
    return out * env * (vel / 127) * 0.11


def _band(x: np.ndarray, lo: float, hi: float) -> np.ndarray:
    spec = np.fft.rfft(x)
    fr = np.fft.rfftfreq(len(x), 1 / SR)
    spec *= (fr >= lo) & (fr <= hi)
    return np.fft.irfft(spec, n=len(x))


def drums(pitch, dur, vel, rng):
    v = vel / 127
    if pitch == KICK:
        n = int(0.5 * SR)
        t = np.arange(n) / SR
        freq = 46 + 70 * np.exp(-t / 0.035)
        out = np.sin(2 * np.pi * np.cumsum(freq) / SR) * np.exp(-t / 0.26)
        out[: int(0.002 * SR)] += 0.3
        return np.tanh(out * 1.3) * v * 0.3
    if pitch == SNARE:
        n = int(0.32 * SR)
        t = np.arange(n) / SR
        noise = _band(rng.standard_normal(n), 900, 7000) * np.exp(-t / 0.11)
        body = np.sin(2 * np.pi * 185 * t) * np.exp(-t / 0.05)
        return (0.55 * noise / (np.abs(noise).max() + 1e-9) + 0.5 * body) * v * 0.14
    if pitch == RIM:
        n = int(0.08 * SR)
        t = np.arange(n) / SR
        out = _band(rng.standard_normal(n), 1200, 3500) * np.exp(-t / 0.012)
        out += np.sin(2 * np.pi * 820 * t) * np.exp(-t / 0.015)
        return out / (np.abs(out).max() + 1e-9) * v * 0.1
    # hats
    tau, length = (0.16, 0.6) if pitch == OPEN_HAT else (0.022, 0.12)
    n = int(length * SR)
    t = np.arange(n) / SR
    out = _band(rng.standard_normal(n), 6500, 14000) * np.exp(-t / tau)
    return out / (np.abs(out).max() + 1e-9) * v * 0.045


INSTRUMENTS = {"Pad": pad, "Bass": sub_bass, "Pings": ping}
LOFI_INSTRUMENTS = {"Keys": epiano, "Bass": lofi_bass, "Lead": lead, "Drums": drums, "Pings": ping}
REVERB_SEND = {"Pad": 0.55, "Bass": 0.08, "Pings": 0.75, "Keys": 0.3, "Lead": 0.4, "Drums": 0.08}
PAN = {"Lead": 0.6, "Bass": 0.5, "Drums": 0.5}


# =========================================================================== dsp helpers

def lowpass(x: np.ndarray, fc: float) -> np.ndarray:
    """Zero-phase steep lowpass in the frequency domain (per channel)."""
    spec = np.fft.rfft(x, axis=0)
    freqs = np.fft.rfftfreq(x.shape[0], 1 / SR)
    spec *= (1 / np.sqrt(1 + (freqs / fc) ** 8))[:, None]
    return np.fft.irfft(spec, n=x.shape[0], axis=0)


def reverb(x: np.ndarray, rng, rt60: float = 4.5, length: float = 6.0) -> np.ndarray:
    n = int(length * SR)
    t = np.arange(n) / SR
    ir = rng.standard_normal((n, 2)) * np.exp(-6.9 * t / rt60)[:, None]
    ir[: int(0.03 * SR)] = 0                              # pre-delay
    ir = lowpass(ir, 5000)
    ir /= np.sqrt((ir ** 2).sum(axis=0))
    size = 1 << int(np.ceil(np.log2(x.shape[0] + n)))
    out = np.fft.irfft(np.fft.rfft(x, size, axis=0) * np.fft.rfft(ir, size, axis=0), size, axis=0)
    return out[: x.shape[0]]


def lofi_post(x: np.ndarray, rng) -> np.ndarray:
    """Tape wow/flutter + vinyl crackle/hiss. LFOs complete whole cycles over the
    buffer and the read wraps around, so a looping buffer stays seamless."""
    n = x.shape[0]
    secs = n / SR
    t = np.arange(n) / SR
    wow_f = max(1, round(0.35 * secs)) / secs
    flutter_f = max(1, round(5.5 * secs)) / secs
    delay = 0.0018 * np.sin(2 * np.pi * wow_f * t) + 0.00025 * np.sin(2 * np.pi * flutter_f * t)
    read = np.arange(n) - delay * SR
    out = np.stack([np.interp(read, np.arange(n), x[:, c], period=n) for c in range(2)], axis=1)

    crackle = np.zeros((n, 2))
    for c in range(2):
        pos = rng.integers(0, n, int(secs * 9))
        crackle[pos, c] = rng.choice([-1, 1], len(pos)) * rng.exponential(0.012, len(pos))
    crackle = lowpass(crackle, 4500)
    hiss = lowpass(rng.standard_normal((n, 2)), 5000) * 0.0012
    return out + crackle + hiss


# =========================================================================== render

def render(piece: dict, rng) -> np.ndarray:
    spb = 60 / piece["bpm"]
    tracks = piece["tracks"]
    instruments = LOFI_INSTRUMENTS if "Keys" in tracks else INSTRUMENTS
    swing, humanize = piece.get("swing", 0.5), piece.get("humanize", 0.0)
    loop_len = int(piece["length_beats"] * spb * SR)
    tail = int(8 * SR)
    total = loop_len + tail
    buses = {name: np.zeros((total, 2)) for name in tracks}

    for note in piece["notes"]:
        start_beat = note.start
        if abs((start_beat % 1) - 0.5) < 1e-6:
            start_beat += swing - 0.5
        start_s = start_beat * spb + (rng.uniform(-humanize, humanize) if humanize else 0)
        audio = instruments[note.track](note.pitch, note.length * spb, note.vel, rng)
        start = max(0, int(start_s * SR))
        end = min(total, start + len(audio))
        audio = audio[: end - start]
        if note.track == "Pings":
            pan = rng.uniform(0.15, 0.85)
        elif note.track == "Drums":
            pan = 0.62 if note.pitch in (HAT, OPEN_HAT) else 0.5
        elif note.track in PAN:
            pan = PAN[note.track]
        else:
            pan = 0.5 + np.clip((note.pitch - 55) / 40, -0.25, 0.25)
        buses[note.track][start:end, 0] += audio * np.sqrt(1 - pan)
        buses[note.track][start:end, 1] += audio * np.sqrt(pan)

    if "Pad" in buses:
        buses["Pad"] = lowpass(buses["Pad"], 1800)
    dry = sum(buses.values())
    send = sum(buses[k] * REVERB_SEND[k] for k in tracks)
    mix = dry + 0.9 * reverb(send, rng, rt60=4.5 if "Pad" in tracks else 2.2)

    if piece["loop"]:
        out = mix[:loop_len].copy()
        out[:tail] += mix[loop_len:]                      # wrap tail -> seamless loop
    else:
        out = mix
        fade = int(0.5 * SR)
        out[-fade:] *= np.linspace(1, 0, fade)[:, None]

    if "post" in piece:
        out = piece["post"](out, rng)
    if "master_lowpass" in piece:
        out = lowpass(out, piece["master_lowpass"])

    out = np.tanh(out * 1.2) / 1.2                        # gentle soft clip
    piece["_bus_rms_db"] = {k: 20 * np.log10(np.sqrt((b ** 2).mean()) + 1e-12) for k, b in buses.items()}
    return out / np.max(np.abs(out)) * 0.89               # -1 dBFS peak


def write_wav(path: Path, audio: np.ndarray) -> None:
    pcm = (np.clip(audio, -1, 1) * 32767).astype("<i2")
    with wave.open(str(path), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


# =========================================================================== midi

def _vlq(n: int) -> bytes:
    out = [n & 0x7F]
    n >>= 7
    while n:
        out.append((n & 0x7F) | 0x80)
        n >>= 7
    return bytes(reversed(out))


def write_midi(path: Path, piece: dict, tpq: int = 480) -> None:
    tempo = int(60_000_000 / piece["bpm"])
    chunks = [_vlq(0) + b"\xff\x51\x03" + tempo.to_bytes(3, "big")
              + _vlq(0) + b"\xff\x58\x04\x04\x02\x18\x08"
              + _vlq(0) + b"\xff\x2f\x00"]
    for i, name in enumerate(piece["tracks"]):
        ch = 9 if name == "Drums" else i
        events = []
        for n in piece["notes"]:
            if n.track != name:
                continue
            on, off = round(n.start * tpq), round((n.start + n.length) * tpq)
            events.append((on, 1, bytes([0x90 | ch, n.pitch, n.vel])))
            events.append((off, 0, bytes([0x80 | ch, n.pitch, 0])))
        events.sort(key=lambda e: (e[0], e[1]))          # note-offs before note-ons
        data = _vlq(0) + b"\xff\x03" + _vlq(len(name)) + name.encode()
        last = 0
        for tick, _, msg in events:
            data += _vlq(tick - last) + msg
            last = tick
        data += _vlq(0) + b"\xff\x2f\x00"
        chunks.append(data)
    with open(path, "wb") as f:
        f.write(b"MThd" + struct.pack(">IHHH", 6, 1, len(chunks), tpq))
        for c in chunks:
            f.write(b"MTrk" + struct.pack(">I", len(c)) + c)


# =========================================================================== main

def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", type=Path, default=Path(__file__).parent / "out")
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--piece", nargs="*", choices=sorted(PIECES), default=sorted(PIECES))
    args = ap.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)

    for key in args.piece:
        rng = np.random.default_rng(args.seed)
        piece = PIECES[key](rng)
        write_midi(args.out / f"{piece['name']}.mid", piece)
        audio = render(piece, rng)
        write_wav(args.out / f"{piece['name']}.wav", audio)
        levels = ", ".join(f"{k} {v:.0f}dB" for k, v in piece["_bus_rms_db"].items())
        print(f"{piece['name']}: {len(audio) / SR:.1f}s, {len(piece['notes'])} notes -> {args.out}  [{levels}]")


if __name__ == "__main__":
    main()
