using System;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerInputNew), typeof(PlayerInventory), typeof(PlayerInteractionNew))]
public sealed class PlayerSpiritLevelController : NetworkBehaviour
{
    private readonly NetworkVariable<int> measuredComponentIdNetwork = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<int> measuredPointIdNetwork = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<sbyte> measuredViewSignNetwork = new NetworkVariable<sbyte>(1);

    private PlayerInputNew input;
    private PlayerInventory inventory;
    private PlayerInteractionNew interaction;
    private PlayerHealth health;
    private PlayerConcreteTrapController concreteTrap;
    private PlayerFirstPersonArms firstPersonArms;
    private PlayerEquippableItemVisuals thirdPersonVisuals;
    private int localMeasuredComponentId = -1;
    private int localMeasuredPointId = -1;
    private sbyte localMeasuredViewSign = 1;

    public bool IsMeasuring => MeasuredComponentId >= 0 && MeasuredPointIdValue >= 0;
    public SpiritLevelMeasurementAxis MeasuredAxis =>
        TryResolvePoint(MeasuredComponentId, MeasuredPointIdValue, out SpiritLevelMeasurementPoint point)
            ? point.Axis
            : SpiritLevelMeasurementAxis.Length;
    public SpiritLevelProfileSO SelectedProfile => IsSpiritLevelSelected() ? GetSelectedProfile() : null;
    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private int MeasuredComponentId => IsNetworkActive ? measuredComponentIdNetwork.Value : localMeasuredComponentId;
    private int MeasuredPointIdValue => IsNetworkActive ? measuredPointIdNetwork.Value : localMeasuredPointId;
    private sbyte MeasuredViewSign => IsNetworkActive ? measuredViewSignNetwork.Value : localMeasuredViewSign;

    private void Awake()
    {
        input = GetComponent<PlayerInputNew>();
        inventory = GetComponent<PlayerInventory>();
        interaction = GetComponent<PlayerInteractionNew>();
        health = GetComponent<PlayerHealth>();
        concreteTrap = GetComponent<PlayerConcreteTrapController>();
        firstPersonArms = GetComponent<PlayerFirstPersonArms>();
        thirdPersonVisuals = GetComponent<PlayerEquippableItemVisuals>();
    }

    private void OnEnable()
    {
        input.OnAction += HandleActionStarted;
        input.OnActionCanceled += HandleActionCanceled;
        inventory.OnSelectedItemChanged += HandleSelectedItemChanged;
    }

    private void OnDisable()
    {
        if (input != null)
        {
            input.OnAction -= HandleActionStarted;
            input.OnActionCanceled -= HandleActionCanceled;
        }
        if (inventory != null) inventory.OnSelectedItemChanged -= HandleSelectedItemChanged;
        ClearVisualPresentation();
    }

    private void Update()
    {
        if (IsNetworkActive && IsServer && IsMeasuring && !CanContinueServerMeasurement())
        {
            ClearServerMeasurement();
        }

        if (HasLocalControl() && IsMeasuring && !CanContinueLocalMeasurement())
        {
            RequestStopMeasurement();
        }
        UpdateVisualPresentation();
    }

    private void HandleActionStarted(object sender, EventArgs e)
    {
        if (!HasLocalControl() || !IsSpiritLevelSelected() || input.IsGameplayUiOpen ||
            health != null && health.IsDowned || concreteTrap != null && concreteTrap.IsTrapped ||
            interaction.CurrentTarget is not SpiritLevelMeasurementPoint point || !point.IsAvailable)
        {
            return;
        }

        SpiritLevelProfileSO profile = GetSelectedProfile();
        float range = profile != null ? profile.measurementRange : 2.5f;
        if (Vector3.Distance(transform.position, point.MeasurementPose.position) > range)
        {
            return;
        }

        int componentId = point.TargetSite != null && point.TargetSite.BridgeComponent != null
            ? point.TargetSite.BridgeComponent.ComponentID
            : -1;
        if (componentId < 0) return;

        sbyte viewSign = ResolveViewSign(point, interaction.AimCameraTransform != null
            ? interaction.AimCameraTransform.right
            : transform.right);

        if (IsNetworkActive)
        {
            BeginMeasurementServerRpc(componentId, point.PointId, viewSign);
        }
        else
        {
            localMeasuredComponentId = componentId;
            localMeasuredPointId = point.PointId;
            localMeasuredViewSign = viewSign;
        }
    }

