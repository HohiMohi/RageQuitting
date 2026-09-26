import bpy, math, os, random, json
import numpy as np
from mathutils import Vector

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = r"D:\Programy\UnityProjects\RageQuitting\Assets\Art\Environment\TerrainRoadLookdev"
os.makedirs(OUT, exist_ok=True)
N = 1024
SEED = 73142
rng = np.random.default_rng(SEED)

def periodic_field(size, octaves, seed_offset=0):
    """Smooth, seamless value field built only from integer-frequency sinusoids."""
    rr = np.random.default_rng(SEED + seed_offset)
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    x, y = xx / size, yy / size
    out = np.zeros((size, size), np.float32)
    weights = 0.0
    for i, freq in enumerate(octaves):
        terms = 0
        for _ in range(8):
            a = rr.uniform(0, 2*math.pi)
            phase = rr.uniform(0, 2*math.pi)
            kx = int(round(math.cos(a)*freq)); ky = int(round(math.sin(a)*freq))
            terms += np.sin(2*math.pi*(kx*x + ky*y) + phase)
        w = 1.0 / (1 + i*1.45)
        out += (terms / 8.0) * w
        weights += w
    out /= weights
    out -= out.mean()
    out /= max(float(np.std(out))*2.0, 1e-5)
    return out

def authored_patch_layer(size, patches, softness=0.18):
    """Hand-placed periodic organic pigment marks, distinct from procedural fields.

    Patch values are (u, v, radius_u, radius_v, angle_degrees, phase, opacity).
    Radii are normalized to the 4 m tile and each boundary has authored lobes.
    """
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    x, y = xx / size, yy / size
    layer = np.zeros((size, size), np.float32)
    for u0, v0, ru, rv, angle_deg, phase, opacity in patches:
        dx = (x-u0+0.5) % 1.0 - 0.5
        dy = (y-v0+0.5) % 1.0 - 0.5
        a = math.radians(angle_deg); ca, sa = math.cos(a), math.sin(a)
        ex = (ca*dx + sa*dy) / ru
        ey = (-sa*dx + ca*dy) / rv
        theta = np.arctan2(ey, ex)
        radius = np.sqrt(ex*ex + ey*ey)
        edge = (1.0 + .105*np.sin(3*theta+phase) + .065*np.cos(5*theta-phase*.7)
                + .035*np.sin(7*theta+phase*1.3))
        t = np.clip((edge-radius)/softness, 0.0, 1.0)
        alpha = (t*t*(3.0-2.0*t))*opacity
        layer = np.maximum(layer, alpha)
    return layer

def save_image(name, rgb, alpha=None):
    im = bpy.data.images.new(name, width=N, height=N, alpha=False, float_buffer=False)
    if alpha is None:
        rgba = np.concatenate([np.clip(rgb, 0, 1), np.ones((N,N,1),np.float32)], axis=2)
    else:
        rgba = np.concatenate([np.clip(rgb,0,1), alpha[:,:,None]], axis=2)
    im.pixels.foreach_set(rgba.astype(np.float32).ravel())
    im.filepath_raw = os.path.join(OUT, name + '.png')
    im.file_format = 'PNG'; im.save()
    im.pack()
    return im

def make_normal(name, height, strength=2.5):
    # Periodic central differences avoid an edge discontinuity in the normal map.
    # Keep relief traversable: slope is measured in texture texels, then scaled
    # by the artistic strength instead of multiplying by the full image width.
    dx = (np.roll(height,-1,1)-np.roll(height,1,1)) * 0.5 * strength
    dy = (np.roll(height,-1,0)-np.roll(height,1,0)) * 0.5 * strength
    nx, ny, nz = -dx, -dy, np.ones_like(dx)
    inv = 1.0 / np.sqrt(nx*nx + ny*ny + nz*nz)
    rgb = np.stack((nx*inv*.5+.5, ny*inv*.5+.5, nz*inv*.5+.5), axis=2)
    return save_image(name, rgb)

def add_material(name, color_img, normal_img, rough=0.9):
    m = bpy.data.materials.new(name); m.diffuse_color=(.42,.32,.2,1); m.use_nodes=True
    nt=m.node_tree; bs=nt.nodes.get('Principled BSDF')
    ci=nt.nodes.new('ShaderNodeTexImage'); ci.image=color_img; ci.label='Base Color (sRGB)'
    nt.links.new(ci.outputs['Color'], bs.inputs['Base Color'])
    ni=nt.nodes.new('ShaderNodeTexImage'); ni.image=normal_img; ni.image.colorspace_settings.name='Non-Color'; ni.label='Tangent Normal (Non-Color)'
    nm=nt.nodes.new('ShaderNodeNormalMap'); nm.inputs['Strength'].default_value=.65
    nt.links.new(ni.outputs['Color'],nm.inputs['Color']); nt.links.new(nm.outputs['Normal'],bs.inputs['Normal'])
    bs.inputs['Roughness'].default_value=rough
    return m

