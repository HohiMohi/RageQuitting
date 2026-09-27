"""Generate a deterministic, editable 4 m Terrain Road V4 tile and Cycles bakes.

Blender 5.1.2:
  blender.exe --factory-startup --background --python-exit-code 1 --python generate_terrain_road_v4.py

The mesh is a periodic earth bed with overlapping, jittered quadrilateral
patches. Each patch has a broad tilted top with small planar color/height
facets. The three material IDs and vertex colors describe compacted earth,
grey stone and dark earth. All output maps are baked from this geometry,
never painted from an image mask. Blender Y maps to Terrain Z.
"""
import bpy
import json
import math
import os
import random
import hashlib
import numpy as np

ROOT = r"D:\Programy\UnityProjects\RageQuitting"
SOURCE = os.path.join(ROOT, "ArtSource", "TerrainRoadV4")
OUT = os.path.join(ROOT, "Assets", "Art", "Environment", "TerrainRoadLookdev", "SurfaceV4")
REPORT = os.path.join(ROOT, "Artifacts", "TerrainRoadV4")
BLEND = os.path.join(SOURCE, "TerrainRoadV4.blend")
os.makedirs(SOURCE, exist_ok=True); os.makedirs(OUT, exist_ok=True); os.makedirs(REPORT, exist_ok=True)

SIZE = 2048
TILE = 4.0
SEED = 4260926
NX, NY = 24, 16
BAKE_SHIFT = (0.613, 0.379)
random.seed(SEED)
rng = np.random.default_rng(SEED)

# Fresh isolated Blender process and clean scene: this never affects an open Blender UI.
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for block in list(bpy.data.meshes):
    if block.users == 0: bpy.data.meshes.remove(block)

verts = []
faces = []
face_materials = []
face_tints = []
uvs = []

def add_vertex(p):
    verts.append(tuple(float(v) for v in p))
    return len(verts) - 1

def add_face(indices, material, tint):
    faces.append(tuple(indices)); face_materials.append(int(material)); face_tints.append(float(tint))


# Continuous, gently warped compacted-earth bed. Its boundary vertices and
# heights are exact periodic copies; individual stones overlap this bed.
BED_N=72
bed_nodes={}
canonical={}
def bed_height(x,y):
    return .0035+.0013*math.sin(2*math.pi*(2*x/TILE+.13))*math.cos(2*math.pi*(3*y/TILE-.21))+.0006*math.sin(2*math.pi*(5*x/TILE+2*y/TILE))
for j in range(BED_N):
    for i in range(BED_N):
        x=TILE*i/BED_N; y=TILE*j/BED_N
        if i not in (0,): x+=float(rng.uniform(-.002,.002))
        if j not in (0,): y+=float(rng.uniform(-.002,.002))
        z=bed_height(x,y)
        canonical[(i,j)]=(x,y,z); bed_nodes[(i,j)]=add_vertex((x,y,z))
for j in range(BED_N):
    x,y,z=canonical[(0,j)]; bed_nodes[(BED_N,j)]=add_vertex((x+TILE,y,z))
for i in range(BED_N):
    x,y,z=canonical[(i,0)]; bed_nodes[(i,BED_N)]=add_vertex((x,y+TILE,z))
x,y,z=canonical[(0,0)]; bed_nodes[(BED_N,BED_N)]=add_vertex((x+TILE,y+TILE,z))
for j in range(BED_N):
    for i in range(BED_N):
        a=bed_nodes[(i,j)]; b=bed_nodes[(i+1,j)]; c=bed_nodes[(i,j+1)]; d=bed_nodes[(i+1,j+1)]
        tint=float(rng.uniform(.94,1.06))
        if rng.random()<.5: add_face((a,b,d),0,tint); add_face((a,d,c),0,tint*float(rng.uniform(.98,1.02)))
        else: add_face((a,b,c),0,tint); add_face((b,d,c),0,tint*float(rng.uniform(.98,1.02)))

