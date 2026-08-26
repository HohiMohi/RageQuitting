using StarterAssets;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class PlayerFirstPersonArms : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private FirstPersonController firstPersonController;
    [SerializeField] private PlayerInputNew playerInput;
    [SerializeField] private PlayerActionController playerActionController;
    [SerializeField] private RopeToolController ropeToolController;
    [SerializeField] private PlayerInteractionNew playerInteraction;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PlayerTurnFeedback playerTurnFeedback;
    [SerializeField] private PlayerCameraFeedbackComposer cameraFeedbackComposer;
    [SerializeField] private AudioSource toolAudioSource;

    [Header("Rendering")]
    [SerializeField, Range(0, 31)] private int firstPersonRenderLayer = 30;
    [SerializeField] private float firstPersonNearClipPlane = 0.01f;

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
    [SerializeField] private float idleBobCyclesPerSecond = 0.4f;
    [SerializeField] private float walkArmCyclesPerMeter = 0.45f;
    [SerializeField] private float sprintArmCyclesPerMeter = 0.4f;
    [SerializeField] private float locomotionAmplitudeSmoothing = 10f;
    [SerializeField] private float sprintBlendSpeed = 8f;
    [SerializeField] private float maximumTrackedDistancePerFrame = 0.5f;
    [SerializeField] private float actionDuration = 0.32f;
    [SerializeField] private float actionSwingAngle = 36f;
    [SerializeField] private float hitReactionDuration = 0.22f;
    [SerializeField] private float poseLerpSpeed = 14f;
    [SerializeField] private float maximumTurnLagPosition = 0.018f;
    [SerializeField] private float maximumTurnLagAngle = 2f;

    [Header("Tool Visual")]
    [SerializeField] private EquippableToolVisualBuilder.ToolVisualMaterials toolVisualMaterials;
    [SerializeField] private Vector3 toolLocalPosition = new Vector3(0.02f, -0.06f, 0.38f);
    [SerializeField] private Vector3 toolLocalEulerAngles = new Vector3(0f, 270f, 0f);
    [SerializeField] private Vector3 toolLocalScale = new Vector3(0.5f, 0.5f, 0.5f);
    [SerializeField] private Vector3 toolSwingPositionOffset = new Vector3(0f, 0.02f, 0.08f);
    [SerializeField] private Vector3 toolSwingEulerOffset = new Vector3(24f, -8f, -14f);
    [SerializeField] private float twoHandedGripBlendSpeed = 14f;
    [SerializeField] private Vector3 twoHandedHandEulerOffset = new Vector3(0f, 0f, 90f);

    private Transform armsRoot;
    private Transform leftArmRoot;
    private Transform leftHandVisual;
    private Transform rightArmRoot;
    private Transform rightToolAnchor;
    private Transform spiritLevelToolAnchor;
    private bool spiritLevelMeasurementActive;
    private float spiritLevelPoseBlend;
    private Transform secondaryGrip;
    private GameObject rightToolVisual;
    private Material skinMaterial;
    private Material forearmMaterial;
    private float idleBobPhase;
    private float locomotionPhase;
    private float locomotionAmount;
    private float sprintBlend;
    private float twoHandedGripBlend;
    private Vector3 previousPlayerPosition;
    private bool hasPreviousPlayerPosition;
    private Camera firstPersonCamera;
    private Camera baseCamera;
    private int originalBaseCameraCullingMask;
    private bool baseCameraMaskOverridden;
    private float actionTimer;
    private float hitReactionTimer;
    private float actionCameraFeedbackTimer;
    private float actionCameraFeedbackDuration;
    private Vector3 actionCameraPositionOffset;
    private Vector3 actionCameraEulerOffset;
    private float previousHealth;
    private int currentToolItemTypeValue = -2;
    private bool setupCompleted;
    private readonly Dictionary<EquippableItemType, AudioClip> proceduralSwingClips = new();

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
        ResetLocomotionState();
    }

    private void Start()
    {
        TrySetup();
    }

    public override void OnNetworkSpawn()
    {
        setupCompleted = false;
        ResetLocomotionState();
        TrySetup();
    }

    public override void OnNetworkDespawn()
    {
        DestroyArms();
    }

    private void OnEnable()
    {
        ResetLocomotionState();
        SubscribeEvents();
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
        cameraFeedbackComposer?.ClearActionFeedback();
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

        EnsureFirstPersonCamera();

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        UpdateTimers(deltaTime);
        UpdateLocomotionAnimation(deltaTime);
        UpdatePose(deltaTime);
    }

    private void LateUpdate()
    {
        if (firstPersonCamera == null || baseCamera == null)
        {
            return;
        }

        firstPersonCamera.fieldOfView = baseCamera.fieldOfView;
        firstPersonCamera.aspect = baseCamera.aspect;
        firstPersonCamera.nearClipPlane = Mathf.Max(0.001f, firstPersonNearClipPlane);
        firstPersonCamera.farClipPlane = baseCamera.farClipPlane;
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

        if (ropeToolController == null)
        {
            ropeToolController = GetComponent<RopeToolController>();
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

        if (playerTurnFeedback == null)
        {
            playerTurnFeedback = GetComponent<PlayerTurnFeedback>();
        }

        if (cameraFeedbackComposer == null)
        {
            cameraFeedbackComposer = GetComponent<PlayerCameraFeedbackComposer>();
        }

        if (cameraFeedbackComposer != null && cameraFeedbackComposer.OutputTarget != null)
        {
            cameraRoot = cameraFeedbackComposer.OutputTarget;
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
        rootObject.layer = firstPersonRenderLayer;
        armsRoot = rootObject.transform;
        armsRoot.SetParent(cameraRoot, false);
        armsRoot.localPosition = rootLocalPosition;
        armsRoot.localRotation = Quaternion.Euler(rootLocalEulerAngles);

        leftArmRoot = CreateArm("Left", -armSpacing, out leftHandVisual);
        rightArmRoot = CreateArm("Right", armSpacing, out _);
        rightToolAnchor = CreateToolAnchor(rightArmRoot);
        spiritLevelToolAnchor = CreateSpiritLevelToolAnchor();
        RefreshToolVisual();
    }

    private void EnsureFirstPersonCamera()
    {
        Camera currentMainCamera = Camera.main;
        if (currentMainCamera == null)
        {
            return;
        }

        if (firstPersonCamera != null && baseCamera == currentMainCamera)
        {
            return;
        }

        DestroyFirstPersonCamera();
        baseCamera = currentMainCamera;
        originalBaseCameraCullingMask = baseCamera.cullingMask;
        baseCameraMaskOverridden = true;
        baseCamera.cullingMask &= ~(1 << firstPersonRenderLayer);

        GameObject cameraObject = new GameObject("FirstPersonArmsCamera");
        cameraObject.transform.SetParent(baseCamera.transform, false);
        firstPersonCamera = cameraObject.AddComponent<Camera>();
        firstPersonCamera.CopyFrom(baseCamera);
        firstPersonCamera.cullingMask = 1 << firstPersonRenderLayer;
        firstPersonCamera.nearClipPlane = Mathf.Max(0.001f, firstPersonNearClipPlane);

        UniversalAdditionalCameraData baseCameraData = baseCamera.GetUniversalAdditionalCameraData();
        UniversalAdditionalCameraData overlayCameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        overlayCameraData.renderType = CameraRenderType.Overlay;
        overlayCameraData.renderPostProcessing = false;
        if (!baseCameraData.cameraStack.Contains(firstPersonCamera))
        {
            baseCameraData.cameraStack.Add(firstPersonCamera);
        }
    }

    private void DestroyFirstPersonCamera()
    {
        if (baseCamera != null)
        {
            UniversalAdditionalCameraData baseCameraData = baseCamera.GetUniversalAdditionalCameraData();
            if (firstPersonCamera != null)
            {
                baseCameraData.cameraStack.Remove(firstPersonCamera);
            }

            if (baseCameraMaskOverridden)
            {
                baseCamera.cullingMask = originalBaseCameraCullingMask;
            }
        }

        baseCameraMaskOverridden = false;
        baseCamera = null;
        if (firstPersonCamera != null)
        {
            Destroy(firstPersonCamera.gameObject);
            firstPersonCamera = null;
        }
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

    private Transform CreateArm(string sideName, float xOffset, out Transform handVisual)
    {
        GameObject armObject = new GameObject($"FPP_{sideName}Arm");
        Transform armRoot = armObject.transform;
        armRoot.SetParent(armsRoot, false);
        armRoot.localPosition = new Vector3(xOffset, 0f, 0f);
        armRoot.localRotation = Quaternion.identity;

        CreateArmSegment($"{sideName}_UpperArm", armRoot, new Vector3(0f, -0.015f, 0.08f), new Vector3(0.052f, 0.13f, 0.052f), skinMaterial, new Vector3(72f, 0f, 0f));
        CreateArmSegment($"{sideName}_Forearm", armRoot, new Vector3(0f, -0.085f, 0.2f), new Vector3(0.048f, 0.16f, 0.048f), forearmMaterial, new Vector3(66f, 0f, 0f));
        handVisual = CreateHand(
            $"{sideName}_Hand",
            armRoot,
            new Vector3(0f, -0.14f, 0.31f),
            new Vector3(0.082f, 0.055f, 0.072f),
            skinMaterial);

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
        anchor.localPosition = toolLocalPosition;
        anchor.localRotation = Quaternion.Euler(toolLocalEulerAngles);
        anchor.localScale = toolLocalScale;
        return anchor;
    }

    private Transform CreateSpiritLevelToolAnchor()
    {
        if (armsRoot == null)
        {
            return null;
        }

        GameObject anchorObject = new GameObject("FPP_SpiritLevelCenterAnchor");
        Transform anchor = anchorObject.transform;
        anchor.SetParent(armsRoot, false);
        ApplySpiritLevelAnchorPose(anchor, false, true);
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

    private Transform CreateHand(string objectName, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject hand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hand.name = objectName;
        hand.transform.SetParent(parent, false);
        hand.transform.localPosition = localPosition;
        hand.transform.localRotation = Quaternion.identity;
        hand.transform.localScale = localScale;
        ApplyVisualObjectSettings(hand, material);
        return hand.transform;
    }

    private void ApplyVisualObjectSettings(GameObject visualObject, Material material)
    {
        visualObject.layer = firstPersonRenderLayer;
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

        if (actionCameraFeedbackTimer > 0f)
        {
            actionCameraFeedbackTimer = Mathf.Max(0f, actionCameraFeedbackTimer - deltaTime);
            float weight = actionCameraFeedbackDuration > 0f
                ? Mathf.SmoothStep(0f, 1f, actionCameraFeedbackTimer / actionCameraFeedbackDuration)
                : 0f;
            cameraFeedbackComposer?.SetActionFeedback(
                actionCameraPositionOffset * weight,
                actionCameraEulerOffset * weight);
        }
        else
        {
            cameraFeedbackComposer?.ClearActionFeedback();
        }
    }

    private void UpdateLocomotionAnimation(float deltaTime)
    {
        Vector3 currentPosition = transform.position;
        Vector3 horizontalDelta = hasPreviousPlayerPosition
            ? currentPosition - previousPlayerPosition
            : Vector3.zero;
        horizontalDelta.y = 0f;
        previousPlayerPosition = currentPosition;
        hasPreviousPlayerPosition = true;

        bool isDowned = playerHealth != null && playerHealth.IsDowned;
        bool canAnimateLocomotion = firstPersonController != null
            && firstPersonController.Grounded
            && !isDowned;
        bool isSprinting = canAnimateLocomotion && firstPersonController.IsSprinting;

        sprintBlend = Mathf.MoveTowards(
            sprintBlend,
            isSprinting ? 1f : 0f,
            Mathf.Max(0f, sprintBlendSpeed) * deltaTime);

        float speedReference = Mathf.Max(
            0.01f,
            Mathf.Lerp(firstPersonController != null ? firstPersonController.MoveSpeed : 1f,
                firstPersonController != null ? firstPersonController.SprintSpeed : 1f,
                sprintBlend));
        float targetLocomotionAmount = canAnimateLocomotion
            ? Mathf.Clamp01(firstPersonController.HorizontalSpeed / speedReference)
            : 0f;
        locomotionAmount = Mathf.MoveTowards(
            locomotionAmount,
            targetLocomotionAmount,
            Mathf.Max(0f, locomotionAmplitudeSmoothing) * deltaTime);

        if (!canAnimateLocomotion)
        {
            return;
        }

        const float completeAnimationCycle = Mathf.PI * 4f;
        idleBobPhase = Mathf.Repeat(
            idleBobPhase + deltaTime * Mathf.Max(0f, idleBobCyclesPerSecond) * Mathf.PI * 2f,
            completeAnimationCycle);

        float travelledDistance = Mathf.Min(
            horizontalDelta.magnitude,
            Mathf.Max(0f, maximumTrackedDistancePerFrame));
        if (travelledDistance <= 0f)
        {
            return;
        }

        float cyclesPerMeter = Mathf.Lerp(
            Mathf.Max(0f, walkArmCyclesPerMeter),
            Mathf.Max(0f, sprintArmCyclesPerMeter),
            sprintBlend);
        locomotionPhase = Mathf.Repeat(
            locomotionPhase + travelledDistance * cyclesPerMeter * Mathf.PI * 2f,
            completeAnimationCycle);
    }

    private void UpdatePose(float deltaTime)
    {
        float moveAmount = locomotionAmount;
        bool isSprinting = sprintBlend > 0.5f && moveAmount > 0.01f;
        bool isCarrying = playerInteraction != null && playerInteraction.IsHoldingObject;
        bool isDowned = playerHealth != null && playerHealth.IsDowned;
        bool useTwoHandedGrip = ShouldUseTwoHandedGrip(isCarrying, isDowned);
        twoHandedGripBlend = Mathf.MoveTowards(
            twoHandedGripBlend,
            useTwoHandedGrip ? 1f : 0f,
            Mathf.Max(0f, twoHandedGripBlendSpeed) * deltaTime);

        float movementBobAmplitude = Mathf.Lerp(moveBobAmplitude, sprintBobAmplitude, sprintBlend) * moveAmount;
        float idleWeight = firstPersonController != null && firstPersonController.Grounded && !isDowned
            ? 1f - moveAmount
            : 0f;
        float bob = Mathf.Sin(idleBobPhase) * idleBobAmplitude * idleWeight
            + Mathf.Sin(locomotionPhase) * movementBobAmplitude;
        float sway = Mathf.Cos(idleBobPhase * 0.5f) * idleBobAmplitude * idleWeight * 0.55f
            + Mathf.Cos(locomotionPhase * 0.5f) * movementBobAmplitude * 0.55f;
        float actionNormalized = actionDuration > 0f ? Mathf.Clamp01(actionTimer / actionDuration) : 0f;
        float actionCurve = Mathf.Sin(actionNormalized * Mathf.PI);
        float hitNormalized = hitReactionDuration > 0f ? Mathf.Clamp01(hitReactionTimer / hitReactionDuration) : 0f;
        GetProfiledActionPose(
            out Vector3 profiledToolPosition,
            out Vector3 profiledToolEuler,
            out Vector3 profiledRightArmEuler,
            out float profiledLeftArmWeight);

        Vector3 targetRootPosition = rootLocalPosition + new Vector3(sway, bob, 0f);
        Vector3 targetRootEuler = rootLocalEulerAngles;
		float turnAmount = playerTurnFeedback != null ? playerTurnFeedback.TurnAmount : 0f;
		targetRootPosition += new Vector3(-turnAmount * maximumTurnLagPosition, 0f, 0f);
		targetRootEuler += new Vector3(0f, -turnAmount * maximumTurnLagAngle, 0f);

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

        UpdateArmPose(
            rightArmRoot,
            false,
            actionCurve,
            moveAmount,
            isCarrying,
            isDowned,
            profiledRightArmEuler,
            profiledLeftArmWeight);
        UpdateToolPose(actionCurve, isDowned, profiledToolPosition, profiledToolEuler);
        UpdateArmPose(
            leftArmRoot,
            true,
            actionCurve,
            moveAmount,
            isCarrying,
            isDowned,
            profiledRightArmEuler,
            profiledLeftArmWeight);
    }

    private void GetProfiledActionPose(
        out Vector3 toolPosition,
        out Vector3 toolEuler,
        out Vector3 rightArmEuler,
        out float leftArmWeight)
    {
        toolPosition = Vector3.zero;
        toolEuler = Vector3.zero;
        rightArmEuler = Vector3.zero;
        leftArmWeight = 0f;

        if (ropeToolController != null && ropeToolController.IsRopeSelected())
        {
            switch (ropeToolController.CurrentState)
            {
                case RopeState.Charging:
                    float charge = ropeToolController.ChargeNormalized;
                    toolPosition = Vector3.Lerp(Vector3.zero, new Vector3(-0.06f, 0.05f, -0.08f), charge);
                    toolEuler = Vector3.Lerp(Vector3.zero, new Vector3(12f, -8f, -18f), charge);
                    rightArmEuler = Vector3.Lerp(Vector3.zero, new Vector3(-10f, 0f, 12f), charge);
                    leftArmWeight = 1f;
                    return;
                case RopeState.Reeling:
                    float reelCycle = Mathf.Sin(Time.unscaledTime * 8f);
                    toolPosition = new Vector3(-0.03f, 0.01f, -0.04f);
                    toolEuler = new Vector3(8f, 0f, reelCycle * 10f);
                    rightArmEuler = new Vector3(-6f, 0f, reelCycle * 8f);
                    leftArmWeight = 1f;
                    return;
                case RopeState.PayingOut:
                    toolPosition = new Vector3(0f, 0.02f, 0.05f);
                    toolEuler = new Vector3(-8f, 0f, -6f);
                    rightArmEuler = new Vector3(6f, 0f, -5f);
                    leftArmWeight = 1f;
                    return;
            }
        }

        if (playerActionController == null ||
            !playerActionController.IsActionInProgress ||
            playerActionController.CurrentActionItem == null)
        {
            return;
        }

        EquippableActionProfileSO profile = playerActionController.CurrentActionItem.actionProfile;
        if (profile == null)
        {
            return;
        }

        float phaseProgress = playerActionController.CurrentActionPhaseNormalized;
        switch (playerActionController.CurrentActionPhase)
        {
            case EquippableActionPhase.WindUp:
                toolPosition = Vector3.Lerp(Vector3.zero, profile.toolWindUpPositionOffset, phaseProgress);
                toolEuler = Vector3.Lerp(Vector3.zero, profile.toolWindUpEulerOffset, phaseProgress);
                rightArmEuler = Vector3.Lerp(Vector3.zero, profile.rightArmWindUpEulerOffset, phaseProgress);
                break;
            case EquippableActionPhase.Strike:
                toolPosition = Vector3.Lerp(profile.toolWindUpPositionOffset, profile.toolImpactPositionOffset, phaseProgress);
                toolEuler = Vector3.Lerp(profile.toolWindUpEulerOffset, profile.toolImpactEulerOffset, phaseProgress);
                rightArmEuler = Vector3.Lerp(profile.rightArmWindUpEulerOffset, profile.rightArmImpactEulerOffset, phaseProgress);
                break;
            case EquippableActionPhase.ImpactFreeze:
                toolPosition = profile.toolImpactPositionOffset;
                toolEuler = profile.toolImpactEulerOffset;
                rightArmEuler = profile.rightArmImpactEulerOffset;
                break;
            case EquippableActionPhase.Recovery:
                toolPosition = Vector3.Lerp(profile.toolImpactPositionOffset, Vector3.zero, phaseProgress);
                toolEuler = Vector3.Lerp(profile.toolImpactEulerOffset, Vector3.zero, phaseProgress);
                rightArmEuler = Vector3.Lerp(profile.rightArmImpactEulerOffset, Vector3.zero, phaseProgress);
                break;
        }

        leftArmWeight = Mathf.Clamp01(profile.leftArmActionWeight);
    }

    private void UpdateArmPose(
        Transform armRoot,
        bool isLeft,
        float actionCurve,
        float moveAmount,
        bool isCarrying,
        bool isDowned,
        Vector3 profiledRightArmEuler,
        float profiledLeftArmWeight)
    {
        if (armRoot == null)
        {
            return;
        }

        float side = isLeft ? -1f : 1f;
        Vector3 targetPosition = new Vector3(side * armSpacing, 0f, 0f);
        Vector3 targetEuler = new Vector3(0f, side * 3f, side * -3f);

        targetEuler.x += Mathf.Sin(locomotionPhase + (isLeft ? 0f : Mathf.PI)) * moveAmount * 7f;
        targetEuler.x += actionCurve * actionSwingAngle * (isLeft ? 0.45f : 1f);
        targetEuler.z += actionCurve * side * -12f;
        targetEuler += isLeft
            ? new Vector3(
                profiledRightArmEuler.x,
                -profiledRightArmEuler.y,
                -profiledRightArmEuler.z) * profiledLeftArmWeight
            : profiledRightArmEuler;

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

        if (isLeft && twoHandedGripBlend > 0f && TryGetTwoHandedGripPose(out Vector3 gripPosition, out Quaternion gripRotation))
        {
            targetPosition = Vector3.Lerp(targetPosition, gripPosition, twoHandedGripBlend);
            Quaternion normalRotation = Quaternion.Euler(targetEuler);
            armRoot.localPosition = Vector3.Lerp(armRoot.localPosition, targetPosition, Time.deltaTime * poseLerpSpeed);
            armRoot.localRotation = Quaternion.Slerp(
                armRoot.localRotation,
                Quaternion.Slerp(normalRotation, gripRotation, twoHandedGripBlend),
                Time.deltaTime * poseLerpSpeed);
            return;
        }

        armRoot.localPosition = Vector3.Lerp(armRoot.localPosition, targetPosition, Time.deltaTime * poseLerpSpeed);
        armRoot.localRotation = Quaternion.Slerp(armRoot.localRotation, Quaternion.Euler(targetEuler), Time.deltaTime * poseLerpSpeed);
    }

    private bool ShouldUseTwoHandedGrip(bool isCarrying, bool isDowned)
    {
        EquippableItemSO selectedItem = playerInventory != null ? playerInventory.GetItemInSlot(0) : null;
        return !isCarrying &&
               !isDowned &&
               selectedItem != null &&
               selectedItem.itemType != EquippableItemType.SpiritLevel &&
               selectedItem.IsTwoHanded &&
               secondaryGrip != null &&
               rightToolVisual != null &&
               rightToolVisual.activeInHierarchy;
    }

    private bool TryGetTwoHandedGripPose(out Vector3 armPosition, out Quaternion armRotation)
    {
        armPosition = default;
        armRotation = Quaternion.identity;
        if (armsRoot == null || leftHandVisual == null || secondaryGrip == null)
        {
            return false;
        }

        Vector3 gripPosition = armsRoot.InverseTransformPoint(secondaryGrip.position);
        armRotation = Quaternion.Inverse(armsRoot.rotation) *
                      secondaryGrip.rotation *
                      Quaternion.Euler(twoHandedHandEulerOffset);
        armPosition = gripPosition - armRotation * leftHandVisual.localPosition;
        return true;
    }

    private void UpdateToolPose(
        float actionCurve,
        bool isDowned,
        Vector3 profiledPositionOffset,
        Vector3 profiledEulerOffset)
    {
        Transform activeAnchor = GetActiveToolAnchor();
        if (activeAnchor == null)
        {
            return;
        }

        if (IsSpiritLevelSelectedInSlot())
        {
            ApplySpiritLevelAnchorPose(activeAnchor, isDowned, false);
        }
        else
        {
            ApplyRegularToolAnchorPose(activeAnchor, actionCurve, isDowned, profiledPositionOffset, profiledEulerOffset);
        }

        if (rightToolVisual != null && rightToolVisual.activeSelf == isDowned)
        {
            rightToolVisual.SetActive(!isDowned);
        }
    }

    private void ApplyRegularToolAnchorPose(
        Transform anchor,
        float actionCurve,
        bool isDowned,
        Vector3 profiledPositionOffset = default,
        Vector3 profiledEulerOffset = default)
    {
        Vector3 targetPosition = toolLocalPosition + toolSwingPositionOffset * actionCurve + profiledPositionOffset;
        Vector3 targetEuler = toolLocalEulerAngles + toolSwingEulerOffset * actionCurve + profiledEulerOffset;

        if (isDowned)
        {
            targetPosition += new Vector3(0f, -0.08f, -0.08f);
            targetEuler += new Vector3(18f, 0f, 0f);
        }

        anchor.localPosition = targetPosition;
        anchor.localRotation = Quaternion.Euler(targetEuler);
        anchor.localScale = toolLocalScale;
    }

    private void ApplySpiritLevelAnchorPose(Transform anchor, bool isDowned, bool immediate)
    {
        EquippableItemSO selectedItem = playerInventory != null ? playerInventory.GetCurrentSelectedItem() : null;
        SpiritLevelProfileSO profile = selectedItem != null ? selectedItem.spiritLevelProfile : null;
        Vector3 idlePosition = profile != null ? profile.idleLocalPosition : new Vector3(0f, -0.12f, 0.34f);
        Vector3 idleEuler = profile != null ? profile.idleLocalEuler : Vector3.zero;
        Vector3 measurementPosition = profile != null ? profile.measurementLocalPosition : new Vector3(0f, -0.02f, 0.27f);
        Vector3 measurementEuler = profile != null ? profile.measurementLocalEuler : Vector3.zero;
        float duration = profile != null ? profile.measurementTransitionDuration : 0.2f;

        if (immediate)
        {
            spiritLevelPoseBlend = spiritLevelMeasurementActive ? 1f : 0f;
        }
        else
        {
            float step = duration > 0.001f ? Time.deltaTime / duration : 1f;
            spiritLevelPoseBlend = Mathf.MoveTowards(
                spiritLevelPoseBlend,
                spiritLevelMeasurementActive ? 1f : 0f,
                step);
        }

        Vector3 targetPosition = Vector3.Lerp(idlePosition, measurementPosition, spiritLevelPoseBlend);
        Quaternion targetRotation = Quaternion.Slerp(
            Quaternion.Euler(idleEuler),
            Quaternion.Euler(measurementEuler),
            spiritLevelPoseBlend);
        if (isDowned)
        {
            targetPosition += new Vector3(0f, -0.08f, -0.08f);
            targetRotation *= Quaternion.Euler(18f, 0f, 0f);
        }

        anchor.localPosition = targetPosition;
        anchor.localRotation = targetRotation;
        anchor.localScale = toolLocalScale;
    }

    public void SetSpiritLevelMeasurement(bool active, SpiritLevelMeasurementAxis axis)
    {
        spiritLevelMeasurementActive = active;
    }

    private void RefreshToolVisual()
    {
        if (rightToolAnchor == null || spiritLevelToolAnchor == null || playerInventory == null)
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

        Transform visualParent = itemType == EquippableItemType.SpiritLevel
            ? spiritLevelToolAnchor
            : rightToolAnchor;
        if (itemType == EquippableItemType.SpiritLevel)
        {
            spiritLevelPoseBlend = spiritLevelMeasurementActive ? 1f : 0f;
            ApplySpiritLevelAnchorPose(spiritLevelToolAnchor, false, true);
        }
        rightToolVisual = EquippableToolVisualBuilder.BuildVisual(itemType, visualParent, toolVisualMaterials);
        if (rightToolVisual != null)
        {
            secondaryGrip = FindChildRecursive(rightToolVisual.transform, EquippableToolVisualBuilder.SecondaryGripName);
            SetLayerRecursively(rightToolVisual, firstPersonRenderLayer);
            if (playerHealth != null)
            {
                rightToolVisual.SetActive(!playerHealth.IsDowned);
            }

#if UNITY_EDITOR
            EquippableItemSO selectedItem = playerInventory.GetItemInSlot(0);
            if (selectedItem != null && selectedItem.IsTwoHanded && secondaryGrip == null)
            {
                Debug.LogWarning(
                    $"Two-handed tool '{selectedItem.itemName}' has no {EquippableToolVisualBuilder.SecondaryGripName}. " +
                    "Falling back to the regular left-arm animation.",
                    this);
            }
#endif
        }
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        if (root.name == childName)
        {
            return root;
        }

        foreach (Transform child in root)
        {
            Transform result = FindChildRecursive(child, childName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null)
        {
            return;
        }

        root.layer = layer;
        foreach (Transform child in root.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private int GetSlot0ItemTypeValue()
    {
        EquippableItemSO item = playerInventory.GetItemInSlot(0);
        return item != null ? (int)item.itemType : -1;
    }

    private bool IsSpiritLevelSelectedInSlot()
    {
        return GetSlot0ItemTypeValue() == (int)EquippableItemType.SpiritLevel;
    }

    private Transform GetActiveToolAnchor()
    {
        return IsSpiritLevelSelectedInSlot() ? spiritLevelToolAnchor : rightToolAnchor;
    }

    private static bool IsSupportedToolType(EquippableItemType itemType)
    {
        return itemType == EquippableItemType.Axe ||
               itemType == EquippableItemType.Pickaxe ||
               itemType == EquippableItemType.Shovel ||
               itemType == EquippableItemType.IndustrialHammer ||
               itemType == EquippableItemType.Wrench ||
               itemType == EquippableItemType.Rope ||
               itemType == EquippableItemType.SpiritLevel;
    }

    private void ClearToolVisual()
    {
        secondaryGrip = null;
        twoHandedGripBlend = 0f;
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
            playerActionController.OnToolActionStarted -= PlayerActionController_OnToolActionStarted;
            playerActionController.OnToolActionImpact -= PlayerActionController_OnToolActionImpact;
            playerActionController.OnToolActionEnded -= PlayerActionController_OnToolActionEnded;
            playerActionController.OnActionPerformed += Gameplay_OnActionAnimation;
            playerActionController.OnActionAltPerformed += Gameplay_OnActionAnimation;
            playerActionController.OnToolActionStarted += PlayerActionController_OnToolActionStarted;
            playerActionController.OnToolActionImpact += PlayerActionController_OnToolActionImpact;
            playerActionController.OnToolActionEnded += PlayerActionController_OnToolActionEnded;
        }

        if (playerInteraction != null)
        {
            playerInteraction.OnInteractionPerformed -= Gameplay_OnActionAnimation;
            playerInteraction.OnInteractionPerformed += Gameplay_OnActionAnimation;
        }

        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged += PlayerHealth_OnHealthChanged;
            playerHealth.OnDownedStateChanged -= PlayerHealth_OnDownedStateChanged;
            playerHealth.OnDownedStateChanged += PlayerHealth_OnDownedStateChanged;
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
            playerActionController.OnToolActionStarted -= PlayerActionController_OnToolActionStarted;
            playerActionController.OnToolActionImpact -= PlayerActionController_OnToolActionImpact;
            playerActionController.OnToolActionEnded -= PlayerActionController_OnToolActionEnded;
        }

        if (playerInteraction != null)
        {
            playerInteraction.OnInteractionPerformed -= Gameplay_OnActionAnimation;
        }

        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged -= PlayerHealth_OnHealthChanged;
            playerHealth.OnDownedStateChanged -= PlayerHealth_OnDownedStateChanged;
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

        if (playerActionController == null || !playerActionController.IsActionInProgress)
        {
            actionTimer = actionDuration;
        }
    }

    private void PlayerActionController_OnToolActionStarted(
        object sender,
        PlayerActionController.ToolActionEventArgs e)
    {
        PlaySwingAudio(e.Item, e.Profile);
    }

    private void PlayerActionController_OnToolActionImpact(
        object sender,
        PlayerActionController.ToolActionEventArgs e)
    {
        if (!e.Hit || e.Profile == null)
        {
            return;
        }

        actionCameraPositionOffset = e.Profile.cameraKickPosition;
        actionCameraEulerOffset = e.Profile.cameraKickEuler;
        actionCameraFeedbackDuration = Mathf.Max(0.01f, e.Profile.cameraKickRecoveryDuration);
        actionCameraFeedbackTimer = actionCameraFeedbackDuration;
    }

    private void PlayerActionController_OnToolActionEnded(
        object sender,
        PlayerActionController.ToolActionEventArgs e)
    {
        if (e.Phase == EquippableActionPhase.None && !e.Hit)
        {
            cameraFeedbackComposer?.ClearActionFeedback();
        }
    }

    private void PlaySwingAudio(EquippableItemSO item, EquippableActionProfileSO profile)
    {
        if (item == null || profile == null)
        {
            return;
        }

        if (toolAudioSource == null)
        {
            toolAudioSource = gameObject.AddComponent<AudioSource>();
            toolAudioSource.playOnAwake = false;
            toolAudioSource.spatialBlend = 0f;
        }

        AudioClip clip = profile.swingClip;
        if (clip == null && !proceduralSwingClips.TryGetValue(item.itemType, out clip))
        {
            clip = CreateProceduralSwingClip(item.itemType);
            proceduralSwingClips[item.itemType] = clip;
        }

        toolAudioSource.pitch = Mathf.Max(0.01f, profile.swingPitch);
        toolAudioSource.PlayOneShot(clip, Mathf.Clamp01(profile.swingVolume));
    }

    private static AudioClip CreateProceduralSwingClip(EquippableItemType itemType)
    {
        const int sampleRate = 22050;
        const float duration = 0.16f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float weight = itemType == EquippableItemType.IndustrialHammer ||
                       itemType == EquippableItemType.Pickaxe
            ? 0.75f
            : 1f;

        System.Random random = new System.Random(1709 + (int)itemType * 97);
        for (int index = 0; index < sampleCount; index++)
        {
            float normalized = index / (float)sampleCount;
            float envelope = Mathf.Sin(normalized * Mathf.PI) * (1f - normalized);
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            samples[index] = noise * envelope * 0.09f * weight;
        }

        AudioClip clip = AudioClip.Create(
            $"Procedural_{itemType}_Swing",
            sampleCount,
            1,
            sampleRate,
            false);
        clip.SetData(samples, 0);
        return clip;
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

    private void PlayerHealth_OnDownedStateChanged(object sender, EventArgs e)
    {
        ResetLocomotionState();
    }

    private void ResetLocomotionState()
    {
        idleBobPhase = 0f;
        locomotionPhase = 0f;
        locomotionAmount = 0f;
        sprintBlend = 0f;
        previousPlayerPosition = transform.position;
        hasPreviousPlayerPosition = true;
    }

    private void DestroyArms()
    {
        DestroyFirstPersonCamera();

        if (armsRoot != null)
        {
            Destroy(armsRoot.gameObject);
            armsRoot = null;
            leftArmRoot = null;
            rightArmRoot = null;
            rightToolAnchor = null;
            spiritLevelToolAnchor = null;
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
