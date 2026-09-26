"""Generate a seamless 4 m TerrainRoad lookdev set in Blender 5.1.2.

The shared periodic patch layout drives road color, relief normals, and grayscale
brush masks. Source blend and report are saved beside this script's project-level
ArtSource/Artifacts directories; only final PNG maps are written under Assets.
"""
import bpy
import json
import math
import os

import numpy as np


PROJECT = r"D:\Programy\UnityProjects\RageQuitting"
SOURCE = os.path.join(PROJECT, "ArtSource", "TerrainRoadReliefV2")
ASSET_DIR = os.path.join(PROJECT, "Assets", "Art", "Environment", "TerrainRoadLookdev", "SurfaceV2")
REPORT_DIR = os.path.join(PROJECT, "Artifacts", "TerrainRoadReliefV2")
BLEND_PATH = os.path.join(SOURCE, "TerrainRoadReliefV2.blend")
SIZE = 1024
TILE_METERS = 4.0
SEED = 24109

os.makedirs(SOURCE, exist_ok=True)
os.makedirs(ASSET_DIR, exist_ok=True)
os.makedirs(REPORT_DIR, exist_ok=True)
rng = np.random.default_rng(SEED)
y, x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)
u, v = x / SIZE, y / SIZE


def periodic_field(frequencies, seed_offset, smoothness=1.0):
    """A repeatable, edge-periodic field assembled from integer frequencies."""
    local = np.random.default_rng(SEED + seed_offset)
    out = np.zeros((SIZE, SIZE), np.float32)
    weight_sum = 0.0
    for i, freq in enumerate(frequencies):
        terms = np.zeros_like(out)
        for _ in range(7):
            angle = local.uniform(0.0, math.tau)
            phase = local.uniform(0.0, math.tau)
            kx = int(round(math.cos(angle) * freq))
            ky = int(round(math.sin(angle) * freq))
            terms += np.sin(math.tau * (kx * u + ky * v) + phase)
        weight = 1.0 / (1.0 + i * smoothness)
        out += terms * (weight / 7.0)
        weight_sum += weight
    out /= weight_sum
    out -= float(out.mean())
    out /= max(float(out.std()) * 2.2, 1.0e-5)
    return out


def make_patch_mask(patches, seed_offset, edge_softness_px=1.5):
    """Return a periodic union mask; patch tuple: u,v,diameter_m,angle,phase."""
    local = np.random.default_rng(SEED + seed_offset)
    mask = np.zeros((SIZE, SIZE), np.float32)
    for cu, cv, diameter, angle_deg, phase in patches:
        dx = (u - cu + 0.5) % 1.0 - 0.5
        dy = (v - cv + 0.5) % 1.0 - 0.5
        angle = math.radians(angle_deg)
        ca, sa = math.cos(angle), math.sin(angle)
        rx = (ca * dx + sa * dy) * TILE_METERS
        ry = (-sa * dx + ca * dy) * TILE_METERS
        aspect = float(local.uniform(0.72, 1.27))
        theta = np.arctan2(ry / aspect, rx * aspect)
        radius = np.sqrt((rx * aspect) ** 2 + (ry / aspect) ** 2)
        jagged_edge = (0.5 * diameter) * (
            1.0 + 0.12 * np.sin(3.0 * theta + phase)
            + 0.075 * np.cos(5.0 * theta - phase * 0.7)
            + 0.050 * np.sin(7.0 * theta + phase * 1.3)
            + 0.033 * np.cos(11.0 * theta - phase * 0.35)
            + 0.037 * np.sin(17.0 * theta + phase * 0.8)
            + 0.025 * np.cos(23.0 * theta - phase * 0.55)
        )
        softness = edge_softness_px * TILE_METERS / SIZE
        alpha = np.clip((jagged_edge - radius + softness) / (2.0 * softness), 0.0, 1.0)
        alpha = alpha * alpha * (3.0 - 2.0 * alpha)
        mask = np.maximum(mask, alpha.astype(np.float32))
    return mask


