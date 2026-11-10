using UnityEngine;

[DisallowMultipleComponent]
public sealed class TumbleweedCaptureSurface : MonoBehaviour
{
    [SerializeField] private SphereCollider captureCollider;
    [SerializeField] private TumbleweedMovement owner;

    public SphereCollider Collider => captureCollider;
    public TumbleweedMovement Owner => owner;
    public Vector3 WorldPosition => owner != null ? owner.transform.position : transform.position;
    public float WorldRadius => captureCollider != null
        ? captureCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z)
        : 0f;

    private void Awake()
    {
        if (captureCollider == null) TryGetComponent(out captureCollider);
        if (owner == null) owner = GetComponentInParent<TumbleweedMovement>();
    }
}
