using System;
using UnityEngine;

public class EquippableItemUI : MonoBehaviour
{
    [SerializeField] private EquippableItemInfoUI equippableItemInfoUI;
    [SerializeField] private EquippableItem equippableItem;
    public EventHandler<ShowUIEventArgs> EquippableItemUI_ShowUI;
    public class ShowUIEventArgs : EventArgs
    {
        public EquippableItemSO equippableItemSO;
    }
    public EventHandler EquippableItemUI_HideUI;


    private void Awake()
    {
    }
    private void Start()
    {
        equippableItem.OnLookAt += EquippableItem_OnLookAt;
        equippableItem.OnLookAway += EquippableItem_OnLookAway;
    }

    private void EquippableItem_OnLookAway(object sender, EventArgs e)
    {
        EquippableItemUI_HideUI?.Invoke(this, e);
    }

    private void EquippableItem_OnLookAt(object sender, EquippableItem.OnLookAtEventArgs e)
    {
        EquippableItemUI_ShowUI?.Invoke(this, new ShowUIEventArgs
        {
            equippableItemSO = e.equippableItemSO
        });
    }
}
