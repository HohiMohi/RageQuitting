using System;
using System.Collections.Generic;
using UnityEngine;

public enum SingleCarryPlacementGeometryStatus
{
    NoObstacle,
    SurfaceSupported,
    NoPhysicalColliders,
    UnsupportedCollider,
    RaycastBufferSaturated
}

public struct SingleCarryPlacementPose
{
    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    public SingleCarryPlacementGeometryStatus Status { get; }
    public Collider SurfaceCollider { get; }
    public Vector3 SurfaceNormal { get; }

    public SingleCarryPlacementPose(Vector3 position, Quaternion rotation,
        SingleCarryPlacementGeometryStatus status, Collider surfaceCollider, Vector3 surfaceNormal)
    {
        Position = position;
        Rotation = rotation;
        Status = status;
        SurfaceCollider = surfaceCollider;
        SurfaceNormal = surfaceNormal;
    }
}

/// <summary>Computes a surface-supported pose from cached physical shapes and a center-camera aim ray.</summary>
public sealed class SingleCarryPlacementGeometrySolver
{
    private enum ShapeKind { Box, Sphere, Capsule, Mesh, Unsupported }

    private sealed class Shape
    {
        public Collider Collider;
        public ShapeKind Kind;
        public Vector3[] MeshVertices;
    }

    private const int MaximumRaycastBufferSize = 8192;
    private readonly Transform sourceRoot;
    private readonly Shape[] shapes;
    private RaycastHit[] raycastHits = new RaycastHit[32];

    public SingleCarryPlacementGeometrySolver(GameObject source)
    {
        sourceRoot = source != null ? source.transform : null;
        Collider[] colliders = source != null ? source.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
        var foundShapes = new List<Shape>(colliders.Length);
        foreach (Collider collider in colliders)
        {
            if (collider == null || collider.isTrigger) continue;
            Shape shape = new Shape { Collider = collider };
            if (collider is BoxCollider) shape.Kind = ShapeKind.Box;
            else if (collider is SphereCollider) shape.Kind = ShapeKind.Sphere;
            else if (collider is CapsuleCollider) shape.Kind = ShapeKind.Capsule;
            else if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
            {
                shape.Kind = ShapeKind.Mesh;
                Mesh mesh = meshCollider.sharedMesh;
                if (!mesh.isReadable)
                {
                    shape.Kind = ShapeKind.Unsupported;
                }
                else
                {
                    try
                    {
                        Vector3[] vertices = mesh.vertices;
                        int[] triangles = mesh.triangles;
                        var referenced = new bool[vertices.Length];
                        foreach (int index in triangles)
                            if ((uint)index < (uint)referenced.Length) referenced[index] = true;
                        var usedVertices = new List<Vector3>(vertices.Length);
                        for (int i = 0; i < vertices.Length; i++)
                            if (referenced[i]) usedVertices.Add(vertices[i]);
                        shape.MeshVertices = usedVertices.ToArray();
                    }
                    catch (UnityException) { shape.MeshVertices = null; }
                }
                if (shape.MeshVertices == null || shape.MeshVertices.Length == 0) shape.Kind = ShapeKind.Unsupported;
            }
            else shape.Kind = ShapeKind.Unsupported;
            foundShapes.Add(shape);
        }
        shapes = foundShapes.ToArray();
    }

    public SingleCarryPlacementPose Calculate(Ray aimRay, float distance, Quaternion baseYaw,
        Quaternion localRotation, Transform ignoredOwner)
    {
        Vector3 aimPoint = aimRay.GetPoint(Mathf.Max(0f, distance));
        Quaternion rotation = baseYaw * localRotation;
        if (sourceRoot == null)
            return new SingleCarryPlacementPose(aimPoint, rotation,
                SingleCarryPlacementGeometryStatus.NoPhysicalColliders, null, Vector3.zero);

        int physicalCount = 0;
        foreach (Shape shape in shapes)
        {
            Collider collider = shape.Collider;
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) continue;
            physicalCount++;
            if (shape.Kind == ShapeKind.Unsupported)
                return new SingleCarryPlacementPose(aimPoint, rotation,
                    SingleCarryPlacementGeometryStatus.UnsupportedCollider, null, Vector3.zero);
        }
        if (physicalCount == 0)
            return new SingleCarryPlacementPose(aimPoint, rotation,
                SingleCarryPlacementGeometryStatus.NoPhysicalColliders, null, Vector3.zero);

        if (!TryFindFirstSurface(aimRay, Mathf.Max(0f, distance), ignoredOwner, out RaycastHit hit,
                out SingleCarryPlacementGeometryStatus rayStatus))
            return new SingleCarryPlacementPose(aimPoint, rotation, rayStatus, null, Vector3.zero);

