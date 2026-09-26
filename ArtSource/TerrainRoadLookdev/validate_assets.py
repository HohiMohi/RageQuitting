import bpy, os, json

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = r"D:\Programy\UnityProjects\RageQuitting\Assets\Art\Environment\TerrainRoadLookdev"
blend = os.path.join(ROOT, 'TerrainRoadLookdev.blend')
report_path = os.path.join(ROOT, 'validation_report.json')
with open(report_path, encoding='utf-8') as f: report = json.load(f)

checks = {'blend_reopened': False, 'png_loads': {}, 'fbx_imports': {}}
bpy.ops.wm.open_mainfile(filepath=blend)
checks['blend_reopened'] = len(bpy.data.objects) == 5 and len(bpy.data.images) >= 8
if not checks['blend_reopened']:
    raise RuntimeError('Saved .blend did not reopen with expected objects and images')

pngs = [(name, os.path.join(OUT, name + '.png')) for name in report['texture_checks']]
pngs += [(name, os.path.join(ROOT, name + '.png')) for name in ['Preview_T_Grass_Warm', 'Preview_T_Road_OchreStone', 'Preview_T_Stone_MutedGrey', 'TerrainRoadLookdev_3x3_Montage']]
for name, path in pngs:
    image = bpy.data.images.load(path, check_existing=False)
    checks['png_loads'][name] = {'loaded': image is not None, 'size': list(image.size)}
    expected = [1024, 1024] if name in report['texture_checks'] else [768, 768]
    if image is None or list(image.size) != expected:
        raise RuntimeError('PNG failed to load at expected size: ' + path)
    bpy.data.images.remove(image)

for name in list(report['fbx_bytes']):
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    path = os.path.join(OUT, name)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=False)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if len(meshes) != 1 or len(meshes[0].data.polygons) < 1:
        raise RuntimeError('FBX did not import as one non-empty mesh: ' + path)
    checks['fbx_imports'][name] = {'loaded': True, 'mesh_count': len(meshes), 'vertices': len(meshes[0].data.vertices), 'polygons': len(meshes[0].data.polygons)}

report['file_load_checks'] = checks
with open(report_path, 'w', encoding='utf-8') as f: json.dump(report, f, indent=2)
print('ASSET_LOAD_VALIDATION_COMPLETE', json.dumps(checks, indent=2))