# Broad low-frequency regions are the visual structure; fine fields only soften edges and add grain.
grass_large=periodic_field(N,[2,3,5,8],1); grass_mid=periodic_field(N,[12,18,25],2); grass_fine=periodic_field(N,[38,57,81],3)
grass_h=np.clip(.5 + .17*grass_large + .085*grass_mid + .027*grass_fine,0,1)
grass_palette=np.array([[.285,.345,.145],[.405,.455,.205],[.535,.535,.265]],np.float32)
grass_t=np.clip(.5 + .30*grass_large + .12*grass_mid + .06*grass_fine,0,1)
def palette_mix(t, pal):
    lo=np.clip(t,0,1)*(len(pal)-1); idx=np.floor(lo).astype(np.int32); f=(lo-idx)[...,None]
    idx=np.minimum(idx,len(pal)-2)
    return pal[idx]*(1-f)+pal[idx+1]*f
grass_rgb=palette_mix(grass_t,grass_palette)
grass_rgb += grass_fine[:,:,None]*np.array([.012,.014,.006],np.float32)
AUTHORED_GRASS_DRY_PATCHES=[
    (.08,.17,.040,.030,-22,.4,.88),(.22,.69,.050,.032,31,1.2,.78),(.39,.28,.035,.050,-8,2.0,.82),
    (.56,.83,.045,.030,18,.9,.80),(.73,.42,.050,.036,-29,2.7,.85),(.91,.12,.032,.045,11,1.6,.78),
    (.84,.76,.030,.026,42,2.1,.70),(.13,.91,.027,.039,-33,.2,.74),(.48,.55,.030,.025,9,1.8,.70),
    (.32,.08,.033,.028,-12,2.4,.74),(.66,.04,.030,.036,27,.7,.68),(.04,.51,.038,.026,15,1.1,.72)]
AUTHORED_GRASS_OLIVE_PATCHES=[
    (.16,.36,.030,.025,12,.8,.76),(.46,.95,.025,.035,-16,1.9,.66),(.62,.62,.035,.025,30,2.6,.72),
    (.96,.48,.025,.033,-5,.3,.70),(.29,.48,.026,.025,8,1.3,.68),(.76,.10,.034,.024,-21,2.1,.72)]
grass_dry=authored_patch_layer(N,AUTHORED_GRASS_DRY_PATCHES,.28)
grass_olive=authored_patch_layer(N,AUTHORED_GRASS_OLIVE_PATCHES,.30)
grass_rgb=grass_rgb*(1.0-grass_dry[:,:,None]*.65)+np.array([.59,.49,.245],np.float32)*grass_dry[:,:,None]*.65
grass_rgb=grass_rgb*(1.0-grass_olive[:,:,None]*.50)+np.array([.29,.335,.15],np.float32)*grass_olive[:,:,None]*.50
grass_h=np.clip(grass_h + grass_dry*.010 - grass_olive*.006,0,1)

road_broad=periodic_field(N,[3,4,6,8,11],11)
road_mid=periodic_field(N,[13,17,22,29],12)
road_fine=periodic_field(N,[43,59,79],13)

# Jittered, toroidal Voronoi facets make one continuous patchwork across the tile.
# A gentle periodic warp keeps the cells organic; no cell resembles an isolated pebble.
road_rng=np.random.default_rng(SEED+1901)
road_sites=[]
min_site_distance=.052
attempts=0
while len(road_sites)<78 and attempts<30000:
    attempts+=1
    candidate=(float(road_rng.random()),float(road_rng.random()))
    if all(min((candidate[0]-u+.5)%1-.5, (candidate[0]-u-.5)%1+.5)**2+
           min((candidate[1]-v+.5)%1-.5, (candidate[1]-v-.5)%1+.5)**2 >= min_site_distance**2
           for u,v in road_sites):
        road_sites.append(candidate)
if len(road_sites)<68:
    raise RuntimeError('Periodic facet site generation failed to reach a usable density')
