using UnityEngine;

public class MountableBridgeComponent : MonoBehaviour, IPIckableNew, IInteractableNew
{
    [SerializeField] private MountableBridgeComponentSO mountableBridgeComponentSO;
    private Rigidbody _rigidbody;
    private bool isPickedUp = false;

    public void Interact(Transform interactor)
    {
        PickedUp(interactor);
    }

    public void PickedUp(Transform parent)
    {
        parent.GetComponent<PlayerInteractionNew>().PickUpObject(this.gameObject, this);
        isPickedUp = true;
        UpdatePickedUpProperties();
    }

    public void DroppedDown()
    {
        isPickedUp = false;
        UpdatePickedUpProperties();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody == null)
        {
            _rigidbody = gameObject.AddComponent<Rigidbody>();
        }
    }

    private void UpdatePickedUpProperties()
    {
        if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody == null) return;

        if (isPickedUp)
        {
            _rigidbody.useGravity = false;
            _rigidbody.isKinematic = true;
        }
        else
        {
            _rigidbody.useGravity = true;
            _rigidbody.isKinematic = false;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public MountableBridgeComponentSO GetMountableBridgeComponentSO()
        {
            return mountableBridgeComponentSO;
    }

    public void LookedAt(Transform interactor)
    {
        Debug.Log("Looked at Mountable Bridge Component");
    }

    public void LookedAway(Transform interactor)
    {
        Debug.Log("Looked away from Mountable Bridge Component");
    }

    public float GetMovementSpeedPenalty()
    {
        return mountableBridgeComponentSO.movementSpeedPenalty;
    }

    public int GetMinAmountOfPlayersNeeded()
    {
        return mountableBridgeComponentSO.minAmountOfPlayersNeeded;
    }


}
