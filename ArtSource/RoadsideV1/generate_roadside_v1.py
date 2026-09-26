"""Deterministically generate Roadside V1 source meshes and tileable textures.

Run with Blender 5.1.2:
  blender.exe --factory-startup --background --python-exit-code 1 --python generate_roadside_v1.py

All geometry and texture pixels are authored here. Grass is built from tapered,
curved ribbon blades (not crossed cards); rock geometry is faceted mesh geometry
with a shared procedural tangent detail normal; terrain normals come from the
same periodic physical height relief used by the ground reference mesh.
"""
import bpy
import hashlib
import json
import math
import os
import random
import shutil
import struct
import sys
from mathutils import Vector
import numpy as np

ROOT = r"D:\Programy\UnityProjects\RageQuitting"
SOURCE = os.path.join(ROOT, "ArtSource", "RoadsideV1")
UNITY = os.path.join(ROOT, "Assets", "Art", "Environment", "TerrainRoadLookdev", "RoadsideV1")
MODELS = os.path.join(UNITY, "Models")
REPORTS = os.path.join(ROOT, "Artifacts", "RoadsideV1")
BLEND = os.path.join(SOURCE, "RoadsideV1.blend")
SEED = 20260926
TILE_M = 4.0
TEXTURE_SIZE = 2048
GRASS_ATLAS_SIZE = 1024
random.seed(SEED)
np.random.seed(SEED)
for folder in (SOURCE, UNITY, MODELS, REPORTS):
    os.makedirs(folder, exist_ok=True)

def periodic_gaussian_field(size, seed, count, low, high, sigma_range, tile=TILE_M):
    rng = np.random.default_rng(seed)
    axis = np.linspace(0, tile, size, endpoint=False, dtype=np.float32)
    xx, yy = np.meshgrid(axis, axis)
    field = np.zeros((size, size), np.float32)
    for _ in range(count):
        cx, cy = rng.random(2) * tile
        sx, sy = rng.uniform(*sigma_range, 2)
        amp = rng.uniform(low, high)
        dx = np.minimum(np.abs(xx-cx), tile-np.abs(xx-cx))
        dy = np.minimum(np.abs(yy-cy), tile-np.abs(yy-cy))
        field += amp * np.exp(-0.5*((dx/sx)**2+(dy/sy)**2)).astype(np.float32)
    field -= field.mean()
    peak = max(float(np.max(np.abs(field))), 1e-6)
    return np.clip(field / peak, -1, 1)

def periodic_noise(size, seed, cells):
    rng = np.random.default_rng(seed)
    coarse = rng.random((cells, cells), dtype=np.float32)
    coarse = np.pad(coarse, ((0, 1), (0, 1)), mode='wrap')
    # Enlarge the periodic low-frequency Fourier spectrum; the result remains periodic.
    spectrum = np.fft.rfft2(coarse[:cells, :cells])
    out = np.fft.irfft2(spectrum, s=(size, size)).real.astype(np.float32)
    out = (out - out.mean()) / max(float(out.std()), 1e-6)
    return np.clip(out, -2.5, 2.5)

def periodic_angular_patches(size, seed, count=58):
    """Low-contrast, softened superellipse patches with periodic wraparound."""
    rng=np.random.default_rng(seed)
    axis=np.linspace(0,TILE_M,size,endpoint=False,dtype=np.float32)
    xx,yy=np.meshgrid(axis,axis)
    field=np.zeros((size,size),np.float32)
    for _ in range(count):
        cx,cy=rng.random(2)*TILE_M
        rx,ry=rng.uniform(.10,.25),rng.uniform(.07,.17)
        angle=rng.uniform(-math.pi,math.pi)
        dx=np.minimum(np.abs(xx-cx),TILE_M-np.abs(xx-cx))
        dy=np.minimum(np.abs(yy-cy),TILE_M-np.abs(yy-cy))
        ca,sa=math.cos(angle),math.sin(angle)
        u=(ca*dx+sa*dy)/rx
        v=(-sa*dx+ca*dy)/ry
        q=(np.abs(u)**4+np.abs(v)**4)**.25
        t=np.clip((1.10-q)/.22,0,1)
        t=t*t*(3-2*t)
        field+=float(rng.choice([-1,1]))*t*float(rng.uniform(.35,.8))
    return np.clip(field,-1,1)

