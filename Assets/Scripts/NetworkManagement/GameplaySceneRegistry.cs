public static class GameplaySceneRegistry
{
    public const string FppSceneName = "FPP_scene";
    public const string TutorialSceneName = "Tutorial_scene";
    public const string TutorialTerrainStage02SceneName = "TutorialTerrainStage02";
    public const string TutorialTerrainStage02CopySceneName = "TutorialTerrainStage02_Copy";
    public const string TutorialDeckAreaV1SceneName = "TutorialDeckAreaV1";

    public static bool IsGameplayScene(string sceneName)
    {
        return sceneName == FppSceneName || sceneName == TutorialSceneName || sceneName == TutorialTerrainStage02SceneName
            || sceneName == TutorialTerrainStage02CopySceneName
            || sceneName == TutorialDeckAreaV1SceneName;
    }
}
