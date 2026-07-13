using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public static class BaseResourceSpawnUtility
{
    private const float DefaultScatterRadius = 0.35f;

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
        Vector3 position = origin + GetSpawnOffset(recipe, spawnIndex);
        if (TrySpawnResource(resourceSO, position, rotation, out GameObject spawnedResource))
        {
            spawnedResources.Add(spawnedResource);
        }
    }

    private static Vector3 GetSpawnOffset(BaseResourceDestructionRecipe recipe, int spawnIndex)
    {
        if (recipe.spawnOffsets != null && spawnIndex >= 0 && spawnIndex < recipe.spawnOffsets.Length)
        {
            return recipe.spawnOffsets[spawnIndex];
        }

        float scatterRadius = recipe.fallbackScatterRadius > 0f ? recipe.fallbackScatterRadius : DefaultScatterRadius;
        Vector2 randomCircle = Random.insideUnitCircle * scatterRadius;
        return new Vector3(randomCircle.x, 0f, randomCircle.y);
    }
}