        Vector3 normal = hit.normal.sqrMagnitude > 0.000001f ? hit.normal.normalized : Vector3.up;
        Matrix4x4 candidateRootMatrix = Matrix4x4.TRS(hit.point, rotation, sourceRoot.lossyScale);
        float minimumProjection = float.PositiveInfinity;
        foreach (Shape shape in shapes)
        {
            Collider collider = shape.Collider;
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) continue;
            Matrix4x4 localToWorld = candidateRootMatrix * sourceRoot.worldToLocalMatrix * collider.transform.localToWorldMatrix;
            if (!TryGetMinimumProjection(shape, localToWorld, normal, out float projection))
                return new SingleCarryPlacementPose(aimPoint, rotation,
                    SingleCarryPlacementGeometryStatus.UnsupportedCollider, hit.collider, normal);
            minimumProjection = Mathf.Min(minimumProjection, projection);
        }

        float shiftAlongNormal = Vector3.Dot(hit.point, normal) - minimumProjection;
        Vector3 position = hit.point + normal * shiftAlongNormal;
        return new SingleCarryPlacementPose(position, rotation,
            SingleCarryPlacementGeometryStatus.SurfaceSupported, hit.collider, normal);
    }

    private bool TryFindFirstSurface(Ray ray, float maxDistance, Transform ignoredOwner, out RaycastHit nearest,
        out SingleCarryPlacementGeometryStatus status)
    {
        nearest = default;
        status = SingleCarryPlacementGeometryStatus.NoObstacle;
        if (maxDistance <= 0f) return false;

        int hitCount;
        while (true)
        {
            hitCount = Physics.RaycastNonAlloc(ray, raycastHits, maxDistance, Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            if (hitCount < raycastHits.Length) break;
            if (raycastHits.Length >= MaximumRaycastBufferSize)
            {
                status = SingleCarryPlacementGeometryStatus.RaycastBufferSaturated;
                return false;
            }
            Array.Resize(ref raycastHits, Mathf.Min(raycastHits.Length * 2, MaximumRaycastBufferSize));
        }

        float nearestDistance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = raycastHits[i];
            Collider collider = candidate.collider;
            if (collider == null || collider.isTrigger || IsInHierarchy(collider.transform, sourceRoot)
                || IsInHierarchy(collider.transform, ignoredOwner)) continue;
            if (candidate.distance >= nearestDistance) continue;
            nearestDistance = candidate.distance;
            nearest = candidate;
            found = true;
        }
        return found;
    }

    private static bool TryGetMinimumProjection(Shape shape, Matrix4x4 localToWorld, Vector3 normal, out float minimum)
    {
        Collider collider = shape.Collider;
        if (HasShear(localToWorld))
        {
            minimum = 0f;
            return false;
        }
        if (shape.Kind == ShapeKind.Box && collider is BoxCollider box)
        {
            Vector3 center = localToWorld.MultiplyPoint3x4(box.center);
            Vector3 half = box.size * 0.5f;
            Vector3 x = localToWorld.MultiplyVector(Vector3.right * half.x);
            Vector3 y = localToWorld.MultiplyVector(Vector3.up * half.y);
            Vector3 z = localToWorld.MultiplyVector(Vector3.forward * half.z);
            minimum = Vector3.Dot(center, normal) - Mathf.Abs(Vector3.Dot(x, normal))
                - Mathf.Abs(Vector3.Dot(y, normal)) - Mathf.Abs(Vector3.Dot(z, normal));
            return true;
        }
        if (shape.Kind == ShapeKind.Sphere && collider is SphereCollider sphere)
        {
            Vector3 center = localToWorld.MultiplyPoint3x4(sphere.center);
            float radius = sphere.radius * MaximumAxisScale(localToWorld);
            minimum = Vector3.Dot(center, normal) - radius;
            return true;
        }
        if (shape.Kind == ShapeKind.Capsule && collider is CapsuleCollider capsule)
        {
            Vector3 center = localToWorld.MultiplyPoint3x4(capsule.center);
            int axis = capsule.direction;
            int crossA = axis == 0 ? 1 : 0;
            int crossB = axis == 2 ? 1 : 2;
            Vector3 axisVector = axis == 0 ? Vector3.right : axis == 2 ? Vector3.forward : Vector3.up;
            Vector3 scale = new Vector3(localToWorld.MultiplyVector(Vector3.right).magnitude,
                localToWorld.MultiplyVector(Vector3.up).magnitude, localToWorld.MultiplyVector(Vector3.forward).magnitude);
            float worldRadius = capsule.radius * Mathf.Max(scale[crossA], scale[crossB]);
            float worldHeight = Mathf.Max(capsule.height * scale[axis], worldRadius * 2f);
            float segmentHalfLength = Mathf.Max(0f, worldHeight * 0.5f - worldRadius);
            Vector3 worldAxis = localToWorld.MultiplyVector(axisVector).normalized;
            float segmentSupport = segmentHalfLength * Mathf.Abs(Vector3.Dot(worldAxis, normal));
            minimum = Vector3.Dot(center, normal) - segmentSupport - worldRadius;
            return true;
        }
        if (shape.Kind == ShapeKind.Mesh && shape.MeshVertices != null && shape.MeshVertices.Length > 0)
        {
            minimum = float.PositiveInfinity;
            foreach (Vector3 vertex in shape.MeshVertices)
                minimum = Mathf.Min(minimum, Vector3.Dot(localToWorld.MultiplyPoint3x4(vertex), normal));
            return true;
        }
        minimum = 0f;
        return false;
    }

    private static float MaximumAxisScale(Matrix4x4 matrix)
    {
        return Mathf.Max(matrix.MultiplyVector(Vector3.right).magnitude,
            matrix.MultiplyVector(Vector3.up).magnitude, matrix.MultiplyVector(Vector3.forward).magnitude);
    }

    private static bool HasShear(Matrix4x4 matrix)
    {
        Vector3 x = matrix.MultiplyVector(Vector3.right);
        Vector3 y = matrix.MultiplyVector(Vector3.up);
        Vector3 z = matrix.MultiplyVector(Vector3.forward);
        if (x.sqrMagnitude < 0.00000001f || y.sqrMagnitude < 0.00000001f || z.sqrMagnitude < 0.00000001f)
            return true;
        const float tolerance = 0.0001f;
        return Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > tolerance
            || Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) > tolerance
            || Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) > tolerance;
    }

    private static bool IsInHierarchy(Transform candidate, Transform root)
    {
        return candidate != null && root != null && (candidate == root || candidate.IsChildOf(root));
    }
}
