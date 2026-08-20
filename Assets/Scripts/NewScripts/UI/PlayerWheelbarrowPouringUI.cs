using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class PlayerWheelbarrowPouringUI : MonoBehaviour
{
    private Canvas canvas;
    private RectTransform leftCursor;
    private RectTransform rightCursor;
    private Image status;
    private NetworkObject playerNetworkObject;

    private void Awake()
    {
        playerNetworkObject = GetComponent<NetworkObject>();
        BuildUi();
    }

    private void Update()
    {
        if (playerNetworkObject != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !playerNetworkObject.IsOwner)
        {
            canvas.enabled = false;
            return;
        }
        ulong clientId = playerNetworkObject != null ? playerNetworkObject.OwnerClientId : 0;
        WheelbarrowPouringMinigame minigame = WheelbarrowPouringMinigame.FindForPlayer(clientId);
        bool visible = minigame != null && minigame.State == WheelbarrowPouringState.Active;
        canvas.enabled = visible;
        if (!visible) return;
        SetCursor(leftCursor, minigame.LeftCursor);
        SetCursor(rightCursor, minigame.RightCursor);
        float difference = Mathf.Abs(minigame.LeftCursor - minigame.RightCursor);
        ConcretePouringProfileSO settings = minigame.Profile;
        status.color = difference <= (settings != null ? settings.SynchronizedTolerance : 0.15f)
            ? new Color(0.25f, 0.9f, 0.35f, 0.9f)
            : difference > (settings != null ? settings.CriticalDifference : 0.35f)
                ? new Color(0.95f, 0.2f, 0.15f, 0.9f)
                : new Color(1f, 0.75f, 0.15f, 0.9f);
    }

    private void BuildUi()
    {
        GameObject canvasObject = new GameObject("WheelbarrowPouringUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 35;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        RectTransform panel = CreateImage(canvasObject.transform, "Panel", new Color(0.04f, 0.05f, 0.05f, 0.82f));
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(210f, 390f);
        panel.anchoredPosition = new Vector2(0f, -30f);
        CreateTrack(panel, -45f, out leftCursor);
        CreateTrack(panel, 45f, out rightCursor);
        status = CreateImage(panel, "SyncStatus", Color.green).GetComponent<Image>();
        status.rectTransform.anchorMin = status.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        status.rectTransform.sizeDelta = new Vector2(150f, 10f);
        status.rectTransform.anchoredPosition = new Vector2(0f, 20f);
        canvas.enabled = false;
    }

    private void CreateTrack(RectTransform parent, float x, out RectTransform cursor)
    {
        RectTransform track = CreateImage(parent, "Track", new Color(0.35f, 0.38f, 0.4f, 0.9f));
        track.anchorMin = track.anchorMax = new Vector2(0.5f, 0.5f);
        track.sizeDelta = new Vector2(18f, 290f);
        track.anchoredPosition = new Vector2(x, 15f);
        cursor = CreateImage(track, "Cursor", new Color(0.3f, 0.9f, 0.45f, 1f));
        cursor.anchorMin = cursor.anchorMax = new Vector2(0.5f, 0f);
        cursor.sizeDelta = new Vector2(34f, 16f);
    }

    private static RectTransform CreateImage(Transform parent, string name, Color color)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(Image));
        item.transform.SetParent(parent, false);
        Image image = item.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return item.GetComponent<RectTransform>();
    }

    private static void SetCursor(RectTransform cursor, float value)
    {
        if (cursor != null) cursor.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, 290f, Mathf.Clamp01(value)));
    }
}
