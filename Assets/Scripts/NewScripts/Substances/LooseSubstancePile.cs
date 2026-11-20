using System;
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

    private readonly NetworkVariable<float> unitsNetwork = new NetworkVariable<float>();
    private float localUnits;
    private bool isReturningToExcavation;
    private bool isConsumed;

    public ContainerSubstanceSO Substance => substance;
    public float CurrentUnits => IsSpawned ? unitsNetwork.Value : localUnits;

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
            unitsNetwork.Value = localUnits;
        }
        ApplyVisual(unitsNetwork.Value);
    }

    public override void OnNetworkDespawn()
    {
        unitsNetwork.OnValueChanged -= OnUnitsChanged;
    }

    public void Initialize(ContainerSubstanceSO newSubstance, float units)
    {
        if (!IsAuthority() || newSubstance == null || !SubstanceAmount.IsFinitePositive(units)) return;
        substance = newSubstance;
        SetUnits(units);
    }

    public bool CanExtract(ContainerSubstanceSO requested)
    {
        return requested != null && requested == substance && CurrentUnits > 0;
    }

    public float TryExtract(ContainerSubstanceSO requested, float units)
    {
        if (!IsAuthority() || !CanExtract(requested) || !SubstanceAmount.IsFinitePositive(units)) return 0f;
        float extracted = Mathf.Min(units, CurrentUnits);
        SetUnits(CurrentUnits - extracted);
        return extracted;
    }

    public bool CanReturnTo(BridgeConstructionSite site)
    {
        return IsAuthority() && !isReturningToExcavation && !isConsumed && site != null && substance != null &&
               substance.IsSoil && CurrentUnits > 0;
    }

    public float TryReturnTo(BridgeConstructionSite site)
    {
        if (!CanReturnTo(site))
        {
            return 0;
        }

        isReturningToExcavation = true;
        try
        {
            float availableUnits = CurrentUnits;
            float progressBefore = site.RemovedSoilUnits;
            float acceptedUnits;
            if (site.ExcavationVolume == null || !site.ExcavationVolume.UsesLooseSoilHeightField)
            {
                acceptedUnits = Mathf.Clamp(site.ReturnSoil(availableUnits), 0f, availableUnits);
                if (acceptedUnits > 0f) SetUnits(availableUnits - acceptedUnits);
            }
            else
            {
                acceptedUnits = Mathf.Clamp(
                    site.TryReturnLooseSoilFromPile(this, transform.position), 0f, availableUnits);
            }

            if (acceptedUnits > 0f)
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

    public float RemoveUnitsFromWorld(float units)
    {
        if (!IsAuthority() || !SubstanceAmount.IsFinitePositive(units)) return 0f;
        float removed = Mathf.Min(units, CurrentUnits);
        SetUnits(CurrentUnits - removed);
        return removed;
    }

    internal double GetMaximumRemovableUnits(double maximumDebit)
    {
        if (!IsAuthority() || isConsumed || double.IsNaN(maximumDebit) ||
            double.IsInfinity(maximumDebit) || maximumDebit <= 0d) return 0d;
        GetMaximumRemovalRequest(CurrentUnits, CurrentUnits, maximumDebit, out double storedDelta);
        return storedDelta;
    }

    internal double RemoveUnitsWithinBudget(double maximumDebit)
    {
        if (!IsAuthority() || isConsumed || double.IsNaN(maximumDebit) ||
            double.IsInfinity(maximumDebit) || maximumDebit <= 0d) return 0d;
        float unitsBefore = CurrentUnits;
        float request = GetMaximumRemovalRequest(unitsBefore, unitsBefore, maximumDebit, out double expectedDebit);
        if (request <= 0f || expectedDebit <= 0d) return 0d;
        RemoveUnitsFromWorld(request);
        double actualDebit = (double)unitsBefore - CurrentUnits;
        if (actualDebit > maximumDebit)
        {
            if (CurrentUnits > 0f) SetUnits(unitsBefore);
            return 0d;
        }
        return actualDebit;
    }

    private static float GetMaximumRemovalRequest(float currentUnits, float maximumRequest,
        double maximumDebit, out double storedDelta)
    {
        storedDelta = 0d;
        if (!SubstanceAmount.IsFinitePositive(currentUnits) ||
            !SubstanceAmount.IsFinitePositive(maximumRequest) || double.IsNaN(maximumDebit) ||
            double.IsInfinity(maximumDebit) || maximumDebit <= 0d) return 0f;

        maximumRequest = Mathf.Min(currentUnits, maximumRequest);
        int low = 0;
        int high = BitConverter.SingleToInt32Bits(maximumRequest);
        while (low < high)
        {
            int middle = (int)(low + ((long)high - low + 1L) / 2L);
            float request = BitConverter.Int32BitsToSingle(middle);
            float resultingUnits = Mathf.Max(0f, currentUnits - request);
            double moved = (double)currentUnits - resultingUnits;
            if (moved <= maximumDebit) low = middle;
            else high = middle - 1;
        }

        float safeRequest = BitConverter.Int32BitsToSingle(low);
        storedDelta = (double)currentUnits - Mathf.Max(0f, currentUnits - safeRequest);
        return storedDelta > 0d ? safeRequest : 0f;
    }

    private void SetUnits(float units)
    {
        float clamped = float.IsNaN(units) || float.IsInfinity(units) ? 0f : Mathf.Max(0f, units);
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

    private void OnUnitsChanged(float previous, float current) => ApplyVisual(current);

    [Conditional("UNITY_EDITOR")]
    private void LogReturnTransaction(BridgeConstructionSite site, float availableUnits, float acceptedUnits,
        float progressBefore, float progressAfter)
    {
        UnityEngine.Debug.Log(
            $"[LooseSubstancePile] {name} ({GetInstanceID()}): returned {SubstanceAmount.Format(acceptedUnits)}/{SubstanceAmount.Format(availableUnits)} units " +
            $"to {site.name}; excavation {SubstanceAmount.Format(progressBefore)} -> {SubstanceAmount.Format(progressAfter)}.",
            this);
    }

    private void ApplyVisual(float units)
    {
        if (visualRoot == null) return;
        float count = Mathf.Clamp(units, 0f, 3f);
        float unitBlend = Mathf.Clamp01(count);
        float sizeBlend = Mathf.InverseLerp(1f, 3f, count);
        visualRoot.localScale = Vector3.Lerp(oneUnitScale * unitBlend, fullScale, sizeBlend);
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