# Embedded, individually chipped quadrilateral plates at aperiodic centers.
# The tile repeats as a whole, while neighboring translated copies supply the
# fragments crossing each bake edge.
patch_areas=[]; cell_centers=[]; patch_specs=[]
PATCH_COUNT=720
for patch_index in range(PATCH_COUNT):
    cx=float(rng.uniform(0,TILE)); cy=float(rng.uniform(0,TILE))
    size_kind=float(rng.random())
    if size_kind<.12: width=float(rng.uniform(.10,.15)); length=float(rng.uniform(.14,.19))
    elif size_kind>.94: width=float(rng.uniform(.23,.30)); length=float(rng.uniform(.30,.40))
    else: width=float(rng.uniform(.14,.22)); length=float(rng.uniform(.20,.29))
    freer=rng.random()<=.15
    theta=float(rng.uniform(0,math.pi)) if freer else math.radians(float(rng.uniform(-25,25)))
    u=np.asarray((math.cos(theta),math.sin(theta))); v=np.asarray((-math.sin(theta),math.cos(theta)))
    rotation=np.column_stack((u,v)); material_sample=float(rng.random()); material=1 if material_sample<.42 else (2 if material_sample<.50 else 0)
    inset=float(rng.uniform(.18,.25)); buried=bool(rng.random()<.26)
    crown=float(rng.uniform(.027,.034) if not buried else rng.uniform(.021,.028))
    tilt_u=float(rng.uniform(-.025,.025)); tilt_v=float(rng.uniform(-.020,.020))
    nxp=nyp=4; outer={}; top={}; local_heights={}
    # A subtly notched/chipped outline; corners vary in size and middle edges bend.
    corner_scales=[float(rng.uniform(.94,1.02)) for _ in range(4)]
    for jj in range(nyp+1):
        for ii in range(nxp+1):
            su=ii/nxp*2-1; sv=jj/nyp*2-1
            xscale=corner_scales[0] if ii==0 and jj==0 else corner_scales[1] if ii==nxp and jj==0 else corner_scales[2] if ii==nxp and jj==nyp else corner_scales[3] if ii==0 and jj==nyp else 1.0
            lx=su*width*.5*xscale; ly=sv*length*.5*xscale
            if (ii in (0,nxp) and jj not in (0,nyp)) or (jj in (0,nyp) and ii not in (0,nxp)):
                lx+=float(rng.uniform(-.002,.002)); ly+=float(rng.uniform(-.002,.002))
            xy=np.asarray((cx,cy))+rotation@np.asarray((lx,ly))
            z=bed_height(float(xy[0]),float(xy[1]))-.0015
            outer[(ii,jj)]=add_vertex((xy[0],xy[1],z))
    tx=float(rng.uniform(-.22,.22)); ty=float(rng.uniform(-.18,.18))
    for jj in range(nyp+1):
        for ii in range(nxp+1):
            su=(ii/nxp*2-1)*(1-inset)+tx*inset; sv=(jj/nyp*2-1)*(1-inset)+ty*inset
            lx=su*width*.5; ly=sv*length*.5
            xy=np.asarray((cx,cy))+rotation@np.asarray((lx,ly))
            sub=float(rng.uniform(-.0018,.0018))
            z=bed_height(float(xy[0]),float(xy[1]))+crown+tilt_u*lx+tilt_v*ly+sub
            top[(ii,jj)]=add_vertex((xy[0],xy[1],z)); local_heights[(ii,jj)]=z
    # Close each bevel strip along the exact shared top and outer boundary chains.
    for k in range(nxp):
        for a,b,c,d in ((outer[(k,0)],outer[(k+1,0)],top[(k+1,0)],top[(k,0)]),
                        (outer[(nxp-k,nyp)],outer[(nxp-k-1,nyp)],top[(nxp-k-1,nyp)],top[(nxp-k,nyp)])):
            tint=float(rng.uniform(.94,1.06)); add_face((a,b,c),material,tint); add_face((a,c,d),material,tint)
    for k in range(nyp):
        for a,b,c,d in ((outer[(nxp,k)],outer[(nxp,k+1)],top[(nxp,k+1)],top[(nxp,k)]),
                        (outer[(0,nyp-k)],outer[(0,nyp-k-1)],top[(0,nyp-k-1)],top[(0,nyp-k)])):
            tint=float(rng.uniform(.94,1.06)); add_face((a,b,c),material,tint); add_face((a,c,d),material,tint)
    for jj in range(nyp):
        for ii in range(nxp):
            a=top[(ii,jj)]; b=top[(ii+1,jj)]; c=top[(ii,jj+1)]; d=top[(ii+1,jj+1)]
            tint=float(rng.uniform(.91,1.09))
            if rng.random()<.5: add_face((a,b,d),material,tint); add_face((a,d,c),material,tint*float(rng.uniform(.98,1.02)))
            else: add_face((a,b,c),material,tint); add_face((b,d,c),material,tint*float(rng.uniform(.98,1.02)))
    patch_areas.append(width*length); cell_centers.append((cx,cy,width,length,material)); patch_specs.append((width,length,theta,freer,material))