def scatter_patches(count, min_diameter, max_diameter, local_seed, edge_jaggle=1.0):
    local = np.random.default_rng(local_seed)
    patches = []
    for _ in range(count):
        patches.append((
            float(local.random()), float(local.random()),
            float(local.uniform(min_diameter, max_diameter)),
            float(local.uniform(-90.0, 90.0)), float(local.uniform(0.0, math.tau)) * edge_jaggle,
        ))
    return patches


def normal_from_height(height, strength):
    """Tangent-space normal map from the same periodic height/masks as albedo."""
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * (0.5 * strength)
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * (0.5 * strength)
    nx, ny, nz = -dx, -dy, np.ones_like(dx)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack((nx * inv * 0.5 + 0.5, ny * inv * 0.5 + 0.5,
                     nz * inv * 0.5 + 0.5), axis=2)


def write_png(name, rgb, colorspace):
    arr = np.clip(rgb, 0.0, 1.0).astype(np.float32)
    rgba = np.concatenate((arr, np.ones((SIZE, SIZE, 1), np.float32)), axis=2)
    image = bpy.data.images.new(name, width=SIZE, height=SIZE, alpha=False, float_buffer=False)
    # Set color space before writing pixels: Blender 5.1 reinitializes generated
    # image storage when this setting changes, which would otherwise save black maps.
    image.colorspace_settings.name = colorspace
    image.pixels.foreach_set(rgba.ravel())
    image.filepath_raw = os.path.join(ASSET_DIR, name + ".png")
    image.file_format = "PNG"
    image.save()
    image.pack()
    return image


def palette_noise(base, field, amplitude, tint):
    return np.clip(base + field[:, :, None] * amplitude * np.asarray(tint, np.float32), 0.0, 1.0)


# Stacked, overlapping radial patches create a continuous surface with no cell grid.
# Most earth/stone patches are 10–25 cm; a few broad fragments and many chips add scale.
earth_ochre_patches = scatter_patches(220, 0.10, 0.25, SEED + 31)
earth_brown_patches = scatter_patches(170, 0.075, 0.20, SEED + 32)
earth_tan_patches = scatter_patches(135, 0.09, 0.23, SEED + 33)
earth_large_patches = scatter_patches(10, 0.34, 0.60, SEED + 34)
stone_patches = scatter_patches(142, 0.10, 0.25, SEED + 35)
stone_small_patches = scatter_patches(38, 0.035, 0.09, SEED + 351)
stone_large_patches = scatter_patches(9, 0.30, 0.48, SEED + 36)
chips = scatter_patches(190, 0.010, 0.038, SEED + 37)

earth_ochre_mask = make_patch_mask(earth_ochre_patches, 41)
earth_brown_mask = make_patch_mask(earth_brown_patches, 42)
earth_tan_mask = make_patch_mask(earth_tan_patches, 43)
earth_large_mask = make_patch_mask(earth_large_patches, 44)
earth_patch_mask = np.maximum.reduce((earth_ochre_mask, earth_brown_mask, earth_tan_mask, earth_large_mask))
stone_mask = np.maximum.reduce((make_patch_mask(stone_patches, 45),
                                make_patch_mask(stone_small_patches, 451),
                                make_patch_mask(stone_large_patches, 46)))
chip_mask = make_patch_mask(chips, 47, edge_softness_px=0.65)
earth_mask = np.clip(1.0 - stone_mask, 0.0, 1.0)

road_broad = periodic_field([2, 3, 5, 8], 21)
road_mid = periodic_field([11, 15, 21, 29], 22)
road_fine = periodic_field([41, 57, 79], 23)
stone_grain = periodic_field([18, 27, 39, 55, 73], 24)
earth_grain = periodic_field([4, 7, 12, 18, 27, 41], 25)

