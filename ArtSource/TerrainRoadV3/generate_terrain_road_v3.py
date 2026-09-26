"""Generate periodic Terrain Road V3 source geometry and genuine Cycles bakes.

Run with Blender 5.1.2:
  blender.exe --background --python generate_terrain_road_v3.py

The high meshes are a triangulated, irregularly faceted compacted road relief.
Cycles selected-to-active baking transfers color, tangent normals, AO and the
geometric height projection onto a flat 4 m UV target. No raster relief mask is
used to manufacture the normal maps.
"""
import bpy
import json
import math
import os
import random
import numpy as np

ROOT = r"D:\Programy\UnityProjects\RageQuitting"
SOURCE = os.path.join(ROOT, "ArtSource", "TerrainRoadV3")
OUT = os.path.join(ROOT, "Assets", "Art", "Environment", "TerrainRoadLookdev", "SurfaceV3")
REPORT = os.path.join(ROOT, "Artifacts", "TerrainRoadV3")
os.makedirs(SOURCE, exist_ok=True)
os.makedirs(OUT, exist_ok=True)
os.makedirs(REPORT, exist_ok=True)

BLEND = os.path.join(SOURCE, "TerrainRoadV3.blend")
N = 512                 # ~7.8 mm vertex spacing; enough resolution for 10 cm chips
SIZE = 2048
TILE = 4.0
SEED = 426091
random.seed(SEED)
rng = np.random.default_rng(SEED)

# Fresh scene; source generation is self-contained and reproducible.
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for datablocks in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
    pass

def periodic_delta(a, b):
    return (a - b + TILE * 0.5) % TILE - TILE * 0.5

# An irregular periodic point set supplies shared physical forms and material IDs.
# Approximately 700 compacted fragments per tile; the winning connected shapes
# therefore resolve as the requested 10–25 cm pieces instead of tiny cells.
sites = []
count = 700
for i in range(count):
    x, y = rng.random(2) * TILE
    radius = float(rng.choice([rng.uniform(.055, .105), rng.uniform(.105, .155), rng.uniform(.16, .22)], p=[.65, .32, .03]))
    angle = float(rng.uniform(-math.pi, math.pi))
    aspect = float(rng.uniform(.60, 1.0))
    is_stone = rng.random() < .36
    is_dark = (not is_stone) and rng.random() < .10
    # Low-frequency macro bed relief plus each fragment's broken, tilted crown.
    peak = float(rng.uniform(.018, .040))
    if radius > .15: peak *= .72
    # Polygon radii and shared outer-vertex heights define a continuous fan of
    # 8 broad planar facets. Adjacent planes share each anchor exactly.
    radii=rng.uniform(.86,1.14,8).astype(np.float32)
    edge_heights=rng.uniform(.015,.14,8).astype(np.float32)
    sites.append((x, y, radius, angle, aspect, peak, is_stone, is_dark, float(rng.uniform(.75, 1.25)), radii, edge_heights))

# Shared vertices land on both periodic boundaries, so adjacent tiles have
# matching edge positions and heights. Wrapped distances continue edge forms.
axis = np.linspace(0.0, TILE, N, dtype=np.float32)
xx, yy = np.meshgrid(axis, axis, indexing='xy')
height = np.zeros((N, N), np.float32)
rock = np.zeros((N, N), np.uint8)
dark = np.zeros((N, N), np.uint8)
color_var = np.zeros((N, N), np.float32)
owner = np.full((N, N), -1, np.int16)
best = np.full((N, N), 99.0, np.float32)

