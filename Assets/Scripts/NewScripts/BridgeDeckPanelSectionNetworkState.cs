using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public enum BridgeDeckPanelPlacementFailure : byte
{
    None,
    MissingQuarters,
    HorizontalOffset,
    HeightOffset,
    Orientation,
    SlotOccupied,
    NeighborPenetration,
    SectionNotConfigured
}

internal readonly struct BridgeDeckPanelPlacementAssessment
{
    public readonly BridgeDeckPanelSlotOutline Outline;
    public readonly BridgeDeckPanelPlacementFailure Failure;
    public readonly float AlongMeters;
    public readonly float AcrossMeters;
    public readonly float HeightMeters;
    public readonly float OrientationDegrees;
    public readonly bool HorizontalOutside;
    public readonly bool AlongOutside;
    public readonly bool AcrossOutside;
    public readonly bool HeightOutside;
    public readonly bool OrientationOutside;

    public BridgeDeckPanelPlacementAssessment(BridgeDeckPanelSlotOutline outline,
        BridgeDeckPanelPlacementFailure failure, float alongMeters, float acrossMeters,
        float heightMeters, float orientationDegrees, bool horizontalOutside,
        bool alongOutside, bool acrossOutside, bool heightOutside, bool orientationOutside)
    {
        Outline = outline;
        Failure = failure;
        AlongMeters = alongMeters;
        AcrossMeters = acrossMeters;
        HeightMeters = heightMeters;
        OrientationDegrees = orientationDegrees;
        HorizontalOutside = horizontalOutside;
        AlongOutside = alongOutside;
        AcrossOutside = acrossOutside;
        HeightOutside = heightOutside;
        OrientationOutside = orientationOutside;
    }
}

/// <summary>Replicated assessment for one physical panel; network id distinguishes same-prefab panels.</summary>
public struct BridgeDeckPanelReplicatedState : INetworkSerializable, System.IEquatable<BridgeDeckPanelReplicatedState>
{
    public ulong PanelNetworkObjectId;
    public int SlotIndex;
    public byte QuarterMask;
    public byte Failure;
    public bool Complete;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref PanelNetworkObjectId);
        serializer.SerializeValue(ref SlotIndex);
        serializer.SerializeValue(ref QuarterMask);
        serializer.SerializeValue(ref Failure);
        serializer.SerializeValue(ref Complete);
    }

    public bool Equals(BridgeDeckPanelReplicatedState other) => PanelNetworkObjectId == other.PanelNetworkObjectId
        && SlotIndex == other.SlotIndex
        && QuarterMask == other.QuarterMask && Failure == other.Failure && Complete == other.Complete;
}

/// <summary>Server-owned per-panel quarter assessments and durable section slot claims.</summary>
[DisallowMultipleComponent]
public sealed class BridgeDeckPanelSectionNetworkState : NetworkBehaviour
{
    private sealed class LocalPanelState
    {
        public BridgeDeckPanelMounting Panel;
        public BridgeDeckPanelReplicatedState State;
    }

    private static readonly List<BridgeDeckPanelSectionNetworkState> ActiveSections = new();
    private NetworkList<BridgeDeckPanelReplicatedState> replicatedPanels;
    private readonly List<MountingNail> activeNails = new(64);
    private readonly List<BridgeDeckPanelMounting> activePanels = new(16);
    private readonly List<LocalPanelState> localPanels = new(16);
    private readonly List<int> stalePanelIndices = new(16);
    private readonly Dictionary<MountableBridgeComponent, Collider[]> panelColliderCache = new();
    private readonly Collider[] overlapBuffer = new Collider[64];
    private BridgeDeckSection section;
    private BridgeDeckPanelConstructionWorkflowSO workflow;
    private bool subscribed;

    private void Awake() => EnsureNetworkList();

    public override void OnNetworkSpawn()
    {
        EnsureInitialized();
        SubscribeToReplicatedPanels();
        RefreshReplicatedVisuals();
        ReconcileCompletedSlotsWithGameplayManager();
    }

    public override void OnNetworkDespawn() => Unsubscribe();

    private void OnEnable()
    {
        EnsureNetworkList();
        if (!ActiveSections.Contains(this)) ActiveSections.Add(this);
        EnsureInitialized();
        if (IsSpawned) SubscribeToReplicatedPanels();
        ReconcileCompletedSlotsWithGameplayManager();
    }

    private void OnDisable()
    {
        ActiveSections.Remove(this);
        Unsubscribe();
    }

    public override void OnDestroy()
    {
        EnsureNetworkList();
        Unsubscribe();
        // NetworkBehaviour.OnDestroy initializes and disposes every NetworkVariableBase,
        // including this list when the object was never spawned; do not dispose it twice.
        base.OnDestroy();
    }

    private void SubscribeToReplicatedPanels()
    {
        EnsureNetworkList();
        if (subscribed) return;
        replicatedPanels.OnListChanged += OnReplicatedPanelsChanged;
        subscribed = true;
    }