def save_rgb(name, array, colorspace='sRGB'):
    path = os.path.join(UNITY, name)
    image = bpy.data.images.new(name, width=array.shape[1], height=array.shape[0], alpha=False, float_buffer=False)
    image.colorspace_settings.name = colorspace
    rgba = np.concatenate([np.clip(array,0,1), np.ones((*array.shape[:2],1),np.float32)], axis=2)
    image.pixels.foreach_set(rgba.reshape(-1))
    image.filepath_raw = path
    image.file_format = 'PNG'
    image.save()
    return image, path

def save_gray_rgba(name, array):
    return save_rgb(name, np.repeat(array[:,:,None], 3, axis=2))

def height_to_normal(height_m, strength, tile_m, size):
    # Unity's tangent normal convention; height is periodic on every edge.
    spacing=tile_m/size
    dx=(np.roll(height_m,-1,axis=1)-np.roll(height_m,1,axis=1))/(2*spacing)
    dy=(np.roll(height_m,-1,axis=0)-np.roll(height_m,1,axis=0))/(2*spacing)
    nx, ny = -dx*strength, -dy*strength
    nz = np.ones_like(nx)
    inv = 1.0 / np.sqrt(nx*nx + ny*ny + nz*nz)
    return np.stack([nx*inv*.5+.5, ny*inv*.5+.5, nz*inv*.5+.5], axis=2)

def material_image(name, image, normal_image=None):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (.30,.34,.19,1)
    mat.use_nodes = True
    bs = mat.node_tree.nodes.get('Principled BSDF')
    tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = image
    tex.interpolation = 'Linear'
    mat.node_tree.links.new(tex.outputs['Color'], bs.inputs['Base Color'])
    if normal_image is not None:
        ntex=mat.node_tree.nodes.new('ShaderNodeTexImage'); ntex.image=normal_image
        ntex.image.colorspace_settings.name='Non-Color'
        nmap=mat.node_tree.nodes.new('ShaderNodeNormalMap')
        mat.node_tree.links.new(ntex.outputs['Color'],nmap.inputs['Color'])
        mat.node_tree.links.new(nmap.outputs['Normal'],bs.inputs['Normal'])
    return mat

def ensure_uv(mesh):
    if not mesh.uv_layers:
        mesh.uv_layers.new(name='UVMap')
    return mesh.uv_layers.active.data

