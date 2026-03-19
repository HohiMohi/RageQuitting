using UnityEngine;

public interface IPIckableNew
{
    public void PickedUp(Transform parent);
    public void DroppedDown();
    public float GetMovementSpeedPenalty();
    public int GetMinAmountOfPlayersNeeded();
}