    private void RefreshReplicatedVisuals()
    {
        if (!IsSpawned || NetworkManager == null) return;
        for (int i = 0; i < replicatedPanels.Count; i++)
        {
            BridgeDeckPanelReplicatedState state = replicatedPanels[i];
            if (TryGetPanelTransform(state.PanelNetworkObjectId, out Transform panel))
            {
                BridgeDeckPanelMounting marker = panel.GetComponent<BridgeDeckPanelMounting>();
                if (marker != null) marker.RaiseAssessmentChanged();
            }
            BridgeDeckPanelConstructionSite slot = GetSlot(state.SlotIndex);
            if (slot != null)
            {
                if (state.Complete) slot.ApplyPhysicalPanelCompletion();
                slot.RefreshPhysicalPlacementPrompt();
            }
        }
    }

    private void Update()
    {
        if (!HasEvaluationAuthority()) return;
        EnsureInitialized();
        if (section == null || workflow == null) return;
        MountingNail.CopyActiveNails(gameObject.scene, NetworkManager, activeNails);
        BridgeDeckPanelMounting.CopyActivePanels(gameObject.scene, activePanels);
        RemoveMissingIncompletePanels();
        for (int i = 0; i < activePanels.Count; i++)
        {
            BridgeDeckPanelMounting marker = activePanels[i];
            MountableBridgeComponent panel = marker != null ? marker.GetComponent<MountableBridgeComponent>() : null;
            if (panel == null || !panel.IsPhysicalDeckPanel || FindOwningSection(marker) != this) continue;
            if (IsSpawned && (panel.NetworkObject == null || !panel.NetworkObject.IsSpawned
                || panel.NetworkManager != NetworkManager)) continue;
            EvaluatePanel(marker, panel);
        }
    }

    public bool TryGetPanelState(BridgeDeckPanelMounting marker, out BridgeDeckPanelReplicatedState state)
    {
        if (marker == null)
        {
            state = default;
            return false;
        }
        NetworkObject networkObject = marker.GetComponent<NetworkObject>();
        if (IsSpawned && networkObject != null && networkObject.IsSpawned && networkObject.NetworkManager == NetworkManager)
        {
            ulong id = networkObject.NetworkObjectId;
            for (int i = 0; i < replicatedPanels.Count; i++)
            {
                state = replicatedPanels[i];
                if (state.PanelNetworkObjectId == id) return true;
            }
        }
        else
        {
            for (int i = 0; i < localPanels.Count; i++)
            {
                if (localPanels[i].Panel != marker) continue;
                state = localPanels[i].State;
                return true;
            }
        }
        state = default;
        return false;
    }

    public bool TryGetSlotAssessment(int slotIndex, out BridgeDeckPanelReplicatedState state)
    {
        float closest = float.PositiveInfinity;
        BridgeDeckPanelReplicatedState best = default;
        bool found = false;
        BridgeDeckPanelConstructionSite slot = GetSlot(slotIndex);
        if (slot == null) { state = default; return false; }
        if (IsSpawned)
        {
            for (int i = 0; i < replicatedPanels.Count; i++)
            {
                BridgeDeckPanelReplicatedState candidate = replicatedPanels[i];
                if (candidate.SlotIndex != slotIndex) continue;
                float distance = TryGetPanelTransform(candidate.PanelNetworkObjectId, out Transform panel)
                    ? (panel.position - slot.transform.position).sqrMagnitude : float.PositiveInfinity;
                if (distance < closest) { closest = distance; best = candidate; found = true; }
            }
        }
        else
        {
            for (int i = 0; i < localPanels.Count; i++)
            {
                BridgeDeckPanelReplicatedState candidate = localPanels[i].State;
                if (candidate.SlotIndex != slotIndex) continue;
                float distance = localPanels[i].Panel != null
                    ? (localPanels[i].Panel.transform.position - slot.transform.position).sqrMagnitude : float.PositiveInfinity;
                if (distance < closest) { closest = distance; best = candidate; found = true; }
            }
        }
        state = best;
        return found;
    }

    internal static bool TryGetAssessmentFor(BridgeDeckPanelMounting marker, out BridgeDeckPanelReplicatedState state)
    {
        if (marker != null && marker.TryGetComponent(out BridgeDeckAreaPanel _))
        { state = default; return false; }
        for (int i = 0; i < ActiveSections.Count; i++)
        {
            BridgeDeckPanelSectionNetworkState completedSection = ActiveSections[i];
            if (completedSection == null || !IsCompatibleNetworkScope(completedSection, marker)) continue;
            if (completedSection.TryGetPanelState(marker, out state) && state.Complete) return true;
        }
        BridgeDeckPanelSectionNetworkState owner = FindOwningSection(marker);
        if (owner != null && IsCompatibleNetworkScope(owner, marker) && owner.TryGetPanelState(marker, out state)) return true;
        state = default;
        return false;
    }

    internal static bool TryGetAssessmentFor(BridgeDeckPanelMounting marker, out BridgeDeckPanelReplicatedState state,
        out BridgeDeckPanelConstructionSite site)
    {
        if (marker != null && marker.TryGetComponent(out BridgeDeckAreaPanel _))
        { state = default; site = null; return false; }
        if (marker != null)
        {
            // Completed panels retain their original claim even after moving away from their section.
            for (int i = 0; i < ActiveSections.Count; i++)
            {
                BridgeDeckPanelSectionNetworkState candidate = ActiveSections[i];
                if (candidate == null || !IsCompatibleNetworkScope(candidate, marker)
                    || !candidate.TryGetPanelState(marker, out state) || !state.Complete) continue;
                site = candidate.GetSlot(state.SlotIndex);
                return site != null;
            }

            BridgeDeckPanelSectionNetworkState owner = FindOwningSection(marker);
            if (owner != null && IsCompatibleNetworkScope(owner, marker) && owner.TryGetPanelState(marker, out state))
            {
                site = owner.GetSlot(state.SlotIndex);
                return site != null;
            }
        }
        state = default;
        site = null;
        return false;
    }

