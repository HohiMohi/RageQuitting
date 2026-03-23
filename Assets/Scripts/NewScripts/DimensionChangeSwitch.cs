using System;
using UnityEngine;

public class DimensionChangeSwitch : MonoBehaviour, IInteractableNew
{
    [SerializeField] private ComponentDimension componentDimension;
    [SerializeField] private DimensionChangeType changeType;

    public EventHandler<DimensionChangeSwitchPressedEventArgs> DimensionChangeSwitchPressed;
    public class DimensionChangeSwitchPressedEventArgs : EventArgs
    {
        public ComponentDimension componentDimension;
        public DimensionChangeType dimensionChangeType;
    }

    public void Interact(Transform interactor)
    {
        DimensionChangeSwitchPressed?.Invoke(this, new DimensionChangeSwitchPressedEventArgs
        {
            componentDimension = componentDimension,
            dimensionChangeType = changeType
        });
    }
    public void LookedAt(Transform interactor)
    {
        Debug.Log("Looked at Dimension Change Switch");
    }

    public void LookedAway(Transform interactor)
    {
        Debug.Log("Looked away from Dimension Change Switch");

    }
}

public enum ComponentDimension
{
    Width,
    Length
}

public enum DimensionChangeType
{
    Increase,
    Decrease
}