def export_mesh(obj, filename):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    destination=os.path.join(MODELS,filename)
    bpy.ops.export_scene.fbx(filepath=destination, use_selection=True,
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=False,
        object_types={'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='OFF',
        add_leaf_bones=False, path_mode='STRIP', embed_textures=False,
        axis_forward='-Z', axis_up='Y')
    obj.select_set(False)

# Start with a clean standalone authoring scene.
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

# Seamless 4 m ground surface: clustered, quiet olive grass/ochre earth variation.
macro = periodic_gaussian_field(TEXTURE_SIZE, SEED+1, 82, -.14, .16, (.10,.27))
meso = periodic_gaussian_field(TEXTURE_SIZE, SEED+2, 320, -.045, .055, (.025,.075))
fine = periodic_noise(TEXTURE_SIZE, SEED+3, 48)
patches=periodic_angular_patches(TEXTURE_SIZE,SEED+7)
ground_height_m = (.0075*macro + .0014*meso + .00025*fine + .00045*patches).astype(np.float32)
height = np.clip(.5 + ground_height_m/.020, 0, 1).astype(np.float32)
base = np.array([.315,.355,.205], np.float32)
warm = np.array([.385,.335,.190], np.float32)
green = np.array([.245,.315,.165], np.float32)
blend = np.clip(.48 + .33*macro + .10*meso, 0, 1)[:,:,None]
ground_rgb = base + (warm-base)*np.maximum(blend-.5,0)*1.4 + (green-base)*np.maximum(.5-blend,0)*1.25
ground_rgb += (meso*.012 + fine*.002)[:,:,None]
ground_rgb += patches[:,:,None]*np.array([.011,-.003,-.010],np.float32)
ground_rgb = np.clip(ground_rgb, .08, .72)
ground_albedo, ground_albedo_path = save_rgb('RoadsideV1_Ground_Albedo.png', ground_rgb)
normal_rgb = height_to_normal(ground_height_m, 1.0, TILE_M, TEXTURE_SIZE)
ground_normal, ground_normal_path = save_rgb('RoadsideV1_Ground_Normal.png', normal_rgb, 'Non-Color')
ao = np.clip(.91 + .045*meso - .025*np.maximum(-macro,0), .70, 1.0)
mask = np.stack([np.zeros_like(ao), ao, height, np.zeros_like(ao)], axis=2)
mask_img = bpy.data.images.new('RoadsideV1_Ground_URP_MaskMap', width=TEXTURE_SIZE, height=TEXTURE_SIZE, alpha=True)
mask_img.colorspace_settings.name='Non-Color'
mask_img.pixels.foreach_set(mask.reshape(-1))
mask_path = os.path.join(UNITY,'RoadsideV1_Ground_URP_MaskMap.png')
mask_img.filepath_raw = mask_path
mask_img.file_format='PNG'
mask_img.save()

# Shared grass atlas: three distinct vertical strips, smooth color variation only.
atlas = np.empty((GRASS_ATLAS_SIZE, GRASS_ATLAS_SIZE, 3), np.float32)
rng = np.random.default_rng(SEED+4)
band_bounds=[(0,342),(342,684),(684,1024)]
atlas[:,:,:]=np.array([.27,.34,.14],np.float32)
for band,(lo,hi) in enumerate(band_bounds):
    y = np.linspace(0,1,GRASS_ATLAS_SIZE, dtype=np.float32)[:,None]
    band_width=hi-lo
    x = np.linspace(0,1,band_width, dtype=np.float32)[None,:]
    palette = [(.225,.305,.115),(.29,.365,.145),(.36,.405,.17)][band]
    stripe = .018*np.sin(x*math.tau*3 + band*1.7) + .008*np.sin(x*math.tau*13 + band)
    vertical = -.025*(1-y) + .012*np.sin(y*math.pi)
    noise = rng.normal(0,.002,(GRASS_ATLAS_SIZE,band_width)).astype(np.float32)
    rgb = np.empty((GRASS_ATLAS_SIZE,band_width,3),np.float32)
    for c in range(3): rgb[:,:,c] = palette[c] + stripe + vertical + noise
    atlas[:,lo:hi,:] = np.clip(rgb,.06,.75)
    # The strip's last two texels act as padding to keep trilinear mips in-band.
    atlas[:,hi-2:hi,:]=atlas[:,hi-3:hi-2,:]
    if band<2: atlas[:,hi:hi+2,:]=atlas[:,hi-2:hi-1,:]
grass_atlas, grass_atlas_path = save_rgb('RoadsideV1_Grass_Atlas.png', atlas)

# Rock atlas is shared. Its subtle mineral colors match the existing muted warm-grey road stone.
size=GRASS_ATLAS_SIZE
rock_macro=periodic_gaussian_field(size, SEED+5, 210, -.06, .065, (.015,.055))
rock_fine=periodic_noise(size, SEED+6, 64)
rock_height_m=(.0011*rock_macro+.00022*rock_fine).astype(np.float32)
rock_base=np.array([.345,.335,.305],np.float32)
rock_warm=np.array([.405,.355,.295],np.float32)
rock_mix=np.clip(.5+rock_macro*.45+rock_fine*.035,0,1)[:,:,None]
rock_rgb=rock_base+(rock_warm-rock_base)*rock_mix + (rock_fine*.003)[:,:,None]
rock_atlas, rock_atlas_path=save_rgb('RoadsideV1_Rock_Albedo.png',np.clip(rock_rgb,.08,.75))
rock_normal_rgb=height_to_normal(rock_height_m, 1.0, TILE_M, size)
rock_normal, rock_normal_path=save_rgb('RoadsideV1_Rock_Normal.png',rock_normal_rgb,'Non-Color')

grass_mat=material_image('M_RoadsideV1_GrassAtlas',grass_atlas)
rock_mat=material_image('M_RoadsideV1_RockAtlas',rock_atlas,rock_normal)

def add_grass_blade(verts, faces, uvs, base_xy, h, w, lean, bend, phase, atlas_u):
    # A gently twisting, asymmetric ribbon has real width and a knife-tapered tip.
    segments=6
    start=len(verts)
    band=int(atlas_u*3)
    band_lo,band_hi=band_bounds[band]
    u_left=(band_lo+6)/GRASS_ATLAS_SIZE
    u_right=(band_hi-6)/GRASS_ATLAS_SIZE
    for j in range(segments):
        t=j/segments
        center=np.array([base_xy[0]+lean*math.sin(t*math.pi*.6+phase),
                         base_xy[1]+bend*math.sin(t*math.pi), h*t])
        width=w*((1-t)**.72) * (1.0+.10*math.sin(phase+t*4))
        side_angle=phase + .18*math.sin(t*math.pi*1.5)
        side=np.array([-math.sin(side_angle),math.cos(side_angle),0.0])
        for side_sign in (-1,1):
            p=center+side*width*side_sign
            verts.append(tuple(p))
            u=u_left if side_sign<0 else u_right
            v=.025 + .95*t
            uvs.append((u,v))
    tip=np.array([base_xy[0]+lean*math.sin(math.pi*.6+phase),base_xy[1]+bend*math.sin(math.pi),h])
    verts.append(tuple(tip))
    uvs.append(((u_left+u_right)*.5,.975))
    for j in range(segments-1):
        a=start+j*2
        faces.append((a,a+1,a+3,a+2))
    a=start+(segments-1)*2
    faces.append((a,a+1,start+segments*2))

def make_grass(name, seed, height_m, atlas_band, blade_count, spread, lean_range):
    rng=random.Random(seed)
    verts=[]; faces=[]; uvcoords=[]
    band_u=(atlas_band+0.5)/3.0
    for i in range(blade_count):
        a=rng.random()*math.tau
        radius=spread*math.sqrt(rng.random())*.45
        xy=(math.cos(a)*radius,math.sin(a)*radius)
        h=height_m*rng.uniform(.63,1.0)
        w=height_m*rng.uniform(.065,.105)
        lean=rng.uniform(*lean_range)
        bend=height_m*rng.uniform(-.11,.11)
        add_grass_blade(verts,faces,uvcoords,xy,h,w,lean,bend,a+rng.uniform(-.65,.65),band_u)
    mesh=bpy.data.meshes.new(name+'_Mesh'); mesh.from_pydata(verts,[],faces); mesh.update()
    obj=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(obj)
    obj.data.materials.append(grass_mat)
    for poly in mesh.polygons: poly.use_smooth=True
    uv=ensure_uv(mesh)
    for poly in mesh.polygons:
        for li in poly.loop_indices: uv[li].uv=uvcoords[mesh.loops[li].vertex_index]
    return obj

grass_specs=[('SM_RoadsideGrass_01',1001,.15,0,20,.37,(.025,.045)),
             ('SM_RoadsideGrass_02',1002,.30,1,28,.30,(.035,.075)),
             ('SM_RoadsideGrass_03',1003,.45,2,20,.34,(.055,.125))]
grass_objs=[]
for name,seed,h,band,count,spread,lean_range in grass_specs:
    obj=make_grass(name,seed,h,band,count,spread,lean_range); grass_objs.append(obj); export_mesh(obj,name+'.fbx')

def make_rock(name, seed, kind, dimensions):
    rng=random.Random(seed)
    sides=7 if kind!='boulder' else 8
    phase=rng.uniform(-.16,.16)
    rings=[]
    if kind=='slab':
        shape=[(0.00,.68,.72,-.08),(0.16,.97,.94,0.00),(.69,.91,1.00,.02),(.88,.67,.71,.10),(1.00,.53,.55,.06)]
    elif kind=='wedge':
        shape=[(0.00,.66,.70,-.07),(.13,.92,.89,-.01),(.48,1.0,.91,.04),(.83,.61,.58,.22),(1.00,.39,.43,.17)]
    else:
        shape=[(0.00,.64,.67,-.04),(.13,.91,.88,0.00),(.58,1.0,.96,.01),(.85,.70,.72,-.04),(1.00,.43,.46,-.02)]
    for ri,(zfrac,rx,ry,shiftx) in enumerate(shape):
        ring=[]
        for i in range(sides):
            angle=math.tau*i/sides+phase+(ri%2)*.055
            jitter=rng.uniform(.94,1.06) if ri not in (0,len(shape)-1) else rng.uniform(.97,1.03)
            ring.append((shiftx*dimensions[0] + math.cos(angle)*rx*dimensions[0]*.5*jitter,
                         math.sin(angle)*ry*dimensions[1]*.5*jitter,
                         zfrac*dimensions[2]))
        rings.append(ring)
    verts=[v for ring in rings for v in ring]
    faces=[]
    # Large planar fracture panels; narrow intermediate rings form lightly worn chamfers.
    for ri in range(len(rings)-1):
        lower=ri*sides; upper=(ri+1)*sides
        for i in range(sides):
            a=lower+i; b=lower+(i+1)%sides; c=upper+(i+1)%sides; d=upper+i
            if (i+ri)%2: faces.extend([(a,b,d),(b,c,d)])
            else: faces.extend([(a,b,c),(a,c,d)])
    faces.append(tuple(range(sides-1,-1,-1)))
    cap=len(verts); verts.append((dimensions[0]*rng.uniform(-.10,.10),dimensions[1]*rng.uniform(-.10,.10),dimensions[2]*.96))
    top_start=(len(rings)-1)*sides
    for i in range(sides): faces.append((top_start+i,top_start+(i+1)%sides,cap))
    mesh=bpy.data.meshes.new(name+'_Mesh'); mesh.from_pydata(verts,[],faces); mesh.update()
    obj=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(obj)
    mesh.materials.append(rock_mat)
    for poly in mesh.polygons: poly.use_smooth=False
    bpy.context.view_layer.objects.active=obj
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=.025, area_weight=0.0)
    bpy.ops.object.mode_set(mode='OBJECT')
    return obj

