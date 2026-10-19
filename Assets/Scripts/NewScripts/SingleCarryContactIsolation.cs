using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Suppresses solid contacts from a single-carried compound collider while leaving
/// the colliders enabled for physics queries and placement validation.
/// </summary>
internal sealed class SingleCarryContactIsolation
{
    private readonly struct ColliderSettings
    {
        public readonly Collider Collider;
        public readonly LayerMask ExcludedLayers;
        public readonly int OverridePriority;

        public ColliderSettings(Collider collider)
        {
            Collider = collider;
            ExcludedLayers = collider.excludeLayers;
            OverridePriority = collider.layerOverridePriority;
        }
    }

    private readonly Transform root;
    private readonly List<ColliderSettings> originalSettings = new();
    private bool isIsolated;

    public SingleCarryContactIsolation(Transform root)
    {
        this.root = root;
    }

    public void SetIsolated(bool isolate)
    {
        if (root == null)
        {
            Restore();
            return;
        }

        if (!isolate)
        {
            Restore();
            return;
        }

        if (isIsolated)
        {
            ReapplyToCurrentColliders();
            return;
        }

        originalSettings.Clear();
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || collider.isTrigger) continue;
            originalSettings.Add(new ColliderSettings(collider));
            collider.excludeLayers = Physics.AllLayers;
            collider.layerOverridePriority = int.MaxValue;
        }

        isIsolated = true;
    }

    public void Restore()
    {
        if (!isIsolated)
        {
            originalSettings.Clear();
            return;
        }

        for (int i = 0; i < originalSettings.Count; i++)
        {
            ColliderSettings settings = originalSettings[i];
            if (settings.Collider == null) continue;
            settings.Collider.excludeLayers = settings.ExcludedLayers;
            settings.Collider.layerOverridePriority = settings.OverridePriority;
        }

        originalSettings.Clear();
        isIsolated = false;
    }

    private void ReapplyToCurrentColliders()
    {
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || collider.isTrigger) continue;

            bool alreadyTracked = false;
            for (int i = 0; i < originalSettings.Count; i++)
            {
                if (originalSettings[i].Collider == collider)
                {
                    alreadyTracked = true;
                    break;
                }
            }

            if (!alreadyTracked) originalSettings.Add(new ColliderSettings(collider));
            collider.excludeLayers = Physics.AllLayers;
            collider.layerOverridePriority = int.MaxValue;
        }
    }
}