# Palettes remain warm like V3. The first map is the default road palette;
# Stone and DarkEarth maps shift all three strata gently, not just their base.
palettes = {
    "Road": ((.40,.205,.095),(.30,.285,.245),(.19,.085,.043)),
    "Stone": ((.39,.19,.085),(.32,.31,.275),(.18,.078,.04)),
    "DarkEarth": ((.36,.17,.075),(.29,.265,.225),(.175,.078,.04)),
}
materials=[]
for label, col in zip(("Road","Stone","DarkEarth"),palettes["Road"]):
    m=bpy.data.materials.new("RoadV4_"+label); m.diffuse_color=(*col,1); m.use_nodes=True
    nt=m.node_tree; nt.nodes.clear(); out=nt.nodes.new('ShaderNodeOutputMaterial')
    bs=nt.nodes.new('ShaderNodeBsdfPrincipled'); v=nt.nodes.new('ShaderNodeVertexColor'); v.layer_name='PlateTint'
    mult=nt.nodes.new('ShaderNodeMixRGB'); mult.blend_type='MULTIPLY'; mult.inputs[0].default_value=1.0; mult.inputs[1].default_value=(*col,1)
    nt.links.new(v.outputs['Color'],mult.inputs[2]); nt.links.new(mult.outputs['Color'],bs.inputs['Base Color']); nt.links.new(bs.outputs['BSDF'],out.inputs['Surface'])
    materials.append(m)

mesh=bpy.data.meshes.new("TerrainRoadV4_PeriodicPlateMesh")
mesh.from_pydata(verts,[],faces); mesh.update()
obj=bpy.data.objects.new("TerrainRoadV4_HighSource",mesh); bpy.context.collection.objects.link(obj)
for m in materials: mesh.materials.append(m)
for poly,mi in zip(mesh.polygons,face_materials): poly.material_index=mi; poly.use_smooth=False
attr=mesh.color_attributes.new(name='PlateTint',type='FLOAT_COLOR',domain='CORNER')
for poly,tint in zip(mesh.polygons,face_tints):
    for li in poly.loop_indices: attr.data[li].color=(tint,tint*.985,tint*.96,1)

# Fail before baking if the generated tile contains holes, degenerate faces,
# non-finite coordinates, or non-periodic boundary node positions/heights.
vertex_array=np.asarray(verts,dtype=np.float64)
face_array=np.asarray(faces,dtype=np.int32)
tri_xy=vertex_array[face_array,:2]
signed_area2=(tri_xy[:,1,0]-tri_xy[:,0,0])*(tri_xy[:,2,1]-tri_xy[:,0,1])-(tri_xy[:,1,1]-tri_xy[:,0,1])*(tri_xy[:,2,0]-tri_xy[:,0,0])
face_area2=np.abs(signed_area2)
negative_projected_triangles=int(np.count_nonzero(signed_area2 < -1e-10))
periodic_node_error=max(max(abs(verts[bed_nodes[(0,j)]][1]-verts[bed_nodes[(BED_N,j)]][1]),abs(verts[bed_nodes[(0,j)]][2]-verts[bed_nodes[(BED_N,j)]][2])) for j in range(BED_N+1))
periodic_node_error=max(periodic_node_error,max(max(abs(verts[bed_nodes[(i,0)]][0]-verts[bed_nodes[(i,BED_N)]][0]),abs(verts[bed_nodes[(i,0)]][2]-verts[bed_nodes[(i,BED_N)]][2])) for i in range(BED_N+1)))
source_xy_area=float(face_area2.sum()*.5)
topology_metrics={"invalid_coordinate_count":int(np.size(vertex_array)-np.count_nonzero(np.isfinite(vertex_array))),"degenerate_triangle_count":int(np.count_nonzero(face_area2<1e-12)),"negative_projected_triangle_count":negative_projected_triangles,"minimum_signed_projected_area_m2":float(signed_area2.min()*.5),"intentional_patch_overlap":True,"sum_projected_surface_area_m2":source_xy_area,"periodic_bed_boundary_error_m":float(periodic_node_error),"source_bounds_xyz_minmax":[vertex_array.min(axis=0).tolist(),vertex_array.max(axis=0).tolist()]}
if topology_metrics["invalid_coordinate_count"] or topology_metrics["degenerate_triangle_count"] or negative_projected_triangles or periodic_node_error>1e-7:
    raise RuntimeError("Source topology validation failed before baking: "+json.dumps(topology_metrics))
