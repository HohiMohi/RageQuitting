"""Generate deterministic centered terrain relief stamp masks through Blender Python.

Run with:
  blender.exe --background --python generate_brush_masks_v2.py

The output is grayscale 8-bit PNG. White is full influence; black is no influence.
Stamps are centered, jagged mineral/earth fragments with a short soft outer falloff.
"""
from __future__ import annotations

import math
import os
import random
import struct
import zlib

import bpy


SIZE = 256
OUT_DIR = r"D:\Programy\UnityProjects\RageQuitting\Assets\Art\Environment\TerrainRoadLookdev\SurfaceV2"


def _png_chunk(kind: bytes, payload: bytes) -> bytes:
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)


def _write_gray_png(path: str, values: list[int]) -> None:
    rows = []
    for y in range(SIZE):
        row = bytes(values[y * SIZE:(y + 1) * SIZE])
        rows.append(b"\x00" + row)
    raw = b"".join(rows)
    header = struct.pack(">IIBBBBB", SIZE, SIZE, 8, 0, 0, 0, 0)
    with open(path, "wb") as stream:
        stream.write(b"\x89PNG\r\n\x1a\n")
        stream.write(_png_chunk(b"IHDR", header))
        stream.write(_png_chunk(b"IDAT", zlib.compress(raw, 9)))
        stream.write(_png_chunk(b"IEND", b""))


def _smoothstep(a: float, b: float, x: float) -> float:
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def _make_mask(seed: int, lobes: int, radial_jag: float, cuts: int) -> list[int]:
    rng = random.Random(seed)
    phases = [rng.uniform(0.0, math.tau) for _ in range(7)]
    weights = [rng.uniform(0.035, 0.15) for _ in phases]
    cut_specs = []
    for _ in range(cuts):
        angle = rng.uniform(-math.pi, math.pi)
        width = rng.uniform(0.035, 0.11)
        depth = rng.uniform(0.06, 0.16)
        start = rng.uniform(0.46, 0.77)
        cut_specs.append((angle, width, depth, start))

    result = [0] * (SIZE * SIZE)
    for py in range(SIZE):
        for px in range(SIZE):
            x = (px + 0.5 - SIZE * 0.5) / (SIZE * 0.5)
            y = (py + 0.5 - SIZE * 0.5) / (SIZE * 0.5)
            angle = math.atan2(y, x)
            radius = math.hypot(x, y)
            contour = 0.715
            contour += 0.085 * math.sin(lobes * angle + phases[0])
            contour += 0.055 * math.sin((lobes + 2) * angle + phases[1])
            contour += 0.035 * math.sin(3 * angle + phases[2])
            contour += radial_jag * math.sin(11 * angle + phases[3])
            contour += 0.018 * math.sin(19 * angle + phases[4])
            contour += 0.012 * math.sin(27 * angle + phases[5])
            contour += 0.01 * math.sin(37 * angle + phases[6])

            for cut_angle, width, depth, start in cut_specs:
                da = abs((angle - cut_angle + math.pi) % math.tau - math.pi)
                if da < width:
                    cut_profile = 1.0 - da / width
                    radial_gate = _smoothstep(start, start + 0.11, radius)
                    contour -= depth * cut_profile * radial_gate

            # A few broad inward notches prevent the outline reading like a regular polygon.
            inward_notch = max(0.0, math.cos(angle * (lobes + 1) + phases[2])) ** 14
            contour -= 0.035 * inward_notch

            signed = contour - radius
            softness = 0.035
            value = _smoothstep(-softness, softness * 0.75, signed)
            # Smoothly feather only the outside silhouette; retain a firm, readable center.
            value *= _smoothstep(0.965, 0.72, radius)
            result[py * SIZE + px] = int(max(0.0, min(1.0, value)) * 255.0 + 0.5)
    return result


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    specs = [
        ("B_ReliefShard_A.png", 211, 5, 0.045, 3),
        ("B_ReliefShard_B.png", 877, 7, 0.035, 2),
        ("B_ReliefShard_C.png", 1409, 4, 0.055, 4),
    ]
    for name, seed, lobes, jag, cuts in specs:
        _write_gray_png(os.path.join(OUT_DIR, name), _make_mask(seed, lobes, jag, cuts))

    print("Generated centered relief masks in", OUT_DIR)


if __name__ == "__main__":
    main()
