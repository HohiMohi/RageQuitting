"""Read-only validation of saved V4 bake maps (Blender 5.1.2 --background)."""
import bpy, json, os, numpy as np
ROOT=r"D:\Programy\UnityProjects\RageQuitting"
OUT=os.path.join(ROOT,"Assets","Art","Environment","TerrainRoadLookdev","SurfaceV4")
REPORT=os.path.join(ROOT,"Artifacts","TerrainRoadV4","TerrainRoadV4_BakeMeasurement.json")
PREVIEW=os.path.join(ROOT,"Artifacts","TerrainRoadV4","RoadV4_3x3_PrivatePreview.png")
SIZE=2048; TILE=4.0
def pixels(name,linear=True):
    im=bpy.data.images.load(os.path.join(OUT,name),check_existing=False)
    im.colorspace_settings.name='Non-Color' if linear else 'sRGB'
    a=np.asarray(im.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4)
    if not np.isfinite(a).all(): raise RuntimeError(name+" contains NaN or infinity")
    return a
def decode(name):
    n=pixels(name)[:,:,:3]*2-1
    return n/np.maximum(np.linalg.norm(n,axis=2,keepdims=True),1e-8)
def seam(a):
    return {"x_edge_mean":float(np.abs(a[:,0]-a[:,-1]).mean()),"y_edge_mean":float(np.abs(a[0,:]-a[-1,:]).mean()),
            "x_interior_mean":float(np.abs(np.diff(a,axis=1)).mean()),"y_interior_mean":float(np.abs(np.diff(a,axis=0)).mean())}
