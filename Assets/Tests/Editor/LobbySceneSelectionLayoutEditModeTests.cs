using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class LobbySceneSelectionLayoutEditModeTests
{
    private const string LobbyScenePath = "Assets/Scenes/MultiplayerStartScene.unity";
    private const string ActionGroupName = "SceneSelectionButtonStack";

    private Scene previewScene;

    [SetUp]
    public void SetUp()
    {
        previewScene = EditorSceneManager.OpenPreviewScene(LobbyScenePath);
        Assert.That(previewScene.IsValid() && previewScene.isLoaded, Is.True,
            "The saved lobby scene must load into an isolated preview scene.");
    }

    [TearDown]
    public void TearDown()
    {
        if (previewScene.IsValid() && previewScene.isLoaded)
            EditorSceneManager.ClosePreviewScene(previewScene);
    }

    [Test]
    public void SceneButtonsUseOneVerticalNonOverlappingStackInsideLobbyPanel()
    {
        Transform panel = Find(previewScene, "LobbyPanel");
        Transform stack = panel.Find(ActionGroupName);
        Assert.That(stack, Is.Not.Null, "The scene selection controls must share a dedicated layout group.");

        Type verticalLayoutGroupType = RequireType("UnityEngine.UI.VerticalLayoutGroup, UnityEngine.UI");
        Component layout = stack.GetComponent(verticalLayoutGroupType);
        Assert.That(layout, Is.Not.Null);
        Assert.That(ReadProperty<float>(layout, "spacing"), Is.EqualTo(12f));
        Assert.That(ReadProperty<bool>(layout, "childControlWidth"), Is.True);
        Assert.That(ReadProperty<bool>(layout, "childControlHeight"), Is.True);
        Assert.That(ReadProperty<bool>(layout, "childForceExpandWidth"), Is.False);
        Assert.That(ReadProperty<bool>(layout, "childForceExpandHeight"), Is.False);

        string[] expectedNames =
        {
            "LeaveRoomButton",
            "StartFppSceneButton",
            "StartTutorialSceneButton",
            "StartDeckAreaSceneButton"
        };
        Transform[] buttons = stack.Cast<Transform>().ToArray();
        Assert.That(buttons.Select(button => button.name), Is.EqualTo(expectedNames));

        Type layoutElementType = RequireType("UnityEngine.UI.LayoutElement, UnityEngine.UI");
        var panelRect = (RectTransform)panel;
        var stackRect = (RectTransform)stack;
        Assert.That(panelRect.anchorMin.y, Is.EqualTo(0f));
        Assert.That(panelRect.anchorMax.y, Is.EqualTo(1f));
        Assert.That(panelRect.sizeDelta.y, Is.EqualTo(0f),
            "LobbyPanel must follow the Canvas height as the aspect ratio changes.");

        Transform canvas = panel.parent;
        Type canvasScalerType = RequireType("UnityEngine.UI.CanvasScaler, UnityEngine.UI");
        Component canvasScaler = canvas.GetComponent(canvasScalerType);
        Assert.That(canvasScaler, Is.Not.Null);
        Vector2 referenceResolution = ReadProperty<Vector2>(canvasScaler, "referenceResolution");
        Assert.That(referenceResolution, Is.EqualTo(new Vector2(1920f, 1080f)));
        Assert.That(ReadProperty<float>(canvasScaler, "matchWidthOrHeight"), Is.EqualTo(0f),
            "The preview fixture must verify the project's width-based Canvas scaling.");

        Canvas canvasComponent = canvas.GetComponent<Canvas>();
        Assert.That(canvasComponent, Is.Not.Null);
        ((Behaviour)canvasScaler).enabled = false;
        canvasComponent.renderMode = RenderMode.WorldSpace;
        var canvasRect = (RectTransform)canvasComponent.transform;
        canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
        canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
        canvasRect.pivot = new Vector2(0.5f, 0.5f);
        canvasRect.anchoredPosition = Vector2.zero;
        canvasRect.localScale = Vector3.one;

        foreach (Vector2 aspect in new[] { new Vector2(1920f, 1080f), new Vector2(2560f, 1080f) })
        {
            float logicalHeight = aspect.y * (referenceResolution.x / aspect.x);
            canvasRect.sizeDelta = new Vector2(referenceResolution.x, logicalHeight);
            RebuildLayout(stackRect);
            Assert.That(canvasRect.rect.height, Is.EqualTo(logicalHeight).Within(0.01f));
            Assert.That(panelRect.rect.height, Is.EqualTo(logicalHeight).Within(0.01f),
                "The stretched LobbyPanel must match the controlled Canvas height.");

            Rect stackBounds = GetRectInLocalSpace(panelRect, stackRect);
            AssertRectContained(panelRect.rect, stackBounds,
                $"The complete section must fit LobbyPanel at {aspect.x:0}×{aspect.y:0}.");
            AssertRectContained(canvasRect.rect, stackBounds,
                $"The complete section must fit Canvas at {aspect.x:0}×{aspect.y:0}.");

            foreach (string lobbyContentName in new[]
            {
                "PlayerSlotsContainer", "LobbyTitleText", "LobbyCodeText", "CopyCodeButton"
            })
            {
                Rect lobbyContent = GetRectInLocalSpace(panelRect,
                    (RectTransform)Find(previewScene, lobbyContentName));
                Assert.That(stackBounds.Overlaps(lobbyContent), Is.False,
                    $"The scene selection stack must not overlap {lobbyContentName} at {aspect.x:0}×{aspect.y:0}.");
            }

            var localRects = new Rect[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                var buttonRect = (RectTransform)buttons[i];
                Component element = buttonRect.GetComponent(layoutElementType);
                Assert.That(element, Is.Not.Null, $"{buttons[i].name} needs a LayoutElement.");
                Assert.That(ReadProperty<float>(element, "preferredWidth"), Is.EqualTo(280f));
                Assert.That(ReadProperty<float>(element, "preferredHeight"), Is.EqualTo(70f));
                localRects[i] = GetRectInLocalSpace(panelRect, buttonRect);
                AssertRectContained(panelRect.rect, localRects[i],
                    $"{buttons[i].name} must fit LobbyPanel at {aspect.x:0}×{aspect.y:0}.");
                AssertRectContained(canvasRect.rect, localRects[i],
                    $"{buttons[i].name} must fit Canvas at {aspect.x:0}×{aspect.y:0}.");
                AssertRectContained(stackBounds, localRects[i],
                    $"{buttons[i].name} must fit the layout stack at {aspect.x:0}×{aspect.y:0}.");
            }

            for (int i = 0; i < localRects.Length; i++)
            for (int j = i + 1; j < localRects.Length; j++)
                Assert.That(localRects[i].Overlaps(localRects[j]), Is.False,
                    $"{buttons[i].name} and {buttons[j].name} must not overlap at {aspect.x:0}×{aspect.y:0}.");
        }

    }

    [Test]
    public void SavedLobbySceneKeepsSeparateSceneReferencesAndLabels()
    {
        Transform panel = Find(previewScene, "LobbyPanel");
        Transform tutorialButton = panel.Find(ActionGroupName + "/StartTutorialSceneButton");
        Transform deckButton = panel.Find(ActionGroupName + "/StartDeckAreaSceneButton");
        Assert.That(tutorialButton, Is.Not.Null);
        Assert.That(deckButton, Is.Not.Null);
        Assert.That(tutorialButton, Is.Not.SameAs(deckButton));

        Component roomManager = Find(previewScene, "MultiplayerRoomManager").GetComponent(
            RequireType("MultiplayerRoomManager, Assembly-CSharp"));
        Assert.That(roomManager, Is.Not.Null);
        var roomSerialized = new SerializedObject(roomManager);
        Type buttonType = RequireType("UnityEngine.UI.Button, UnityEngine.UI");
        Component tutorialComponent = tutorialButton.GetComponent(buttonType);
        Component deckComponent = deckButton.GetComponent(buttonType);
        Assert.That(tutorialComponent, Is.Not.Null);
        Assert.That(deckComponent, Is.Not.Null);
        Assert.That(roomSerialized.FindProperty("startTutorialSceneButton").objectReferenceValue,
            Is.SameAs(tutorialComponent));
        Assert.That(roomSerialized.FindProperty("startDeckAreaSceneButton").objectReferenceValue,
            Is.SameAs(deckComponent));

        Assert.That(ReadLabel(tutorialButton), Is.EqualTo("Tutorial"));
        Assert.That(ReadLabel(deckButton), Is.EqualTo("Tutorial — cięcie płyt"));
        UnityEngine.Object font = ReadProperty<UnityEngine.Object>(FindText(deckButton), "font");
        Assert.That(AssetDatabase.GetAssetPath(font),
            Is.EqualTo("Assets/Experiments/BridgeDeckAreaV1/Profiles/DeckAreaUiFontBaked.asset"));
    }

    private static Rect GetRectInLocalSpace(RectTransform localSpace, RectTransform target)
    {
        Matrix4x4 relativeMatrix = Matrix4x4.identity;
        Transform current = target;
        while (current != localSpace)
        {
            Assert.That(current.parent, Is.Not.Null,
                $"{target.name} must remain a descendant of {localSpace.name}.");
            relativeMatrix = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale)
                * relativeMatrix;
            current = current.parent;
        }

        Rect rect = target.rect;
        Vector3[] corners =
        {
            new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMin, rect.yMax),
            new Vector3(rect.xMax, rect.yMin), new Vector3(rect.xMax, rect.yMax)
        };
        Vector3 first = relativeMatrix.MultiplyPoint3x4(corners[0]);
        Vector2 min = first;
        Vector2 max = first;
        foreach (Vector3 corner in corners.Skip(1))
        {
            Vector3 point = relativeMatrix.MultiplyPoint3x4(corner);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void AssertRectContained(Rect container, Rect candidate, string message)
    {
        Assert.That(candidate.min.x, Is.GreaterThanOrEqualTo(container.min.x), message);
        Assert.That(candidate.min.y, Is.GreaterThanOrEqualTo(container.min.y), message);
        Assert.That(candidate.max.x, Is.LessThanOrEqualTo(container.max.x), message);
        Assert.That(candidate.max.y, Is.LessThanOrEqualTo(container.max.y), message);
    }

    private static void RebuildLayout(RectTransform rect)
    {
        Type layoutRebuilderType = RequireType("UnityEngine.UI.LayoutRebuilder, UnityEngine.UI");
        MethodInfo forceRebuild = layoutRebuilderType.GetMethod("ForceRebuildLayoutImmediate",
            BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(RectTransform) }, null);
        Assert.That(forceRebuild, Is.Not.Null);
        forceRebuild.Invoke(null, new object[] { rect });
        Canvas.ForceUpdateCanvases();
    }

    private static Transform Find(Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Single(transform => transform.name == name);
    }

    private static string ReadLabel(Transform button)
    {
        return ReadProperty<string>(FindText(button), "text");
    }

    private static Component FindText(Transform button)
    {
        Type textType = RequireType("TMPro.TMP_Text, Unity.TextMeshPro");
        return button.GetComponentInChildren(textType, true);
    }

    private static T ReadProperty<T>(object target, string propertyName)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(property, Is.Not.Null, $"Missing property {propertyName} on {target.GetType().Name}.");
        return (T)property.GetValue(target);
    }

    private static Type RequireType(string assemblyQualifiedName)
    {
        Type type = Type.GetType(assemblyQualifiedName);
        Assert.That(type, Is.Not.Null, $"Could not resolve {assemblyQualifiedName}.");
        return type;
    }
}

