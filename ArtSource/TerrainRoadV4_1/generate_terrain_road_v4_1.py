"""Generate the periodic, single-surface Terrain Road V4.1 source and bakes.

Run with Blender 5.1.2:
  blender.exe --factory-startup --background --python-exit-code 1 --python generate_terrain_road_v4_1.py

All source values are deterministic functions of a periodic description. Blender
Y maps to Terrain Z. The source is one 1025 x 1025 vertex height field; there are
no stacked plates. Bake images and private previews are written only to the
V4_1 asset and ignored Artifacts directories.
"""
import bpy
import json
import math
import os
import time
import sys
import numpy as np

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
SOURCE = os.path.dirname(__file__)
OUT = os.path.join(ROOT, "Assets", "Art", "Environment", "TerrainRoadLookdev", "SurfaceV4_1")
ART = os.path.join(ROOT, "Artifacts", "TerrainRoadV4_1")
BLEND = os.path.join(SOURCE, "TerrainRoadV4_1.blend")
REPORT = os.path.join(ART, "TerrainRoadV4_1_Generation.json")
SIZE = 2048
N = 1024
TILE = 4.0
SEED = 42609261
PALETTES = {
    "Road": ((.40,.205,.095),(.30,.285,.245),(.19,.085,.043)),
    "Stone": ((.39,.19,.085),(.32,.31,.275),(.18,.078,.04)),
    "DarkEarth": ((.36,.17,.075),(.29,.265,.225),(.175,.078,.04)),
}
os.makedirs(OUT, exist_ok=True)
os.makedirs(ART, exist_ok=True)
started = time.time()
rng = np.random.default_rng(SEED)

# Periodic 2-D cell layout: staggered rows, slow longitudinal drift, and
# bounded center perturbations preserve an exact repeating boundary.
nx, ny = 18, 16
sites = []
run_groups=np.zeros((ny,nx),dtype=np.int32)
run_materials=np.zeros((ny,nx),dtype=np.uint8)
run_drifts=np.zeros((ny,nx),dtype=np.float32)
for i in range(nx):
    j=0; run=0; lateral=float(rng.uniform(-.025,.025))
    while j<ny:
        run_len=int(rng.integers(2,4))
        group=int(rng.integers(0,8))
        if group==7: run_material=1 if rng.random()<.55 else 0
        else: run_material=1 if group in (1,4) else (2 if group in (2,6) else 0)
        lateral+=float(rng.uniform(-.018,.018))
        lateral=float(np.clip(lateral,-.075,.075))
        for jj in range(j,min(ny,j+run_len)):
            run_groups[jj,i]=group; run_materials[jj,i]=run_material; run_drifts[jj,i]=lateral
        j+=run_len; run+=1
for j in range(ny):
    for i in range(nx):
        x = (TILE * (i + .5 + (j % 2) * .5) / nx + run_drifts[j,i]) % TILE
        y = TILE * (j + .5) / ny
        x = (x + float(rng.uniform(-.035, .035))) % TILE
        y += float(rng.uniform(-.030, .030))
        theta = math.radians(float(rng.uniform(-18, 18)))
        # Child regions stay 10–25 cm; the deterministic parent id groups
        # adjacent children into broken 40–80 cm longitudinal runs.
        length = float(rng.uniform(.15, .24))
        width = float(rng.uniform(.10, .21))
        parent = int(run_groups[j,i])
        material = int(run_materials[j,i])
        group = parent
        tone = rng.uniform(.84,1.16,3)
        height_level = .023 + (.030 if group % 2 else 0) + float(rng.uniform(-.0015,.0015))
        knots_u=np.asarray((-1,-.82,-.61,-.43,0,.36,.57,.78,1),np.float64)
        knots_v=np.asarray((-1,-.77,-.54,-.31,0,.41,.68,.85,1),np.float64)
        notch_u=rng.choice((0.0,0.0,0.0,-.32,-.24,.22,.30),size=6).astype(np.float64)
        notch_v=rng.choice((0.0,0.0,0.0,-.30,-.20,.20,.28),size=6).astype(np.float64)
        notches_u=np.concatenate(([0.0],notch_u[:3],[0.0],notch_u[3:],[0.0]))
        notches_v=np.concatenate(([0.0],notch_v[:3],[0.0],notch_v[3:],[0.0]))
        sites.append((x % TILE, y % TILE, theta, length, width, material, group, tone[0], tone[1], tone[2], height_level,*notches_u,*notches_v))
sites = np.asarray(sites, dtype=np.float64)
if sites.shape != (nx*ny,29) or sites[:,11:20].shape[1] != 9 or sites[:,20:29].shape[1] != 9:
    raise RuntimeError("Cell descriptor/notch arrays have invalid shape: %s" % (sites.shape,))

# Small jittered quadrilateral fields (about 2–6 cm across) vary the color
# inside the larger fragments. They are color only and fade at their edges.
micro_nx, micro_ny = 64, 100
micro_rng = np.random.default_rng(SEED ^ 0x5a31)
micro_jx = micro_rng.uniform(-.24,.24,(micro_ny,micro_nx))
micro_jy = micro_rng.uniform(-.24,.24,(micro_ny,micro_nx))
micro_theta = np.deg2rad(micro_rng.uniform(-24,24,(micro_ny,micro_nx)))
micro_width = micro_rng.uniform(.028,.055,(micro_ny,micro_nx))
micro_length = micro_rng.uniform(.025,.058,(micro_ny,micro_nx))
micro_value = micro_rng.uniform(-.18,.18,(micro_ny,micro_nx))

