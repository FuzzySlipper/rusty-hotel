"""Offline authoring of Hotel's quiet, seamless mechanical beds; never runs at game/build time."""
from pathlib import Path
import math, random, wave, struct

RATE = 22050
SECONDS = 12
N = RATE * SECONDS
ROOT = Path(__file__).resolve().parents[1] / 'content' / 'audio'
ROOT.mkdir(parents=True, exist_ok=True)

def write(name, seed, hum, hiss):
    rng = random.Random(seed)
    low = 0.0
    samples = []
    for i in range(N):
        t = i / RATE
        low = low * .94 + rng.uniform(-1, 1) * .06
        # All oscillators have an integer number of cycles over the loop.
        slow = 1 + .13 * math.sin(2 * math.pi * t / 6)
        value = hum * (math.sin(2*math.pi*60*t) + .25*math.sin(2*math.pi*120*t)) * slow
        value += hiss * low
        samples.append(value)
    # Short wrap crossfade prevents a waveform discontinuity; no periodic silence.
    fade = 1024
    for i in range(fade):
        u = i / fade
        samples[N-fade+i] = samples[N-fade+i]*(1-u) + samples[i]*u
    samples = samples[fade:]
    with wave.open(str(ROOT / name), 'wb') as f:
        f.setnchannels(1); f.setsampwidth(2); f.setframerate(RATE)
        f.writeframes(b''.join(struct.pack('<h', round(max(-1,min(1,v))*32767)) for v in samples))
    rms = math.sqrt(sum(v*v for v in samples)/len(samples))
    print(f'{name}: peak={max(abs(v) for v in samples):.4f} rms={20*math.log10(rms):.1f} dBFS')

write('ventilation.wav', 9326, .095, .28)
write('tape-room.wav', 1973, .015, .35)
