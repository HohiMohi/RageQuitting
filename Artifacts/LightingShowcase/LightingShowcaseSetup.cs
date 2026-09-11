const string urpSource = "Assets/StarterAssets/Environment/RenderPipelineProfiles/StarterAssetsURPAsset.asset";
const string rendererSource = "Assets/StarterAssets/Environment/RenderPipelineProfiles/StarterAssetsURPRenderer.asset";
const string urpPath = "Assets/Settings/Rendering/ShowcaseURPAsset.asset";
const string rendererPath = "Assets/Settings/Rendering/ShowcaseUniversalRenderer.asset";
const string lightingPath = "Assets/Settings/Lighting/Tutorial_Afternoon_LightingSettings.lighting";
const string bakingSetPath = "Assets/Settings/Lighting/Tutorial_Afternoon_ProbeVolumeBakingSet.asset";
const string envProfilePath = "Assets/ScriptableObjectAssets/New/Lighting/FairyAfternoonEnvironmentLightingProfile.asset";
const string skyPath = "Assets/Materials/Lighting/FairyAfternoonProceduralSkybox.mat";
const string volumePath = "Assets/Settings/Lighting/FairyAfternoonVolumeProfile.asset";
const string scenePath = "Assets/Scenes/Tutorial_scene.unity";

void EnsureFolder(string path)
{
    var parts = path.Split('/');
    var current = parts[0];
    for (var i = 1; i < parts.Length; i++)
    {
        var next = current + "/" + parts[i];
        if (!AssetDatabase.IsValidFolder(next))
            AssetDatabase.CreateFolder(current, parts[i]);
        current = next;
    }
}

void SetInt(UnityEditor.SerializedObject so, string name, int value)
{
    var p = so.FindProperty(name) ?? throw new InvalidOperationException($"Missing serialized property {name} on {so.targetObject.name}");
    p.intValue = value;
}

void SetBool(UnityEditor.SerializedObject so, string name, bool value)
{
    var p = so.FindProperty(name) ?? throw new InvalidOperationException($"Missing serialized property {name} on {so.targetObject.name}");
    p.boolValue = value;
}

void SetFloat(UnityEditor.SerializedObject so, string name, float value)
{
    var p = so.FindProperty(name) ?? throw new InvalidOperationException($"Missing serialized property {name} on {so.targetObject.name}");
    p.floatValue = value;
}

void SetObject(UnityEditor.SerializedObject so, string name, UnityEngine.Object value)
{
    var p = so.FindProperty(name) ?? throw new InvalidOperationException($"Missing serialized property {name} on {so.targetObject.name}");
    p.objectReferenceValue = value;
}

EnsureFolder("Assets/Settings/Rendering");
EnsureFolder("Assets/Settings/Lighting");
EnsureFolder("Assets/ScriptableObjectAssets/New/Lighting");
EnsureFolder("Assets/Materials/Lighting");

if (AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(urpPath) == null)
{
    if (!AssetDatabase.CopyAsset(urpSource, urpPath)) throw new InvalidOperationException("Could not copy URP asset.");
}
if (AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(rendererPath) == null)
{
    if (!AssetDatabase.CopyAsset(rendererSource, rendererPath)) throw new InvalidOperationException("Could not copy renderer asset.");
}
AssetDatabase.ImportAsset(urpPath, ImportAssetOptions.ForceUpdate);
AssetDatabase.ImportAsset(rendererPath, ImportAssetOptions.ForceUpdate);

var urp = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(urpPath);
var renderer = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(rendererPath);
urp.name = "Showcase URP Asset";
renderer.name = "Showcase Universal Renderer";