deep_earth = np.array([0.275, 0.145, 0.078], np.float32)
earth_palette = np.array([
    [0.60, 0.355, 0.185], [0.575, 0.335, 0.17], [0.625, 0.375, 0.195],
    [0.55, 0.325, 0.17], [0.615, 0.365, 0.16], [0.59, 0.345, 0.18],
], np.float32)
grey_palette = np.array([
    [0.37, 0.385, 0.39], [0.405, 0.415, 0.41], [0.44, 0.445, 0.43],
    [0.385, 0.40, 0.405],
], np.float32)
base_t = np.clip(0.48 + 0.18 * road_broad + 0.08 * road_mid + 0.04 * earth_grain, 0, 1)[:, :, None]
earth_rgb = np.array([0.585, 0.34, 0.17], np.float32)[None, None, :] * (0.90 + 0.18 * base_t)
earth_rgb = palette_noise(earth_rgb, earth_grain, 0.034, (1.0, 0.78, 0.52))
earth_colors = [
    np.array([0.675, 0.395, 0.18], np.float32),
    np.array([0.455, 0.235, 0.12], np.float32),
    np.array([0.69, 0.425, 0.225], np.float32),
    np.array([0.52, 0.275, 0.135], np.float32),
]
for mask, color, opacity in (
    (earth_ochre_mask, earth_colors[0], 0.72),
    (earth_brown_mask, earth_colors[1], 0.68),
    (earth_tan_mask, earth_colors[2], 0.55),
    (earth_large_mask, earth_colors[3], 0.40),
):
    alpha = (mask * opacity)[:, :, None]
    earth_rgb = earth_rgb * (1.0 - alpha) + color[None, None, :] * alpha
grey_stone = np.array([0.36, 0.375, 0.385], np.float32)
grey_light = np.array([0.48, 0.485, 0.475], np.float32)
stone_t = np.clip(0.48 + 0.20 * stone_grain + 0.07 * road_fine, 0, 1)[:, :, None]
stone_rgb = grey_stone[None, None, :] * (1.0 - stone_t) + grey_light[None, None, :] * stone_t
stone_rgb = palette_noise(stone_rgb, road_mid, 0.014, (0.78, 0.82, 0.84))

road_rgb = earth_rgb * (1.0 - stone_mask[:, :, None]) + stone_rgb * stone_mask[:, :, None]
road_rgb = palette_noise(road_rgb, road_broad, 0.032, (0.84, 0.68, 0.46))
road_rgb = palette_noise(road_rgb, road_fine, 0.020, (0.9, 0.74, 0.54))
chip_color = np.array([0.37, 0.245, 0.15], np.float32)
road_rgb = road_rgb * (1.0 - chip_mask[:, :, None] * 0.24) + chip_color[None, None, :] * chip_mask[:, :, None] * 0.24
dark_earth_rgb = np.clip(deep_earth[None, None, :] +
                         earth_grain[:, :, None] * np.array([0.045, 0.031, 0.019], np.float32), 0, 1)

# Height is a normalized detail source for matching normals and future sculpt masks.
# Positive stone plateaus surround fine chip depressions; earth texture remains lower.
stone_height = np.clip(0.52 + 0.105 * stone_grain + 0.024 * road_fine - 0.045 * chip_mask, 0, 1)
earth_height = np.clip(0.48 + 0.090 * earth_grain + 0.020 * road_fine, 0, 1)
road_height = np.clip(0.50 + 0.025 * road_broad + 0.008 * road_fine
                      + 0.026 * earth_ochre_mask - 0.020 * earth_brown_mask
                      + 0.021 * earth_tan_mask + 0.030 * earth_large_mask
                      + 0.062 * stone_mask - 0.022 * chip_mask, 0, 1)
dark_earth_height = np.clip(0.48 + 0.085 * earth_grain + 0.018 * road_fine, 0, 1)

