using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Runtime mesh view for a loose-soil height field. The mesh has no collider.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class FoundationLooseSoilField : MonoBehaviour
{
    private static readonly List<FoundationLooseSoilField> ActiveFields = new List<FoundationLooseSoilField>(4);
    private static readonly List<Vector3> MeshVertices = new List<Vector3>(8192);
    private static readonly List<int> MeshTriangles = new List<int>(12288);
    private static readonly List<Vector2> MeshUvs = new List<Vector2>(8192);
    [SerializeField] private float footprintWidth = 6.5f;
    [SerializeField] private float footprintLength = 4f;
    [SerializeField] private float cellSize = 0.2f;
    [SerializeField] private float layerDepth = 0.4f;
    [SerializeField] private float initialHeight = 0.4f;
    [SerializeField] private float unitsPerLayer = 6f;
    [SerializeField, Min(0.05f)] private float returnRadius = 0.6f;
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private MeshRenderer meshRenderer;

    private LooseSoilHeightField heightField;
    private Mesh runtimeMesh;
    private bool surfaceActive;

    public LooseSoilHeightField HeightField => heightField;
    public Mesh RuntimeMesh => runtimeMesh;
    public bool HasHeightField => heightField != null;
    public bool IsSurfaceActive => surfaceActive && isActiveAndEnabled && gameObject.activeInHierarchy &&
                                  meshRenderer != null && meshRenderer.enabled;
    public float ConfiguredCellSize => cellSize;
    public float ReturnRadius => Mathf.Max(0.05f, returnRadius);

    /// <summary>Resolves the owning site through the volume's serialized association.</summary>
    public bool TryGetConstructionSite(out BridgeConstructionSite site)
    {
        site = null;
        FoundationExcavationVolume volume = GetComponentInParent<FoundationExcavationVolume>();
        if (volume == null || !volume.UsesLooseSoilHeightField || !volume.HasValidLooseSoilConfiguration ||
            volume.LooseSoilField != this) return false;
        BridgeConstructionSite candidate = volume.ConstructionSite;
        if (candidate == null || candidate.ExcavationVolume != volume) return false;
        site = candidate;
        return true;
    }

    private void OnEnable()
    {
        if (!ActiveFields.Contains(this)) ActiveFields.Add(this);
    }

    private void OnDisable()
    {
        ActiveFields.Remove(this);
    }

    /// <summary>Returns the maximum normalized immersion among currently active loose fields.</summary>
    public static float GetMaximumImmersion(Vector3 worldFootPosition)
    {
        float maximum = 0f;
        for (int i = ActiveFields.Count - 1; i >= 0; i--)
        {
            FoundationLooseSoilField field = ActiveFields[i];
            if (field == null)
            {
                ActiveFields.RemoveAt(i);
                continue;
            }

            if (field.TryGetImmersion(worldFootPosition, out float immersion) && immersion > maximum)
                maximum = immersion;
        }
        return maximum;
    }

    /// <summary>Finds an active field whose footprint contains the brush center, including a previously carved cell.</summary>
    public static bool TryFindCollectionField(Vector3 worldPosition, out FoundationLooseSoilField result)
    {
        result = null;
        for (int i = ActiveFields.Count - 1; i >= 0; i--)
        {
            FoundationLooseSoilField field = ActiveFields[i];
            if (field == null) { ActiveFields.RemoveAt(i); continue; }
            if (!field.IsSurfaceActive || field.heightField == null) continue;
            Vector3 local = field.transform.InverseTransformPoint(worldPosition);
            if (!IsFinite(local.x) || !IsFinite(local.y) || !IsFinite(local.z) ||
                local.y < -0.05f || local.y > field.heightField.MaximumHeight + 0.05f ||
                local.x < -field.footprintWidth * 0.5f || local.x > field.footprintWidth * 0.5f ||
                local.z < -field.footprintLength * 0.5f || local.z > field.footprintLength * 0.5f)
                continue;
            result = field;
            return true;
        }
        return false;
    }

    /// <summary>Finds the nearest registered pit plane crossed by an aim ray, whether or not soil is visible.</summary>
    public static bool TryFindAimedPit(Ray ray, float maximumDistance, out FoundationLooseSoilField result,
        out BridgeConstructionSite site, out Vector3 hitPoint, out float hitDistance)
    {
        result = null;
        site = null;
        hitPoint = default;
        hitDistance = 0f;
        if (!IsFinite(ray.origin.x) || !IsFinite(ray.origin.y) || !IsFinite(ray.origin.z) ||
            !IsFinite(ray.direction.x) || !IsFinite(ray.direction.y) || !IsFinite(ray.direction.z) ||
            !IsFinite(maximumDistance) || maximumDistance <= 0f || ray.direction.sqrMagnitude < 0.99f ||
            ray.direction.sqrMagnitude > 1.01f) return false;

        float nearestDistance = float.PositiveInfinity;
        for (int i = ActiveFields.Count - 1; i >= 0; i--)
        {
            FoundationLooseSoilField field = ActiveFields[i];
            if (field == null)
            {
                ActiveFields.RemoveAt(i);
                continue;
            }
            if (!field.TryGetConstructionSite(out BridgeConstructionSite candidateSite)) continue;

            LooseSoilHeightField model = field.heightField;
            if (model == null) continue;
            Vector3 localOrigin = field.transform.InverseTransformPoint(ray.origin);
            Vector3 localDirection = field.transform.InverseTransformVector(ray.direction);
            if (!IsFinite(localOrigin.x) || !IsFinite(localOrigin.y) || !IsFinite(localOrigin.z) ||
                !IsFinite(localDirection.x) || !IsFinite(localDirection.y) || !IsFinite(localDirection.z) ||
                Mathf.Abs(localDirection.y) < 0.0001f) continue;

            // Empty and hidden pits remain targetable through the hard-support plane.
            ConsiderAimedSurface(field, candidateSite, ray, localOrigin, localDirection, 0f,
                -field.footprintWidth * 0.5f, field.footprintWidth * 0.5f,
                -field.footprintLength * 0.5f, field.footprintLength * 0.5f,
                maximumDistance, ref nearestDistance, ref result, ref site, ref hitPoint, ref hitDistance);

            // Visible loose soil may stand well above the floor. Test each flat cell top
            // so a shallow ray can still identify it within interaction range.
            for (int row = 0; row < model.Rows; row++)
            for (int column = 0; column < model.Columns; column++)
            {
                float top = model.GetHeight(column, row);
                if (top <= 0f) continue;
                float x0 = -model.Width * 0.5f + column * model.CellSize;
                float z0 = -model.Length * 0.5f + row * model.CellSize;
                float x1 = x0 + model.CellWidth(column);
                float z1 = z0 + model.CellLength(row);
                ConsiderAimedSurface(field, candidateSite, ray, localOrigin, localDirection, top,
                    x0, x1, z0, z1, maximumDistance,
                    ref nearestDistance, ref result, ref site, ref hitPoint, ref hitDistance);
            }
        }
        return result != null;
    }

    private static void ConsiderAimedSurface(FoundationLooseSoilField field, BridgeConstructionSite candidateSite,
        Ray ray, Vector3 localOrigin, Vector3 localDirection, float localHeight,
        float minX, float maxX, float minZ, float maxZ, float maximumDistance,
        ref float nearestDistance, ref FoundationLooseSoilField result, ref BridgeConstructionSite site,
        ref Vector3 hitPoint, ref float hitDistance)
    {
        float distance = (localHeight - localOrigin.y) / localDirection.y;
        if (!IsFinite(distance) || distance < 0f || distance > maximumDistance || distance >= nearestDistance) return;
        Vector3 local = localOrigin + localDirection * distance;
        if (local.x < minX || local.x > maxX || local.z < minZ || local.z > maxZ) return;
        nearestDistance = distance;
        result = field;
        site = candidateSite;
        hitPoint = ray.GetPoint(distance);
        hitDistance = distance;
    }

    /// <summary>Checks the pit footprint in local X/Z; height does not affect a gravity-bound dump column.</summary>
    public bool ContainsHorizontalFootprint(Vector3 worldPosition)
    {
        if (!IsFinite(worldPosition.x) || !IsFinite(worldPosition.y) || !IsFinite(worldPosition.z)) return false;
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        return IsFinite(local.x) && IsFinite(local.z) &&
               local.x >= -footprintWidth * 0.5f && local.x <= footprintWidth * 0.5f &&
               local.z >= -footprintLength * 0.5f && local.z <= footprintLength * 0.5f;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private bool TryGetImmersion(Vector3 worldFootPosition, out float immersion)
    {
        immersion = 0f;
        if (!IsSurfaceActive || heightField == null) return false;
        Vector3 local = transform.InverseTransformPoint(worldFootPosition);
        if (!heightField.TryGetHeight(local.x, local.z, out float surfaceY) || surfaceY <= 0f)
            return false;
        if (float.IsNaN(local.y) || float.IsInfinity(local.y) || local.y < -0.0001f)
            return false;
        float depth = surfaceY - Mathf.Max(0f, local.y);
        if (float.IsNaN(depth) || float.IsInfinity(depth) || depth <= 0f)
            return false;
        immersion = Mathf.Clamp01(depth / layerDepth);
        return immersion > 0f;
    }

    private void Awake()
    {
        if (heightField == null) Initialize(footprintWidth, footprintLength, cellSize, layerDepth,
            initialHeight, unitsPerLayer);
    }

    public void Initialize(float width, float length, float gridCellSize, float supportDepth,
        float surfaceHeight, float unitsForLayer)
    {
        var nextField = new LooseSoilHeightField(width, length, gridCellSize, supportDepth,
            surfaceHeight, unitsForLayer);
        footprintWidth = width; footprintLength = length; cellSize = gridCellSize;
        layerDepth = supportDepth; initialHeight = surfaceHeight; unitsPerLayer = unitsForLayer;
        heightField = nextField;
        EnsureMeshComponents();
        RebuildMesh();
    }

    public bool EnsureConfiguration(float width, float length, float gridCellSize, float supportDepth,
        float surfaceHeight, float unitsForLayer)
    {
        if (heightField != null && heightField.Width == width && heightField.Length == length &&
            heightField.CellSize == gridCellSize && layerDepth == supportDepth &&
            heightField.MaximumHeight >= supportDepth &&
            initialHeight == surfaceHeight && unitsPerLayer == unitsForLayer)
        {
            return false;
        }

        Initialize(width, length, gridCellSize, supportDepth, surfaceHeight, unitsForLayer);
        return true;
    }

    /// <summary>Explicitly rebuild after a model change. No per-frame mesh work is performed.</summary>
    public void RebuildMesh()
    {
        if (heightField == null) return;
        EnsureMeshComponents();
        if (runtimeMesh == null)
        {
            runtimeMesh = new Mesh
            {
                name = name + " (Loose Soil Runtime)",
                hideFlags = HideFlags.DontSave
            };
            runtimeMesh.MarkDynamic();
        }

        List<Vector3> vertices = MeshVertices;
        List<int> triangles = MeshTriangles;
        List<Vector2> uvs = MeshUvs;
        vertices.Clear(); triangles.Clear(); uvs.Clear();
        for (int row = 0; row < heightField.Rows; row++)
        for (int column = 0; column < heightField.Columns; column++)
        {
            float x0 = -footprintWidth * 0.5f + column * cellSize;
            float z0 = -footprintLength * 0.5f + row * cellSize;
            float x1 = x0 + heightField.CellWidth(column);
            float z1 = z0 + heightField.CellLength(row);
            float top = heightField.GetHeight(column, row);
            if (top <= 0f) continue;
            AddQuad(vertices, triangles, uvs, new Vector3(x0, top, z0), new Vector3(x0, top, z1),
                new Vector3(x1, top, z1), new Vector3(x1, top, z0));
            AddSideIfNeeded(vertices, triangles, uvs, x0, z0, x1, z0, top,
                row > 0 ? heightField.GetHeight(column, row - 1) : 0f);
            AddSideIfNeeded(vertices, triangles, uvs, x1, z0, x1, z1, top,
                column + 1 < heightField.Columns ? heightField.GetHeight(column + 1, row) : 0f);
            AddSideIfNeeded(vertices, triangles, uvs, x1, z1, x0, z1, top,
                row + 1 < heightField.Rows ? heightField.GetHeight(column, row + 1) : 0f);
            AddSideIfNeeded(vertices, triangles, uvs, x0, z1, x0, z0, top,
                column > 0 ? heightField.GetHeight(column - 1, row) : 0f);
        }

        runtimeMesh.Clear();
        runtimeMesh.indexFormat = vertices.Count > ushort.MaxValue
            ? IndexFormat.UInt32
            : IndexFormat.UInt16;
        runtimeMesh.SetVertices(vertices);
        runtimeMesh.SetTriangles(triangles, 0);
        runtimeMesh.SetUVs(0, uvs);
        runtimeMesh.RecalculateNormals();
        runtimeMesh.RecalculateBounds();
        meshFilter.sharedMesh = runtimeMesh;
    }

    private void EnsureMeshComponents()
    {
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
    }

    public LooseSoilHeightField.Snapshot CaptureSnapshot() => heightField?.CaptureSnapshot();

    public bool CanRestoreSnapshot(LooseSoilHeightField.Snapshot snapshot) =>
        heightField != null && heightField.CanRestoreSnapshot(snapshot);

    public bool RestoreSnapshot(LooseSoilHeightField.Snapshot snapshot)
    {
        if (!CanRestoreSnapshot(snapshot)) return false;
        bool changed = !heightField.MatchesSnapshot(snapshot);
        if (!heightField.RestoreSnapshot(snapshot)) return false;
        if (changed) RebuildMesh();
        return true;
    }

    public void ResetToInitial()
    {
        Initialize(footprintWidth, footprintLength, cellSize, layerDepth, initialHeight, unitsPerLayer);
    }

    public void Clear()
    {
        if (heightField == null) return;
        heightField.Clear();
        RebuildMesh();
    }

    public void ReleaseRuntimeMeshForSceneSave()
    {
        SetSurfaceState(false, null);
        ReleaseRuntimeMesh();
    }

    public void SetSurfaceState(bool visible, Material sharedMaterial)
    {
        EnsureMeshComponents();
        if (sharedMaterial != null && meshRenderer.sharedMaterial != sharedMaterial)
            meshRenderer.sharedMaterial = sharedMaterial;
        if (meshRenderer.enabled != visible) meshRenderer.enabled = visible;
        surfaceActive = visible;
    }

    public bool TryGetWorldSurfaceHeight(Vector3 worldPosition, out float worldHeight)
    {
        worldHeight = 0f;
        if (heightField == null || !IsSurfaceActive) return false;
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        if (!heightField.TryGetHeight(localPosition.x, localPosition.z, out float localSurfaceY)) return false;
        worldHeight = transform.TransformPoint(new Vector3(localPosition.x, localSurfaceY, localPosition.z)).y;
        return !float.IsNaN(worldHeight) && !float.IsInfinity(worldHeight);
    }

    private void AddSideIfNeeded(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs,
        float x0, float z0, float x1, float z1, float height, float neighborHeight)
    {
        float bottom = Mathf.Min(height, neighborHeight);
        if (height <= bottom) return;
        AddQuad(vertices, triangles, uvs, new Vector3(x0, bottom, z0), new Vector3(x0, height, z0),
            new Vector3(x1, height, z1), new Vector3(x1, bottom, z1));
    }

    private void AddQuad(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int first = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        AddUv(uvs, a); AddUv(uvs, b); AddUv(uvs, c); AddUv(uvs, d);
        triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
        triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
    }

    private void AddUv(List<Vector2> uvs, Vector3 vertex) =>
        uvs.Add(new Vector2(vertex.x + footprintWidth * 0.5f, vertex.z + footprintLength * 0.5f));

    private void OnDestroy()
    {
        ActiveFields.Remove(this);
        ReleaseRuntimeMesh();
    }

    private void ReleaseRuntimeMesh()
    {
        if (runtimeMesh == null) return;
        if (meshFilter != null && meshFilter.sharedMesh == runtimeMesh) meshFilter.sharedMesh = null;
        if (Application.isPlaying) Destroy(runtimeMesh);
        else DestroyImmediate(runtimeMesh);
        runtimeMesh = null;
    }
}