obj["tile_meters"]=TILE; obj["generator_seed"]=SEED
obj["design_note"]="Periodic quadrilateral embedded plates, broad off-center tilted tops, nested planar subfields; Blender Y maps to Terrain Z."

# Geometric mesh statistics from the actual source.
xy=np.asarray([v.co[:2] for v in mesh.vertices],dtype=np.float64)
tri=np.asarray([p.vertices[:] for p in mesh.polygons],dtype=np.int32)
xy_tri=xy[tri]
areas=.5*np.abs((xy_tri[:,1,0]-xy_tri[:,0,0])*(xy_tri[:,2,1]-xy_tri[:,0,1])-(xy_tri[:,1,1]-xy_tri[:,0,1])*(xy_tri[:,2,0]-xy_tri[:,0,0]))
total_area=TILE*TILE
diameters=2*np.sqrt(np.asarray(patch_areas)/math.pi)

# Build a flat UV receiver at z=-0.06 m, select neighboring periodic source copies for rays.
scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=12
scene.render.resolution_x=SIZE; scene.render.resolution_y=SIZE; scene.render.resolution_percentage=100
scene.render.bake.use_selected_to_active=True; scene.render.bake.use_cage=True; scene.render.bake.cage_extrusion=.08
scene.render.bake.margin=16; scene.render.bake.use_clear=True; scene.render.bake.max_ray_distance=.10
bpy.ops.mesh.primitive_plane_add(size=TILE,location=(TILE/2+BAKE_SHIFT[0],TILE/2+BAKE_SHIFT[1],-.002)); low=bpy.context.object; low.name="RoadV4_BakeReceiver"
low.data.uv_layers.active.name='RoadV4_UV'; receiver_mat=bpy.data.materials.new("RoadV4_BakeReceiverMaterial"); receiver_mat.use_nodes=True; low.data.materials.append(receiver_mat)
def set_receiver_uv(receiver):
    uv=receiver.data.uv_layers.active
    for loop in receiver.data.loops:
        co=receiver.data.vertices[loop.vertex_index].co
        uv.data[loop.index].uv=(float(co.x/TILE+.5),float(co.y/TILE+.5))
    coords=np.asarray([uv.data[i].uv[:] for i in range(len(uv.data))],dtype=np.float32)
    if float(coords.min()) < -1e-6 or float(coords.max()) > 1.000001:
        raise RuntimeError("Receiver UVs do not cover exact 0..1 tile")
set_receiver_uv(low)
target=receiver_mat.node_tree.nodes.new('ShaderNodeTexImage'); receiver_mat.node_tree.nodes.active=target
halo=[]
for ox in (-TILE,0,TILE):
    for oy in (-TILE,0,TILE):
        if ox==0 and oy==0: continue
        cp=obj.copy(); cp.data=obj.data; cp.name="TerrainRoadV4_PeriodicHalo"; cp.location=(ox,oy,0); bpy.context.collection.objects.link(cp); halo.append(cp)
bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); low.select_set(True)
for cp in halo: cp.select_set(True)
bpy.context.view_layer.objects.active=low

def image(name,floatbuf=False):
    im=bpy.data.images.new(name,width=SIZE,height=SIZE,alpha=True,float_buffer=floatbuf)
    im.colorspace_settings.name='Non-Color'
    return im

