using UnityEngine;

public static class SharedCarryCollisionShapeUtility
{
    public static bool TryGetCapsule(GameObject root, out Vector3 point1, out Vector3 point2, out float radius)
    {
        point1 = Vector3.zero;
        point2 = Vector3.zero;
        radius = 0f;

        if (root == null)
        {
            return false;
        }

        if (root.TryGetComponent(out CharacterController characterController) && characterController.enabled)
        {
            return TryGetCharacterControllerCapsule(characterController, out point1, out point2, out radius);
        }

        if (root.TryGetComponent(out CapsuleCollider capsuleCollider) && capsuleCollider.enabled)
        {
            return TryGetCapsuleColliderCapsule(capsuleCollider, out point1, out point2, out radius);
        }

        Collider collider = root.GetComponent<Collider>();
        if (collider == null || !collider.enabled)
        {
            return false;
        }

        Bounds bounds = collider.bounds;
        radius = Mathf.Max(0.01f, Mathf.Min(bounds.extents.x, bounds.extents.z));
        float halfSegment = Mathf.Max(0f, bounds.extents.y - radius);
        point1 = bounds.center + Vector3.up * halfSegment;
        point2 = bounds.center - Vector3.up * halfSegment;
        return true;
    }

    private static bool TryGetCharacterControllerCapsule(CharacterController characterController, out Vector3 point1, out Vector3 point2, out float radius)
    {
        Transform root = characterController.transform;
        Vector3 lossyScale = root.lossyScale;
        float horizontalScale = Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z));
        float verticalScale = Mathf.Abs(lossyScale.y);

        radius = Mathf.Max(0.01f, characterController.radius * horizontalScale);
        float height = Mathf.Max(characterController.height * verticalScale, radius * 2f);
        Vector3 center = root.TransformPoint(characterController.center);
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 up = root.up;
        point1 = center + up * halfSegment;
        point2 = center - up * halfSegment;
        return true;
    }

    private static bool TryGetCapsuleColliderCapsule(CapsuleCollider capsuleCollider, out Vector3 point1, out Vector3 point2, out float radius)
    {
        Transform root = capsuleCollider.transform;
        if (capsuleCollider.direction != 1)
        {
            Bounds bounds = capsuleCollider.bounds;
            radius = Mathf.Max(0.01f, Mathf.Min(bounds.extents.x, bounds.extents.z));
            float boundsHalfSegment = Mathf.Max(0f, bounds.extents.y - radius);
            point1 = bounds.center + Vector3.up * boundsHalfSegment;
            point2 = bounds.center - Vector3.up * boundsHalfSegment;
            return true;
        }

        Vector3 lossyScale = root.lossyScale;
        float horizontalScale = Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z));
        float verticalScale = Mathf.Abs(lossyScale.y);
        radius = Mathf.Max(0.01f, capsuleCollider.radius * horizontalScale);
        float height = Mathf.Max(capsuleCollider.height * verticalScale, radius * 2f);
        Vector3 center = root.TransformPoint(capsuleCollider.center);
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 up = root.up;
        point1 = center + up * halfSegment;
        point2 = center - up * halfSegment;
        return true;
    }
}