def evaluate_field(x, y):
    """Return one continuous height field and hierarchical RGB surface color."""
    # Mild periodic warp bends the quadrilateral partition while preserving
    # exact tile repetition.
    wx = x + .035*np.sin(2*np.pi*y/TILE + .4) + .014*np.sin(4*np.pi*x/TILE + 1.1)
    wy = y + .027*np.sin(2*np.pi*x/TILE - .8) + .012*np.sin(4*np.pi*y/TILE + .7)
    best = np.full(x.shape, np.inf, dtype=np.float32)
    best_color = np.full(x.shape, np.inf, dtype=np.float32)
    ids = np.zeros(x.shape, dtype=np.uint8)
    # Pass one: find the nearest irregular quadrilateral and categorical color.
    def distances(sx,sy,theta,length,width,group,wx,wy,notches_u,notches_v):
        dx=(wx-sx+TILE*.5)%TILE-TILE*.5; dy=(wy-sy+TILE*.5)%TILE-TILE*.5
        ct,st=math.cos(theta),math.sin(theta)
        u=dx*st+dy*ct; v=dx*ct-dy*st
        p4=(np.abs(u/(length*.5))**4+np.abs(v/(width*.5))**4)**.25
        # Unequal seeded piecewise-linear contour offsets: mostly quiet edge
        # spans with occasional 1–3 cm chips and no repetitive sawtooth.
        ku=np.asarray((-1,-.82,-.61,-.43,0,.36,.57,.78,1),dtype=np.float64)
        kv=np.asarray((-1,-.77,-.54,-.31,0,.41,.68,.85,1),dtype=np.float64)
        ou=np.interp(np.clip(u/(length*.5),-1,1),ku,notches_u)*.20
        ov=np.interp(np.clip(v/(width*.5),-1,1),kv,notches_v)*.20
        dcolor=p4+ou+ov
        return dx,dy,p4,dcolor
    for site in sites:
        sx,sy,theta,length,width,mat,group,tr,tg,tb,height_level=site[:11]
        _,_,dgeom,dcolor=distances(sx,sy,theta,length,width,group,wx,wy,site[11:20],site[20:29])
        win=dcolor<best_color
        ids[win]=int(mat); best_color[win]=dcolor[win]
        best=np.minimum(best,dcolor)
    # Pass two: smooth competitive weights. The closest face always has weight
    # one, so no fixed earth term makes a closed falloff ring around fragments.
    sum_w = np.zeros(x.shape, dtype=np.float32)
    sum_h = np.zeros(x.shape, dtype=np.float32)
    sum_rgb = np.zeros(x.shape+(3,), dtype=np.float32)
    family_w = np.zeros((3,)+x.shape,dtype=np.float32)
    cell_colors = np.asarray(PALETTES['Road'],dtype=np.float32)
    for site in sites:
        sx,sy,theta,length,width,mat,group,tr,tg,tb,height_level=site[:11]
        dx,dy,dgeom,dcolor=distances(sx,sy,theta,length,width,group,wx,wy,site[11:20],site[20:29])
        delta=np.maximum(dcolor-best,0)
        w=np.exp(-5.0*delta).astype(np.float32)
        off = float(height_level)
        tilt_y = .048*np.sin(2*np.pi*(sx/TILE*2 + .31))
        tilt_x = .036*np.cos(2*np.pi*(sy/TILE*2 - .17))
        sum_w += w
        sum_h += w*(off + tilt_x*dx + tilt_y*dy)
        family_w[int(mat)] += w
        basecol=cell_colors[int(mat)]*np.asarray((tr,tg,tb),dtype=np.float32)
        sum_rgb += w[...,None]*basecol

    # A local 3x3 neighborhood of jittered micro-sites adds color-only 2–6 cm
    # subdivisions, independent of the large-form height field.
    bix=np.floor(wx/TILE*micro_nx).astype(np.int32); biy=np.floor(wy/TILE*micro_ny).astype(np.int32)
    micro_best=np.full(x.shape,np.inf,dtype=np.float32); micro_tone=np.zeros(x.shape,dtype=np.float32)
    for oy in (-1,0,1):
        for ox in (-1,0,1):
            mi=(biy+oy)%micro_ny; mj=(bix+ox)%micro_nx
            cx=(mj+.5+micro_jx[mi,mj])*TILE/micro_nx
            cy=(mi+.5+micro_jy[mi,mj])*TILE/micro_ny
            mdx=(wx-cx+TILE*.5)%TILE-TILE*.5; mdy=(wy-cy+TILE*.5)%TILE-TILE*.5
            ct=np.cos(micro_theta[mi,mj]); st=np.sin(micro_theta[mi,mj])
            mu=mdx*st+mdy*ct; mv=mdx*ct-mdy*st
            md=np.maximum(np.abs(mu)/(micro_length[mi,mj]*.5),np.abs(mv)/(micro_width[mi,mj]*.5))
            win=md<micro_best; micro_best[win]=md[win]; micro_tone[win]=micro_value[mi,mj][win]
    micro_edge=(1-micro_best)*.5*.03
    mf=np.clip((micro_edge+.004)/.010,0,1); mf=mf*mf*(3-2*mf)
    micro_factor=1+micro_tone*mf*(best<.95)

    # Quiet compacted bed plus broad, shallow fragments. A smooth raised field
    # follows cell interiors and blends over a 2–5 cm transition; no hard walls.
    broad = sum_h / np.maximum(sum_w,1e-8)
    bed = (.010
        + .0011*np.sin(2*np.pi*(2*x/TILE + .13))*np.cos(2*np.pi*(3*y/TILE - .21))
        + .00045*np.sin(2*np.pi*(5*x/TILE + 2*y/TILE)))
    # Fine breakups are low amplitude and derivative-periodic.
    fine = .0005*np.sin(2*np.pi*(11*x/TILE + 7*y/TILE + .2))*np.sin(2*np.pi*(5*y/TILE - 3*x/TILE))
    height = bed + broad + fine
    soil=np.asarray(PALETTES['Road'][0],dtype=np.float32)
    rgb=sum_rgb/np.maximum(sum_w.reshape(sum_w.shape+(1,)),1e-8)
    rgb=.90*rgb+.10*soil
    rgb*=micro_factor.reshape(micro_factor.shape+(1,))
    family_coverage=np.mean(family_w/np.maximum(sum_w.reshape((1,)+sum_w.shape),1e-8),axis=tuple(range(1,family_w.ndim)))
    return height.astype(np.float32), ids, rgb.astype(np.float32), family_coverage.astype(float).tolist(), float(sum_w.min()), int(np.count_nonzero(sum_w < .01))

