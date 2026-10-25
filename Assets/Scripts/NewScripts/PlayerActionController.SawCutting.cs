using Unity.Netcode;
using UnityEngine;

public partial class PlayerActionController
{
    private bool sawPrimaryHeld;
    private bool sawBlockedUntilRelease;
    private BridgeDeckAreaPanel activeSawTarget;
    private BridgeDeckAreaPanel previewSawTarget;
    private LineRenderer sawPreviewLine;
    private Material sawPreviewMaterial;
    private readonly RaycastHit[] sawAimHits = new RaycastHit[24];
    private uint sawRequestSequence;
    private uint sawActiveSequence;
    private float nextSawRefreshTime;
    private float sawBeginWaitUntil;
    private int previewSawOffsetMillimeters;
    private BridgeDeckAreaCut previewSawCut;
    private bool sawPreviewValid;

    public SawCutProfileSO SelectedSawProfile
    {
        get
        {
            EquippableItemSO item = _inventory != null ? _inventory.GetSelectedItemForServerValidation() : null;
            return item != null && item.itemType == EquippableItemType.Saw ? item.sawCutProfile : null;
        }
    }

    private bool IsSawSelected()
    {
        EquippableItemSO item = _inventory != null ? _inventory.GetCurrentSelectedItem() : null;
        return item != null && item.itemType == EquippableItemType.Saw;
    }

    private bool CanUseSawPrimaryInput()
    {
        if (_playerInteraction == null) return false;
        return !_playerInteraction.HasPickedUpObject && !_playerInteraction.IsSharedCarryMovementActive
            && !_playerInteraction.IsFlexibleSaplingInteractionActive && !_playerInteraction.IsBucketActionHoldActive
            && !_playerInteraction.HasContextActionConsumer;
    }

    private void BeginSawCutOnFreshPress()
    {
        if (sawPrimaryHeld) return;
        sawPrimaryHeld = true;
        sawBlockedUntilRelease = false;
        if (!HasLocalActionAuthority() || !isActiveAndEnabled || !IsSawSelected()
            || !CanUseSawPrimaryInput()
            || _playerInputNew == null || !_playerInputNew.IsLocalGameplayInputActive
            || IsDowned() || _concreteTrap != null && _concreteTrap.BlocksGameplayInput
            || performAction || IsActionInProgress || !TryResolveSawPreview(out BridgeDeckAreaPanel panel,
                out int freshOffsetMillimeters, out _, out bool freshSplitValid) || !freshSplitValid)
        {
            sawBlockedUntilRelease = true;
            return;
        }

        SawCutProfileSO profile = SelectedSawProfile;
        if (profile == null || !IsOwnNetworkSessionActive() && !panel.IsEligible(profile, true))
        {
            sawBlockedUntilRelease = true;
            return;
        }

        activeSawTarget = panel;
        uint sequence = NextSawSequence();
        sawActiveSequence = sequence;
        if (!IsOwnNetworkSessionActive())
        {
            ulong actorId = OfflineSawActorId;
            bool started = panel.CutStarted
                ? panel.ResumeCutOffline(actorId, profile)
                : panel.TryBeginCutOffline(freshOffsetMillimeters, actorId, profile);
            if (!started) sawBlockedUntilRelease = true;
            return;
        }

        if (panel.IsSpawned)
        {
            Ray ray = GetSawAimRay();
            panel.RequestBeginCutServerRpc(_networkObject.NetworkObjectId, ray.origin, ray.direction, sequence);
            sawBeginWaitUntil = Time.unscaledTime + .4f;
            nextSawRefreshTime = Time.unscaledTime + .1f;
        }
        else sawBlockedUntilRelease = true;
    }

    protected void LateUpdate()
    {
        UpdateSawCutting();
    }