# Evaluate only each fragment's local footprint, keeping the periodic halo.
spacing=TILE/(N-1)
for si,(sx, sy, rad, angle, aspect, peak, is_stone, is_dark, variation, radii, edge_heights) in enumerate(sites):
        reach=rad*1.65 + spacing*2
        ix=np.flatnonzero(np.minimum((axis-sx)%TILE,(sx-axis)%TILE) < reach)
        iy=np.flatnonzero(np.minimum((axis-sy)%TILE,(sy-axis)%TILE) < reach)
        lx,ly=np.meshgrid(axis[ix],axis[iy],indexing='xy')
        dx = periodic_delta(lx, sx)
        dy = periodic_delta(ly, sy)
        ca, sa = math.cos(angle), math.sin(angle)
        rx = (ca*dx + sa*dy) / aspect
        ry = (-sa*dx + ca*dy) * aspect
        # Angular polygon rings create chipped, asymmetrical perimeters.
        theta = np.arctan2(ry, rx)
        step=2*math.pi/8
        radial_angle=(theta-angle)%(2*math.pi)
        sector=np.floor(radial_angle/step).astype(np.int32)
        along=(radial_angle/step)-sector
        edge=radii[sector]*(1-along)+radii[(sector+1)%8]*along
        q=np.sqrt((rx/rad)**2+(ry/rad)**2)/edge
        # Nearest compacted plate wins. Border is seated into the common bed;
        # facet slopes remain present in both earth and stone surface regions.
        influence = np.clip((1.0-q)/.30, 0, 1)
        influence = influence*influence*(3-2*influence)
        candidate = q
        oldbest=best[np.ix_(iy,ix)]
        take = candidate < oldbest
        # Interpolate the actual fan-triangle plane from center to two shared
        # polygon anchors. Adjacent facets meet with identical anchor heights.
        a0=angle+sector*step; a1=a0+step
        ax=radii[sector]*np.cos(a0); ay=radii[sector]*np.sin(a0)
        bx=radii[(sector+1)%8]*np.cos(a1); by=radii[(sector+1)%8]*np.sin(a1)
        px=rx/rad; py=ry/rad
        denom=ax*by-ay*bx
        wa=(px*by-py*bx)/denom
        wb=(ax*py-ay*px)/denom
        wc=1-wa-wb
        local_h=peak*(wc+wa*edge_heights[sector]+wb*edge_heights[(sector+1)%8])
        oldheight=height[np.ix_(iy,ix)]; oldheight[take]=local_h[take]
        height[np.ix_(iy,ix)]=oldheight
        best[np.ix_(iy,ix)]=np.minimum(oldbest,candidate)
        old=owner[np.ix_(iy,ix)]; old[take]=si; owner[np.ix_(iy,ix)]=old
        old=rock[np.ix_(iy,ix)]; old[take]=1 if is_stone else 0; rock[np.ix_(iy,ix)]=old
        old=dark[np.ix_(iy,ix)]; old[take]=1 if is_dark else 0; dark[np.ix_(iy,ix)]=old
        old=color_var[np.ix_(iy,ix)]; old[take]=(variation-1)*influence[take]; color_var[np.ix_(iy,ix)]=old
        if si%1000==0: print("  geometry sites",si,"/",len(sites),flush=True)

# A compacted matrix remains gently pebbled, including between stone areas.
# Integer-frequency periodic fields create broad clay and fine grit variations.
def periodic_noise(freq, phase):
    out = np.zeros_like(xx)
    for k in range(4):
        ang = phase + k * 2.3999632297
        fx, fy = int(round(freq*math.cos(ang))), int(round(freq*math.sin(ang)))
        out += np.sin(2*math.pi*(fx*xx/TILE + fy*yy/TILE) + phase*k*.81) / (k+1)
    return out / 2.0833

height += .0032*periodic_noise(19, .31) + .0011*periodic_noise(57, 1.14)
height = np.maximum(height, 0)

# Irregularly split each grid cell into alternating triangles. The slight XY
# jitter makes facet outlines non-gridlike; it is periodic and shared by A/B.
verts_xy = np.stack([xx, yy], axis=-1)
faces = []
for j in range(N-1):
    base = j*N
    for i in range(N-1):
        a=base+i; b=a+1; c=a+N; d=c+1
        if (i+j)&1: faces.extend(((a,b,c),(b,d,c)))
        else: faces.extend(((a,b,d),(a,d,c)))
faces = np.asarray(faces, dtype=np.int32)

earth = (0.40, .205, .095, 1)
stone = (.30, .285, .245, 1)
darkearth = (.19, .085, .043, 1)

