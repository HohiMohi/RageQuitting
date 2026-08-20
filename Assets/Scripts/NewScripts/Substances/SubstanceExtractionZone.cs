using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class SubstanceExtractionZone : MonoBehaviour, ISubstanceSource, IInteractableNew, IInteractionPromptProvider
{
    private static readonly Dictionary<int, SubstanceExtractionZone> ActiveZones = new Dictionary<int, SubstanceExtractionZone>();

    [SerializeField] private int sourceId;
    [SerializeField] private ContainerSubstanceSO substance;
    [SerializeField] private string interactionLabel = "Scoop";

    private Collider[] sourceColliders;

    public int SourceId => sourceId;
    public ContainerSubstanceSO Substance => substance;

    private void Awake()
    {
        CacheColliders();
    }

    private void OnEnable()
    {
        CacheColliders();
        if (sourceId != 0)
        {
            ActiveZones[sourceId] = this;
        }
    }

    private void OnDisable()
    {
        if (sourceId != 0 && ActiveZones.TryGetValue(sourceId, out SubstanceExtractionZone current) && current == this)
        {
            ActiveZones.Remove(sourceId);
        }
    }

    public static bool TryGet(int id, out SubstanceExtractionZone zone)
    {
        return ActiveZones.TryGetValue(id, out zone) && zone != null && zone.isActiveAndEnabled;
    }

    public bool CanExtract(ContainerSubstanceSO requested)
    {
        return requested != null && requested == substance;
    }

    public bool TryExtract(ContainerSubstanceSO requested, int units)
    {
        return units > 0 && CanExtract(requested) && HasAuthority();
    }

    public Vector3 GetClosestInteractionPoint(Vector3 worldPosition)
    {
        CacheColliders();
        Vector3 closestPoint = transform.position;
        float closestSqrDistance = (closestPoint - worldPosition).sqrMagnitude;

        foreach (Collider sourceCollider in sourceColliders)
        {
            if (sourceCollider == null || !sourceCollider.enabled || !sourceCollider.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 candidate = sourceCollider.ClosestPoint(worldPosition);
            float sqrDistance = (candidate - worldPosition).sqrMagnitude;
            if (sqrDistance < closestSqrDistance)
            {
                closestPoint = candidate;
                closestSqrDistance = sqrDistance;
            }
        }

        return closestPoint;
    }

    public void Interact(Transform interactor)
    {
    }

    public void LookedAt(Transform interactor) { }

    public void LookedAway(Transform interactor) { }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        string name = substance != null ? substance.DisplayName : "substance";
        prompts.Add(new InteractionPrompt(PlayerInputActionKind.ActionAlt, $"Hold to {interactionLabel.ToLowerInvariant()} {name}"));
    }

    private static bool HasAuthority()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager == null || !manager.IsListening || manager.IsServer;
    }

    private void CacheColliders()
    {
        if (sourceColliders == null || sourceColliders.Length == 0)
        {
            sourceColliders = GetComponentsInChildren<Collider>(true);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (sourceId == 0)
        {
            sourceId = Mathf.Abs(GetInstanceID());
        }
    }
#endif
}
