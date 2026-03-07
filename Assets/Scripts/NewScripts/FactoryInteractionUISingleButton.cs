using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


public class FactoryInteractionUISingleButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private MountableBridgeComponentSO mountableBridgeComponentSO;
    [SerializeField] private Image bridgeComponentImage;
    [SerializeField] private TextMeshProUGUI bridgeComponentNameText;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public EventHandler<OnButtonClickEventArgs> OnButtonClick;
    public class OnButtonClickEventArgs : EventArgs
    {
        public MountableBridgeComponentSO mountableBridgeComponentSO;
    }

    private void Awake()
    {

    }
    void Start()
    {
        button.onClick.AddListener(() =>
        {
            button.Select();
            OnButtonClick?.Invoke(this, new OnButtonClickEventArgs { mountableBridgeComponentSO = mountableBridgeComponentSO });
        });
        bridgeComponentImage.sprite = mountableBridgeComponentSO.componentSprite;
        bridgeComponentNameText.text = mountableBridgeComponentSO.bridgeComponentSO.componentName;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SetMountableBridgeComponentSO(MountableBridgeComponentSO mountableBridgeComponentSO)
    {
        this.mountableBridgeComponentSO = mountableBridgeComponentSO;
    }

    
}
