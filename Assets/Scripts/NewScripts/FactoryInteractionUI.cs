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
        CreateUIButtons();
        buttonTemplate.gameObject.SetActive(false);

        Hide();
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
        requiredResourcesPanelUI.SetRequiredResourcesInformations(e.mountableBridgeComponentSO);
        confirmButton.Select();
        //Hide();
        //HideCursor();
        //OnAnyUIClosed?.Invoke(this, EventArgs.Empty);
    }
}