def bake_color_palette(label,palette):
    for i,m in enumerate(materials):
        col=palette[i]; bs=m.node_tree.nodes.get('Principled BSDF'); bs.inputs['Base Color'].default_value=(*col,1)
        # Update the connected vertex tint multiplier base color.
        mul=next(n for n in m.node_tree.nodes if n.type=='MIX_RGB'); mul.inputs[1].default_value=(*col,1)
    im=image("RoadV4_"+label+"Albedo")
    im.colorspace_settings.name='sRGB'
    target.image=im; scene.render.bake.use_pass_color=True; scene.render.bake.use_pass_direct=False; scene.render.bake.use_pass_indirect=False
    bpy.ops.object.bake(type='DIFFUSE',use_selected_to_active=True)
    px=np.asarray(im.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4); rgb=px[:,:,:3]
    nearblack=float(np.mean(np.max(rgb,axis=2)<.015))
    print(label,"albedo QA",{"finite":bool(np.isfinite(rgb).all()),"std":float(rgb.std()),"nearblack":nearblack,"min":float(rgb.min()),"max":float(rgb.max())},flush=True)
    if not np.isfinite(rgb).all() or float(rgb.std())<.02 or nearblack>.03:
        raise RuntimeError(label+" albedo has non-finite, low-variance, or missed-black pixels")
    im.filepath_raw=os.path.join(OUT,"RoadV4_"+label+"Albedo.png"); im.file_format='PNG'; im.save()
    return rgb

print("Bake three unlit albedos",flush=True)
albedo_stats={}
for label,palette in palettes.items():
    rgb=bake_color_palette(label,palette)
    albedo_stats[label]={"rgb_std":float(rgb.std()),"nearblack_fraction":float(np.mean(np.max(rgb,axis=2)<.015)),"rgb_min":float(rgb.min()),"rgb_max":float(rgb.max())}

print("Bake physical tangent normal",flush=True)
normal=image("RoadV4_Normal"); target.image=normal
scene.render.bake.normal_space='TANGENT'; scene.render.bake.normal_r='POS_X'; scene.render.bake.normal_g='POS_Y'; scene.render.bake.normal_b='POS_Z'
bpy.ops.object.bake(type='NORMAL',use_selected_to_active=True)
normal.filepath_raw=os.path.join(OUT,"RoadV4_Normal.png"); normal.file_format='PNG'; normal.save()
normal_px=np.asarray(normal.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4)
norm=normal_px[:,:,:3]*2-1; norm/=np.maximum(np.linalg.norm(norm,axis=2,keepdims=True),1e-8)
normal_invalid=float(np.mean(norm[:,:,2]<.25))
if not np.isfinite(norm).all() or float(norm[:,:,:2].std())<.02 or normal_invalid>.03:
    raise RuntimeError("Normal bake failed physical range/non-flat/missed-ray validation")

print("Render linear 32-bit height",flush=True)
bpy.data.objects.remove(low,do_unlink=True)
camdata=bpy.data.cameras.new("RoadV4_HeightCamera"); cam=bpy.data.objects.new("RoadV4_HeightCamera",camdata); bpy.context.collection.objects.link(cam)
cam.location=(TILE/2+BAKE_SHIFT[0],TILE/2+BAKE_SHIFT[1],.25); cam.rotation_euler=(0,0,0); cam.data.type='ORTHO'; cam.data.ortho_scale=TILE; cam.data.clip_start=.001; cam.data.clip_end=5; scene.camera=cam
old_hide={ob:ob.hide_render for ob in scene.objects}
for ob in scene.objects:
    if ob!=obj and ob not in halo: ob.hide_render=True
old_engine=scene.render.engine; scene.render.engine='CYCLES'; scene.view_settings.view_transform='Standard'
old_surface_links={m:[(l.from_node,l.from_socket) for l in m.node_tree.links if l.to_node==next(n for n in m.node_tree.nodes if n.type=='OUTPUT_MATERIAL') and l.to_socket.name=='Surface'] for m in materials}
for m in materials:
    nt=m.node_tree; out=next(n for n in nt.nodes if n.type=='OUTPUT_MATERIAL')
    em=nt.nodes.new('ShaderNodeEmission'); geom=nt.nodes.new('ShaderNodeNewGeometry'); sep=nt.nodes.new('ShaderNodeSeparateXYZ')
    nt.links.new(geom.outputs['Position'],sep.inputs[0]); nt.links.new(sep.outputs['Z'],em.inputs['Color']); nt.links.new(em.outputs[0],out.inputs['Surface'])
