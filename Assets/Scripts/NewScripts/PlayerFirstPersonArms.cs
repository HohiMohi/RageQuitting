using StarterAssets;
using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerFirstPersonArms : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private FirstPersonController firstPersonController;
    [SerializeField] private PlayerInputNew playerInput;
    [SerializeField] private PlayerActionController playerActionController;
    [SerializeField] private PlayerInteractionNew playerInteraction;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerInventory playerInventory;

    [Header("Pose")]
    [SerializeField] private Vector3 rootLocalPosition = new Vector3(0f, -0.24f, 0.95f);
    [SerializeField] private Vector3 rootLocalEulerAngles = new Vector3(-4f, 0f, 0f);
    [SerializeField] private float armSpacing = 0.24f;
    [SerializeField] private Color skinColor = new Color(0.45f, 0.78f, 0.32f, 1f);
    [SerializeField] private Color forearmColor = new Color(0.35f, 0.55f, 0.24f, 1f);

    [Header("Animation")]
    [SerializeField] private float idleBobAmplitude = 0.012f;
    [SerializeField] private float moveBobAmplitude = 0.045f;
    [SerializeField] private float sprintBobAmplitude = 0.07f;
    [SerializeField] private float bobFrequency = 7f;
    [SerializeField] private float sprintBobFrequency = 10f;
    [SerializeField] private float actionDuration = 0.32f;
    [SerializeField] private float actionSwingAngle = 36f;
    [SerializeField] private float hitReactionDuration = 0.22f;
    [SerializeField] private float poseLerpSpeed = 14f;

    [Header("Tool Visual")]
    [SerializeField] private EquippableToolVisualBuilder.ToolVisualMaterials toolVisualMaterials;
    [SerializeField] private Vector3 toolLocalPosition = new Vector3(0.02f, -0.06f, 0.38f);
    [SerializeField] private Vector3 toolLocalEulerAngles = new Vector3(0f, 270f, 0f);
    [SerializeField] private Vector3 toolLocalScale = new Vector3(0.5f, 0.5f, 0.5f);
    [SerializeField] private Vector3 toolSwingPositionOffset = new Vector3(0f, 0.02f, 0.08f);
    [SerializeField] private Vector3 toolSwingEulerOffset = new Vector3(24f, -8f, -14f);

    private Transform armsRoot;
    private Transform leftArmRoot;
    private Transform rightArmRoot;
    private Transform rightToolAnchor;
    private GameObject rightToolVisual;
    private Material skinMaterial;
    private Material forearmMaterial;
    private float bobTimer;
    private float actionTimer;
    private float hitReactionTimer;
    private float previousHealth;
    private int currentToolItemTypeValue = -2;
    private bool setupCompleted;

    private bool ShouldShowLocalArms
    {
        get
        {
            if (IsSpawned)
            {
                return IsOwner;
            }

            return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
        }
    }

    private void Awake()
    {
        CacheReferences();
        previousHealth = playerHealth != null ? playerHealth.CurrentHealth : 0f;
    }

    private void Start()
    {
        TrySetup();
    }

    public override void OnNetworkSpawn()
    {
        setupCompleted = false;
        TrySetup();
    }

    public override void OnNetworkDespawn()
    {
        DestroyArms();
    }

    private void OnEnable()
    {
        SubscribeEvents();
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
    }

    private void Update()
    {
        if (!setupCompleted)
        {
            TrySetup();
        }

        if (!setupCompleted || armsRoot == null)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        UpdateTimers(deltaTime);
        UpdatePose(deltaTime);
    }

    private void CacheReferences()
    {
        if (firstPersonController == null)
        {
            firstPersonController = GetComponent<FirstPersonController>();
        }

        if (playerInput == null)
        {
            playerInput = GetComponent<PlayerInputNew>();
        }

        if (playerActionController == null)
        {
            playerActionController = GetComponent<PlayerActionController>();
        }

        if (playerInteraction == null)
        {
            playerInteraction = GetComponent<PlayerInteractionNew>();
        }

        if (playerHealth == null)
        {
            playerHealth = GetComponent<PlayerHealth>();
        }

        if (playerInventory == null)
        {
            playerInventory = GetComponent<PlayerInventory>();
        }

        PlayerCameraFeedbackComposer feedbackComposer = GetComponent<PlayerCameraFeedbackComposer>();
        if (feedbackComposer != null && feedbackComposer.OutputTarget != null)
        {
            cameraRoot = feedbackComposer.OutputTarget;
        }
        else if (cameraRoot == null && firstPersonController != null && firstPersonController.CinemachineCameraTarget != null)
        {
            cameraRoot = firstPersonController.CinemachineCameraTarget.transform;
        }
    }

    private void TrySetup()
    {
        if (setupCompleted)
        {
            return;
        }

        CacheReferences();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsSpawned)
        {
            return;
        }

        if (!ShouldShowLocalArms || cameraRoot == null)
        {
            DestroyArms();
            setupCompleted = true;
            return;
        }

        CreateArms();
        setupCompleted = true;
    }

    private void CreateArms()
    {
        if (armsRoot != null)
        {
            return;
        }

        skinMaterial = new Material(GetDefaultShader());
        forearmMaterial = new Material(GetDefaultShader());
        skinMaterial.color = skinColor;
        forearmMaterial.color = forearmColor;

        GameObject rootObject = new GameObject("FirstPersonArms");
        armsRoot = rootObject.transform;
        armsRoot.SetParent(cameraRoot, false);
        armsRoot.localPosition = rootLocalPosition;
        armsRoot.localRotation = Quaternion.Euler(rootLocalEulerAngles);

        leftArmRoot = CreateArm("Left", -armSpacing);
        rightArmRoot = CreateArm("Right", armSpacing);
        rightToolAnchor = CreateToolAnchor(rightArmRoot);
        RefreshToolVisual();
    }

    private Shader GetDefaultShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
        {
            return shader;
        }

        shader = Shader.Find("Standard");
        if (shader != null)
        {
            return shader;
        }

        return Shader.Find("Sprites/Default");
    }

    private Transform CreateArm(string sideName, float xOffset)
    {
        GameObject armObject = new GameObject($"FPP_{sideName}Arm");
        Transform armRoot = armObject.transform;
        armRoot.SetParent(armsRoot, false);
        armRoot.localPosition = new Vector3(xOffset, 0f, 0f);
        armRoot.localRotation = Quaternion.identity;

        CreateArmSegment($"{sideName}_UpperArm", armRoot, new Vector3(0f, -0.015f, 0.08f), new Vector3(0.052f, 0.13f, 0.052f), skinMaterial, new Vector3(72f, 0f, 0f));
        CreateArmSegment($"{sideName}_Forearm", armRoot, new Vector3(0f, -0.085f, 0.2f), new Vector3(0.048f, 0.16f, 0.048f), forearmMaterial, new Vector3(66f, 0f, 0f));
        CreateHand($"{sideName}_Hand", armRoot, new Vector3(0f, -0.14f, 0.31f), new Vector3(0.082f, 0.055f, 0.072f), skinMaterial);

        return armRoot;
    }

    private Transform CreateToolAnchor(Transform parent)
    {
        if (parent == null)
        {
            return null;
        }

        GameObject anchorObject = new GameObject("FPP_RightHandToolAnchor");
        Transform anchor = anchorObject.transform;
        anchor.SetParent(parent, false);
        rightToolAnchor = anchor;
        ApplyToolAnchorPose(0f, false);
        return anchor;
    }

    private void CreateArmSegment(string objectName, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, Vector3 eulerAngles)
    {
        GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        segment.name = objectName;
        segment.transform.SetParent(parent, false);
        segment.transform.localPosition = localPosition;
        segment.transform.localRotation = Quaternion.Euler(eulerAngles);
        segment.transform.localScale = localScale;
        ApplyVisualObjectSettings(segment, material);
    }

    private void CreateHand(string objectName, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject hand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hand.name = objectName;
        hand.transform.SetParent(parent, false);
        hand.transform.localPosition = localPosition;
        hand.transform.localRotation = Quaternion.identity;
        hand.transform.localScale = localScale;
        ApplyVisualObjectSettings(hand, material);
    }

    private void ApplyVisualObjectSettings(GameObject visualObject, Material material)
    {
        foreach (var collider in visualObject.GetComponentsInChildren<Collider>())
        {
            Destroy(collider);
        }

        if (visualObject.TryGetComponent(out Renderer renderer))
        {
            renderer.sharedMaterial = material;
        }
    }

    private void UpdateTimers(float deltaTime)
    {
        actionTimer = Mathf.Max(0f, actionTimer - deltaTime);
        hitReactionTimer = Mathf.Max(0f, hitReactionTimer - deltaTime);
    }

    private void UpdatePose(float deltaTime)
    {
        Vector2 moveInput = playerInput != null ? playerInput.GetMoveVectorValue() : Vector2.zero;
        float moveAmount = Mathf.Clamp01(moveInput.magnitude);
        bool isSprinting = firstPersonController != null && firstPersonController.IsSprinting && moveAmount > 0.1f;
        bool isCarrying = playerInteraction != null && playerInteraction.IsHoldingObject;
        bool isDowned = playerHealth != null && playerHealth.IsDowned;

        float currentBobFrequency = isSprinting ? sprintBobFrequency : bobFrequency;
        bobTimer += deltaTime * currentBobFrequency * Mathf.Lerp(0.35f, 1f, moveAmount);

        float bobAmplitude = isSprinting ? sprintBobAmplitude : Mathf.Lerp(idleBobAmplitude, moveBobAmplitude, moveAmount);
        float bob = Mathf.Sin(bobTimer) * bobAmplitude;
        float sway = Mathf.Cos(bobTimer * 0.5f) * bobAmplitude * 0.55f;
        float actionNormalized = actionDuration > 0f ? Mathf.Clamp01(actionTimer / actionDuration) : 0f;
        float actionCurve = Mathf.Sin(actionNormalized * Mathf.PI);
        float hitNormalized = hitReactionDuration > 0f ? Mathf.Clamp01(hitReactionTimer / hitReactionDuration) : 0f;

        Vector3 targetRootPosition = rootLocalPosition + new Vector3(sway, bob, 0f);
        Vector3 targetRootEuler = rootLocalEulerAngles;

        if (isSprinting)
        {
            targetRootPosition += new Vector3(0f, -0.06f, -0.04f);
            targetRootEuler += new Vector3(7f, 0f, 0f);
        }

        if (isCarrying)
        {
            targetRootPosition += new Vector3(0f, -0.03f, 0.12f);
            targetRootEuler += new Vector3(-6f, 0f, 0f);
        }

        if (isDowned)
        {
            targetRootPosition += new Vector3(0f, -0.32f, -0.18f);
            targetRootEuler += new Vector3(24f, 0f, 0f);
        }

        targetRootPosition += new Vector3(0f, 0f, -hitNormalized * 0.08f);

        armsRoot.localPosition = Vector3.Lerp(armsRoot.localPosition, targetRootPosition, deltaTime * poseLerpSpeed);
        armsRoot.localRotation = Quaternion.Slerp(armsRoot.localRotation, Quaternion.Euler(targetRootEuler), deltaTime * poseLerpSpeed);

        UpdateArmPose(leftArmRoot, true, actionCurve, moveAmount, isCarrying, isDowned);
        UpdateArmPose(rightArmRoot, false, actionCurve, moveAmount, isCarrying, isDowned);
        UpdateToolPose(actionCurve, isDowned);
    }

    private void UpdateArmPose(Transform armRoot, bool isLeft, float actionCurve, float moveAmount, bool isCarrying, bool isDowned)
    {
        if (armRoot == null)
        {
            return;
        }

        float side = isLeft ? -1f : 1f;
        Vector3 targetPosition = new Vector3(side * armSpacing, 0f, 0f);
        Vector3 targetEuler = new Vector3(0f, side * 3f, side * -3f);

        targetEuler.x += Mathf.Sin(bobTimer + (isLeft ? 0f : Mathf.PI)) * moveAmount * 7f;
        targetEuler.x += actionCurve * actionSwingAngle * (isLeft ? 0.45f : 1f);
        targetEuler.z += actionCurve * side * -12f;

        if (isCarrying)
        {
            targetPosition += new Vector3(side * 0.06f, -0.02f, 0.08f);
            targetEuler += new Vector3(-18f, side * 4f, side * 5f);
        }

        if (isDowned)
        {
            targetPosition += new Vector3(side * 0.03f, -0.08f, -0.08f);
            targetEuler += new Vector3(18f, side * -12f, side * 18f);
        }

        armRoot.localPosition = Vector3.Lerp(armRoot.localPosition, targetPosition, Time.deltaTime * poseLerpSpeed);
        armRoot.localRotation = Quaternion.Slerp(armRoot.localRotation, Quaternion.Euler(targetEuler), Time.deltaTime * poseLerpSpeed);
    }

    private void UpdateToolPose(float actionCurve, bool isDowned)
    {
        if (rightToolAnchor == null)
        {
            return;
        }

        ApplyToolAnchorPose(actionCurve, isDowned);

        if (rightToolVisual != null && rightToolVisual.activeSelf == isDowned)
        {
            rightToolVisual.SetActive(!isDowned);
        }
    }

    private void ApplyToolAnchorPose(float actionCurve, bool isDowned)
    {
        if (rightToolAnchor == null)
        {
            return;
        }

        Vector3 targetPosition = toolLocalPosition + toolSwingPositionOffset * actionCurve;
        Vector3 targetEuler = toolLocalEulerAngles + toolSwingEulerOffset * actionCurve;

        if (isDowned)
        {
            targetPosition += new Vector3(0f, -0.08f, -0.08f);
            targetEuler += new Vector3(18f, 0f, 0f);
        }

        rightToolAnchor.localPosition = targetPosition;
        rightToolAnchor.localRotation = Quaternion.Euler(targetEuler);
        rightToolAnchor.localScale = toolLocalScale;
    }

    private void RefreshToolVisual()
    {
        if (rightToolAnchor == null || playerInventory == null)
        {
            return;
        }

        int slotItemTypeValue = GetSlot0ItemTypeValue();
        if (slotItemTypeValue == currentToolItemTypeValue)
        {
            return;
        }

        currentToolItemTypeValue = slotItemTypeValue;
        ClearToolVisual();

        if (slotItemTypeValue < 0)
        {
            return;
        }

        EquippableItemType itemType = (EquippableItemType)slotItemTypeValue;
        if (!IsSupportedToolType(itemType))
        {
            return;
        }

        rightToolVisual = EquippableToolVisualBuilder.BuildVisual(itemType, rightToolAnchor, toolVisualMaterials);
        if (rightToolVisual != null && playerHealth != null)
        {
            rightToolVisual.SetActive(!playerHealth.IsDowned);
        }
    }

    private int GetSlot0ItemTypeValue()
    {
        EquippableItemSO item = playerInventory.GetItemInSlot(0);
        return item != null ? (int)item.itemType : -1;
    }

    private static bool IsSupportedToolType(EquippableItemType itemType)
    {
        return itemType == EquippableItemType.Axe || itemType == EquippableItemType.Pickaxe;
    }

    private void ClearToolVisual()
    {
        if (rightToolVisual == null)
        {
            return;
        }

        Destroy(rightToolVisual);
        rightToolVisual = null;
    }

    private void SubscribeEvents()
    {
        if (playerActionController != null)
        {
            playerActionController.OnActionPerformed -= Gameplay_OnActionAnimation;
            playerActionController.OnActionAltPerformed -= Gameplay_OnActionAnimation;
            playerActionController.OnActionPerformed += Gameplay_OnActionAnimation;
            playerActionController.OnActionAltPerformed += Gameplay_OnActionAnimation;
        }

        if (playerInteraction != null)
        {
            playerInteraction.OnInteractionPerformed -= Gameplay_OnActionAnimation;
            playerInteraction.OnInteractionPerformed += Gameplay_OnActionAnimation;
        }

        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged += PlayerHealth_OnHealthChanged;
        }

        if (playerInventory != null)
        {
            playerInventory.OnInventorySlotsChanged -= PlayerInventory_OnInventorySlotsChanged;
            playerInventory.OnInventorySlotsChanged += PlayerInventory_OnInventorySlotsChanged;
        }
    }

    private void UnsubscribeEvents()
    {
        if (playerActionController != null)
        {
            playerActionController.OnActionPerformed -= Gameplay_OnActionAnimation;
            playerActionController.OnActionAltPerformed -= Gameplay_OnActionAnimation;
        }

        if (playerInteraction != null)
        {
            playerInteraction.OnInteractionPerformed -= Gameplay_OnActionAnimation;
        }

        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged -= PlayerHealth_OnHealthChanged;
        }

        if (playerInventory != null)
        {
            playerInventory.OnInventorySlotsChanged -= PlayerInventory_OnInventorySlotsChanged;
        }
    }

    private void PlayerInventory_OnInventorySlotsChanged(object sender, EventArgs e)
    {
        RefreshToolVisual();
    }

    private void Gameplay_OnActionAnimation(object sender, EventArgs e)
    {
        if (playerHealth != null && playerHealth.IsDowned)
        {
            return;
        }

        actionTimer = actionDuration;
    }

    private void PlayerHealth_OnHealthChanged(object sender, EventArgs e)
    {
        if (playerHealth == null)
        {
            return;
        }

        float currentHealth = playerHealth.CurrentHealth;
        if (currentHealth < previousHealth)
        {
            hitReactionTimer = hitReactionDuration;
        }

        previousHealth = currentHealth;
    }

    private void DestroyArms()
    {
        if (armsRoot != null)
        {
            Destroy(armsRoot.gameObject);
            armsRoot = null;
            leftArmRoot = null;
            rightArmRoot = null;
            rightToolAnchor = null;
            rightToolVisual = null;
            currentToolItemTypeValue = -2;
        }

        if (skinMaterial != null)
        {
            Destroy(skinMaterial);
            skinMaterial = null;
        }

        if (forearmMaterial != null)
        {
            Destroy(forearmMaterial);
            forearmMaterial = null;
        }
    }
}
