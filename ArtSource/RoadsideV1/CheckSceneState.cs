var s=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
return s.path+" dirty="+UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty+" playing="+UnityEditor.EditorApplication.isPlaying;