    public bool IsSlotComplete(int slotIndex)
    {
        if (IsSpawned)
        {
            for (int i = 0; i < replicatedPanels.Count; i++)
                if (replicatedPanels[i].SlotIndex == slotIndex && replicatedPanels[i].Complete) return true;
            return false;
        }
        for (int i = 0; i < localPanels.Count; i++)
            if (localPanels[i].State.SlotIndex == slotIndex && localPanels[i].State.Complete) return true;
        return false;
    }

    internal bool IsCompletedSlot(BridgeDeckPanelConstructionSite site)
    {
        return site != null && section != null && section.PhysicalPanelState == this
            && GetSlot(site.SlotIndex) == site && IsSlotComplete(site.SlotIndex);
    }

    private void ReconcileCompletedSlotsWithGameplayManager()
    {
        if (section == null || section.PanelSlots == null) return;
        for (int i = 0; i < section.PanelSlots.Count; i++)
        {
            BridgeDeckPanelConstructionSite site = section.PanelSlots[i];
            if (!IsSlotComplete(i) || site == null) continue;
            site.ApplyPhysicalPanelCompletion();
            GameplayManager manager = GameplayManager.Instance;
            if (manager != null) manager.RecordPhysicalDeckPanelCompletion(this, site);
        }
    }

    internal static void ReconcileAllCompletedSlotsWithGameplayManager()
    {
        for (int i = ActiveSections.Count - 1; i >= 0; i--)
        {
            BridgeDeckPanelSectionNetworkState candidate = ActiveSections[i];
            if (candidate == null) { ActiveSections.RemoveAt(i); continue; }
            candidate.EnsureInitialized();
            candidate.ReconcileCompletedSlotsWithGameplayManager();
        }
    }

    internal static bool TryAssessPlacementPreview(MountableBridgeComponent panel, Vector3 candidatePosition,
        Quaternion candidateRotation, out BridgeDeckPanelPlacementAssessment assessment)
    {
        assessment = default;
        if (panel == null || !panel.IsPhysicalDeckPanel) return false;
        if (!panel.TryGetComponent(out BridgeDeckPanelMounting marker)) return false;

        BridgeDeckPanelSectionNetworkState bestSection = null;
        BridgeDeckPanelConstructionSite site = null;
        float closest = float.PositiveInfinity;
        for (int i = 0; i < ActiveSections.Count; i++)
        {
            BridgeDeckPanelSectionNetworkState candidate = ActiveSections[i];
            if (candidate == null || !IsCompatibleNetworkScope(candidate, marker)) continue;
            candidate.EnsureInitialized();
            if (candidate.section == null) continue;
            int slotIndex = candidate.FindNearestSlot(candidatePosition);
            BridgeDeckPanelConstructionSite candidateSite = candidate.GetSlot(slotIndex);
            if (candidateSite == null) continue;
            float distance = (candidatePosition - candidateSite.transform.position).sqrMagnitude;
            if (distance >= closest) continue;
            closest = distance;
            bestSection = candidate;
            site = candidateSite;
        }
        if (bestSection == null || site == null || bestSection.workflow == null) return false;

        BridgeDeckPanelConstructionWorkflowSO profile = bestSection.workflow;
        BridgeDeckPanelStage2Assessment.GetPoseError(site.transform, candidatePosition, candidateRotation,
            out float alongMeters, out float acrossMeters, out float heightMeters, out float orientationDegrees);
        bool alongOutside = Mathf.Abs(alongMeters) > profile.PhysicalPanelMaximumAlongOffset;
        bool acrossOutside = Mathf.Abs(acrossMeters) > profile.PhysicalPanelMaximumAcrossOffset;
        bool horizontalOutside = profile.PhysicalPanelUsesDirectionalHorizontalTolerance
            ? alongOutside || acrossOutside
            : new Vector2(alongMeters, acrossMeters).magnitude > profile.PhysicalPanelMaximumHorizontalOffset;
        if (!profile.PhysicalPanelUsesDirectionalHorizontalTolerance)
            alongOutside = acrossOutside = horizontalOutside;
        bool heightOutside = Mathf.Abs(heightMeters) > profile.PhysicalPanelMaximumHeightError;
        bool orientationOutside = orientationDegrees > profile.PhysicalPanelMaximumOrientationErrorDegrees;

        BridgeDeckPanelPoseFailure poseFailure = profile.PhysicalPanelUsesDirectionalHorizontalTolerance
            ? BridgeDeckPanelStage2Assessment.EvaluatePose(candidatePosition, candidateRotation, site.transform,
                profile.PhysicalPanelMaximumAlongOffset, profile.PhysicalPanelMaximumAcrossOffset,
                profile.PhysicalPanelMaximumHeightError, profile.PhysicalPanelMaximumOrientationErrorDegrees)
            : BridgeDeckPanelStage2Assessment.EvaluatePose(candidatePosition, candidateRotation, site.transform,
                profile.PhysicalPanelMaximumHorizontalOffset, profile.PhysicalPanelMaximumHeightError,
                profile.PhysicalPanelMaximumOrientationErrorDegrees);
        BridgeDeckPanelPlacementFailure failure = ToPlacementFailure(poseFailure);
        if (poseFailure == BridgeDeckPanelPoseFailure.None)
        {
            int slotIndex = bestSection.FindNearestSlot(candidatePosition);
            if (bestSection.IsClaimedByAnotherSlot(marker, slotIndex)) failure = BridgeDeckPanelPlacementFailure.SlotOccupied;
            else if (!bestSection.HasAcceptableNeighborPenetrationAtPose(panel, candidatePosition, candidateRotation))
                failure = BridgeDeckPanelPlacementFailure.NeighborPenetration;
        }
        BridgeDeckPanelSlotOutline outline = site.GetComponentInChildren<BridgeDeckPanelSlotOutline>(true);
        assessment = new BridgeDeckPanelPlacementAssessment(outline, failure, alongMeters, acrossMeters,
            heightMeters, orientationDegrees, horizontalOutside, alongOutside, acrossOutside,
            heightOutside, orientationOutside);
        return true;
    }

