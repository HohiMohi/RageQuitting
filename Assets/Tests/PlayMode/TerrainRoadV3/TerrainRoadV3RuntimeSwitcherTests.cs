using System.Collections;
using NUnit.Framework;
using RageQuitting.Lookdev.TerrainRoadV3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace RageQuitting.Tests.PlayMode.TerrainRoadV3
{
    public sealed class TerrainRoadV3RuntimeSwitcherTests : InputTestFixture
    {
        private GameObject root;
        private Terrain terrain;
        private TerrainData sourceData;
        private TerrainLayer[] sourceLayers;
        private Texture2D normalA;
        private Texture2D normalB;
        private Texture2D sourceColor;
        private Texture2D sourceMask;
        private Keyboard keyboard;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            sourceData = new TerrainData { heightmapResolution = 33, size = new Vector3(8, 2, 8) };
            var heights = new float[33,33];
            for (var y = 0; y < 33; y++) for (var x = 0; x < 33; x++) heights[y,x] = .04f * x / 32f + .02f * y / 32f;
            sourceData.SetHeights(0, 0, heights);
            sourceLayers = new TerrainLayer[4];
            for (var i = 0; i < sourceLayers.Length; i++) sourceLayers[i] = new TerrainLayer { name = "source-" + i };
            sourceColor = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            sourceColor.SetPixel(0,0,new Color(.4f,.3f,.2f,1f)); sourceColor.Apply();
            sourceMask = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            sourceMask.SetPixel(0,0,new Color(0f,.7f,.3f,0f)); sourceMask.Apply();
            for (var i = 1; i < sourceLayers.Length; i++)
            {
                sourceLayers[i].diffuseTexture = sourceColor;
                sourceLayers[i].maskMapTexture = sourceMask;
                sourceLayers[i].tileSize = new Vector2(4f,4f);
                sourceLayers[i].tileOffset = new Vector2(2f,-1f);
            }
            sourceData.terrainLayers = sourceLayers;
            normalA = MakeNormal("A");
            normalB = MakeNormal("B");
            root = Terrain.CreateTerrainGameObject(sourceData);
            root.SetActive(false);
            terrain = root.GetComponent<Terrain>();
        }

        [TearDown]
        public override void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (normalA != null) Object.DestroyImmediate(normalA);
            if (normalB != null) Object.DestroyImmediate(normalB);
            if (sourceColor != null) Object.DestroyImmediate(sourceColor);
            if (sourceMask != null) Object.DestroyImmediate(sourceMask);
            if (sourceData != null) Object.DestroyImmediate(sourceData);
            foreach (var layer in sourceLayers) if (layer != null) Object.DestroyImmediate(layer);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator F6AndF7ChangeOnlyRuntimeNormalReferences()
        {
            var originalData = terrain.terrainData;
            var originalStack = (TerrainLayer[])sourceData.terrainLayers.Clone();
            sourceLayers[1].normalMapTexture = normalB;
            sourceLayers[2].normalMapTexture = normalB;
            sourceLayers[3].normalMapTexture = normalB;
            var originalAlbedo = sourceLayers[1].diffuseTexture;
            var originalMask = sourceLayers[1].maskMapTexture;
            var originalSize = sourceLayers[1].tileSize;
            var sourceNormalSnapshot = new[] { sourceLayers[1].normalMapTexture, sourceLayers[2].normalMapTexture, sourceLayers[3].normalMapTexture };
            var sourceHeights = sourceData.GetHeights(0, 0, sourceData.heightmapResolution, sourceData.heightmapResolution);
            var switcher = root.AddComponent<TerrainRoadV3RuntimeSwitcher>();
            SetField(switcher, "terrain", terrain);
            SetField(switcher, "sourceLayers", new[] { sourceLayers[1], sourceLayers[2], sourceLayers[3] });
            SetField(switcher, "normalA", normalA);
            SetField(switcher, "normalB", normalB);
            root.SetActive(true);
            yield return null;

            var runtimeData = terrain.terrainData;
            var runtimeColliderData = terrain.GetComponent<TerrainCollider>().terrainData;
            var runtimeLayers = new[] { runtimeData.terrainLayers[1], runtimeData.terrainLayers[2], runtimeData.terrainLayers[3] };
            Assert.AreNotSame(originalData, runtimeData);
            Assert.AreSame(runtimeData, runtimeColliderData);
            Assert.AreSame(sourceLayers[0], runtimeData.terrainLayers[0]);
            Assert.AreSame(originalStack[0], sourceData.terrainLayers[0]);
            Assert.AreSame(normalB, runtimeData.terrainLayers[1].normalMapTexture);
            Assert.AreSame(originalAlbedo, runtimeData.terrainLayers[1].diffuseTexture);
            Assert.AreSame(originalMask, runtimeData.terrainLayers[1].maskMapTexture);
            Assert.AreEqual(originalSize, runtimeData.terrainLayers[1].tileSize);
            Assert.That(HeightsEqual(sourceHeights, runtimeData.GetHeights(0,0,runtimeData.heightmapResolution,runtimeData.heightmapResolution)), Is.True);

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F6));
            yield return null;
            Assert.AreSame(normalA, runtimeData.terrainLayers[1].normalMapTexture);
            Assert.AreSame(normalA, runtimeData.terrainLayers[2].normalMapTexture);
            Assert.AreSame(normalA, runtimeData.terrainLayers[3].normalMapTexture);
            Assert.AreSame(originalStack[0], sourceData.terrainLayers[0]);
            Assert.AreSame(originalStack[0].normalMapTexture, runtimeData.terrainLayers[0].normalMapTexture);
            Assert.AreSame(originalAlbedo, runtimeData.terrainLayers[1].diffuseTexture);
            Assert.AreSame(originalMask, runtimeData.terrainLayers[1].maskMapTexture);
            Assert.That(HeightsEqual(sourceHeights, sourceData.GetHeights(0,0,sourceData.heightmapResolution,sourceData.heightmapResolution)), Is.True);
            for (var i = 0; i < 3; i++) Assert.AreSame(sourceNormalSnapshot[i], sourceLayers[i + 1].normalMapTexture);

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F7));
            yield return null;
            Assert.AreSame(originalStack[0].normalMapTexture, runtimeData.terrainLayers[0].normalMapTexture);
            for (var i = 1; i < 4; i++) Assert.AreSame(normalB, runtimeData.terrainLayers[i].normalMapTexture);

            switcher.enabled = false;
            yield return null;
            Assert.AreSame(originalData, terrain.terrainData);
            Assert.AreSame(originalData, terrain.GetComponent<TerrainCollider>().terrainData);
            Assert.IsTrue(runtimeData == null);
            foreach (var runtimeLayer in runtimeLayers) Assert.IsTrue(runtimeLayer == null);
            Assert.IsTrue(sourceLayers[1] != null);
            for (var i = 0; i < 3; i++) Assert.AreSame(sourceNormalSnapshot[i], sourceLayers[i + 1].normalMapTexture);
        }

        [UnityTest]
        public IEnumerator ReenableCreatesFreshCopiesAndDestroyRestoresOwnedReferences()
        {
            var originalData = terrain.terrainData;
            var switcher = root.AddComponent<TerrainRoadV3RuntimeSwitcher>();
            SetField(switcher, "terrain", terrain);
            SetField(switcher, "sourceLayers", new[] { sourceLayers[1], sourceLayers[2], sourceLayers[3] });
            SetField(switcher, "normalA", normalA);
            SetField(switcher, "normalB", normalB);
            root.SetActive(true);
            yield return null;
            var firstRuntime = terrain.terrainData;
            var firstRuntimeLayer = firstRuntime.terrainLayers[1];
            switcher.enabled = false;
            yield return null;
            Assert.AreSame(originalData, terrain.terrainData);
            switcher.enabled = true;
            yield return null;
            var secondRuntime = terrain.terrainData;
            var secondRuntimeLayer = secondRuntime.terrainLayers[1];
            Assert.AreNotSame(firstRuntime, secondRuntime);
            Assert.IsTrue(firstRuntime == null);
            Assert.IsTrue(firstRuntimeLayer == null);
            Assert.AreSame(sourceLayers[0].normalMapTexture, secondRuntime.terrainLayers[0].normalMapTexture);
            Assert.AreSame(normalB, secondRuntime.terrainLayers[1].normalMapTexture);
            Object.Destroy(root);
            root = null;
            yield return null;
            Assert.IsTrue(secondRuntime == null);
            Assert.IsTrue(secondRuntimeLayer == null);
            Assert.IsTrue(sourceData != null);
            Assert.AreSame(sourceLayers[0], sourceData.terrainLayers[0]);
        }

        private static Texture2D MakeNormal(string label)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { name = label };
            texture.SetPixel(0, 0, new Color(.5f, .5f, 1f, 1f));
            texture.Apply();
            return texture;
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }

        private static bool HeightsEqual(float[,] a, float[,] b)
        {
            if (a.GetLength(0) != b.GetLength(0) || a.GetLength(1) != b.GetLength(1)) return false;
            for (var y = 0; y < a.GetLength(0); y++)
                for (var x = 0; x < a.GetLength(1); x++)
                    if (Mathf.Abs(a[y, x] - b[y, x]) > 1e-6f) return false;
            return true;
        }
    }
}