    private void HandleActionCanceled(object sender, EventArgs e)
    {
        if (HasLocalControl() && IsMeasuring) RequestStopMeasurement();
    }

    private void HandleSelectedItemChanged(object sender, PlayerInventory.OnSelectedItemChangedEventArgs e)
    {
        if (HasLocalControl() && IsMeasuring && (e.selectedItem == null || e.selectedItem.itemType != EquippableItemType.SpiritLevel))
        {
            RequestStopMeasurement();
        }
    }

    [ServerRpc]
    private void BeginMeasurementServerRpc(int componentId, int pointId, sbyte requestedViewSign, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || pointId < 0 ||
            !TryResolvePoint(componentId, pointId, out SpiritLevelMeasurementPoint point) ||
            !point.IsAvailable)
        {
            return;
        }

        EquippableItemSO selectedItem = inventory.GetSelectedItemForServerValidation();
        SpiritLevelProfileSO profile = selectedItem != null ? selectedItem.spiritLevelProfile : null;
        float range = profile != null ? profile.measurementRange : 2.5f;
        if (selectedItem == null || selectedItem.itemType != EquippableItemType.SpiritLevel ||
            health != null && health.IsDowned || concreteTrap != null && concreteTrap.IsTrapped ||
            Vector3.Distance(transform.position, point.MeasurementPose.position) > range)
        {
            return;
        }