rock_specs=[('SM_RoadsideRock_01',2001,'slab',(.34,.27,.09)),('SM_RoadsideRock_02',2002,'wedge',(.62,.48,.34)),('SM_RoadsideRock_03',2003,'boulder',(.96,.76,.58))]
rock_objs=[]
for name,seed,kind,dims in rock_specs:
    obj=make_rock(name,seed,kind,dims); rock_objs.append(obj); export_mesh(obj,name+'.fbx')

# Keep the editable-source FBX identical to the Unity import copy.
for spec in grass_specs+rock_specs:
    shutil.copyfile(os.path.join(MODELS,spec[0]+'.fbx'),os.path.join(SOURCE,spec[0]+'.fbx'))

# Blender tile plane authored from the same normalized relief. This is a source reference only.
verts=[]; faces=[]
grid=33
for j in range(grid):
    for i in range(grid):
        x=TILE_M*i/(grid-1); y=TILE_M*j/(grid-1)
        row=int((j%(grid-1))*TEXTURE_SIZE/(grid-1))
        col=int((i%(grid-1))*TEXTURE_SIZE/(grid-1))
        z=float(ground_height_m[row,col])
        verts.append((x,y,z))
for j in range(grid-1):
    for i in range(grid-1):
        a=j*grid+i; faces.append((a,a+1,a+1+grid,a+grid))