maps = {}
road_normal = normal_from_height(road_height, 2.2)
stone_normal = normal_from_height(stone_height, 2.0)
earth_normal = normal_from_height(dark_earth_height, 1.8)
maps["T_Road_ReliefV2"] = write_png("T_Road_ReliefV2", road_rgb, "sRGB")
maps["N_Road_ReliefV2"] = write_png("N_Road_ReliefV2", road_normal, "Non-Color")
maps["T_Stone_Grey_ReliefV2"] = write_png("T_Stone_Grey_ReliefV2", stone_rgb, "sRGB")
maps["N_Stone_Grey_ReliefV2"] = write_png("N_Stone_Grey_ReliefV2", stone_normal, "Non-Color")
maps["T_Earth_Dark_ReliefV2"] = write_png("T_Earth_Dark_ReliefV2", dark_earth_rgb, "sRGB")
maps["N_Earth_Dark_ReliefV2"] = write_png("N_Earth_Dark_ReliefV2", earth_normal, "Non-Color")
maps["M_Road_StonePatches"] = write_png("M_Road_StonePatches", np.repeat(stone_mask[:, :, None], 3, axis=2), "Non-Color")
maps["M_Road_EarthRecess"] = write_png("M_Road_EarthRecess", np.repeat(earth_mask[:, :, None], 3, axis=2), "Non-Color")
maps["M_Road_EarthPatches"] = write_png("M_Road_EarthPatches", np.repeat(earth_patch_mask[:, :, None], 3, axis=2), "Non-Color")
maps["M_Road_FineChips"] = write_png("M_Road_FineChips", np.repeat(chip_mask[:, :, None], 3, axis=2), "Non-Color")