        measuredComponentIdNetwork.Value = componentId;
        measuredPointIdNetwork.Value = pointId;
        measuredViewSignNetwork.Value = ValidateViewSign(point, requestedViewSign);
    }

    [ServerRpc]
    private void StopMeasurementServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId == OwnerClientId) ClearServerMeasurement();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) ClearServerMeasurement();
        ClearVisualPresentation();
    }

    private void RequestStopMeasurement()
    {
        if (IsNetworkActive) StopMeasurementServerRpc();
        else
        {
            localMeasuredComponentId = -1;
            localMeasuredPointId = -1;
            localMeasuredViewSign = 1;
        }
    }

    internal void CancelForConcreteTrap()
    {
        if (IsNetworkActive)
        {
            if (IsServer) ClearServerMeasurement();
            else if (IsOwner) StopMeasurementServerRpc();
        }
        else
        {
            localMeasuredComponentId = -1;
            localMeasuredPointId = -1;
            localMeasuredViewSign = 1;
        }
        ClearVisualPresentation();
    }

    private void ClearServerMeasurement()
    {
        measuredComponentIdNetwork.Value = -1;
        measuredPointIdNetwork.Value = -1;
        measuredViewSignNetwork.Value = 1;
    }

    private bool CanContinueLocalMeasurement()
    {
        if (!IsSpiritLevelSelected() || input.IsGameplayUiOpen || health != null && health.IsDowned ||
            concreteTrap != null && concreteTrap.IsTrapped ||
            !TryResolvePoint(MeasuredComponentId, MeasuredPointIdValue, out SpiritLevelMeasurementPoint point) ||
            interaction.CurrentTarget != point || !point.IsAvailable)
        {
            return false;
        }

        SpiritLevelProfileSO profile = GetSelectedProfile();
        return Vector3.Distance(transform.position, point.MeasurementPose.position) <=
               (profile != null ? profile.measurementRange : 2.5f);
    }

    private bool CanContinueServerMeasurement()
    {
        if (!TryResolvePoint(MeasuredComponentId, MeasuredPointIdValue, out SpiritLevelMeasurementPoint point) ||
            !point.IsAvailable || health != null && health.IsDowned || concreteTrap != null && concreteTrap.IsTrapped)
        {
            return false;
        }

        EquippableItemSO selectedItem = inventory.GetSelectedItemForServerValidation();
        SpiritLevelProfileSO profile = selectedItem != null ? selectedItem.spiritLevelProfile : null;
        return selectedItem != null && selectedItem.itemType == EquippableItemType.SpiritLevel &&
               Vector3.Distance(transform.position, point.MeasurementPose.position) <=
               (profile != null ? profile.measurementRange : 2.5f);
    }

    private void UpdateVisualPresentation()
    {
        SpiritLevelMeasurementPoint point = null;
        bool hasPoint = IsMeasuring && TryResolvePoint(MeasuredComponentId, MeasuredPointIdValue, out point);
        float logicalTilt = 0f;
        if (hasPoint && point.TargetSite is ILevelingMeasurementTarget target)
        {
            // The vial communicates the correction side: hit the side indicated by the bubble.
            logicalTilt = -MeasuredViewSign * point.ReadingSign * target.GetLogicalTilt(point.Axis);
        }

        SpiritLevelProfileSO profile = GetSelectedProfile();
        foreach (SpiritLevelVial vial in GetComponentsInChildren<SpiritLevelVial>(true))
        {
            vial.Configure(profile);
            vial.SetMeasurement(hasPoint, logicalTilt);
        }

        firstPersonArms?.SetSpiritLevelMeasurement(
            hasPoint && HasLocalControl(),
            hasPoint ? point.Axis : SpiritLevelMeasurementAxis.Length);
        thirdPersonVisuals?.SetSpiritLevelMeasurementPose(hasPoint, hasPoint ? point.MeasurementPose : null);
    }

    private void ClearVisualPresentation()
    {
        foreach (SpiritLevelVial vial in GetComponentsInChildren<SpiritLevelVial>(true)) vial.SetMeasurement(false, 0f);
        firstPersonArms?.SetSpiritLevelMeasurement(false, SpiritLevelMeasurementAxis.Length);
        thirdPersonVisuals?.SetSpiritLevelMeasurementPose(false, null);
    }

    public bool IsMeasuringPoint(SpiritLevelMeasurementPoint point)
    {
        if (point == null || !IsMeasuring || point.TargetSite == null || point.TargetSite.BridgeComponent == null)
        {
            return false;
        }

        return point.TargetSite.BridgeComponent.ComponentID == MeasuredComponentId &&
               point.PointId == MeasuredPointIdValue;
    }

    private bool TryResolvePoint(int componentId, int pointId, out SpiritLevelMeasurementPoint point)
    {
        point = null;
        if (GameplayManager.Instance == null ||
            !GameplayManager.Instance.TryGetConstructionSite(componentId, out BridgeConstructionSite site) ||
            site is not ILevelingMeasurementTarget)
        {
            return false;
        }

        foreach (SpiritLevelMeasurementPoint candidate in site.GetComponentsInChildren<SpiritLevelMeasurementPoint>(true))
        {
            if (candidate != null && candidate.PointId == pointId)
            {
                if (point != null)
                {
                    return false;
                }
                point = candidate;
            }
        }
        return point != null;
    }

    private bool IsSpiritLevelSelected()
    {
        EquippableItemSO item = inventory != null ? inventory.GetCurrentSelectedItem() : null;
        return item != null && item.itemType == EquippableItemType.SpiritLevel;
    }

    private SpiritLevelProfileSO GetSelectedProfile()
    {
        EquippableItemSO item = inventory != null ? inventory.GetCurrentSelectedItem() : null;
        return item != null ? item.spiritLevelProfile : null;
    }

    private static sbyte ResolveViewSign(SpiritLevelMeasurementPoint point, Vector3 viewRight)
    {
        if (point == null) return 1;

        Vector3 direction = Vector3.ProjectOnPlane(point.PositiveTiltWorldDirection, Vector3.up);
        Vector3 right = Vector3.ProjectOnPlane(viewRight, Vector3.up);
        if (direction.sqrMagnitude <= 0.0001f || right.sqrMagnitude <= 0.0001f)
        {
            return (sbyte)point.FallbackViewSign;
        }

        float alignment = Vector3.Dot(direction.normalized, right.normalized);
        if (Mathf.Abs(alignment) < 0.15f)
        {
            return (sbyte)point.FallbackViewSign;
        }
        return alignment >= 0f ? (sbyte)1 : (sbyte)-1;
    }

    private sbyte ValidateViewSign(SpiritLevelMeasurementPoint point, sbyte requestedViewSign)
    {
        sbyte clampedRequest = requestedViewSign < 0 ? (sbyte)-1 : (sbyte)1;
        sbyte serverSign = ResolveViewSign(point, transform.right);

        Vector3 direction = Vector3.ProjectOnPlane(point.PositiveTiltWorldDirection, Vector3.up);
        Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up);
        if (direction.sqrMagnitude <= 0.0001f || right.sqrMagnitude <= 0.0001f ||
            Mathf.Abs(Vector3.Dot(direction.normalized, right.normalized)) < 0.15f)
        {
            return clampedRequest;
        }
        return serverSign;
    }

    private bool HasLocalControl()
    {
        return !IsNetworkActive || IsOwner;
    }
}