ground_mesh=bpy.data.meshes.new('RoadsideV1_GroundReference_Mesh'); ground_mesh.from_pydata(verts,[],faces); ground_mesh.update()
ground_obj=bpy.data.objects.new('REF_RoadsideV1_Ground_4mTile',ground_mesh); bpy.context.collection.objects.link(ground_obj)
ground_obj.data.materials.append(material_image('M_RoadsideV1_GroundReference',ground_albedo,ground_normal))
uv=ensure_uv(ground_mesh)
for poly in ground_mesh.polygons:
    for li in poly.loop_indices:
        co=ground_mesh.vertices[ground_mesh.loops[li].vertex_index].co
        uv[li].uv=(co.x/TILE_M,co.y/TILE_M)
ground_obj.hide_render=True
ground_obj.hide_viewport=True

# Put authored assets into clear collections in the editable source file.
def move_collection(obj, name):
    coll=bpy.data.collections.get(name) or bpy.data.collections.new(name)
    if coll.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(coll)
    for old in list(obj.users_collection): old.objects.unlink(obj)
    coll.objects.link(obj)
for obj in grass_objs: move_collection(obj,'Roadside V1 Grass Clumps')
for obj in rock_objs: move_collection(obj,'Roadside V1 Rocks')
move_collection(ground_obj,'Roadside V1 Ground Reference (Hidden)')