scene.render.image_settings.file_format='OPEN_EXR'; scene.render.image_settings.color_depth='32'; scene.render.filepath=os.path.join(OUT,"RoadV4_Height.exr")
bpy.ops.render.render(write_still=True)
for ob,h in old_hide.items(): ob.hide_render=h
for m in materials:
    nt=m.node_tree
    for n in list(nt.nodes):
        if n.type in {'EMISSION','NEW_GEOMETRY','SEPARATE_XYZ'}: nt.nodes.remove(n)
    out=next(n for n in nt.nodes if n.type=='OUTPUT_MATERIAL')
    for from_node,from_socket in old_surface_links[m]: nt.links.new(from_socket,out.inputs['Surface'])
    # Base color input is restored to the warm Road palette before saving source.
    mi=materials.index(m); col=palettes['Road'][mi]; m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(*col,1)
    mul=next(n for n in nt.nodes if n.type=='MIX_RGB'); mul.inputs[1].default_value=(*col,1)

height_img=bpy.data.images.load(os.path.join(OUT,"RoadV4_Height.exr"),check_existing=False); height_img.colorspace_settings.name='Non-Color'
height_px=np.asarray(height_img.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4); h=height_px[:,:,0]
if not np.isfinite(h).all(): raise RuntimeError("Height EXR contains NaN or infinity")
height_range=(float(h.min()),float(h.max()))
if height_range[1]-height_range[0] < .02: raise RuntimeError("Height bake relief is below 2 cm")

print("Bake AO",flush=True)
bpy.ops.mesh.primitive_plane_add(size=TILE,location=(TILE/2+BAKE_SHIFT[0],TILE/2+BAKE_SHIFT[1],-.002)); low=bpy.context.object; low.name='RoadV4_AOReceiver'; low.data.materials.append(receiver_mat)
set_receiver_uv(low)
target.image=image('RoadV4_AO'); bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); low.select_set(True)
for cp in halo: cp.select_set(True)
bpy.context.view_layer.objects.active=low; scene.render.bake.max_ray_distance=.15
bpy.ops.object.bake(type='AO',use_selected_to_active=True)
ao_px=np.asarray(target.image.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4); ao_raw=ao_px[:,:,0]
if not np.isfinite(ao_raw).all() or float(ao_raw.std())<.01: raise RuntimeError("AO bake invalid or flat")
ao=np.clip(1-(1-np.clip(ao_raw,0,1))*.35,.65,1)
target.image.pixels.foreach_set(np.stack((ao,ao,ao,np.ones_like(ao)),axis=-1).reshape(-1))
target.image.filepath_raw=os.path.join(OUT,"RoadV4_AO.png"); target.image.file_format='PNG'; target.image.save()

# TerrainLit packed mask: R metallic 0, G gentle AO only, B linear normalized height, A smoothness 0.
packed=np.zeros((SIZE,SIZE,4),np.float32)
packed[:,:,1]=np.clip(1-(1-np.clip(ao,0,1))*.35,.65,1)
packed[:,:,2]=np.clip((h-height_range[0])/max(height_range[1]-height_range[0],1e-8),0,1)
mask=image('RoadV4_URP_MaskMap'); mask.pixels.foreach_set(packed.reshape(-1)); mask.filepath_raw=os.path.join(OUT,'RoadV4_URP_MaskMap.png'); mask.file_format='PNG'; mask.save()

# Reload saved files for range, seam, finite, tile-alignment, and normal/height orientation QA.
def loadpx(path):
    im=bpy.data.images.load(path,check_existing=False); im.colorspace_settings.name='Non-Color'
    return np.asarray(im.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4)