    private void UpdateSawCutting()
    {
        bool activeTool = IsSawSelected();
        BridgeDeckAreaPanel previewPanel = null;
        sawPreviewValid = activeTool && HasLocalActionAuthority() && isActiveAndEnabled
            && CanUseSawPrimaryInput()
            && _playerInputNew != null && _playerInputNew.IsLocalGameplayInputActive
            && !IsDowned() && (_concreteTrap == null || !_concreteTrap.BlocksGameplayInput)
            && TryResolveSawPreview(out previewPanel, out previewSawOffsetMillimeters, out previewSawCut, out _);
        if (sawPreviewValid)
        {
            previewSawTarget = previewPanel;
            DrawSawPreview(previewPanel, previewSawOffsetMillimeters);
        }
        else
        {
            if (sawPreviewLine != null) sawPreviewLine.enabled = false;
            if (sawPrimaryHeld && !sawBlockedUntilRelease) InterruptSawCutting();
        }

        if (!sawPrimaryHeld || sawBlockedUntilRelease || activeSawTarget == null) return;
        if (!sawPreviewValid || previewPanel != activeSawTarget)
        {
            InterruptSawCutting();
            return;
        }
        SawCutProfileSO profile = SelectedSawProfile;
        if (profile == null || !IsOwnNetworkSessionActive() && !activeSawTarget.IsEligible(profile, true))
        {
            InterruptSawCutting();
            return;
        }

        if (!IsOwnNetworkSessionActive())
        {
            ulong actorId = OfflineSawActorId;
            if (!activeSawTarget.CutStarted || activeSawTarget.State.OperatorClientId != actorId)
            {
                InterruptSawCutting();
                return;
            }
            activeSawTarget.AdvanceCutOffline(actorId, Time.deltaTime, profile);
            if (activeSawTarget.IsCutComplete(profile))
            {
                activeSawTarget.TryCompleteOfflineCut(profile);
                sawBlockedUntilRelease = true;
                sawPrimaryHeld = false;
                activeSawTarget = null;
            }
            return;
        }

        if (Time.unscaledTime < nextSawRefreshTime) return;
        nextSawRefreshTime = Time.unscaledTime + .1f;
        Ray ray = GetSawAimRay();
        BridgeDeckAreaPanelSnapshot state = activeSawTarget.State;
        if (state.OperatorNetworkObjectId != _networkObject.NetworkObjectId
            || state.OperatorRequestSequence != sawActiveSequence)
        {
            if (Time.unscaledTime > sawBeginWaitUntil) InterruptSawCutting();
            return;
        }
        activeSawTarget.RefreshCutServerRpc(_networkObject.NetworkObjectId, ray.origin, ray.direction, sawActiveSequence);
    }

    private void InterruptSawCutting()
    {
        if (sawPrimaryHeld && activeSawTarget != null && IsOwnNetworkSessionActive() && activeSawTarget.IsSpawned)
            activeSawTarget.PauseCutServerRpc(_networkObject.NetworkObjectId, sawActiveSequence);
        else if (sawPrimaryHeld && activeSawTarget != null && !IsOwnNetworkSessionActive())
        {
            activeSawTarget.PauseCutOffline(OfflineSawActorId);
        }
        sawBlockedUntilRelease = true;
        if (sawPreviewLine != null) sawPreviewLine.enabled = false;
    }

    private void CancelSawCuttingImmediately()
    {
        if (sawPrimaryHeld) InterruptSawCutting();
        sawPrimaryHeld = false;
        sawBlockedUntilRelease = false;
        sawActiveSequence = 0;
        activeSawTarget = null;
        if (sawPreviewLine != null) sawPreviewLine.enabled = false;
    }

