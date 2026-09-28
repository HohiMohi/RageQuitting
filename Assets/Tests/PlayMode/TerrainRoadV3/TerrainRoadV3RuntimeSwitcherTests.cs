using System.Collections;
using NUnit.Framework;
using RageQuitting.Lookdev.TerrainRoadV3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

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
        private TerrainLayer[] v4Layers;
        private Texture2D[] v4Diffuse;
        private Texture2D v4Normal;
        private Texture2D v4Mask;
        private TerrainLayer partialLayer;
        private TerrainLayer[] deepBumpLayers;
        private Texture2D deepBumpDiffuse;
        private Texture2D deepBumpNormal;
        private Texture2D deepBumpMask;
        private Texture2D sourceGrassTexture;
        private TerrainLayer invalidDeepBumpLayer;
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
            sourceData.SetDetailResolution(32, 8);
            sourceGrassTexture = MakeColor("Source Grass Detail", new Color(.2f, .6f, .1f, 1f));
            sourceData.detailPrototypes = new[]
            {
                new DetailPrototype { prototypeTexture = sourceGrassTexture, renderMode = DetailRenderMode.GrassBillboard }
            };
            var grassDetails = new int[32, 32];
            for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++) grassDetails[y, x] = (x + y) % 4;
            sourceData.SetDetailLayer(0, 0, 0, grassDetails);
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
            v4Layers = new TerrainLayer[3];
            v4Diffuse = new Texture2D[3];
            v4Normal = MakeNormal("V4 Normal");
            v4Mask = MakeColor("V4 Mask", new Color(.1f, .8f, .4f, .2f));
            for (var i = 0; i < 3; i++)
            {
                v4Diffuse[i] = MakeColor("V4 Diffuse " + i, new Color(.2f + i * .1f, .5f, .3f, 1f));
                v4Layers[i] = new TerrainLayer
                {
                    name = "V4 template " + i,
                    diffuseTexture = v4Diffuse[i],
                    normalMapTexture = v4Normal,
                    maskMapTexture = v4Mask,
                    tileSize = new Vector2(6f + i, 7f + i),
                    tileOffset = new Vector2(.25f, .5f),
                    normalScale = .7f,
                    metallic = .12f,
                    smoothness = .23f,
                    specular = new Color(.1f, .2f, .3f, 1f),
                    diffuseRemapMin = new Color(.1f, .2f, .3f, 0f),
                    diffuseRemapMax = new Color(.6f, .7f, .8f, 1f),
                    maskMapRemapMin = new Vector4(.1f, .2f, .3f, .4f),
                    maskMapRemapMax = new Vector4(.6f, .7f, .8f, .9f)
                };
            }
            deepBumpDiffuse = MakeColor("DeepBump Albedo V1", new Color(.72f, .41f, .24f, 1f));
            deepBumpNormal = MakeNormal("DeepBump Normal V2");
            deepBumpMask = MakeColor("DeepBump Constant Mask", new Color(.2f, .3f, .4f, .5f));
            deepBumpLayers = new TerrainLayer[3];
            for (var i = 0; i < deepBumpLayers.Length; i++)
            {
                deepBumpLayers[i] = new TerrainLayer
                {
                    name = "DeepBump template " + i,
                    diffuseTexture = deepBumpDiffuse,
                    normalMapTexture = deepBumpNormal,
                    maskMapTexture = deepBumpMask,
                    tileSize = new Vector2(4f, 4f),
                    tileOffset = Vector2.zero,
                    normalScale = 1f,
                    metallic = .05f + i * .03f,
                    smoothness = .18f + i * .07f,
                    specular = new Color(.15f + i * .1f, .2f, .25f, 1f),
                    diffuseRemapMin = new Color(.03f, .04f, .05f, 0f),
                    diffuseRemapMax = new Color(.9f, .8f, .7f, 1f),
                    maskMapRemapMin = new Vector4(.01f, .02f, .03f, .04f),
                    maskMapRemapMax = new Vector4(.91f, .82f, .73f, .64f)
                };
            }
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
            if (v4Normal != null) Object.DestroyImmediate(v4Normal);
            if (v4Mask != null) Object.DestroyImmediate(v4Mask);
            if (v4Diffuse != null) foreach (var texture in v4Diffuse) if (texture != null) Object.DestroyImmediate(texture);
            if (v4Layers != null) foreach (var layer in v4Layers) if (layer != null) Object.DestroyImmediate(layer);
            if (partialLayer != null) Object.DestroyImmediate(partialLayer);
            if (deepBumpLayers != null) foreach (var layer in deepBumpLayers) if (layer != null) Object.DestroyImmediate(layer);
            if (invalidDeepBumpLayer != null) Object.DestroyImmediate(invalidDeepBumpLayer);
            if (deepBumpDiffuse != null) Object.DestroyImmediate(deepBumpDiffuse);
            if (deepBumpNormal != null) Object.DestroyImmediate(deepBumpNormal);
            if (deepBumpMask != null) Object.DestroyImmediate(deepBumpMask);
            if (sourceGrassTexture != null) Object.DestroyImmediate(sourceGrassTexture);
            if (sourceData != null) Object.DestroyImmediate(sourceData);
            foreach (var layer in sourceLayers) if (layer != null) Object.DestroyImmediate(layer);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator F8AndV3KeysRoundTripAllMapsAndLayerPropertiesWithoutAllocatingCopies()
        {
            var originalData = terrain.terrainData;
            var sourceHeights = sourceData.GetHeights(0, 0, sourceData.heightmapResolution, sourceData.heightmapResolution);
            var sourceNormalRefs = new[] { sourceLayers[1].normalMapTexture, sourceLayers[2].normalMapTexture, sourceLayers[3].normalMapTexture };
            var sourceDiffuseRefs = new[] { sourceLayers[1].diffuseTexture, sourceLayers[2].diffuseTexture, sourceLayers[3].diffuseTexture };
            var sourceMaskRefs = new[] { sourceLayers[1].maskMapTexture, sourceLayers[2].maskMapTexture, sourceLayers[3].maskMapTexture };
            var sourceAlphas = sourceData.GetAlphamaps(0, 0, sourceData.alphamapWidth, sourceData.alphamapHeight);
            var sourceGrass = sourceData.GetDetailLayer(0, 0, sourceData.detailWidth, sourceData.detailHeight, 0);
            var v4DiffuseRemaps = new[] { v4Layers[0].diffuseRemapMin, v4Layers[1].diffuseRemapMin, v4Layers[2].diffuseRemapMin };
            var switcher = MakeSwitcher(true);
            yield return null;
            var runtime = terrain.terrainData;
            var layers = new[] { runtime.terrainLayers[1], runtime.terrainLayers[2], runtime.terrainLayers[3] };

            yield return Press(Key.F8);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V4, switcher.CurrentVariant);
            for (var i = 0; i < 3; i++)
            {
                Assert.AreSame(v4Diffuse[i], layers[i].diffuseTexture);
                Assert.AreSame(v4Normal, layers[i].normalMapTexture);
                Assert.AreSame(v4Mask, layers[i].maskMapTexture);
                Assert.AreEqual(v4Layers[i].tileSize, layers[i].tileSize);
                Assert.AreEqual(v4Layers[i].tileOffset, layers[i].tileOffset);
                Assert.AreEqual(v4Layers[i].normalScale, layers[i].normalScale);
                Assert.AreEqual(v4Layers[i].metallic, layers[i].metallic);
                Assert.AreEqual(v4Layers[i].smoothness, layers[i].smoothness);
                Assert.AreEqual(v4Layers[i].diffuseRemapMin, layers[i].diffuseRemapMin);
                Assert.AreEqual(v4Layers[i].diffuseRemapMax, layers[i].diffuseRemapMax);
                Assert.AreEqual(v4Layers[i].maskMapRemapMin, layers[i].maskMapRemapMin);
                Assert.AreEqual(v4Layers[i].maskMapRemapMax, layers[i].maskMapRemapMax);
            }
            yield return Press(Key.F8);
            yield return null;
            Assert.AreSame(runtime, terrain.terrainData);
            for (var i = 0; i < 3; i++) Assert.AreSame(layers[i], runtime.terrainLayers[i + 1]);

            yield return Press(Key.F6);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3A, switcher.CurrentVariant);
            for (var i = 0; i < 3; i++)
            {
                Assert.AreSame(sourceDiffuseRefs[i], layers[i].diffuseTexture);
                Assert.AreSame(normalA, layers[i].normalMapTexture);
                Assert.AreSame(sourceMaskRefs[i], layers[i].maskMapTexture);
                Assert.AreEqual(sourceLayers[i + 1].tileSize, layers[i].tileSize);
                Assert.AreEqual(sourceLayers[i + 1].tileOffset, layers[i].tileOffset);
                Assert.AreEqual(sourceLayers[i + 1].normalScale, layers[i].normalScale);
                Assert.AreEqual(sourceLayers[i + 1].metallic, layers[i].metallic);
                Assert.AreEqual(sourceLayers[i + 1].smoothness, layers[i].smoothness);
            }
            yield return Press(Key.F7);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3B, switcher.CurrentVariant);
            Assert.IsFalse(switcher.IsVariantA);
            for (var i = 0; i < 3; i++)
            {
                Assert.AreSame(sourceDiffuseRefs[i], layers[i].diffuseTexture);
                Assert.AreSame(normalB, layers[i].normalMapTexture);
                Assert.AreSame(sourceMaskRefs[i], layers[i].maskMapTexture);
                Assert.AreSame(sourceNormalRefs[i], sourceLayers[i + 1].normalMapTexture);
            }
            Assert.AreSame(originalData, sourceData);
            Assert.That(HeightsEqual(sourceHeights, sourceData.GetHeights(0,0,sourceData.heightmapResolution,sourceData.heightmapResolution)), Is.True);
            Assert.AreSame(runtime, terrain.GetComponent<TerrainCollider>().terrainData);
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3B, switcher.CurrentVariant);
            for (var i = 0; i < 3; i++)
            {
                Assert.AreSame(v4Diffuse[i], v4Layers[i].diffuseTexture);
                Assert.AreSame(v4Normal, v4Layers[i].normalMapTexture);
                Assert.AreSame(v4Mask, v4Layers[i].maskMapTexture);
                Assert.AreEqual(v4DiffuseRemaps[i], v4Layers[i].diffuseRemapMin);
            }
            Assert.That(AlphamapsEqual(sourceAlphas, sourceData.GetAlphamaps(0, 0, sourceData.alphamapWidth, sourceData.alphamapHeight)), Is.True);
            Assert.That(DetailLayersEqual(sourceGrass, sourceData.GetDetailLayer(0, 0, sourceData.detailWidth, sourceData.detailHeight, 0)), Is.True);

            yield return Press(Key.F8);
            var oldRuntime = terrain.terrainData;
            var oldRuntimeLayers = new[] { oldRuntime.terrainLayers[1], oldRuntime.terrainLayers[2], oldRuntime.terrainLayers[3] };
            switcher.enabled = false;
            yield return null;
            Assert.AreSame(originalData, terrain.terrainData);
            Assert.AreSame(originalData, terrain.GetComponent<TerrainCollider>().terrainData);
            Assert.IsTrue(oldRuntime == null);
            foreach (var oldLayer in oldRuntimeLayers) Assert.IsTrue(oldLayer == null);
            switcher.enabled = true;
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3B, switcher.CurrentVariant);
            Assert.AreNotSame(oldRuntime, terrain.terrainData);
            Assert.AreSame(normalB, terrain.terrainData.terrainLayers[1].normalMapTexture);
            Assert.That(AlphamapsEqual(sourceAlphas, sourceData.GetAlphamaps(0, 0, sourceData.alphamapWidth, sourceData.alphamapHeight)), Is.True);
        }

        [UnityTest]
        public IEnumerator MissingV4WarnsOnceAndLeavesV3SwitchingAvailable()
        {
            var partial = (TerrainLayer[])v4Layers.Clone();
            partialLayer = Object.Instantiate(v4Layers[1]);
            partialLayer.maskMapTexture = null;
            partial[1] = partialLayer;
            var switcher = MakeSwitcher(true, partial);
            var runtime = terrain.terrainData;
            var baselineMaps = new Texture2D[3, 3];
            for (var i = 0; i < 3; i++)
            {
                baselineMaps[i, 0] = runtime.terrainLayers[i + 1].diffuseTexture;
                baselineMaps[i, 1] = runtime.terrainLayers[i + 1].normalMapTexture;
                baselineMaps[i, 2] = runtime.terrainLayers[i + 1].maskMapTexture;
            }
            yield return null;
            LogAssert.Expect(LogType.Warning, new Regex("Road V4 is unavailable"));
            yield return Press(Key.F8);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3B, switcher.CurrentVariant);
            for (var i = 0; i < 3; i++)
            {
                Assert.AreSame(baselineMaps[i, 0], runtime.terrainLayers[i + 1].diffuseTexture);
                Assert.AreSame(baselineMaps[i, 1], runtime.terrainLayers[i + 1].normalMapTexture);
                Assert.AreSame(baselineMaps[i, 2], runtime.terrainLayers[i + 1].maskMapTexture);
            }
            yield return Press(Key.F8);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3B, switcher.CurrentVariant);
            yield return Press(Key.F6);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3A, switcher.CurrentVariant);
            switcher.enabled = false;
            yield return null;
            switcher.enabled = true;
            yield return null;
            LogAssert.Expect(LogType.Warning, new Regex("Road V4 is unavailable"));
            yield return Press(Key.F8);
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3B, switcher.CurrentVariant);
        }

        [UnityTest]
        public IEnumerator F7DeepBumpV4V3RoundTripRestoresFullLayerStateAndReusesCopies()
        {
            var originalData = terrain.terrainData;
            var sourceHeights = sourceData.GetHeights(0, 0, sourceData.heightmapResolution, sourceData.heightmapResolution);
            var sourceAlphas = sourceData.GetAlphamaps(0, 0, sourceData.alphamapWidth, sourceData.alphamapHeight);
            var sourceGrass = sourceData.GetDetailLayer(0, 0, sourceData.detailWidth, sourceData.detailHeight, 0);
            var v3Properties = new LayerSnapshot[3];
            for (var i = 0; i < 3; i++) v3Properties[i] = new LayerSnapshot(sourceLayers[i + 1]);
            var switcher = MakeSwitcher(true, null, true);
            yield return null;
            var runtime = terrain.terrainData;
            var collider = terrain.GetComponent<TerrainCollider>();
            var runtimeLayers = new[] { runtime.terrainLayers[1], runtime.terrainLayers[2], runtime.terrainLayers[3] };
            var roadLayers = new[] { sourceLayers[1], sourceLayers[2], sourceLayers[3] };
            var deepProperties = new LayerSnapshot[3];
            for (var i = 0; i < 3; i++) deepProperties[i] = new LayerSnapshot(deepBumpLayers[i]);

            yield return Press(Key.F9);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.DeepBump, switcher.CurrentVariant);
            for (var i = 0; i < 3; i++) AssertLayerEquals(deepProperties[i], runtimeLayers[i], deepBumpDiffuse, deepBumpNormal, deepBumpMask);
            yield return Press(Key.F9);
            yield return null;
            Assert.AreSame(runtime, terrain.terrainData);
            for (var i = 0; i < 3; i++) Assert.AreSame(runtimeLayers[i], runtime.terrainLayers[i + 1]);

            yield return Press(Key.F8);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V4, switcher.CurrentVariant);
            for (var i = 0; i < 3; i++) AssertLayerEquals(new LayerSnapshot(v4Layers[i]), runtimeLayers[i], v4Diffuse[i], v4Normal, v4Mask);
            yield return Press(Key.F9);
            yield return null;
            for (var i = 0; i < 3; i++) AssertLayerEquals(deepProperties[i], runtimeLayers[i], deepBumpDiffuse, deepBumpNormal, deepBumpMask);

            yield return Press(Key.F6);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3A, switcher.CurrentVariant);
            for (var i = 0; i < 3; i++)
            {
                AssertLayerEquals(v3Properties[i], runtimeLayers[i], sourceColor, normalA, sourceMask);
            }
            yield return Press(Key.F7);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3B, switcher.CurrentVariant);
            for (var i = 0; i < 3; i++) AssertLayerEquals(v3Properties[i], runtimeLayers[i], sourceColor, normalB, sourceMask);

            Assert.AreSame(originalData, sourceData);
            Assert.AreSame(runtime, collider.terrainData);
            Assert.That(HeightsEqual(sourceHeights, sourceData.GetHeights(0, 0, sourceData.heightmapResolution, sourceData.heightmapResolution)), Is.True);
            Assert.That(AlphamapsEqual(sourceAlphas, sourceData.GetAlphamaps(0, 0, sourceData.alphamapWidth, sourceData.alphamapHeight)), Is.True);
            Assert.That(DetailLayersEqual(sourceGrass, sourceData.GetDetailLayer(0, 0, sourceData.detailWidth, sourceData.detailHeight, 0)), Is.True);
            for (var i = 0; i < 3; i++)
            {
                Assert.AreSame(roadLayers[i], sourceData.terrainLayers[i + 1]);
                Assert.AreSame(sourceColor, roadLayers[i].diffuseTexture);
                Assert.AreSame(sourceMask, roadLayers[i].maskMapTexture);
                Assert.AreEqual(new Vector2(2f, -1f), roadLayers[i].tileOffset);
                Assert.AreEqual(new Vector2(4f, 4f), roadLayers[i].tileSize);
            }

            yield return Press(Key.F9);
            switcher.enabled = false;
            yield return null;
            Assert.AreSame(originalData, terrain.terrainData);
            Assert.AreSame(originalData, collider.terrainData);
            Assert.IsTrue(runtime == null);
            foreach (var layer in runtimeLayers) Assert.IsTrue(layer == null);
            switcher.enabled = true;
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3B, switcher.CurrentVariant);
            var reentered = terrain.terrainData;
            Assert.AreNotSame(runtime, reentered);
            yield return Press(Key.F9);
            var reenteredLayers = new[] { reentered.terrainLayers[1], reentered.terrainLayers[2], reentered.terrainLayers[3] };
            switcher.enabled = false;
            yield return null;
            Assert.AreSame(originalData, terrain.terrainData);
            foreach (var layer in reenteredLayers) Assert.IsTrue(layer == null);
            switcher.enabled = true;
            yield return null;
            yield return Press(Key.F9);
            var destroyedRuntime = terrain.terrainData;
            var destroyedLayers = new[] { destroyedRuntime.terrainLayers[1], destroyedRuntime.terrainLayers[2], destroyedRuntime.terrainLayers[3] };
            Object.Destroy(switcher);
            yield return null;
            Assert.AreSame(originalData, terrain.terrainData);
            Assert.AreSame(originalData, collider.terrainData);
            Assert.IsTrue(destroyedRuntime == null);
            foreach (var layer in destroyedLayers) Assert.IsTrue(layer == null);
        }

        [UnityTest]
        public IEnumerator InvalidOptionalTemplatesLeaveCurrentVariantIntactAndOtherVariantWorks()
        {
            var partialDeep = (TerrainLayer[])deepBumpLayers.Clone();
            invalidDeepBumpLayer = Object.Instantiate(deepBumpLayers[1]);
            invalidDeepBumpLayer.normalMapTexture = null;
            partialDeep[1] = invalidDeepBumpLayer;
            var switcher = MakeSwitcher(true, null, true, partialDeep);
            yield return null;
            var runtime = terrain.terrainData;
            yield return Press(Key.F8);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V4, switcher.CurrentVariant);
            var v4Maps = CaptureMaps(runtime);
            LogAssert.Expect(LogType.Warning, new Regex("DeepBump is unavailable"));
            yield return Press(Key.F9);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V4, switcher.CurrentVariant);
            AssertMaps(v4Maps, runtime);
            yield return Press(Key.F9);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V4, switcher.CurrentVariant);
            AssertMaps(v4Maps, runtime);
            yield return Press(Key.F6);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3A, switcher.CurrentVariant);
            yield return Press(Key.F9);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3A, switcher.CurrentVariant);

            var partialV4 = (TerrainLayer[])v4Layers.Clone();
            partialLayer = Object.Instantiate(v4Layers[1]);
            partialLayer.diffuseTexture = null;
            partialV4[1] = partialLayer;
            // Replace the templates and re-enable so availability is revalidated independently.
            switcher.enabled = false;
            yield return null;
            SetField(switcher, "v4TemplateLayers", partialV4);
            SetField(switcher, "deepBumpTemplateLayers", deepBumpLayers);
            switcher.enabled = true;
            yield return null;
            yield return Press(Key.F9);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.DeepBump, switcher.CurrentVariant);
            var deepMaps = CaptureMaps(terrain.terrainData);
            LogAssert.Expect(LogType.Warning, new Regex("Road V4 is unavailable"));
            yield return Press(Key.F8);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.DeepBump, switcher.CurrentVariant);
            AssertMaps(deepMaps, terrain.terrainData);
            yield return Press(Key.F8);
            yield return null;
            AssertMaps(deepMaps, terrain.terrainData);
            yield return Press(Key.F6);
            yield return null;
            Assert.AreEqual(TerrainRoadV3RuntimeSwitcher.Variant.V3A, switcher.CurrentVariant);
        }

        private struct LayerSnapshot
        {
            public Vector2 tileSize, tileOffset;
            public float normalScale, metallic, smoothness;
            public Color specular;
            public Vector4 diffuseRemapMin, diffuseRemapMax, maskMapRemapMin, maskMapRemapMax;
            public LayerSnapshot(TerrainLayer layer)
            {
                tileSize = layer.tileSize; tileOffset = layer.tileOffset; normalScale = layer.normalScale;
                metallic = layer.metallic; smoothness = layer.smoothness; specular = layer.specular;
                diffuseRemapMin = layer.diffuseRemapMin; diffuseRemapMax = layer.diffuseRemapMax;
                maskMapRemapMin = layer.maskMapRemapMin; maskMapRemapMax = layer.maskMapRemapMax;
            }
        }

        private static void AssertLayerEquals(LayerSnapshot expected, TerrainLayer actual, Texture2D diffuse, Texture2D normal, Texture2D mask)
        {
            Assert.AreSame(diffuse, actual.diffuseTexture);
            Assert.AreSame(normal, actual.normalMapTexture);
            Assert.AreSame(mask, actual.maskMapTexture);
            Assert.AreEqual(expected.tileSize, actual.tileSize); Assert.AreEqual(expected.tileOffset, actual.tileOffset);
            Assert.AreEqual(expected.normalScale, actual.normalScale); Assert.AreEqual(expected.metallic, actual.metallic);
            Assert.AreEqual(expected.smoothness, actual.smoothness);
            AssertColorApproximately(expected.specular, actual.specular);
            Assert.AreEqual(expected.diffuseRemapMin, actual.diffuseRemapMin);
            Assert.AreEqual(expected.diffuseRemapMax, actual.diffuseRemapMax);
            Assert.AreEqual(expected.maskMapRemapMin, actual.maskMapRemapMin);
            Assert.AreEqual(expected.maskMapRemapMax, actual.maskMapRemapMax);
        }

        private static void AssertColorApproximately(Color expected, Color actual)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(1e-5f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(1e-5f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(1e-5f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(1e-5f));
        }

        private static Texture2D[,] CaptureMaps(TerrainData data)
        {
            var maps = new Texture2D[3, 3];
            for (var i = 0; i < 3; i++)
            {
                maps[i, 0] = data.terrainLayers[i + 1].diffuseTexture;
                maps[i, 1] = data.terrainLayers[i + 1].normalMapTexture;
                maps[i, 2] = data.terrainLayers[i + 1].maskMapTexture;
            }
            return maps;
        }

        private static void AssertMaps(Texture2D[,] expected, TerrainData data)
        {
            for (var i = 0; i < 3; i++)
            {
                Assert.AreSame(expected[i, 0], data.terrainLayers[i + 1].diffuseTexture);
                Assert.AreSame(expected[i, 1], data.terrainLayers[i + 1].normalMapTexture);
                Assert.AreSame(expected[i, 2], data.terrainLayers[i + 1].maskMapTexture);
            }
        }

        private TerrainRoadV3RuntimeSwitcher MakeSwitcher(bool withV4, TerrainLayer[] templateOverride = null, bool withDeepBump = false, TerrainLayer[] deepBumpOverride = null)
        {
            var switcher = root.AddComponent<TerrainRoadV3RuntimeSwitcher>();
            SetField(switcher, "terrain", terrain);
            SetField(switcher, "sourceLayers", new[] { sourceLayers[1], sourceLayers[2], sourceLayers[3] });
            SetField(switcher, "normalA", normalA);
            SetField(switcher, "normalB", normalB);
            if (withV4) SetField(switcher, "v4TemplateLayers", templateOverride ?? v4Layers);
            if (withDeepBump) SetField(switcher, "deepBumpTemplateLayers", deepBumpOverride ?? deepBumpLayers);
            root.SetActive(true);
            return switcher;
        }

        private IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
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

        private static Texture2D MakeColor(string label, Color color)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { name = label };
            texture.SetPixels(new[] { color, color, color, color });
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

        private static bool AlphamapsEqual(float[,,] a, float[,,] b)
        {
            if (a.GetLength(0) != b.GetLength(0) || a.GetLength(1) != b.GetLength(1) || a.GetLength(2) != b.GetLength(2)) return false;
            for (var y = 0; y < a.GetLength(0); y++)
                for (var x = 0; x < a.GetLength(1); x++)
                    for (var layer = 0; layer < a.GetLength(2); layer++)
                        if (Mathf.Abs(a[y, x, layer] - b[y, x, layer]) > 1e-6f) return false;
            return true;
        }

        private static bool DetailLayersEqual(int[,] a, int[,] b)
        {
            if (a.GetLength(0) != b.GetLength(0) || a.GetLength(1) != b.GetLength(1)) return false;
            for (var y = 0; y < a.GetLength(0); y++)
                for (var x = 0; x < a.GetLength(1); x++)
                    if (a[y, x] != b[y, x]) return false;
            return true;
        }
    }
}