albedo=loadpx(os.path.join(OUT,'RoadV4_RoadAlbedo.png'))[:,:,:3]
albedo_triplet=np.concatenate((albedo,loadpx(os.path.join(OUT,'RoadV4_StoneAlbedo.png'))[:,:,:3],loadpx(os.path.join(OUT,'RoadV4_DarkEarthAlbedo.png'))[:,:,:3]),axis=2)
albedo_linear=np.where(albedo_triplet<=.04045,albedo_triplet/12.92,((albedo_triplet+.055)/1.055)**2.4)
palette_signatures=np.asarray([[palette[material] for palette in palettes.values()] for material in range(3)],dtype=np.float32).reshape(3,9)
class_distance=np.linalg.norm(albedo_linear[:,:,None,:]-palette_signatures[None,None,:,:],axis=3)
visible_class=np.argmin(class_distance,axis=2)
stone_area=float(np.mean(visible_class==1)); darkearth_area=float(np.mean(visible_class==2))
normal_saved=loadpx(os.path.join(OUT,'RoadV4_Normal.png'))[:,:,:3]*2-1
normal_saved/=np.maximum(np.linalg.norm(normal_saved,axis=2,keepdims=True),1e-8)
height_saved=loadpx(os.path.join(OUT,'RoadV4_Height.exr'))[:,:,0]
ao_saved=loadpx(os.path.join(OUT,'RoadV4_AO.png'))[:,:,0]
def seam(a):
    return {"x_edge_mean":float(np.abs(a[:,0]-a[:,-1]).mean()),"y_edge_mean":float(np.abs(a[0,:]-a[-1,:]).mean()),
            "x_interior_mean":float(np.abs(np.diff(a,axis=1)).mean()),"y_interior_mean":float(np.abs(np.diff(a,axis=0)).mean())}
dh=TILE/(SIZE-1); gy,gx=np.gradient(height_saved,dh,dh)
bakedx=normal_saved[:,:,0]/np.maximum(normal_saved[:,:,2],1e-8); bakedy=normal_saved[:,:,1]/np.maximum(normal_saved[:,:,2],1e-8)
surface_valid=(normal_saved[:,:,2]>.25)&(np.abs(gx)<.8)&(np.abs(gy)<.8)
valid=(normal_saved[:,:,2]>.82)&(np.abs(gx)<.45)&(np.abs(gy)<.45)
def corr(a,b): return float(np.corrcoef(a[valid].ravel(),b[valid].ravel())[0,1])
def corr_surface(a,b): return float(np.corrcoef(a[surface_valid].ravel(),b[surface_valid].ravel())[0,1])
orientation_x=corr(bakedx,-gx); orientation_y=corr(bakedy,-gy)
orientation_x_surface=corr_surface(bakedx,-gx); orientation_y_surface=corr_surface(bakedy,-gy)
orientation_x_all=float(np.corrcoef(bakedx.ravel(),-gx.ravel())[0,1]); orientation_y_all=float(np.corrcoef(bakedy.ravel(),-gy.ravel())[0,1])
expected=np.stack((-gx,-gy,np.ones_like(gx)),axis=2); expected/=np.maximum(np.linalg.norm(expected,axis=2,keepdims=True),1e-8)
normal_height_angular_error=np.degrees(np.arccos(np.clip(np.sum(expected*normal_saved,axis=2),-1,1)))
normal_tilt=np.degrees(np.arctan2(np.linalg.norm(normal_saved[:,:,:2],axis=2),np.maximum(normal_saved[:,:,2],1e-8)))
normal_edge=seam(normal_saved)
height_edge=seam(height_saved)
def edge_ratio(s,axis):
    return s[axis+"_edge_mean"]/max(s[axis+"_interior_mean"],1e-8)
qa_failures=[]
if not .30 <= stone_area <= .40: qa_failures.append("Visible stone coverage outside 30-40% acceptance band")
if orientation_x_surface < .60 or orientation_y_surface < .60: qa_failures.append("Surface-valid normal-to-height orientation correlation is weak or axis-mapped incorrectly")
edge_ratios={"normal_x":edge_ratio(normal_edge,"x"),"normal_y":edge_ratio(normal_edge,"y"),"height_x":edge_ratio(height_edge,"x"),"height_y":edge_ratio(height_edge,"y")}
if max(edge_ratios.values()) > 6: qa_failures.append("Bake tile seam gradient is implausible relative to interior gradients")
if not (height_range[1]-height_range[0] >= .02 and np.isfinite(albedo).all() and np.isfinite(normal_saved).all() and np.isfinite(height_saved).all() and np.isfinite(ao_saved).all()):
    qa_failures.append("One or more saved bake maps failed range or finite-pixel validation")
