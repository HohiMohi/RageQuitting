"""Generate seamless warm dirt path grain textures and an editable Blender source.

Run with Blender 5.1.2:
  blender.exe --background --python generate_grain_road.py
Only the four named textures are written into Assets/Art/Terrain/Prototype.
"""
import bpy
import json
import math
import os
import zlib
import struct
import hashlib
import numpy as np

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = r"D:\Programy\UnityProjects\RageQuitting\Assets\Art\Terrain\Prototype"
SIZE = 1024
SEED = 20260923
SPARSE_COUNT = 520
DENSE_COUNT = 1040

os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(SEED)
yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)

def wrap_delta(value, center):
    return (value - center + SIZE * 0.5) % SIZE - SIZE * 0.5

def make_grains():
    """A deterministic uniform sequence; Dense is Sparse plus later chips."""
    result = []
    for _ in range(DENSE_COUNT):
        cx, cy = rng.uniform(0, SIZE, 2)
        pebble = rng.random() < .14
        broad_patch = (not pebble) and rng.random() < .10
        if broad_patch:
            rx, ry = rng.uniform(12, 23), rng.uniform(8, 17)
            nvert = int(rng.integers(7, 12))
        elif pebble:
            radius = rng.uniform(3.5, 7.0)
            rx, ry = radius * rng.uniform(.85, 1.15), radius * rng.uniform(.78, 1.18)
            nvert = 0
        else:
            rx, ry = rng.uniform(5.0, 12.0), rng.uniform(3.8, 10.0)
            nvert = int(rng.integers(5, 10))
        angle = float(rng.uniform(0, math.tau))
        if nvert:
            step = math.tau / nvert
            angles = np.arange(nvert, dtype=np.float32) * step + rng.uniform(-.19, .19, nvert) * step
            # Independent radii and vertex angles make actual multi-corner outlines,
            # not a stretched ellipse with a noisy edge.
            radii = rng.uniform(.66, 1.20, nvert)
            vertices = np.stack((np.cos(angles)*radii, np.sin(angles)*radii), axis=1)
            ca, sa = math.cos(angle), math.sin(angle)
            vertices = vertices @ np.array([[ca, -sa], [sa, ca]], dtype=np.float32).T
        else:
            vertices = None
        pigment = int(rng.choice([0, 1, 2, 3], p=[.40, .20, .29, .11]))
        height = float(rng.uniform(.042, .082))
        result.append((cx, cy, rx, ry, vertices, pebble, pigment, height))
    return result

GRAINS = make_grains()

def periodic_backdrop():
    """Periodic, non-directional pigment mottling at three spatial scales."""
    def value_noise(period, seed):
        local_rng = np.random.default_rng(seed)
        lattice = local_rng.uniform(-1, 1, (period, period)).astype(np.float32)
        px, py = xx * period / SIZE, yy * period / SIZE
        ix, iy = np.floor(px).astype(np.int32), np.floor(py).astype(np.int32)
        fx, fy = px-ix, py-iy
        fx, fy = fx*fx*(3-2*fx), fy*fy*(3-2*fy)
        a, b = lattice[iy % period, ix % period], lattice[iy % period, (ix+1) % period]
        c, d = lattice[(iy+1) % period, ix % period], lattice[(iy+1) % period, (ix+1) % period]
        return (a*(1-fx)+b*fx)*(1-fy)+(c*(1-fx)+d*fx)*fy
    field = .63*value_noise(4, SEED+10) + .29*value_noise(9, SEED+11) + .13*value_noise(19, SEED+12)
    return field / (np.max(np.abs(field)) + 1e-6)

BACKDROP = periodic_backdrop()
BASE = np.array([.55, .29, .10], dtype=np.float32)
PIGMENTS = [
    np.array([.74, .45, .17], dtype=np.float32),   # warm ochre
    np.array([.34, .16, .07], dtype=np.float32),    # umber
    np.array([.63, .35, .12], dtype=np.float32),    # middle earth
    np.array([.82, .55, .23], dtype=np.float32),    # pale mineral chip
]

