using UnityEngine;

public enum SingleCarryPlacementValidationStatus
{
    Valid,
    Occupied,
    UnsupportedGeometry,
    QueryBufferSaturated
}

public struct SingleCarryPlacementValidationResult
{
    public SingleCarryPlacementValidationStatus Status { get; }
    public Collider BlockingCollider { get; }
    public float PenetrationDepth { get; }
    public bool IsValid => Status == SingleCarryPlacementValidationStatus.Valid;

    public SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus status,
        Collider blockingCollider = null, float penetrationDepth = 0f)
    {
        Status = status;
        BlockingCollider = blockingCollider;
        PenetrationDepth = penetrationDepth;
    }
}

/// <summary>Checks a proposed carry pose without moving or changing the held object's physics state.</summary>
public sealed class SingleCarryPlacementValidator
{
    private const int InitialOverlapBufferSize = 32;
    private const int MaximumOverlapBufferSize = 8192;
    private const float BroadphasePadding = 0.001f;
    private const float SupportingContactTolerance = 0.00002f;
    // Native overlap queries include exact tangency and can report a few micrometers of
    // numerical overlap. Erode primitive query shapes by 50 um: live 6000.3 probes show
    // this clears 20 um contact noise while still detecting a 0.5 mm penetration.
    private const float NativeOverlapInset = 0.00005f;

    private readonly Transform sourceRoot;
    private readonly Collider[] sourceColliders;
    private Collider[] overlapBuffer = new Collider[InitialOverlapBufferSize];
    private Collider[] exactOverlapBuffer = new Collider[InitialOverlapBufferSize];

    public SingleCarryPlacementValidator(GameObject source)
    {
        sourceRoot = source != null ? source.transform : null;
        sourceColliders = source != null ? source.GetComponentsInChildren<Collider>(true) : System.Array.Empty<Collider>();
    }

