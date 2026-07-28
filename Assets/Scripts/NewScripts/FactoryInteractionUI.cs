using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FactoryInteractionUI : MonoBehaviour
{
    [SerializeField] private BaseFactory baseFactory;
    [SerializeField] private Button firstSelectedButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private SpriteRenderer selectedMountableBridgeComponentSprite;
    [SerializeField] private Transform container;
    [SerializeField] private Transform buttonTemplate;
    [SerializeField] private UnityEngine.UIElements.ScrollView scrollView;
    [SerializeField] private RequiredResourcesPanelUI requiredResourcesPanelUI;
    [SerializeField] private FactoryStorageResourcesPanelUI storageResourcesPanelUI;
    [SerializeField] private FactorySelectedComponentStatusUI selectedComponentStatusUI;
    [SerializeField] private FurnaceFuelPanelUI furnaceFuelPanelUI;
    [SerializeField] private Button confirmButton;
    [SerializeField] private TextMeshProUGUI currentlySelectedBridgeComponentNameText;
    private ProductionRecipeSO selectedProductionRecipeSO;
    public static EventHandler OnAnyUIClosed;
    public EventHandler<OnConfirmButtonClickEventArgs> OnConfirmButtonClick;
    public class OnConfirmButtonClickEventArgs : EventArgs
    {
        public ProductionRecipeSO productionRecipeSO;
    }

    private void Awake()
    {
        closeButton.onClick.AddListener(() =>
        {
            Hide();
            HideCursor();
            OnAnyUIClosed?.Invoke(this, EventArgs.Empty);
        });

        confirmButton.onClick.AddListener(() =>
        {
            Hide();
            HideCursor();
            OnAnyUIClosed?.Invoke(this, EventArgs.Empty);
            if (selectedProductionRecipeSO != null)
            {
                OnConfirmButtonClick?.Invoke(this, new OnConfirmButtonClickEventArgs { productionRecipeSO = selectedProductionRecipeSO });
            }
        });
    }

    private void Start()
    {
        baseFactory.OnInteract += baseFactory_OnInteract;
        baseFactory.OnFactoryStateChanged += BaseFactory_OnFactoryStateChanged;
        if (baseFactory.Storage != null)
        {
            baseFactory.Storage.BaseResourceAmountChanged += Storage_BaseResourceAmountChanged;
            FurnaceStorage furnaceStorage = GetFurnaceStorage();
            if (furnaceStorage != null)
            {
                furnaceStorage.FurnaceStateChanged += FurnaceStorage_FurnaceStateChanged;
            }
        }

        EnsureOptionalPanels();
        CreateUIButtons();
        buttonTemplate.gameObject.SetActive(false);
        RefreshFactoryInformations();

        Hide();
    }

    private void OnDestroy()
    {
        if (baseFactory != null)
        {
            baseFactory.OnInteract -= baseFactory_OnInteract;
            baseFactory.OnFactoryStateChanged -= BaseFactory_OnFactoryStateChanged;
            if (baseFactory.Storage != null)
            {
                baseFactory.Storage.BaseResourceAmountChanged -= Storage_BaseResourceAmountChanged;
                FurnaceStorage furnaceStorage = GetFurnaceStorage();
                if (furnaceStorage != null)
                {
                    furnaceStorage.FurnaceStateChanged -= FurnaceStorage_FurnaceStateChanged;
                }
            }
        }
    }

    private void baseFactory_OnInteract(object sender, EventArgs e)
    {
        Debug.Log("baseFactory_OnInteract");
        Show();
        ShowCursor();

    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void Show()
    {
        gameObject.SetActive(true);
        RefreshFactoryInformations();
        firstSelectedButton.Select();
    }
    public void ShowCursor()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.Confined;
    }

    public void HideCursor()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    public void CreateUIButtons()
    {
        foreach (ProductionRecipeSO productionRecipeSO in baseFactory.GetProductionRecipeSOArray())
        {
            Transform buttonTransform = Instantiate(buttonTemplate, container);
            buttonTransform.gameObject.SetActive(true);
            buttonTransform.GetComponent<FactoryInteractionUISingleButton>().SetProductionRecipeSO(productionRecipeSO);
            buttonTransform.GetComponent<FactoryInteractionUISingleButton>().OnButtonClick += Button_OnClick;
        }
        Destroy(buttonTemplate.gameObject);
    }

    private void Button_OnClick(object sender, FactoryInteractionUISingleButton.OnButtonClickEventArgs e)
    {
        selectedProductionRecipeSO = e.productionRecipeSO;
        currentlySelectedBridgeComponentNameText.text = selectedProductionRecipeSO.RecipeName;
        RefreshFactoryInformations();
        confirmButton.Select();
        //Hide();
        //HideCursor();
        //OnAnyUIClosed?.Invoke(this, EventArgs.Empty);
    }

    private void BaseFactory_OnFactoryStateChanged(object sender, EventArgs e)
    {
        RefreshFactoryInformations();
    }

    private void Storage_BaseResourceAmountChanged(object sender, BaseStorageNew.BaseResourceAmountChangedEventArgs e)
    {
        RefreshFactoryInformations();
    }

    private void FurnaceStorage_FurnaceStateChanged(object sender, EventArgs e)
    {
        RefreshFactoryInformations();
    }

    private void RefreshFactoryInformations()
    {
        ProductionRecipeSO recipeToDisplay = selectedProductionRecipeSO != null
            ? selectedProductionRecipeSO
            : baseFactory.SelectedRecipe;

        if (recipeToDisplay != null && currentlySelectedBridgeComponentNameText != null)
        {
            currentlySelectedBridgeComponentNameText.text = recipeToDisplay.RecipeName;
        }

        if (requiredResourcesPanelUI != null)
        {
            requiredResourcesPanelUI.SetRequiredResourcesInformations(recipeToDisplay, baseFactory.Storage);
        }

        if (storageResourcesPanelUI != null)
        {
            storageResourcesPanelUI.Refresh(baseFactory.Storage);
        }

        if (selectedComponentStatusUI != null)
        {
            selectedComponentStatusUI.Refresh(baseFactory);
        }

        if (furnaceFuelPanelUI != null)
        {
            furnaceFuelPanelUI.Refresh(GetFurnaceStorage());
        }
    }

    private void EnsureOptionalPanels()
    {
        if (storageResourcesPanelUI == null)
        {
            storageResourcesPanelUI = GetComponentInChildren<FactoryStorageResourcesPanelUI>(true);
        }

        if (storageResourcesPanelUI == null)
        {
            storageResourcesPanelUI = FactoryStorageResourcesPanelUI.CreateRuntimePanel(transform);
        }

        if (selectedComponentStatusUI == null)
        {
            selectedComponentStatusUI = GetComponentInChildren<FactorySelectedComponentStatusUI>(true);
        }

        if (selectedComponentStatusUI == null)
        {
            selectedComponentStatusUI = FactorySelectedComponentStatusUI.CreateRuntimePanel(transform);
        }

        if (GetFurnaceStorage() == null)
        {
            return;
        }

        if (furnaceFuelPanelUI == null)
        {
            furnaceFuelPanelUI = GetComponentInChildren<FurnaceFuelPanelUI>(true);
        }

        if (furnaceFuelPanelUI == null)
        {
            furnaceFuelPanelUI = FurnaceFuelPanelUI.CreateRuntimePanel(transform);
        }
    }

    private FurnaceStorage GetFurnaceStorage()
    {
        if (baseFactory is BlastFurnaceFactory blastFurnaceFactory)
        {
            return blastFurnaceFactory.FurnaceStorage;
        }

        return baseFactory.Storage as FurnaceStorage;
    }
}
