using System;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Opens the authored bridge fixture in disposable preview scenes for EditMode tests.</summary>
public sealed class BridgeAssemblyTestFixtureUtility : IDisposable
{
    public const string ProductionScenePath = "Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity";
    public const string FixtureScenePath = "Assets/Tests/Fixtures/MountingNailTestScene.unity";

    public Scene ProductionPreview { get; private set; }
    public Scene FixturePreview { get; private set; }
    public GameObject FixtureRoot { get; private set; }

    public void OpenSeparateProductionPreview()
    {
        if (!ProductionPreview.IsValid())
            ProductionPreview = EditorSceneManager.OpenPreviewScene(ProductionScenePath);
    }

    public BridgeAssemblyTestFixtureUtility(bool includeProductionPreview)
    {
        try
        {
            if (includeProductionPreview)
                ProductionPreview = EditorSceneManager.OpenPreviewScene(ProductionScenePath);

            FixturePreview = EditorSceneManager.OpenPreviewScene(FixtureScenePath);
            GameObject source = FixturePreview.GetRootGameObjects().SingleOrDefault(root => root.name == "NailTestArea");
            if (source == null)
                throw new InvalidOperationException("The dedicated mounting nail test scene has no NailTestArea root.");

            if (includeProductionPreview)
            {
                FixtureRoot = UnityEngine.Object.Instantiate(source);
                FixtureRoot.name = "NailTestArea";
                SceneManager.MoveGameObjectToScene(FixtureRoot, ProductionPreview);
            }
            else
            {
                FixtureRoot = source;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Scene TestScene => ProductionPreview.IsValid() ? ProductionPreview : FixturePreview;

    public void Dispose()
    {
        if (FixtureRoot != null && FixturePreview.IsValid() && ProductionPreview.IsValid())
            UnityEngine.Object.DestroyImmediate(FixtureRoot);
        FixtureRoot = null;

        if (FixturePreview.IsValid() && FixturePreview.isLoaded)
            EditorSceneManager.ClosePreviewScene(FixturePreview);
        if (ProductionPreview.IsValid() && ProductionPreview.isLoaded)
            EditorSceneManager.ClosePreviewScene(ProductionPreview);

        FixturePreview = default;
        ProductionPreview = default;
    }
}
