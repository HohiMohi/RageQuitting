using Cinemachine;
using StarterAssets;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerNetworkSetup : NetworkBehaviour
{
    [SerializeField] private GameObject playerBodyVisual;

    private bool setupCompleted;
    private static CinemachineVirtualCamera sharedVirtualCamera;

    public GameObject PlayerBodyVisual => playerBodyVisual;

    public void SetPlayerBodyVisual(GameObject visual)
    {
        playerBodyVisual = visual;
    }

    public override void OnNetworkSpawn()
    {
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

    private void Start()
    {
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

        sharedVirtualCamera ??= FindFirstObjectByType<CinemachineVirtualCamera>();
        if (sharedVirtualCamera == null)
        {
            Debug.LogWarning("PlayerNetworkSetup: No Cinemachine virtual camera found in scene.");
            return;
        }

        var targetTransform = cameraTarget.transform;
        sharedVirtualCamera.Follow = targetTransform;
        sharedVirtualCamera.LookAt = targetTransform;

        if (TryGetComponent(out PlayerInteractionNew playerInteraction))
        {
            playerInteraction.SetInteractionOrigin(targetTransform);
        }
    }

    public static void DisableScenePlacedPlayers()
    {
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
}
