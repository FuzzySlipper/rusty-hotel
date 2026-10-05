"""Derives tileable normal maps from the hotel's wallpaper and carpet textures (optional offline authoring).

    python3 tools/author-normals.py

Needs numpy and Pillow. Each map is a height field made from the source texture plus seeded grain, differentiated
with periodic (wrap-around) operations so it tiles exactly like its source. Writes RGBA8 PNGs to content/materials/.
"""
import os
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
MATERIALS = os.path.join(ROOT, "content", "materials")

# source, output, ink emboss, emboss blur (px), grain amount, grain blur (px), strength, seed
MAPS = [
    ("medallion.png", "medallion-normal.png", 0.35, 2.0, 0.65, 0.8, 2.2, 11),
    ("angular.png", "angular-normal.png", 0.35, 2.0, 0.65, 0.8, 2.2, 12),
    ("carpet.png", "carpet-normal.png", 0.0, 0.0, 1.0, 0.6, 3.5, 13),
]

def periodic_blur(field, sigma):
    if sigma <= 0:
        return field
    h, w = field.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    kernel = np.exp(-2 * (np.pi * sigma) ** 2 * (fx ** 2 + fy ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(field) * kernel))

def normalise(field):
    field = field - field.mean()
    spread = np.abs(field).max()
    return field / spread if spread > 0 else field

def author(source, output, emboss, emboss_blur, grain, grain_blur, strength, seed):
    rgb = np.asarray(Image.open(os.path.join(MATERIALS, source)).convert("RGB"), dtype=np.float64) / 255
    luminance = rgb @ np.array([0.2126, 0.7152, 0.0722])
    rng = np.random.default_rng(seed)
    height = np.zeros_like(luminance)
    if emboss > 0:
        # Printed ink stands very slightly proud of the paper.
        height += emboss * normalise(periodic_blur(luminance, emboss_blur))
    if grain > 0:
        noise = periodic_blur(rng.standard_normal(luminance.shape), grain_blur)
        # Carpet nap follows the weave: modulate the grain by the texture's own detail.
        detail = luminance - periodic_blur(luminance, 3.0)
        height += grain * normalise(noise + (4 * detail if emboss == 0 else 0))
    height = normalise(height)
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) / 2
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) / 2
    nx, ny, nz = -dx * strength, dy * strength, np.ones_like(height)
    length = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
    normal = np.stack([nx / length, ny / length, nz / length], axis=-1)
    pixels = np.clip((normal * 0.5 + 0.5) * 255 + 0.5, 0, 255).astype(np.uint8)
    alpha = np.full(pixels.shape[:2] + (1,), 255, dtype=np.uint8)
    Image.fromarray(np.concatenate([pixels, alpha], axis=-1), "RGBA").save(os.path.join(MATERIALS, output))
    print(output, pixels.shape[1], "x", pixels.shape[0])

for entry in MAPS:
    author(*entry)