    private void EvaluatePanel(BridgeDeckPanelMounting marker, MountableBridgeComponent panel)
    {
        if (marker == null || panel == null || marker.TryGetComponent(out BridgeDeckAreaPanel _)) return;
        int slotIndex = FindNearestSlot(panel.transform.position);
        BridgeDeckPanelReplicatedState previous;
        bool hasPrevious = TryGetPanelState(marker, out previous);
        if (hasPrevious && previous.Complete) return;

        byte mask = CalculateQuarterMask(panel);
        BridgeDeckPanelPlacementFailure failure = BridgeDeckPanelPlacementFailure.MissingQuarters;
        BridgeDeckPanelPoseFailure poseFailure = BridgeDeckPanelPoseFailure.HorizontalOffset;
        BridgeDeckPanelConstructionSite site = GetSlot(slotIndex);
        if (site != null)
        {
            poseFailure = workflow.PhysicalPanelUsesDirectionalHorizontalTolerance
                ? BridgeDeckPanelStage2Assessment.EvaluatePose(panel.transform, site.transform,
                    workflow.PhysicalPanelMaximumAlongOffset, workflow.PhysicalPanelMaximumAcrossOffset,
                    workflow.PhysicalPanelMaximumHeightError, workflow.PhysicalPanelMaximumOrientationErrorDegrees)
                : BridgeDeckPanelStage2Assessment.EvaluatePose(panel.transform, site.transform,
                    workflow.PhysicalPanelMaximumHorizontalOffset, workflow.PhysicalPanelMaximumHeightError,
                    workflow.PhysicalPanelMaximumOrientationErrorDegrees);
            failure = ToPlacementFailure(poseFailure);
        }
        else
        {
            failure = BridgeDeckPanelPlacementFailure.SectionNotConfigured;
        }

        if (poseFailure == BridgeDeckPanelPoseFailure.None && site != null)
        {
            if (IsClaimedByAnotherSlot(marker, slotIndex)) failure = BridgeDeckPanelPlacementFailure.SlotOccupied;
            else if (!HasAcceptableNeighborPenetration(panel)) failure = BridgeDeckPanelPlacementFailure.NeighborPenetration;
            else if (mask == (byte)BridgeDeckPanelQuarterMask.All)
            {
                failure = BridgeDeckPanelPlacementFailure.None;
                CommitCompletion(marker, panel, slotIndex, mask);
                return;
            }
            else failure = BridgeDeckPanelPlacementFailure.MissingQuarters;
        }

        WritePanelState(marker, new BridgeDeckPanelReplicatedState
        {
            PanelNetworkObjectId = GetPanelNetworkObjectId(marker),
            SlotIndex = slotIndex,
            QuarterMask = mask,
            Failure = (byte)failure,
            Complete = false
        });
    }

