using UnityEngine;
using UnityEngine.InputSystem;

namespace RageQuitting.Lookdev.TerrainRoadV3
{
    /// <summary>Scene-only lookdev switcher. Clones terrain data/layers while enabled and never edits source assets.</summary>
    [RequireComponent(typeof(Terrain), typeof(TerrainCollider))]
    public sealed class TerrainRoadV3RuntimeSwitcher : MonoBehaviour
    {
        [SerializeField] private Terrain terrain;
        [SerializeField] private TerrainLayer[] sourceLayers = new TerrainLayer[3];
        [SerializeField] private Texture2D normalA;
        [SerializeField] private Texture2D normalB;
        [SerializeField] private Key keyA = Key.F6;
        [SerializeField] private Key keyB = Key.F7;

        private TerrainData originalData;
        private TerrainData originalColliderData;
        private TerrainCollider terrainCollider;
        private TerrainData runtimeData;
        private TerrainLayer[] runtimeLayers;
        private bool appliedA;
        private float messageUntil;

        public bool IsVariantA => appliedA;
        public TerrainData RuntimeData => runtimeData;
        public TerrainLayer[] RuntimeLayers => runtimeLayers;

        private void OnEnable()
        {
            if (!CreateRuntimeCopies()) return;
            ApplyVariant(false);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || runtimeLayers == null) return;
            if (keyboard[keyA].wasPressedThisFrame) ApplyVariant(true);
            else if (keyboard[keyB].wasPressedThisFrame) ApplyVariant(false);
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
            var stack = (TerrainLayer[])originalData.terrainLayers.Clone();
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

        private void ApplyVariant(bool variantA)
        {
            if (runtimeLayers == null) return;
            var normal = variantA ? normalA : normalB;
            for (var i = 0; i < runtimeLayers.Length; i++) runtimeLayers[i].normalMapTexture = normal;
            appliedA = variantA;
            messageUntil = Time.unscaledTime + 1.5f;
        }

        private void OnGUI()
        {
            if (runtimeLayers == null || Time.unscaledTime > messageUntil) return;
            GUI.Label(new Rect(18f, 18f, 260f, 28f), $"Road normal variant {(appliedA ? "A" : "B")}  |  F6 / F7");
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
        }

        private static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