var urpSO = new SerializedObject(urp);
var rendererList = urpSO.FindProperty("m_RendererDataList");
rendererList.arraySize = 1;
rendererList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
SetInt(urpSO, "m_DefaultRendererIndex", 0);
SetBool(urpSO, "m_RequireDepthTexture", true);
SetBool(urpSO, "m_RequireOpaqueTexture", false);
SetBool(urpSO, "m_SupportsHDR", true);
SetInt(urpSO, "m_MSAA", 1);
SetFloat(urpSO, "m_RenderScale", 1f);
SetBool(urpSO, "m_MainLightShadowsSupported", true);
SetInt(urpSO, "m_MainLightShadowmapResolution", 2048);
SetInt(urpSO, "m_AdditionalLightsRenderingMode", 1);
SetBool(urpSO, "m_AdditionalLightShadowsSupported", true);
SetBool(urpSO, "m_ReflectionProbeBlending", true);
SetBool(urpSO, "m_ReflectionProbeBoxProjection", true);
SetInt(urpSO, "m_ShadowCascadeCount", 2);
SetFloat(urpSO, "m_ShadowDistance", 75f);
SetBool(urpSO, "m_SoftShadowsSupported", true);
SetInt(urpSO, "m_ColorGradingMode", (int)UnityEngine.Rendering.Universal.ColorGradingMode.HighDynamicRange);
SetInt(urpSO, "m_ColorGradingLutSize", 32);
SetInt(urpSO, "m_LightProbeSystem", (int)UnityEngine.Rendering.Universal.LightProbeSystem.ProbeVolumes);
SetBool(urpSO, "m_SupportProbeVolumeScenarios", true);
SetBool(urpSO, "m_SupportProbeVolumeScenarioBlending", false);
urpSO.ApplyModifiedPropertiesWithoutUndo();
EditorUtility.SetDirty(urp);

var rendererSO = new SerializedObject(renderer);
SetInt(rendererSO, "m_RenderingMode", (int)UnityEngine.Rendering.Universal.RenderingMode.Forward);
var features = rendererSO.FindProperty("m_RendererFeatures");
var featureMap = rendererSO.FindProperty("m_RendererFeatureMap");
var existingSsao = AssetDatabase.LoadAllAssetsAtPath(rendererPath).OfType<UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion>().ToList();
UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion ssao;
if (existingSsao.Count == 0)
{
    ssao = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion>();
    ssao.name = "Showcase SSAO";
    AssetDatabase.AddObjectToAsset(ssao, renderer);
}
else
{
    ssao = existingSsao[0];
    for (var i = 1; i < existingSsao.Count; i++) UnityEngine.Object.DestroyImmediate(existingSsao[i], true);
}
for (var i = features.arraySize - 1; i >= 0; i--)
{
    var value = features.GetArrayElementAtIndex(i).objectReferenceValue;
    if (value is UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion && value != ssao)
        features.DeleteArrayElementAtIndex(i);
}
var foundSsao = false;
for (var i = 0; i < features.arraySize; i++)
{
    if (features.GetArrayElementAtIndex(i).objectReferenceValue == ssao) foundSsao = true;
}
if (!foundSsao)
{
    features.InsertArrayElementAtIndex(features.arraySize);
    features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = ssao;
}
featureMap.arraySize = features.arraySize;
for (var i = 0; i < features.arraySize; i++)
    featureMap.GetArrayElementAtIndex(i).longValue = features.GetArrayElementAtIndex(i).objectReferenceValue == null ? 0 : (long)Unsupported.GetLocalIdentifierInFileForPersistentObject(features.GetArrayElementAtIndex(i).objectReferenceValue);
rendererSO.ApplyModifiedPropertiesWithoutUndo();
ssao.SetActive(true);
var ssaoSO = new SerializedObject(ssao);
var settings = ssaoSO.FindProperty("m_Settings");
settings.FindPropertyRelative("AOMethod").intValue = 0;
settings.FindPropertyRelative("Downsample").boolValue = false;
settings.FindPropertyRelative("AfterOpaque").boolValue = false;
settings.FindPropertyRelative("Source").intValue = 1;
settings.FindPropertyRelative("NormalSamples").intValue = 1;
settings.FindPropertyRelative("Intensity").floatValue = 1.2f;
settings.FindPropertyRelative("DirectLightingStrength").floatValue = 0.15f;
settings.FindPropertyRelative("Radius").floatValue = 0.035f;
settings.FindPropertyRelative("Samples").intValue = 1;
settings.FindPropertyRelative("BlurQuality").intValue = 0;
ssaoSO.ApplyModifiedPropertiesWithoutUndo();
ssao.Create();
EditorUtility.SetDirty(ssao);
EditorUtility.SetDirty(renderer);

UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = urp;
var originalQuality = QualitySettings.GetQualityLevel();
for (var i = 0; i < QualitySettings.names.Length; i++)
{
    QualitySettings.SetQualityLevel(i, false);
    QualitySettings.renderPipeline = null;
}
QualitySettings.SetQualityLevel(originalQuality, false);

