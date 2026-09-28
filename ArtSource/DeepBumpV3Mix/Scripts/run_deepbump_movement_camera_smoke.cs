var player = UnityEngine.GameObject.Find("PlayerNew");
var controller = player.GetComponent<UnityEngine.CharacterController>();
var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("DeepBumpMovementSmokeKeyboard");
var camera = UnityEngine.Camera.main;
var startPosition = player.transform.position;
var startGrounded = controller.isGrounded;
var cameraActive = camera != null && camera.enabled && camera.gameObject.activeInHierarchy;
System.Collections.IEnumerator Probe() {
    try {
        UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
        for (var i = 0; i < 90; i++) yield return null;
        UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
        for (var i = 0; i < 5; i++) yield return null;
        var delta = UnityEngine.Vector3.Distance(startPosition, player.transform.position);
        var json = "{\"moved\":" + (delta > 0.25f).ToString().ToLowerInvariant() + ",\"groundedAtStart\":" + startGrounded.ToString().ToLowerInvariant() + ",\"groundedAtEnd\":" + controller.isGrounded.ToString().ToLowerInvariant() + ",\"mainCameraActive\":" + cameraActive.ToString().ToLowerInvariant() + ",\"positionDelta\":" + delta.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}";
        System.IO.File.WriteAllText(System.IO.Path.GetFullPath("Artifacts/DeepBumpF9/movement-camera-smoke.json"), json, new System.Text.UTF8Encoding(false));
        UnityEngine.Debug.Log("DeepBump movement-camera smoke: " + json);
    } finally {
        try { UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState()); UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard); } catch { }
    }
}
player.GetComponent<StarterAssets.FirstPersonController>().StartCoroutine(Probe());
System.IO.File.WriteAllText(System.IO.Path.GetFullPath("Artifacts/DeepBumpF9/movement-camera-smoke.json"), "{\"status\":\"running\"}", new System.Text.UTF8Encoding(false));
