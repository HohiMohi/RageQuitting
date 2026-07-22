using System.Collections;
using Cinemachine;
using StarterAssets;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PlayerNetworkSetup : NetworkBehaviour
{
    [SerializeField] private GameObject playerBodyVisual;

    private bool setupCompleted;
    private static CinemachineVirtualCamera sharedVirtualCamera;
    private Coroutine delayedCameraBindingCoroutine;

    public GameObject PlayerBodyVisual => playerBodyVisual;

    public void ConfirmInitialSpawnPlacement(Vector3 position, Quaternion rotation)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        transform.SetPositionAndRotation(position, rotation);
        ConfirmInitialSpawnPlacementClientRpc(position, rotation, CreateTargetClientRpcParams(OwnerClientId));
    }

    public void SetPlayerBodyVisual(GameObject visual)
    {
        playerBodyVisual = visual;
    }

    public override void OnNetworkSpawn()
    {
        if (IsNetworkSessionActive() && IsScenePlacedNetworkPlayer())
        {
            gameObject.SetActive(false);
            return;
        }

        DisableScenePlacedPlayers();

        if (IsOwner)
        {
            SetupLocalPlayer();
        }
        else
        {
            SetupRemotePlayer();
        }

        setupCompleted = true;
    }

    public override void OnNetworkDespawn()
    {
        StopDelayedCameraBinding();
        if (IsOwner)
        {
            ClearLocalCameraBinding();
        }

        setupCompleted = false;
        base.OnNetworkDespawn();
    }

    private void Start()
    {
        if (IsNetworkSessionActive() && IsScenePlacedNetworkPlayer())
        {
            gameObject.SetActive(false);
            return;
        }

        if (setupCompleted || !ShouldRunAsLocalPlayer())
        {
            return;
        }

        SetupLocalPlayer();
        setupCompleted = true;
    }

    private void SetupLocalPlayer()
    {
        if (playerBodyVisual != null)
        {
            playerBodyVisual.SetActive(false);
        }

        AssignCameraToLocalPlayer();
        ScheduleCameraRebind();
        EnsureGameTimerUi();
    }

    private void EnsureGameTimerUi()
    {
        if (!TryGetComponent(out GameTimerUI gameTimerUi))
        {
            gameTimerUi = gameObject.AddComponent<GameTimerUI>();
        }

        gameTimerUi.enabled = true;
    }

    private void AssignCameraToLocalPlayer()
    {
        if (!TryGetComponent(out FirstPersonController firstPersonController))
        {
            return;
        }

        var cameraTarget = firstPersonController.CinemachineCameraTarget;
        if (cameraTarget == null)
        {
            Debug.LogWarning("PlayerNetworkSetup: Local player is missing a Cinemachine camera target.");
            return;
        }

        sharedVirtualCamera = FindActiveSceneVirtualCamera();
        if (sharedVirtualCamera == null)
        {
            Debug.LogWarning("PlayerNetworkSetup: No Cinemachine virtual camera found in scene.");
            return;
        }

        var targetTransform = cameraTarget.transform;
        if (TryGetComponent(out PlayerCameraFeedbackComposer feedbackComposer))
        {
            targetTransform = feedbackComposer.OutputTarget;
        }

        sharedVirtualCamera.Follow = targetTransform;
        sharedVirtualCamera.LookAt = targetTransform;

        firstPersonController.ResetLookRotationState();

        if (TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            playerInteraction.SetInteractionOrigin(targetTransform);
        }
    }

    private void ScheduleCameraRebind()
    {
        StopDelayedCameraBinding();
        delayedCameraBindingCoroutine = StartCoroutine(RebindCameraAfterSceneInitialization());
    }

    private IEnumerator RebindCameraAfterSceneInitialization()
    {
        yield return null;

        delayedCameraBindingCoroutine = null;
        if (isActiveAndEnabled && ShouldRunAsLocalPlayer() && !IsScenePlacedNetworkPlayer())
        {
            AssignCameraToLocalPlayer();
        }
    }

    private void StopDelayedCameraBinding()
    {
        if (delayedCameraBindingCoroutine == null)
        {
            return;
        }

        StopCoroutine(delayedCameraBindingCoroutine);
        delayedCameraBindingCoroutine = null;
    }

    private static CinemachineVirtualCamera FindActiveSceneVirtualCamera()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (sharedVirtualCamera != null
            && sharedVirtualCamera.isActiveAndEnabled
            && sharedVirtualCamera.gameObject.scene == activeScene)
        {
            return sharedVirtualCamera;
        }

        sharedVirtualCamera = null;
        foreach (CinemachineVirtualCamera virtualCamera in FindObjectsByType<CinemachineVirtualCamera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (virtualCamera.gameObject.scene == activeScene)
            {
                sharedVirtualCamera = virtualCamera;
                break;
            }
        }

        return sharedVirtualCamera;
    }

    private void ClearLocalCameraBinding()
    {
        if (sharedVirtualCamera != null
            && TryGetComponent(out FirstPersonController firstPersonController)
            && firstPersonController.CinemachineCameraTarget != null)
        {
            Transform expectedTarget = firstPersonController.CinemachineCameraTarget.transform;
            if (TryGetComponent(out PlayerCameraFeedbackComposer feedbackComposer))
            {
                expectedTarget = feedbackComposer.OutputTarget;
            }

            if (sharedVirtualCamera.Follow == expectedTarget)
            {
                sharedVirtualCamera.Follow = null;
                sharedVirtualCamera.LookAt = null;
            }
        }

        sharedVirtualCamera = null;
    }

    public static void DisableScenePlacedPlayers()
    {
        if (!IsNetworkSessionActive())
        {
            return;
        }

        foreach (var setup in FindObjectsByType<PlayerNetworkSetup>(FindObjectsSortMode.None))
        {
            var networkObject = setup.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSceneObject == true)
            {
                setup.gameObject.SetActive(false);
            }
        }
    }

    private void SetupRemotePlayer()
    {
        SetComponentEnabled<FirstPersonController>(false);
        SetComponentEnabled<PlayerInputNew>(false);
        SetComponentEnabled<PlayerInteractionNew>(false);
        SetComponentEnabled<PlayerActionController>(false);
        SetComponentEnabled<PlayerInventory>(false);
        SetComponentEnabled<PlayerMovementFeedback>(false);
        SetComponentEnabled<PlayerInput>(false);
        SetComponentEnabled<StarterAssetsInputs>(false);

        if (TryGetComponent(out CharacterController characterController))
        {
            characterController.enabled = false;
        }

        if (playerBodyVisual != null)
        {
            playerBodyVisual.SetActive(true);
        }

        DisableOwnerOnlyUi();
    }

    [ClientRpc]
    private void ConfirmInitialSpawnPlacementClientRpc(
        Vector3 position,
        Quaternion rotation,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
        {
            return;
        }

        CharacterController characterController = GetComponent<CharacterController>();
        bool wasEnabled = characterController != null && characterController.enabled;
        if (wasEnabled)
        {
            characterController.enabled = false;
        }

        transform.SetPositionAndRotation(position, rotation);

        if (wasEnabled)
        {
            characterController.enabled = true;
        }
    }

    private static ClientRpcParams CreateTargetClientRpcParams(ulong clientId)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };
    }

    private void DisableOwnerOnlyUi()
    {
        foreach (var staminaUi in GetComponentsInChildren<PlayerStaminaUI>(true))
        {
            staminaUi.gameObject.SetActive(false);
        }

        foreach (var healthUi in GetComponentsInChildren<PlayerHealthUI>(true))
        {
            healthUi.gameObject.SetActive(false);
        }

        foreach (var damageFeedback in GetComponentsInChildren<PlayerDamageFeedback>(true))
        {
            damageFeedback.enabled = false;
        }

        foreach (var respawnPromptUi in GetComponentsInChildren<PlayerRespawnPromptUI>(true))
        {
            respawnPromptUi.gameObject.SetActive(false);
        }

        foreach (var inventoryUi in GetComponentsInChildren<PlayerInventoryUI>(true))
        {
            inventoryUi.gameObject.SetActive(false);
        }

        foreach (var lookingAtUi in GetComponentsInChildren<LookingAtComponentUI>(true))
        {
            lookingAtUi.gameObject.SetActive(false);
        }

        foreach (var bridgeRequirementsUi in GetComponentsInChildren<BridgeRequirementsUI>(true))
        {
            bridgeRequirementsUi.gameObject.SetActive(false);
        }

        foreach (var restartLevelUi in GetComponentsInChildren<RestartLevelUI>(true))
        {
            restartLevelUi.gameObject.SetActive(false);
        }

        foreach (var canvas in GetComponentsInChildren<Canvas>(true))
        {
            canvas.gameObject.SetActive(false);
        }
    }

    private static void SetComponentEnabled<T>(GameObject target, bool enabled) where T : Behaviour
    {
        if (target.TryGetComponent(out T component))
        {
            component.enabled = enabled;
        }
    }

    private void SetComponentEnabled<T>(bool enabled) where T : Behaviour
    {
        SetComponentEnabled<T>(gameObject, enabled);
    }

    private bool ShouldRunAsLocalPlayer()
    {
        if (IsSpawned)
        {
            return IsOwner;
        }

        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
    }

    private bool IsScenePlacedNetworkPlayer()
    {
        NetworkObject networkObject = GetComponent<NetworkObject>();
        return networkObject != null && networkObject.IsSceneObject == true;
    }

    private static bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }
}
