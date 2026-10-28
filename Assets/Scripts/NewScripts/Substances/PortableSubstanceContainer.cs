using System.Collections;
using System.Diagnostics;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(BaseResourceNew), typeof(NetworkObject))]
public class PortableSubstanceContainer : NetworkBehaviour
{
    private enum ContextTargetKind : byte { None, ConstructionSite, LoosePile, ExtractionZone, NetworkSink }

    [SerializeField, Min(1)] private int capacity = 3;
    [SerializeField] private ContainerSubstanceSO[] supportedSubstances;
    [SerializeField] private LooseSubstancePile loosePilePrefab;
    [SerializeField] private Transform contentVisual;
    [SerializeField] private Renderer contentRenderer;
    [SerializeField] private float interactionDistance = 2.5f;
    [SerializeField, Min(0f)] private float actionHoldDuration = 1.5f;
    [SerializeField] private Vector3 emptyContentLocalScale = new Vector3(0.27f, 0.02f, 0.27f);
    [SerializeField] private float contentBottomLocalY = 0.45f;
    [SerializeField] private float contentTopLocalY = 0.55f;
    [SerializeField] private int respawnPointIndex;

    [Header("Automatic loose-soil collection")]
    [SerializeField, Tooltip("Automatically collect loose foundation soil while this bucket is held and its holder moves horizontally.")]
    private bool automaticLooseSoilCollection = true;
    [SerializeField, Min(0.01f), Tooltip("Maximum soil collection rate in portions per second while moving.")]
    private float looseSoilUnitsPerSecond = 0.5f;
    [SerializeField, Min(0.01f), Tooltip("Horizontal radius around the holder used to remove loose soil.")]
    private float looseSoilBrushRadius = 0.6f;
    [SerializeField, Min(0f), Tooltip("When the entire active layer has at most this much soil remaining, transfer its tail together if it fits.")]
    private float looseSoilWholeLayerTailThreshold = 0.05f;
    [SerializeField, Min(0.05f), Tooltip("Server sampling interval. Missed intervals are discarded rather than accumulated.")]
    private float looseSoilSampleInterval = 0.1f;
    [SerializeField, Min(0f), Tooltip("Ignore horizontal movement below this distance per sample to avoid jitter collection.")]
    private float looseSoilMinimumMovement = 0.01f;
    [SerializeField, Min(0.1f), Tooltip("Reject unusually large per-sample displacements such as teleports.")]
    private float looseSoilMaximumMovementPerSample = 1.5f;

    private readonly NetworkVariable<int> substanceIndexNetwork = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<float> unitsNetwork = new NetworkVariable<float>();
    private readonly NetworkVariable<bool> returningNetwork = new NetworkVariable<bool>();
    private int localSubstanceIndex = -1;
    private float localUnits;
    private BaseResourceNew carryResource;

    public ContainerSubstanceSO CurrentSubstance => GetSubstance(CurrentSubstanceIndex);
    public float CurrentUnits => IsSpawned ? unitsNetwork.Value : localUnits;
    public int Capacity => Mathf.Max(1, capacity);
    public float ActionHoldDuration => Mathf.Max(0f, actionHoldDuration);
    public bool IsReturning => IsSpawned ? returningNetwork.Value : localReturning;
    internal int CurrentSubstanceIndexForRollback => CurrentSubstanceIndex;
    public string ContentsLabel => CurrentUnits <= 0 || CurrentSubstance == null
        ? $"Empty 0 / {Capacity}"
        : $"{CurrentSubstance.DisplayName} {SubstanceAmount.Format(CurrentUnits)} / {Capacity}";
    private int CurrentSubstanceIndex => IsSpawned ? substanceIndexNetwork.Value : localSubstanceIndex;
    private bool localReturning;
    private float nextLooseSoilSampleTime;
    private float lastLooseSoilSampleTime;
    private Transform sampledHolder;
    private CharacterController sampledHolderCharacterController;
    private Vector3 previousHolderPosition;
    private ulong sampledHolderClientId = ulong.MaxValue;
    private bool hasHolderSample;
    private bool bucketActionCollectionSuppressed;
    private ulong bucketActionSuppressedHolderClientId = ulong.MaxValue;
    private PlayerInteractionNew bucketActionSuppressedHolder;

    protected virtual void OnEnable() => ResetLooseSoilSampling();
    protected virtual void OnDisable()
    {
        ClearBucketActionCollectionIntent();
        ResetLooseSoilSampling();
    }

    private void Awake()
    {
        carryResource = GetComponent<BaseResourceNew>();
        ApplyVisual();
    }

