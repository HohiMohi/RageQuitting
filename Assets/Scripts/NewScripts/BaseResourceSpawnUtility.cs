using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public static class BaseResourceSpawnUtility
{
    private const float DefaultScatterRadius = 0.35f;
    private const float DefaultSpawnOffsetScatterRadius = 0.15f;
    private const float DefaultScatterVelocityMin = 0.5f;
    private const float DefaultScatterVelocityMax = 1.5f;
    private const float DefaultScatterUpwardBias = 0.5f;

    public static bool TrySpawnResource(BaseResourceSO resourceSO, Vector3 position, Quaternion rotation, out GameObject spawnedResource)
    {
        spawnedResource = null;
        if (resourceSO == null || resourceSO.resourcePrefab == null)
        {
            return false;
        }

        bool networkSessionActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networkSessionActive && !NetworkManager.Singleton.IsServer)
        {
            return false;
        }

        spawnedResource = Object.Instantiate(resourceSO.resourcePrefab, position, rotation);
        if (!networkSessionActive)
        {
            return true;
        }

        if (!spawnedResource.TryGetComponent(out NetworkObject networkObject))
        {
            Debug.LogError($"{spawnedResource.name} is missing NetworkObject and cannot be spawned as a networked resource.");
            Object.Destroy(spawnedResource);
            spawnedResource = null;
            return false;
        }

        networkObject.Spawn(true);
        return true;
    }

    public static IReadOnlyList<GameObject> SpawnProducts(BaseResourceDestructionRecipe recipe, Vector3 origin, Quaternion rotation)
    {
        List<GameObject> spawnedResources = new List<GameObject>();
        int spawnIndex = 0;

        if (recipe.products != null && recipe.products.Length > 0)
        {
            foreach (BaseResourceDestructionProduct product in recipe.products)
            {
                int amount = Mathf.Max(0, product.amount);
                for (int i = 0; i < amount; i++)
                {
                    TrySpawnProduct(product.resourceSO, recipe, origin, rotation, spawnIndex, spawnedResources);
                    spawnIndex++;
                }
            }

            return spawnedResources;
        }

        if (recipe.finalProductBaseResourceSO != null)
        {
            TrySpawnProduct(recipe.finalProductBaseResourceSO, recipe, origin, rotation, spawnIndex, spawnedResources);
        }

        return spawnedResources;
    }

    private static void TrySpawnProduct(BaseResourceSO resourceSO, BaseResourceDestructionRecipe recipe, Vector3 origin, Quaternion rotation, int spawnIndex, List<GameObject> spawnedResources)
    {
        Vector3 spawnOffset = GetSpawnOffset(recipe, spawnIndex, out bool applySpawnOffsetImpulse);
        Vector3 position = origin + spawnOffset;
        if (TrySpawnResource(resourceSO, position, rotation, out GameObject spawnedResource))
        {
            spawnedResources.Add(spawnedResource);
            if (applySpawnOffsetImpulse)
            {
                ApplySpawnOffsetImpulse(spawnedResource, recipe, spawnOffset);
            }
        }
    }

    private static Vector3 GetSpawnOffset(BaseResourceDestructionRecipe recipe, int spawnIndex, out bool applySpawnOffsetImpulse)
    {
        applySpawnOffsetImpulse = false;
        if (recipe.spawnOffsets != null && spawnIndex >= 0 && spawnIndex < recipe.spawnOffsets.Length)
        {
            Vector3 spawnOffset = recipe.spawnOffsets[spawnIndex];
            if (!recipe.scatterSpawnOffsets)
            {
                return spawnOffset;
            }

            float offsetScatterRadius = recipe.spawnOffsetScatterRadius > 0f
                ? recipe.spawnOffsetScatterRadius
                : DefaultSpawnOffsetScatterRadius;
            applySpawnOffsetImpulse = true;
            return spawnOffset + Random.insideUnitSphere * offsetScatterRadius;
        }

        float scatterRadius = recipe.fallbackScatterRadius > 0f ? recipe.fallbackScatterRadius : DefaultScatterRadius;
        Vector2 randomCircle = Random.insideUnitCircle * scatterRadius;
        return new Vector3(randomCircle.x, 0f, randomCircle.y);
    }

    private static void ApplySpawnOffsetImpulse(GameObject spawnedResource, BaseResourceDestructionRecipe recipe, Vector3 spawnOffset)
    {
        if (spawnedResource == null
            || !spawnedResource.TryGetComponent(out Rigidbody body)
            || body.isKinematic)
        {
            return;
        }

        float minimumVelocity = recipe.scatterVelocityMin > 0f
            ? recipe.scatterVelocityMin
            : DefaultScatterVelocityMin;
        float maximumVelocity = recipe.scatterVelocityMax > 0f
            ? recipe.scatterVelocityMax
            : DefaultScatterVelocityMax;
        minimumVelocity = Mathf.Max(0f, minimumVelocity);
        maximumVelocity = Mathf.Max(0f, maximumVelocity);
        if (minimumVelocity > maximumVelocity)
        {
            (minimumVelocity, maximumVelocity) = (maximumVelocity, minimumVelocity);
        }

        float upwardBias = recipe.scatterUpwardBias > 0f
            ? recipe.scatterUpwardBias
            : DefaultScatterUpwardBias;
        Vector3 impulseDirection = new Vector3(
            spawnOffset.x,
            Mathf.Abs(spawnOffset.y) + upwardBias,
            spawnOffset.z);
        if (impulseDirection.sqrMagnitude <= 0.0001f)
        {
            impulseDirection = Vector3.up;
        }

        float velocity = Random.Range(minimumVelocity, maximumVelocity);
        body.AddForce(impulseDirection.normalized * velocity, ForceMode.VelocityChange);
    }
}
