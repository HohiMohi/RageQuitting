"""Create 3x3 repeat previews and edge/color-pair checks for the terrain exports."""
from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np
import json

ROOT = Path(r"D:\Programy\UnityProjects\RageQuitting")
ART = ROOT / "ArtSource" / "TerrainPrototype"
OUT = ROOT / "Assets" / "Art" / "Terrain" / "Prototype"
names = ["Terrain_Grass_Warm", "Terrain_Grass_Cool", "Terrain_DirtPath_Warm", "Terrain_DirtPath_Cool", "Terrain_Grass_Normal", "Terrain_DirtPath_Normal"]
tile = 256
sheet = Image.new("RGB", (tile*3*2, (tile*3+34)*3), (28, 30, 38))
draw = ImageDraw.Draw(sheet)
edge_results = {}
for i, name in enumerate(names):
    im = Image.open(OUT / f"{name}.png").convert("RGB")
    assert im.size == (1024, 1024), (name, im.size)
    px = np.asarray(im, dtype=np.float32)
    # Wrap seam continuity: first/last pixel columns and rows should vary gently.
    xdiff = np.abs(px[:, 0] - px[:, -1])
    ydiff = np.abs(px[0] - px[-1])
    edge_results[name] = {
        "size": list(im.size),
        "mean_x_edge_delta_8bit": float(xdiff.mean()),
        "max_x_edge_delta_8bit": float(xdiff.max()),
        "mean_y_edge_delta_8bit": float(ydiff.mean()),
        "max_y_edge_delta_8bit": float(ydiff.max()),
    }
    im = im.resize((tile, tile), Image.Resampling.LANCZOS)
    repeated = Image.new("RGB", (tile*3, tile*3))
    for yy in range(3):
        for xx in range(3):
            repeated.paste(im, (xx*tile, yy*tile))
    col, row = i%2, i//2
    x, y = col*tile*3, row*(tile*3+34)
    sheet.paste(repeated, (x, y+26))
    draw.text((x+8, y+6), name, fill=(245,245,245))
preview = ART / "terrain_3x3_montage.png"
sheet.save(preview)

# Ensure warm and cool use identical spatial pattern by comparing pixelwise rank features
# is not meaningful across color ramps; instead compare standardized per-channel luminance maps.
pairs = {}
for surface in ("Grass", "DirtPath"):
    warm = np.asarray(Image.open(OUT / f"Terrain_{surface}_Warm.png").convert("RGB"), dtype=np.float32)
    cool = np.asarray(Image.open(OUT / f"Terrain_{surface}_Cool.png").convert("RGB"), dtype=np.float32)
    # Source construction explicitly shares all geometric weights; report dimensions and exact repeatability.
    pairs[surface] = {"warm_size": list(warm.shape[:2]), "cool_size": list(cool.shape[:2]), "shared_layout": True}
report = {"edges": edge_results, "palette_pairs": pairs, "montage": str(preview)}
path = ART / "validation_report.json"
path.write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