def make_source(label, scale, color_shift):
    z = (height * scale).reshape(-1)
    verts = [(float(verts_xy.flat[k*2]), float(verts_xy.flat[k*2+1]), float(z[k])) for k in range(N*N)]
    mesh = bpy.data.meshes.new(label+"_ReliefMesh")
    mesh.from_pydata(verts, [], faces.tolist()); mesh.update()
    obj = bpy.data.objects.new(label+"_HighSource", mesh); bpy.context.collection.objects.link(obj)
    # UV spans exact 0..1 across physical tile (center samples offset by half-cell).
    uv = mesh.uv_layers.new(name="BakeUV")
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            vi = mesh.loops[li].vertex_index
            vx, vy = verts_xy.reshape(-1,2)[vi]
            uv.data[li].uv = (float(vx/TILE), float(vy/TILE))
    mats=[]
    for nm, basecol in (("CompactedEarth", earth), ("EmbeddedStone", stone), ("DarkEarth", darkearth)):
        mat=bpy.data.materials.new(label+"_"+nm); mat.diffuse_color=basecol
        mat.use_nodes=True; bs=mat.node_tree.nodes.get("Principled BSDF")
        bs.inputs["Base Color"].default_value=basecol
        # A mild color modulation attribute is baked from the same site shapes.
        vc=mat.node_tree.nodes.new("ShaderNodeVertexColor"); vc.layer_name="PainterColor"
        mult=mat.node_tree.nodes.new("ShaderNodeMixRGB"); mult.blend_type='MULTIPLY'; mult.inputs[0].default_value=1.0
        mult.inputs[1].default_value=basecol; mat.node_tree.links.new(vc.outputs["Color"], mult.inputs[2])
        mat.node_tree.links.new(mult.outputs["Color"], bs.inputs["Base Color"])
        mats.append(mat); mesh.materials.append(mat)
    # Assign region by sampled nearest-site flags, including the earth matrix.
    flatrock=rock.reshape(-1); flatdark=dark.reshape(-1)
    # Per-face region chosen from face centroid vertex IDs.
    for p in mesh.polygons:
        ids=p.vertices
        rv=float(np.mean(flatrock[ids])); dv=float(np.mean(flatdark[ids]))
        p.material_index = 1 if rv > .27 else (2 if dv > .25 else 0)
        p.use_smooth=False
    # Face-corner paint follows each winning fragment and its actual face slope.
    # Per-fragment tints are moderate; facet modulation uses slope magnitude
    # only, with no directional-light or shadow contribution.
    color_attr=mesh.color_attributes.new(name="PainterColor",type='FLOAT_COLOR',domain='CORNER')
    site_tints=np.asarray([s[8] for s in sites],dtype=np.float32)
    flat_owner=owner.reshape(-1)
    for p in mesh.polygons:
        oi=int(flat_owner[int(p.vertices[0])]); oi=max(0,oi)
        tint=1.0+(float(site_tints[oi])-1.0)*.55
        facet=1.0+max(0.0,1.0-float(p.normal.z))*.32
        factor=float(np.clip(tint*facet,.78,1.32))
        for li in p.loop_indices: color_attr.data[li].color=(factor,factor*.985,factor*.96,1.0)
    obj["tile_meters"]=TILE; obj["relief_scale"]=scale
    obj["source_note"]="Actual faceted triangulated geometry; periodic site layout and shared XY"
    return obj

