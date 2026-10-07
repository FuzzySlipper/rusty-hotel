# Writes the weathering noise the aged surface shader samples (offline; never on the build path).
#   python3 art/weathering-01/noise.py
# A 512-square tileable RGBA8 PNG, read as linear data: each channel is band-limited noise made periodic by building it
# in the frequency domain, normalised to the full range. R, broad patches, is wear; G, blotches, is water stains; B,
# streaks long down the image and narrow across it, is where a wallpaper seam lifts; A is fine grain. The seed is fixed,
# so the file is reproduced exactly. Writes content/materials/weathering/noise.png.
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "content", "materials", "weathering", "noise.png")
SIZE = 512
rng = np.random.default_rng(9627)
fy, fx = np.meshgrid(np.fft.fftfreq(SIZE) * SIZE, np.fft.fftfreq(SIZE) * SIZE, indexing="ij")

def band(low, high, power, stretch=(1, 1)):
    """Noise whose frequencies (cycles per image, x and y scaled by stretch) lie between low and high, falling as f^-power."""
    f = np.hypot(fx * stretch[0], fy * stretch[1])
    shape = np.where((f >= low) & (f <= high), np.maximum(f, 1) ** -power, 0)
    field = np.real(np.fft.ifft2(np.fft.fft2(rng.standard_normal((SIZE, SIZE))) * shape))
    return (field - field.min()) / (field.max() - field.min())

channels = [band(1, 12, 1.2), band(2, 24, 1.0), band(1, 16, 1.0, stretch=(1, 6)), band(24, 160, 0.5)]
pixels = np.stack([np.round(c * 255).astype(np.uint8) for c in channels], axis=-1)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
Image.fromarray(pixels, "RGBA").save(OUT, optimize=True)
print("WROTE", os.path.relpath(OUT), [round(float(c.std()), 3) for c in channels])
