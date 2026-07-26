using UnityEngine;
using UnityEngine.UI;

public class PlayerCrosshairUI : MonoBehaviour
{
    [SerializeField] private Graphic crosshairGraphic;
    [SerializeField] private PlayerInputNew playerInput;
    [SerializeField] private PlayerHealth playerHealth;

    private void Awake()
    {
        playerInput ??= GetComponentInParent<PlayerInputNew>();
        playerHealth ??= GetComponentInParent<PlayerHealth>();
        RefreshVisibility();
    }

    private void Update()
    {
        RefreshVisibility();
    }

    public void RefreshVisibility()
    {
        if (crosshairGraphic == null)
        {
            return;
        }

        bool gameplayUiOpen = playerInput != null && playerInput.IsGameplayUiOpen;
        bool isDowned = playerHealth != null && playerHealth.IsDowned;
        crosshairGraphic.enabled = !gameplayUiOpen && !isDowned;
    }
}