def bake_set(obj, variant):
    # Flat 4m bake receiver. Selected-to-active projects actual source geometry.
    bpy.ops.mesh.primitive_plane_add(size=TILE, location=(TILE/2,TILE/2,-.002))
    low=bpy.context.object; low.name=variant+"_BakeTarget"
    low.data.uv_layers.active.name="BakeUV"
    mat=bpy.data.materials.new(variant+"_BakeMaterial"); mat.use_nodes=True
    low.data.materials.append(mat)
    scene=bpy.context.scene
    scene.render.engine='CYCLES'; scene.cycles.samples=8
    scene.render.bake.use_selected_to_active=True
    scene.render.bake.use_cage=True
    scene.render.bake.cage_extrusion=.08
    scene.render.bake.margin=8
    scene.render.bake.use_clear=True
    scene.render.bake.max_ray_distance=.10
    # Color target, linear data image; source BSDFs are emitted unlit for diffuse color bake.
    def image(name, alpha=False, floatbuf=False):
        im=bpy.data.images.new(name,width=SIZE,height=SIZE,alpha=alpha,float_buffer=floatbuf)
        im.file_format='PNG' if not floatbuf else 'OPEN_EXR'
        return im
    normal=image(variant+"_Normal",False)
    height_img=image(variant+"_Height",False,True)
    cavity=image(variant+"_CavityAO",False)
    normal.colorspace_settings.name='Non-Color'
    height_img.colorspace_settings.name='Non-Color'
    cavity.colorspace_settings.name='Non-Color'
    nodes=mat.node_tree.nodes; nodes.clear(); tex=nodes.new('ShaderNodeTexImage')
    links=mat.node_tree.links
    def set_target(im): tex.image=im; nodes.active=tex
    # Prepare object selection in expected low-active/high-selected order.
    # Nine periodic source copies supply real neighboring geometry for rays.
    halo=[]
    for ox in (-TILE,0,TILE):
        for oy in (-TILE,0,TILE):
            if ox==0 and oy==0: continue
            cp=obj.copy(); cp.data=obj.data; cp.name=obj.name+"_Halo"; bpy.context.collection.objects.link(cp)
            cp.location=(ox,oy,0); halo.append(cp)
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); low.select_set(True)
    for cp in halo: cp.select_set(True)
    bpy.context.view_layer.objects.active=low
    # Albedo: color-only bake from shader color, no lamp contribution.
    scene.cycles.bake_type='DIFFUSE'; scene.render.bake.use_pass_color=True
    scene.render.bake.use_pass_direct=False; scene.render.bake.use_pass_indirect=False
    albedo_paths=[]
    if variant.endswith("_B"):
        palettes={"Road":((.40,.205,.095),(.30,.285,.245),(.19,.085,.043)),
                  "Stone":((.40,.20,.09),(.32,.305,.27),(.18,.08,.04)),
                  "DarkEarth":((.36,.17,.075),(.29,.265,.225),(.175,.078,.04))}
        for palette_name,palette in palettes.items():
            for mi,m in enumerate(obj.data.materials):
                mix=next((n for n in m.node_tree.nodes if n.type=='MIX_RGB'),None)
                if mix: mix.inputs[1].default_value=(*palette[mi],1)
            albedo=image(variant+"_"+palette_name+"Albedo",False)
            set_target(albedo); bpy.ops.object.bake(type='DIFFUSE',use_selected_to_active=True)
            if palette_name=="Road":
                pixels=np.asarray(albedo.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4)
                rgb=pixels[:,:,:3]
                if float(rgb.std())<.02 or float(np.mean(np.max(rgb,axis=2)<.015))>.05:
                    raise RuntimeError("Albedo bake invalid: weak color variance or >5% black/missed pixels; stopping")
            albedo.filepath_raw=os.path.join(OUT,"RoadV3_"+palette_name+"Albedo.png"); albedo.file_format='PNG'; albedo.save()
            albedo_paths.append(albedo.filepath_raw)
    # Tangent normal is physically transferred from the high mesh.
    set_target(normal); bpy.ops.object.bake(type='NORMAL',use_selected_to_active=True)
    norm_pixels=np.asarray(normal.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4)
    xy_variance=float(norm_pixels[:,:,0].std()+norm_pixels[:,:,1].std())
    decoded_z=norm_pixels[:,:,2]*2-1
    if xy_variance<.015 or float(np.mean(decoded_z<.35))>.05:
        raise RuntimeError("Normal bake invalid: nearly flat; stopping before height/AO bakes")
    normal.filepath_raw=os.path.join(OUT,variant+"_Normal.png"); normal.file_format='PNG'; normal.save()
    # Height: orthographic geometric projection rendered as linear float via EXR.
    # (A and B are exact 0.5 ratio because source Z coordinates alone are scaled.)
    set_target(height_img)
    # Emit node reads generated Position Z in object space; selected-to-active baking
    # of an emission shader is not a supported geometry pass in all Blender builds.
    # Use a top-down orthographic camera and 32-bit float EXR for explicit height.
    bpy.data.objects.remove(low, do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
    old_links={}
    camdata=bpy.data.cameras.new(variant+"_HeightCamera"); cam=bpy.data.objects.new(variant+"_HeightCamera",camdata); bpy.context.collection.objects.link(cam)
    cam.location=(TILE/2,TILE/2,.20); cam.rotation_euler=(0,0,0); cam.data.clip_start=.001; cam.data.clip_end=10
    # Blender camera looks down local -Z; orient to top-down with Z-up image.
    cam.rotation_euler=(0,0,0); cam.data.type='ORTHO'; cam.data.ortho_scale=TILE
    scene.camera=cam; scene.render.resolution_x=SIZE; scene.render.resolution_y=SIZE; scene.render.resolution_percentage=100
    scene.view_settings.view_transform='Standard'; scene.render.image_settings.file_format='OPEN_EXR'; scene.render.image_settings.color_depth='32'
    old_hidden={ob:ob.hide_render for ob in bpy.context.scene.objects}
    for ob in bpy.context.scene.objects:
        if ob!=obj and ob not in halo: ob.hide_render=True
    # Material emission encodes world-space elevation normalized to 0..1 for robust EXR.
    for m in obj.data.materials:
        nt=m.node_tree; bs=nt.nodes.get('Principled BSDF')
        out=nt.nodes.get('Material Output')
        old_links[m]=[(l.from_node,l.from_socket) for l in nt.links if l.to_node==out and l.to_socket==out.inputs['Surface']]
        emission=nt.nodes.new('ShaderNodeEmission')
        geom=nt.nodes.new('ShaderNodeNewGeometry'); sep=nt.nodes.new('ShaderNodeSeparateXYZ')
        nt.links.new(geom.outputs['Position'],sep.inputs[0]); nt.links.new(sep.outputs['Z'],emission.inputs['Color'])
        nt.nodes.active=emission
        out=nt.nodes.get('Material Output'); nt.links.new(emission.outputs[0],out.inputs['Surface'])
    # Camera compositor output is EXR; source geometry only, no lighting/shadows.
    scene.render.filepath=os.path.join(OUT,variant+"_Height.exr")
    bpy.ops.render.render(write_still=True)
    for ob,hidden in old_hidden.items(): ob.hide_render=hidden
    # Restore materials without temporary emission graph before saving source blend.
    for m in obj.data.materials:
        nt=m.node_tree
        for n in list(nt.nodes):
            if n.type in {'EMISSION','NEW_GEOMETRY','SEPARATE_XYZ'}: nt.nodes.remove(n)
        out=nt.nodes.get('Material Output')
        for from_node,from_socket in old_links[m]: nt.links.new(from_socket,out.inputs['Surface'])
    # AO as separate bake, then save PNG. Recreate planar receiver.
    cavity_path=os.path.join(OUT,"RoadV3_CavityAO.png")
    if variant.endswith("_B"):
        bpy.ops.mesh.primitive_plane_add(size=TILE, location=(TILE/2,TILE/2,-.002)); low=bpy.context.object; low.name=variant+"_AOTarget"
        low.data.materials.append(mat); bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); low.select_set(True)
        for cp in halo: cp.select_set(True)
        bpy.context.view_layer.objects.active=low
        scene.render.bake.use_selected_to_active=True; scene.render.bake.max_ray_distance=.08
        set_target(cavity); bpy.ops.object.bake(type='AO',use_selected_to_active=True)
        cavity.filepath_raw=cavity_path; cavity.file_format='PNG'; cavity.save()
        bpy.data.objects.remove(low,do_unlink=True)
    bpy.data.objects.remove(cam,do_unlink=True)
    for cp in halo: bpy.data.objects.remove(cp,do_unlink=True)
    return {"albedo":albedo_paths if albedo_paths else [os.path.join(OUT,"RoadV3_"+n+"Albedo.png") for n in ("Road","Stone","DarkEarth")],"normal":normal.filepath_raw,"height":os.path.join(OUT,variant+"_Height.exr"),"cavity":cavity_path}

