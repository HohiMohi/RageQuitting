using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// One-shot EditMode/PlayMode diagnostic for the Stage02 loose-soil collection path.
/// It never saves a scene or asset. Runtime-only changes are removed by unloading Stage02.
/// </summary>
[InitializeOnLoad]
internal static class LooseSoilPerformanceProbe
{
    private const string ArmedKey = "LooseSoilPerformanceProbe.Armed";
    private const string Stage02Path = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";
    private const string ExpectedProjectRoot = @"D:\Programy\UnityProjects\RageQuitting";
    private const string ReportPath = "Artifacts/LooseSoilStability_20261027/phase-final-offline.json";
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static AsyncOperation loadOperation;
    private static AsyncOperation unloadOperation;
    private static Scene probeScene;
    private static string priorActiveScenePath;
    private static int readyFrame;
    private static bool cleanupStarted;
    [Serializable]
    private sealed class SampleStats
    {
        public string name;
        public int count;
        public double medianMs;
        public double p95Ms;
        public double maxMs;
        public double meanAllocatedBytes;
        public bool stoppedAtElapsedLimit;
    }

    [Serializable]
    private sealed class ProbeReport
    {
        public string unityVersion;
        public string scenePath;
        public bool networkSessionActive;
        public int gameplayManagerRequirementSubscribers;
        public float initialSoilUnits;
        public int heightFieldColumns;
        public int heightFieldRows;
        public int bucketCapacity;
        public float successfulCollectionUnits;
        public float soilUnitsAfterCollection;
        public bool constructionBookActive;
        public bool requirementsUiActive;
        public bool allocatedBytesCounterReturnedData;
        public string measurementContext;
        public SampleStats collectEmptyBucket;
        public SampleStats collectFullBucket;
        public SampleStats collectOutsideField;
        public SampleStats rebuildMesh;
        public SampleStats captureSnapshot;
        public SampleStats bridgeRefresh;
        public SampleStats managerNotification;
        public string status;
        public string error;
    }

