from pathlib import Path
import json
import sys
import numpy as np
from PIL import Image, ImageFilter

project = Path(__file__).resolve().parents[2]
source_dir = project / "ArtSource" / "TerrainPrototype"
asset_dir = project / "Assets" / "Art" / "Terrain" / "Prototype"
artifact_dir = project / "Artifacts" / "PainterlyRoadLookdev"
source_copy = source_dir / "PainterlyRoadPrototype_Source.png"
raw = source_copy
albedo_path = asset_dir / "Terrain_DirtPath_Warm_Painterly.png"
normal_path = asset_dir / "Terrain_DirtPath_Warm_Painterly_Normal.png"
# Regenerate only the normal PNG after a derivative-convention correction.
if "--normal-only" in sys.argv:
    seamless = np.asarray(Image.open(albedo_path).convert("RGB"), dtype=np.float32)
    lum = Image.fromarray(seamless.astype(np.uint8), "RGB").convert("L").filter(ImageFilter.GaussianBlur(radius=4))
    height = np.asarray(lum, dtype=np.float32) / 255.0
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5 * 60.0
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5 * 60.0
    nx, ny, nz = -dx, dy, np.ones_like(dx)  # PNG rows run downward; Unity tangent +Y follows upward UV v.
    inv = 1.0 / np.sqrt(nx*nx + ny*ny + nz*nz)
    normal = np.stack((nx*inv, ny*inv, nz*inv), axis=-1)
    normal_rgb = np.clip(np.rint((normal * 0.5 + 0.5) * 255), 0, 255)
    def seam_blend_normal(arr, band=96):
        out = arr.copy()
        for axis in (1, 0):
            n = out.shape[axis]
            for d in range(band):
                weight = 0.25 * (1.0 + np.cos(np.pi * d / band))
                left_idx, right_idx = d, n - 1 - d
                if axis == 1:
                    left = out[:, left_idx, :].copy(); right = out[:, right_idx, :].copy()
                    out[:, left_idx, :] = (1 - weight) * left + weight * right
                    out[:, right_idx, :] = (1 - weight) * right + weight * left
                else:
                    top = out[left_idx, :, :].copy(); bottom = out[right_idx, :, :].copy()
                    out[left_idx, :, :] = (1 - weight) * top + weight * bottom
                    out[right_idx, :, :] = (1 - weight) * bottom + weight * top
        return np.clip(np.rint(out), 0, 255).astype(np.uint8)
    normal_rgb = seam_blend_normal(normal_rgb)
    normal_vec = normal_rgb.astype(np.float32) / 255.0 * 2.0 - 1.0
    normal_vec /= np.maximum(np.linalg.norm(normal_vec, axis=-1, keepdims=True), 1e-6)
    normal_rgb = np.clip(np.rint((normal_vec * 0.5 + 0.5) * 255), 0, 255).astype(np.uint8)
    Image.fromarray(normal_rgb, "RGB").save(normal_path, optimize=True)
    print(f"Updated normal only: {normal_path}; ny = +d(height)/d(PNG row)")
    raise SystemExit(0)
source_dir.mkdir(parents=True, exist_ok=True)
asset_dir.mkdir(parents=True, exist_ok=True)
artifact_dir.mkdir(parents=True, exist_ok=True)
raw_img = Image.open(raw).convert("RGB")
if raw.resolve() != source_copy.resolve():
    raw_img.save(source_copy)
