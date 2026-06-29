using UnityEngine;
using TMPro;

public class LookingAtComponentUI : MonoBehaviour
{
    [SerializeField] private TMP_Text componentInfoText;
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private PlayerInteractionNew playerInteraction;
    [SerializeField] private UnityEngine.UI.Image assemblingProgressBar;
    [SerializeField] private GameObject progressCircleHolder;

    private void Start()
    {
        if (playerInteraction == null)
        {
            playerInteraction = Object.FindFirstObjectByType<PlayerInteractionNew>();
        }
        Hide();
    }

    private void Update()
    {
        if (playerInteraction == null) return;

        IInteractableNew currentInteractable = playerInteraction.GetCurrentInteractable();
        if (currentInteractable is BridgeComponent bridgeComponent)
        {
            bool isReadyToMount = bridgeComponent.CanBeMounted && !bridgeComponent.IsMounted;
            bool isReadyToAssemble = bridgeComponent.IsMounted && !bridgeComponent.IsAssembled && bridgeComponent.NeedAssembling;

            if (isReadyToMount || isReadyToAssemble)
            {
                BridgeComponentSO so = bridgeComponent.GetBridgeComponentSO();
                if (so != null)
                {
                    string actionName = isReadyToMount ? "Mount" : "Assemble";
                    componentInfoText.text = $"{actionName} {so.componentName}";
                    
                    if (isReadyToAssemble)
                    {
                        if (progressCircleHolder != null) progressCircleHolder.SetActive(true);
                        if (assemblingProgressBar != null)
                        {
                            assemblingProgressBar.fillAmount = bridgeComponent.GetAssemblingProgressNormalized();
                        }
                    }
                    else
                    {
                        if (progressCircleHolder != null) progressCircleHolder.SetActive(false);
                    }

                    Show();
                    return;
                }
            }
        }

        Hide();
    }

    private void Show()
    {
        if (visualRoot != null)
        {
            visualRoot.SetActive(true);
        }
    }

    private void Hide()
    {
        if (visualRoot != null)
        {
            visualRoot.SetActive(false);
        }
    }
}