# Cheap shape smoke checks catch accidental 1-D/2-D broadcast assumptions
# before the million-sample procedural pass.
for _shape in ((17,), (5,7)):
    _px=np.zeros(_shape,dtype=np.float32)+.13; _py=np.zeros(_shape,dtype=np.float32)+.29
    _result=evaluate_field(_px,_py)
    if _result[0].shape!=_shape or _result[1].shape!=_shape or _result[2].shape!=_shape+(3,):
        raise RuntimeError("Field shape check failed for %s" % (_shape,))

print("Evaluating deterministic periodic height/color fields", flush=True)
axis = np.linspace(0.0, TILE, N+1, dtype=np.float32)
xx, yy = np.meshgrid(axis, axis, indexing='xy')
height, material_ids, tint, coverage_values, support_min, support_hole_count = evaluate_field(xx, yy)
del xx, yy
# Validate the procedural surface and visible color coverage before allocating
# the large Blender mesh.
grad_y, grad_x = np.gradient(height, TILE/N, TILE/N)
palette_array=np.asarray(PALETTES['Road'],dtype=np.float32)
rgb_class=np.argmin(np.sum((tint[:,:,None,:]-palette_array[None,None,:,:])**2,axis=3),axis=2)
coverage = {name: float(coverage_values[i]) for i,name in enumerate(("earth","stone","darkearth"))}
gray_coverage = coverage['stone']
edge_z_x=float(np.max(np.abs(height[:,0]-height[:,-1])))
edge_z_y=float(np.max(np.abs(height[0,:]-height[-1,:])))
probe=np.linspace(0,TILE,257,dtype=np.float32); eps=1e-4
def hfield(px,py): return evaluate_field(px,py)[0]
xp=np.full_like(probe,eps); xm=np.full_like(probe,-eps); xtp=np.full_like(probe,TILE+eps); xtm=np.full_like(probe,TILE-eps)
deriv_x=float(np.max(np.abs((hfield(xp,probe)-hfield(xm,probe))/(2*eps)-(hfield(xtp,probe)-hfield(xtm,probe))/(2*eps))))
yp=np.full_like(probe,eps); ym=np.full_like(probe,-eps); ytp=np.full_like(probe,TILE+eps); ytm=np.full_like(probe,TILE-eps)
deriv_y=float(np.max(np.abs((hfield(probe,yp)-hfield(probe,ym))/(2*eps)-(hfield(probe,ytp)-hfield(probe,ytm))/(2*eps))))
span=int(round(.15*N/TILE))
local_relief=np.concatenate((np.abs(height[:,span:]-height[:,:-span]).ravel(),np.abs(height[span:,:]-height[:-span,:]).ravel()))
local_relief_percentiles=[float(v) for v in np.percentile(local_relief,[50,90,95,99])]
if not .30 <= gray_coverage <= .40:
    print("STYLE METRIC WARNING: visible grey coverage %.5f outside 30-40%%; continuing to preview for review." % gray_coverage,flush=True)
if max(edge_z_x,edge_z_y)>2e-5 or max(deriv_x,deriv_y)>0.004:
    raise RuntimeError("Periodic geometry/derivative validation failed: " + json.dumps({"height_x":edge_z_x,"height_y":edge_z_y,"gradient_x":deriv_x,"gradient_y":deriv_y}))
if not np.isfinite(height).all() or not np.isfinite(tint).all(): raise RuntimeError("Source fields contain non-finite values")
print("Field checks before mesh",json.dumps({"visible_grey_coverage":gray_coverage,"height_range_m":[float(height.min()),float(height.max())],"local_relief_abs_diff_m_p50_p90_p95_p99":local_relief_percentiles,"derivative_error_x_y": [deriv_x,deriv_y],"support_hole_fraction_with_earth_bed_fallback":support_hole_count/height.size}),flush=True)
verts = np.empty(((N+1)*(N+1), 3), dtype=np.float32)
verts[:, 0] = np.tile(axis, N+1)
verts[:, 1] = np.repeat(axis, N+1)
verts[:, 2] = height.ravel()
# Consistent diagonal direction is deliberately unrelated to material cells.
faces = np.empty((N*N*2, 3), dtype=np.int32)
base = (np.repeat(np.arange(N, dtype=np.int32)[:, None]*(N+1), N, axis=1) + np.tile(np.arange(N, dtype=np.int32), (N,1))).ravel()
a = base; b = base + 1; c = base + (N+1); d = c + 1
faces[0::2] = np.stack((a, b, d), axis=1)
faces[1::2] = np.stack((a, d, c), axis=1)
del base, a, b, c, d

bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
for block in list(bpy.data.meshes):
    if block.users == 0: bpy.data.meshes.remove(block)
mesh = bpy.data.meshes.new("TerrainRoadV4_1_ContinuousHeightfield")
mesh.from_pydata(verts.tolist(), [], faces.tolist())
mesh.update(calc_edges=True)
# Use periodic central-difference normals so duplicated tile borders shade
# identically instead of inheriting one-sided boundary normals.
spacing=TILE/N
unique_h=height[:-1,:-1]
gx_unique=(np.roll(unique_h,-1,axis=1)-np.roll(unique_h,1,axis=1))/(2*spacing)
gy_unique=(np.roll(unique_h,-1,axis=0)-np.roll(unique_h,1,axis=0))/(2*spacing)
ng_x=np.pad(gx_unique,((0,1),(0,1)),mode='wrap')
ng_y=np.pad(gy_unique,((0,1),(0,1)),mode='wrap')
vn=np.stack((-ng_x,-ng_y,np.ones_like(height)),axis=-1).reshape(-1,3)
vn/=np.linalg.norm(vn,axis=1)[:,None]
mesh.normals_split_custom_set_from_vertices(vn.tolist())
del verts, faces
obj = bpy.data.objects.new("TerrainRoadV4_1_ContinuousSource", mesh)
bpy.context.collection.objects.link(obj)
material = bpy.data.materials.new("RoadV4_1_ContinuousSurfaceColor")
material.diffuse_color = (.34,.22,.13,1); material.use_nodes = True
nt=material.node_tree; nt.nodes.clear(); out=nt.nodes.new('ShaderNodeOutputMaterial')
bs=nt.nodes.new('ShaderNodeBsdfPrincipled'); bs.inputs['Roughness'].default_value=1.0; bs.inputs['Metallic'].default_value=0.0
vc=nt.nodes.new('ShaderNodeVertexColor'); vc.layer_name='SurfaceColor'
nt.links.new(vc.outputs['Color'],bs.inputs['Base Color']); nt.links.new(bs.outputs['BSDF'],out.inputs['Surface'])
mesh.materials.append(material)
mesh.polygons.foreach_set('material_index', np.zeros(len(mesh.polygons),dtype=np.int32))
mesh.polygons.foreach_set('use_smooth', np.ones(len(mesh.polygons), dtype=np.bool_))
attr=mesh.color_attributes.new(name='SurfaceColor',type='FLOAT_COLOR',domain='POINT')
point_rgb=tint.reshape(-1,3)
rgba_colors=np.concatenate((point_rgb,np.ones((point_rgb.shape[0],1),np.float32)),axis=1)
attr.data.foreach_set('color',rgba_colors.ravel())

mesh["tile_meters"]=TILE; mesh["grid_vertices_per_axis"]=N+1; mesh["generator_seed"]=SEED
mesh["design_note"]="One periodic C1 height field with normalized blends of shallow tilted fragment planes; irregular longitudinal cells encode color regions and 2-6 cm inner fields. Blender Y maps to Terrain Z."

# Fast private geometry review preview. Re-run with --preview-only to stop
# after previews and the editable source blend, before expensive bake passes.
scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE'; scene.render.resolution_x=1200; scene.render.resolution_y=1200; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.render.image_settings.color_mode='RGBA'; scene.view_settings.view_transform='Standard'
ld=bpy.data.lights.new("V4_1_Softbox","AREA"); lo=bpy.data.objects.new("V4_1_Softbox",ld); scene.collection.objects.link(lo); lo.location=(2,1.8,3); ld.energy=75; ld.size=3
camd=bpy.data.cameras.new("V4_1_PreviewCamera"); cam=bpy.data.objects.new("V4_1_PreviewCamera",camd); scene.collection.objects.link(cam); scene.camera=cam
from mathutils import Vector
def render_preview(name, loc, target_point, ortho=None):
    cam.location=loc; cam.rotation_euler=(Vector(target_point)-cam.location).to_track_quat('-Z','Y').to_euler()
    if ortho is not None: cam.data.type='ORTHO'; cam.data.ortho_scale=ortho
    else: cam.data.type='PERSP'; cam.data.lens=52
    scene.render.filepath=os.path.join(ART,name); bpy.ops.render.render(write_still=True)
render_preview("RoadV4_1_TopPreview.png",(2,2,6),(2,2,0),4.0)
render_preview("RoadV4_1_ObliquePreview.png",(6,-2,5),(2,2,0),None)
# Actual repeated source geometry proof (not a tiled render): render nine
# neighboring periodic copies under one camera and directional light.
proof_copies=[]
for ox in (-TILE,0,TILE):
    for oy in (-TILE,0,TILE):
        if ox==0 and oy==0: continue
        cp=obj.copy(); cp.data=obj.data; cp.location=(ox,oy,0); bpy.context.collection.objects.link(cp); proof_copies.append(cp)
sun_data=bpy.data.lights.new("V4_1_TileProofSun","SUN"); sun=bpy.data.objects.new("V4_1_TileProofSun",sun_data); scene.collection.objects.link(sun); sun.rotation_euler=(math.radians(24),math.radians(-18),math.radians(16)); sun_data.energy=2.0
render_preview("RoadV4_1_3x3GeometryProof.png",(2,2,12),(2,2,0),12.0)
scene.collection.objects.unlink(sun); bpy.data.objects.remove(sun,do_unlink=True); bpy.data.lights.remove(sun_data)
for cp in proof_copies: bpy.data.objects.remove(cp,do_unlink=True)
if '--preview-only' in sys.argv:
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    print("Preview-only source saved; bake stage skipped.",flush=True)
    raise SystemExit(0)