var environmentProfile = AssetDatabase.LoadAssetAtPath<EnvironmentLightingProfile>(envProfilePath);
if (environmentProfile == null)
{
    environmentProfile = ScriptableObject.CreateInstance<EnvironmentLightingProfile>();
    environmentProfile.name = "Fairy Afternoon Environment Lighting Profile";
    AssetDatabase.CreateAsset(environmentProfile, envProfilePath);
}

var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
if (sky == null)
{
    var shader = Shader.Find("Skybox/Procedural") ?? throw new InvalidOperationException("Built-in procedural sky shader not found.");
    sky = new Material(shader) { name = "Fairy Afternoon Procedural Skybox" };
    AssetDatabase.CreateAsset(sky, skyPath);
}
sky.SetColor("_SkyTint", EnvironmentLightingProfile.AfternoonSkyTint);
sky.SetColor("_GroundColor", EnvironmentLightingProfile.AfternoonGroundColor);
sky.SetFloat("_Exposure", EnvironmentLightingProfile.AfternoonSkyExposure);
sky.SetFloat("_AtmosphereThickness", EnvironmentLightingProfile.AfternoonAtmosphereThickness);
sky.SetFloat("_SunSize", 0.035f);
sky.SetFloat("_SunSizeConvergence", 5f);
EditorUtility.SetDirty(sky);

var volumeProfile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(volumePath);
if (volumeProfile == null)
{
    volumeProfile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
    volumeProfile.name = "Fairy Afternoon Volume Profile";
    AssetDatabase.CreateAsset(volumeProfile, volumePath);
}
foreach (var component in volumeProfile.components.ToArray()) volumeProfile.Remove(component.GetType());
var tone = volumeProfile.Add<UnityEngine.Rendering.Universal.Tonemapping>(true); tone.mode.Override(UnityEngine.Rendering.Universal.TonemappingMode.Neutral);
var wb = volumeProfile.Add<UnityEngine.Rendering.Universal.WhiteBalance>(true); wb.temperature.Override(EnvironmentLightingProfile.AfternoonWhiteBalanceTemperature); wb.tint.Override(2f);
var color = volumeProfile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true); color.postExposure.Override(EnvironmentLightingProfile.AfternoonPostExposure); color.contrast.Override(EnvironmentLightingProfile.AfternoonPostContrast); color.saturation.Override(EnvironmentLightingProfile.AfternoonPostSaturation); color.colorFilter.Override(EnvironmentLightingProfile.AfternoonPostColorFilter);
var bloom = volumeProfile.Add<UnityEngine.Rendering.Universal.Bloom>(true); bloom.intensity.Override(EnvironmentLightingProfile.AfternoonBloomIntensity); bloom.threshold.Override(1.05f); bloom.scatter.Override(0.55f); bloom.clamp.Override(65472f); bloom.highQualityFiltering.Override(true);
var vignette = volumeProfile.Add<UnityEngine.Rendering.Universal.Vignette>(true); vignette.intensity.Override(0f); vignette.active = false;
var motion = volumeProfile.Add<UnityEngine.Rendering.Universal.MotionBlur>(true); motion.intensity.Override(0f); motion.active = false;
var dof = volumeProfile.Add<UnityEngine.Rendering.Universal.DepthOfField>(true); dof.active = false;
var ca = volumeProfile.Add<UnityEngine.Rendering.Universal.ChromaticAberration>(true); ca.intensity.Override(0f); ca.active = false;
var lens = volumeProfile.Add<UnityEngine.Rendering.Universal.LensDistortion>(true); lens.intensity.Override(0f); lens.active = false;
EditorUtility.SetDirty(volumeProfile);

var lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(lightingPath);
if (lightingSettings == null)
{
    lightingSettings = new LightingSettings { name = "Tutorial Afternoon Lighting Settings" };
    AssetDatabase.CreateAsset(lightingSettings, lightingPath);
}
lightingSettings.realtimeGI = false;
lightingSettings.bakedGI = true;
lightingSettings.mixedBakeMode = MixedLightingMode.IndirectOnly;
lightingSettings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
lightingSettings.lightmapResolution = 12f;
lightingSettings.lightmapMaxSize = 1024;
lightingSettings.lightmapPadding = 2;
lightingSettings.indirectResolution = 2f;
lightingSettings.maxBounces = 2;
lightingSettings.environmentImportanceSampling = true;
EditorUtility.SetDirty(lightingSettings);
Lightmapping.lightingSettings = lightingSettings;

