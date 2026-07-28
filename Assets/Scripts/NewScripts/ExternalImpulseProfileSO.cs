using UnityEngine;

[CreateAssetMenu(fileName = "ExternalImpulseProfile", menuName = "Scriptable Objects/External Impulse Profile")]
public class ExternalImpulseProfileSO : ScriptableObject
{
    [Header("Initial velocity")]
    [SerializeField] private float horizontalSpeed = 10f;
    [SerializeField] private float verticalSpeed = 7f;

    [Header("Motion")]
    [SerializeField] private float horizontalDeceleration = 12.5f;
    [SerializeField] private float gravityMultiplier = 1f;
    [SerializeField] private float maximumDuration = 2.5f;
    [SerializeField, Range(0f, 1f)] private float movementControlMultiplier = 0.35f;

    [Header("Stacking limits")]
    [SerializeField] private float maximumHorizontalSpeed = 16f;
    [SerializeField] private float maximumVerticalSpeed = 10f;

    [Header("Consequences")]
    [SerializeField] private bool forceDropHeldObject = true;

    public ExternalImpulseData CreateImpulse(Vector3 horizontalDirection)
    {
        horizontalDirection.y = 0f;
        if (horizontalDirection.sqrMagnitude > 0.0001f)
        {
            horizontalDirection.Normalize();
        }

        return new ExternalImpulseData
        {
            InitialVelocity = horizontalDirection * Mathf.Max(0f, horizontalSpeed)
                + Vector3.up * Mathf.Max(0f, verticalSpeed),
            HorizontalDeceleration = Mathf.Max(0f, horizontalDeceleration),
            GravityMultiplier = Mathf.Max(0f, gravityMultiplier),
            MaximumDuration = Mathf.Max(0.05f, maximumDuration),
            MovementControlMultiplier = Mathf.Clamp01(movementControlMultiplier),
            MaximumHorizontalSpeed = Mathf.Max(0f, maximumHorizontalSpeed),
            MaximumVerticalSpeed = Mathf.Max(0f, maximumVerticalSpeed),
            ForceDropHeldObject = forceDropHeldObject
        };
    }
}
