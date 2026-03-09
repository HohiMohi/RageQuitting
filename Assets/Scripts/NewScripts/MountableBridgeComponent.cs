using UnityEngine;

public class MountableBridgeComponent : MonoBehaviour, IPIckableNew, IInteractableNew
{
    [SerializeField] private MountableBridgeComponentSO mountableBridgeComponentSO;

    public void Interact(Transform interactor)
    {
        PickedUp(interactor);
    }

    public void PickedUp(Transform parent)
    {
        parent.GetComponent<PlayerInteractionNew>().PickUpObject(this.gameObject, this);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
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
}