    protected virtual void Update()
    {
        if (!automaticLooseSoilCollection || !HasValidAutomaticCollectionSettings() || !CanRunAutomaticCollection())
        {
            ResetLooseSoilSampling();
            return;
        }

        float now = Time.time;
        float interval = looseSoilSampleInterval;
        if (now < nextLooseSoilSampleTime) return;
        // Cap a single hitch; missed time is discarded rather than accumulated.
        float elapsed = Mathf.Min(Mathf.Max(0f, now - lastLooseSoilSampleTime), Mathf.Max(interval, 0.25f));
        lastLooseSoilSampleTime = now;
        nextLooseSoilSampleTime = now + interval;

        if (carryResource == null) carryResource = GetComponent<BaseResourceNew>();
        if (!TryGetAuthoritativeHolder(out Transform holder, out ulong clientId))
        {
            ClearBucketActionCollectionIntent();
            ResetLooseSoilSampling();
            return;
        }

        Vector3 holderPosition = holder.position;
        bool sameHolder = hasHolderSample && sampledHolder == holder && sampledHolderClientId == clientId;
        Vector3 movement = sameHolder ? holderPosition - previousHolderPosition : Vector3.zero;
        // Resolve the capsule-foot point before publishing this holder into the cache.
        Vector3 position = GetCollectionPoint(holder);
        previousHolderPosition = holderPosition;
        sampledHolder = holder;
        sampledHolderClientId = clientId;
        hasHolderSample = true;

        PlayerHealth holderHealth = holder.GetComponent<PlayerHealth>();
        if (holderHealth != null && holderHealth.IsDowned)
        {
            ClearBucketActionCollectionIntent();
            return;
        }

        if (bucketActionCollectionSuppressed)
        {
            bool sameIntentHolder = IsNetworkSessionActive()
                ? bucketActionSuppressedHolderClientId == clientId &&
                  holder.GetComponent<PlayerInteractionNew>() == bucketActionSuppressedHolder
                : bucketActionSuppressedHolder != null && bucketActionSuppressedHolder.transform == holder;
            if (sameIntentHolder) return;
            ClearBucketActionCollectionIntent();
        }

        if (!sameHolder || IsReturning || CurrentUnits >= Capacity ||
            CurrentSubstance != null && !CurrentSubstance.IsSoil ||
            !IsFinite(movement.x) || !IsFinite(movement.z)) return;

        movement.y = 0f;
        float distance = movement.magnitude;
        if (distance <= 0f || distance < looseSoilMinimumMovement ||
            distance > looseSoilMaximumMovementPerSample) return;

        ContainerSubstanceSO soil = CurrentSubstance;
        if (soil == null) soil = FindSupportedSoil();
        if (soil == null || GetAvailableCapacity(soil) <= 0f) return;
        if (!FoundationLooseSoilField.TryFindCollectionField(position, out FoundationLooseSoilField field)) return;
        if (!field.TryGetConstructionSite(out BridgeConstructionSite site)) return;

        // Movement gates collection; elapsed sample time bounds the amount and is not retained.
        float requested = Mathf.Min(looseSoilUnitsPerSecond * elapsed, GetAvailableCapacity(soil));
        if (!SubstanceAmount.IsFinitePositive(requested)) return;
        site.TryCollectLooseSoil(this, soil, position, Mathf.Max(0.01f, looseSoilBrushRadius),
            requested, looseSoilWholeLayerTailThreshold);
    }

    private bool CanRunAutomaticCollection()
    {
        NetworkManager manager = NetworkManager;
        if (IsSpawned) return IsServer;
        return manager == null || !manager.IsListening;
    }

    private bool HasValidAutomaticCollectionSettings() =>
        IsFinite(looseSoilUnitsPerSecond) && looseSoilUnitsPerSecond > 0f &&
        IsFinite(looseSoilBrushRadius) && looseSoilBrushRadius > 0f &&
        IsFinite(looseSoilWholeLayerTailThreshold) && looseSoilWholeLayerTailThreshold >= 0f &&
        IsFinite(looseSoilSampleInterval) && looseSoilSampleInterval >= 0.05f &&
        IsFinite(looseSoilMinimumMovement) && looseSoilMinimumMovement >= 0f &&
        IsFinite(looseSoilMaximumMovementPerSample) && looseSoilMaximumMovementPerSample > 0f &&
        looseSoilMaximumMovementPerSample > looseSoilMinimumMovement;

    private Vector3 GetCollectionPoint(Transform holder)
    {
        if (sampledHolder != holder)
            sampledHolderCharacterController = holder.GetComponent<CharacterController>();
        CharacterController controller = sampledHolderCharacterController;
        return controller != null
            ? holder.TransformPoint(controller.center - Vector3.up * (controller.height * 0.5f))
            : holder.position;
    }

    private bool TryGetAuthoritativeHolder(out Transform holder, out ulong clientId)
    {
        holder = null;
        clientId = ulong.MaxValue;
        if (carryResource == null || !carryResource.IsPickedUp) return false;
        NetworkManager manager = NetworkManager;
        if (!IsSpawned || manager == null || !manager.IsListening)
        {
            if (sampledHolder != null && sampledHolder.gameObject.activeInHierarchy &&
                sampledHolder.TryGetComponent(out PlayerInteractionNew cachedPlayer) &&
                cachedPlayer.GetPickedUpGameObject() == gameObject)
                holder = sampledHolder;
            else
            {
                PlayerInteractionNew player = FindFirstObjectByType<PlayerInteractionNew>();
                if (player != null && player.GetPickedUpGameObject() == gameObject) holder = player.transform;
            }
            return holder != null && carryResource.IsHeldBy(ulong.MaxValue);
        }

        if (manager == null || !manager.IsListening) return false;
        foreach (NetworkClient client in manager.ConnectedClientsList)
        {
            if (client?.PlayerObject == null || !carryResource.IsHeldBy(client.ClientId)) continue;
            holder = client.PlayerObject.transform;
            clientId = client.ClientId;
            return true;
        }
        return false;
    }

