using UnityEngine;

public class WheelbarrowAutoBoardingTrigger : MonoBehaviour
{
    [SerializeField] private WheelbarrowController wheelbarrow;
    private void OnTriggerEnter(Collider other) => wheelbarrow?.TryAutomaticBoarding(other);
}