    internal bool TryResetIncompletePanelWithHammer(BridgeDeckPanelMounting marker, Vector3 awayDirection)
    {
        if (marker == null || marker.TryGetComponent(out BridgeDeckAreaPanel _)
            || !marker.isActiveAndEnabled || !marker.gameObject.activeInHierarchy
            || !marker.IsPhysicalPanel || !HasEvaluationAuthority()
            || marker.ResetImpulsePending) return false;
        EnsureInitialized();
        if (section == null || workflow == null) return false;
        if (!marker.TryGetComponent(out MountableBridgeComponent panel)
            || !panel.TryGetComponent(out Rigidbody body)
            || !panel.IsPhysicalDeckPanel || panel.IsPickedUp
            || !panel.isActiveAndEnabled || !body.gameObject.activeInHierarchy
            || !IsFinite(awayDirection)) return false;
        if (panel.TryGetComponent(out BridgeDeckAreaPanel areaPanel)
            && (areaPanel.IsLockedForCompletedArea
                || BridgeDeckAreaRequirement.RefreshPanelBeforeNailRemoval(areaPanel.transform))) return false;

        // Completed claims stay durable even after moving the panel away from its original slot.
        if (HasDurableCompletionClaim(marker)) return false;
        if (FindOwningSection(marker) != this) return false;
        panel.TryGetComponent(out NetworkObject panelNetworkObject);
        if (IsSpawned && (!IsServer || panelNetworkObject == null || !panelNetworkObject.IsSpawned
            || panelNetworkObject.NetworkManager != NetworkManager)) return false;

        // A same-frame last nail may have changed the real pose and attachment state before Update
        // writes its list state. Take a fresh snapshot and run the production assessment first.
        MountingNail.CopyActiveNails(gameObject.scene, NetworkManager, activeNails);
        EvaluatePanel(marker, panel);
        if (HasDurableCompletionClaim(marker)) return false;

        awayDirection.y = 0f;
        if (awayDirection.sqrMagnitude < 0.000001f) return false;
        awayDirection.Normalize();
        if (!marker.TryBeginResetImpulse()) return false;

        bool networkedPanel = IsSpawned;
        NetworkManager resetNetworkManager = networkedPanel ? NetworkManager : null;
        ulong panelSpawnId = networkedPanel ? panelNetworkObject.NetworkObjectId : 0;
        Vector3 deltaVelocity = Vector3.up * workflow.PanelResetUpwardDeltaVelocity
            + awayDirection * workflow.PanelResetAwayDeltaVelocity;
        if (networkedPanel && panelNetworkObject.OwnerClientId != NetworkManager.ServerClientId)
        {
            List<NetworkTransform> transforms = new();
            List<Vector3> positions = new();
            List<Quaternion> rotations = new();
            List<Vector3> scales = new();
            foreach (NetworkTransform networkTransform in panelNetworkObject.GetComponentsInChildren<NetworkTransform>(true))
            {
                if (networkTransform == null || !networkTransform.IsSpawned
                    || networkTransform.NetworkObject != panelNetworkObject) continue;
                transforms.Add(networkTransform);
                positions.Add(networkTransform.GetSpaceRelativePosition());
                rotations.Add(networkTransform.GetSpaceRelativeRotation());
                scales.Add(networkTransform.transform.localScale);
            }
            panelNetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
            for (int i = 0; i < transforms.Count; i++)
                if (transforms[i] != null && transforms[i].IsSpawned)
                    transforms[i].Teleport(positions[i], rotations[i], scales[i]);
        }
        body.isKinematic = false;
        body.WakeUp();
        if (panel.IsPickedUp)
        {
            marker.EndResetImpulse();
            return false;
        }

        // A panel may have inherited the non-authoritative replica's gravity-disabled state
        // during ownership transfer. The accepted server reset makes it an unheld dynamic body.
        body.useGravity = true;

        MountingNail.CopyActiveNails(gameObject.scene, NetworkManager, activeNails);
        List<MountingNailConnectionRegistry> deferredRegistries = new(4);
        for (int i = 0; i < activeNails.Count; i++)
        {
            MountingNail nail = activeNails[i];
            if (nail == null || !nail.TryGetAttachmentInfo(out MountingNailAttachmentInfo info)) continue;
            bool targetsPanel = IsExactPanelTarget(info.FirstTarget, panel)
                || info.State == MountingNailState.EmbeddedPair && IsExactPanelTarget(info.SecondTarget, panel);
            if (!targetsPanel) continue;
            MountingNailConnectionRegistry registry = nail.RemoveForPanelReset(panelNetworkObject);
            if (registry != null && !deferredRegistries.Contains(registry)) deferredRegistries.Add(registry);
        }

        // RemovePending nails disappear from the active snapshot before this immediate reassessment.
        MountingNail.CopyActiveNails(gameObject.scene, NetworkManager, activeNails);
        EvaluatePanel(marker, panel);
        for (int i = 0; i < deferredRegistries.Count; i++)
            if (deferredRegistries[i] != null)
                deferredRegistries[i].ConsumeSavedOwnershipForPanelReset(panelNetworkObject);
        marker.StartResetImpulse(body, deltaVelocity, panel, panelNetworkObject,
            networkedPanel, resetNetworkManager, panelSpawnId);
        return true;
    }

    internal static bool TryResetPanelWithHammer(BridgeDeckPanelMounting marker, Vector3 awayDirection)
    {
        BridgeDeckPanelSectionNetworkState owner = FindOwningSection(marker);
        return owner != null && owner.TryResetIncompletePanelWithHammer(marker, awayDirection);
    }

    private bool HasDurableCompletionClaim(BridgeDeckPanelMounting marker)
    {
        for (int i = 0; i < ActiveSections.Count; i++)
        {
            BridgeDeckPanelSectionNetworkState candidate = ActiveSections[i];
            if (candidate != null && IsCompatibleNetworkScope(candidate, marker)
                && candidate.TryGetPanelState(marker, out BridgeDeckPanelReplicatedState state) && state.Complete)
                return true;
        }
        return false;
    }

