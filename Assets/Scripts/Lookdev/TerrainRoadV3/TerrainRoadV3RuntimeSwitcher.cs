using UnityEngine;
using UnityEngine.InputSystem;

namespace RageQuitting.Lookdev.TerrainRoadV3
{
    /// <summary>Scene-only lookdev switcher. Clones terrain data/layers while enabled and never edits source assets.</summary>
    [RequireComponent(typeof(Terrain), typeof(TerrainCollider))]
    public sealed class TerrainRoadV3RuntimeSwitcher : MonoBehaviour
    {
        public enum Variant { V3A, V3B, V4 }

        [SerializeField] private Terrain terrain;
        [SerializeField] private TerrainLayer[] sourceLayers = new TerrainLayer[3];
        [SerializeField] private Texture2D normalA;
        [SerializeField] private Texture2D normalB;
        [SerializeField] private Key keyA = Key.F6;
        [SerializeField] private Key keyB = Key.F7;
        [SerializeField] private TerrainLayer[] v4TemplateLayers = new TerrainLayer[3];
        [SerializeField] private Key keyV4 = Key.F8;

        private TerrainData originalData;
        private TerrainData originalColliderData;
        private TerrainCollider terrainCollider;
        private TerrainData runtimeData;
        private TerrainLayer[] runtimeLayers;
        private LayerMaps[] v3Maps;
        private bool v4Available;
        private bool v4WarningShown;
        private bool appliedA;
        private float messageUntil;
        private Variant currentVariant = Variant.V3B;

        private struct LayerMaps
        {
            public Texture2D diffuse;
            public Texture2D normal;
            public Texture2D mask;
            public Vector2 tileSize;
            public Vector2 tileOffset;
            public float normalScale;
            public float metallic;
            public float smoothness;
            public Color specular;
            public Color diffuseRemapMin;
            public Color diffuseRemapMax;
            public Vector4 maskMapRemapMin;
            public Vector4 maskMapRemapMax;

            public LayerMaps(TerrainLayer layer)
            {
                diffuse = layer.diffuseTexture;
                normal = layer.normalMapTexture;
                mask = layer.maskMapTexture;
                tileSize = layer.tileSize;
                tileOffset = layer.tileOffset;
                normalScale = layer.normalScale;
                metallic = layer.metallic;
                smoothness = layer.smoothness;
                specular = layer.specular;
                diffuseRemapMin = layer.diffuseRemapMin;
                diffuseRemapMax = layer.diffuseRemapMax;
                maskMapRemapMin = layer.maskMapRemapMin;
                maskMapRemapMax = layer.maskMapRemapMax;
            }
        }

        public bool IsVariantA => appliedA;
        public Variant CurrentVariant => currentVariant;
        public TerrainData RuntimeData => runtimeData;
        public TerrainLayer[] RuntimeLayers => runtimeLayers;

        private void OnEnable()
        {
            v4WarningShown = false;
            if (!CreateRuntimeCopies()) return;
            ApplyVariant(Variant.V3B);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || runtimeLayers == null) return;
            if (keyboard[keyA].wasPressedThisFrame) ApplyVariant(Variant.V3A);
            else if (keyboard[keyB].wasPressedThisFrame) ApplyVariant(Variant.V3B);
            else if (keyboard[keyV4].wasPressedThisFrame) ApplyVariant(Variant.V4);
        }

        private bool CreateRuntimeCopies()
        {
            if (!Application.isPlaying) return false;
            if (terrain == null) terrain = GetComponent<Terrain>();
            terrainCollider = terrain != null ? terrain.GetComponent<TerrainCollider>() : null;
            if (terrain == null || terrainCollider == null || terrain.terrainData == null ||
                sourceLayers == null || sourceLayers.Length != 3 || normalA == null || normalB == null)
            {
                Debug.LogError("TerrainRoadV3 requires a Terrain, matching TerrainCollider, three source layers, and both normal maps.", this);
                return false;
            }

            originalData = terrain.terrainData;
            originalColliderData = terrainCollider.terrainData;
            var sourceStack = originalData.terrainLayers;
            if (sourceStack == null || sourceStack.Length == 0)
            {
                Debug.LogError("TerrainRoadV3 requires a non-empty terrain layer stack.", this);
                originalData = null;
                originalColliderData = null;
                terrainCollider = null;
                return false;
            }
            var stack = (TerrainLayer[])sourceStack.Clone();
            v3Maps = new LayerMaps[sourceLayers.Length];
            for (var i = 0; i < sourceLayers.Length; i++)
            {
                if (sourceLayers[i] == null || System.Array.IndexOf(stack, sourceLayers[i]) < 0)
                {
                    Debug.LogError("Every source layer must be non-null and present in the terrain layer stack.", this);
                    originalData = null;
                    originalColliderData = null;
                    terrainCollider = null;
                    return false;
                }
                v3Maps[i] = new LayerMaps(sourceLayers[i]);
                for (var j = 0; j < i; j++)
                    if (sourceLayers[j] == sourceLayers[i])
                    {
                        Debug.LogError("Road V3 source layers must be unique.", this);
                        originalData = null;
                        originalColliderData = null;
                        terrainCollider = null;
                        return false;
                }
            }

            v4Available = HasValidV4Templates();

            runtimeData = Instantiate(originalData);
            runtimeData.name = originalData.name + " (Road V3 Runtime)";
            runtimeData.hideFlags = HideFlags.DontSave;
            runtimeData.terrainLayers = stack;
            runtimeLayers = new TerrainLayer[3];
            for (var i = 0; i < sourceLayers.Length; i++)
            {
                runtimeLayers[i] = Instantiate(sourceLayers[i]);
                runtimeLayers[i].name = sourceLayers[i].name + " (Runtime)";
                runtimeLayers[i].hideFlags = HideFlags.DontSave;
                var index = System.Array.IndexOf(stack, sourceLayers[i]);
                stack[index] = runtimeLayers[i];
            }
            runtimeData.terrainLayers = stack;
            terrain.terrainData = runtimeData;
            terrainCollider.terrainData = runtimeData;
            return true;
        }

