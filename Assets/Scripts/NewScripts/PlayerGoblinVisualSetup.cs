using UnityEngine;

public class PlayerGoblinVisualSetup : MonoBehaviour
{
    [SerializeField] private GameObject goblinVisualPrefab;
    [SerializeField] private RuntimeAnimatorController animatorController;
    [SerializeField] private Avatar animatorAvatar;
    [SerializeField] private GameObject fallbackVisual;
    [SerializeField] private Vector3 localPosition = Vector3.zero;
    [SerializeField] private Vector3 localEulerAngles = Vector3.zero;
    [SerializeField] private Vector3 localScale = Vector3.one;
    [SerializeField] private bool hideFallbackVisual = true;

    private GameObject spawnedVisual;

    public GameObject SpawnedVisual => spawnedVisual;

    private void Awake()
    {
        SetupVisual();
    }

    private void SetupVisual()
    {
        if (spawnedVisual != null || goblinVisualPrefab == null)
        {
            RegisterVisual(spawnedVisual != null ? spawnedVisual : fallbackVisual);
            return;
        }

        spawnedVisual = Instantiate(goblinVisualPrefab, transform);
        spawnedVisual.name = "GoblinPlayerVisual";
        spawnedVisual.transform.localPosition = localPosition;
        spawnedVisual.transform.localRotation = Quaternion.Euler(localEulerAngles);
        spawnedVisual.transform.localScale = localScale;
        SetLayerRecursively(spawnedVisual, gameObject.layer);
        DisableGameplayComponents(spawnedVisual);

        Animator animator = spawnedVisual.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            animator = spawnedVisual.AddComponent<Animator>();
        }

        if (animatorAvatar != null)
        {
            animator.avatar = animatorAvatar;
        }

        if (animatorController != null)
        {
            animator.runtimeAnimatorController = animatorController;
        }

        if (hideFallbackVisual && fallbackVisual != null)
        {
            HideFallbackRenderers(fallbackVisual);
        }

        if (!spawnedVisual.TryGetComponent(out GoblinAnimationEventReceiver _))
        {
            spawnedVisual.AddComponent<GoblinAnimationEventReceiver>();
        }

        RegisterVisual(spawnedVisual);

        if (TryGetComponent(out PlayerAnimationController animationController))
        {
            animationController.SetAnimator(animator);
        }
    }

    private void RegisterVisual(GameObject visual)
    {
        if (visual == null || !TryGetComponent(out PlayerNetworkSetup networkSetup))
        {
            return;
        }

        networkSetup.SetPlayerBodyVisual(visual);
    }

    private void DisableGameplayComponents(GameObject visual)
    {
        foreach (var characterController in visual.GetComponentsInChildren<CharacterController>(true))
        {
            characterController.enabled = false;
        }

        foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }
    }

    private void HideFallbackRenderers(GameObject visual)
    {
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
        }
    }

    private void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}