# Continue with a fresh bake scene; the preview camera/light do not affect maps.
scene.render.engine='CYCLES'; scene.render.resolution_x=SIZE; scene.render.resolution_y=SIZE

# Geometry must extend beyond each baked tile edge for correct normal/AO rays.
halo=[]
for ox in (-TILE,0,TILE):
    for oy in (-TILE,0,TILE):
        if ox==0 and oy==0: continue
        cp=obj.copy(); cp.data=obj.data; cp.name="V4_1_PeriodicBakeNeighbor"; cp.location=(ox,oy,0); bpy.context.collection.objects.link(cp); halo.append(cp)

scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=8
scene.cycles.use_denoising=False
scene.render.resolution_x=SIZE; scene.render.resolution_y=SIZE; scene.render.resolution_percentage=100
scene.render.bake.use_selected_to_active=True; scene.render.bake.use_cage=False
if not hasattr(scene.render.bake,'cage_extrusion'): raise RuntimeError("Blender bake cage_extrusion API unavailable")
scene.render.bake.cage_extrusion=.16
scene.render.bake.margin=16; scene.render.bake.use_clear=True; scene.render.bake.max_ray_distance=.20
bpy.ops.mesh.primitive_plane_add(size=TILE,location=(TILE/2,TILE/2,-.05)); receiver=bpy.context.object; receiver.name="V4_1_BakeReceiver"
receiver_mat=bpy.data.materials.new("V4_1_BakeReceiverMaterial"); receiver_mat.use_nodes=True; receiver.data.materials.append(receiver_mat)
uv=receiver.data.uv_layers.active
for loop in receiver.data.loops:
    co=receiver.data.vertices[loop.vertex_index].co; uv.data[loop.index].uv=(co.x/TILE+.5,co.y/TILE+.5)
target=receiver_mat.node_tree.nodes.new('ShaderNodeTexImage'); receiver_mat.node_tree.nodes.active=target
bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); receiver.select_set(True)
for cp in halo: cp.select_set(True)
bpy.context.view_layer.objects.active=receiver

def new_image(name, float_buffer=False, srgb=False):
    im=bpy.data.images.new(name,width=SIZE,height=SIZE,alpha=True,float_buffer=float_buffer)
    im.colorspace_settings.name='sRGB' if srgb else 'Non-Color'
    target.image=im
    return im
def save_image(im, filename, fmt):
    im.filepath_raw=os.path.join(OUT,filename); im.file_format=fmt
    if fmt=='OPEN_EXR':
        old=(scene.render.image_settings.file_format,scene.render.image_settings.color_mode,scene.render.image_settings.color_depth,scene.render.image_settings.exr_codec,scene.view_settings.view_transform)
        scene.render.image_settings.file_format='OPEN_EXR'; scene.render.image_settings.color_mode='RGBA'; scene.render.image_settings.color_depth='32'; scene.render.image_settings.exr_codec='ZIP'
        scene.view_settings.view_transform='Raw'
        im.save_render(im.filepath_raw,scene=scene)
        scene.render.image_settings.file_format,scene.render.image_settings.color_mode,scene.render.image_settings.color_depth,scene.render.image_settings.exr_codec,scene.view_settings.view_transform=old
    else:
        # Image.save writes the raw Non-Color channel values without a view transform.
        im.save()

print("Baking three material albedos",flush=True)
palette_scales={"Road":(1.0,1.0,1.0),"Stone":(.98,1.02,1.05),"DarkEarth":(.93,.89,.84)}
base_point_rgb=tint.reshape(-1,3).copy()
for label in PALETTES:
    scaled=base_point_rgb*np.asarray(palette_scales[label],np.float32)[None,:]
    attr.data.foreach_set('color',np.concatenate((scaled,np.ones((len(scaled),1),np.float32)),axis=1).ravel())
    im=new_image("V4_1_"+label+"Albedo",srgb=True)
    scene.render.bake.use_pass_color=True; scene.render.bake.use_pass_direct=False; scene.render.bake.use_pass_indirect=False
    bpy.ops.object.bake(type='DIFFUSE',use_selected_to_active=True)
    save_image(im,"RoadV4_1_%sAlbedo.png"%label,'PNG')

print("Baking real tangent-space normal from continuous source geometry",flush=True)
normal=new_image("V4_1_Normal")
scene.render.bake.normal_space='TANGENT'; scene.render.bake.normal_r='POS_X'; scene.render.bake.normal_g='POS_Y'; scene.render.bake.normal_b='POS_Z'
bpy.ops.object.bake(type='NORMAL',use_selected_to_active=True)
save_image(normal,"RoadV4_1_Normal.png",'PNG')

print("Baking gentle ambient occlusion",flush=True)
ao=new_image("V4_1_AO")
scene.render.bake.use_pass_color=False; scene.render.bake.use_pass_direct=False; scene.render.bake.use_pass_indirect=False
scene.cycles.ao_bounces=1
if not hasattr(scene.cycles,'ao_bounces'): raise RuntimeError("Cycles AO bake controls unavailable")
bpy.ops.object.bake(type='AO',use_selected_to_active=True)
ao_px=np.asarray(ao.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4)
gentle_ao=np.clip(.85+.15*ao_px[:,:,0],0,1)
ao_px[:,:,:3]=gentle_ao[:,:,None]; ao.pixels.foreach_set(ao_px.ravel())
save_image(ao,"RoadV4_1_AO.png",'PNG')

