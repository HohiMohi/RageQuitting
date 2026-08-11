using System.Collections;
using System.Diagnostics;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(BaseResourceNew), typeof(NetworkObject))]
public class PortableSubstanceContainer : NetworkBehaviour
{
    private enum ContextTargetKind : byte { None, ConstructionSite, LoosePile }

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

    private readonly NetworkVariable<int> substanceIndexNetwork = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<int> unitsNetwork = new NetworkVariable<int>();
    private readonly NetworkVariable<bool> returningNetwork = new NetworkVariable<bool>();
    private int localSubstanceIndex = -1;
    private int localUnits;
    private BaseResourceNew carryResource;

    public ContainerSubstanceSO CurrentSubstance => GetSubstance(CurrentSubstanceIndex);
    public int CurrentUnits => IsSpawned ? unitsNetwork.Value : localUnits;
    public int Capacity => Mathf.Max(1, capacity);
    public float ActionHoldDuration => Mathf.Max(0f, actionHoldDuration);
    public bool IsReturning => IsSpawned ? returningNetwork.Value : localReturning;
    public string ContentsLabel => CurrentUnits <= 0 || CurrentSubstance == null
        ? $"Empty 0 / {Capacity}"
        : $"{CurrentSubstance.DisplayName} {CurrentUnits} / {Capacity}";
    private int CurrentSubstanceIndex => IsSpawned ? substanceIndexNetwork.Value : localSubstanceIndex;
    private bool localReturning;

    private void Awake()
    {
        carryResource = GetComponent<BaseResourceNew>();
        ApplyVisual();
    }

    public override void OnNetworkSpawn()
    {
        substanceIndexNetwork.OnValueChanged += OnSubstanceChanged;
        unitsNetwork.OnValueChanged += OnUnitsChanged;
        returningNetwork.OnValueChanged += OnReturningChanged;
        ApplyVisual();
    }

    public override void OnNetworkDespawn()
    {
        substanceIndexNetwork.OnValueChanged -= OnSubstanceChanged;
        unitsNetwork.OnValueChanged -= OnUnitsChanged;
        returningNetwork.OnValueChanged -= OnReturningChanged;
    }