var bakingSet = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.ProbeVolumeBakingSet>(bakingSetPath);
if (bakingSet == null)
{
    bakingSet = ScriptableObject.CreateInstance<UnityEngine.Rendering.ProbeVolumeBakingSet>();
    bakingSet.name = "Tutorial Afternoon Probe Volume Baking Set";
    AssetDatabase.CreateAsset(bakingSet, bakingSetPath);
}
var bakingSO = new SerializedObject(bakingSet);
var sceneGuids = bakingSO.FindProperty("m_SceneGUIDs");
sceneGuids.ClearArray();
var tutorialGuid = AssetDatabase.AssetPathToGUID(scenePath);
sceneGuids.InsertArrayElementAtIndex(0);
sceneGuids.GetArrayElementAtIndex(0).stringValue = tutorialGuid;
var scenarios = bakingSO.FindProperty("m_LightingScenarios");
scenarios.ClearArray();
scenarios.InsertArrayElementAtIndex(0);
scenarios.GetArrayElementAtIndex(0).stringValue = "Afternoon";
SetBool(bakingSO, "singleSceneMode", true);
SetFloat(bakingSO, "minDistanceBetweenProbes", 1.5f);
SetInt(bakingSO, "simplificationLevels", 3);
SetFloat(bakingSO, "minRendererVolumeSize", 0.5f);
SetBool(bakingSO, "skyOcclusion", false);
bakingSO.FindProperty("lightingScenario").stringValue = "Afternoon";
bakingSO.ApplyModifiedPropertiesWithoutUndo();
EditorUtility.SetDirty(bakingSet);

var existingRoot = GameObject.Find("EnvironmentLightingRoot");
if (existingRoot != null) UnityEngine.Object.DestroyImmediate(existingRoot);
var root = new GameObject("EnvironmentLightingRoot");

var sunGo = new GameObject("Afternoon Sun"); sunGo.transform.SetParent(root.transform); sunGo.transform.rotation = Quaternion.Euler(EnvironmentLightingProfile.AfternoonSunEulerAngles);
var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.color = EnvironmentLightingProfile.AfternoonSunColor; sun.intensity = EnvironmentLightingProfile.AfternoonSunIntensity; sun.shadowStrength = EnvironmentLightingProfile.AfternoonSunShadowStrength; sun.shadows = LightShadows.Soft; sun.lightmapBakeType = LightmapBakeType.Mixed;

var volumeGo = new GameObject("Fairy Afternoon Global Volume"); volumeGo.transform.SetParent(root.transform); volumeGo.layer = LayerMask.NameToLayer("PostProcessing");
var volume = volumeGo.AddComponent<UnityEngine.Rendering.Volume>(); volume.isGlobal = true; volume.priority = 10f; volume.sharedProfile = volumeProfile;

var probeGo = new GameObject("Tutorial APV Probe Volume"); probeGo.transform.SetParent(root.transform); probeGo.transform.position = new Vector3(-5f, 8f, 0f);
var probeVolume = probeGo.AddComponent<UnityEngine.Rendering.ProbeVolume>(); probeVolume.mode = UnityEngine.Rendering.ProbeVolume.Mode.Local; probeVolume.size = new Vector3(90f, 24f, 90f); probeVolume.fillEmptySpaces = true;

ReflectionProbe AddReflection(string name, Vector3 position, Vector3 size)
{
    var go = new GameObject(name); go.transform.SetParent(root.transform); go.transform.position = position;
    var probe = go.AddComponent<ReflectionProbe>(); probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked; probe.resolution = 128; probe.boxProjection = true; probe.blendDistance = 2f; probe.size = size; probe.importance = 1;
    return probe;
}
AddReflection("Camp Reflection Probe", new Vector3(-15f, 3f, -28f), new Vector3(20f, 8f, 18f));
AddReflection("Forge Reflection Probe", new Vector3(-30f, 3f, 10f), new Vector3(16f, 8f, 18f));
AddReflection("Carpenter Reflection Probe", new Vector3(-14f, 3f, 10f), new Vector3(16f, 8f, 18f));
AddReflection("Cave Reflection Probe", new Vector3(-22f, 4f, 29f), new Vector3(18f, 10f, 14f));
AddReflection("River Bridge Reflection Probe", new Vector3(8f, 3f, 0f), new Vector3(24f, 8f, 22f));

