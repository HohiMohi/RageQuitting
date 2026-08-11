using System.Diagnostics;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject), typeof(Rigidbody), typeof(Collider))]
public class LooseSubstancePile : NetworkBehaviour, ISubstanceSource
{
    [SerializeField] private ContainerSubstanceSO substance;
    [SerializeField, Min(1)] private int initialUnits = 1;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Vector3 oneUnitScale = new Vector3(0.45f, 0.2f, 0.45f);
    [SerializeField] private Vector3 fullScale = new Vector3(0.8f, 0.45f, 0.8f);

    private readonly NetworkVariable<int> unitsNetwork = new NetworkVariable<int>();
    private int localUnits;
    private bool isReturningToExcavation;
    private bool isConsumed;

    public ContainerSubstanceSO Substance => substance;
    public int CurrentUnits => IsSpawned ? unitsNetwork.Value : localUnits;

    private void Awake()
    {
        localUnits = Mathf.Max(1, initialUnits);
        ApplyVisual(localUnits);
    }

    public override void OnNetworkSpawn()
    {
        unitsNetwork.OnValueChanged += OnUnitsChanged;
        if (IsServer && unitsNetwork.Value <= 0)
        {
            unitsNetwork.Value = Mathf.Max(1, initialUnits);
        }
        ApplyVisual(unitsNetwork.Value);
    }

    public override void OnNetworkDespawn()
    {
        unitsNetwork.OnValueChanged -= OnUnitsChanged;
    }

    public void Initialize(ContainerSubstanceSO newSubstance, int units)
    {
        if (!IsAuthority()) return;
        initialUnits = Mathf.Max(1, units);
        substance = newSubstance;
        SetUnits(initialUnits);
    }

    public bool CanExtract(ContainerSubstanceSO requested)
    {
        return requested != null && requested == substance && CurrentUnits > 0;
    }

    public bool TryExtract(ContainerSubstanceSO requested, int units)
    {
        if (!IsAuthority() || !CanExtract(requested) || units <= 0) return false;
        SetUnits(CurrentUnits - Mathf.Min(units, CurrentUnits));
        return true;
    }

    public bool CanReturnTo(BridgeConstructionSite site)
    {
        return IsAuthority() && !isReturningToExcavation && !isConsumed && site != null && substance != null &&
               substance.IsSoil && CurrentUnits > 0;
    }

    public int TryReturnTo(BridgeConstructionSite site)
    {
        if (!CanReturnTo(site))
        {
            return 0;
        }

        isReturningToExcavation = true;
        try
        {
            int availableUnits = CurrentUnits;
            int progressBefore = site.RemovedSoilUnits;
            int acceptedUnits = Mathf.Clamp(site.ReturnSoil(availableUnits), 0, availableUnits);
            if (acceptedUnits > 0)
            {
                SetUnits(availableUnits - acceptedUnits);
            }

            LogReturnTransaction(site, availableUnits, acceptedUnits, progressBefore, site.RemovedSoilUnits);
            return acceptedUnits;
        }
        finally
        {
            if (!isConsumed)
            {
                isReturningToExcavation = false;
            }
        }
    }

    public bool OwnsCollider(Collider candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        Rigidbody attachedBody = candidate.attachedRigidbody;
        if (attachedBody != null)
        {
            return attachedBody.gameObject == gameObject;
        }

        return candidate.gameObject == gameObject;
    }

    public void RemoveUnitsFromWorld(int units)
    {
        if (!IsAuthority() || units <= 0) return;
        SetUnits(CurrentUnits - Mathf.Min(units, CurrentUnits));
    }

    private void SetUnits(int units)
    {
        int clamped = Mathf.Max(0, units);
        localUnits = clamped;
        if (IsSpawned)
        {
            unitsNetwork.Value = clamped;
        }
        else
        {
            ApplyVisual(clamped);
        }

        if (clamped == 0)
        {
            isConsumed = true;
            DisableColliders();
            if (IsSpawned && NetworkObject != null) NetworkObject.Despawn(true);
            else Destroy(gameObject);
        }
    }

    private void DisableColliders()
    {
        foreach (Collider pileCollider in GetComponentsInChildren<Collider>(true))
        {
            pileCollider.enabled = false;
        }
    }

    private void OnUnitsChanged(int previous, int current) => ApplyVisual(current);

    [Conditional("UNITY_EDITOR")]
    private void LogReturnTransaction(BridgeConstructionSite site, int availableUnits, int acceptedUnits,
        int progressBefore, int progressAfter)
    {
        UnityEngine.Debug.Log(
            $"[LooseSubstancePile] {name} ({GetInstanceID()}): returned {acceptedUnits}/{availableUnits} units " +
            $"to {site.name}; excavation {progressBefore} -> {progressAfter}.",
            this);
    }

    private void ApplyVisual(int units)
    {
        if (visualRoot == null) return;
        float t = Mathf.InverseLerp(1f, 3f, Mathf.Clamp(units, 1, 3));
        visualRoot.localScale = Vector3.Lerp(oneUnitScale, fullScale, t);
    }

    private bool IsAuthority()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            return true;
        }

        return IsSpawned ? IsServer : NetworkManager.Singleton.IsServer;
    }
}