def ratio(s,axis): return s[axis+"_edge_mean"]/max(s[axis+"_interior_mean"],1e-8)
albedo=pixels('RoadV4_RoadAlbedo.png',False)[:,:,:3]
stone=pixels('RoadV4_StoneAlbedo.png',False)[:,:,:3]
dark=pixels('RoadV4_DarkEarthAlbedo.png',False)[:,:,:3]
normal=decode('RoadV4_Normal.png')
height=pixels('RoadV4_Height.exr')[:,:,0]
ao=pixels('RoadV4_AO.png')[:,:,0]
mask=pixels('RoadV4_URP_MaskMap.png')
triplet=np.concatenate((albedo,stone,dark),axis=2); linear=np.where(triplet<=.04045,triplet/12.92,((triplet+.055)/1.055)**2.4)
palettes=[[(.40,.205,.095),(.30,.285,.245),(.19,.085,.043)],[(.39,.19,.085),(.32,.31,.275),(.18,.078,.04)],[(.36,.17,.075),(.29,.265,.225),(.175,.078,.04)]]
signatures=np.asarray([[pal[mi] for pal in palettes] for mi in range(3)],np.float32).reshape(3,9)
class_ids=np.argmin(np.linalg.norm(linear[:,:,None,:]-signatures[None,None,:,:],axis=3),axis=2)
tile_preview=albedo[::4,::4,:]
preview=np.tile(tile_preview,(3,3,1)); preview_image=bpy.data.images.new("RoadV4_3x3_PrivatePreview",width=preview.shape[1],height=preview.shape[0],alpha=True)
preview_image.colorspace_settings.name='sRGB'; preview_image.pixels.foreach_set(np.concatenate((preview,np.ones((*preview.shape[:2],1),np.float32)),axis=2).reshape(-1))
preview_image.filepath_raw=PREVIEW; preview_image.file_format='PNG'; preview_image.save()
d=TILE/(SIZE-1); gy,gx=np.gradient(height,d,d)
surface_valid=(normal[:,:,2]>.25)&(np.abs(gx)<.8)&(np.abs(gy)<.8)
valid=(normal[:,:,2]>.82)&(np.abs(gx)<.45)&(np.abs(gy)<.45)
nx=normal[:,:,0]/np.maximum(normal[:,:,2],1e-8); ny=normal[:,:,1]/np.maximum(normal[:,:,2],1e-8)
cx=float(np.corrcoef(nx[valid].ravel(),-gx[valid].ravel())[0,1])
cy=float(np.corrcoef(ny[valid].ravel(),-gy[valid].ravel())[0,1])
surfacecx=float(np.corrcoef(nx[surface_valid].ravel(),-gx[surface_valid].ravel())[0,1]); surfacecy=float(np.corrcoef(ny[surface_valid].ravel(),-gy[surface_valid].ravel())[0,1])
allcx=float(np.corrcoef(nx.ravel(),-gx.ravel())[0,1]); allcy=float(np.corrcoef(ny.ravel(),-gy.ravel())[0,1])
expected=np.stack((-gx,-gy,np.ones_like(gx)),axis=2); expected/=np.maximum(np.linalg.norm(expected,axis=2,keepdims=True),1e-8)
angle=np.degrees(np.arccos(np.clip(np.sum(expected*normal,axis=2),-1,1)))
signs={"x_neg":float(np.corrcoef(nx[valid].ravel(),-gx[valid].ravel())[0,1]),"x_pos":float(np.corrcoef(nx[valid].ravel(),gx[valid].ravel())[0,1]),"y_neg":float(np.corrcoef(ny[valid].ravel(),-gy[valid].ravel())[0,1]),"y_pos":float(np.corrcoef(ny[valid].ravel(),gy[valid].ravel())[0,1])}
earth=albedo; earth_linear=np.where(earth<=.04045,earth/12.92,((earth+.055)/1.055)**2.4); palette=np.array([[.40,.205,.095],[.30,.285,.245],[.19,.085,.043]],np.float32); ids=np.argmin(np.linalg.norm(earth_linear[:,:,None,:]-palette[None,None,:,:],axis=3),axis=2); bedvalid=surface_valid&(ids==0)
bedcorr={"x":float(np.corrcoef(nx[bedvalid].ravel(),-gx[bedvalid].ravel())[0,1]),"y":float(np.corrcoef(ny[bedvalid].ravel(),-gy[bedvalid].ravel())[0,1]),"pixels":int(bedvalid.sum())}
metrics={"resolution_px":[SIZE,SIZE],"height_minmax_m":[float(height.min()),float(height.max())],
         "visible_material_coverage":{"stone":float(np.mean(class_ids==1)),"darkearth":float(np.mean(class_ids==2)),"earth":float(np.mean(class_ids==0))},
         "normal_height_orientation_corr":{"strict_interior_blender_x_to_terrain_x":cx,"strict_interior_blender_y_to_terrain_z":cy,"strict_interior_sample_count":int(valid.sum()),"surface_valid_blender_x_to_terrain_x":surfacecx,"surface_valid_blender_y_to_terrain_z":surfacecy,"surface_valid_sample_count":int(surface_valid.sum()),"unfiltered_blender_x_to_terrain_x":allcx,"unfiltered_blender_y_to_terrain_z":allcy,"unfiltered_sample_count":int(height.size),"surface_valid_angular_error_degrees_p50_p95":[float(x) for x in np.percentile(angle[surface_valid],[50,95])]},
         "normal_height_sign_diagnostics":signs,"earth_classified_normal_height_corr":bedcorr,
         "seams":{"albedo":seam(albedo),"normal":seam(normal),"height":seam(height),"ao":seam(ao),"mask":seam(mask)},
         "edge_to_interior_ratios":{"normal_x":ratio(seam(normal),"x"),"normal_y":ratio(seam(normal),"y"),"height_x":ratio(seam(height),"x"),"height_y":ratio(seam(height),"y"),"albedo_x":ratio(seam(albedo),"x"),"albedo_y":ratio(seam(albedo),"y")},
         "map_stats":{"road_albedo_rgb_std":float(albedo.std()),"stone_albedo_rgb_std":float(stone.std()),"darkearth_albedo_rgb_std":float(dark.std()),"normal_invalid_fraction":float(np.mean(normal[:,:,2]<.25),),"ao_minmax_std":[float(ao.min()),float(ao.max()),float(ao.std())],"mask_channel_minmax":[[float(mask[:,:,k].min()),float(mask[:,:,k].max())] for k in range(4)]}}
os.makedirs(os.path.dirname(REPORT),exist_ok=True)
with open(REPORT,'w',encoding='utf-8') as f: json.dump(metrics,f,indent=2)
print(json.dumps(metrics,indent=2))
