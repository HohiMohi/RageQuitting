using UnityEngine;

/// <summary>Describes a held single-carry object that can enter local placement mode.</summary>
public interface ISingleCarryPlaceable
{
    bool CanEnterSingleCarryPlacement { get; }

    /// <summary>Returns true only while this object is held by the supplied local player and can be released offline.</summary>
    bool CanReleaseSingleCarryPlacement(PlayerInteractionNew holder);

    /// <summary>Commits an already-validated offline placement pose and restores ordinary rigidbody simulation.</summary>
    bool TryReleaseSingleCarryPlacement(PlayerInteractionNew holder, Vector3 position, Quaternion rotation);

    /// <summary>Checks server-owned carry bookkeeping before a network placement is committed.</summary>
    bool CanCompleteNetworkSingleCarryPlacement(ulong holderClientId, PlayerInteractionNew holder);

    /// <summary>Commits a previously validated exact placement pose on the server.</summary>
    bool TryCompleteNetworkSingleCarryPlacement(ulong holderClientId, PlayerInteractionNew holder,
        Vector3 position, Quaternion rotation);
}