# Save .blend after source images are packed so the authored file is self-contained.
for image in (ground_albedo,ground_normal,mask_img,grass_atlas,rock_atlas,rock_normal):
    try: image.pack()
    except Exception: pass
bpy.ops.wm.save_as_mainfile(filepath=BLEND)

# Render a private review sheet with all six authored assets on the ground tile.
# Preview-only staging happens after the source .blend is saved and after FBX export.
ground_obj.hide_render=True
ground_obj.hide_viewport=True
for obj in grass_objs+rock_objs:
    obj.location.x=0.35 + (grass_objs+rock_objs).index(obj)*.86
    obj.location.y=0.0
preview_ground_mesh=bpy.data.meshes.new('PreviewGround_Mesh')
preview_ground_mesh.from_pydata([(-.25,-1.7,-.012),(5.6,-1.7,-.012),(5.6,1.7,-.012),(-.25,1.7,-.012)],[],[(0,1,2,3)])
preview_ground_mesh.update()
preview_ground=bpy.data.objects.new('PreviewGround',preview_ground_mesh); bpy.context.collection.objects.link(preview_ground)
preview_ground.data.materials.append(material_image('M_PreviewGround',ground_albedo))
preview_uv=ensure_uv(preview_ground_mesh)
for loop,uvc in zip(preview_ground_mesh.loops,[(0,0),(2.375,0),(2.375,1), (0,1)]): preview_uv[loop.index].uv=uvc
cam_data=bpy.data.cameras.new('RoadsideV1_PreviewCamera'); cam=bpy.data.objects.new('RoadsideV1_PreviewCamera',cam_data)
bpy.context.collection.objects.link(cam); cam.location=(2.55,-8.0,5.0)
target=Vector((2.55,0,.16)); cam.rotation_euler=(target-Vector(cam.location)).to_track_quat('-Z','Y').to_euler()
cam_data.type='ORTHO'; cam_data.ortho_scale=6.4; bpy.context.scene.camera=cam
key_data=bpy.data.lights.new('Preview_Key','AREA'); key=bpy.data.objects.new('Preview_Key',key_data); bpy.context.collection.objects.link(key)
key.location=(1.8,-3.5,6.0); key_data.energy=1050; key_data.shape='DISK'; key_data.size=5.0
key.rotation_euler=(Vector((3.2,0,0))-Vector(key.location)).to_track_quat('-Z','Y').to_euler()
fill_data=bpy.data.lights.new('Preview_Fill','AREA'); fill=bpy.data.objects.new('Preview_Fill',fill_data); bpy.context.collection.objects.link(fill)
fill.location=(5.0,2.0,5.0); fill_data.energy=650; fill_data.size=4.5
fill.rotation_euler=(Vector((4.0,0,0))-Vector(fill.location)).to_track_quat('-Z','Y').to_euler()
scene=bpy.context.scene
scene.render.engine='CYCLES'; scene.cycles.samples=16
scene.render.resolution_x=1600; scene.render.resolution_y=900; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.render.filepath=os.path.join(REPORTS,'RoadsideV1_AssetReview.png')
scene.world.color=(.19,.19,.19)
bpy.ops.render.render(write_still=True)

