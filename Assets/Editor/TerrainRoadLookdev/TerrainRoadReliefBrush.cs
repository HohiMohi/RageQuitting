using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TerrainTools;

namespace RageQuitting.EditorTools.TerrainRoadLookdev
{
    /// <summary>
    /// Scene View brush for coordinated stone/earth terrain-layer painting and relief.
    /// This window intentionally edits only the explicitly assigned TerrainData.
    /// </summary>
    public sealed class TerrainRoadReliefBrush : EditorWindow
    {
        private enum SurfaceKind { Stone, DarkEarth }

        private const string SurfaceDirectory = "Assets/Art/Environment/TerrainRoadLookdev/SurfaceV2";
        private const string ReportPath = "Artifacts/TerrainRoadReliefV2/brush_import_validation.json";
        private static readonly string[] StampNames =
        {
            "Shard A", "Shard B", "Shard C"
        };
        private static readonly string[] StampPaths =
        {
            SurfaceDirectory + "/B_ReliefShard_A.png",
            SurfaceDirectory + "/B_ReliefShard_B.png",
            SurfaceDirectory + "/B_ReliefShard_C.png"
        };
        private static readonly string[] TilingTextures =
        {
            "T_Road_ReliefV2", "N_Road_ReliefV2",
            "T_Stone_Grey_ReliefV2", "N_Stone_Grey_ReliefV2",
            "T_Earth_Dark_ReliefV2", "N_Earth_Dark_ReliefV2",
            "M_Road_StonePatches", "M_Road_EarthRecess", "M_Road_EarthPatches", "M_Road_FineChips"
        };

        [MenuItem("Tools/Terrain Road Relief/Apply and validate imports")]
        private static void ApplyImportSettingsFromMenu()
        {
            string report = ApplyAndValidateImports();
            Debug.Log("Terrain road brush import validation: " + report);
        }

        [SerializeField] private Terrain targetTerrain;
        [SerializeField] private TerrainLayer stoneLayer;
        [SerializeField] private TerrainLayer darkEarthLayer;
        [SerializeField] private Texture2D roadLimitMask;
        [SerializeField] private SurfaceKind surfaceKind;
        [SerializeField] private int stampIndex;
        [SerializeField] private bool paintingEnabled;
        [SerializeField] private float brushSize = 1f;
        [SerializeField] private float strength = 0.8f;
        [SerializeField] private float rotationDegrees;
        [SerializeField] private float scaleJitter = 0.25f;
        [SerializeField] private bool randomRotation = true;
        [SerializeField] private Vector2 stoneLift = new Vector2(0.02f, 0.04f);
        [SerializeField] private Vector2 earthRecess = new Vector2(0.01f, 0.02f);

        private bool strokeActive;
        private bool strokeUndoRegistered;
        private Vector3 previousPoint;
        private float distanceToNextStamp;
        private int undoGroup;
        private System.Random random = new System.Random();

        [MenuItem("Window/Terrain/Road Relief Brush")]
        private static void OpenWindow()
        {
            TerrainRoadReliefBrush window = GetWindow<TerrainRoadReliefBrush>("Road Relief");
            window.minSize = new Vector2(340f, 430f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            EndStroke();
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Terrain Road Relief", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Each stroke stamps one jagged surface fragment and its matching height relief. A readable road-limit mask is required; white permits painting.", MessageType.Info);

            targetTerrain = (Terrain)EditorGUILayout.ObjectField("Target Terrain", targetTerrain, typeof(Terrain), true);
            surfaceKind = (SurfaceKind)EditorGUILayout.EnumPopup("Surface", surfaceKind);
            stoneLayer = (TerrainLayer)EditorGUILayout.ObjectField("Stone Terrain Layer", stoneLayer, typeof(TerrainLayer), false);
            darkEarthLayer = (TerrainLayer)EditorGUILayout.ObjectField("Dark Earth Terrain Layer", darkEarthLayer, typeof(TerrainLayer), false);
            roadLimitMask = (Texture2D)EditorGUILayout.ObjectField("Fixed Road Limit Mask", roadLimitMask, typeof(Texture2D), false);

            EditorGUILayout.Space();
            stampIndex = EditorGUILayout.Popup("Centered Stamp", Mathf.Clamp(stampIndex, 0, StampNames.Length - 1), StampNames);
            brushSize = EditorGUILayout.Slider("Stamp Size (m)", brushSize, 0.75f, 1.5f);
            strength = EditorGUILayout.Slider("Strength", strength, 0.05f, 1f);
            rotationDegrees = EditorGUILayout.Slider("Rotation (degrees)", rotationDegrees, -180f, 180f);
            randomRotation = EditorGUILayout.Toggle("Random Rotation", randomRotation);
            scaleJitter = EditorGUILayout.Slider("Scale Jitter", scaleJitter, 0f, 0.5f);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                stoneLift = EditorGUILayout.Vector2Field("Stone lift (m)", stoneLift);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                earthRecess = EditorGUILayout.Vector2Field("Earth recess (m)", earthRecess);
            }
            stoneLift.x = Mathf.Clamp(stoneLift.x, 0.02f, 0.04f);
            stoneLift.y = Mathf.Clamp(stoneLift.y, stoneLift.x, 0.04f);
            earthRecess.x = Mathf.Clamp(earthRecess.x, 0.01f, 0.02f);
            earthRecess.y = Mathf.Clamp(earthRecess.y, earthRecess.x, 0.02f);

