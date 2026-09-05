using System.Collections.Generic;
using System.IO;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RageQuitting.Editor.AgentHarness
{
    internal static class ValidationEditorContextService
    {
        public static ValidationEditorContext Capture()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            var scenes = new List<ValidationOpenSceneContext>(SceneManager.sceneCount);
            bool hasDirtyScene = false;

            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                hasDirtyScene |= scene.isDirty;
                scenes.Add(new ValidationOpenSceneContext
                {
                    handle = scene.handle,
                    name = scene.name ?? string.Empty,
                    path = scene.path ?? string.Empty,
                    isLoaded = scene.isLoaded,
                    isDirty = scene.isDirty,
                    isActive = scene == activeScene
                });
            }

            UnityEditor.PackageManager.PackageInfo pipelinePackage = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CliCommandAttribute).Assembly);
            return new ValidationEditorContext
            {
                schemaVersion = 1,
                unityVersion = Application.unityVersion,
                projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                activeSceneName = activeScene.IsValid() ? activeScene.name ?? string.Empty : string.Empty,
                activeScenePath = activeScene.IsValid() ? activeScene.path ?? string.Empty : string.Empty,
                activeSceneIsDirty = activeScene.IsValid() && activeScene.isDirty,
                openScenes = scenes.ToArray(),
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                playModeState = ResolvePlayModeState(),
                isPlaying = EditorApplication.isPlaying,
                isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                pipelinePackagePresent = pipelinePackage != null,
                pipelinePackageVersion = pipelinePackage != null ? pipelinePackage.version : string.Empty,
                hasRelevantUnsavedSceneState = hasDirtyScene
            };
        }

        private static string ResolvePlayModeState()
        {
            if (EditorApplication.isPlaying)
            {
                return EditorApplication.isPaused ? "Paused" : "Playing";
            }

            return EditorApplication.isPlayingOrWillChangePlaymode ? "Changing" : "Editing";
        }
    }
}