def seam_diff(arr):
    return float(max(np.max(np.abs(arr[0]-arr[-1])),np.max(np.abs(arr[:,0]-arr[:,-1]))))

def mesh_metrics(obj):
    bpy.context.view_layer.update()
    # Measure authored local dimensions; preview staging later moves them into a row.
    coords=[v.co for v in obj.data.vertices]
    mins=[float(min(v[k] for v in coords)) for k in range(3)]
    maxs=[float(max(v[k] for v in coords)) for k in range(3)]
    min_triangle_area=float('inf')
    for poly in obj.data.polygons:
        points=[obj.data.vertices[i].co for i in poly.vertices]
        for i in range(1,len(points)-1):
            area=.5*((points[i]-points[0]).cross(points[i+1]-points[0])).length
            min_triangle_area=min(min_triangle_area,float(area))
    uv_bounds=None
    if obj.data.uv_layers:
        uv_values=[(float(item.uv.x),float(item.uv.y)) for item in obj.data.uv_layers.active.data]
        uv_bounds=[[min(p[0] for p in uv_values),min(p[1] for p in uv_values)],[max(p[0] for p in uv_values),max(p[1] for p in uv_values)]]
    return {'boundsMinM':mins,'boundsMaxM':maxs,'dimensionsM':[maxs[i]-mins[i] for i in range(3)],
        'vertices':len(obj.data.vertices),'triangles':sum(max(0,len(p.vertices)-2) for p in obj.data.polygons),
        'uvLayers':len(obj.data.uv_layers),'uvBounds':uv_bounds,'minTriangleAreaM2':min_triangle_area,
        'hasMaterial':bool(obj.data.materials)}

def png_readback(path, expected_size, non_color):
    image=bpy.data.images.load(path,check_existing=False)
    image.colorspace_settings.name='Non-Color' if non_color else 'sRGB'
    width,height=image.size
    pixels=np.array(image.pixels[:],dtype=np.float32).reshape((height,width,4))
    finite=bool(np.isfinite(pixels).all())
    if (width,height)!=(expected_size,expected_size) or not finite:
        raise RuntimeError(f'PNG readback invalid: {path} size={(width,height)} finite={finite}')
    result={'path':os.path.relpath(path,ROOT).replace('\\','/'),'width':width,'height':height,
        'channels':image.channels,'colorspace':'Non-Color' if non_color else 'sRGB-decoded linear',
        'finite':finite,'decodedMin':float(pixels[:,:,:3].min()),'decodedMax':float(pixels[:,:,:3].max()),
        'channelMinMax':[[float(pixels[:,:,i].min()),float(pixels[:,:,i].max())] for i in range(4)],
        'edgeAdjacentMaxDelta':float(max(np.max(np.abs(pixels[0]-pixels[-1])),np.max(np.abs(pixels[:,0]-pixels[:,-1]))))}
    if non_color and 'Normal' in os.path.basename(path):
        tangent=pixels[:,:,:3]*2.0-1.0
        normal_length=np.linalg.norm(tangent,axis=2)
        result['normalizedTangentLengthRange']=[float(normal_length.min()),float(normal_length.max())]
        if float(normal_length.min())<.985 or float(normal_length.max())>1.015:
            raise RuntimeError(f'Unnormalized tangent normals in {path}')
    if os.path.basename(path).endswith('MaskMap.png'):
        if float(pixels[:,:,0].max())!=0.0 or float(pixels[:,:,3].max())!=0.0:
            raise RuntimeError(f'URP mask metallic/smoothness channels are nonzero: {path}')
    bpy.data.images.remove(image)
    return result

png_readbacks={
    'groundAlbedo':png_readback(ground_albedo_path,TEXTURE_SIZE,False),
    'groundNormal':png_readback(ground_normal_path,TEXTURE_SIZE,True),
    'groundMask':png_readback(mask_path,TEXTURE_SIZE,True),
    'grassAtlas':png_readback(grass_atlas_path,GRASS_ATLAS_SIZE,False),
    'rockAlbedo':png_readback(rock_atlas_path,GRASS_ATLAS_SIZE,False),
    'rockNormal':png_readback(rock_normal_path,GRASS_ATLAS_SIZE,True)}