# Height comes from the same field used to create source vertices. Bilinear
# samples are at baked texture texel centers in UV space; rows remain Blender's
# bottom-up image order so height and tangent normal share orientation.
print("Writing linear metric height and TerrainLit mask",flush=True)
coords=(np.arange(SIZE,dtype=np.float32)+.5)*(N/SIZE)
x0=np.floor(coords).astype(np.int32); y0=x0.copy(); fx=coords-x0; fy=fx.copy(); x1=np.minimum(x0+1,N); y1=x1.copy()
wx0=(1-fx)[None,:]; wx1=fx[None,:]; wy0=(1-fy)[:,None]; wy1=fy[:,None]
hmap=(height[np.ix_(y0,x0)]*wy0*wx0 + height[np.ix_(y0,x1)]*wy0*wx1 + height[np.ix_(y1,x0)]*wy1*wx0 + height[np.ix_(y1,x1)]*wy1*wx1).astype(np.float32)
rgba=np.empty((SIZE,SIZE,4),np.float32); rgba[:,:,:3]=hmap[:,:,None]; rgba[:,:,3]=1
hi=bpy.data.images.new("V4_1_Height",width=SIZE,height=SIZE,alpha=True,float_buffer=True); hi.colorspace_settings.name='Non-Color'; hi.pixels.foreach_set(rgba.ravel()); save_image(hi,"RoadV4_1_Height.exr",'OPEN_EXR')
maskpix=np.zeros((SIZE,SIZE,4),np.float32)
ao_px=np.asarray(ao.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4)
maskpix[:,:,1]=np.clip(ao_px[:,:,0],0,1)
maskpix[:,:,2]=(hmap-hmap.min())/max(float(hmap.max()-hmap.min()),1e-8)
maskpix[:,:,3]=0
mi=bpy.data.images.new("V4_1_URP_Mask",width=SIZE,height=SIZE,alpha=True,float_buffer=False); mi.colorspace_settings.name='Non-Color'; mi.pixels.foreach_set(maskpix.ravel()); save_image(mi,"RoadV4_1_URP_MaskMap.png",'PNG')

# Render a nine-tile repeat of the actual baked albedo + tangent normal.
for source_obj in [obj]+halo: source_obj.hide_render=True
receiver.hide_render=True
lo.hide_render=True
bpy.ops.mesh.primitive_plane_add(size=12,location=(2,2,-.10)); baked_plane=bpy.context.object; baked_plane.name="V4_1_BakedMaterialTileProof"
baked_mat=bpy.data.materials.new("V4_1_BakedMaterialProof"); baked_mat.use_nodes=True
proof_nt=baked_mat.node_tree; proof_nt.nodes.clear()
proof_out=proof_nt.nodes.new('ShaderNodeOutputMaterial'); proof_bs=proof_nt.nodes.new('ShaderNodeBsdfPrincipled'); proof_bs.inputs['Roughness'].default_value=1.0
proof_uv=proof_nt.nodes.new('ShaderNodeTexCoord'); proof_map=proof_nt.nodes.new('ShaderNodeMapping'); proof_map.inputs['Scale'].default_value=(3,3,1); proof_nt.links.new(proof_uv.outputs['UV'],proof_map.inputs['Vector'])
albedo_img=bpy.data.images.load(os.path.join(OUT,"RoadV4_1_RoadAlbedo.png"),check_existing=False); albedo_img.colorspace_settings.name='sRGB'
normal_img=bpy.data.images.load(os.path.join(OUT,"RoadV4_1_Normal.png"),check_existing=False); normal_img.colorspace_settings.name='Non-Color'
albedo_node=proof_nt.nodes.new('ShaderNodeTexImage'); albedo_node.image=albedo_img; albedo_node.extension='REPEAT'; proof_nt.links.new(proof_map.outputs['Vector'],albedo_node.inputs['Vector']); proof_nt.links.new(albedo_node.outputs['Color'],proof_bs.inputs['Base Color'])
normal_node=proof_nt.nodes.new('ShaderNodeTexImage'); normal_node.image=normal_img; normal_node.extension='REPEAT'; proof_nt.links.new(proof_map.outputs['Vector'],normal_node.inputs['Vector'])
normal_decode=proof_nt.nodes.new('ShaderNodeNormalMap'); normal_decode.space='TANGENT'; proof_nt.links.new(normal_node.outputs['Color'],normal_decode.inputs['Color']); proof_nt.links.new(normal_decode.outputs['Normal'],proof_bs.inputs['Normal'])
proof_nt.links.new(proof_bs.outputs['BSDF'],proof_out.inputs['Surface'])
proof_out.location=(500,0); proof_bs.location=(240,0); baked_plane.data.materials.append(baked_mat)
proof_sun_data=bpy.data.lights.new("V4_1_BakedProofSun","SUN"); proof_sun=bpy.data.objects.new("V4_1_BakedProofSun",proof_sun_data); scene.collection.objects.link(proof_sun); proof_sun.rotation_euler=(math.radians(24),math.radians(-18),math.radians(16)); proof_sun_data.energy=2.0
scene.render.engine='BLENDER_EEVEE'; scene.render.resolution_x=1200; scene.render.resolution_y=1200; scene.render.resolution_percentage=100
render_preview("RoadV4_1_3x3TileProof.png",(2,2,12),(2,2,0),12.0)
bpy.data.objects.remove(baked_plane,do_unlink=True); bpy.data.materials.remove(baked_mat); bpy.data.objects.remove(proof_sun,do_unlink=True); bpy.data.lights.remove(proof_sun_data)
for source_obj in [obj]+halo: source_obj.hide_render=False
receiver.hide_render=False
lo.hide_render=False
scene.render.engine='CYCLES'; scene.render.resolution_x=SIZE; scene.render.resolution_y=SIZE; scene.render.resolution_percentage=100

