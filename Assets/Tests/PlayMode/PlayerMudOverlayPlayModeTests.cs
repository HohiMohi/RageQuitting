using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerMudOverlayPlayModeTests
{
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private GameObject playerRoot;
    private Component presenter;
    private ScriptableObject style;
    private readonly Type presenterType = Type.GetType("PlayerMudOverlay, Assembly-CSharp");
    private readonly Type styleType = Type.GetType("PlayerMudOverlayStyleSO, Assembly-CSharp");
    private Texture2D mudTexture;
    private Texture2D maskTexture;
    private Texture2D dripTexture;
    private Sprite mudSprite;
    private Sprite dripSprite;

    [TearDown]
    public void Cleanup()
    {
        if (playerRoot != null) UnityEngine.Object.DestroyImmediate(playerRoot);
        else if (root != null) UnityEngine.Object.DestroyImmediate(root);
        if (style != null) UnityEngine.Object.DestroyImmediate(style);
        if (mudSprite != null) UnityEngine.Object.DestroyImmediate(mudSprite);
        if (dripSprite != null) UnityEngine.Object.DestroyImmediate(dripSprite);
        if (mudTexture != null) UnityEngine.Object.DestroyImmediate(mudTexture);
        if (maskTexture != null) UnityEngine.Object.DestroyImmediate(maskTexture);
        if (dripTexture != null) UnityEngine.Object.DestroyImmediate(dripTexture);
    }

    [Test]
    public void DripsUseOneLocalImageAndSevenToTwelveSecondStartIntervals()
    {
        CreatePresenter();
        SetRandomSeed(149);
        SetState(0.8f, true);

        Tick(20f, 0f);
        float firstStart = NextDripStartTime;
        Assert.That(firstStart, Is.InRange(27f, 32f), "Initial wait is 7–12 game-time seconds.");
        Tick(firstStart - 0.01f, 0.01f);
        Assert.That(IsDripRunning, Is.False);
        Tick(firstStart, 0f);
        Assert.That(IsDripRunning, Is.True);
        float firstDropDuration = GetPrivateFloat("dripDuration");
        Assert.That(firstDropDuration, Is.InRange(4f, 6f));
        Assert.That(ActiveDripImages(), Is.EqualTo(1));

        Tick(firstStart + firstDropDuration, firstDropDuration);
        Assert.That(IsDripRunning, Is.False);
        float secondStart = NextDripStartTime;
        Assert.That(secondStart - firstStart, Is.InRange(7f, 12f), "The interval is measured between starts, not after the drop ends.");
        Tick(secondStart, 0f);
        Assert.That(IsDripRunning, Is.True);
        Assert.That(DripStartTime - firstStart, Is.EqualTo(secondStart - firstStart).Within(0.0001f));
        Assert.That(ActiveDripImages(), Is.EqualTo(1));
    }

    [Test]
    public void BelowThresholdFadesAndCancelsPendingDripWhileZeroAndDisableResetImmediately()
    {
        CreatePresenter();
        SetRandomSeed(77);
        SetState(0.5f, true);
        Tick(0f, 0f);
        float scheduled = NextDripStartTime;
        SetState(0.34f, true);
        Assert.That(NextDripStartTime, Is.EqualTo(-1f), "Dropping below 35% clears a pending start.");
        Tick(scheduled + 1f, 0f);
        Assert.That(IsDripRunning, Is.False);

        SetState(0.8f, true);
        Tick(30f, 0f);
        float start = NextDripStartTime;
        Tick(start, 0f);
        Assert.That(IsDripRunning, Is.True);
        SetState(0.2f, true);
        Tick(start + 0.05f, 0.05f);
        Assert.That(IsDripRunning, Is.True);
        SetState(0.2f, true);
        Tick(start + 0.1f, 0.05f);
        Assert.That(IsDripRunning, Is.True, "Repeated below-threshold status callbacks do not restart the 0.2 second fade.");
        SetState(0.2f, true);
        Tick(start + 0.15f, 0.05f);
        Assert.That(IsDripRunning, Is.True);
        Tick(start + 0.21f, 0.06f);
        Assert.That(IsDripRunning, Is.False, "An active drip fades promptly below the threshold.");

        SetState(0.7f, true);
        Tick(80f, 0f);
        Tick(NextDripStartTime, 0f);
        Assert.That(IsDripRunning, Is.True);
        SetState(0f, true);
        Assert.That(IsDripRunning, Is.False);
        Assert.That(ActiveDripImages(), Is.Zero);
        Assert.That(GetGroup().alpha, Is.Zero);
        Assert.That(DisplayedAmount, Is.Zero);

        SetState(0.7f, true);
        Tick(100f, 0f);
        Tick(NextDripStartTime, 0f);
        Assert.That(IsDripRunning, Is.True);
        ((Behaviour)presenter).enabled = false;
        Assert.That(IsDripRunning, Is.False);
        Assert.That(ActiveDripImages(), Is.Zero);
        Assert.That(GetGroup().alpha, Is.Zero);
    }

    [Test]
    public void StatusControllerDrivesPresenterLocallyAndLegacyOverlayRemainsSupported()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            Assert.Ignore("The offline fixture requires no active network session.");
        CreatePresenter();
        playerRoot = new GameObject("Mud local status controller fixture");
        playerRoot.SetActive(false);
        root.transform.SetParent(playerRoot.transform, false);
        playerRoot.AddComponent<NetworkObject>();
        Type statusesType = Type.GetType("PlayerTemporaryStatusController, Assembly-CSharp");
        Type mudType = Type.GetType("PlayerMudStatusController, Assembly-CSharp");
        playerRoot.AddComponent(statusesType);
        Component mud = playerRoot.AddComponent(mudType);
        mudType.GetField("mudOverlay", InstancePrivate).SetValue(mud, GetGroup());
        playerRoot.SetActive(true);

        MethodInfo setAmount = mudType.GetMethod("SetAmount", InstancePrivate);
        Assert.That((bool)setAmount.Invoke(mud, new object[] { 0.5f }), Is.True);
        Assert.That(GetGroup().gameObject.activeSelf, Is.True);
        Assert.That(GetGroup().alpha, Is.EqualTo(0.6f).Within(0.0001f), "Configured presentation keeps maximum alpha separate from mud amount.");
        Assert.That(DisplayedAmount, Is.Zero, "Shader reveal begins at the current displayed amount and advances during Update.");

        Assert.That((bool)setAmount.Invoke(mud, new object[] { 0f }), Is.True);
        Assert.That(GetGroup().alpha, Is.Zero);
        Assert.That(GetGroup().gameObject.activeSelf, Is.False);
        Assert.That(IsDripRunning, Is.False);

        // A separate legacy controller with no presenter retains its old linear CanvasGroup alpha.
        GameObject legacyPlayer = new GameObject("Mud legacy overlay test");
        legacyPlayer.AddComponent<NetworkObject>();
        legacyPlayer.AddComponent(statusesType);
        Component legacyMud = legacyPlayer.AddComponent(mudType);
        GameObject legacyOverlay = new GameObject("Legacy overlay", typeof(RectTransform), typeof(CanvasGroup));
        legacyOverlay.transform.SetParent(legacyPlayer.transform, false);
        CanvasGroup legacyGroup = legacyOverlay.GetComponent<CanvasGroup>();
        mudType.GetField("mudOverlay", InstancePrivate).SetValue(legacyMud, legacyGroup);
        mudType.GetMethod("RefreshMudOverlay", InstancePrivate).Invoke(legacyMud, null);
        setAmount = mudType.GetMethod("SetAmount", InstancePrivate);
        Assert.That((bool)setAmount.Invoke(legacyMud, new object[] { 0.5f }), Is.True);
        Assert.That(legacyGroup.alpha, Is.EqualTo(0.3f).Within(0.0001f));
        UnityEngine.Object.DestroyImmediate(legacyPlayer);
    }

    private void CreatePresenter()
    {
        root = new GameObject("Mud overlay lifecycle fixture", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasGroup group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        mudTexture = OpaqueTexture();
        maskTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        maskTexture.SetPixels(new[] { new Color(0.5f, 1f, 0f), new Color(0.5f, 1f, 0f),
            new Color(0.5f, 1f, 0f), new Color(0.5f, 1f, 0f) });
        maskTexture.Apply();
        dripTexture = OpaqueTexture();
        Assert.That(presenterType, Is.Not.Null);
        Assert.That(styleType, Is.Not.Null);
        style = ScriptableObject.CreateInstance(styleType);
        mudSprite = Sprite.Create(mudTexture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
        dripSprite = Sprite.Create(dripTexture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
        SetStyleField("mudTextureSprite", mudSprite);
        SetStyleField("thresholdMap", maskTexture);
        SetStyleField("dripSprite", dripSprite);

        Image mudImage = CreateImage("Mud Texture", root.transform);
        RectTransform mudRect = mudImage.rectTransform;
        mudRect.anchorMin = Vector2.zero;
        mudRect.anchorMax = Vector2.one;
        mudRect.offsetMin = mudRect.offsetMax = Vector2.zero;
        Image drip = CreateImage("Drip", root.transform);
        root.SetActive(false);
        presenter = root.AddComponent(presenterType);
        SetPresenterField("style", style);
        SetPresenterField("mudTextureImage", mudImage);
        SetPresenterField("dripImage", drip);
        SetPresenterField("overlayShader", Shader.Find("RageQuitting/UI/PlayerMudOverlay"));
        root.SetActive(true);
    }

    private static Texture2D OpaqueTexture()
    {
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        texture.Apply();
        return texture;
    }

    private static Image CreateImage(string name, Transform parent)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private void SetStyleField(string name, object value) =>
        styleType.GetField(name, InstancePrivate).SetValue(style, value);

    private void SetPresenterField(string name, object value) =>
        presenterType.GetField(name, InstancePrivate).SetValue(presenter, value);

    private CanvasGroup GetGroup() => root.GetComponent<CanvasGroup>();
    private void SetRandomSeed(int seed) => presenterType.GetField("random", InstancePrivate)
        .SetValue(presenter, new System.Random(seed));
    private void SetState(float amount, bool owner) => presenterType.GetMethod("SetMudState", InstancePrivate)
        .Invoke(presenter, new object[] { amount, owner, 0.6f });
    private void Tick(float time, float deltaTime) => presenterType.GetMethod("Tick", InstancePrivate)
        .Invoke(presenter, new object[] { time, deltaTime });
    private bool IsDripRunning => GetPrivate<bool>("dripRunning");
    private float NextDripStartTime => GetPrivate<bool>("hasScheduledDrip") ? GetPrivate<float>("nextDripStartTime") : -1f;
    private float DripStartTime => GetPrivate<float>("dripStartTime");
    private float DisplayedAmount => GetPrivate<float>("displayedAmount");
    private float GetPrivateFloat(string name) => GetPrivate<float>(name);
    private T GetPrivate<T>(string name) => (T)presenterType.GetField(name, InstancePrivate).GetValue(presenter);
    private int ActiveDripImages() => root.transform.Find("Drip").gameObject.activeSelf ? 1 : 0;
}