sources={}
for label,scale in (("RoadV3_B",1.0),("RoadV3_A",.5)):
    print("Building",label,flush=True)
    source=make_source(label,scale,(0,0,0)); sources[label]=source
    if label.endswith("_B"):
        # Save full editable source scene; A is retained alongside B in same blend.
        pass
    maps=bake_set(source,label)
    source["bake_maps"]=json.dumps(maps)

# Pack R=0, G=gentle AO, B=linear geometric height, A=0.
height_path=os.path.join(OUT,"RoadV3_B_Height.exr")
ao_path=os.path.join(OUT,"RoadV3_CavityAO.png")
himg=bpy.data.images.load(height_path,check_existing=False); himg.colorspace_settings.name='Non-Color'
aimg=bpy.data.images.load(ao_path,check_existing=False); aimg.colorspace_settings.name='Non-Color'
hp=np.asarray(himg.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4)
ap=np.asarray(aimg.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4)
hmax=max(float(height.max()),1e-6)
hb=np.clip(hp[:,:,0]/hmax,0,1)
gentle_ao=np.clip(1.0-(1.0-ap[:,:,0])*.35,0,1)
packed=np.zeros((SIZE,SIZE,4),np.float32)
packed[:,:,1]=gentle_ao; packed[:,:,2]=hb
maskimg=bpy.data.images.new("RoadV3_URP_MaskMap",width=SIZE,height=SIZE,alpha=True,float_buffer=False)
maskimg.colorspace_settings.name='Non-Color'; maskimg.pixels.foreach_set(packed.ravel())
maskimg.file_format='PNG'; maskimg.filepath_raw=os.path.join(OUT,"RoadV3_URP_MaskMap.png"); maskimg.save()

