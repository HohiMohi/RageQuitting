"""Generate seamless hand-tuned terrain prototype textures and a Blender source file.

Run with Blender 5.1.2:
  blender.exe --background --python generate_terrain_textures.py
All color layouts are shared between palettes; normals come from independent brush relief.
"""
import bpy
import math
import os
import numpy as np

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = r"D:\Programy\UnityProjects\RageQuitting\Assets\Art\Terrain\Prototype"
SIZE = 1024
os.makedirs(OUT, exist_ok=True)

def periodic_noise(x, y, seed=0, count=7, freq=1.0):
    rng = np.random.default_rng(seed)
    z = np.zeros_like(x, dtype=np.float32)
    for i in range(count):
        kx = int(rng.integers(1, 8)) * freq
        ky = int(rng.integers(1, 8)) * freq
        phase = rng.random() * math.tau
        amp = 1.0 / (1.0 + i * 0.85)
        z += amp * np.sin(math.tau * (kx*x + ky*y) + phase)
    return z / (np.max(np.abs(z)) + 1e-6)

def stroke_field(x, y, seed, number, length, width, direction, height_scale=1.0):
    """Soft, slightly irregular periodic brush marks; summation yields gentle relief."""
    rng = np.random.default_rng(seed)
    field = np.zeros_like(x, dtype=np.float32)
    for i in range(number):
        cx, cy = rng.random(2)
        ang = direction + rng.uniform(-0.4, 0.4)
        ca, sa = math.cos(ang), math.sin(ang)
        dx = (x-cx+0.5) % 1.0 - 0.5
        dy = (y-cy+0.5) % 1.0 - 0.5
        u = dx*ca + dy*sa
        v = -dx*sa + dy*ca
        ln = length * rng.uniform(0.45, 1.35)
        wd = width * rng.uniform(0.55, 1.5)
        taper = np.clip(1.0 - np.abs(u)/(ln*0.5), 0, 1)
        wobble = 0.12*np.sin((u/ln)*math.tau*2 + rng.random()*math.tau)
        core = np.exp(-((v/wd - wobble)**2)*2.2) * taper
        field += core.astype(np.float32) * rng.uniform(0.5, 1.0)
    field /= (field.max() + 1e-6)
    return field * height_scale

def authored_brush_layers(x, y, marks):
    """Hand-placed tapered brush strokes, wrapping across tile borders."""
    light = np.zeros_like(x, dtype=np.float32)
    shade = np.zeros_like(x, dtype=np.float32)
    for cx, cy, angle, length, width, pigment in marks:
        ca, sa = math.cos(angle), math.sin(angle)
        dx = (x-cx+0.5) % 1.0 - 0.5
        dy = (y-cy+0.5) % 1.0 - 0.5
        u, v = dx*ca + dy*sa, -dx*sa + dy*ca
        taper = np.clip(1.0 - np.abs(u)/(length*.5), 0, 1)
        wobble = .12 * np.sin(u/length*math.tau*1.6 + cx*31 + cy*19)
        cross = np.clip(1.0 - np.abs(v/width-wobble), 0, 1)
        mark = (taper * cross * min(abs(pigment), 1.0)).astype(np.float32)
        if pigment >= 0:
            light = np.maximum(light, mark)
        else:
            shade = np.maximum(shade, mark)
    return light, shade