# Read saved images back and measure seams, normal orientation, dimensions, and finite pixels.
def load_pixels(path, space='Non-Color'):
    im=bpy.data.images.load(path,check_existing=False); im.colorspace_settings.name=space
    w,h=map(int,im.size); return im,np.asarray(im.pixels[:],dtype=np.float32).reshape(h,w,im.channels)
readback={k:load_pixels(os.path.join(OUT,"RoadV4_1_%sAlbedo.png"%k),'sRGB') for k in PALETTES}
normal_im,normal_px=load_pixels(os.path.join(OUT,"RoadV4_1_Normal.png"))
ao_im,ao_read=load_pixels(os.path.join(OUT,"RoadV4_1_AO.png"))
height_im,height_read=load_pixels(os.path.join(OUT,"RoadV4_1_Height.exr"))
mask_im,mask_read=load_pixels(os.path.join(OUT,"RoadV4_1_URP_MaskMap.png"))
image_pairs=list(readback.values())+[(normal_im,normal_px),(ao_im,ao_read),(height_im,height_read),(mask_im,mask_read)]
if any(im.size[0]!=SIZE or im.size[1]!=SIZE for im,arr in image_pairs): raise RuntimeError("One or more saved maps are not 2048x2048")
if any(not np.isfinite(arr).all() for im,arr in image_pairs): raise RuntimeError("Saved bake readback contains non-finite pixels")
def seam_report(arr):
    a=arr[:,:,:3] if arr.ndim==3 else arr; ex=np.abs(a[:,0]-a[:,-1]); ey=np.abs(a[0]-a[-1])
    ix=np.abs(a[:,1:]-a[:,:-1]).mean(); iy=np.abs(a[1:]-a[:-1]).mean()
    return {"x_edge_mean":float(ex.mean()),"x_edge_max":float(ex.max()),"x_interior_neighbor_mean":float(ix),"x_edge_to_interior_ratio":float(ex.mean()/max(ix,1e-12)),"y_edge_mean":float(ey.mean()),"y_edge_max":float(ey.max()),"y_interior_neighbor_mean":float(iy),"y_edge_to_interior_ratio":float(ey.mean()/max(iy,1e-12))}
decoded=normal_px[:,:,:3]*2-1; norm_len=np.maximum(np.linalg.norm(decoded,axis=2),1e-8); decoded/=norm_len[:,:,None]
custom_grid=vn.reshape(N+1,N+1,3)
hgx=np.gradient(hmap,TILE/SIZE,axis=1); hgy=np.gradient(hmap,TILE/SIZE,axis=0)
normal_alignment={"corr_r_to_minus_height_dx":float(np.corrcoef(decoded[:,:,0].ravel(),(-hgx).ravel())[0,1]),"corr_r_to_plus_height_dx":float(np.corrcoef(decoded[:,:,0].ravel(),hgx.ravel())[0,1]),"corr_g_to_minus_height_dy":float(np.corrcoef(decoded[:,:,1].ravel(),(-hgy).ravel())[0,1]),"corr_g_to_plus_height_dy":float(np.corrcoef(decoded[:,:,1].ravel(),hgy.ravel())[0,1]),"mean_decoded_length":float(norm_len.mean()),"normal_seam_max_component_error":float(max(np.abs(normal_px[:,0]-normal_px[:,-1]).max(),np.abs(normal_px[0]-normal_px[-1]).max()))}
# Independently predict tangent normals at texture texel centers by bilinear
# interpolation of the exact periodic custom source mesh vertex normals.
periodic_vn=custom_grid[:-1,:-1]
sample_x0=x0; sample_x1=(x0+1)%N; sample_y0=y0; sample_y1=(y0+1)%N
expected=(periodic_vn[np.ix_(sample_y0,sample_x0)]*wy0[:,:,None]*wx0[:, :,None]
          +periodic_vn[np.ix_(sample_y0,sample_x1)]*wy0[:,:,None]*wx1[:,:,None]
          +periodic_vn[np.ix_(sample_y1,sample_x0)]*wy1[:,:,None]*wx0[:,:,None]
          +periodic_vn[np.ix_(sample_y1,sample_x1)]*wy1[:,:,None]*wx1[:,:,None])