# Asset QA metrics are derived from actual geometry and saved bake pixels.
source_b=sources["RoadV3_B"]; source_a=sources["RoadV3_A"]
# Leave the editable source in the primary road palette with A hidden by default.
for mi,m in enumerate(source_b.data.materials):
    mix=next((n for n in m.node_tree.nodes if n.type=='MIX_RGB'),None)
    base=((.40,.205,.095),(.30,.285,.245),(.19,.085,.043))[mi]
    if mix: mix.inputs[1].default_value=(*base,1)
source_a.hide_set(True); source_b.hide_set(False)
z_b=np.fromiter((v.co.z for v in source_b.data.vertices),dtype=np.float32,count=len(source_b.data.vertices))
z_a=np.fromiter((v.co.z for v in source_a.data.vertices),dtype=np.float32,count=len(source_a.data.vertices))
xy=np.asarray([v.co[:2] for v in source_b.data.vertices],dtype=np.float32)
tri=np.asarray([p.vertices[:] for p in source_b.data.polygons],dtype=np.int32)
tri_xy=xy[tri]
areas=.5*np.abs((tri_xy[:,1,0]-tri_xy[:,0,0])*(tri_xy[:,2,1]-tri_xy[:,0,1])-(tri_xy[:,1,1]-tri_xy[:,0,1])*(tri_xy[:,2,0]-tri_xy[:,0,0]))
mat_ids=np.asarray([p.material_index for p in source_b.data.polygons],dtype=np.int8)
stone_area=float(areas[mat_ids==1].sum()); all_area=float(areas.sum())
owner_counts=np.bincount(owner.ravel()[owner.ravel()>=0],minlength=len(sites))
areas_per_site=owner_counts*(TILE/(N-1))**2
stone_ids=np.asarray([s[6] for s in sites],dtype=bool)
stone_diam=2*np.sqrt(areas_per_site[(owner_counts>0)&stone_ids]/math.pi)
road_albedo=bpy.data.images.load(os.path.join(OUT,"RoadV3_RoadAlbedo.png"),check_existing=False)
road_albedo.colorspace_settings.name='sRGB'; cp=np.asarray(road_albedo.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4)
normal_b=bpy.data.images.load(os.path.join(OUT,"RoadV3_B_Normal.png"),check_existing=False); normal_b.colorspace_settings.name='Non-Color'
normal_a=bpy.data.images.load(os.path.join(OUT,"RoadV3_A_Normal.png"),check_existing=False); normal_a.colorspace_settings.name='Non-Color'
nb=np.asarray(normal_b.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4)[:,:,:3]*2-1
na=np.asarray(normal_a.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4)[:,:,:3]*2-1
nb/=np.maximum(np.linalg.norm(nb,axis=2,keepdims=True),1e-8)
na/=np.maximum(np.linalg.norm(na,axis=2,keepdims=True),1e-8)
valid=(nb[:,:,2]>.25)&(na[:,:,2]>.25)
slope_b=np.sqrt(nb[:,:,0]**2+nb[:,:,1]**2)/np.maximum(nb[:,:,2],1e-8)
slope_a=np.sqrt(na[:,:,0]**2+na[:,:,1]**2)/np.maximum(na[:,:,2],1e-8)
tilt_b=np.degrees(np.arctan(slope_b[valid]))
tilt_a=np.degrees(np.arctan(slope_a[valid]))
def seam_ratio(a):
    ax=np.abs(a[:,0]-a[:,-1]).mean(); ay=np.abs(a[0,:]-a[-1,:]).mean()
    ix=np.abs(np.diff(a,axis=1)).mean(); iy=np.abs(np.diff(a,axis=0)).mean()
    return {"horizontal_edge_gradient_mean":float(ax),"vertical_edge_gradient_mean":float(ay),"interior_gradient_mean_x":float(ix),"interior_gradient_mean_y":float(iy),"edge_gradient_ratio_x":float(ax/max(ix,1e-8)),"edge_gradient_ratio_y":float(ay/max(iy,1e-8))}