def rasterize(count):
    # The same softly mottled, warm pigment bed is shared by both densities.
    color = np.clip(BASE[None, None, :] + BACKDROP[..., None] * np.array([.12, .09, .04], dtype=np.float32), .02, .92)
    height = np.full((SIZE, SIZE), .5, dtype=np.float32)
    for cx, cy, rx, ry, vertices, pebble, pigment, bump in GRAINS[:count]:
        extent = int(max(rx, ry) * 1.45) + 2
        ix = (np.arange(math.floor(cx)-extent, math.floor(cx)+extent+1) % SIZE).astype(np.int32)
        iy = (np.arange(math.floor(cy)-extent, math.floor(cy)+extent+1) % SIZE).astype(np.int32)
        gx, gy = np.meshgrid(ix.astype(np.float32), iy.astype(np.float32))
        dx, dy = wrap_delta(gx, cx), wrap_delta(gy, cy)
        if pebble:
            d2 = (dx/rx)**2 + (dy/ry)**2
            mask = d2 <= 1.0
            falloff = np.clip(1.0-d2, 0, 1)
        else:
            px, py = dx/rx, dy/ry
            inside = np.zeros(px.shape, dtype=bool)
            min_edge_distance2 = np.full(px.shape, np.inf, dtype=np.float32)
            for k in range(len(vertices)):
                ax, ay = vertices[k]
                bx, by = vertices[(k+1) % len(vertices)]
                crossing = ((ay > py) != (by > py)) & (px < (bx-ax)*(py-ay)/(by-ay+1e-8)+ax)
                inside ^= crossing
                vx, vy = bx-ax, by-ay
                t = np.clip(((px-ax)*vx+(py-ay)*vy)/(vx*vx+vy*vy+1e-8), 0, 1)
                qx, qy = ax+t*vx, ay+t*vy
                min_edge_distance2 = np.minimum(min_edge_distance2, (px-qx)**2+(py-qy)**2)
            mask = inside
            d2 = (px*px + py*py) / 1.65
            falloff = np.clip(.3 + .7*np.sqrt(min_edge_distance2/(min_edge_distance2.max()+1e-6)), 0, 1)
        if not np.any(mask):
            continue
        grain_color = PIGMENTS[pigment]
        # Flat pigment variation avoids a painted light direction across each chip.
        local_col = np.broadcast_to(grain_color, (*mask.shape, 3))
        sub = color[np.ix_(iy, ix)]
        sub[mask] = local_col[mask]
        color[np.ix_(iy, ix)] = sub
        hsub = height[np.ix_(iy, ix)]
        hsub = np.maximum(hsub, .5 + bump * falloff * np.clip(1-d2, 0, 1))
        height[np.ix_(iy, ix)] = hsub
    return np.clip(color, 0, 1), height

def normal_from_height(height):
    # Toroidal central differences: no edge clamping, no baked directional shadows.
    dx = np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)
    dy = np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)
    nx, ny = -dx * 7.0, -dy * 7.0
    nz = np.ones_like(nx)
    mag = np.sqrt(nx*nx + ny*ny + nz*nz)
    return np.stack((nx/mag*.5+.5, ny/mag*.5+.5, nz/mag*.5+.5), axis=-1)

def save_image(name, pixels, colorspace):
    image = bpy.data.images.new(name, width=SIZE, height=SIZE, alpha=False, float_buffer=False)
    image.colorspace_settings.name = colorspace
    rgba = np.concatenate((pixels.astype(np.float32), np.ones((SIZE, SIZE, 1), dtype=np.float32)), axis=-1)
    image.pixels.foreach_set(rgba.reshape(-1))
    image.filepath_raw = os.path.join(OUT, name + '.png')
    image.file_format = 'PNG'
    image.save()
    return image

def create_preview(name, image, x):
    mat = bpy.data.materials.new(name + '_Preview')
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    output = nodes.new('ShaderNodeOutputMaterial')
    shader = nodes.new('ShaderNodeBsdfPrincipled')
    shader.inputs['Roughness'].default_value = .92
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = image
    tex.extension = 'REPEAT'
    tex.label = 'Seamless warm grain road'
    links.new(tex.outputs['Color'], shader.inputs['Base Color'])
    links.new(shader.outputs['BSDF'], output.inputs['Surface'])
    bpy.ops.mesh.primitive_plane_add(size=2, location=(x, 0, 0))
    obj = bpy.context.object
    obj.name = 'Preview_' + name
    obj.data.materials.append(mat)

def png_chunk(tag, data):
    return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag+data) & 0xffffffff)

def write_montage(sparse, dense):
    # Repeated full tiles make every wrap seam directly visible in each 3x3 view.
    sparse_rgb = np.clip(np.round(sparse*255), 0, 255).astype(np.uint8)
    dense_rgb = np.clip(np.round(dense*255), 0, 255).astype(np.uint8)
    def write(name, rgb):
        raw = b''.join(b'\x00' + rgb[row].tobytes() for row in range(rgb.shape[0]))
        payload = b'\x89PNG\r\n\x1a\n' + png_chunk(b'IHDR', struct.pack('>IIBBBBB', rgb.shape[1], rgb.shape[0], 8, 2, 0, 0, 0))
        payload += png_chunk(b'IDAT', zlib.compress(raw, 6)) + png_chunk(b'IEND', b'')
        with open(os.path.join(ROOT, name), 'wb') as f:
            f.write(payload)
    write('grain_road_sparse_3x3_montage.png', np.tile(sparse_rgb, (3, 3, 1)))
    write('grain_road_dense_3x3_montage.png', np.tile(dense_rgb, (3, 3, 1)))
    combined = np.concatenate((np.tile(sparse_rgb, (1, 3, 1)), np.tile(dense_rgb, (1, 3, 1))), axis=0)
    write('grain_road_3x3_montage.png', combined)