        private bool HasValidV4Templates()
        {
            if (v4TemplateLayers == null || v4TemplateLayers.Length != 3) return false;
            for (var i = 0; i < v4TemplateLayers.Length; i++)
            {
                var layer = v4TemplateLayers[i];
                if (layer == null || layer.diffuseTexture == null || layer.normalMapTexture == null || layer.maskMapTexture == null)
                    return false;
                if (!Mathf.Approximately(layer.diffuseRemapMin.w, 0f) || !Mathf.Approximately(layer.diffuseRemapMax.w, 1f))
                    return false;
                for (var j = 0; j < i; j++) if (v4TemplateLayers[j] == layer) return false;
            }
            return true;
        }

        private void ApplyVariant(Variant variant)
        {
            if (runtimeLayers == null) return;
            if (variant == Variant.V4 && !v4Available)
            {
                if (!v4WarningShown)
                {
                    Debug.LogWarning("Road V4 is unavailable: assign three unique TerrainLayer templates with diffuse, normal, and mask textures, and diffuse remap alpha 0 to 1. V3 remains active.", this);
                    v4WarningShown = true;
                }
                return;
            }

            if (variant == Variant.V4)
            {
                for (var i = 0; i < runtimeLayers.Length; i++)
                {
                    runtimeLayers[i].diffuseTexture = v4TemplateLayers[i].diffuseTexture;
                    runtimeLayers[i].normalMapTexture = v4TemplateLayers[i].normalMapTexture;
                    runtimeLayers[i].maskMapTexture = v4TemplateLayers[i].maskMapTexture;
                    CopyLayerProperties(runtimeLayers[i], v4TemplateLayers[i]);
                }
            }
            else
            {
                // F6/F7 always restore the complete V3 texture set before selecting its normal.
                for (var i = 0; i < runtimeLayers.Length; i++)
                {
                    runtimeLayers[i].diffuseTexture = v3Maps[i].diffuse;
                    runtimeLayers[i].normalMapTexture = v3Maps[i].normal;
                    runtimeLayers[i].maskMapTexture = v3Maps[i].mask;
                    RestoreLayerProperties(runtimeLayers[i], v3Maps[i]);
                }
                var normal = variant == Variant.V3A ? normalA : normalB;
                for (var i = 0; i < runtimeLayers.Length; i++) runtimeLayers[i].normalMapTexture = normal;
            }

            currentVariant = variant;
            appliedA = variant == Variant.V3A;
            messageUntil = Time.unscaledTime + 1.5f;
        }

        private static void CopyLayerProperties(TerrainLayer target, TerrainLayer source)
        {
            target.tileSize = source.tileSize;
            target.tileOffset = source.tileOffset;
            target.normalScale = source.normalScale;
            target.metallic = source.metallic;
            target.smoothness = source.smoothness;
            target.specular = source.specular;
            target.diffuseRemapMin = source.diffuseRemapMin;
            target.diffuseRemapMax = source.diffuseRemapMax;
            target.maskMapRemapMin = source.maskMapRemapMin;
            target.maskMapRemapMax = source.maskMapRemapMax;
        }

        private static void RestoreLayerProperties(TerrainLayer target, LayerMaps maps)
        {
            target.tileSize = maps.tileSize;
            target.tileOffset = maps.tileOffset;
            target.normalScale = maps.normalScale;
            target.metallic = maps.metallic;
            target.smoothness = maps.smoothness;
            target.specular = maps.specular;
            target.diffuseRemapMin = maps.diffuseRemapMin;
            target.diffuseRemapMax = maps.diffuseRemapMax;
            target.maskMapRemapMin = maps.maskMapRemapMin;
            target.maskMapRemapMax = maps.maskMapRemapMax;
        }

        private void OnGUI()
        {
            if (runtimeLayers == null || Time.unscaledTime > messageUntil) return;
            var displayVariant = currentVariant == Variant.V4 ? "RoadBands V1.1 — Płynny" : $"Road {currentVariant}";
            GUI.Label(new Rect(18f, 18f, Mathf.Min(460f, Screen.width - 36f), 28f), $"{displayVariant}  |  F6 / F7 / F8");
        }

        private void OnDisable() => RestoreAndRelease();
        private void OnDestroy() => RestoreAndRelease();

        private void RestoreAndRelease()
        {
            if (terrain != null && runtimeData != null && terrain.terrainData == runtimeData) terrain.terrainData = originalData;
            if (terrainCollider != null && runtimeData != null && terrainCollider.terrainData == runtimeData) terrainCollider.terrainData = originalColliderData;
            ReleaseCopies();
        }

        private void ReleaseCopies()
        {
            if (runtimeLayers != null)
            {
                foreach (var layer in runtimeLayers) if (layer != null) DestroyOwned(layer);
            }
            if (runtimeData != null) DestroyOwned(runtimeData);
            runtimeLayers = null;
            runtimeData = null;
            originalData = null;
            originalColliderData = null;
            terrainCollider = null;
            v3Maps = null;
            v4Available = false;
            currentVariant = Variant.V3B;
            appliedA = false;
        }

        private static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
