using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>Registers the excavation fixture before PlayMode initializes and restores build settings after the run.</summary>
public sealed class ExcavationTestSceneScope : IPrebuildSetup, IPostBuildCleanup
{
    public const string ScenePath = "Assets/Tests/Fixtures/TutorialTerrainStage02.unity";
    private const string WorkingScenePath = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";

#if UNITY_EDITOR
    private const string BackupRelativePath = "Library/ExcavationTests/BuildSettingsBackup.json";
    private static bool preparedInCurrentDomain;

    [Serializable]
    private sealed class SceneEntry
    {
        public string path;
        public bool enabled;
    }

    [Serializable]
    private sealed class BuildSettingsBackup
    {
        public string projectRoot;
        public SceneEntry[] original;
        public SceneEntry[] registered;
    }

    public void Setup()
    {
        if (preparedInCurrentDomain)
        {
            BuildSettingsBackup existing = ReadBackup();
            if (existing == null || !Matches(CurrentEntries(), existing.registered))
                throw new InvalidOperationException("Repeated excavation test setup found an inconsistent registration backup.");
            return;
        }

        RestoreStaleBackupIfSafe();
        SceneEntry[] original = CurrentEntries();
        string canonicalStageName = Path.GetFileNameWithoutExtension(ScenePath);
        SceneEntry[] enabledCanonicalStageEntries = original.Where(scene => scene.enabled &&
            Path.GetFileNameWithoutExtension(scene.path) == canonicalStageName).ToArray();
        if (enabledCanonicalStageEntries.Length != 1 || enabledCanonicalStageEntries[0].path != WorkingScenePath)
            throw new InvalidOperationException(
                "Expected exactly one enabled canonical Stage02 entry in normal Build Settings, pointing to the working scene before replacement.");
        int[] workingSceneIndices = original.Select((scene, index) => new { scene, index })
            .Where(item => item.scene.path == WorkingScenePath).Select(item => item.index).ToArray();
        if (workingSceneIndices.Length != 1 || !original[workingSceneIndices[0]].enabled)
            throw new InvalidOperationException("Expected exactly one enabled working Stage02 scene in normal Build Settings before test replacement.");
        if (original.Any(scene => scene.path == ScenePath))
            throw new InvalidOperationException("The dedicated Stage02 test fixture is already present in normal Build Settings.");

        int workingSceneIndex = workingSceneIndices[0];
        SceneEntry[] registered = original.Select((scene, index) => index == workingSceneIndex
            ? new SceneEntry { path = ScenePath, enabled = true }
            : scene).ToArray();
        BuildSettingsBackup backup = new()
        {
            projectRoot = Path.GetFullPath(Directory.GetCurrentDirectory()),
            original = original,
            registered = registered
        };
        WriteBackup(backup);

        try
        {
            EditorBuildSettings.scenes = ToEditorScenes(registered);
            if (!Matches(CurrentEntries(), registered))
                throw new InvalidOperationException("Unity did not retain the temporary excavation test scene in Editor Build Settings.");
            SceneEntry[] current = CurrentEntries();
            if (current.Count(scene => scene.path == ScenePath && scene.enabled) != 1 ||
                current.Any(scene => scene.path == WorkingScenePath))
                throw new InvalidOperationException("Temporary Stage02 registration must contain exactly the fixture and exclude the working scene.");
            preparedInCurrentDomain = true;
        }
        catch
        {
            RestoreSnapshotAndDeleteBackup(backup);
            throw;
        }
    }

    public void Cleanup()
    {
        BuildSettingsBackup backup = ReadBackup();
        if (backup == null)
        {
            preparedInCurrentDomain = false;
            return;
        }
        RestoreSnapshotAndDeleteBackup(backup);
        preparedInCurrentDomain = false;
    }

    /// <summary>Explicitly recover a leftover test backup when current settings still match its known state.</summary>
    public static void RestoreForRecovery()
    {
        RestoreStaleBackupIfSafe();
        preparedInCurrentDomain = false;
    }

    private static void RestoreStaleBackupIfSafe()
    {
        BuildSettingsBackup backup = ReadBackup();
        if (backup != null) RestoreSnapshotAndDeleteBackup(backup);
    }

    private static void RestoreSnapshotAndDeleteBackup(BuildSettingsBackup backup)
    {
        ValidateProject(backup);
        SceneEntry[] current = CurrentEntries();
        bool matchesOriginal = Matches(current, backup.original);
        bool matchesRegistered = Matches(current, backup.registered);
        if (!matchesOriginal && !matchesRegistered)
            throw new InvalidOperationException(
                "Build Settings changed outside the excavation test registration; refusing to overwrite them. " +
                "The recovery backup was preserved at " + Path.GetFullPath(BackupRelativePath));

        if (!matchesOriginal) EditorBuildSettings.scenes = ToEditorScenes(backup.original);
        if (!Matches(CurrentEntries(), backup.original))
            throw new InvalidOperationException("Unity did not restore the exact original Editor Build Settings array.");

        string path = Path.GetFullPath(BackupRelativePath);
        if (File.Exists(path)) File.Delete(path);
    }

    private static SceneEntry[] CurrentEntries() => EditorBuildSettings.scenes
        .Select(scene => new SceneEntry { path = scene.path, enabled = scene.enabled }).ToArray();

    private static EditorBuildSettingsScene[] ToEditorScenes(SceneEntry[] entries) =>
        entries.Select(entry => new EditorBuildSettingsScene(entry.path, entry.enabled)).ToArray();

    private static bool Matches(SceneEntry[] left, SceneEntry[] right) => left != null && right != null &&
        left.Length == right.Length && left.Where((scene, index) =>
            scene.path != right[index].path || scene.enabled != right[index].enabled).Any() == false;

    private static string BackupPath => Path.GetFullPath(BackupRelativePath);

    private static BuildSettingsBackup ReadBackup()
    {
        if (!File.Exists(BackupPath)) return null;
        BuildSettingsBackup backup = JsonUtility.FromJson<BuildSettingsBackup>(File.ReadAllText(BackupPath));
        ValidateProject(backup);
        return backup;
    }

    private static void WriteBackup(BuildSettingsBackup backup)
    {
        string path = BackupPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(backup, true));
        if (!File.Exists(path)) throw new IOException("Failed to persist the excavation Build Settings recovery backup.");
    }

    private static void ValidateProject(BuildSettingsBackup backup)
    {
        if (backup == null || backup.original == null || backup.registered == null ||
            !string.Equals(Path.GetFullPath(backup.projectRoot), Path.GetFullPath(Directory.GetCurrentDirectory()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The excavation Build Settings recovery backup is malformed or belongs to another project.");
    }
#else
    public void Setup() => throw new NotSupportedException("Excavation scene registration is available only for Editor PlayMode tests.");
    public void Cleanup() { }
#endif
}