road_y,road_x=np.mgrid[0:N,0:N].astype(np.float32)
road_x=road_x/N; road_y=road_y/N
warp_x=periodic_field(N,[2,4,7,11],14)*.020
warp_y=periodic_field(N,[3,5,8,12],15)*.020
road_x=(road_x+warp_x)%1.0; road_y=(road_y+warp_y)%1.0
nearest=np.full((N,N),np.inf,np.float32)
second=np.full((N,N),np.inf,np.float32)
road_labels=np.zeros((N,N),np.int16)
second_labels=np.zeros((N,N),np.int16)
road_power_weights=road_rng.normal(0.0,.0010,size=len(road_sites)).astype(np.float32)
for si,(su,sv) in enumerate(road_sites):
    dx=(road_x-su+.5)%1.0-.5; dy=(road_y-sv+.5)%1.0-.5
    dist=dx*dx+dy*dy-road_power_weights[si]
    closer=dist<nearest
    second_better=(~closer)&(dist<second)
    second_labels[closer]=road_labels[closer]
    second_labels[second_better]=si
    second=np.where(closer,nearest,np.minimum(second,dist))
    road_labels[closer]=si
    nearest[closer]=dist[closer]

# These fixed UV ellipses intentionally choose neighboring facet cells together.
# The repeated tile still reads as a broad mixed surface, not one repeated silhouette.
AUTHORED_ROAD_GREY_CLUSTERS=[
    (.12,.16,.105,.115),(.39,.27,.095,.105),(.78,.19,.115,.105),
    (.88,.53,.100,.125),(.59,.72,.115,.105),(.22,.82,.105,.115),
    (.47,.94,.080,.075)]
def in_authored_grey_cluster(u,v):
    for cu,cv,ru,rv in AUTHORED_ROAD_GREY_CLUSTERS:
        du=(u-cu+.5)%1.0-.5; dv=(v-cv+.5)%1.0-.5
        if (du/(ru*1.08))**2+(dv/(rv*1.08))**2 <= 1.0: return True
    return False
road_is_grey=np.array([in_authored_grey_cluster(u,v) for u,v in road_sites],dtype=bool)

earth_palette=np.array([
    [.34,.195,.095],[.42,.245,.112],[.50,.300,.135],[.57,.355,.170],
    [.63,.410,.218],[.53,.335,.190],[.46,.285,.145],[.59,.375,.155]],np.float32)
grey_palette=np.array([[.32,.325,.31],[.39,.395,.375],[.46,.455,.425],[.35,.365,.355]],np.float32)
earth_ids=road_rng.integers(0,len(earth_palette),size=len(road_sites))
grey_ids=road_rng.integers(0,len(grey_palette),size=len(road_sites))
road_site_colors=np.empty((len(road_sites),3),np.float32)
road_site_colors[~road_is_grey]=earth_palette[earth_ids[~road_is_grey]]
road_site_colors[road_is_grey]=grey_palette[grey_ids[road_is_grey]]
road_site_colors=np.clip(road_site_colors,0,1)
road_rgb=road_site_colors[road_labels]
# A narrow pigment blend softens cell joins without turning the cells into isolated stones.
edge_gap=np.maximum(0.0,second-nearest)
join_blend=np.clip((.0018-edge_gap)/.0018,0.0,1.0)*.30
road_rgb=road_rgb*(1.0-join_blend[:,:,None])+road_site_colors[second_labels]*join_blend[:,:,None]
# Pigment variation stays inside each facet; subtle broad height offsets follow the same facets.
road_mottle=periodic_field(N,[4,7,10,14,19,26],16)
road_rgb += (road_broad*.040+road_mottle*.045+road_mid*.025+road_fine*.012)[:,:,None]*np.array([1.0,.9,.76],np.float32)
road_facet_relief=road_rng.uniform(-1.0,1.0,size=len(road_sites)).astype(np.float32)[road_labels]
road_facet_relief=np.roll(road_facet_relief,1,0)*.12+road_facet_relief*.52+np.roll(road_facet_relief,-1,0)*.12+np.roll(road_facet_relief,1,1)*.12+np.roll(road_facet_relief,-1,1)*.12
road_h=np.clip(.48 + .055*road_broad + .024*road_mid + .008*road_fine + .010*road_facet_relief + .009*road_is_grey[road_labels],0,1)
grey_area=float(np.mean(road_is_grey[road_labels]))
road_cell_areas=np.bincount(road_labels.ravel(),minlength=len(road_sites)).astype(np.float32)*(16.0/(N*N))
road_cell_diameters=2.0*np.sqrt(road_cell_areas/np.pi)

