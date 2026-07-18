using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHeldObjectUI : MonoBehaviour
{
    [SerializeField] private PlayerInteractionNew playerInteraction;
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private Image heldObjectIcon;
    [SerializeField] private TMP_Text heldObjectNameText;
    [SerializeField] private TMP_Text sharedCarryStatusText;
    [SerializeField] private Color supportedColor = new Color(0.78f, 0.84f, 0.88f, 1f);
    [SerializeField] private Color understaffedColor = new Color(0.95f, 0.24f, 0.18f, 1f);

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
        if (playerInteraction != null)
        {
            playerInteraction.OnHeldObjectChanged += HandleHeldObjectChanged;
        }

        Refresh();
    }

    private void OnDisable()
    {
        if (playerInteraction != null)
        {
            playerInteraction.OnHeldObjectChanged -= HandleHeldObjectChanged;
        }
    }

    private void HandleHeldObjectChanged(object sender, System.EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        EnsureReferences();
        GameObject heldObject = playerInteraction != null ? playerInteraction.GetPickedUpGameObject() : null;
        if (visualRoot != null)
        {
            visualRoot.SetActive(heldObject != null);
        }
        if (heldObject == null)
        {
            return;
        }

        IHeldObjectHudInfoProvider provider = FindInfoProvider(heldObject);
        string displayName = provider != null ? provider.HeldObjectDisplayName : CleanObjectName(heldObject.name);
        Sprite icon = provider != null ? provider.HeldObjectIcon : null;

        if (heldObjectNameText != null)
        {
            heldObjectNameText.text = displayName;
        }
        if (heldObjectIcon != null)
        {
            heldObjectIcon.sprite = icon;
            heldObjectIcon.enabled = icon != null;
        }

        int required = playerInteraction.RequiredSharedCarryPlayerCount;
        bool showCarryStatus = playerInteraction.IsSharedCarryMovementActive && required > 1;
        if (sharedCarryStatusText != null)
        {
            sharedCarryStatusText.gameObject.SetActive(showCarryStatus);
            if (showCarryStatus)
            {
                int current = playerInteraction.CurrentSharedCarryPlayerCount;
                sharedCarryStatusText.text = $"Carry {current} / {required}";
                sharedCarryStatusText.color = current < required ? understaffedColor : supportedColor;
            }
        }
    }

    private void EnsureReferences()
    {
        if (playerInteraction == null)
        {
            playerInteraction = GetComponentInParent<PlayerInteractionNew>();
        }
        if (playerInteraction == null && transform.root != null)
        {
            playerInteraction = transform.root.GetComponentInChildren<PlayerInteractionNew>(true);
        }
    }

    private static IHeldObjectHudInfoProvider FindInfoProvider(GameObject heldObject)
    {
        foreach (MonoBehaviour behaviour in heldObject.GetComponentsInParent<MonoBehaviour>(true))
        {
            if (behaviour is IHeldObjectHudInfoProvider provider)
            {
                return provider;
            }
        }
        return null;
    }

    private static string CleanObjectName(string objectName)
    {
        return objectName.Replace("(Clone)", string.Empty).Trim();
    }
}
