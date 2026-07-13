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
    private MountableBridgeComponentSO selectedMountableBridgeComponentSO;
    public static EventHandler OnAnyUIClosed;
    public EventHandler<OnConfirmButtonClickEventArgs> OnConfirmButtonClick;
    public class OnConfirmButtonClickEventArgs : EventArgs
    {
        public MountableBridgeComponentSO mountableBridgeComponentSO;
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
            if (selectedMountableBridgeComponentSO != null)
            {
                OnConfirmButtonClick?.Invoke(this, new OnConfirmButtonClickEventArgs { mountableBridgeComponentSO = selectedMountableBridgeComponentSO });
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
        foreach (MountableBridgeComponentSO mountableBridgeComponentSO in baseFactory.GetMountableBridgeComponentSOArray())
        {
            Transform buttonTransform = Instantiate(buttonTemplate, container);
            buttonTransform.gameObject.SetActive(true);
            buttonTransform.GetComponent<FactoryInteractionUISingleButton>().SetMountableBridgeComponentSO(mountableBridgeComponentSO);
            //factoryInteractionUISingleButtons.Add()
            buttonTransform.GetComponent<FactoryInteractionUISingleButton>().OnButtonClick += Button_OnClick;
        }
        Destroy(buttonTemplate.gameObject);
    }

    private void Button_OnClick(object sender, FactoryInteractionUISingleButton.OnButtonClickEventArgs e)
    {
        selectedMountableBridgeComponentSO = e.mountableBridgeComponentSO;
        currentlySelectedBridgeComponentNameText.text = selectedMountableBridgeComponentSO.bridgeComponentSO.componentName;
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
        MountableBridgeComponentSO componentToDisplay = selectedMountableBridgeComponentSO != null
            ? selectedMountableBridgeComponentSO
            : baseFactory.SelectedComponent;

        if (componentToDisplay != null && currentlySelectedBridgeComponentNameText != null)
        {
            currentlySelectedBridgeComponentNameText.text = componentToDisplay.bridgeComponentSO.componentName;
        }

        if (requiredResourcesPanelUI != null)
        {
            requiredResourcesPanelUI.SetRequiredResourcesInformations(componentToDisplay, baseFactory.Storage);
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