    static LooseSoilPerformanceProbe()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        if (SessionState.GetBool(ArmedKey, false) && EditorApplication.isPlaying)
            EditorApplication.delayCall += BeginStageLoad;
    }

    [MenuItem("Tools/Diagnostics/Run Stage02 Loose Soil Performance Probe")]
    private static void Start()
    {
        if (!IsExactProject() || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || SessionState.GetBool(ArmedKey, false))
        {
            Debug.LogWarning("Loose-soil performance probe aborted: wrong project, Play Mode/compilation active, or probe already armed.");
            return;
        }

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.IsValid() && scene.isLoaded && scene.isDirty)
            {
                Debug.LogWarning("Loose-soil performance probe aborted because an open scene has unsaved changes: " + scene.path);
                return;
            }
        }

        if (EditorApplication.isCompiling)
        {
            Debug.LogWarning("Loose-soil performance probe aborted because scripts are compiling.");
            return;
        }

        priorActiveScenePath = SceneManager.GetActiveScene().path;
        SessionState.SetString("LooseSoilPerformanceProbe.PriorScene", priorActiveScenePath);
        SessionState.SetBool(ArmedKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ArmedKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
            EditorApplication.delayCall += BeginStageLoad;
        else if (state == PlayModeStateChange.ExitingPlayMode && !cleanupStarted)
        {
            SessionState.SetBool(ArmedKey, false);
            EditorApplication.update -= WaitForStageLoad;
            EditorApplication.update -= WaitForFrames;
            EditorApplication.update -= WaitForStageUnload;
        }
    }

    private static void BeginStageLoad()
    {
        if (!SessionState.GetBool(ArmedKey, false) || !EditorApplication.isPlaying) return;
        try
        {
            priorActiveScenePath = SessionState.GetString("LooseSoilPerformanceProbe.PriorScene", string.Empty);
            loadOperation = SceneManager.LoadSceneAsync(Stage02Path, LoadSceneMode.Additive);
            if (loadOperation == null) throw new InvalidOperationException("Unity did not start the additive Stage02 load.");
            EditorApplication.update += WaitForStageLoad;
        }
        catch (Exception exception)
        {
            FinishWithError(exception);
        }
    }

    private static void WaitForStageLoad()
    {
        if (loadOperation == null || !loadOperation.isDone) return;
        EditorApplication.update -= WaitForStageLoad;
        try
        {
            probeScene = SceneManager.GetSceneByPath(Stage02Path);
            if (!probeScene.IsValid() || !probeScene.isLoaded)
                throw new InvalidOperationException("Stage02 did not become a loaded scene.");
            SceneManager.SetActiveScene(probeScene);
            readyFrame = Time.frameCount + 5;
            EditorApplication.update += WaitForFrames;
        }
        catch (Exception exception)
        {
            FinishWithError(exception);
        }
    }

    private static void WaitForFrames()
    {
        if (Time.frameCount < readyFrame) return;
        EditorApplication.update -= WaitForFrames;
        RunMeasurementsAndCleanup();
    }

    private static void RunMeasurementsAndCleanup()
    {
        var report = new ProbeReport
        {
            unityVersion = Application.unityVersion,
            scenePath = Stage02Path,
            measurementContext = "Offline Stage02 PlayMode with a runtime-created, non-networked bucket; scripted API calls, not player input or host topology.",
            status = "started",
            networkSessionActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
        };
        GameObject bucketRoot = null;
        try
        {
            if (report.networkSessionActive)
                throw new InvalidOperationException("A live NGO session is active; the probe will not alter it.");

            FoundationLooseSoilField field = FindInProbeScene<FoundationLooseSoilField>().FirstOrDefault();
            GameplayManager manager = FindInProbeScene<GameplayManager>().FirstOrDefault();
            ConstructionBookController book = FindInProbeScene<ConstructionBookController>().FirstOrDefault();
            BridgeRequirementsUI requirementsUi = FindInProbeScene<BridgeRequirementsUI>().FirstOrDefault();
            if (field == null || manager == null)
                throw new InvalidOperationException("Stage02 must contain both a loose-soil field and GameplayManager.");
            if (!field.HasHeightField)
                field.Initialize(6.5f, 4f, 0.2f, 0.4f, 0.4f, 6f);
            if (!field.TryGetConstructionSite(out BridgeConstructionSite site) || site == null)
                throw new InvalidOperationException("The authored Stage02 soil field has no valid construction-site association.");
            FoundationExcavationVolume volume = site.ExcavationVolume;
            if (volume == null || !volume.UsesLooseSoilHeightField)
                throw new InvalidOperationException("The Stage02 foundation is not configured to collect loose soil.");

            EnsureGameplaySingleton(manager);
            SetField(site, "currentStage", Enum.Parse(GetField(site, "currentStage").FieldType, "Digging"));
            SetField(site, "diggingSubstage", Enum.Parse(GetField(site, "diggingSubstage").FieldType, "SoilRemoval"));
            SetField(site, "diggingCycleIndex", 0);
            SetField(site, "currentWorkProgress", 0f);
            SetField(site, "removedSoilUnits", 0f);

            Material soilMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ScriptableObjectAssets/New/Substances/LooseSoil.mat");
            field.SetSurfaceState(true, soilMaterial);
            LooseSoilHeightField.Snapshot fullSnapshot = field.CaptureSnapshot();
            if (fullSnapshot == null) throw new InvalidOperationException("Could not capture the initial Stage02 height-field snapshot.");
            report.initialSoilUnits = field.HeightField.TotalUnits;
            report.heightFieldColumns = field.HeightField.Columns;
            report.heightFieldRows = field.HeightField.Rows;
            Delegate requirementEvent = typeof(GameplayManager).GetField("OnBridgeRequirementsChanged", InstanceFlags)?.GetValue(manager) as Delegate;
            report.gameplayManagerRequirementSubscribers = requirementEvent != null ? requirementEvent.GetInvocationList().Length : 0;
            report.constructionBookActive = book != null && book.isActiveAndEnabled;
            report.requirementsUiActive = requirementsUi != null && requirementsUi.isActiveAndEnabled;

            GameObject bucketPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/Substances/PortableBucket.prefab");
            if (bucketPrefab == null) throw new InvalidOperationException("Could not load the authored PortableBucket prefab.");
            bucketRoot = UnityEngine.Object.Instantiate(bucketPrefab);
            bucketRoot.name = "Loose Soil Performance Probe Bucket (runtime only)";
            PortableSubstanceContainer bucket = bucketRoot.GetComponent<PortableSubstanceContainer>();
            if (bucket == null) throw new InvalidOperationException("PortableBucket prefab has no PortableSubstanceContainer.");
            report.bucketCapacity = bucket.Capacity;
            ContainerSubstanceSO soil = AssetDatabase.LoadAssetAtPath<ContainerSubstanceSO>("Assets/ScriptableObjectAssets/New/Substances/Soil.asset");
            if (soil == null) throw new InvalidOperationException("Could not load the authored Soil substance asset.");
            Func<BridgeConstructionSite, PortableSubstanceContainer, ContainerSubstanceSO, Vector3, float, float, float, float> collect =
                (Func<BridgeConstructionSite, PortableSubstanceContainer, ContainerSubstanceSO, Vector3, float, float, float, float>)
                Delegate.CreateDelegate(typeof(Func<BridgeConstructionSite, PortableSubstanceContainer, ContainerSubstanceSO, Vector3, float, float, float, float>), null,
                    typeof(BridgeConstructionSite).GetMethod("TryCollectLooseSoil", InstanceFlags));
            Action<BridgeConstructionSite> notify = (Action<BridgeConstructionSite>)Delegate.CreateDelegate(typeof(Action<BridgeConstructionSite>), manager,
                typeof(GameplayManager).GetMethod("NotifyConstructionSiteStateChanged", InstanceFlags));
            Action refresh = site.GetComponent<BridgeComponent>().RefreshVisualAndColliderState;
            Vector3 collectionPosition = field.transform.TransformPoint(Vector3.zero);

            Action resetState = () =>
            {
                field.RestoreSnapshot(fullSnapshot);
                field.SetSurfaceState(true, soilMaterial);
                SetField(site, "currentStage", Enum.Parse(GetField(site, "currentStage").FieldType, "Digging"));
                SetField(site, "diggingSubstage", Enum.Parse(GetField(site, "diggingSubstage").FieldType, "SoilRemoval"));
                SetField(site, "diggingCycleIndex", 0);
                SetField(site, "currentWorkProgress", 0f);
                SetField(site, "removedSoilUnits", 0f);
            };

            float lastCollectionUnits = 0f;
            report.collectEmptyBucket = Measure("TryCollectLooseSoil: empty bucket, 0.6m brush, 0.05 units", 5, 1.5,
                () => { resetState(); bucket.Empty(); }, () => lastCollectionUnits = collect(site, bucket, soil, collectionPosition, 0.6f, 0.05f, 0.05f));
            report.successfulCollectionUnits = lastCollectionUnits;
            report.soilUnitsAfterCollection = field.HeightField.TotalUnits;
            resetState(); bucket.TryAddUnits(soil, bucket.Capacity);
            report.collectFullBucket = Measure("TryCollectLooseSoil: full bucket", 10, 0.5,
                () => { resetState(); bucket.Empty(); bucket.TryAddUnits(soil, bucket.Capacity); }, () => collect(site, bucket, soil, collectionPosition, 0.6f, 0.05f, 0.05f));
            report.collectOutsideField = Measure("TryCollectLooseSoil: collection position outside field", 10, 0.5,
                () => { resetState(); bucket.Empty(); }, () => collect(site, bucket, soil, collectionPosition + Vector3.up * 10f, 0.6f, 0.05f, 0.05f));
            report.rebuildMesh = Measure("FoundationLooseSoilField.RebuildMesh", 20, 0.5, () => { }, field.RebuildMesh);
            report.captureSnapshot = Measure("LooseSoilHeightField.CaptureSnapshot", 20, 0.5, () => { }, () => field.CaptureSnapshot());
            report.bridgeRefresh = Measure("BridgeComponent.RefreshVisualAndColliderState", 20, 0.5, () => { }, refresh);
            report.managerNotification = Measure("GameplayManager.NotifyConstructionSiteStateChanged (subscribers enabled)", 5, 1.5,
                () => resetState(), () => notify(site));
            report.allocatedBytesCounterReturnedData = report.collectEmptyBucket.meanAllocatedBytes > 0 ||
                report.collectFullBucket.meanAllocatedBytes > 0 || report.rebuildMesh.meanAllocatedBytes > 0 ||
                report.captureSnapshot.meanAllocatedBytes > 0;
            report.status = "completed; offline PlayMode profile; Stage02 additive scene unloaded without saving";
        }
        catch (Exception exception)
        {
            report.status = "failed; cleanup attempted; no scene or asset save requested";
            report.error = exception.ToString();
        }
        finally
        {
            if (bucketRoot != null) UnityEngine.Object.Destroy(bucketRoot);
            try
            {
                string fullReportPath = Path.GetFullPath(ReportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath));
                File.WriteAllText(fullReportPath, JsonUtility.ToJson(report, true));
                UnityEngine.Debug.Log("Loose-soil performance probe report written to " + ReportPath + "\n" + JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError("Could not write loose-soil performance report: " + exception);
            }
            BeginStageUnload();
        }
    }

    private static SampleStats Measure(string name, int count, double elapsedLimitSeconds, Action prepare, Action action)
    {
        var values = new List<double>(count);
        long allocatedBytes = 0;
        bool limitReached = false;
        for (int i = 0; i < count; i++)
        {
            prepare();
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            values.Add(stopwatch.Elapsed.TotalMilliseconds);
            if (stopwatch.Elapsed.TotalSeconds >= elapsedLimitSeconds)
            {
                limitReached = i + 1 < count;
                break;
            }
        }
        values.Sort();
        int actualCount = values.Count;
        return new SampleStats
        {
            name = name,
            count = actualCount,
            medianMs = actualCount == 0 ? 0 : values[actualCount / 2],
            p95Ms = actualCount == 0 ? 0 : values[Math.Max(0, (int)Math.Ceiling(actualCount * 0.95) - 1)],
            maxMs = actualCount == 0 ? 0 : values[actualCount - 1],
            meanAllocatedBytes = actualCount == 0 ? 0 : allocatedBytes / (double)actualCount,
            stoppedAtElapsedLimit = limitReached
        };
    }

    private static IEnumerable<T> FindInProbeScene<T>() where T : Component =>
        probeScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

    private static bool IsExactProject()
    {
        string projectRoot = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string expected = Path.GetFullPath(ExpectedProjectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(projectRoot, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static FieldInfo GetField(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException(target.GetType().FullName, name);
    }

    private static void SetField(object target, string name, object value) => GetField(target, name).SetValue(target, value);

    private static void EnsureGameplaySingleton(GameplayManager manager)
    {
        if (GameplayManager.Instance == manager) return;
        FieldInfo singleton = typeof(GameplayManager).GetField("<Instance>k__BackingField", InstanceFlags);
        if (singleton == null) throw new MissingFieldException(typeof(GameplayManager).FullName, "<Instance>k__BackingField");
        singleton.SetValue(null, manager);
    }

    private static void FinishWithError(Exception exception)
    {
        var report = new ProbeReport
        {
            unityVersion = Application.unityVersion,
            scenePath = Stage02Path,
            status = "failed before measurement; cleanup attempted",
            error = exception.ToString()
        };
        try
        {
            string fullReportPath = Path.GetFullPath(ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath));
            File.WriteAllText(fullReportPath, JsonUtility.ToJson(report, true));
        }
        catch { }
        Debug.LogError("Loose-soil performance probe failed: " + exception);
        BeginStageUnload();
    }

    private static void BeginStageUnload()
    {
        if (cleanupStarted) return;
        cleanupStarted = true;
        if (probeScene.IsValid() && probeScene.isLoaded)
            unloadOperation = SceneManager.UnloadSceneAsync(probeScene);
        if (unloadOperation != null)
            EditorApplication.update += WaitForStageUnload;
        else
            FinishCleanup();
    }

    private static void WaitForStageUnload()
    {
        if (unloadOperation != null && !unloadOperation.isDone) return;
        EditorApplication.update -= WaitForStageUnload;
        FinishCleanup();
    }

    private static void FinishCleanup()
    {
        Scene prior = SceneManager.GetSceneByPath(priorActiveScenePath);
        if (prior.IsValid() && prior.isLoaded) SceneManager.SetActiveScene(prior);
        SessionState.SetBool(ArmedKey, false);
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }
}
