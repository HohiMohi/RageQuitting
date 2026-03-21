using StarterAssets;
using UnityEngine;
using UnityEngine.UI;

public class PlayerStaminaUI : MonoBehaviour
{
    [SerializeField] private Image staminaMeterHolder;
    [SerializeField] private FirstPersonController firstPersonController;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Show();
    }

    // Update is called once per frame
    void Update()
    {
        

    }

    private void FixedUpdate()
    {
        UpdateVisual();
    }
    private void Show()
    {
        gameObject.SetActive(true);
    }
    private void Hide()
    {
        gameObject.SetActive(false);
    }

    private void UpdateVisual()
    {
        staminaMeterHolder.fillAmount = firstPersonController.GetStaminaNormalized();
    }
}