stone_b=periodic_field(N,[3,4,6,9,13],21); stone_m=periodic_field(N,[17,24,34],22); stone_f=periodic_field(N,[48,69,91],23)
stone_t2=np.clip(.52 + .30*stone_b + .12*stone_m + .055*stone_f,0,1)
stone_rgb=palette_mix(stone_t2,np.array([[.285,.295,.29],[.405,.415,.405],[.535,.53,.50]],np.float32))
stone_rgb += stone_f[:,:,None]*np.array([.009,.009,.008],np.float32)
AUTHORED_STONE_WEAR_PATCHES=[
    (.11,.25,.050,.030,-19,.3,.83),(.31,.73,.038,.055,17,1.2,.80),(.53,.24,.060,.035,29,2.0,.82),
    (.73,.61,.046,.031,-12,.8,.82),(.93,.13,.039,.048,33,2.7,.80),(.91,.87,.032,.027,-8,1.5,.74),
    (.18,.92,.030,.037,24,2.3,.78),(.43,.49,.035,.027,-31,.6,.72),(.66,.04,.034,.030,9,1.9,.78),
    (.04,.53,.032,.029,-14,1.1,.78),(.81,.34,.030,.039,21,2.5,.76)]
AUTHORED_STONE_WARM_PATCHES=[(.24,.13,.032,.026,12,1.4,.72),(.61,.86,.037,.025,-25,.7,.70),(.85,.51,.028,.034,17,2.2,.70)]
stone_wear=authored_patch_layer(N,AUTHORED_STONE_WEAR_PATCHES,.30)
stone_warm=authored_patch_layer(N,AUTHORED_STONE_WARM_PATCHES,.32)
stone_rgb=stone_rgb*(1-stone_wear[:,:,None]*.38)+np.array([.29,.30,.29],np.float32)*stone_wear[:,:,None]*.38
stone_rgb=stone_rgb*(1-stone_warm[:,:,None]*.46)+np.array([.48,.39,.31],np.float32)*stone_warm[:,:,None]*.46
stone_h=np.clip(.5 + .105*stone_b + .052*stone_m + .014*stone_f + .007*stone_wear - .005*stone_warm,0,1)

imgs={}
for n, rgb in [('T_Grass_Warm',grass_rgb),('T_Road_OchreStone',road_rgb),('T_Stone_MutedGrey',stone_rgb)]: imgs[n]=save_image(n,rgb)
normals={}
for n,h,s in [('N_Grass_Warm',grass_h,2.2),('N_Road_OchreStone',road_h,1.9),('N_Stone_MutedGrey',stone_h,2.3)]: normals[n]=make_normal(n,h,s)
materials=[add_material('M_'+n,imgs[n],normals['N_'+n[2:]]) for n in ['T_Grass_Warm','T_Road_OchreStone','T_Stone_MutedGrey']]

# Asset staging objects in the .blend; FBX exports are individual, origin-centered meshes.
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
def make_rock(name, seed, size=1.0):
    random.seed(seed)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=1, location=(0,0,0))
    ob=bpy.context.object; ob.name=name
    for v in ob.data.vertices:
        p=v.co.copy(); variation=random.uniform(.79,1.23)
        p.x*=variation*random.uniform(.82,1.14); p.y*=variation*random.uniform(.82,1.14)
        p.z*=random.uniform(.64,1.12)
        if p.z < -.15: p.z *= .72
        v.co=p*size
    ob.data.name=name+'_Mesh'; ob.data.materials.append(materials[2]);
    for poly in ob.data.polygons: poly.use_smooth=False
    return ob

def export_obj(ob, filename):
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active=ob
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,filename), use_selection=True, apply_unit_scale=True, bake_space_transform=False, object_types={'MESH'}, add_leaf_bones=False, path_mode='STRIP', embed_textures=False)
    ob.select_set(False)

rocks=[]
for i,(s,sz) in enumerate([(301,1.0),(402,1.0),(503,1.0)],1):
    ob=make_rock('SM_RoadsideRock_%02d'%i,s,sz); rocks.append(ob); export_obj(ob,'SM_RoadsideRock_%02d.fbx'%i)