    internal static bool IsPanelProtectedFromExtraction(Transform target)
    {
        if (target == null) return false;
        BridgeDeckAreaPanel areaPanel = target.GetComponentInParent<BridgeDeckAreaPanel>();
        if (areaPanel != null)
            return areaPanel.IsLockedForCompletedArea;
        BridgeDeckPanelMounting marker = target.GetComponentInParent<BridgeDeckPanelMounting>();
        MountableBridgeComponent panel = marker != null ? marker.GetComponent<MountableBridgeComponent>() : null;
        if (marker == null || panel == null || !panel.IsPhysicalDeckPanel) return false;

        // Completion is a durable slot claim, so movement or later section reconfiguration
        // must not make an already completed panel eligible for extraction.
        for (int i = 0; i < ActiveSections.Count; i++)
        {
            BridgeDeckPanelSectionNetworkState candidate = ActiveSections[i];
            if (candidate != null && IsCompatibleNetworkScope(candidate, marker)
                && candidate.TryGetPanelState(marker, out BridgeDeckPanelReplicatedState state) && state.Complete)
                return true;
        }

        BridgeDeckPanelSectionNetworkState owner = FindOwningSection(marker);
        if (owner == null || !owner.HasEvaluationAuthority() || owner.section == null || owner.workflow == null)
            return false;

        // A cached incomplete assessment can be stale within the frame that the final nail lands.
        // Reassess now so extraction cannot erase the quarter that completes and claims this panel.
        MountingNail.CopyActiveNails(owner.gameObject.scene, owner.NetworkManager, owner.activeNails);
        owner.EvaluatePanel(marker, panel);
        return owner.HasDurableCompletionClaim(marker);
    }

    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private byte CalculateQuarterMask(MountableBridgeComponent panel)
    {
        byte mask = 0;
        for (int i = 0; i < activeNails.Count; i++)
        {
            MountingNail nail = activeNails[i];
            if (nail == null || !nail.TryGetAttachmentInfo(out MountingNailAttachmentInfo info)) continue;
            if (!IsExactPanelTarget(info.FirstTarget, panel) || !IsMainGirderTarget(info.SecondTarget)) continue;
            if (BridgeDeckPanelStage2Assessment.TryGetNailQuarter(info, panel.transform, workflow.PhysicalPanelDimensions,
                    workflow.PhysicalPanelNailAxisExclusionHalfWidth, out BridgeDeckPanelQuarterMask quarter, out _))
                mask |= (byte)quarter;
        }
        return mask;
    }

    private static bool IsExactPanelTarget(Transform target, MountableBridgeComponent panel)
        => target != null && target.GetComponentInParent<MountableBridgeComponent>() == panel;

    private bool IsMainGirderTarget(Transform target)
    {
        if (target == null || section == null || section.MainGirders == null) return false;
        BridgeComponent physicalRoot = target.GetComponentInParent<BridgeComponent>();
        if (physicalRoot == null) return false;
        for (int i = 0; i < section.MainGirders.Count; i++)
            if (section.MainGirders[i] == physicalRoot) return true;
        return false;
    }

    private bool HasAcceptableNeighborPenetration(MountableBridgeComponent panel)
        => panel != null && HasAcceptableNeighborPenetrationAtPose(panel, panel.transform.position, panel.transform.rotation);

