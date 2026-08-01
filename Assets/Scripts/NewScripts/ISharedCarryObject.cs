using UnityEngine;

public interface ISharedCarryObject
{
    void SubmitSharedCarryInput(Vector3 worldTranslationInput, Vector3 worldLateralInput, float directYawInput, float gripHeightInput);
    void RequestSharedCarryExhaustion();
}