metrics={
 "resolution_px":[SIZE,SIZE],"tile_m":TILE,"bake_offset_xy_m":list(BAKE_SHIFT),"seed":SEED,"source_vertices":len(mesh.vertices),"source_faces":len(mesh.polygons),
 "source_mesh_sha256":hashlib.sha256(np.asarray(verts,dtype=np.float32).tobytes()+np.asarray(faces,dtype=np.int32).tobytes()).hexdigest(),
 "visible_stone_coverage_fraction_from_albedo_classification":stone_area,"visible_darkearth_coverage_fraction_from_albedo_classification":darkearth_area,
 "stone_plate_equivalent_diameter_m_p10_p50_p90":[float(x) for x in np.percentile(diameters,[10,50,90])],
 "patch_short_axis_m_p10_p50_p90":[float(x) for x in np.percentile([p[0] for p in patch_specs],[10,50,90])],"patch_long_axis_m_p10_p50_p90":[float(x) for x in np.percentile([p[1] for p in patch_specs],[10,50,90])],
 "patch_long_axis_within_25deg_of_terrain_z_fraction":float(np.mean([not p[3] for p in patch_specs])),
 "geometry_topology":topology_metrics,
 "source_height_minmax_m":[float(min(v[2] for v in verts)),float(max(v[2] for v in verts))],"baked_height_minmax_m":list(height_range),
 "normal_invalid_fraction":normal_invalid,"normal_tilt_degrees_p50_p95_p99":[float(x) for x in np.percentile(normal_tilt,[50,95,99])],
 "normal_height_orientation_surface_valid_corr":{"blender_x_to_terrain_x":orientation_x_surface,"blender_y_to_terrain_z":orientation_y_surface},"normal_height_orientation_surface_valid_sample_count":int(surface_valid.sum()),
 "normal_height_orientation_strict_interior_corr":{"blender_x_to_terrain_x":orientation_x,"blender_y_to_terrain_z":orientation_y},"normal_height_orientation_strict_interior_sample_count":int(valid.sum()),
 "normal_height_orientation_unfiltered_corr":{"blender_x_to_terrain_x":orientation_x_all,"blender_y_to_terrain_z":orientation_y_all},"normal_height_orientation_unfiltered_sample_count":int(height_saved.size),
 "normal_height_surface_valid_angular_error_degrees_p50_p95":[float(x) for x in np.percentile(normal_height_angular_error[surface_valid],[50,95])],
 "normal_seams":normal_edge,"height_seams":height_edge,"ao_seams":seam(ao_saved),"albedo_seams":seam(albedo),"edge_to_interior_ratios":edge_ratios,
 "ao_minmax_std":[float(ao_saved.min()),float(ao_saved.max()),float(ao_saved.std())],"albedo":albedo_stats,
 "bake_pixels_finite":bool(np.isfinite(albedo).all() and np.isfinite(normal_saved).all() and np.isfinite(height_saved).all() and np.isfinite(ao_saved).all()),
 "files":sorted(n for n in os.listdir(OUT) if n.startswith('RoadV4_')),"qa_failures":qa_failures
}
with open(os.path.join(REPORT,'TerrainRoadV4_AssetMetrics.json'),'w',encoding='utf-8') as f: json.dump(metrics,f,indent=2)
with open(os.path.join(REPORT,'TerrainRoadV4_Validation.txt'),'w',encoding='utf-8') as f:
    f.write("Terrain Road V4 source and bake validation\n"+json.dumps(metrics,indent=2)+"\n")

# Save editable source only: hide no needed input geometry; temporary bake receivers,
# halos and camera are removed so the source scene stays small and clear.
for cp in halo: bpy.data.objects.remove(cp,do_unlink=True)
for name in ('RoadV4_AOReceiver','RoadV4_BakeReceiver','RoadV4_HeightCamera'):
    ob=bpy.data.objects.get(name)
    if ob: bpy.data.objects.remove(ob,do_unlink=True)
scene.camera=None
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active=None
bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print(json.dumps(metrics,indent=2),flush=True)
print("Saved editable source:",BLEND,flush=True)
if qa_failures: raise RuntimeError("Bake validation failed: "+"; ".join(qa_failures))
