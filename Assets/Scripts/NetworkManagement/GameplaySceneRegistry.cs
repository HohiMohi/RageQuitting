public static class GameplaySceneRegistry
{
    public const string FppSceneName = "FPP_scene";
    public const string TutorialSceneName = "Tutorial_scene";

    public static bool IsGameplayScene(string sceneName)
    {
        return sceneName == FppSceneName || sceneName == TutorialSceneName;
    }
}