    private ContainerSubstanceSO FindSupportedSoil()
    {
        if (supportedSubstances == null) return null;
        foreach (ContainerSubstanceSO candidate in supportedSubstances)
            if (candidate != null && candidate.IsSoil) return candidate;
        return null;
    }

    private void ResetLooseSoilSampling()
    {
        hasHolderSample = false;
        sampledHolder = null;
        sampledHolderCharacterController = null;
        sampledHolderClientId = ulong.MaxValue;
        lastLooseSoilSampleTime = Time.time;
        nextLooseSoilSampleTime = Time.time + (IsFinite(looseSoilSampleInterval)
            ? Mathf.Max(0.05f, looseSoilSampleInterval)
            : 0.1f);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public override void OnNetworkSpawn()
    {
        ResetLooseSoilSampling();
        substanceIndexNetwork.OnValueChanged += OnSubstanceChanged;
        unitsNetwork.OnValueChanged += OnUnitsChanged;
        returningNetwork.OnValueChanged += OnReturningChanged;
        ApplyVisual();
    }

    public override void OnNetworkDespawn()
    {
        ClearBucketActionCollectionIntent();
        ResetLooseSoilSampling();
        substanceIndexNetwork.OnValueChanged -= OnSubstanceChanged;
        unitsNetwork.OnValueChanged -= OnUnitsChanged;
        returningNetwork.OnValueChanged -= OnReturningChanged;
    }

    public void RequestContextAction(PlayerInteractionNew player, MonoBehaviour target)
    {
        RequestContextAction(player, target, false, default);
    }

    /// <summary>Requests the normal bucket action with the holder's client-supplied aim ray for server validation.</summary>
    public void RequestContextActionWithAimRay(PlayerInteractionNew player, MonoBehaviour target, Ray aimRay)
    {
        RequestContextAction(player, target, true, aimRay);
    }

    private void RequestContextAction(PlayerInteractionNew player, MonoBehaviour target, bool hasAimRay, Ray aimRay)
    {
        if (player == null || player.GetPickedUpGameObject() != gameObject) return;

        ContextTargetKind kind = ContextTargetKind.None;
        ulong targetId = 0;
        if (target != null)
        {
            ISubstanceSink sink = target as ISubstanceSink;
            sink ??= target.GetComponent<ISubstanceSink>() ?? target.GetComponentInParent<ISubstanceSink>();
            BridgeConstructionSite site = ResolveTargetConstructionSite(target);
            SubstanceExtractionZone zone = target.GetComponent<SubstanceExtractionZone>() ?? target.GetComponentInParent<SubstanceExtractionZone>();
            if (sink is MonoBehaviour sinkBehaviour && sinkBehaviour.GetComponentInParent<NetworkObject>() is NetworkObject sinkNetworkObject)
            {
                kind = ContextTargetKind.NetworkSink;
                targetId = sinkNetworkObject.NetworkObjectId;
            }
            else if (site != null && site.BridgeComponent != null)
            {
                kind = ContextTargetKind.ConstructionSite;
                targetId = (ulong)site.BridgeComponent.ComponentID;
            }
            else if (zone != null)
            {
                kind = ContextTargetKind.ExtractionZone;
                targetId = (ulong)zone.SourceId;
            }
            else
            {
                LooseSubstancePile pile = target.GetComponent<LooseSubstancePile>() ?? target.GetComponentInParent<LooseSubstancePile>();
                if (pile != null && pile.NetworkObject != null)
                {
                    kind = ContextTargetKind.LoosePile;
                    targetId = pile.NetworkObjectId;
                }
            }
        }

        if (IsNetworkSessionActive())
        {
            if (IsServer) ExecuteContextAction(NetworkManager.Singleton.LocalClientId, kind, targetId,
                hasAimRay ? aimRay : default, hasAimRay);
            else if (hasAimRay) RequestContextActionWithAimRayServerRpc((byte)kind, targetId,
                aimRay.origin, aimRay.direction);
            else RequestContextActionServerRpc((byte)kind, targetId);
        }
        else
        {
            ExecuteContextAction(ulong.MaxValue, kind, targetId, hasAimRay ? aimRay : default, hasAimRay, target);
        }
    }

    private static BridgeConstructionSite ResolveTargetConstructionSite(MonoBehaviour target)
    {
        if (target == null) return null;
        BridgeConstructionSite site = target as BridgeConstructionSite;
        if (site == null) site = target.GetComponent<BridgeConstructionSite>();
        if (site == null) site = target.GetComponentInParent<BridgeConstructionSite>();
        if (site != null) return site;

        FoundationExcavationVolume volume = target as FoundationExcavationVolume;
        if (volume == null) volume = target.GetComponent<FoundationExcavationVolume>();
        if (volume == null) volume = target.GetComponentInParent<FoundationExcavationVolume>();
        if (volume != null) return volume.ConstructionSite;

        FoundationLooseSoilField field = target as FoundationLooseSoilField;
        if (field == null) field = target.GetComponent<FoundationLooseSoilField>();
        if (field == null) field = target.GetComponentInParent<FoundationLooseSoilField>();
        return field != null && field.TryGetConstructionSite(out site) ? site : null;
    }

    public string GetContextActionDescription(MonoBehaviour target)
    {
        ISubstanceSink sink = target as ISubstanceSink;
        if (sink == null && target != null)
        {
            sink = target.GetComponent<ISubstanceSink>() ?? target.GetComponentInParent<ISubstanceSink>();
        }
        if (sink != null)
        {
            return sink.GetDepositPrompt(this);
        }

        if (CurrentUnits > 0f && CurrentSubstance != null && CurrentSubstance.IsSoil &&
            ResolveTargetConstructionSite(target) is BridgeConstructionSite soilSite &&
            soilSite.ExcavationVolume != null && soilSite.ExcavationVolume.UsesLooseSoilHeightField)
            return $"Hold to return soil - {ContentsLabel}";

        ISubstanceSource source = target as ISubstanceSource;
        if (source == null && target != null)
        {
            source = target.GetComponent<ISubstanceSource>() ?? target.GetComponentInParent<ISubstanceSource>();
        }
        if (source != null)
        {
            ContainerSubstanceSO availableSubstance = FindSupportedSubstance(source);
            if (availableSubstance == null)
            {
                return ContentsLabel;
            }
            if (CurrentUnits > 0 && CurrentSubstance != availableSubstance)
            {
                return $"Empty bucket first - {ContentsLabel}";
            }
            return CurrentUnits >= Capacity
                ? $"Bucket full - {ContentsLabel}"
                : $"Hold to collect {availableSubstance.DisplayName} - {ContentsLabel}";
        }
        return CurrentUnits > 0 ? $"Hold to empty bucket - {ContentsLabel}" : ContentsLabel;
    }

    /// <summary>Limits semantic pit targeting to the soil-return bucket action.</summary>
    internal bool ShouldTrackLooseSoilPitAim(MonoBehaviour target)
    {
        if (CurrentUnits <= 0f || CurrentSubstance == null || !CurrentSubstance.IsSoil) return false;
        if (target == null) return true;
        if (ResolveSink(ContextTargetKind.None, 0, target) != null) return false;

        BridgeConstructionSite site = ResolveTargetConstructionSite(target);
        if (site != null)
            return site.ExcavationVolume != null && site.ExcavationVolume.UsesLooseSoilHeightField;

        return false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestContextActionServerRpc(byte kind, ulong targetId, ServerRpcParams rpcParams = default)
    {
        ExecuteContextAction(rpcParams.Receive.SenderClientId, (ContextTargetKind)kind, targetId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestContextActionWithAimRayServerRpc(byte kind, ulong targetId, Vector3 rayOrigin,
        Vector3 rayDirection, ServerRpcParams rpcParams = default)
    {
        if (!IsFinite(rayOrigin) || !IsUnitDirection(rayDirection)) return;
        ExecuteContextAction(rpcParams.Receive.SenderClientId, (ContextTargetKind)kind, targetId,
            new Ray(rayOrigin, rayDirection), true);
    }

    private void ExecuteContextAction(ulong senderClientId, ContextTargetKind kind, ulong targetId,
        Ray aimRay = default, bool hasAimRay = false, MonoBehaviour localTarget = null)
    {
        if (!ValidateHolder(senderClientId, out PlayerInteractionNew player)) return;
        PlayerHealth holderHealth = player.GetComponent<PlayerHealth>();
        if (holderHealth != null && holderHealth.IsDowned) return;

        ISubstanceSink sink = ResolveSink(kind, targetId, localTarget);
        if (sink != null)
        {
            if (CurrentUnits > 0)
            {
                sink.TryDeposit(this, player);
            }
            return;
        }

        ISubstanceSource source = hasAimRay ? ResolveSource(kind, targetId, localTarget) : null;
        bool isLooseSoilPitSource = source is BridgeConstructionSite pitSource &&
                                    pitSource.ExcavationVolume != null &&
                                    pitSource.ExcavationVolume.UsesLooseSoilHeightField;
        if (source != null && !isLooseSoilPitSource)
        {
            ExecuteSourceContextAction(source, senderClientId, player);
            return;
        }

        if (hasAimRay)
        {
            if (!IsValidBucketAimRay(player, aimRay)) return;
            if (CurrentUnits > 0f && CurrentSubstance != null && CurrentSubstance.IsSoil &&
                FoundationLooseSoilField.TryFindAimedPit(aimRay, float.MaxValue,
                    out FoundationLooseSoilField aimedField, out BridgeConstructionSite aimedSite,
                    out _, out float aimDistance))
            {
                Vector3 dumpPoint = GetBucketDumpPoint(player);
                if (aimedField.ContainsHorizontalFootprint(dumpPoint))
                {
                    Vector3 closestPitPoint = GetClosestPitSupportPoint(aimedField, player.transform.position);
                    bool inRange = Vector3.Distance(player.transform.position, closestPitPoint) <= interactionDistance;
                    if (inRange && HasClearAimPath(player, aimedSite, aimRay, aimDistance))
                        aimedSite.TryReturnLooseSoilFromBucket(this, CurrentSubstance, dumpPoint, CurrentUnits);
                    // A dump whose ray and forward column both identify this pit is a pit action.
                    // Full or otherwise rejected returns keep the bucket contents in place.
                    return;
                }
                // The ray identified a pit, but the forward dump column belongs elsewhere.
                // Preserve the contents instead of spawning into a different pit or pile.
                return;
            }
        }

        source ??= ResolveSource(kind, targetId, localTarget);
        if (source != null)
        {
            ExecuteSourceContextAction(source, senderClientId, player);
            return;
        }

        if (CurrentUnits > 0)
        {
            DumpAll(GetBucketDumpPoint(player), $"RMB dump by client {senderClientId}");
        }
    }

    private void ExecuteSourceContextAction(ISubstanceSource source, ulong senderClientId, PlayerInteractionNew player)
    {
        ContainerSubstanceSO requested = CurrentSubstance;
        if (requested == null) requested = FindSupportedSubstance(source);
        Vector3 sourcePoint = source switch
        {
            BridgeConstructionSite site => site.GetClosestInteractionPoint(player.transform.position),
            SubstanceExtractionZone zone => zone.GetClosestInteractionPoint(player.transform.position),
            MonoBehaviour sourceBehaviour => sourceBehaviour.transform.position,
            _ => player.transform.position
        };
        if (requested == null || CurrentUnits >= Capacity || !source.CanExtract(requested) ||
            Vector3.Distance(player.transform.position, sourcePoint) > interactionDistance)
        {
            return;
        }

        float requestedAmount = Mathf.Min(1f, Capacity - CurrentUnits);
        float added = TryAddUnits(requested, requestedAmount,
            $"RMB scoop from {GetSourceDebugName(source)} by client {senderClientId}");
        if (added > 0f)
        {
            float extracted = source.TryExtract(requested, added);
            extracted = float.IsNaN(extracted) || float.IsInfinity(extracted)
                ? 0f
                : Mathf.Clamp(extracted, 0f, added);
            if (extracted < added) TryRemoveUnits(requested, added - extracted);
        }
    }

    private static Vector3 GetClosestPitSupportPoint(FoundationLooseSoilField field, Vector3 worldPosition)
    {
        LooseSoilHeightField heightField = field.HeightField;
        Vector3 local = field.transform.InverseTransformPoint(worldPosition);
        local.x = Mathf.Clamp(local.x, -heightField.Width * 0.5f, heightField.Width * 0.5f);
        local.y = 0f;
        local.z = Mathf.Clamp(local.z, -heightField.Length * 0.5f, heightField.Length * 0.5f);
        return field.transform.TransformPoint(local);
    }
    private static Vector3 GetBucketDumpPoint(PlayerInteractionNew player) =>
        player.transform.position + player.transform.forward * 1.1f + Vector3.up * 0.25f;

    private bool IsValidBucketAimRay(PlayerInteractionNew player, Ray aimRay)
    {
        if (!IsFinite(aimRay.origin) || !IsUnitDirection(aimRay.direction)) return false;
        Transform anchor = player.SingleCarryPlacementAimAnchor;
        if (anchor == null) return false;
        Vector3 trustedAnchor = anchor.position;
        NetworkObject playerObject = player.GetComponent<NetworkObject>();
        if (IsNetworkSessionActive())
        {
            NetworkTransform playerNetworkTransform = playerObject != null
                ? playerObject.GetComponent<NetworkTransform>()
                : null;
            if (playerObject == null || playerNetworkTransform == null || !playerNetworkTransform.IsSpawned) return false;
            Vector3 anchorLocalOffset = playerObject.transform.InverseTransformPoint(anchor.position);
            Vector3 authoritativePosition = playerNetworkTransform.GetSpaceRelativePosition(true);
            Quaternion authoritativeRotation = playerNetworkTransform.GetSpaceRelativeRotation(true);
            if (playerNetworkTransform.InLocalSpace && playerObject.transform.parent != null)
            {
                authoritativePosition = playerObject.transform.parent.TransformPoint(authoritativePosition);
                authoritativeRotation = playerObject.transform.parent.rotation * authoritativeRotation;
            }
            trustedAnchor = authoritativePosition + authoritativeRotation *
                Vector3.Scale(anchorLocalOffset, playerObject.transform.localScale);
        }

        Vector3 offset = aimRay.origin - trustedAnchor;
        float forwardOffset = Vector3.Dot(offset, aimRay.direction);
        Vector3 lateralOffset = offset - aimRay.direction * forwardOffset;
        float nearestAllowedForwardOffset = Mathf.Clamp(forwardOffset, 0f, 0.2f);
        float residualSquared = lateralOffset.sqrMagnitude +
                                Mathf.Pow(forwardOffset - nearestAllowedForwardOffset, 2f);
        return IsFinite(residualSquared) && residualSquared <= 0.15f * 0.15f;
    }

    private bool HasClearAimPath(PlayerInteractionNew player, BridgeConstructionSite site, Ray aimRay,
        float aimDistance)
    {
        Physics.SyncTransforms();
        RaycastHit[] hits = Physics.RaycastAll(aimRay, aimDistance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        FoundationExcavationVolume targetVolume = site != null ? site.ExcavationVolume : null;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i].collider;
            if (hit == null || hit.transform.IsChildOf(player.transform) || hit.transform.IsChildOf(transform)) continue;
            FoundationExcavationVolume hitVolume = hit.GetComponentInParent<FoundationExcavationVolume>();
            if (targetVolume != null && hitVolume == targetVolume && hits[i].distance >= aimDistance - 0.05f) continue;
            return false;
        }
        return true;
    }

    private static bool IsFinite(Vector3 value) =>
        IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

    private static bool IsUnitDirection(Vector3 value)
    {
        float magnitudeSquared = value.sqrMagnitude;
        return IsFinite(value) && IsFinite(magnitudeSquared) && magnitudeSquared >= 0.99f && magnitudeSquared <= 1.01f;
    }

    public float TryAddUnits(ContainerSubstanceSO substance, float units)
    {
        return TryAddUnits(substance, units, "explicit API add");
    }

    /// <summary>Capacity available for an exact authoritative transfer of the requested substance.</summary>
    public float GetAvailableCapacity(ContainerSubstanceSO substance)
    {
        if (!IsAuthority() || substance == null || GetSubstanceIndex(substance) < 0 ||
            (CurrentUnits > 0f && CurrentSubstance != substance)) return 0f;
        return Mathf.Max(0f, Capacity - CurrentUnits);
    }

    private float TryAddUnits(ContainerSubstanceSO substance, float units, string reason)
    {
        if (!IsAuthority() || substance == null || !SubstanceAmount.IsFinitePositive(units) || CurrentUnits >= Capacity) return 0f;
        int index = GetSubstanceIndex(substance);
        if (index < 0 || (CurrentUnits > 0 && index != CurrentSubstanceIndex)) return 0f;
        float added = Mathf.Min(units, Capacity - CurrentUnits);
        SetContents(index, CurrentUnits + added, reason);
        return added;
    }

    public float TryRemoveUnits(ContainerSubstanceSO substance, float units)
    {
        if (!IsAuthority() || substance == null || substance != CurrentSubstance || !SubstanceAmount.IsFinitePositive(units)) return 0f;
        float removed = Mathf.Min(units, CurrentUnits);
        SetContents(CurrentSubstanceIndex, CurrentUnits - removed, "explicit API remove");
        return removed;
    }

    internal double GetMaximumRemovableUnits(ContainerSubstanceSO substance, double maximumDebit)
    {
        if (!CanRemoveForMeasuredTransfer(substance) || double.IsNaN(maximumDebit) ||
            double.IsInfinity(maximumDebit) || maximumDebit <= 0d) return 0d;
        GetMaximumRemovalRequest(CurrentUnits, CurrentUnits, maximumDebit, out double storedDelta);
        return storedDelta;
    }

    internal double TryRemoveUnitsWithinBudget(ContainerSubstanceSO substance, double maximumDebit)
    {
        if (!CanRemoveForMeasuredTransfer(substance) || double.IsNaN(maximumDebit) ||
            double.IsInfinity(maximumDebit) || maximumDebit <= 0d) return 0d;
        float unitsBefore = CurrentUnits;
        int substanceIndexBefore = CurrentSubstanceIndex;
        float request = GetMaximumRemovalRequest(unitsBefore, unitsBefore, maximumDebit, out double expectedDebit);
        if (request <= 0f || expectedDebit <= 0d) return 0d;
        TryRemoveUnits(substance, request);
        double actualDebit = (double)unitsBefore - CurrentUnits;
        if (actualDebit > maximumDebit)
        {
            TryRestoreContentsForRollback(substanceIndexBefore, unitsBefore);
            return 0d;
        }
        return actualDebit;
    }

    private bool CanRemoveForMeasuredTransfer(ContainerSubstanceSO substance) =>
        IsAuthority() && substance != null && substance == CurrentSubstance && CurrentUnits > 0f;

    private static float GetMaximumRemovalRequest(float currentUnits, float maximumRequest,
        double maximumDebit, out double storedDelta)
    {
        storedDelta = 0d;
        if (!SubstanceAmount.IsFinitePositive(currentUnits) ||
            !SubstanceAmount.IsFinitePositive(maximumRequest) || double.IsNaN(maximumDebit) ||
            double.IsInfinity(maximumDebit) || maximumDebit <= 0d) return 0f;

        maximumRequest = Mathf.Min(currentUnits, maximumRequest);
        int low = 0;
        int high = System.BitConverter.SingleToInt32Bits(maximumRequest);
        while (low < high)
        {
            int middle = (int)(low + ((long)high - low + 1L) / 2L);
            float request = System.BitConverter.Int32BitsToSingle(middle);
            float resultingUnits = Mathf.Max(0f, currentUnits - request);
            double moved = (double)currentUnits - resultingUnits;
            if (moved <= maximumDebit) low = middle;
            else high = middle - 1;
        }

        float safeRequest = System.BitConverter.Int32BitsToSingle(low);
        storedDelta = (double)currentUnits - Mathf.Max(0f, currentUnits - safeRequest);
        return storedDelta > 0d ? safeRequest : 0f;
    }

    internal bool TryRestoreContentsForRollback(int substanceIndex, float units)
    {
        if (!IsAuthority() || float.IsNaN(units) || float.IsInfinity(units) || units < 0f || units > Capacity ||
            units > 0f && (substanceIndex < 0 || GetSubstance(substanceIndex) == null) ||
            units == 0f && substanceIndex != -1) return false;

        SetContents(substanceIndex, units, "loose soil collection rollback");
        return CurrentSubstanceIndex == substanceIndex && CurrentUnits == units;
    }

    public void Empty(bool spawnContents = false, Vector3 position = default)
    {
        if (!IsAuthority()) return;
        if (spawnContents && CurrentUnits > 0)
        {
            if (SpawnPile(position, CurrentSubstance, CurrentUnits)) SetContents(-1, 0, "explicit empty with spawned contents");
            return;
        }
        SetContents(-1, 0, "explicit empty");
    }

    public void ReturnToRespawnPoint(float delay)
    {
        if (!IsAuthority() || IsReturning) return;
        carryResource?.ForceReleaseForEnvironmentalRemoval();
        ResetLooseSoilSampling();
        Empty();
        SetReturning(true);
        StartCoroutine(ReturnRoutine(Mathf.Max(0f, delay)));
    }

    private IEnumerator ReturnRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (BucketRespawnPoint.TryGetReturnPose(respawnPointIndex, out Vector3 position, out Quaternion rotation))
        {
            transform.SetPositionAndRotation(position, rotation);
        }
        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        SetReturning(false);
    }

    private void DumpAll(Vector3 position, string reason)
    {
        if (CurrentSubstance == null || CurrentUnits <= 0) return;
        if (SpawnPile(position, CurrentSubstance, CurrentUnits)) SetContents(-1, 0, reason);
    }

    private bool SpawnPile(Vector3 position, ContainerSubstanceSO substance, float units)
    {
        if (loosePilePrefab == null || substance == null || !SubstanceAmount.IsFinitePositive(units)) return false;
        LooseSubstancePile pile = Instantiate(loosePilePrefab, position, Quaternion.identity);
        pile.Initialize(substance, units);
        if (IsNetworkSessionActive())
        {
            if (pile.NetworkObject == null) { Destroy(pile.gameObject); return false; }
            pile.NetworkObject.Spawn();
        }
        return true;
    }

    private ISubstanceSource ResolveSource(ContextTargetKind kind, ulong targetId, MonoBehaviour localTarget)
    {
        if (localTarget is ISubstanceSource direct) return direct;
        if (kind == ContextTargetKind.ConstructionSite)
        {
            return GameplayManager.Instance != null && GameplayManager.Instance.TryGetConstructionSite((int)targetId, out BridgeConstructionSite site) ? site : null;
        }
        if (kind == ContextTargetKind.LoosePile && NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out NetworkObject targetObject))
        {
            return targetObject.GetComponent<LooseSubstancePile>();
        }
        if (kind == ContextTargetKind.ExtractionZone && SubstanceExtractionZone.TryGet((int)targetId, out SubstanceExtractionZone zone))
        {
            return zone;
        }
        return null;
    }

    private ISubstanceSink ResolveSink(ContextTargetKind kind, ulong targetId, MonoBehaviour localTarget)
    {
        if (localTarget is ISubstanceSink direct)
        {
            return direct;
        }
        if (localTarget != null)
        {
            ISubstanceSink localSink = localTarget.GetComponent<ISubstanceSink>() ?? localTarget.GetComponentInParent<ISubstanceSink>();
            if (localSink != null)
            {
                return localSink;
            }
        }
        if (kind == ContextTargetKind.NetworkSink && NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out NetworkObject targetObject))
        {
            return targetObject.GetComponent<ISubstanceSink>() ?? targetObject.GetComponentInChildren<ISubstanceSink>();
        }
        return null;
    }