    private bool TryResolveSawPreview(out BridgeDeckAreaPanel panel, out int offsetMillimeters,
        out BridgeDeckAreaCut cut, out bool splitValid)
    {
        panel = null; offsetMillimeters = 0; cut = default; splitValid = false;
        EquippableItemSO item = _inventory != null ? _inventory.GetCurrentSelectedItem() : null;
        if (item == null || item.itemType != EquippableItemType.Saw || item.sawCutProfile == null
            || _playerInteraction == null || !_playerInteraction.TryGetAimRay(out Ray ray)) return false;
        ray = GetSawAimRay();
        RaycastHit[] hits = GetSawAimHits(ray, item.sawCutProfile.maximumRangeMeters, out int hitCount);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.collider.transform.root == transform.root
                || !BelongsToThisNetworkSession(hit.collider)) continue;
            panel = hit.collider.GetComponentInParent<BridgeDeckAreaPanel>();
            if (panel == null) return false;
            Vector3 local = panel.transform.InverseTransformPoint(hit.point);
            offsetMillimeters = Mathf.RoundToInt((local.x + panel.LengthMillimeters * .0005f) * 1000f);
            if (panel.CutStarted) offsetMillimeters = panel.CutOffsetMillimeters;
            splitValid = panel.CutStarted || panel.TryGetCut(offsetMillimeters, item.sawCutProfile, out cut);
            return true;
        }
        return false;
    }

    private Ray GetSawAimRay()
    {
        if (_playerInteraction != null && _playerInteraction.TryGetAimRay(out Ray viewportRay,
                out Camera sourceCamera, out _))
            return new Ray(sourceCamera != null ? sourceCamera.transform.position : viewportRay.origin,
                viewportRay.direction);
        return new Ray(transform.position, transform.forward);
    }

    private void DrawSawPreview(BridgeDeckAreaPanel panel, int offsetMillimeters)
    {
        if (sawPreviewLine == null)
        {
            SawCutProfileSO sawProfile = SelectedSawProfile;
            Material sourceMaterial = sawProfile != null ? sawProfile.previewLineMaterial : null;
            if (sourceMaterial == null) return;
            GameObject lineObject = new("SawCutPreview");
            lineObject.transform.SetParent(transform, false);
            sawPreviewLine = lineObject.AddComponent<LineRenderer>();
            sawPreviewLine.positionCount = 2;
            sawPreviewLine.useWorldSpace = true;
            sawPreviewLine.startWidth = sawPreviewLine.endWidth = .018f;
            sawPreviewMaterial = new Material(sourceMaterial) { color = new Color(1f, .8f, .2f, .95f) };
            sawPreviewLine.sharedMaterial = sawPreviewMaterial;
        }
        float x = offsetMillimeters * .001f - panel.LengthMillimeters * .0005f;
        float width = panel.WidthMillimeters * .001f;
        float top = panel.ThicknessMillimeters * .001f + .015f;
        sawPreviewLine.enabled = true;
        sawPreviewLine.SetPosition(0, panel.transform.TransformPoint(new Vector3(x, top, -width * .5f)));
        sawPreviewLine.SetPosition(1, panel.transform.TransformPoint(new Vector3(x, top, width * .5f)));
    }

    private uint NextSawSequence()
    {
        sawRequestSequence = unchecked(sawRequestSequence + 1);
        if (sawRequestSequence == 0) sawRequestSequence = 1;
        return sawRequestSequence;
    }

    public bool ServerSawRequestIsValid(BridgeDeckAreaPanel target, Vector3 origin, Vector3 direction,
        ulong senderClientId)
        => ServerSawRequestIsValid(target, origin, direction, senderClientId, out _);

    public bool ServerSawRequestIsValid(BridgeDeckAreaPanel target, Vector3 origin, Vector3 direction,
        ulong senderClientId, out Vector3 hitPoint)
    {
        hitPoint = default;
        if (target == null || !IsServer || NetworkManager == null || OwnerClientId != senderClientId
            || _inventory == null || _playerHealth != null && _playerHealth.IsDowned
            || _concreteTrap != null && _concreteTrap.BlocksGameplayInput
            || !CanUseSawPrimaryInput()
            || !IsFinite(origin) || !IsFinite(direction) || direction.sqrMagnitude < .99f || direction.sqrMagnitude > 1.01f
            || !IsTrustedMountingNailAimOrigin(origin, direction.normalized))
            return false;
        EquippableItemSO item = _inventory.GetSelectedItemForServerValidation();
        if (item == null || item.itemType != EquippableItemType.Saw || item.sawCutProfile == null) return false;
        float range = item.sawCutProfile.maximumRangeMeters;
        RaycastHit[] hits = GetSawAimHits(new Ray(origin, direction.normalized), range, out int hitCount);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.collider.transform.root == transform.root
                || !BelongsToThisNetworkSession(hit.collider)) continue;
            if (hit.collider.GetComponentInParent<BridgeDeckAreaPanel>() != target) return false;
            hitPoint = hit.point;
            return true;
        }
        return false;
    }

    public bool TryGetSawCutPresentation(MonoBehaviour target, out string text, out float progress, out bool active)
    {
        text = null; progress = 0f; active = false;
        if (!IsSawSelected() || !CanUseSawPrimaryInput() || _playerInputNew == null
            || !_playerInputNew.IsLocalGameplayInputActive || IsDowned()
            || _concreteTrap != null && _concreteTrap.BlocksGameplayInput
            || performAction || IsActionInProgress
            || !TryResolveSawPreview(out BridgeDeckAreaPanel panel,
                out int offset, out BridgeDeckAreaCut cut, out bool splitValid)
            || target != null && target.GetComponentInParent<BridgeDeckAreaPanel>() != panel) return false;
        previewSawTarget = panel;
        active = panel.CutStarted;
        SawCutProfileSO profile = SelectedSawProfile;
        bool networkSessionActive = panel.IsSpawned && panel.NetworkManager != null
            && panel.NetworkManager.IsListening;
        bool clientReplica = networkSessionActive && !panel.NetworkManager.IsServer;
        string unavailableReason = profile == null ? null
            : clientReplica
                ? panel.GetSawCutUnavailableReasonForClientReplica(profile)
                : panel.GetSawCutUnavailableReason(profile);
        if (panel.CutStarted)
        {
            BridgeDeckAreaPanelSnapshot state = panel.State;
            bool operatorActive = state.OperatorClientId != ulong.MaxValue;
            bool localOperator = operatorActive && IsLocalSawOperator(panel, state.OperatorClientId);
            string progressText = $"Saw: {panel.ProgressMilliseconds / 1000f:0.0} / {(profile != null ? profile.activeCutSeconds : 4f):0.0}s";
            string dimensionsText = profile != null && panel.TryGetFragmentLengths(profile, out BridgeDeckAreaCut activeCut)
                ? $"Cut at {panel.CutOffsetMillimeters} mm — A {activeCut.FirstLengthMillimeters} mm + B {activeCut.SecondLengthMillimeters} mm"
                : null;
            text = !string.IsNullOrEmpty(unavailableReason)
                ? $"Saw: cut unavailable — {unavailableReason}; {progressText}"
                    + (string.IsNullOrEmpty(dimensionsText) ? string.Empty : $"\n{dimensionsText}")
                : operatorActive && !localOperator
                    ? $"{progressText}; another player is cutting"
                    : operatorActive
                        ? $"{progressText}; hold LMB to continue"
                        : $"{progressText}; hold LMB to resume";
        }
        else text = splitValid
                ? !string.IsNullOrEmpty(unavailableReason)
                    ? $"Saw: cut unavailable — {unavailableReason}\nCut at {offset} mm — A {cut.FirstLengthMillimeters} mm + B {cut.SecondLengthMillimeters} mm"
                    : $"Saw: hold LMB at {offset} mm — A {cut.FirstLengthMillimeters} mm + B {cut.SecondLengthMillimeters} mm"
                : $"Saw: cut unavailable — each piece must be at least {panel.MinimumFragmentLengthMillimeters(SelectedSawProfile)} mm";
        progress = profile != null && profile.activeCutSeconds > 0f
            ? Mathf.Clamp01(panel.ProgressMilliseconds / 1000f / profile.activeCutSeconds) : 0f;
        return true;
    }

    private bool IsLocalSawOperator(BridgeDeckAreaPanel panel, ulong operatorClientId)
    {
        bool networkSessionActive = panel != null && panel.IsSpawned && panel.NetworkManager != null
            && panel.NetworkManager.IsListening;
        return networkSessionActive
            ? operatorClientId == panel.NetworkManager.LocalClientId
            : operatorClientId == OfflineSawActorId;
    }

    private ulong OfflineSawActorId => 0x8000000000000000UL | unchecked((uint)GetInstanceID());

    internal RaycastHit[] GetSawAimHits(Ray ray, float range, out int hitCount)
    {
        hitCount = Physics.RaycastNonAlloc(ray, sawAimHits, range,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (hitCount == sawAimHits.Length)
        {
            RaycastHit[] completeHits = Physics.RaycastAll(ray, range,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(completeHits, SawRaycastHitComparer.Instance);
            hitCount = completeHits.Length;
            return completeHits;
        }
        System.Array.Sort(sawAimHits, 0, hitCount, SawRaycastHitComparer.Instance);
        return sawAimHits;
    }

    internal bool BelongsToThisNetworkSession(Collider collider)
    {
        NetworkObject networkObject = collider != null ? collider.GetComponentInParent<NetworkObject>() : null;
        return networkObject == null || !networkObject.IsSpawned || networkObject.NetworkManager == NetworkManager;
    }

    private sealed class SawRaycastHitComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public static readonly SawRaycastHitComparer Instance = new();
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    private void CleanupSawCuttingVisuals()
    {
        if (sawPreviewLine != null) sawPreviewLine.enabled = false;
        if (sawPreviewMaterial != null)
        {
            if (Application.isPlaying) Destroy(sawPreviewMaterial);
            else DestroyImmediate(sawPreviewMaterial);
            sawPreviewMaterial = null;
        }
        if (sawPreviewLine != null) Destroy(sawPreviewLine.gameObject);
        sawPreviewLine = null;
    }
}