            EditorGUILayout.Space();
            bool wasPaintingEnabled = paintingEnabled;
            bool ready = ValidateSetup(out string reason);
            using (new EditorGUI.DisabledScope(!ready))
            {
                paintingEnabled = GUILayout.Toggle(paintingEnabled, paintingEnabled ? "Painting enabled — drag in Scene View" : "Enable Scene View painting", "Button", GUILayout.Height(30f));
            }
            if (wasPaintingEnabled && !paintingEnabled)
                EndStroke();
            if (!ready)
                EditorGUILayout.HelpBox(reason, MessageType.Warning);
            else if (paintingEnabled)
                EditorGUILayout.HelpBox("Left-drag on the assigned Terrain to paint. The stroke samples at spaced intervals and will not repeat while stationary.", MessageType.None);

            EditorGUILayout.Space();
            if (GUILayout.Button("Apply and validate road/stamp import settings"))
            {
                string report = ApplyAndValidateImports();
                Debug.Log("Terrain road brush import validation: " + report);
            }
        }

        private bool ValidateSetup(out string reason)
        {
            if (targetTerrain == null || targetTerrain.terrainData == null)
            {
                reason = "Assign a target Terrain with TerrainData.";
                return false;
            }
            TerrainLayer chosenLayer = surfaceKind == SurfaceKind.Stone ? stoneLayer : darkEarthLayer;
            if (chosenLayer == null)
            {
                reason = surfaceKind == SurfaceKind.Stone ? "Assign the stone TerrainLayer." : "Assign the dark earth TerrainLayer.";
                return false;
            }
            if (Array.IndexOf(targetTerrain.terrainData.terrainLayers, chosenLayer) < 0)
            {
                reason = "The selected TerrainLayer must already be assigned to this Terrain. The brush does not change TerrainLayer arrays.";
                return false;
            }
            if (roadLimitMask == null || !roadLimitMask.isReadable)
            {
                reason = "Assign a readable fixed road-limit mask (white pixels permit painting).";
                return false;
            }
            Texture2D stamp = GetStamp();
            if (stamp == null || !stamp.isReadable)
            {
                reason = "The selected centered stamp is missing or not readable. Apply and validate import settings.";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private Texture2D GetStamp()
        {
            if (stampIndex < 0 || stampIndex >= StampPaths.Length)
                return null;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(StampPaths[stampIndex]);
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!paintingEnabled || targetTerrain == null || roadLimitMask == null)
                return;

            Event evt = Event.current;
            if (strokeActive && (evt.type == EventType.MouseUp || evt.type == EventType.Ignore))
            {
                EndStroke();
                if (evt.type == EventType.MouseUp)
                    evt.Use();
                return;
            }
            Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            TerrainCollider terrainCollider = targetTerrain.GetComponent<TerrainCollider>();
            if (terrainCollider == null || !terrainCollider.Raycast(ray, out RaycastHit hit, float.MaxValue))
                return;

            Vector3 localHit = targetTerrain.transform.InverseTransformPoint(hit.point);
            bool allowed = RoadAllows(localHit.x, localHit.z);
            DrawStampPreview(localHit, allowed);
            Handles.Label(hit.point + hit.normal * 0.03f, allowed ? "road relief" : "outside road mask");

            if (!allowed || !ValidateSetup(out _))
                return;
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
            {
                BeginStroke(localHit);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && strokeActive && evt.button == 0)
            {
                ContinueStroke(localHit);
                evt.Use();
            }
            else if ((evt.type == EventType.MouseUp && evt.button == 0) || evt.type == EventType.Ignore)
            {
                EndStroke();
            }
            if (evt.type == EventType.MouseMove)
                sceneView.Repaint();
        }

        private void BeginStroke(Vector3 localPoint)
        {
            if (!ValidateSetup(out string reason))
            {
                ShowNotification(new GUIContent(reason));
                return;
            }

            strokeActive = true;
            strokeUndoRegistered = false;
            distanceToNextStamp = 0f;
            previousPoint = localPoint;
            undoGroup = Undo.GetCurrentGroup();
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paint Road Relief Stroke");
            StampAt(localPoint);
        }

        private void ContinueStroke(Vector3 localPoint)
        {
            Vector2 from = new Vector2(previousPoint.x, previousPoint.z);
            Vector2 to = new Vector2(localPoint.x, localPoint.z);
            Vector2 delta = to - from;
            float distance = delta.magnitude;
            float spacing = Mathf.Max(0.15f, brushSize * 0.42f);
            if (distance <= 0.0001f)
                return;

            float traversed = 0f;
            while (distanceToNextStamp + distance - traversed >= spacing)
            {
                float segment = spacing - distanceToNextStamp;
                traversed += segment;
                Vector2 position = Vector2.Lerp(from, to, traversed / distance);
                Vector3 candidate = new Vector3(position.x, localPoint.y, position.y);
                if (RoadAllows(candidate.x, candidate.z))
                    StampAt(candidate);
                distanceToNextStamp = 0f;
            }
            distanceToNextStamp += distance - traversed;
            previousPoint = localPoint;
        }

        private void EndStroke()
        {
            if (!strokeActive)
                return;
            strokeActive = false;
            try
            {
                PaintContext.ApplyDelayedActions();
            }
            finally
            {
                if (strokeUndoRegistered)
                    Undo.CollapseUndoOperations(undoGroup);
                strokeUndoRegistered = false;
            }
        }

        private void StampAt(Vector3 center)
        {
            Texture2D stamp = GetStamp();
            if (stamp == null || !RoadAllows(center.x, center.z))
                return;

            TerrainData data = targetTerrain.terrainData;
            TerrainLayer[] layers = data.terrainLayers;
            TerrainLayer targetLayer = surfaceKind == SurfaceKind.Stone ? stoneLayer : darkEarthLayer;
            int targetLayerIndex = Array.IndexOf(layers, targetLayer);
            if (targetLayerIndex < 0)
                return;

            float sizeScale = 1f + Range(-scaleJitter, scaleJitter);
            float stampSize = Mathf.Clamp(brushSize * sizeScale, 0.75f, 1.5f);
            float angle = rotationDegrees + (randomRotation ? Range(-180f, 180f) : 0f);
            float heightDelta = surfaceKind == SurfaceKind.Stone
                ? Range(stoneLift.x, stoneLift.y)
                : -Range(earthRecess.x, earthRecess.y);
            float opacity = strength;
            Rect region = new Rect(center.x - stampSize * 0.85f, center.z - stampSize * 0.85f, stampSize * 1.7f, stampSize * 1.7f);
            Rect terrainBounds = new Rect(0f, 0f, data.size.x, data.size.z);
            region = Intersect(region, terrainBounds);
            if (region.width <= 0f || region.height <= 0f)
                return;

            if (!strokeUndoRegistered)
            {
                // Alphamap pixels live in separate TerrainData-owned textures.
                // Register them with the TerrainData so a single Ctrl-Z restores
                // both the height change and the paired splat change.
                var undoObjects = new List<UnityEngine.Object> { data };
                undoObjects.AddRange(data.alphamapTextures);
                Undo.RegisterCompleteObjectUndo(undoObjects.ToArray(), "Paint Road Relief Stroke");
                strokeUndoRegistered = true;
            }

            Texture2D road = roadLimitMask;
            Vector2 maskCoverage = GetMaskCoverage(stamp.GetPixels(), stamp.width, stamp.height);
            float sizeX = data.size.x;
            float sizeZ = data.size.z;
            PaintContext textureContext = null;
            PaintContext heightContext = null;
            bool textureEnded = false;
            bool heightEnded = false;
            try
            {
                heightContext = TerrainPaintUtility.BeginPaintHeightmap(targetTerrain, region, 0, false);
                if (heightContext == null || heightContext.terrainCount != 1)
                {
                    if (heightContext != null)
                        TerrainPaintUtility.ReleaseContextResources(heightContext);
                    heightContext = null;
                    throw new InvalidOperationException("Height paint context did not resolve to the assigned Terrain only.");
                }
                Color[] heights = ReadRenderTexture(heightContext.sourceRenderTexture);

                // BeginPaintTexture gathers the requested layer into a single-channel R8 context.
                textureContext = TerrainPaintUtility.BeginPaintTexture(targetTerrain, region, targetLayer, 0, false);
                if (textureContext == null || textureContext.terrainCount != 1)
                {
                    if (textureContext != null)
                        TerrainPaintUtility.ReleaseContextResources(textureContext);
                    textureContext = null;
                    throw new InvalidOperationException("Texture paint context did not resolve to the assigned Terrain only.");
                }
                Color[] layerPixels = ReadRenderTexture(textureContext.sourceRenderTexture);

                ApplyStamp(heightContext, heights, center, stampSize, angle, stamp, maskCoverage, road, sizeX, sizeZ, opacity,
                    true,
                    (color, influence) => new Color(Mathf.Clamp01(color.r + heightDelta * influence * PaintContext.kNormalizedHeightScale / data.size.y), color.g, color.b, color.a));
                ApplyStamp(textureContext, layerPixels, center, stampSize, angle, stamp, maskCoverage, road, sizeX, sizeZ, opacity, false,
                    (color, influence) => new Color(Mathf.Lerp(color.r, 1f, influence), color.g, color.b, color.a));

                WriteRenderTexture(heightContext.destinationRenderTexture, heights);
                WriteRenderTexture(textureContext.destinationRenderTexture, layerPixels);
                TerrainPaintUtility.EndPaintHeightmap(heightContext, "Paint Road Relief Stroke");
                heightEnded = true;
                TerrainPaintUtility.EndPaintTexture(textureContext, "Paint Road Relief Stroke");
                textureEnded = true;
                textureContext = null;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowNotification(new GUIContent("Road relief stamp failed; see Console."));
            }
            finally
            {
                if (heightContext != null && !heightEnded)
                    TerrainPaintUtility.ReleaseContextResources(heightContext);
                if (textureContext != null && !textureEnded)
                    TerrainPaintUtility.ReleaseContextResources(textureContext);
            }
        }

        private delegate Color HeightOperation(Color source, float influence);

        private void ApplyStamp(PaintContext context, Color[] pixels, Vector3 center, float stampSize, float angle,
            Texture2D stampTexture, Vector2 maskCoverage, Texture2D roadTexture,
            float sizeX, float sizeZ, float opacity, bool sharedHeightMap, HeightOperation operation)
        {
            int width = context.destinationRenderTexture.width;
            int height = context.destinationRenderTexture.height;
            RectInt rect = context.pixelRect;
            float mapWidth = Mathf.Max(1, context.targetTextureWidth - (sharedHeightMap ? 1 : 0));
            float mapHeight = Mathf.Max(1, context.targetTextureHeight - (sharedHeightMap ? 1 : 0));
            float cosine = Mathf.Cos(angle * Mathf.Deg2Rad);
            float sine = Mathf.Sin(angle * Mathf.Deg2Rad);
            for (int y = 0; y < height; y++)
            {
                float terrainZ = (rect.y + y + (sharedHeightMap ? 0f : 0.5f)) * sizeZ / mapHeight;
                for (int x = 0; x < width; x++)
                {
                    float terrainX = (rect.x + x + (sharedHeightMap ? 0f : 0.5f)) * sizeX / mapWidth;
                    // Use the same UV sampler as RoadAllows so the hard road boundary agrees
                    // with texture-space filtering at edge texel centers.
                    float roadValue = roadTexture.GetPixelBilinear(terrainX / sizeX, terrainZ / sizeZ).r;
                    if (roadValue < 0.5f)
                        continue;
                    float u, v;
                    StampUv(terrainX - center.x, terrainZ - center.z, stampSize, maskCoverage, cosine, sine, out u, out v);
                    if (u < 0f || u > 1f || v < 0f || v > 1f)
                        continue;
                    float mask = stampTexture.GetPixelBilinear(u, v).r;
                    float influence = mask * roadValue * opacity;
                    if (influence <= 0.001f)
                        continue;
                    int index = y * width + x;
                    pixels[index] = operation(pixels[index], influence);
                }
            }
        }

        private bool RoadAllows(float x, float z)
        {
            if (targetTerrain == null || roadLimitMask == null || !roadLimitMask.isReadable)
                return false;
            Vector3 size = targetTerrain.terrainData.size;
            if (x < 0f || z < 0f || x > size.x || z > size.z)
                return false;
            return roadLimitMask.GetPixelBilinear(x / size.x, z / size.z).r >= 0.5f;
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin);
            float yMin = Mathf.Max(a.yMin, b.yMin);
            float xMax = Mathf.Min(a.xMax, b.xMax);
            float yMax = Mathf.Min(a.yMax, b.yMax);
            return new Rect(xMin, yMin, Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, yMax - yMin));
        }

        private void DrawStampPreview(Vector3 localCenter, bool allowed)
        {
            Texture2D stamp = GetStamp();
            if (stamp == null || !stamp.isReadable || targetTerrain == null)
                return;
            Vector2 maskCoverage = GetMaskCoverage(stamp.GetPixels(), stamp.width, stamp.height);
            Handles.color = allowed ? new Color(0.95f, 0.72f, 0.28f, 0.95f) : new Color(0.95f, 0.24f, 0.2f, 0.9f);
            const int points = 72;
            Vector3[] outline = new Vector3[points + 1];
            float size = brushSize;
            float angle = rotationDegrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(angle);
            float sine = Mathf.Sin(angle);
            for (int i = 0; i < points; i++)
            {
                float theta = i * Mathf.PI * 2f / points;
                float radius = 0f;
                for (float testRadius = 0.02f; testRadius <= 0.49f; testRadius += 0.005f)
                {
                    float u = 0.5f + Mathf.Cos(theta) * testRadius;
                    float v = 0.5f + Mathf.Sin(theta) * testRadius;
                    if (stamp.GetPixelBilinear(u, v).r < 0.5f)
                        break;
                    radius = testRadius;
                }
                float x = Mathf.Cos(theta) * radius * size / maskCoverage.x;
                float z = Mathf.Sin(theta) * radius * size / maskCoverage.y;
                float localX = localCenter.x + cosine * x - sine * z;
                float localZ = localCenter.z + sine * x + cosine * z;
                Vector3 world = targetTerrain.transform.TransformPoint(new Vector3(localX, localCenter.y, localZ));
                world.y = targetTerrain.SampleHeight(world) + targetTerrain.transform.position.y + 0.025f;
                outline[i] = world;
            }
            outline[points] = outline[0];
            Handles.DrawAAPolyLine(2f, outline);
        }

        private static Vector2 GetMaskCoverage(Color[] pixels, int width, int height)
        {
            int minX = width;
            int maxX = -1;
            int minY = height;
            int maxY = -1;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (pixels[y * width + x].r < 0.5f)
                        continue;
                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
            }
            if (maxX < minX || maxY < minY)
                return Vector2.one;
            return new Vector2(Mathf.Max(0.01f, (maxX - minX) / (float)(width - 1)),
                Mathf.Max(0.01f, (maxY - minY) / (float)(height - 1)));
        }

        private static void StampUv(float dx, float dz, float size, Vector2 maskCoverage, float cosine, float sine, out float u, out float v)
        {
            float x = (cosine * dx + sine * dz) * maskCoverage.x / size + 0.5f;
            float y = (-sine * dx + cosine * dz) * maskCoverage.y / size + 0.5f;
            u = x;
            v = y;
        }

        private static Color[] ReadRenderTexture(RenderTexture renderTexture)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = renderTexture;
                readback.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0, false);
                readback.Apply(false, false);
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                DestroyImmediate(readback);
            }
        }

        private static void WriteRenderTexture(RenderTexture renderTexture, Color[] pixels)
        {
            Texture2D upload = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                upload.SetPixels(pixels);
                upload.Apply(false, false);
                Graphics.Blit(upload, renderTexture);
            }
            finally
            {
                DestroyImmediate(upload);
            }
        }

        private float Range(float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        private static string ApplyAndValidateImports()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            List<string> rows = new List<string>();
            bool allPassed = true;
            foreach (string name in TilingTextures)
                allPassed &= ConfigureTexture(name + ".png", TextureWrapMode.Repeat, rows);
            foreach (string path in StampPaths)
                allPassed &= ConfigureTexture(Path.GetFileName(path), TextureWrapMode.Clamp, rows);

            string reportDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts", "TerrainRoadReliefV2");
            Directory.CreateDirectory(reportDirectory);
            string report = "{\n  \"unityEditorVersion\":\"" + Application.unityVersion + "\",\n  \"allPassed\":" + (allPassed ? "true" : "false") +
                            ",\n  \"textures\":[\n    " + string.Join(",\n    ", rows) + "\n  ]\n}\n";
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), ReportPath), report);
            if (!allPassed)
                throw new InvalidOperationException("Road and stamp texture import settings did not all validate. See " + ReportPath);
            return ReportPath;
        }

        private static bool ConfigureTexture(string fileName, TextureWrapMode wrapMode, List<string> rows)
        {
            string path = SurfaceDirectory + "/" + fileName;
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                rows.Add("{\"name\":\"" + fileName + "\",\"passed\":false,\"error\":\"not imported\"}");
                return false;
            }
            bool isNormal = fileName.StartsWith("N_", StringComparison.Ordinal);
            bool isMask = fileName.StartsWith("M_", StringComparison.Ordinal) || fileName.StartsWith("B_", StringComparison.Ordinal);
            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !isNormal && !isMask;
            importer.wrapMode = wrapMode;
            importer.isReadable = isMask;
            importer.mipmapEnabled = !isMask;
            if (isMask)
                importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            TextureImporter verified = AssetImporter.GetAtPath(path) as TextureImporter;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool passed = verified != null && texture != null && verified.wrapMode == wrapMode && verified.isReadable == isMask &&
                          verified.textureType == (isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default) &&
                          verified.sRGBTexture == (!isNormal && !isMask) && verified.mipmapEnabled == !isMask;
            int maskMin = 255;
            int maskMax = 0;
            int maskEdgeMax = 0;
            float maskMean = 0f;
            if (isMask && texture != null)
            {
                Color[] pixels = texture.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    int value = Mathf.RoundToInt(pixels[i].r * 255f);
                    maskMin = Mathf.Min(maskMin, value);
                    maskMax = Mathf.Max(maskMax, value);
                    maskMean += value;
                    int x = i % texture.width;
                    int y = i / texture.width;
                    if (fileName.StartsWith("B_", StringComparison.Ordinal) &&
                        (x == 0 || y == 0 || x == texture.width - 1 || y == texture.height - 1))
                        maskEdgeMax = Mathf.Max(maskEdgeMax, value);
                }
                maskMean /= pixels.Length;
                if (fileName.StartsWith("B_", StringComparison.Ordinal))
                {
                    int center = Mathf.RoundToInt(texture.GetPixel(texture.width / 2, texture.height / 2).r * 255f);
                    passed &= texture.width == 256 && texture.height == 256 && maskMax >= 240 && maskMean > 20f &&
                              maskEdgeMax == 0 && center >= 180;
                }
            }
            rows.Add("{\"name\":\"" + fileName + "\",\"wrapMode\":\"" + (verified == null ? "missing" : verified.wrapMode.ToString()) +
                     "\",\"textureType\":\"" + (verified == null ? "missing" : verified.textureType.ToString()) +
                     "\",\"sRGB\":" + (verified != null && verified.sRGBTexture ? "true" : "false") +
                     ",\"readable\":" + (verified != null && verified.isReadable ? "true" : "false") +
                     ",\"mipmaps\":" + (verified != null && verified.mipmapEnabled ? "true" : "false") +
                     (isMask ? ",\"maskMin\":" + maskMin + ",\"maskMax\":" + maskMax + ",\"maskMean\":" + maskMean.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ",\"maskEdgeMax\":" + maskEdgeMax : string.Empty) +
                     ",\"passed\":" + (passed ? "true" : "false") + "}");
            return passed;
        }
    }
}
