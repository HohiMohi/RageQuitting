using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


public class FactoryInteractionUISingleButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private ProductionRecipeSO productionRecipeSO;
    [SerializeField] private Image bridgeComponentImage;
    [SerializeField] private TextMeshProUGUI bridgeComponentNameText;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public EventHandler<OnButtonClickEventArgs> OnButtonClick;
    public class OnButtonClickEventArgs : EventArgs
    {
        public ProductionRecipeSO productionRecipeSO;
    }

    private void Awake()
    {

    }
    void Start()
    {
        button.onClick.AddListener(() =>
        {
            button.Select();
            OnButtonClick?.Invoke(this, new OnButtonClickEventArgs { productionRecipeSO = productionRecipeSO });
        });
        bridgeComponentImage.sprite = productionRecipeSO != null ? productionRecipeSO.RecipeIcon : null;
        bridgeComponentNameText.text = productionRecipeSO != null ? productionRecipeSO.RecipeName : "Missing recipe";
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SetProductionRecipeSO(ProductionRecipeSO productionRecipeSO)
    {
        this.productionRecipeSO = productionRecipeSO;
    }

    
}