# Keep the source scene self-contained and scoped to this new art prototype.
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.context.scene.render.engine = 'CYCLES'
made = {}
for label, count in (('Sparse', SPARSE_COUNT), ('Dense', DENSE_COUNT)):
    rgb, height = rasterize(count)
    normal = normal_from_height(height)
    color_img = save_image('Terrain_DirtPath_Warm_Grain' + label, rgb, 'sRGB')
    normal_img = save_image('Terrain_DirtPath_Warm_Grain' + label + '_Normal', normal, 'Non-Color')
    create_preview('Terrain_DirtPath_Warm_Grain' + label, color_img, 0 if label == 'Sparse' else 2.5)
    made[label] = (rgb, height, normal)
    for image in (color_img, normal_img):
        image.pack()
write_montage(made['Sparse'][0], made['Dense'][0])
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, 'GrainRoad.blend'))

# Store objective wrap metrics, generated-file hashes, and protected-source hashes.
def edge_metrics(image):
    # first/last pixel discontinuity is compared with interior one-pixel steps.
    dx = np.abs(image[:, 0].astype(np.float64) - image[:, -1].astype(np.float64))
    dy = np.abs(image[0].astype(np.float64) - image[-1].astype(np.float64))
    inside_x = np.abs(image[:, 1:].astype(np.float64) - image[:, :-1].astype(np.float64))
    inside_y = np.abs(image[1:].astype(np.float64) - image[:-1].astype(np.float64))
    return {'edge_mean_abs': float(max(dx.mean(), dy.mean())),
            'interior_step_mean_abs': float(max(inside_x.mean(), inside_y.mean())),
            'edge_to_interior_ratio': float(max(dx.mean(), dy.mean())/(max(inside_x.mean(), inside_y.mean())+1e-9))}

protected_before = {
    'Terrain_DirtPath_Warm.png': '85EEFDEFBE412ED0C12815D2FF4D5CA948BC5CD2CD7185563FB798928CDA4380',
    'Terrain_Grass_Warm.png': '7F3FDB93AA72C219DA0F6C7ACBB869917FBC9FC60B6178D01CDAE70B2A9FF5B2',
}
report = {'seed': SEED, 'size': [SIZE, SIZE], 'grain_counts': {'sparse': SPARSE_COUNT, 'dense': DENSE_COUNT},
          'dense_includes_sparse_grain_prefix': True,
          'variants_share_palette_backdrop_and_grain_distribution': True,
          'shared_bare_soil_base_rgb': BASE.tolist(),
          'periodic_backdrop_amplitude_rgb': [.12, .09, .04],
          'grain_pigments_rgb': [c.tolist() for c in PIGMENTS],
          'png_dimensions': {'color_sparse': [SIZE,SIZE], 'color_dense': [SIZE,SIZE],
                             'normal_sparse': [SIZE,SIZE], 'normal_dense': [SIZE,SIZE],
                             'sparse_montage': [SIZE*3,SIZE*3], 'dense_montage': [SIZE*3,SIZE*3],
                             'combined_montage': [SIZE*3,SIZE*2]},
          'normal_bump_range': [.042, .082], 'blender_version': bpy.app.version_string,
          'textures': {k: {'color': edge_metrics(v[0]), 'normal': edge_metrics(v[2])} for k,v in made.items()},
          'files': {name: hashlib.sha256(open(os.path.join(OUT,name+'.png'),'rb').read()).hexdigest()
                    for name in ('Terrain_DirtPath_Warm_GrainSparse','Terrain_DirtPath_Warm_GrainDense',
                                 'Terrain_DirtPath_Warm_GrainSparse_Normal','Terrain_DirtPath_Warm_GrainDense_Normal')},
          'protected_asset_hashes_sha256_before_revision': protected_before,
          'protected_asset_hashes_sha256_after_revision': {
              name: hashlib.sha256(open(os.path.join(OUT,name),'rb').read()).hexdigest()
              for name in protected_before}}
with open(os.path.join(ROOT, 'validation_report.json'), 'w', encoding='utf-8') as f:
    json.dump(report, f, indent=2)
