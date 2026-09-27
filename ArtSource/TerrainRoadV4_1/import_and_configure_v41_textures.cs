var root="Assets/Art/Environment/TerrainRoadLookdev/SurfaceV4_1/";
var files=new[]{"RoadV4_1_RoadAlbedo.png","RoadV4_1_StoneAlbedo.png","RoadV4_1_DarkEarthAlbedo.png","RoadV4_1_Normal.png","RoadV4_1_Height.exr","RoadV4_1_AO.png","RoadV4_1_URP_MaskMap.png"};
var imported=new System.Collections.Generic.List<string>();
foreach(var file in files) {
    string path=root+file;
    if(!System.IO.File.Exists(System.IO.Path.Combine(UnityEngine.Application.dataPath,"..",path))) throw new System.Exception("Source texture is missing: "+path);
    UnityEditor.AssetDatabase.ImportAsset(path,UnityEditor.ImportAssetOptions.ForceUpdate|UnityEditor.ImportAssetOptions.ForceSynchronousImport);
    var importer=UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
    if(importer==null) throw new System.Exception("TextureImporter was not created: "+path);
    importer.textureType=file=="RoadV4_1_Normal.png"?UnityEditor.TextureImporterType.NormalMap:UnityEditor.TextureImporterType.Default;
    importer.sRGBTexture=file.EndsWith("Albedo.png",System.StringComparison.Ordinal);
    importer.wrapMode=UnityEngine.TextureWrapMode.Repeat;
    importer.mipmapEnabled=true;
    importer.filterMode=UnityEngine.FilterMode.Trilinear;
    importer.anisoLevel=8;
    importer.textureCompression=UnityEditor.TextureImporterCompression.Uncompressed;
    importer.maxTextureSize=2048;
    importer.SaveAndReimport();
    var texture=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
    if(texture==null||texture.width!=2048||texture.height!=2048) throw new System.Exception("Texture imported at an unexpected size: "+path);
    imported.Add(path);
}
return "Imported and configured seven V4.1 textures: "+string.Join(", ",imported.ToArray());