    public SingleCarryPlacementValidationResult Validate(SingleCarryPlacementPose pose, Transform ignoredGhostRoot = null)
    {
        if (sourceRoot == null || pose.Status == SingleCarryPlacementGeometryStatus.NoPhysicalColliders
            || pose.Status == SingleCarryPlacementGeometryStatus.UnsupportedCollider)
            return new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry);
        if (pose.Status == SingleCarryPlacementGeometryStatus.RaycastBufferSaturated)
            return new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.QueryBufferSaturated);

        int activeColliderCount = 0;
        Matrix4x4 candidateRoot = Matrix4x4.TRS(pose.Position, pose.Rotation, sourceRoot.lossyScale);
        Matrix4x4 sourceRootInverse = sourceRoot.worldToLocalMatrix;

        foreach (Collider candidate in sourceColliders)
        {
            if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy || candidate.isTrigger)
                continue;
            activeColliderCount++;

            if (!IsSupportedShape(candidate))
                return new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry);

            Matrix4x4 candidateMatrix = candidateRoot * sourceRootInverse * candidate.transform.localToWorldMatrix;
            if (!TryGetCandidateTransform(candidateMatrix, out Vector3 candidatePosition, out Quaternion candidateRotation,
                    out Vector3 candidateScale))
                return new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry);
            Vector3 sourceScale = candidate.transform.lossyScale;
            if (Mathf.Abs(candidateScale.x - sourceScale.x) > 0.0001f
                || Mathf.Abs(candidateScale.y - sourceScale.y) > 0.0001f
                || Mathf.Abs(candidateScale.z - sourceScale.z) > 0.0001f)
                return new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry);

            if (!TryGetBroadphaseSphere(candidate, candidateMatrix, candidateScale, out Vector3 center, out float radius))
                return new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry);

            int hitCount;
            while (true)
            {
                hitCount = Physics.OverlapSphereNonAlloc(center, radius + BroadphasePadding, overlapBuffer,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                if (hitCount < overlapBuffer.Length)
                    break;
                if (overlapBuffer.Length >= MaximumOverlapBufferSize)
                    return new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.QueryBufferSaturated);
                System.Array.Resize(ref overlapBuffer, Mathf.Min(overlapBuffer.Length * 2, MaximumOverlapBufferSize));
            }

            for (int i = 0; i < hitCount; i++)
            {
                Collider obstacle = overlapBuffer[i];
                if (obstacle == null || !obstacle.enabled || !obstacle.gameObject.activeInHierarchy || obstacle.isTrigger
                    || IsInHierarchy(obstacle.transform, sourceRoot) || IsInHierarchy(obstacle.transform, ignoredGhostRoot))
                    continue;

                if (!Physics.ComputePenetration(candidate, candidatePosition, candidateRotation,
                        obstacle, obstacle.transform.position, obstacle.transform.rotation,
                        out _, out float penetrationDepth))
                {
                    if (IsAmbiguousConcaveObstacle(obstacle))
                    {
                        if (!TryExactPrimitiveOverlap(candidate, candidatePosition, candidateRotation,
                                candidateScale, obstacle, out bool exactOverlap, out bool saturated))
                            return new SingleCarryPlacementValidationResult(
                                SingleCarryPlacementValidationStatus.UnsupportedGeometry, obstacle);
                        if (saturated)
                            return new SingleCarryPlacementValidationResult(
                                SingleCarryPlacementValidationStatus.QueryBufferSaturated, obstacle);
                        if (exactOverlap)
                            return new SingleCarryPlacementValidationResult(
                                SingleCarryPlacementValidationStatus.Occupied, obstacle);
                    }
                    continue;
                }

                // A zero-depth contact is not an intersection, regardless of which coplanar
                // tile produced the ray hit. Positive depth beyond the small tolerance blocks.
                if (penetrationDepth <= SupportingContactTolerance)
                    continue;

                return new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.Occupied,
                    obstacle, penetrationDepth);
            }
        }

        return activeColliderCount == 0
            ? new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.UnsupportedGeometry)
            : new SingleCarryPlacementValidationResult(SingleCarryPlacementValidationStatus.Valid);
    }

    private static bool IsSupportedShape(Collider collider)
    {
        if (collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider)
            return true;
        return collider is MeshCollider meshCollider && meshCollider.convex && meshCollider.sharedMesh != null
            && meshCollider.sharedMesh.isReadable;
    }

    private static bool IsAmbiguousConcaveObstacle(Collider obstacle)
    {
        if (obstacle is TerrainCollider)
            return true;
        return obstacle is MeshCollider meshCollider && !meshCollider.convex;
    }

    private bool TryExactPrimitiveOverlap(Collider candidate, Vector3 position, Quaternion rotation,
        Vector3 scale, Collider target, out bool overlapsTarget, out bool saturated)
    {
        overlapsTarget = false;
        saturated = false;

        while (true)
        {
            int hitCount;
            if (candidate is BoxCollider box)
            {
                Vector3 halfExtents = Vector3.Scale(box.size * 0.5f, scale) - Vector3.one * NativeOverlapInset;
                if (halfExtents.x <= 0f || halfExtents.y <= 0f || halfExtents.z <= 0f)
                    return false;
                Vector3 center = position + rotation * Vector3.Scale(box.center, scale);
                hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, exactOverlapBuffer, rotation,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
            }
            else if (candidate is SphereCollider sphere)
            {
                float radius = sphere.radius * Mathf.Max(scale.x, scale.y, scale.z) - NativeOverlapInset;
                if (radius <= 0f)
                    return false;
                Vector3 center = position + rotation * Vector3.Scale(sphere.center, scale);
                hitCount = Physics.OverlapSphereNonAlloc(center, radius, exactOverlapBuffer,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
            }
            else if (candidate is CapsuleCollider capsule)
            {
                int axis = capsule.direction;
                int crossA = axis == 0 ? 1 : 0;
                int crossB = axis == 2 ? 1 : 2;
                float radius = capsule.radius * Mathf.Max(scale[crossA], scale[crossB]);
                float axisScale = scale[axis];
                float scaledHeight = Mathf.Max(capsule.height * axisScale, radius * 2f);
                float segmentHalfLength = Mathf.Max(0f, scaledHeight * 0.5f - radius);
                radius -= NativeOverlapInset;
                if (radius <= 0f)
                    return false;
                Vector3 localAxis = axis == 0 ? Vector3.right : axis == 2 ? Vector3.forward : Vector3.up;
                Vector3 center = position + rotation * Vector3.Scale(capsule.center, scale);
                Vector3 worldAxis = rotation * localAxis;
                Vector3 point0 = center - worldAxis * segmentHalfLength;
                Vector3 point1 = center + worldAxis * segmentHalfLength;
                hitCount = Physics.OverlapCapsuleNonAlloc(point0, point1, radius, exactOverlapBuffer,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
            }
            else
            {
                // There is no allocation-free exact query for a proposed convex-mesh shape.
                return false;
            }

            if (hitCount < exactOverlapBuffer.Length)
            {
                for (int i = 0; i < hitCount; i++)
                {
                    if (exactOverlapBuffer[i] == target)
                    {
                        overlapsTarget = true;
                        break;
                    }
                }
                return true;
            }

            if (exactOverlapBuffer.Length >= MaximumOverlapBufferSize)
            {
                saturated = true;
                return true;
            }
            System.Array.Resize(ref exactOverlapBuffer,
                Mathf.Min(exactOverlapBuffer.Length * 2, MaximumOverlapBufferSize));
        }
    }

    private static bool TryGetCandidateTransform(Matrix4x4 matrix, out Vector3 position,
        out Quaternion rotation, out Vector3 scale)
    {
        Vector3 x = matrix.MultiplyVector(Vector3.right);
        Vector3 y = matrix.MultiplyVector(Vector3.up);
        Vector3 z = matrix.MultiplyVector(Vector3.forward);
        position = matrix.GetColumn(3);
        scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
        if (!IsFinite(position) || !IsFinite(scale) || scale.x < 0.000001f || scale.y < 0.000001f || scale.z < 0.000001f)
        {
            rotation = Quaternion.identity;
            return false;
        }

        Vector3 nx = x / scale.x;
        Vector3 ny = y / scale.y;
        Vector3 nz = z / scale.z;
        const float shearTolerance = 0.0001f;
        float determinant = Vector3.Dot(Vector3.Cross(nx, ny), nz);
        if (Mathf.Abs(Vector3.Dot(nx, ny)) > shearTolerance
            || Mathf.Abs(Vector3.Dot(nx, nz)) > shearTolerance
            || Mathf.Abs(Vector3.Dot(ny, nz)) > shearTolerance
            || determinant < 0.999f)
        {
            rotation = Quaternion.identity;
            return false;
        }

        rotation = Quaternion.LookRotation(nz, ny);
        return true;
    }

    private static bool TryGetBroadphaseSphere(Collider collider, Matrix4x4 candidateMatrix,
        Vector3 scale, out Vector3 center, out float radius)
    {
        Vector3 localCenter;
        if (collider is BoxCollider box)
        {
            localCenter = box.center;
            Vector3 half = box.size * 0.5f;
            Vector3 x = candidateMatrix.MultiplyVector(Vector3.right * half.x);
            Vector3 y = candidateMatrix.MultiplyVector(Vector3.up * half.y);
            Vector3 z = candidateMatrix.MultiplyVector(Vector3.forward * half.z);
            radius = Mathf.Sqrt(x.sqrMagnitude + y.sqrMagnitude + z.sqrMagnitude);
        }
        else if (collider is SphereCollider sphere)
        {
            localCenter = sphere.center;
            radius = sphere.radius * Mathf.Max(scale.x, scale.y, scale.z);
        }
        else if (collider is CapsuleCollider capsule)
        {
            localCenter = capsule.center;
            int axis = capsule.direction;
            int crossA = axis == 0 ? 1 : 0;
            int crossB = axis == 2 ? 1 : 2;
            float scaledRadius = capsule.radius * Mathf.Max(scale[crossA], scale[crossB]);
            float scaledHeight = Mathf.Max(capsule.height * scale[axis], scaledRadius * 2f);
            radius = scaledRadius + Mathf.Max(0f, scaledHeight * 0.5f - scaledRadius);
        }
        else if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
        {
            Bounds bounds = meshCollider.sharedMesh.bounds;
            localCenter = bounds.center;
            radius = Vector3.Scale(bounds.extents, scale).magnitude;
        }
        else
        {
            center = Vector3.zero;
            radius = 0f;
            return false;
        }

        center = candidateMatrix.MultiplyPoint3x4(localCenter);
        return IsFinite(center) && !float.IsNaN(radius) && !float.IsInfinity(radius) && radius >= 0f;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private static bool IsInHierarchy(Transform candidate, Transform root)
    {
        return candidate != null && root != null && (candidate == root || candidate.IsChildOf(root));
    }
}