expected/=np.maximum(np.linalg.norm(expected,axis=2)[:,:,None],1e-8)
dot=np.clip(np.sum(decoded*expected,axis=2),-1,1)
angular=np.degrees(np.arccos(dot))
edge_expected_x=np.linalg.norm(expected[:,0]-expected[:,-1],axis=1).mean()
edge_expected_y=np.linalg.norm(expected[0]-expected[-1],axis=1).mean()
edge_actual_x=np.linalg.norm(decoded[:,0]-decoded[:,-1],axis=1).mean()
edge_actual_y=np.linalg.norm(decoded[0]-decoded[-1],axis=1).mean()
edge_fit_x=np.linalg.norm(decoded[:,[0,-1]]-expected[:,[0,-1]],axis=2).mean()
edge_fit_y=np.linalg.norm(decoded[[0,-1],:]-expected[[0,-1],:],axis=2).mean()
interior_fit=np.linalg.norm(decoded[2:-2,2:-2]-expected[2:-2,2:-2],axis=2).mean()
normal_alignment.update({"sampled_source_mesh_mean_angular_error_deg":float(angular.mean()),"sampled_source_mesh_p95_angular_error_deg":float(np.percentile(angular,95)),"periodic_expected_edge_pair_mean_delta_x":float(edge_expected_x),"periodic_expected_edge_pair_mean_delta_y":float(edge_expected_y),"baked_edge_pair_mean_delta_x":float(edge_actual_x),"baked_edge_pair_mean_delta_y":float(edge_actual_y),"baked_vs_expected_edge_mean_vector_error_x":float(edge_fit_x),"baked_vs_expected_edge_mean_vector_error_y":float(edge_fit_y),"baked_vs_expected_interior_mean_vector_error":float(interior_fit),"edge_to_interior_bake_error_ratio_x":float(edge_fit_x/max(interior_fit,1e-12)),"edge_to_interior_bake_error_ratio_y":float(edge_fit_y/max(interior_fit,1e-12))})
black_hole_fraction={k:float(np.mean(np.all(arr[:,:,:3]<.005,axis=2))) for k,(im,arr) in readback.items()}
custom_grid=vn.reshape(N+1,N+1,3)
custom_normal_edge_error=float(max(np.max(np.abs(custom_grid[0]-custom_grid[-1])),np.max(np.abs(custom_grid[:,0]-custom_grid[:,-1]))))
def exr_channel_types(path):
    import struct
    data=open(path,'rb').read()
    if struct.unpack_from('<I',data,0)[0]!=20000630: raise RuntimeError("Height output is not an OpenEXR file")
    off=8; types={}
    while off<len(data):
        end=data.index(b'\0',off); name=data[off:end].decode('ascii'); off=end+1
        if not name: break
        end=data.index(b'\0',off); attr_type=data[off:end].decode('ascii'); off=end+1
        size=struct.unpack_from('<i',data,off)[0]; off+=4; value_end=off+size
        if name=='channels' and attr_type=='chlist':
            pos=off
            while pos<value_end:
                end=data.index(b'\0',pos); channel=data[pos:end].decode('ascii'); pos=end+1
                if not channel: break
                types[channel]=struct.unpack_from('<i',data,pos)[0]; pos+=16
        off=value_end
    return types
exr_types=exr_channel_types(os.path.join(OUT,"RoadV4_1_Height.exr"))
if not exr_types or any(exr_types.get(ch)!=2 for ch in ('R','G','B')): raise RuntimeError("Height EXR channels are not FLOAT32: "+repr(exr_types))
file_qa={"readback_resolution_px":{k:list(map(int,v[0].size)) for k,v in readback.items()},"finite_pixels_all_maps":True,"height_readback_range_m":[float(height_read[:,:,:3].min()),float(height_read[:,:,:3].max())],"height_readback_vs_field_max_abs_m":float(np.max(np.abs(height_read[:,:,0]-hmap))),"normal_height_orientation_alignment":normal_alignment,"black_bake_hole_fraction":black_hole_fraction,"seam_metrics":{"RoadAlbedo":seam_report(readback['Road'][1]),"Normal":seam_report(normal_px),"AO":seam_report(ao_read),"Height":seam_report(height_read),"Mask":seam_report(mask_read)},"exr_channel_pixel_types":exr_types,"exr_float_image_buffer":bool(height_im.is_float),"periodic_source_custom_normal_edge_max_error":custom_normal_edge_error}
if max(black_hole_fraction.values())>.001: raise RuntimeError("Unexpected black pixels in albedo bake: "+json.dumps(black_hole_fraction))
if file_qa["height_readback_vs_field_max_abs_m"]>1e-6: raise RuntimeError("Height EXR readback does not align with the emitted shared field")
if custom_normal_edge_error>1e-7: raise RuntimeError("Periodic source mesh normals mismatch across tile borders")
# Restore default Road source point colors before saving the editable source.
attr.data.foreach_set('color',np.concatenate((base_point_rgb,np.ones((len(base_point_rgb),1),np.float32)),axis=1).ravel())
bpy.data.objects.remove(receiver,do_unlink=True)
for cp in halo: bpy.data.objects.remove(cp,do_unlink=True)

# Read back saved outputs for basic dimensional/finite checks.
metrics={"blender_version":bpy.app.version_string,"seed":SEED,"tile_m":TILE,"grid_vertices_per_axis":N+1,"vertices":(N+1)**2,"unique_xy_per_vertex":True,"overlapping_source_layers":False,"visible_material_coverage":coverage,"visible_grey_coverage":gray_coverage,
 "height_range_m":[float(height.min()),float(height.max())],"height_relief_peak_to_peak_m":float(height.max()-height.min()),"periodic_boundary":{"height_max_error_m_x":edge_z_x,"height_max_error_m_y":edge_z_y,"gradient_max_error_x":deriv_x,"gradient_max_error_y":deriv_y},
 "derivative_field_range":{"gx_minmax":[float(grad_x.min()),float(grad_x.max())],"gy_minmax":[float(grad_y.min()),float(grad_y.max())]},"map_resolution_px":[SIZE,SIZE],"height_exr_bits_per_channel":32,"source_mesh_faces":len(mesh.polygons),"file_readback_QA":file_qa,"elapsed_seconds":time.time()-started,"bakes":["RoadV4_1_RoadAlbedo.png","RoadV4_1_StoneAlbedo.png","RoadV4_1_DarkEarthAlbedo.png","RoadV4_1_Normal.png","RoadV4_1_Height.exr","RoadV4_1_AO.png","RoadV4_1_URP_MaskMap.png"]}
with open(REPORT,'w',encoding='utf-8') as f: json.dump(metrics,f,indent=2)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print(json.dumps(metrics,indent=2),flush=True)