    public void RequestContextAction(PlayerInteractionNew player, MonoBehaviour target)
    {
        if (player == null || player.GetPickedUpGameObject() != gameObject) return;

        ContextTargetKind kind = ContextTargetKind.None;
        ulong targetId = 0;
        if (target != null)
        {
            BridgeConstructionSite site = target.GetComponent<BridgeConstructionSite>() ?? target.GetComponentInParent<BridgeConstructionSite>();
            if (site != null && site.BridgeComponent != null)
            {
                kind = ContextTargetKind.ConstructionSite;
                targetId = (ulong)site.BridgeComponent.ComponentID;
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
            if (IsServer) ExecuteContextAction(NetworkManager.Singleton.LocalClientId, kind, targetId);
            else RequestContextActionServerRpc((byte)kind, targetId);
        }
        else
        {
            ExecuteContextAction(ulong.MaxValue, kind, targetId, target);
        }
    }

    public string GetContextActionDescription(MonoBehaviour target)
    {
        ISubstanceSource source = target as ISubstanceSource;
        if (source == null && target != null)
        {
            source = target.GetComponent<ISubstanceSource>() ?? target.GetComponentInParent<ISubstanceSource>();
        }
        if (source != null)
        {
            return CurrentUnits >= Capacity ? $"Bucket full - {ContentsLabel}" : $"Hold to scoop - {ContentsLabel}";
        }
        return CurrentUnits > 0 ? $"Hold to empty bucket - {ContentsLabel}" : ContentsLabel;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestContextActionServerRpc(byte kind, ulong targetId, ServerRpcParams rpcParams = default)
    {
        ExecuteContextAction(rpcParams.Receive.SenderClientId, (ContextTargetKind)kind, targetId);
    }

    private void ExecuteContextAction(ulong senderClientId, ContextTargetKind kind, ulong targetId, MonoBehaviour localTarget = null)
    {
        if (!ValidateHolder(senderClientId, out PlayerInteractionNew player)) return;

        ISubstanceSource source = ResolveSource(kind, targetId, localTarget);
        if (source != null)
        {
            ContainerSubstanceSO requested = CurrentSubstance ?? FirstSupportedSubstance();
            Vector3 sourcePoint = source is BridgeConstructionSite site
                ? site.GetClosestInteractionPoint(player.transform.position)
                : ((MonoBehaviour)source).transform.position;
            if (requested == null || CurrentUnits >= Capacity || !source.CanExtract(requested) ||
                Vector3.Distance(player.transform.position, sourcePoint) > interactionDistance)
            {
                return;
            }

            if (source.TryExtract(requested, 1))
            {
                TryAddUnits(requested, 1, $"RMB scoop from {GetSourceDebugName(source)} by client {senderClientId}");
            }
            return;
        }

        if (CurrentUnits > 0)
        {
            DumpAll(
                player.transform.position + player.transform.forward * 1.1f + Vector3.up * 0.25f,
                $"RMB dump by client {senderClientId}");
        }
    }

    public bool TryAddUnits(ContainerSubstanceSO substance, int units)
    {
        return TryAddUnits(substance, units, "explicit API add");
    }

    private bool TryAddUnits(ContainerSubstanceSO substance, int units, string reason)
    {
        if (!IsAuthority() || substance == null || units <= 0 || CurrentUnits >= Capacity) return false;
        int index = GetSubstanceIndex(substance);
        if (index < 0 || (CurrentUnits > 0 && index != CurrentSubstanceIndex)) return false;
        SetContents(index, Mathf.Min(Capacity, CurrentUnits + units), reason);
        return true;
    }

    public int TryRemoveUnits(ContainerSubstanceSO substance, int units)
    {
        if (!IsAuthority() || substance == null || substance != CurrentSubstance || units <= 0) return 0;
        int removed = Mathf.Min(units, CurrentUnits);
        SetContents(CurrentSubstanceIndex, CurrentUnits - removed, "explicit API remove");
        return removed;
    }

    public void Empty(bool spawnContents = false, Vector3 position = default)
    {
        if (!IsAuthority()) return;
        if (spawnContents && CurrentUnits > 0) SpawnPile(position, CurrentSubstance, CurrentUnits);
        SetContents(-1, 0, spawnContents ? "explicit empty with spawned contents" : "explicit empty");
    }

    public void ReturnToRespawnPoint(float delay)
    {
        if (!IsAuthority() || IsReturning) return;
        carryResource?.ForceReleaseForEnvironmentalRemoval();
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
        SpawnPile(position, CurrentSubstance, CurrentUnits);
        SetContents(-1, 0, reason);
    }

    private void SpawnPile(Vector3 position, ContainerSubstanceSO substance, int units)
    {
        if (loosePilePrefab == null) return;
        LooseSubstancePile pile = Instantiate(loosePilePrefab, position, Quaternion.identity);
        pile.Initialize(substance, units);
        if (IsNetworkSessionActive()) pile.NetworkObject.Spawn();
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

    private void SetContents(int substanceIndex, int units, string reason)
    {
        int previousUnits = CurrentUnits;
        int previousSubstanceIndex = CurrentSubstanceIndex;
        int safeUnits = Mathf.Clamp(units, 0, Capacity);
        int safeIndex = safeUnits > 0 ? substanceIndex : -1;
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
    private void LogContentsChange(int previousSubstanceIndex, int previousUnits, int currentSubstanceIndex,
        int currentUnits, string reason)
    {
        if (previousSubstanceIndex == currentSubstanceIndex && previousUnits == currentUnits)
        {
            return;
        }

        UnityEngine.Debug.Log(
            $"[PortableSubstanceContainer] {name}: {previousUnits} -> {currentUnits} units ({reason}).",
            this);
    }

    private static string GetSourceDebugName(ISubstanceSource source)
    {
        return source is MonoBehaviour behaviour
            ? $"{behaviour.name} ({behaviour.GetInstanceID()})"
            : source.GetType().Name;
    }

    private void OnSubstanceChanged(int previous, int current) => ApplyVisual();
    private void OnUnitsChanged(int previous, int current) => ApplyVisual();
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
            position.y = Mathf.Lerp(contentBottomLocalY, contentTopLocalY, (float)CurrentUnits / Capacity);
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
    private ContainerSubstanceSO FirstSupportedSubstance() => supportedSubstances != null && supportedSubstances.Length > 0 ? supportedSubstances[0] : null;
    private bool IsNetworkSessionActive() => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool IsAuthority() => !IsNetworkSessionActive() || IsServer;
}