img = raw_img.resize((1024, 1024), Image.Resampling.LANCZOS)
a = np.asarray(img, dtype=np.float32)
# Reduce excess orange/blue saturation while keeping lightness and palette layout.
luma = a[:, :, 0:1] * 0.299 + a[:, :, 1:2] * 0.587 + a[:, :, 2:3] * 0.114
a = luma + (a - luma) * 0.74
# Add subtle periodic micro-variation without introducing directional shading.
rng = np.random.default_rng(20260924)
white = rng.normal(0.0, 1.0, (1024, 1024)).astype(np.float32)
tiled = np.tile(white, (3, 3))
cloud_image = Image.fromarray(np.clip(np.rint(tiled * 16 + 128), 0, 255).astype(np.uint8), mode="L").filter(ImageFilter.GaussianBlur(radius=10))
cloud = (np.asarray(cloud_image.crop((1024, 1024, 2048, 2048)), dtype=np.float32) - 128.0) / 16.0
cloud = (cloud - cloud.mean()) / max(float(cloud.std()), 1e-6)
white = (white - white.mean()) / max(float(white.std()), 1e-6)
variation = cloud * 3.0 + white * 0.9
a = np.clip(a + variation[:, :, None], 0, 255)
# Pair opposite edge bands using symmetric averaging at the seam, fading smoothly inward.
def seam_blend(arr, band=96):
    out = arr.copy()
    for axis in (1, 0):
        n = out.shape[axis]
        for d in range(band):
            weight = 0.25 * (1.0 + np.cos(np.pi * d / band))  # 0.5 at edge, 0 at inner limit
            left_idx, right_idx = d, n - 1 - d
            if axis == 1:
                left = out[:, left_idx, :].copy()
                right = out[:, right_idx, :].copy()
                out[:, left_idx, :] = (1 - weight) * left + weight * right
                out[:, right_idx, :] = (1 - weight) * right + weight * left
            else:
                top = out[left_idx, :, :].copy()
                bottom = out[right_idx, :, :].copy()
                out[left_idx, :, :] = (1 - weight) * top + weight * bottom
                out[right_idx, :, :] = (1 - weight) * bottom + weight * top
    return np.clip(np.rint(out), 0, 255).astype(np.uint8)
seamless = seam_blend(a)
Image.fromarray(seamless, "RGB").save(albedo_path, optimize=True)
# Derive shallow relief from luminance variation, not baked directional lighting.
lum = Image.fromarray(seamless, "RGB").convert("L").filter(ImageFilter.GaussianBlur(radius=4))
height = np.asarray(lum, dtype=np.float32) / 255.0
dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5 * 60.0
dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5 * 60.0
nx, ny, nz = -dx, dy, np.ones_like(dx)  # PNG rows run downward; Unity tangent +Y follows upward UV v.
inv = 1.0 / np.sqrt(nx*nx + ny*ny + nz*nz)
normal = np.stack((nx*inv, ny*inv, nz*inv), axis=-1)
normal_rgb = np.clip(np.rint((normal * 0.5 + 0.5) * 255), 0, 255)
normal_rgb = seam_blend(normal_rgb)
# Restore unit tangent vectors after matching wrap borders.
normal_vec = normal_rgb.astype(np.float32) / 255.0 * 2.0 - 1.0
normal_vec /= np.maximum(np.linalg.norm(normal_vec, axis=-1, keepdims=True), 1e-6)
normal_rgb = np.clip(np.rint((normal_vec * 0.5 + 0.5) * 255), 0, 255).astype(np.uint8)
Image.fromarray(normal_rgb, "RGB").save(normal_path, optimize=True)
# 3x3 repeat for seam/repetition review.
repeat = np.tile(seamless, (3, 3, 1))
Image.fromarray(repeat, "RGB").resize((768, 768), Image.Resampling.NEAREST).save(artifact_dir / "PainterlyRoadPrototype_3x3.png", optimize=True)
def stats(image):
    x = np.abs(image[:, 0, :].astype(np.int16) - image[:, -1, :].astype(np.int16))
    y = np.abs(image[0, :, :].astype(np.int16) - image[-1, :, :].astype(np.int16))
    return {"x_mean_delta_8bit": float(x.mean()), "x_max_delta_8bit": int(x.max()), "y_mean_delta_8bit": float(y.mean()), "y_max_delta_8bit": int(y.max())}
report = {
    "source_imagegen_path": str(raw),
    "source_copy": str(source_copy),
    "tile_size": [1024, 1024],
    "chroma_adjustment": "Reduced RGB chroma to 74% around luminance to temper orange and blue while retaining hue layout.",
    "micro_variation": "Fixed-seed tileable Gaussian cloud noise, standard deviation 3.0/255, plus white grain 0.9/255, added equally to RGB channels; no directional light term.",
    "seam_processing": "Opposing 96 px edge bands use symmetric raised-cosine cross-blending, with 50/50 edge average fading to unchanged pixels at band inner limits.",
    "normal_derivation": "Convert processed albedo to Rec.601 grayscale luminance, Gaussian blur radius 4 px, calculate wrapped central-difference gradients, scale slope by 60, normalize tangent-space vectors. Match opposite edge bands and renormalize. Shallow relief cue; no cast-light information is used.",
    "albedo_edges": stats(seamless),
    "normal_edges": stats(normal_rgb),
    "albedo_asset": str(albedo_path),
    "normal_asset": str(normal_path),
    "repeat_montage": str(artifact_dir / "PainterlyRoadPrototype_3x3.png")
}
(artifact_dir / "texture_processing_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))