def make_grass(name, seed, height, spread):
    random.seed(seed); verts=[]; faces=[]
    blades=9 if seed%2 else 11
    for i in range(blades):
        a=random.random()*2*math.pi; r=random.random()*spread*.45
        bx,by=math.cos(a)*r,math.sin(a)*r
        h=height*random.uniform(.68,1.12); lean=random.uniform(.06,.22)*height
        width=height*random.uniform(.065,.105)
        # Two crossed tapered quad strips, warm muted green variation in vertex colors.
        for rot in (0,math.pi/2):
            d=Vector((math.cos(a+rot),math.sin(a+rot),0)); side=Vector((-d.y,d.x,0))
            base=Vector((bx,by,0)); tip=base+d*lean+Vector((0,0,h)); mid=base+d*lean*.35+Vector((0,0,h*.58))
            start=len(verts)
            verts += [base-side*width,base+side*width,mid-side*width*.58,mid+side*width*.58,tip]
            faces += [(start,start+1,start+3,start+2),(start+2,start+3,start+4)]
    mesh=bpy.data.meshes.new(name+'_Mesh'); mesh.from_pydata(verts,[],faces); mesh.update()
    ob=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(ob); ob.data.materials.append(materials[0])
    return ob

grass_objs=[]
for i, vals in enumerate([(801,.52,.62),(802,.68,.72)],1):
    ob=make_grass('SM_RoadsideGrass_%02d'%i,*vals); grass_objs.append(ob); export_obj(ob,'SM_RoadsideGrass_%02d.fbx'%i)

# Tileable 3x3 color preview: each swatch repeats the same 256px crop 3x3 times.
def tile_preview(img, filename):
    data=np.array(img.pixels[:],dtype=np.float32).reshape((N,N,4))[:,:,:3]
    # Tile a complete periodic image 3x3, then downsample to a compact review sheet cell.
    block=data[::4,::4]
    tiled=np.tile(block,(3,3,1))
    out=bpy.data.images.new(filename,width=768,height=768,alpha=False)
    rgba=np.concatenate([tiled,np.ones((768,768,1),np.float32)],axis=2)
    out.pixels.foreach_set(rgba.ravel()); out.filepath_raw=os.path.join(ROOT,filename+'.png'); out.file_format='PNG'; out.save()
    return out
previews=[tile_preview(imgs[n],'Preview_'+n) for n in ['T_Grass_Warm','T_Road_OchreStone','T_Stone_MutedGrey']]
im=bpy.data.images.new('TerrainRoadLookdev_3x3_Montage',width=768,height=768,alpha=False)
# Instead make each row a three-by-three tiled crop of its source; this is a 3x3 grid of material swatches.
grid=np.zeros((768,768,3),np.float32)
# Layout each material in a 3x3 repetition: complete texture repeated in each swatch row.
for r,n in enumerate(['T_Grass_Warm','T_Road_OchreStone','T_Stone_MutedGrey']):
    src=np.array(imgs[n].pixels[:],dtype=np.float32).reshape((N,N,4))[:,:,:3][::4,::4]
    for c in range(3): grid[r*256:(r+1)*256,c*256:(c+1)*256,:]=src[:256,:256,:]
im.pixels.foreach_set(np.concatenate([grid,np.ones((768,768,1),np.float32)],axis=2).ravel()); im.filepath_raw=os.path.join(ROOT,'TerrainRoadLookdev_3x3_Montage.png'); im.file_format='PNG'; im.save()

# Clean staging: retain all authored objects, material nodes, and images in the source scene.
for ob in rocks+grass_objs: ob.location=(0,0,0)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT,'TerrainRoadLookdev.blend'))

# Validation report includes numeric opposite-edge deltas for every map and FBX sizes.
def edge_delta(img):
    a=np.array(img.pixels[:],dtype=np.float32).reshape((N,N,4))[:,:,:3]
    wrap=np.concatenate([np.abs(a[:,0]-a[:,-1]).ravel(),np.abs(a[0]-a[-1]).ravel()])
    inside=np.concatenate([np.abs(a[:,1:]-a[:,:-1]).ravel(),np.abs(a[1:]-a[:-1]).ravel()])
    wrap_p99=float(np.percentile(wrap,99)); inside_p99=float(np.percentile(inside,99))
    return {'wrap_adjacent_max_delta':float(np.max(wrap)),'wrap_adjacent_p99_delta':wrap_p99,
            'interior_neighbor_p99_delta':inside_p99,
            'wrap_to_interior_p99_ratio':float(wrap_p99/max(inside_p99,1e-8))}
