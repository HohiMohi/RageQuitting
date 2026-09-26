"""Read-only bake QA. Run using Blender 5.1.2 --background --python <this file>."""
import bpy
import json
import os
import numpy as np

ROOT=r"D:\Programy\UnityProjects\RageQuitting"
OUT=os.path.join(ROOT,"Assets","Art","Environment","TerrainRoadLookdev","SurfaceV3")
REPORT=os.path.join(ROOT,"Artifacts","TerrainRoadV3","TerrainRoadV3_AssetMetrics.json")
SIZE=2048
TILE=4.0

def pixels(filename, noncolor=True):
    im=bpy.data.images.load(os.path.join(OUT,filename),check_existing=False)
    if noncolor: im.colorspace_settings.name='Non-Color'
    return np.asarray(im.pixels[:],dtype=np.float32).reshape(SIZE,SIZE,4)

def decode_normal(filename):
    raw=pixels(filename)[:,:,:3]*2.0-1.0
    return raw/np.maximum(np.linalg.norm(raw,axis=2,keepdims=True),1e-8)

metrics=json.load(open(REPORT,encoding='utf-8'))
normal_b=decode_normal("RoadV3_B_Normal.png")
normal_a=decode_normal("RoadV3_A_Normal.png")
height_b=pixels("RoadV3_B_Height.exr")[:,:,0]
height_a=pixels("RoadV3_A_Height.exr")[:,:,0]
valid=(normal_b[:,:,2]>.25)&(normal_a[:,:,2]>.25)
slope_b=np.sqrt(normal_b[:,:,0]**2+normal_b[:,:,1]**2)/np.maximum(normal_b[:,:,2],1e-8)
slope_a=np.sqrt(normal_a[:,:,0]**2+normal_a[:,:,1]**2)/np.maximum(normal_a[:,:,2],1e-8)
metrics["normal_geometric_slope_ratio"]={
    "baked_tangent_slope_median_A_to_B":float(np.median(slope_a[valid])/max(np.median(slope_b[valid]),1e-8)),
    "baked_slope_p50_p95_B":[float(x) for x in np.percentile(slope_b[valid],[50,95])],
    "baked_slope_p50_p95_A":[float(x) for x in np.percentile(slope_a[valid],[50,95])],
    "source_z_A_B_half_error_m":float(metrics["source_z_A_B_half_error_m"])
}
metrics["normal_baked_tilt_degrees"]={
    "B_p50_p95_p99":[float(x) for x in np.percentile(np.degrees(np.arctan(slope_b[valid])),[50,95,99])],
    "A_p50_p95_p99":[float(x) for x in np.percentile(np.degrees(np.arctan(slope_a[valid])),[50,95,99])],
    "A_to_B_median_ratio":float(np.median(np.degrees(np.arctan(slope_a[valid])))/max(np.median(np.degrees(np.arctan(slope_b[valid]))),1e-8))
}
height_error=height_a-.5*height_b
metrics["baked_height_A_B_half_error"]={"max_abs_m":float(np.max(np.abs(height_error))),"rms_m":float(np.sqrt(np.mean(height_error**2)))}

def orientation(height, normal):
    d=TILE/(SIZE-1)
    gy,gx=np.gradient(height,d,d)
    baked_x=normal[:,:,0]/np.maximum(normal[:,:,2],1e-8)
    baked_y=normal[:,:,1]/np.maximum(normal[:,:,2],1e-8)
    mask=(normal[:,:,2]>.25)&(np.abs(gx)<.8)&(np.abs(gy)<.8)
    def corr(a,b):
        av=a[mask].astype(np.float64); bv=b[mask].astype(np.float64)
        return float(np.corrcoef(av,bv)[0,1])
    return {"samples":int(mask.sum()),"corr_baked_x_vs_negative_height_dx":corr(baked_x,-gx),
            "corr_baked_y_vs_negative_height_dy":corr(baked_y,-gy),
            "corr_baked_y_vs_positive_height_dy":corr(baked_y,gy)}

metrics["normal_height_orientation"]={"B":orientation(height_b,normal_b),"A":orientation(height_a,normal_a)}
with open(REPORT,"w",encoding="utf-8") as f: json.dump(metrics,f,indent=2)
print(json.dumps({"normal_geometric_slope_ratio":metrics["normal_geometric_slope_ratio"],
                  "normal_height_orientation":metrics["normal_height_orientation"]},indent=2))