    private bool HasAcceptableNeighborPenetrationAtPose(MountableBridgeComponent panel,
        Vector3 candidatePosition, Quaternion candidateRotation)
    {
        if (panel == null || workflow == null) return false;
        if (!panelColliderCache.TryGetValue(panel, out Collider[] colliders))
        {
            colliders = panel.GetComponentsInChildren<Collider>(true);
            panelColliderCache.Add(panel, colliders);
        }
        Quaternion rotationDelta = candidateRotation * Quaternion.Inverse(panel.transform.rotation);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider own = colliders[i];
            if (own == null || !own.enabled || !own.gameObject.activeInHierarchy || own.isTrigger) continue;
            Bounds actualBounds = own.bounds;
            Vector3 ownCenter = candidatePosition + rotationDelta * (actualBounds.center - panel.transform.position);
            Vector3 ownPosition = candidatePosition + rotationDelta * (own.transform.position - panel.transform.position);
            Quaternion ownRotation = rotationDelta * own.transform.rotation;
            int count = Physics.OverlapBoxNonAlloc(ownCenter, actualBounds.extents + Vector3.one * 0.002f,
                overlapBuffer, rotationDelta, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count == overlapBuffer.Length) return false;
            for (int j = 0; j < count; j++)
            {
                Collider other = overlapBuffer[j];
                if (other == null || other == own || other.transform.IsChildOf(panel.transform)) continue;
                MountableBridgeComponent otherPanel = other.GetComponentInParent<MountableBridgeComponent>();
                if (otherPanel == null || otherPanel == panel || !otherPanel.IsPhysicalDeckPanel) continue;
                // Loopback clients share one physics scene in this process. Their replicated panel
                // colliders are not physical neighbors from this panel's authority's perspective.
                if (panel.IsSpawned && otherPanel.IsSpawned && panel.NetworkManager != otherPanel.NetworkManager) continue;
                if (Physics.ComputePenetration(own, ownPosition, ownRotation, other,
                        other.transform.position, other.transform.rotation, out _, out float depth)
                    && depth > workflow.PhysicalPanelMaximumNeighborPenetration) return false;
            }
        }
        return true;
    }

    private bool IsClaimedByAnotherSlot(BridgeDeckPanelMounting marker, int slotIndex)
    {
        for (int s = 0; s < ActiveSections.Count; s++)
        {
            BridgeDeckPanelSectionNetworkState otherSection = ActiveSections[s];
            if (otherSection == null || otherSection.gameObject.scene != gameObject.scene) continue;
            if (otherSection.IsSpawned && IsSpawned && otherSection.NetworkManager != NetworkManager) continue;
            if (otherSection.HasClaim(marker, slotIndex, otherSection == this)) return true;
        }
        return false;
    }

    private bool IsLegacySlotClaimed(int slotIndex)
    {
        BridgeDeckPanelConstructionSite site = GetSlot(slotIndex);
        if (site == null) return false;
        BridgeComponent bridge = site.GetComponent<BridgeComponent>();
        return site.IsComplete || bridge != null && bridge.StartFullyCompleted;
    }

    private bool HasClaim(BridgeDeckPanelMounting marker, int slotIndex, bool checkSlot)
    {
        if (checkSlot && IsLegacySlotClaimed(slotIndex)) return true;
        if (IsSpawned)
        {
            ulong id = GetPanelNetworkObjectId(marker);
            for (int i = 0; i < replicatedPanels.Count; i++)
            {
                BridgeDeckPanelReplicatedState entry = replicatedPanels[i];
                if (entry.Complete && (entry.PanelNetworkObjectId == id || checkSlot && entry.SlotIndex == slotIndex)) return true;
            }
        }
        else
        {
            for (int i = 0; i < localPanels.Count; i++)
            {
                BridgeDeckPanelReplicatedState entry = localPanels[i].State;
                if (entry.Complete && (localPanels[i].Panel == marker || checkSlot && entry.SlotIndex == slotIndex)) return true;
            }
        }
        return false;
    }

    private void CommitCompletion(BridgeDeckPanelMounting marker, MountableBridgeComponent panel, int slotIndex, byte mask)
    {
        if (!HasEvaluationAuthority() || IsClaimedByAnotherSlot(marker, slotIndex)) return;
        if (IsSpawned)
        {
            NetworkObject panelObject = panel.NetworkObject;
            if (!IsServer || panelObject == null || !panelObject.IsSpawned || panelObject.NetworkManager != NetworkManager) return;
        }
        WritePanelState(marker, new BridgeDeckPanelReplicatedState
        {
            PanelNetworkObjectId = GetPanelNetworkObjectId(marker),
            SlotIndex = slotIndex,
            QuarterMask = mask,
            Failure = (byte)BridgeDeckPanelPlacementFailure.None,
            Complete = true
        });
        BridgeDeckPanelConstructionSite site = GetSlot(slotIndex);
        if (site != null)
        {
            site.ApplyPhysicalPanelCompletion();
            GameplayManager manager = GameplayManager.Instance;
            if (manager != null) manager.RecordPhysicalDeckPanelCompletion(this, site);
        }
    }

    private void WritePanelState(BridgeDeckPanelMounting marker, BridgeDeckPanelReplicatedState next)
    {
        if (IsSpawned)
        {
            if (!IsServer) return;
            int index = FindReplicatedPanel(next.PanelNetworkObjectId);
            if (index < 0) replicatedPanels.Add(next);
            else if (!replicatedPanels[index].Equals(next)) replicatedPanels[index] = next;
            return;
        }
        int localIndex = FindLocalPanel(marker);
        if (localIndex < 0) localPanels.Add(new LocalPanelState { Panel = marker, State = next });
        else
        {
            if (localPanels[localIndex].State.Equals(next)) return;
            localPanels[localIndex].State = next;
        }
        marker.RaiseAssessmentChanged();
    }

    private void RemoveMissingIncompletePanels()
    {
        stalePanelIndices.Clear();
        if (IsSpawned)
        {
            if (!IsServer) return;
            for (int i = 0; i < replicatedPanels.Count; i++)
            {
                BridgeDeckPanelReplicatedState state = replicatedPanels[i];
                if (state.Complete) continue;
                bool active = false;
                for (int p = 0; p < activePanels.Count; p++)
                    if (GetPanelNetworkObjectId(activePanels[p]) == state.PanelNetworkObjectId
                        && FindOwningSection(activePanels[p]) == this) { active = true; break; }
                if (!active) stalePanelIndices.Add(i);
            }
            for (int i = stalePanelIndices.Count - 1; i >= 0; i--) replicatedPanels.RemoveAt(stalePanelIndices[i]);
            return;
        }
        for (int i = localPanels.Count - 1; i >= 0; i--)
        {
            LocalPanelState local = localPanels[i];
            if (local.State.Complete) continue;
            if (local.Panel == null || !local.Panel.isActiveAndEnabled || FindOwningSection(local.Panel) != this)
                localPanels.RemoveAt(i);
        }
    }

    private int FindNearestSlot(Vector3 position)
    {
        int result = -1;
        float closest = float.PositiveInfinity;
        IReadOnlyList<BridgeDeckPanelConstructionSite> slots = section != null ? section.PanelSlots : null;
        if (slots == null) return result;
        for (int i = 0; i < slots.Count; i++)
        {
            BridgeDeckPanelConstructionSite candidate = slots[i];
            if (candidate == null || !candidate.UsesPhysicalPanelPlacement) continue;
            float distance = (position - candidate.transform.position).sqrMagnitude;
            if (distance < closest) { closest = distance; result = i; }
        }
        return result;
    }

    private BridgeDeckPanelConstructionSite GetSlot(int slotIndex)
    {
        IReadOnlyList<BridgeDeckPanelConstructionSite> slots = section != null ? section.PanelSlots : null;
        return slots != null && slotIndex >= 0 && slotIndex < slots.Count ? slots[slotIndex] : null;
    }

    private static BridgeDeckPanelSectionNetworkState FindOwningSection(BridgeDeckPanelMounting panel)
    {
        if (panel == null || panel.TryGetComponent(out BridgeDeckAreaPanel _)) return null;
        BridgeDeckPanelSectionNetworkState best = null;
        float closest = float.PositiveInfinity;
        for (int s = 0; s < ActiveSections.Count; s++)
        {
            BridgeDeckPanelSectionNetworkState candidate = ActiveSections[s];
            if (candidate == null || !IsCompatibleNetworkScope(candidate, panel)) continue;
            candidate.EnsureInitialized();
            if (candidate.section == null) continue;
            int slot = candidate.FindNearestSlot(panel.transform.position);
            BridgeDeckPanelConstructionSite site = candidate.GetSlot(slot);
            if (site == null) continue;
            float distance = (panel.transform.position - site.transform.position).sqrMagnitude;
            if (distance < closest) { closest = distance; best = candidate; }
        }
        return best;
    }

    private static bool IsCompatibleNetworkScope(BridgeDeckPanelSectionNetworkState candidate, BridgeDeckPanelMounting panel)
    {
        if (candidate == null || panel == null || panel.TryGetComponent(out BridgeDeckAreaPanel _)
            || candidate.gameObject.scene != panel.gameObject.scene) return false;
        MountableBridgeComponent mountable = panel.GetComponent<MountableBridgeComponent>();
        NetworkManager panelManager = mountable != null ? mountable.NetworkManager : null;
        NetworkManager sectionManager = candidate.NetworkManager;
        bool panelListening = panelManager != null && panelManager.IsListening;
        bool sectionListening = sectionManager != null && sectionManager.IsListening;
        return panelListening == sectionListening && (!panelListening || panelManager == sectionManager);
    }

    private int FindReplicatedPanel(ulong networkId)
    {
        for (int i = 0; i < replicatedPanels.Count; i++) if (replicatedPanels[i].PanelNetworkObjectId == networkId) return i;
        return -1;
    }

    private bool TryGetPanelTransform(ulong networkId, out Transform panelTransform)
    {
        panelTransform = null;
        if (networkId == 0 || NetworkManager == null || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkId, out NetworkObject panelObject))
            return false;
        panelTransform = panelObject.transform;
        return true;
    }

    private int FindLocalPanel(BridgeDeckPanelMounting marker)
    {
        for (int i = 0; i < localPanels.Count; i++) if (localPanels[i].Panel == marker) return i;
        return -1;
    }

    private static ulong GetPanelNetworkObjectId(BridgeDeckPanelMounting marker)
    {
        NetworkObject networkObject = marker != null ? marker.GetComponent<NetworkObject>() : null;
        return networkObject != null && networkObject.IsSpawned ? networkObject.NetworkObjectId : 0;
    }

    private bool HasEvaluationAuthority()
    {
        if (IsSpawned) return IsServer;
        return NetworkManager == null || !NetworkManager.IsListening;
    }

    private void EnsureNetworkList()
    {
        if (replicatedPanels == null) replicatedPanels = new NetworkList<BridgeDeckPanelReplicatedState>();
    }

    private void EnsureInitialized()
    {
        if (section == null) section = GetComponentInParent<BridgeDeckSection>();
        if (section != null && section.PanelSlots != null && section.PanelSlots.Count > 0 && section.PanelSlots[0] != null)
        {
            BridgeComponent component = section.PanelSlots[0].GetComponent<BridgeComponent>();
            workflow = component != null && component.GetBridgeComponentSO() != null
                ? component.GetBridgeComponentSO().deckPanelConstructionWorkflow : null;
        }
    }

    private void OnReplicatedPanelsChanged(NetworkListEvent<BridgeDeckPanelReplicatedState> changeEvent)
    {
        BridgeDeckPanelReplicatedState state = changeEvent.Value;
        NetworkObject panelObject = NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(state.PanelNetworkObjectId, out NetworkObject resolved)
            ? resolved : null;
        if (panelObject != null)
        {
            BridgeDeckPanelMounting marker = panelObject.GetComponent<BridgeDeckPanelMounting>();
            if (marker != null) marker.RaiseAssessmentChanged();
        }
        BridgeDeckPanelConstructionSite site = GetSlot(state.SlotIndex);
        if (site != null)
        {
            if (state.Complete)
            {
                site.ApplyPhysicalPanelCompletion();
                GameplayManager manager = GameplayManager.Instance;
                if (manager != null) manager.RecordPhysicalDeckPanelCompletion(this, site);
            }
            site.RefreshPhysicalPlacementPrompt();
        }
    }

    private static BridgeDeckPanelPlacementFailure ToPlacementFailure(BridgeDeckPanelPoseFailure failure) => failure switch
    {
        BridgeDeckPanelPoseFailure.None => BridgeDeckPanelPlacementFailure.None,
        BridgeDeckPanelPoseFailure.HorizontalOffset => BridgeDeckPanelPlacementFailure.HorizontalOffset,
        BridgeDeckPanelPoseFailure.HeightOffset => BridgeDeckPanelPlacementFailure.HeightOffset,
        _ => BridgeDeckPanelPlacementFailure.Orientation
    };

    private void Unsubscribe()
    {
        if (!subscribed || replicatedPanels == null) return;
        replicatedPanels.OnListChanged -= OnReplicatedPanelsChanged;
        subscribed = false;
    }
}
