using System;

namespace RageQuitting.Editor.AgentHarness
{
    [Serializable]
    internal sealed class ValidationOpenSceneContext
    {
        public int handle;
        public string name;
        public string path;
        public bool isLoaded;
        public bool isDirty;
        public bool isActive;
    }

    [Serializable]
    internal sealed class ValidationEditorContext
    {
        public int schemaVersion;
        public string unityVersion;
        public string projectPath;
        public string activeSceneName;
        public string activeScenePath;
        public bool activeSceneIsDirty;
        public ValidationOpenSceneContext[] openScenes;
        public bool isCompiling;
        public bool isUpdating;
        public string playModeState;
        public bool isPlaying;
        public bool isPlayingOrWillChangePlaymode;
        public bool pipelinePackagePresent;
        public string pipelinePackageVersion;
        public bool hasRelevantUnsavedSceneState;
    }

    [Serializable]
    internal sealed class ValidationHarnessLaunchResult
    {
        public string reportPath;
        public string summaryPath;
        public string status;
        public int exitCode;
        public string resolvedTier;
    }
}
