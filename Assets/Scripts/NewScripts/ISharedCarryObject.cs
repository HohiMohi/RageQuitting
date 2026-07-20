using UnityEngine;

public interface ISharedCarryObject
{
    void SubmitSharedCarryInput(Vector3 worldTranslationInput, float yawInput);
    void RequestSharedCarryExhaustion();
}