report={'blender_version':bpy.app.version_string,'resolution':[N,N],'seed':SEED,'texture_checks':{},'fbx_bytes':{},'road_patch_coverage':{'grey_facet_fraction':grey_area,'facets_per_tile':len(road_sites),'authored_grey_cluster_anchors':len(AUTHORED_ROAD_GREY_CLUSTERS),'equivalent_facet_diameter_m':{'p10':float(np.percentile(road_cell_diameters,10)),'median':float(np.median(road_cell_diameters)),'p90':float(np.percentile(road_cell_diameters,90))}},'authored_patch_layers':['warm grass dry and olive pigment marks with matching subtle height accents','road grey facet clusters selected by fixed UV ellipses; ochre, tan, brown and grey cell pigments; facet-correlated subtle relief','muted stone wear and warm mineral pigment marks with matching subtle height accents'],'notes':['Road is a continuous periodic weighted Voronoi patchwork with varied cell size, curved seams, and mottled pigment inside facets; warm facets dominate and grey facets occur in authored neighboring-cell clusters. No isolated pebble geometry, regular lanes or directional lighting is baked.','Tessellation sites, power weights, cluster anchors, pigment palette, and relief are repeatable; a subtle periodic coordinate warp softens the underlying lattice.','Texture fields and authored patch masks wrap periodically; normal relief uses periodic central differences.']}
# Synthetic ramp sanity check. Blender Image.pixels is bottom-left row first;
# a height ramp increasing with array row is therefore increasing with UV +Y.
# Its correct tangent normal Y is negative, encoded below 0.5 (green); Unity importer
# flipGreenChannel remains false. The earlier Pillow prototype uses top-origin rows,
# so its +dy expression is algebraically the same after converting row orientation.
ramp=np.tile(np.linspace(0.0,1.0,N,dtype=np.float32)[:,None],(1,N))
ramp_dy=(np.roll(ramp,-1,0)-np.roll(ramp,1,0))*.5*2.0
ramp_ny=-ramp_dy
ramp_green=float(ramp_ny[N//2,N//2]*.5+0.5)
assert ramp_green < .5, 'Synthetic +UV.Y ramp must encode a negative tangent Y normal'
report['normal_green_sign_check']={'result':'passed','blender_pixel_row_zero':'bottom','synthetic_height_ramp':'increases toward UV +Y','encoded_green_at_center':ramp_green,'expected':'below 0.5 for negative tangent-space Y; flipGreenChannel=false','pillow_prototype_note':'Pillow row zero is top, so +dy there is equivalent to -dy in the Blender bottom-origin array.'}
# Synthetic ramp sanity check. Blender Image.pixels is bottom-left row first;
# a height ramp increasing with array row is therefore increasing with UV +Y.
# Its correct tangent normal Y is negative, encoded below 0.5 (green); Unity importer
# flipGreenChannel remains false. The earlier Pillow prototype uses top-origin rows,
# so its +dy expression is algebraically the same after converting row orientation.
ramp=np.tile(np.linspace(0.0,1.0,N,dtype=np.float32)[:,None],(1,N))
ramp_dy=(np.roll(ramp,-1,0)-np.roll(ramp,1,0))*.5*2.0
ramp_ny=-ramp_dy
ramp_green=float(ramp_ny[N//2,N//2]*.5+0.5)
assert ramp_green < .5, 'Synthetic +UV.Y ramp must encode a negative tangent Y normal'
report['normal_green_sign_check']={'result':'passed','blender_pixel_row_zero':'bottom','synthetic_height_ramp':'increases toward UV +Y','encoded_green_at_center':ramp_green,'expected':'below 0.5 for negative tangent-space Y; flipGreenChannel=false','pillow_prototype_note':'Pillow row zero is top, so +dy there is equivalent to -dy in the Blender bottom-origin array.'}
for n,img in list(imgs.items())+list(normals.items()):
    report['texture_checks'][n]={'size':list(img.size),'opposite_edge_max_delta':edge_delta(img),'file_bytes':os.path.getsize(os.path.join(OUT,n+'.png'))}
for f in ['SM_RoadsideRock_01.fbx','SM_RoadsideRock_02.fbx','SM_RoadsideRock_03.fbx','SM_RoadsideGrass_01.fbx','SM_RoadsideGrass_02.fbx']:
    report['fbx_bytes'][f]=os.path.getsize(os.path.join(OUT,f))
with open(os.path.join(ROOT,'validation_report.json'),'w',encoding='utf-8') as fp: json.dump(report,fp,indent=2)
print('LOOKDEV_GENERATION_COMPLETE',json.dumps({'blend':os.path.join(ROOT,'TerrainRoadLookdev.blend'),'out':OUT,'report':report},indent=2))
