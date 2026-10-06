public static class GameplaySceneRegistry
{
    public const string FppSceneName = "FPP_scene";
    public const string TutorialSceneName = "Tutorial_scene";
    public const string TutorialTerrainStage02SceneName = "TutorialTerrainStage02";

    public static bool IsGameplayScene(string sceneName)
    {
        return sceneName == FppSceneName || sceneName == TutorialSceneName || sceneName == TutorialTerrainStage02SceneName;
    }
}