def make_material(name, albedo, normal):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    color_node = nodes.new("ShaderNodeTexImage")
    color_node.image = albedo
    color_node.label = "Unlit source albedo; no directional shading baked"
    mat.node_tree.links.new(color_node.outputs["Color"], bsdf.inputs["Base Color"])
    normal_node = nodes.new("ShaderNodeTexImage")
    normal_node.image = normal
    normal_node.image.colorspace_settings.name = "Non-Color"
    normal_map = nodes.new("ShaderNodeNormalMap")
    mat.node_tree.links.new(normal_node.outputs["Color"], normal_map.inputs["Color"])
    mat.node_tree.links.new(normal_map.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.92
    return mat


materials = [
    make_material("M_Road_ReliefV2", maps["T_Road_ReliefV2"], maps["N_Road_ReliefV2"]),
    make_material("M_Stone_Grey_ReliefV2", maps["T_Stone_Grey_ReliefV2"], maps["N_Stone_Grey_ReliefV2"]),
    make_material("M_Earth_Dark_ReliefV2", maps["T_Earth_Dark_ReliefV2"], maps["N_Earth_Dark_ReliefV2"]),
]

# Author a dedicated scene and collection without clearing or saving the user's
# current interactive Blender scene. The .blend is written as a selected datablock
# library below, so unrelated open objects are never included in the source file.
source_scene = bpy.data.scenes.new("TerrainRoadReliefV2")
stage = bpy.data.collections.new("TerrainRoadReliefV2_MaterialSwatches")
source_scene.collection.children.link(stage)
for idx, material in enumerate(materials):
    mesh = bpy.data.meshes.new(material.name + "_TileMesh")
    mesh.from_pydata([(-2, -2, 0), (2, -2, 0), (2, 2, 0), (-2, 2, 0)], [], [(0, 1, 2, 3)])
    mesh.uv_layers.new(name="UVMap")
    uv = mesh.uv_layers.active.data
    for poly in mesh.polygons:
        for loop_index, uv_coord in zip(poly.loop_indices, ((0, 0), (1, 0), (1, 1), (0, 1))):
            uv[loop_index].uv = uv_coord
    mesh.materials.append(material)
    ob = bpy.data.objects.new("PreviewTile_" + material.name, mesh)
    ob.location.x = idx * 4.5
    stage.objects.link(ob)

blend_datablocks = {source_scene, stage, *materials, *maps.values()}
bpy.data.libraries.write(BLEND_PATH, blend_datablocks, path_remap="RELATIVE_ALL", fake_user=True, compress=True)


def wrap_metrics(array):
    wrapped = np.concatenate((np.abs(array[:, 0] - array[:, -1]).reshape(-1),
                              np.abs(array[0, :] - array[-1, :]).reshape(-1)))
    adjacent = np.concatenate((np.abs(array[:, 1:] - array[:, :-1]).reshape(-1),
                               np.abs(array[1:, :] - array[:-1, :]).reshape(-1)))
    seam_p999 = float(np.percentile(wrapped, 99.9))
    interior_p999 = float(np.percentile(adjacent, 99.9))
    return {
        "seam_adjacent_p999_delta": seam_p999,
        "interior_adjacent_p999_delta": interior_p999,
        "seam_to_interior_p999_ratio": float(seam_p999 / max(interior_p999, 1e-8)),
        "max_seam_delta": float(wrapped.max()),
        "max_interior_delta": float(adjacent.max()),
        "max_seam_to_interior_ratio": float(wrapped.max() / max(float(adjacent.max()), 1e-8)),
    }


checks = {}
for name, arr in (("road_albedo", road_rgb), ("stone_albedo", stone_rgb),
                  ("earth_albedo", dark_earth_rgb), ("stone_mask", stone_mask),
                  ("earth_mask", earth_mask), ("earth_patch_mask", earth_patch_mask),
                  ("fine_chip_mask", chip_mask),
                  ("road_height", road_height), ("road_normal", road_normal),
                  ("stone_normal", stone_normal), ("earth_normal", earth_normal)):
    checks[name] = wrap_metrics(arr)

synthetic_ramp = np.linspace(0.0, 1.0, SIZE, dtype=np.float32)
synthetic_ramp_dy = (synthetic_ramp[SIZE // 2 + 1] - synthetic_ramp[SIZE // 2 - 1]) * 0.5 * 2.0
synthetic_normal_y_ramp_green = float(0.5 - synthetic_ramp_dy * 0.5)
report = {
    "blender_version": bpy.app.version_string,
    "tile_meters": TILE_METERS,
    "resolution": [SIZE, SIZE],
    "seed": SEED,
    "texture_files": {name: os.path.getsize(os.path.join(ASSET_DIR, name + ".png")) for name in maps},
    "seam_checks": checks,
    "surface_patch_design": {
        "construction": "Overlapping, periodic jagged masks on a softly mottled warm earth base; no shared cell grid or continuous dark seam network.",
        "earth_patch_sets": {"ochre": len(earth_ochre_patches), "brown": len(earth_brown_patches),
                             "tan": len(earth_tan_patches), "larger": len(earth_large_patches)},
        "stone_patch_count": len(stone_patches) + len(stone_small_patches),
        "stone_size_sets": {"medium": len(stone_patches), "small": len(stone_small_patches),
                            "larger": len(stone_large_patches)},
        "medium_patch_target_diameter_m": [0.10, 0.25],
        "larger_patch_diameter_m": [0.30, 0.60],
        "fine_chip_count": len(chips), "fine_chip_diameter_m": [0.010, 0.038],
        "earth_patch_coverage_fraction": float(earth_patch_mask.mean()),
        "stone_coverage_fraction": float(stone_mask.mean()),
        "earth_recess_mask_fraction": float(earth_mask.mean()),
    },
    "matching_features": "Road pigment and height share the same overlapping earth/stone/chip masks. Normal maps are computed from corresponding periodic height fields, so relief follows the same patch contours.",
    "albedo_lighting": "Base color maps contain pigment and material variation only; no directional lighting or cast shadows are baked.",
    "normal_convention": "Tangent-space RGB, Blender row 0 is bottom; a synthetic +UV.Y ramp encodes green below 0.5, consistent with Unity flipGreenChannel=false.",
    "synthetic_normal_y_ramp_green": synthetic_normal_y_ramp_green,
    "blend_path": BLEND_PATH,
    "asset_directory": ASSET_DIR,
    "validation_limits": ["Unity import metadata and visual appearance were not verified by this Blender generation script."],
}
assert report["synthetic_normal_y_ramp_green"] < 0.5, "Positive UV.Y height ramp must produce a green normal component below 0.5."
report_path = os.path.join(REPORT_DIR, "terrain_road_relief_v2_validation.json")
with open(report_path, "w", encoding="utf-8") as stream:
    json.dump(report, stream, indent=2)

result = {
    "status": "ok",
    "blender_version": bpy.app.version_string,
    "blend_path": BLEND_PATH,
    "textures": list(maps),
    "report_path": report_path,
    "stone_coverage_fraction": report["surface_patch_design"]["stone_coverage_fraction"],
}
