using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SharedCarryCollisionController : MonoBehaviour
{
    private readonly List<IgnoredCollisionPair> ignoredPairs = new List<IgnoredCollisionPair>();

    public void SetHolderCollisionIgnored(Transform holderRoot, bool ignored)
    {
        if (holderRoot == null)
        {
            return;
        }

        RestoreHolderCollisions(holderRoot);
        if (!ignored)
        {
            return;
        }

        Collider[] carriedColliders = GetComponentsInChildren<Collider>(false);
        Collider[] holderColliders = holderRoot.GetComponentsInChildren<Collider>(false);
        foreach (Collider carriedCollider in carriedColliders)
        {
            if (carriedCollider == null || carriedCollider.isTrigger)
            {
                continue;
            }

            foreach (Collider holderCollider in holderColliders)
            {
                if (holderCollider == null || holderCollider.isTrigger)
                {
                    continue;
                }

                Physics.IgnoreCollision(carriedCollider, holderCollider, true);
                ignoredPairs.Add(new IgnoredCollisionPair(holderRoot, carriedCollider, holderCollider));
            }
        }
    }

    public void RestoreHolderCollisions(Transform holderRoot)
    {
        for (int index = ignoredPairs.Count - 1; index >= 0; index--)
        {
            IgnoredCollisionPair pair = ignoredPairs[index];
            if (pair.HolderRoot != holderRoot)
            {
                continue;
            }

            if (pair.CarriedCollider != null && pair.HolderCollider != null)
            {
                Physics.IgnoreCollision(pair.CarriedCollider, pair.HolderCollider, false);
            }

            ignoredPairs.RemoveAt(index);
        }
    }

    public void RestoreAllCollisions()
    {
        for (int index = ignoredPairs.Count - 1; index >= 0; index--)
        {
            IgnoredCollisionPair pair = ignoredPairs[index];
            if (pair.CarriedCollider != null && pair.HolderCollider != null)
            {
                Physics.IgnoreCollision(pair.CarriedCollider, pair.HolderCollider, false);
            }
        }

        ignoredPairs.Clear();
    }

    private void OnDestroy()
    {
        RestoreAllCollisions();
    }

    private readonly struct IgnoredCollisionPair
    {
        public readonly Transform HolderRoot;
        public readonly Collider CarriedCollider;
        public readonly Collider HolderCollider;

        public IgnoredCollisionPair(Transform holderRoot, Collider carriedCollider, Collider holderCollider)
        {
            HolderRoot = holderRoot;
            CarriedCollider = carriedCollider;
            HolderCollider = holderCollider;
        }
    }
}