    private ContainerSubstanceSO FindSupportedSubstance(ISubstanceSource source)
    {
        if (supportedSubstances == null || source == null)
        {
            return null;
        }
        foreach (ContainerSubstanceSO candidate in supportedSubstances)
        {
            if (candidate != null && source.CanExtract(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private bool ValidateHolder(ulong clientId, out PlayerInteractionNew player)
    {
        player = null;
        if (!IsNetworkSessionActive())
        {
            player = FindFirstObjectByType<PlayerInteractionNew>();
            return player != null && player.GetPickedUpGameObject() == gameObject;
        }
        if (carryResource == null || !carryResource.IsHeldBy(clientId) ||
            !NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
        {
            return false;
        }
        player = client.PlayerObject.GetComponent<PlayerInteractionNew>();
        return player != null;
    }

    internal bool SetBucketActionCollectionIntent(PlayerInteractionNew player, bool held)
    {
        if (player == null) return false;
        NetworkObject playerNetworkObject = player.GetComponent<NetworkObject>();

        if (!IsNetworkSessionActive())
        {
            if (held)
            {
                if (player.GetPickedUpGameObject() != gameObject ||
                    player.GetComponent<PlayerHealth>() is PlayerHealth health && health.IsDowned ||
                    carryResource == null || !carryResource.IsHeldBy(ulong.MaxValue)) return false;
                bucketActionCollectionSuppressed = true;
                bucketActionSuppressedHolderClientId = ulong.MaxValue;
                bucketActionSuppressedHolder = player;
                return true;
            }

            if (bucketActionCollectionSuppressed && bucketActionSuppressedHolder == player)
                ClearBucketActionCollectionIntent();
            return true;
        }

        if (!IsSpawned || playerNetworkObject == null || !playerNetworkObject.IsOwner) return false;
        if (held && (player.GetPickedUpGameObject() != gameObject ||
                     player.GetComponent<PlayerHealth>() is PlayerHealth networkHealth && networkHealth.IsDowned)) return false;

        if (IsServer)
        {
            ulong clientId = NetworkManager.Singleton.LocalClientId;
            if (held)
            {
                if (!ValidateHolder(clientId, out PlayerInteractionNew authoritativePlayer) || authoritativePlayer != player)
                    return false;
                bucketActionCollectionSuppressed = true;
                bucketActionSuppressedHolderClientId = clientId;
                bucketActionSuppressedHolder = player;
            }
            else if (bucketActionCollectionSuppressed && bucketActionSuppressedHolderClientId == clientId)
            {
                ClearBucketActionCollectionIntent();
            }
            return true;
        }

        SetBucketActionCollectionIntentServerRpc(held);
        return true;
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetBucketActionCollectionIntentServerRpc(bool held, ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (held)
        {
            if (!ValidateHolder(senderClientId, out PlayerInteractionNew player)) return;
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            if (health != null && health.IsDowned) return;
            bucketActionCollectionSuppressed = true;
            bucketActionSuppressedHolderClientId = senderClientId;
            bucketActionSuppressedHolder = player;
            return;
        }

        // A release may arrive after a drop; only the client that began the intent may clear it.
        if (bucketActionCollectionSuppressed && bucketActionSuppressedHolderClientId == senderClientId)
            ClearBucketActionCollectionIntent();
    }

    private void ClearBucketActionCollectionIntent()
    {
        bucketActionCollectionSuppressed = false;
        bucketActionSuppressedHolderClientId = ulong.MaxValue;
        bucketActionSuppressedHolder = null;
    }

    private void SetContents(int substanceIndex, float units, string reason)
    {
        float previousUnits = CurrentUnits;
        int previousSubstanceIndex = CurrentSubstanceIndex;
        float safeUnits = float.IsNaN(units) || float.IsInfinity(units) ? 0f : Mathf.Clamp(units, 0f, Capacity);
        int safeIndex = safeUnits > 0f ? substanceIndex : -1;
        if (IsSpawned)
        {
            substanceIndexNetwork.Value = safeIndex;
            unitsNetwork.Value = safeUnits;
        }
        else
        {
            localSubstanceIndex = safeIndex;
            localUnits = safeUnits;
            ApplyVisual();
        }

        LogContentsChange(previousSubstanceIndex, previousUnits, safeIndex, safeUnits, reason);
    }

    [Conditional("UNITY_EDITOR")]
    private void LogContentsChange(int previousSubstanceIndex, float previousUnits, int currentSubstanceIndex,
        float currentUnits, string reason)
    {
        if (previousSubstanceIndex == currentSubstanceIndex && previousUnits == currentUnits)
        {
            return;
        }

        UnityEngine.Debug.Log(
            $"[PortableSubstanceContainer] {name}: {SubstanceAmount.Format(previousUnits)} -> {SubstanceAmount.Format(currentUnits)} units ({reason}).",
            this);
    }

    private static string GetSourceDebugName(ISubstanceSource source)
    {
        return source is MonoBehaviour behaviour
            ? $"{behaviour.name} ({behaviour.GetInstanceID()})"
            : source.GetType().Name;
    }

    private void OnSubstanceChanged(int previous, int current) => ApplyVisual();
    private void OnUnitsChanged(float previous, float current) => ApplyVisual();
    private void OnReturningChanged(bool previous, bool current) => ApplyReturningState(current);

    private void SetReturning(bool returning)
    {
        if (IsSpawned) returningNetwork.Value = returning;
        else { localReturning = returning; ApplyReturningState(returning); }
    }

    private void ApplyReturningState(bool returning)
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = !returning;
        foreach (Collider collider in GetComponentsInChildren<Collider>(true)) collider.enabled = !returning;
        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null && IsAuthority()) body.isKinematic = returning;
    }

    private void ApplyVisual()
    {
        if (contentVisual != null)
        {
            contentVisual.gameObject.SetActive(CurrentUnits > 0);
            Vector3 scale = contentVisual.localScale;
            scale.x = emptyContentLocalScale.x;
            scale.z = emptyContentLocalScale.z;
            scale.y = emptyContentLocalScale.y;
            contentVisual.localScale = scale;
            Vector3 position = contentVisual.localPosition;
            position.y = Mathf.Lerp(contentBottomLocalY, contentTopLocalY, CurrentUnits / Capacity);
            contentVisual.localPosition = position;
        }
        if (contentRenderer != null && CurrentSubstance != null) contentRenderer.material.color = CurrentSubstance.DisplayColor;
    }

    private int GetSubstanceIndex(ContainerSubstanceSO substance)
    {
        if (supportedSubstances == null) return -1;
        for (int i = 0; i < supportedSubstances.Length; i++) if (supportedSubstances[i] == substance) return i;
        return -1;
    }

    private ContainerSubstanceSO GetSubstance(int index) => supportedSubstances != null && index >= 0 && index < supportedSubstances.Length ? supportedSubstances[index] : null;
    private bool IsNetworkSessionActive() => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool IsAuthority() => !IsNetworkSessionActive() || IsServer;
}
