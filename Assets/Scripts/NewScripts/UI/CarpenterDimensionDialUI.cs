using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CarpenterDimensionDialUI : MonoBehaviour
{
    [SerializeField] private CarpenterTableFactory factory;
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private RectTransform dialCenter;
    [SerializeField] private RectTransform marker;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text currentValueText;
    [SerializeField] private TMP_Text minimumValueText;
    [SerializeField] private TMP_Text maximumValueText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private float markerRadius = 132f;
    [SerializeField] private float degreesPerStep = 30f;
    [SerializeField] private float deniedMessageDuration = 1.5f;

    private PlayerInputNew localPlayerInput;
    private PlayerHealth localPlayerHealth;
    private ComponentDimension activeDimension;
    private bool isOpen;
    private bool isDragging;
    private float currentDragAngle;
    private float lastPointerAngle;
    private int previewStepIndex;
    private Coroutine deniedMessageCoroutine;

    private void Awake()
    {
        if (factory == null)
        {
            factory = GetComponentInParent<CarpenterTableFactory>();
        }

        if (marker != null)
        {
            CarpenterDimensionDialDragHandle dragHandle = marker.GetComponent<CarpenterDimensionDialDragHandle>();
            if (dragHandle == null)
            {
                dragHandle = marker.gameObject.AddComponent<CarpenterDimensionDialDragHandle>();
            }
            dragHandle.Initialize(this);
        }

        closeButton?.onClick.AddListener(Close);
        SetVisualVisible(false);
    }

    private void Start()
    {
        if (factory == null)
        {
            enabled = false;
            return;
        }

        factory.DimensionAdjustmentGranted += Factory_DimensionAdjustmentGranted;
        factory.DimensionAdjustmentDenied += Factory_DimensionAdjustmentDenied;
        factory.DimensionAdjustmentRevoked += Factory_DimensionAdjustmentRevoked;
        factory.DimensionStepRejected += Factory_DimensionStepRejected;
        factory.OnFactoryStateChanged += Factory_OnFactoryStateChanged;
    }

    private void OnDestroy()
    {
        if (factory != null)
        {
            factory.DimensionAdjustmentGranted -= Factory_DimensionAdjustmentGranted;
            factory.DimensionAdjustmentDenied -= Factory_DimensionAdjustmentDenied;
            factory.DimensionAdjustmentRevoked -= Factory_DimensionAdjustmentRevoked;
            factory.DimensionStepRejected -= Factory_DimensionStepRejected;
            factory.OnFactoryStateChanged -= Factory_OnFactoryStateChanged;
        }

        closeButton?.onClick.RemoveListener(Close);
        DetachPlayerInput();
    }

    public void Show(CarpenterTableFactory targetFactory, ComponentDimension dimension)
    {
        if (targetFactory == null)
        {
            return;
        }

        factory = targetFactory;
        activeDimension = dimension;
        ResolveLocalPlayer();
        if (localPlayerInput == null)
        {
            return;
        }

        isOpen = true;
        isDragging = false;
        previewStepIndex = factory.GetDimensionStepIndex(activeDimension);
        currentDragAngle = GetAngleForStep(previewStepIndex);
        RefreshLabels(previewStepIndex);
        SetMarkerAngle(currentDragAngle);
        SetStatus(string.Empty);
        SetVisualVisible(true);

        localPlayerInput.OnUI_Interact += PlayerInput_OnCloseRequested;
        localPlayerInput.OnUI_Back += PlayerInput_OnCloseRequested;
        if (localPlayerHealth != null)
        {
            localPlayerHealth.OnDownedStateChanged += PlayerHealth_OnDownedStateChanged;
        }

        localPlayerInput.SetGameplayUiOpen(true);
    }

    public void RefreshConfirmedValue()
    {
        if (!isOpen || factory == null || isDragging)
        {
            return;
        }

        previewStepIndex = factory.GetDimensionStepIndex(activeDimension);
        currentDragAngle = GetAngleForStep(previewStepIndex);
        SetMarkerAngle(currentDragAngle);
        RefreshLabels(previewStepIndex);
    }

    public void BeginDrag(PointerEventData eventData)
    {
        if (!isOpen || factory == null || dialCenter == null)
        {
            return;
        }

        isDragging = true;
        lastPointerAngle = GetPointerAngle(eventData);
    }

    public void Drag(PointerEventData eventData)
    {
        if (!isDragging || factory == null)
        {
            return;
        }

        float pointerAngle = GetPointerAngle(eventData);
        float angularDelta = Mathf.DeltaAngle(lastPointerAngle, pointerAngle);
        lastPointerAngle = pointerAngle;

        float maximumAngle = GetMaximumAngle();
        currentDragAngle = Mathf.Clamp(currentDragAngle + angularDelta, 0f, maximumAngle);
        SetMarkerAngle(currentDragAngle);

        int nearestStep = Mathf.Clamp(
            Mathf.RoundToInt(currentDragAngle / Mathf.Max(1f, degreesPerStep)),
            0,
            factory.GetDimensionStepCount(activeDimension) - 1);

        if (nearestStep != previewStepIndex)
        {
            previewStepIndex = nearestStep;
            RefreshCurrentValue(previewStepIndex);
            factory.RequestSetDimensionStep(activeDimension, previewStepIndex);
        }
    }

    public void EndDrag(PointerEventData eventData)
    {
        if (!isDragging)
        {
            return;
        }

        isDragging = false;
        currentDragAngle = GetAngleForStep(previewStepIndex);
        SetMarkerAngle(currentDragAngle);
        RefreshCurrentValue(previewStepIndex);
    }

    public void Close()
    {
        CloseInternal(true);
    }

    private void CloseInternal(bool releaseLock)
    {
        if (!isOpen)
        {
            return;
        }

        ComponentDimension dimensionToRelease = activeDimension;
        isOpen = false;
        isDragging = false;
        DetachPlayerInput();
        SetVisualVisible(false);

        if (releaseLock && factory != null)
        {
            factory.RequestEndDimensionAdjustment(dimensionToRelease);
        }
    }

    private void ResolveLocalPlayer()
    {
        DetachPlayerInput();

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening && networkManager.LocalClient != null)
        {
            NetworkObject playerObject = networkManager.LocalClient.PlayerObject;
            if (playerObject != null)
            {
                localPlayerInput = playerObject.GetComponent<PlayerInputNew>();
                localPlayerHealth = playerObject.GetComponent<PlayerHealth>();
            }
            return;
        }

        localPlayerInput = FindFirstObjectByType<PlayerInputNew>();
        localPlayerHealth = localPlayerInput != null ? localPlayerInput.GetComponent<PlayerHealth>() : null;
    }

    private void DetachPlayerInput()
    {
        if (localPlayerInput != null)
        {
            localPlayerInput.OnUI_Interact -= PlayerInput_OnCloseRequested;
            localPlayerInput.OnUI_Back -= PlayerInput_OnCloseRequested;
            localPlayerInput.SetGameplayUiOpen(false);
        }

        if (localPlayerHealth != null)
        {
            localPlayerHealth.OnDownedStateChanged -= PlayerHealth_OnDownedStateChanged;
        }

        localPlayerInput = null;
        localPlayerHealth = null;
    }

    private void Factory_DimensionAdjustmentGranted(object sender, CarpenterTableFactory.DimensionAdjustmentEventArgs e)
    {
        Show(factory, e.Dimension);
    }

    private void Factory_DimensionAdjustmentDenied(object sender, CarpenterTableFactory.DimensionAdjustmentDeniedEventArgs e)
    {
        if (deniedMessageCoroutine != null)
        {
            StopCoroutine(deniedMessageCoroutine);
        }
        deniedMessageCoroutine = StartCoroutine(ShowDeniedMessage(e.Dimension, e.Reason));
    }

    private void Factory_DimensionAdjustmentRevoked(object sender, CarpenterTableFactory.DimensionAdjustmentEventArgs e)
    {
        if (isOpen && e.Dimension == activeDimension)
        {
            CloseInternal(false);
        }
    }

    private void Factory_DimensionStepRejected(object sender, CarpenterTableFactory.DimensionAdjustmentEventArgs e)
    {
        if (!isOpen || e.Dimension != activeDimension)
        {
            return;
        }

        isDragging = false;
        RefreshConfirmedValue();
        SetStatus("Change rejected");
    }

    private void Factory_OnFactoryStateChanged(object sender, EventArgs e)
    {
        if (isOpen && factory.IsProducing)
        {
            CloseInternal(false);
            return;
        }

        RefreshConfirmedValue();
    }

    private void PlayerInput_OnCloseRequested(object sender, EventArgs e)
    {
        Close();
    }

    private void PlayerHealth_OnDownedStateChanged(object sender, EventArgs e)
    {
        if (localPlayerHealth != null && localPlayerHealth.IsDowned)
        {
            Close();
        }
    }

    private IEnumerator ShowDeniedMessage(ComponentDimension dimension, string reason)
    {
        activeDimension = dimension;
        RefreshLabels(factory.GetDimensionStepIndex(dimension));
        SetStatus(string.IsNullOrWhiteSpace(reason) ? "Crank is unavailable" : reason);
        SetVisualVisible(true);
        yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, deniedMessageDuration));
        if (!isOpen)
        {
            SetVisualVisible(false);
        }
        deniedMessageCoroutine = null;
    }

    private void RefreshLabels(int stepIndex)
    {
        string name = activeDimension == ComponentDimension.Width ? "Width" : "Length";
        if (titleText != null)
        {
            titleText.text = $"Adjust {name}";
        }

        if (minimumValueText != null)
        {
            minimumValueText.text = factory.GetDimensionMin(activeDimension).ToString("0.##");
        }

        if (maximumValueText != null)
        {
            maximumValueText.text = factory.GetDimensionMax(activeDimension).ToString("0.##");
        }

        RefreshCurrentValue(stepIndex);
    }

    private void RefreshCurrentValue(int stepIndex)
    {
        if (currentValueText == null || factory == null)
        {
            return;
        }

        string name = activeDimension == ComponentDimension.Width ? "Width" : "Length";
        currentValueText.text = $"{name}: {factory.GetDimensionValueForStep(activeDimension, stepIndex):0.##}";
    }

    private float GetPointerAngle(PointerEventData eventData)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                dialCenter,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return lastPointerAngle;
        }

        float angle = Mathf.Atan2(localPoint.x, localPoint.y) * Mathf.Rad2Deg;
        return Mathf.Repeat(angle, 360f);
    }

    private float GetAngleForStep(int stepIndex)
    {
        return Mathf.Clamp(stepIndex * Mathf.Max(1f, degreesPerStep), 0f, GetMaximumAngle());
    }

    private float GetMaximumAngle()
    {
        return Mathf.Min(300f, Mathf.Max(0f, factory.GetDimensionStepCount(activeDimension) - 1) * Mathf.Max(1f, degreesPerStep));
    }

    private void SetMarkerAngle(float angle)
    {
        if (marker == null)
        {
            return;
        }

        float radians = angle * Mathf.Deg2Rad;
        marker.anchoredPosition = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * markerRadius;
        marker.localRotation = Quaternion.Euler(0f, 0f, -angle);
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));
        }
    }

    private void SetVisualVisible(bool visible)
    {
        if (visualRoot != null)
        {
            visualRoot.SetActive(visible);
        }
    }
}