files=[]
for path in [os.path.join(SOURCE,'generate_roadside_v1.py'),BLEND]+[os.path.join(SOURCE,s[0]+'.fbx') for s in grass_specs+rock_specs]+[os.path.join(MODELS,s[0]+'.fbx') for s in grass_specs+rock_specs]+[ground_albedo_path,ground_normal_path,mask_path,grass_atlas_path,rock_atlas_path,rock_normal_path,os.path.join(REPORTS,'RoadsideV1_AssetReview.png')]:
    files.append({'path':os.path.relpath(path,ROOT).replace('\\','/'),'bytes':os.path.getsize(path),'sha256':hashlib.sha256(open(path,'rb').read()).hexdigest()})
hash_by_path={item['path']:item['sha256'] for item in files}
fbx_parity={spec[0]:{
    'source':hash_by_path[f"ArtSource/RoadsideV1/{spec[0]}.fbx"],
    'unity':hash_by_path[f"Assets/Art/Environment/TerrainRoadLookdev/RoadsideV1/Models/{spec[0]}.fbx"],
    'identical':hash_by_path[f"ArtSource/RoadsideV1/{spec[0]}.fbx"]==hash_by_path[f"Assets/Art/Environment/TerrainRoadLookdev/RoadsideV1/Models/{spec[0]}.fbx"]}
    for spec in grass_specs+rock_specs}
if not all(item['identical'] for item in fbx_parity.values()):
    raise RuntimeError('Source and Unity FBX copies differ')
report={
    'generator':'ArtSource/RoadsideV1/generate_roadside_v1.py','seed':SEED,
    'blender':bpy.app.version_string,'tileMeters':TILE_M,'textureSize':TEXTURE_SIZE,
    'grassAtlasSize':GRASS_ATLAS_SIZE,
    'assets':{obj.name:mesh_metrics(obj) for obj in grass_objs+rock_objs},
    'fbxSourceUnityParity':fbx_parity,
    'textures':{
        'groundAlbedo':{'path':os.path.relpath(ground_albedo_path,ROOT).replace('\\','/'),'channels':'RGB sRGB','seamMaxDelta':seam_diff(ground_rgb)},
        'groundNormal':{'path':os.path.relpath(ground_normal_path,ROOT).replace('\\','/'),'channels':'RGB tangent normal from height','seamMaxDelta':seam_diff(normal_rgb)},
        'groundMask':{'path':os.path.relpath(mask_path,ROOT).replace('\\','/'),'channels':'R metal=0; G AO; B height; A smoothness=0','seamMaxDelta':seam_diff(mask)},
        'grassAtlas':{'path':os.path.relpath(grass_atlas_path,ROOT).replace('\\','/'),'regions':3},
        'rockAlbedo':{'path':os.path.relpath(rock_atlas_path,ROOT).replace('\\','/'),'palette':'muted warm grey with subtle ochre mineral variation'},
        'rockNormal':{'path':os.path.relpath(rock_normal_path,ROOT).replace('\\','/'),'channels':'RGB tangent normal from periodic rock height'}},
    'pngReadbacks':png_readbacks,
    'files':files,
    'preview':'Artifacts/RoadsideV1/RoadsideV1_AssetReview.png',
    'notes':['Textures are color-only/albedo, without baked directional shadows or lighting.','Ground albedo accents are low-contrast and correlated to the same physical relief field.','Rock albedo and normal share smart-projected UVs; the normal map is procedural periodic surface detail, not a baked mesh normal.','FBX exports are mesh-only and contain no colliders.','Unity import settings are applied through the live Editor in the next step.']}
with open(os.path.join(REPORTS,'AssetValidation.json'),'w',encoding='utf-8') as f:
    json.dump(report,f,indent=2)
    f.write('\n')
print('ROADSIDE_V1_REPORT='+os.path.join(REPORTS,'AssetValidation.json'))
print('ROADSIDe_V1_ASSETS='+json.dumps(report['assets'],separators=(',',':')))
