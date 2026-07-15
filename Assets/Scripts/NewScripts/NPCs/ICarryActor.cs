using Unity.Netcode;
using UnityEngine;

public enum CarryActorType
{
    Player,
    NPC
}

public interface ICarryActor
{
    ulong ActorId { get; }
    CarryActorType ActorType { get; }
    NetworkObject NetworkObject { get; }
    Transform CarryAnchor { get; }
    Transform BodyAnchor { get; }
    Vector3 BodyAnchorLocalOffset { get; }
    float CollisionRadius { get; }
    bool CanCarryObject { get; }
    bool CanParticipateInSharedCarry { get; }
    void ConfirmCarry(GameObject carriedObject);
    void ConfirmSharedCarry(GameObject carriedObject, Vector3 attachLocalPoint, float movementPenalty);
    void ForceRelease(GameObject carriedObject);
    Vector3 GetSharedCarryInput();
    void ApplySharedCarryAttachment(Vector3 attachWorldPoint);
}