var accentLights = new List<Light>();
Light AddAccent(string name, Vector3 position, Color lightColor, float intensity, float range, LightShadows shadows)
{
    var go = new GameObject(name); go.transform.SetParent(root.transform); go.transform.position = position;
    var light = go.AddComponent<Light>(); light.type = LightType.Point; light.color = lightColor; light.intensity = intensity; light.range = range; light.shadows = shadows; light.lightmapBakeType = LightmapBakeType.Mixed;
    accentLights.Add(light); return light;
}
AddAccent("Camp Hearth Accent", new Vector3(-15f, 2.1f, -28f), new Color(1f, 0.45f, 0.18f), 1.15f, 9f, LightShadows.Soft);
AddAccent("Forge Ember Accent", new Vector3(-30f, 2.5f, 10f), new Color(1f, 0.28f, 0.08f), 1.55f, 8f, LightShadows.Soft);
AddAccent("Carpenter Lantern Accent", new Vector3(-14f, 2.6f, 10f), new Color(1f, 0.58f, 0.25f), 0.8f, 7f, LightShadows.None);
AddAccent("Cave Glow Accent", new Vector3(-22f, 3f, 26f), new Color(1f, 0.42f, 0.16f), 1.05f, 9f, LightShadows.Soft);
AddAccent("Bridge Guide Accent", new Vector3(8f, 2.3f, 0f), new Color(1f, 0.66f, 0.32f), 0.65f, 10f, LightShadows.None);

var controller = root.AddComponent<EnvironmentLightingController>();
var controllerSO = new SerializedObject(controller);
SetObject(controllerSO, "profile", environmentProfile);
SetObject(controllerSO, "mainDirectionalLight", sun);
SetObject(controllerSO, "globalVolume", volume);
SetObject(controllerSO, "volumeProfileTemplate", volumeProfile);
SetObject(controllerSO, "proceduralSkyboxTemplate", sky);
SetObject(controllerSO, "timeSourceBehaviour", null);
SetFloat(controllerSO, "manualNormalizedTime", EnvironmentLightingProfile.AfternoonNormalizedTime);
var accents = controllerSO.FindProperty("localAccentLights"); accents.arraySize = accentLights.Count;
for (var i = 0; i < accentLights.Count; i++) accents.GetArrayElementAtIndex(i).objectReferenceValue = accentLights[i];
controllerSO.ApplyModifiedPropertiesWithoutUndo();

var environment = GameObject.Find("TutorialEnvironment");
var excludedNames = new[] { "RiverGameplay", "ToolRack", "Bridge", "Mountable", "Construction", "Gameplay" };
var marked = 0;
if (environment != null)
{
    foreach (var rendererComponent in environment.GetComponentsInChildren<Renderer>(true))
    {
        var go = rendererComponent.gameObject;
        if (excludedNames.Any(n => go.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
        var ancestor = go.transform;
        var dynamic = false;
        while (ancestor != null)
        {
            if (excludedNames.Any(n => ancestor.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) || ancestor.GetComponent<Rigidbody>() != null || ancestor.GetComponent<Animator>() != null || ancestor.GetComponents<Component>().Any(c => c != null && c.GetType().Name == "NetworkObject")) { dynamic = true; break; }
            if (ancestor == environment.transform) break;
            ancestor = ancestor.parent;
        }
        if (dynamic) continue;
        var flags = GameObjectUtility.GetStaticEditorFlags(go) | StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
        GameObjectUtility.SetStaticEditorFlags(go, flags);
        marked++;
    }
}

RenderSettings.sun = sun;
RenderSettings.skybox = sky;
RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
RenderSettings.ambientIntensity = EnvironmentLightingProfile.AfternoonAmbientIntensity;
RenderSettings.fog = true;
RenderSettings.fogMode = FogMode.ExponentialSquared;
RenderSettings.fogColor = EnvironmentLightingProfile.AfternoonFogColor;
RenderSettings.fogDensity = EnvironmentLightingProfile.AfternoonFogDensity;

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
AssetDatabase.SaveAssets();
if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(), scenePath)) throw new InvalidOperationException("Failed to save Tutorial scene.");
AssetDatabase.SaveAssets();
return $"SETUP_OK quality={QualitySettings.GetQualityLevel()} urp={AssetDatabase.GetAssetPath(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline)} features={renderer.rendererFeatures.Count} ssao={renderer.rendererFeatures.Count(f => f is UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion)} scenario={string.Join(",", bakingSet.lightingScenarios)} markedGI={marked} accents={accentLights.Count}";