def make_surface(seed, layout_direction, brush_marks, stroke_count):
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)
    x, y = xx/SIZE, yy/SIZE
    broad = periodic_noise(x, y, seed, count=5, freq=1.0)
    fine = periodic_noise(x, y, seed+31, count=9, freq=2.0)
    light_brush, shade_brush = authored_brush_layers(x, y, brush_marks)
    fine_strokes = stroke_field(x, y, seed+93, stroke_count, 0.075, 0.0045, layout_direction+0.15, 1.0)
    height_strokes = stroke_field(x, y, seed+701, stroke_count//2, 0.13, 0.010, layout_direction+0.22, 1.0)
    height_fine = stroke_field(x, y, seed+907, stroke_count//5, 0.055, 0.005, layout_direction-0.19, 1.0)
    # The normal relief is independently authored and is never sampled from pigment/color.
    height = (0.5 + 0.10*height_strokes + 0.035*height_fine).astype(np.float32)
    # Centered wrap-aware gradient. Amplitude is intentionally subtle for normal usage.
    dy, dx = np.roll(height, -1, 0)-np.roll(height, 1, 0), np.roll(height, -1, 1)-np.roll(height, 1, 1)
    nx, ny = -dx*10.0, -dy*10.0
    nz = np.ones_like(nx)
    norm = np.sqrt(nx*nx + ny*ny + nz*nz)
    normal = np.stack((nx/norm*.5+.5, ny/norm*.5+.5, nz/norm*.5+.5), axis=-1)
    return x, y, broad, fine, light_brush, shade_brush, fine_strokes, normal

def save_png(name, pixels, colorspace):
    im = bpy.data.images.new(name, width=SIZE, height=SIZE, alpha=False, float_buffer=False)
    im.colorspace_settings.name = colorspace
    im.pixels.foreach_set(np.asarray(pixels, dtype=np.float32).reshape(-1))
    im.filepath_raw = os.path.join(OUT, name + ".png")
    im.file_format = 'PNG'
    im.save()
    return im

def create_material(name, image):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (0.5, 0.5, 0.5, 1)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    out = nodes.new('ShaderNodeOutputMaterial')
    shader = nodes.new('ShaderNodeBsdfPrincipled')
    shader.inputs['Roughness'].default_value = .92
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = image
    tex.interpolation = 'Linear'
    tex.extension = 'REPEAT'
    tex.label = 'Seamless authored diffuse'
    links.new(tex.outputs['Color'], shader.inputs['Base Color'])
    links.new(shader.outputs['BSDF'], out.inputs['Surface'])
    return mat

# Build identical coordinate/layout sets for both palette variants per surface.
definitions = {
    'Grass': dict(seed=17, direction=.34,
                  brush_marks=[
                      (.08,.08,.35,.24,.036,.72),(.34,.17,.55,.17,.027,-.50),(.78,.12,.18,.28,.041,.62),(.58,.29,.48,.21,.030,-.54),
                      (.94,.25,.23,.20,.032,.54),(.21,.41,.40,.27,.038,.62),(.48,.43,.10,.19,.026,-.46),(.73,.50,.52,.23,.034,.68),
                      (.05,.63,.20,.18,.028,-.46),(.38,.61,.48,.29,.040,.65),(.64,.72,.27,.22,.030,.55),(.90,.68,.58,.26,.037,-.52),
                      (.22,.83,.32,.24,.034,.56),(.52,.90,.12,.20,.028,-.43),(.84,.91,.43,.28,.042,.66),(.13,.96,.58,.19,.030,.50),
                      (.43,.04,.25,.22,.033,.58),(.69,.36,.37,.20,.027,-.43),(.31,.76,.54,.18,.030,.62),(.98,.47,.15,.24,.036,-.48)], strokes=54),
    'DirtPath': dict(seed=49, direction=.12,
                  brush_marks=[
                      (.07,.11,.09,.29,.043,.58),(.35,.07,-.06,.20,.033,-.45),(.71,.14,.13,.31,.046,.62),(.92,.28,.01,.20,.031,-.48),
                      (.18,.32,.17,.24,.039,.50),(.51,.27,.04,.31,.047,-.50),(.82,.43,.12,.23,.036,.56),(.04,.54,.21,.27,.042,.48),
                      (.29,.49,.06,.30,.046,-.46),(.60,.57,.19,.22,.033,.60),(.91,.62,.02,.29,.044,.56),(.13,.75,-.02,.23,.037,-.44),
                      (.43,.72,.14,.32,.048,.62),(.72,.82,.05,.25,.040,-.48),(.98,.88,.18,.24,.034,.52),(.25,.94,.10,.30,.046,.56),
                      (.56,.89,.00,.23,.037,-.46),(.83,.09,.12,.29,.043,.60)], strokes=45),
}
palette = {
    'Warm': {
        'Grass': ((.24,.39,.10),[(.48,.61,.18),(.13,.23,.08)]),
        'DirtPath': ((.55,.29,.10),[(.82,.55,.23),(.34,.16,.07)]),
    },
    'Cool': {
        'Grass': ((.19,.33,.25),[(.36,.48,.30),(.12,.24,.23)]),
        'DirtPath': ((.43,.30,.27),[(.62,.42,.35),(.29,.21,.28)]),
    },
}

# Scene contains a neutral preview plane for every material and a camera-free source setup.
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for sidx, (surface, cfg) in enumerate(definitions.items()):
    x, y, broad, fine, light_brush, shade_brush, fine_strokes, normal = make_surface(cfg['seed'], cfg['direction'], cfg['brush_marks'], cfg['strokes'])
    for pname in ('Warm', 'Cool'):
        base, accents = palette[pname][surface]
        # Broad low-contrast pigment sweeps retain surface color identity.
        color = np.broadcast_to(np.asarray(base, dtype=np.float32), (*x.shape, 3)).copy()
        sweep_alpha = np.clip(.20 + .22*broad + .10*fine, .02, .48)
        color = color*(1-sweep_alpha[...,None]) + np.asarray(accents[0],dtype=np.float32)*sweep_alpha[...,None]
        # Deliberately varied hand-placed long strokes define painted grass and compacted earth.
        color = color*(1-light_brush[...,None]*.76) + np.asarray(accents[0],dtype=np.float32)*light_brush[...,None]*.76
        color = color*(1-shade_brush[...,None]*.66) + np.asarray(accents[1],dtype=np.float32)*shade_brush[...,None]*.66
        # Short, quiet bristle ticks break up the larger strokes without dot-like blobs.
        tick = np.clip((fine_strokes-.10)*.42, 0, .13)
        color = color*(1-tick[...,None]) + np.asarray(accents[0],dtype=np.float32)*tick[...,None]
        color = np.clip(color, 0, 1)
        stem = f"Terrain_{surface}_{pname}"
        diffuse = save_png(stem, np.concatenate((color, np.ones((*x.shape,1),dtype=np.float32)), axis=-1), 'sRGB')
        mat = create_material(f"{stem}_Preview", diffuse)
        bpy.ops.mesh.primitive_plane_add(size=2, location=((sidx*5)+(0 if pname=='Warm' else 2.5), 0, 0))
        obj = bpy.context.object
        obj.name = f"Preview_{stem}"
        obj.data.materials.append(mat)
        if pname == 'Warm':
            save_png(f"Terrain_{surface}_Normal", np.concatenate((normal, np.ones((*x.shape,1),dtype=np.float32)), axis=-1), 'Non-Color')

# Save a compact, editable Blender source file with generated textures packed.
for img in bpy.data.images:
    if img.source == 'FILE':
        img.pack()
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, 'TerrainPrototype.blend'))