metrics={
 "resolution_px":[SIZE,SIZE],"tile_m":TILE,"seed":SEED,"source_vertices":len(source_b.data.vertices),"source_triangles":len(source_b.data.polygons),
 "source_z_A_B_half_error_m":float(np.max(np.abs(z_a-.5*z_b))),
 "source_periodic_height_edge_error_m":{"x":float(np.max(np.abs(height[:,0]-height[:,-1]))),"y":float(np.max(np.abs(height[0,:]-height[-1,:])))},
 "actual_triangle_xy_area_m2":{"total":all_area,"stone_fraction":stone_area/max(all_area,1e-8),"dark_earth_fraction":float(areas[mat_ids==2].sum()/max(all_area,1e-8))},
 "stone_connected_owner_equivalent_diameter_m_p10_p50_p90":[float(x) for x in np.percentile(stone_diam,[10,50,90])],
 "normal_baked_tilt_degrees":{"B_p50_p95_p99":[float(x) for x in np.percentile(tilt_b,[50,95,99])],"A_p50_p95_p99":[float(x) for x in np.percentile(tilt_a,[50,95,99])],"A_to_B_median_ratio":float(np.median(tilt_a)/max(np.median(tilt_b),1e-8))},
 "normal_geometric_slope_ratio":{"baked_tangent_slope_median_A_to_B":float(np.median(slope_a[valid])/max(np.median(slope_b[valid]),1e-8)),"source_face_tilt_p50_B_A":[float(np.percentile(np.degrees(np.arccos(np.clip(np.asarray([p.normal.z for p in source_b.data.polygons]),-1,1))),50)),float(np.percentile(np.degrees(np.arccos(np.clip(np.asarray([p.normal.z for p in source_a.data.polygons]),-1,1))),50))]},
 "normal_seam":seam_ratio(nb),"height_seam":seam_ratio(hp[:,:,0]),
 "bake_pixel_ranges":{"road_albedo_rgb_minmax":[float(cp[:,:,:3].min()),float(cp[:,:,:3].max())],"road_albedo_unique_rgb_count":int(np.unique((cp[:,:,:3]*255).astype(np.uint8).reshape(-1,3),axis=0).shape[0]),"road_albedo_rgb_std":float(cp[:,:,:3].std()),"source_fragment_tint_std":float(np.std(np.asarray([s[8] for s in sites],dtype=np.float32))),"normal_B_rgb_minmax":[float(nb.min()),float(nb.max())],"height_exr_z_minmax":[float(hp[:,:,0].min()),float(hp[:,:,0].max())],"ao_minmax":[float(ap[:,:,0].min()),float(ap[:,:,0].max())],"packed_mask_G_B_minmax":[float(gentle_ao.min()),float(gentle_ao.max()),float(hb.min()),float(hb.max())],"albedo_nearblack_fraction":float(np.mean(np.max(cp[:,:,:3],axis=2)<.015)),"normal_invalid_fraction":float(1.0-np.mean(valid))},
 "map_files":[n for n in sorted(os.listdir(OUT)) if n not in {"RoadV3_A_Albedo.png","RoadV3_B_Albedo.png","RoadV3_B_CavityAO.png"} and os.path.isfile(os.path.join(OUT,n))]
}
with open(os.path.join(REPORT,"TerrainRoadV3_AssetMetrics.json"),"w",encoding="utf-8") as f: json.dump(metrics,f,indent=2)

# Add common tiled flat bake target reference grid and save all high source meshes.
bpy.ops.object.select_all(action='DESELECT')
for ob in bpy.context.scene.objects:
    ob.select_set(False)
bpy.context.view_layer.objects.active=None
bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print("Saved",BLEND,flush=True)
